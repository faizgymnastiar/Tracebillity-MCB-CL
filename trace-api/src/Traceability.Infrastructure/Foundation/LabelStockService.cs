using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Infrastructure.Foundation;

/// <summary>
/// Stok label QR internal NG (Fase 2). ID dialokasikan dari sequence DB yang tidak
/// pernah diulang, batch + label tersimpan atomik, dan permintaan ulang dengan kunci
/// idempotensi yang sama mengembalikan batch lama tanpa menambah digit.
/// </summary>
public sealed class LabelStockService : ILabelStockService
{
    /// <summary>Kolom CSV yang tidak berubah — patokan template NiceLabel.</summary>
    public static readonly string[] CsvColumns =
    {
        "LABEL_ID", "PAYLOAD", "BATCH_ID", "SEQ", "NAMESPACE", "LABEL_STATUS",
        "BATCH_QTY", "GENERATED_AT_UTC", "GENERATED_BY",
        "PRINT_JOB_ID", "PRINT_KIND", "PRINT_CHANNEL",
    };

    private const int MaxIdempotencyKeyLength = 120;
    private const int MaxNoteLength = 200;

    private readonly LabelStore _store;
    private readonly TraceDbOptions _options;

    public LabelStockService(LabelStore store, TraceDbOptions options)
    {
        _store = store;
        _options = options;
    }

    // -----------------------------------------------------------------------
    // Baca
    // -----------------------------------------------------------------------

    public LabelStockSummaryDto GetSummary()
    {
        var (total, available, bound) = _store.StockTotals();
        var (jobs, pending) = _store.JobTotals();
        return new LabelStockSummaryDto
        {
            TotalLabels = total,
            AvailableStock = available,
            BoundStock = bound,
            TotalBatches = _store.CountBatches(),
            TotalPrintJobs = jobs,
            PendingConfirmationJobs = pending,
            Sequence = GetSequenceState(),
        };
    }

    public LabelSequenceStateDto GetSequenceState()
    {
        var (next, maxLabelSeq) = _store.ReadSequence();
        var ledgerHigh = _store.Ledger.TryReadHighWater(out var ledgerError);
        var ledgerMax = ledgerHigh ?? 0;
        var highWater = Math.Max(Math.Max(next - 1, maxLabelSeq), ledgerMax);

        return new LabelSequenceStateDto
        {
            Name = NgLabelRules.Namespace,
            NextValue = next,
            MaxLabelSeq = maxLabelSeq,
            LedgerMaxSeq = ledgerMax,
            HighWaterSeq = highWater,
            // Konsisten bila tidak ada digit di luar DB yang belum tercakup next_value.
            Consistent = ledgerMax <= next - 1 && maxLabelSeq <= next - 1,
            LedgerPath = _store.Ledger.Path,
            LedgerError = ledgerError,
        };
    }

    public PagedResult<LabelBatchDto> ListBatches(LabelBatchQuery query)
    {
        ApplyDayRange(query.From, query.To, out var fromUtc, out var toUtc);
        var page = ClampPage(query.Page);
        var pageSize = ClampSize(query.PageSize, 25, 200);
        var (items, total) = _store.QueryBatches(query.Search, fromUtc, toUtc, page, pageSize);
        return Page(items.Select(ToBatchDto).ToList(), total, page, pageSize);
    }

    public LabelBatchDetailDto GetBatch(string batchId, int labelLimit)
    {
        var batch = _store.FindBatch(batchId) ?? throw FoundationErrors.NotFound($"Batch {batchId} tidak ditemukan.");
        return new LabelBatchDetailDto
        {
            Batch = ToBatchDto(batch),
            Labels = _store.LabelsOfBatch(batch.Id, labelLimit).Select(ToLabelDto).ToList(),
            PrintJobs = _store.QueryJobs(batch.Id, null, null, null, 1, 100).Items.Select(ToJobDto).ToList(),
        };
    }

    public PagedResult<NgLabelDto> ListLabels(string? batchId, string? status, int page, int pageSize)
    {
        if (!string.IsNullOrWhiteSpace(status) && !LabelStatus.IsKnown(status))
        {
            throw FoundationErrors.BadRequest("LABEL_STATUS", $"Status label \"{status}\" tidak dikenal.");
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
        var pageNumber = ClampPage(page);
        var size = ClampSize(pageSize, 50, 500);
        var (items, total) = _store.QueryLabels(
            string.IsNullOrWhiteSpace(batchId) ? null : batchId.Trim(), normalizedStatus, pageNumber, size);
        return Page(items.Select(ToLabelDto).ToList(), total, pageNumber, size);
    }

    public PagedResult<PrintJobDto> ListPrintJobs(PrintJobQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Status) && !PrintJobStatus.IsKnown(query.Status))
        {
            throw FoundationErrors.BadRequest("PRINT_STATUS", $"Status job \"{query.Status}\" tidak dikenal.");
        }

        ApplyDayRange(query.From, query.To, out var fromUtc, out var toUtc);
        var pageNumber = ClampPage(query.Page);
        var size = ClampSize(query.PageSize, 25, 200);
        var normalizedStatus = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim().ToUpperInvariant();
        var (items, total) = _store.QueryJobs(
            string.IsNullOrWhiteSpace(query.BatchId) ? null : query.BatchId.Trim(),
            normalizedStatus, fromUtc, toUtc, pageNumber, size);
        return Page(items.Select(ToJobDto).ToList(), total, pageNumber, size);
    }

    public PrintJobDto GetPrintJob(string jobId)
    {
        var job = _store.FindJob(jobId) ?? throw FoundationErrors.NotFound($"Job cetak {jobId} tidak ditemukan.");
        return ToJobDto(job);
    }

    public NgLabelDto ResolveScan(string payloadOrId)
    {
        var text = (payloadOrId ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw FoundationErrors.BadRequest("LABEL_SCAN", "Payload hasil scan kosong.");
        }

        var labelId = NgLabelRules.NormalizeLabelId(text);
        if (labelId is null)
        {
            throw FoundationErrors.BadRequest("QR_NOT_INTERNAL",
                QrClassifier.MismatchMessage(QrPayloadKind.InternalLabel, QrClassifier.Classify(text)) +
                $" Label internal memakai awalan {NgLabelRules.PayloadPrefix}"
                + $" (contoh {NgLabelRules.BuildPayload(1)}).");
        }

        var record = _store.FindLabel(labelId)
            ?? throw FoundationErrors.NotFound($"Label {labelId} tidak dikenal — ID belum pernah diterbitkan.");
        return ToLabelDto(record);
    }

    // -----------------------------------------------------------------------
    // Generate batch (idempoten + atomik)
    // -----------------------------------------------------------------------

    public GenerateBatchResultDto Generate(CallerPrincipal caller, GenerateBatchInput input)
    {
        var quantity = input.Quantity;
        if (quantity <= 0)
        {
            throw FoundationErrors.BadRequest("LABEL_QTY", "Jumlah label harus lebih dari 0.");
        }

        var maxPerRequest = Math.Max(1, _options.LabelMaxPerRequest);
        if (quantity > maxPerRequest)
        {
            throw FoundationErrors.BadRequest("LABEL_QTY_LIMIT",
                $"Satu permintaan generate maksimal {maxPerRequest} label (batas terukur per permintaan, " +
                $"bukan batas jumlah label sepanjang umur sistem). Nilai saat ini: {quantity}.");
        }

        var idempotencyKey = (input.IdempotencyKey ?? string.Empty).Trim();
        if (idempotencyKey.Length == 0)
        {
            throw FoundationErrors.BadRequest("LABEL_IDEMPOTENCY_KEY",
                "Kunci idempotensi wajib diisi agar permintaan ulang tidak membuat batch tambahan.");
        }

        if (idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            throw FoundationErrors.BadRequest("LABEL_IDEMPOTENCY_KEY",
                $"Kunci idempotensi maksimal {MaxIdempotencyKeyLength} karakter.");
        }

        var note = TrimNote(input.Note);
        var requestHash = BuildRequestHash(quantity, note);

        // Penyangga restore: selama catatan penerbitan belum pasti mengenal batas tertinggi
        // yang pernah keluar, tidak ada ID baru yang boleh diterbitkan.
        AssertSequenceSafe();

        LabelAllocationResult allocation;
        try
        {
            allocation = _store.Allocate(quantity, note, idempotencyKey, requestHash,
                caller.AccountId, DisplayActor(caller));
        }
        catch (SequenceReuseDetectedException exception)
        {
            // Digit pernah tercatat: naikkan lantai supaya permintaan berikutnya tidak bertabrakan.
            _store.RaiseNextValue(exception.LastSeq, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            throw FoundationErrors.Conflict("SEQUENCE_REUSE",
                $"Sequence {exception.FirstSeq}..{exception.LastSeq} ternyata sudah pernah diterbitkan. " +
                "Batas tertinggi telah dinaikkan otomatis; ulangi permintaan generate.");
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            throw FoundationErrors.Conflict("DB_BUSY",
                "Database sedang dipakai transaksi lain. Silakan ulangi permintaan generate.");
        }
        catch (IOException exception)
        {
            throw FoundationErrors.Conflict("LEDGER_WRITE_FAILED",
                $"Catatan penerbitan tidak dapat ditulis ({exception.Message}); batch dibatalkan seluruhnya.");
        }

        // Key sama + isi berbeda = permintaan berbeda; jangan kembalikan batch lama.
        if (allocation.Reused && !string.Equals(allocation.Batch.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw FoundationErrors.Conflict("IDEMPOTENCY_CONFLICT",
                $"Kunci idempotensi \"{idempotencyKey}\" sudah dipakai untuk permintaan berbeda " +
                $"(batch {allocation.Batch.Id}, {allocation.Batch.Quantity} label). Gunakan kunci baru.");
        }

        return new GenerateBatchResultDto
        {
            Batch = ToBatchDto(allocation.Batch),
            Labels = _store.LabelsOfBatch(allocation.Batch.Id, 0).Select(ToLabelDto).ToList(),
            Reused = allocation.Reused,
            Sequence = GetSequenceState(),
            MaxPerRequest = maxPerRequest,
        };
    }

    // -----------------------------------------------------------------------
    // Job cetak & konfirmasi hasil fisik
    // -----------------------------------------------------------------------

    public PrintJobResultDto CreatePrintJob(CallerPrincipal caller, CreatePrintJobInput input)
    {
        var batchId = (input.BatchId ?? string.Empty).Trim();
        if (batchId.Length == 0)
        {
            throw FoundationErrors.BadRequest("LABEL_BATCH_REQUIRED", "Batch label wajib dipilih untuk membuat job cetak.");
        }

        var batch = _store.FindBatch(batchId) ?? throw FoundationErrors.NotFound($"Batch {batchId} tidak ditemukan.");

        var requestedIds = (input.LabelIds ?? new List<string>())
            .Select(id => (id ?? string.Empty).Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var targets = requestedIds.Count == 0
            ? _store.LabelsOfBatch(batch.Id, 0)
            : _store.LabelsByIds(requestedIds);

        if (requestedIds.Count > 0 && targets.Count != requestedIds.Count)
        {
            var missing = requestedIds
                .Where(id => !targets.Any(label => string.Equals(label.Id, id, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            throw FoundationErrors.BadRequest("LABEL_UNKNOWN",
                $"Label tidak dikenal: {string.Join(", ", missing.Take(10))}.");
        }

        if (targets.Any(label => !string.Equals(label.BatchId, batch.Id, StringComparison.Ordinal)))
        {
            throw FoundationErrors.BadRequest("LABEL_SCOPE",
                "Semua label dalam satu job cetak harus berasal dari batch yang sama.");
        }

        if (targets.Count == 0)
        {
            throw FoundationErrors.BadRequest("LABEL_EMPTY_BATCH", $"Batch {batch.Id} tidak punya label untuk dicetak.");
        }

        if (targets.Count > Math.Max(1, _options.LabelMaxPerRequest))
        {
            throw FoundationErrors.BadRequest("PRINT_QTY_LIMIT",
                $"Satu job cetak maksimal {Math.Max(1, _options.LabelMaxPerRequest)} label.");
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(input.IdempotencyKey)
            ? null
            : TrimKey(input.IdempotencyKey!);
        if (idempotencyKey is not null)
        {
            var existing = _store.FindJobByKey(idempotencyKey);
            if (existing is not null)
            {
                return new PrintJobResultDto
                {
                    Job = ToJobDto(existing),
                    Reused = true,
                    ExportCsv = null,
                    ExportFilename = existing.ExportFilename,
                };
            }
        }

        var now = DateTimeOffset.UtcNow;
        var labelIds = targets.Select(label => label.Id).ToList();
        var kind = _store.HasPrintedBefore(batch.Id, labelIds) ? PrintJobKind.Reprint : PrintJobKind.First;
        var job = new PrintJobRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            BatchId = batch.Id,
            Kind = kind,
            Channel = PrintChannel.Normalize(input.Channel),
            Status = PrintJobStatus.Exported,
            Quantity = targets.Count,
            RequestedByAccountId = caller.AccountId,
            RequestedByName = DisplayActor(caller),
            RequestedAtUtc = now.ToString("O", CultureInfo.InvariantCulture),
            ExportFilename = PrintChannel.Normalize(input.Channel) == PrintChannel.DirectPrinter
                ? null : BuildExportFilename(batch.Id, kind, now),
            Note = TrimNote(input.Note),
            IdempotencyKey = idempotencyKey,
            LabelIds = labelIds,
        };

        var (saved, reused) = _store.InsertJob(job);
        if (saved.Channel == PrintChannel.DirectPrinter)
            return new PrintJobResultDto { Job = ToJobDto(saved), Reused = reused };
        var export = BuildCsv(_store.JobLabels(saved.Id), _store.FindBatch(saved.BatchId) ?? batch, saved);

        return new PrintJobResultDto
        {
            Job = ToJobDto(_store.FindJob(saved.Id) ?? saved),
            Reused = reused,
            ExportCsv = export.Content,
            ExportFilename = export.Filename,
        };
    }

    public PrintJobDto ConfirmPrintJob(CallerPrincipal caller, string jobId, ConfirmPrintJobInput input)
    {
        var job = _store.FindJob(jobId) ?? throw FoundationErrors.NotFound($"Job cetak {jobId} tidak ditemukan.");
        if (job.Status == PrintJobStatus.Confirmed)
        {
            throw FoundationErrors.Conflict("PRINT_ALREADY_CONFIRMED",
                $"Job {job.Id} sudah dikonfirmasi pada {job.ConfirmedAtUtc}. Buat job cetak ulang bila perlu.");
        }

        var status = string.IsNullOrWhiteSpace(input.Status) ? PrintJobStatus.Confirmed : input.Status.Trim().ToUpperInvariant();
        if (!PrintJobStatus.IsKnown(status) || status == PrintJobStatus.Exported)
        {
            throw FoundationErrors.BadRequest("PRINT_STATUS",
                $"Status konfirmasi \"{input.Status}\" tidak valid; gunakan CONFIRMED, UNKNOWN, atau FAILED.");
        }

        var knownLabelIds = _store.JobLabelIds(job.Id);
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in input.Results ?? new List<PrintJobLabelResultInput>())
        {
            var labelId = (entry.LabelId ?? string.Empty).Trim();
            if (!knownLabelIds.Contains(labelId, StringComparer.Ordinal))
            {
                throw FoundationErrors.BadRequest("LABEL_SCOPE",
                    $"Label {labelId} bukan bagian dari job {job.Id}; hanya subset label job yang dapat dikonfirmasi.");
            }

            var result = PrintLabelResult.Normalize(entry.Result);
            if (result == PrintLabelResult.Pending)
            {
                throw FoundationErrors.BadRequest("PRINT_RESULT",
                    $"Hasil label {labelId} harus PRINTED atau MISSING.");
            }

            results[labelId] = result;
        }

        if (status != PrintJobStatus.Confirmed && results.Count > 0)
        {
            throw FoundationErrors.BadRequest("PRINT_RESULT",
                $"Hasil per label hanya diterima bila status CONFIRMED (status kini {status}).");
        }

        if (status == PrintJobStatus.Confirmed && results.Count == 0)
        {
            results = knownLabelIds.ToDictionary(id => id, _ => PrintLabelResult.Printed, StringComparer.Ordinal);
        }

        int? confirmedCount;
        if (status == PrintJobStatus.Confirmed)
        {
            var printed = results.Values.Count(value => value == PrintLabelResult.Printed);
            if (input.PrintedCount is { } provided && provided != printed)
            {
                throw FoundationErrors.BadRequest("PRINT_COUNT",
                    $"Jumlah konfirmasi {provided} tidak sama dengan label berstatus PRINTED ({printed}).");
            }

            confirmedCount = printed;
        }
        else
        {
            if (input.PrintedCount is { } pending && pending != 0)
            {
                throw FoundationErrors.BadRequest("PRINT_COUNT",
                    $"Status {status} tidak menerima jumlah tercetak; gunakan CONFIRMED untuk hasil sah.");
            }

            confirmedCount = null;
        }

        var note = TrimNote(string.IsNullOrWhiteSpace(input.Note)
            ? $"Dikonfirmasi oleh {DisplayActor(caller)}"
            : input.Note);

        return ToJobDto(_store.ConfirmJob(
            job.Id, status, confirmedCount, note,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), results));
    }

    public LabelCsvExport BuildJobExport(string jobId)
    {
        var job = _store.FindJob(jobId) ?? throw FoundationErrors.NotFound($"Job cetak {jobId} tidak ditemukan.");
        var batch = _store.FindBatch(job.BatchId) ?? throw FoundationErrors.NotFound($"Batch {job.BatchId} tidak ditemukan.");
        return BuildCsv(_store.JobLabels(job.Id), batch, job);
    }

    // -----------------------------------------------------------------------
    // Rekonsiliasi sequence (prosedur restore)
    // -----------------------------------------------------------------------

    public LabelSequenceStateDto ReconcileSequence(CallerPrincipal caller, long? observedMaxSeq, string note)
    {
        var state = GetSequenceState();
        var floor = Math.Max(state.MaxLabelSeq, state.LedgerMaxSeq);
        if (observedMaxSeq is { } observed)
        {
            if (observed <= 0)
            {
                throw FoundationErrors.BadRequest("SEQUENCE_OBSERVED",
                    "Batas tertinggi yang dilaporkan operator harus lebih dari 0.");
            }

            floor = Math.Max(floor, observed);
        }

        var text = TrimNote(string.IsNullOrWhiteSpace(note)
            ? $"Rekonsiliasi oleh {DisplayActor(caller)}"
            : note);

        if (state.NextValue > floor)
        {
            // Tidak ada yang perlu dinaikkan; tetap catat keputusan rekonsiliasi.
            _store.Ledger.AppendReconciliation(floor, DisplayActor(caller), text);
            return GetSequenceState();
        }

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        _store.EnsureSequenceRow(now);
        _store.RaiseNextValue(floor, now);
        _store.Ledger.AppendReconciliation(floor, DisplayActor(caller), text);
        return GetSequenceState();
    }

    // -----------------------------------------------------------------------
    // Internal
    // -----------------------------------------------------------------------

    private void AssertSequenceSafe()
    {
        var state = GetSequenceState();
        if (state.Consistent) return;

        throw FoundationErrors.Conflict("SEQUENCE_LAG",
            "Sequence penerbitan tertinggal dari batas tertinggi yang pernah diterbitkan " +
            $"(next_value {state.NextValue}, tertinggi {state.HighWaterSeq} dari record label/catatan penerbitan). " +
            "Generate dihentikan sampai dilakukan rekonsiliasi sequence (lihat prosedur restore).");
    }

    private static string BuildRequestHash(int quantity, string note)
    {
        var canonical = string.Join('|', NgLabelRules.Namespace, quantity.ToString(CultureInfo.InvariantCulture), note);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string DisplayActor(CallerPrincipal caller) =>
        string.IsNullOrWhiteSpace(caller.FullName) ? caller.Email : caller.FullName;

    private static string TrimNote(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > MaxNoteLength ? text[..MaxNoteLength] : text;
    }

    private static string TrimKey(string value)
    {
        var text = value.Trim();
        return text.Length > MaxIdempotencyKeyLength ? text[..MaxIdempotencyKeyLength] : text;
    }

    private static string BuildExportFilename(string batchId, string kind, DateTimeOffset at)
    {
        var stamp = Wib.ToLocal(at).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return $"NG-labels_{batchId}_{kind.ToLowerInvariant()}_{stamp}.csv";
    }

    private static LabelCsvExport BuildCsv(IReadOnlyList<NgLabelRecord> labels, LabelBatchRecord batch, PrintJobRecord job)
    {
        // Nama berkas diturunkan dari waktu + jenis job, sehingga ekspor ulang untuk job
        // yang sama selalu menghasilkan nama dan isi yang identik.
        var parsed = DateTimeOffset.TryParse(job.RequestedAtUtc, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var requested);
        var filename = BuildExportFilename(job.BatchId, job.Kind, parsed ? requested : DateTimeOffset.UtcNow);

        var builder = new StringBuilder();
        // Baris selalu berakhir CRLF tanpa bergantung OS server, sehingga ekspor job yang sama
        // menghasilkan byte identik bila dijalankan ulang di mesin/container berbeda.
        builder.Append(string.Join(",", CsvColumns)).Append("\r\n");
        foreach (var label in labels)
        {
            builder.Append(string.Join(",", new[]
            {
                Csv(label.Id),
                Csv(label.Payload),
                Csv(batch.Id),
                label.Seq.ToString(CultureInfo.InvariantCulture),
                Csv(label.Namespace),
                Csv(label.Status),
                batch.Quantity.ToString(CultureInfo.InvariantCulture),
                Csv(batch.CreatedAtUtc),
                Csv(batch.CreatedByName),
                Csv(job.Id),
                Csv(job.Kind),
                Csv(job.Channel),
            })).Append("\r\n");
        }

        return new LabelCsvExport
        {
            Filename = filename,
            Content = builder.ToString(),
            RowCount = labels.Count,
        };
    }

    /// <summary>Sel CSV: selalu berquote agar payload dengan titik/koma tidak diurai ulang.</summary>
    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Contains('"')) text = text.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{text}\"";
    }

    private static void ApplyDayRange(string? isoFrom, string? isoTo, out string? fromUtc, out string? toUtc)
    {
        fromUtc = Wib.TryWibDayRangeUtc(isoFrom, out var start, out _)
            ? start.ToString("O", CultureInfo.InvariantCulture)
            : null;
        toUtc = Wib.TryWibDayRangeUtc(isoTo, out _, out var end)
            ? end.ToString("O", CultureInfo.InvariantCulture)
            : null;
    }

    private static int ClampPage(int page) => Math.Max(1, page);

    private static int ClampSize(int pageSize, int fallback, int max)
    {
        var size = pageSize <= 0 ? fallback : pageSize;
        return Math.Min(max, Math.Max(1, size));
    }

    private static PagedResult<T> Page<T>(List<T> items, int total, int page, int pageSize) => new()
    {
        Items = items,
        Total = total,
        Page = page,
        PageSize = pageSize,
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(1, pageSize))),
    };

    private static LabelBatchDto ToBatchDto(LabelBatchRecord record) => new()
    {
        Id = record.Id,
        Namespace = record.Namespace,
        Quantity = record.Quantity,
        FirstSeq = record.FirstSeq,
        LastSeq = record.LastSeq,
        Note = record.Note,
        CreatedByAccountId = record.CreatedByAccountId,
        CreatedByName = record.CreatedByName,
        CreatedAtUtc = record.CreatedAtUtc,
        AvailableCount = record.AvailableCount,
        BoundCount = record.BoundCount,
        PrintJobCount = record.PrintJobCount,
        ConfirmedPrintCount = record.ConfirmedPrintCount,
        LastPrintedAtUtc = record.LastPrintedAtUtc,
    };

    private static NgLabelDto ToLabelDto(NgLabelRecord record) => new()
    {
        Id = record.Id,
        Seq = record.Seq,
        Namespace = record.Namespace,
        Payload = record.Payload,
        BatchId = record.BatchId,
        Status = record.Status,
        BoundSerial = record.BoundSerial,
        BoundAtUtc = record.BoundAtUtc,
        PrintedCount = record.PrintedCount,
        LastPrintedAtUtc = record.LastPrintedAtUtc,
        CreatedAtUtc = record.CreatedAtUtc,
    };

    private static PrintJobDto ToJobDto(PrintJobRecord record) => new()
    {
        Id = record.Id,
        BatchId = record.BatchId,
        Kind = record.Kind,
        Channel = record.Channel,
        Status = record.Status,
        Quantity = record.Quantity,
        RequestedByAccountId = record.RequestedByAccountId,
        RequestedByName = record.RequestedByName,
        RequestedAtUtc = record.RequestedAtUtc,
        ConfirmedAtUtc = record.ConfirmedAtUtc,
        ConfirmedCount = record.ConfirmedCount,
        ExportFilename = record.ExportFilename,
        Note = record.Note,
        LabelIds = record.LabelIds,
    };
}
