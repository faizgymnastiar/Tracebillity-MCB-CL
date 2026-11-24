using System.Globalization;
using Microsoft.Data.Sqlite;
using Traceability.Domain;

namespace Traceability.Infrastructure.Foundation;

/// <summary>Hasil alokasi batch; Reused = kunci idempotensi sudah pernah dipakai.</summary>
public sealed class LabelAllocationResult
{
    public LabelBatchRecord Batch { get; set; } = new();
    public bool Reused { get; set; }
}

/// <summary>Digit sequence ternyata sudah tercatat sebagai label — penerbitan dihentikan.</summary>
internal sealed class SequenceReuseDetectedException : Exception
{
    public SequenceReuseDetectedException(long firstSeq, long lastSeq)
        : base($"Sequence {firstSeq}..{lastSeq} sebagian sudah tercatat sebagai label.")
    {
        FirstSeq = firstSeq;
        LastSeq = lastSeq;
    }

    public long FirstSeq { get; }
    public long LastSeq { get; }
}

/// <summary>
/// Akses data stok label QR internal (Fase 2). Alokasi ID memakai sequence DB di
/// dalam satu transaksi, sehingga batch + label selalu utuh atau tidak sama sekali.
/// </summary>
public sealed class LabelStore
{
    private const string BatchSelect =
        "SELECT b.id, b.namespace, b.quantity, b.first_seq, b.last_seq, b.note, " +
        "b.created_by_account_id, b.created_by_name, b.created_at_utc, b.idempotency_key, b.request_hash, " +
        "(SELECT COUNT(1) FROM ng_labels l WHERE l.batch_id = b.id AND l.status = 'AVAILABLE') AS available_count, " +
        "(SELECT COUNT(1) FROM ng_labels l WHERE l.batch_id = b.id AND l.status = 'BOUND') AS bound_count, " +
        "(SELECT COUNT(1) FROM print_jobs j WHERE j.batch_id = b.id) AS print_job_count, " +
        "(SELECT COUNT(1) FROM print_jobs j WHERE j.batch_id = b.id AND j.status = 'CONFIRMED') AS confirmed_print_count, " +
        "(SELECT MAX(j.requested_at_utc) FROM print_jobs j WHERE j.batch_id = b.id) AS last_printed_at_utc " +
        "FROM label_batches b";

    private const string LabelSelect =
        "SELECT id, seq, namespace, payload, batch_id, status, bound_serial, bound_at_utc, " +
        "printed_count, last_printed_at_utc, created_at_utc FROM ng_labels";

    private const string JobSelect =
        "SELECT id, batch_id, kind, channel, status, quantity, requested_by_account_id, requested_by_name, " +
        "requested_at_utc, confirmed_at_utc, confirmed_count, export_filename, note, idempotency_key " +
        "FROM print_jobs";

    private readonly TraceDb _db;
    private readonly LabelIssuanceLedger _ledger;

    public LabelStore(TraceDb db, LabelIssuanceLedger ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    public LabelIssuanceLedger Ledger => _ledger;

    // -----------------------------------------------------------------------
    // Sequence penerbitan
    // -----------------------------------------------------------------------

    public (long NextValue, long MaxLabelSeq) ReadSequence()
    {
        using var connection = _db.OpenConnection();
        var next = Sql.ScalarLong(connection,
            "SELECT next_value FROM label_sequences WHERE name = $name;", ("$name", NgLabelRules.Namespace));
        var max = Sql.ScalarLong(connection, "SELECT COALESCE(MAX(seq), 0) FROM ng_labels;");
        return (next, max);
    }

    /// <summary>
    /// Naikkan next_value sampai di atas batas tertinggi yang pernah diterbitkan.
    /// Tidak pernah menurunkan nilai — sequence global tidak di-reset.
    /// </summary>
    public long RaiseNextValue(long requiredHighWaterSeq, string nowUtc)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var current = Sql.ScalarLong(connection,
            "SELECT next_value FROM label_sequences WHERE name = $name;", ("$name", NgLabelRules.Namespace));
        var target = Math.Max(current, requiredHighWaterSeq + 1);
        if (target != current)
        {
            Sql.Execute(connection,
                "UPDATE label_sequences SET next_value = $target, updated_at_utc = $now WHERE name = $name;",
                ("$target", target), ("$now", nowUtc), ("$name", NgLabelRules.Namespace));
        }

        transaction.Commit();
        return target;
    }

    /// <summary>Baris sequence boleh hilang akibat restore DB lama — dipulihkan tanpa mengubah lantai.</summary>
    public void EnsureSequenceRow(string nowUtc)
    {
        using var connection = _db.OpenConnection();
        var exists = Sql.ScalarLong(connection,
            "SELECT COUNT(1) FROM label_sequences WHERE name = $name;", ("$name", NgLabelRules.Namespace));
        if (exists == 0)
        {
            Sql.Execute(connection,
                "INSERT INTO label_sequences (name, next_value, updated_at_utc) VALUES ($name, 1, $now);",
                ("$name", NgLabelRules.Namespace), ("$now", nowUtc));
        }
    }

    // -----------------------------------------------------------------------
    // Batch & label
    // -----------------------------------------------------------------------

    /// <summary>
    /// Alokasi blok digit lalu simpan batch + label dalam satu transaksi, diikuti
    /// penulisan catatan penerbitan sebelum commit. Kegagalan di titik mana pun
    /// membatalkan seluruh batch (tidak ada batch parsial); celah digit dibiarkan.
    /// </summary>
    public LabelAllocationResult Allocate(
        int quantity,
        string note,
        string idempotencyKey,
        string requestHash,
        string accountId,
        string actorName)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            var firstSeq = AllocateBlock(connection, quantity, now);
            var lastSeq = firstSeq + quantity - 1;

            // Penyangga terakhir: digit yang sudah tercatat tidak pernah diterbitkan ulang.
            var alreadyTaken = Sql.ScalarLong(connection,
                "SELECT COUNT(1) FROM ng_labels WHERE seq BETWEEN $first AND $last;",
                ("$first", firstSeq), ("$last", lastSeq));
            if (alreadyTaken > 0)
            {
                throw new SequenceReuseDetectedException(firstSeq, lastSeq);
            }

            var batchId = BuildBatchId(firstSeq);
            Sql.Execute(connection,
                "INSERT INTO label_batches (id, namespace, quantity, first_seq, last_seq, note, " +
                "created_by_account_id, created_by_name, created_at_utc, idempotency_key, request_hash) " +
                "VALUES ($id, $namespace, $quantity, $first, $last, $note, $accountId, $actor, $created, $key, $hash);",
                ("$id", batchId), ("$namespace", NgLabelRules.Namespace), ("$quantity", quantity),
                ("$first", firstSeq), ("$last", lastSeq), ("$note", note),
                ("$accountId", accountId), ("$actor", actorName), ("$created", now),
                ("$key", idempotencyKey), ("$hash", requestHash));

            for (var seq = firstSeq; seq <= lastSeq; seq++)
            {
                Sql.Execute(connection,
                    "INSERT INTO ng_labels (id, seq, namespace, payload, batch_id, status, created_at_utc) " +
                    "VALUES ($id, $seq, $namespace, $payload, $batchId, 'AVAILABLE', $created);",
                    ("$id", NgLabelRules.BuildLabelId(seq)), ("$seq", seq),
                    ("$namespace", NgLabelRules.Namespace), ("$payload", NgLabelRules.BuildPayload(seq)),
                    ("$batchId", batchId), ("$created", now));
            }

            // Ditulis sebelum commit: ledger tidak boleh tertinggal dari DB, sebaliknya
            // baris yatim hanya menambah celah (diizinkan) dan tidak menerbitkan ulang ID.
            _ledger.Append(batchId, firstSeq, lastSeq, actorName);

            transaction.Commit();
            var batch = ReadBatch(connection, batchId, null) ?? throw new InvalidOperationException(
                $"Batch {batchId} hilang setelah commit.");
            return new LabelAllocationResult { Batch = batch, Reused = false };
        }
        catch (SqliteException exception) when (IsIdempotencyCollision(exception))
        {
            SafeRollback(transaction);
            var existing = FindBatchByKey(idempotencyKey);
            if (existing is null) throw;
            return new LabelAllocationResult { Batch = existing, Reused = true };
        }
        catch
        {
            SafeRollback(transaction);
            throw;
        }
    }

    public LabelBatchRecord? FindBatchByKey(string idempotencyKey)
    {
        using var connection = _db.OpenConnection();
        return ReadBatch(connection, null, idempotencyKey);
    }

    public LabelBatchRecord? FindBatch(string batchId)
    {
        using var connection = _db.OpenConnection();
        return ReadBatch(connection, batchId, null);
    }

    public NgLabelRecord? FindLabel(string labelId)
    {
        using var connection = _db.OpenConnection();
        return Sql.ReadOne(connection, $"{LabelSelect} WHERE id = $id;", MapLabel, ("$id", labelId));
    }

    public List<NgLabelRecord> LabelsOfBatch(string batchId, int limit)
    {
        using var connection = _db.OpenConnection();
        var sql = $"{LabelSelect} WHERE batch_id = $batchId ORDER BY seq";
        if (limit > 0) sql += $" LIMIT {limit}";
        return Sql.ReadList(connection, sql + ";", MapLabel, ("$batchId", batchId));
    }

    public List<NgLabelRecord> LabelsByIds(IReadOnlyList<string> labelIds)
    {
        if (labelIds.Count == 0) return new List<NgLabelRecord>();
        using var connection = _db.OpenConnection();
        var placeholders = new List<string>();
        for (var index = 0; index < labelIds.Count; index++)
        {
            placeholders.Add($"$id{index}");
        }

        var parameters = labelIds
            .Select((id, index) => (Name: $"$id{index}", Value: (object?)id))
            .ToArray();
        return Sql.ReadList(connection,
            $"{LabelSelect} WHERE id IN ({string.Join(", ", placeholders)}) ORDER BY seq;",
            MapLabel, parameters);
    }

    public (List<LabelBatchRecord> Items, int Total) QueryBatches(
        string? search, string? fromUtc, string? toUtc, int page, int pageSize)
    {
        using var connection = _db.OpenConnection();
        var where = BuildBatchWhere(search);
        var total = Sql.ScalarLong(connection,
            $"SELECT COUNT(1) FROM label_batches b {where}",
            ("$like", LikeTerm(search)), ("$from", fromUtc), ("$to", toUtc));

        var rows = Sql.ReadList(connection,
            $"{BatchSelect} {where} ORDER BY b.created_at_utc DESC, b.id DESC LIMIT $limit OFFSET $offset;",
            MapBatch,
            ("$like", LikeTerm(search)), ("$from", fromUtc), ("$to", toUtc),
            ("$limit", pageSize), ("$offset", (page - 1) * pageSize));

        return (rows, (int)total);
    }

    public (List<NgLabelRecord> Items, int Total) QueryLabels(
        string? batchId, string? status, int page, int pageSize)
    {
        using var connection = _db.OpenConnection();
        var where = "WHERE ($batchId IS NULL OR batch_id = $batchId) " +
                    "AND ($status IS NULL OR status = $status)";
        var total = Sql.ScalarLong(connection,
            $"SELECT COUNT(1) FROM ng_labels {where}",
            ("$batchId", batchId), ("$status", status));
        var rows = Sql.ReadList(connection,
            $"{LabelSelect} {where} ORDER BY seq DESC LIMIT $limit OFFSET $offset;",
            MapLabel,
            ("$batchId", batchId), ("$status", status),
            ("$limit", pageSize), ("$offset", (page - 1) * pageSize));
        return (rows, (int)total);
    }

    public (int Total, int Available, int Bound) StockTotals()
    {
        using var connection = _db.OpenConnection();
        var total = Sql.ScalarLong(connection, "SELECT COUNT(1) FROM ng_labels;");
        var available = Sql.ScalarLong(connection, "SELECT COUNT(1) FROM ng_labels WHERE status = 'AVAILABLE';");
        var bound = Sql.ScalarLong(connection, "SELECT COUNT(1) FROM ng_labels WHERE status = 'BOUND';");
        return ((int)total, (int)available, (int)bound);
    }

    public int CountBatchLabels(string batchId)
    {
        using var connection = _db.OpenConnection();
        return (int)Sql.ScalarLong(connection, "SELECT COUNT(1) FROM ng_labels WHERE batch_id = $batchId;",
            ("$batchId", batchId));
    }

    // -----------------------------------------------------------------------
    // Job cetak
    // -----------------------------------------------------------------------

    /// <summary>True bila sebagian label ini pernah masuk job — percobaan berikutnya REPRINT.</summary>
    public bool HasPrintedBefore(string batchId, IReadOnlyList<string> labelIds)
    {
        if (labelIds.Count == 0) return false;
        using var connection = _db.OpenConnection();
        var placeholders = string.Join(", ", labelIds.Select((_, index) => $"$id{index}"));
        var parameters = labelIds
            .Select((id, index) => (Name: $"$id{index}", Value: (object?)id))
            .ToArray();
        var found = Sql.ScalarLong(connection,
            $"SELECT COUNT(1) FROM print_job_labels jl JOIN print_jobs j ON j.id = jl.job_id " +
            $"WHERE j.batch_id = $batchId AND jl.label_id IN ({placeholders});",
            parameters.Append(("$batchId", batchId)).ToArray());
        return found > 0;
    }

    /// <summary>Simpan job + subset label atomik. Tabrakan kunci idempotensi mengembalikan job lama.</summary>
    public (PrintJobRecord Job, bool Reused) InsertJob(PrintJobRecord job)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            Sql.Execute(connection,
                "INSERT INTO print_jobs (id, batch_id, kind, channel, status, quantity, " +
                "requested_by_account_id, requested_by_name, requested_at_utc, export_filename, note, idempotency_key) " +
                "VALUES ($id, $batchId, $kind, $channel, $status, $quantity, $accountId, $actor, $requested, " +
                "$filename, $note, $key);",
                ("$id", job.Id), ("$batchId", job.BatchId), ("$kind", job.Kind),
                ("$channel", job.Channel), ("$status", job.Status), ("$quantity", job.Quantity),
                ("$accountId", job.RequestedByAccountId), ("$actor", job.RequestedByName),
                ("$requested", job.RequestedAtUtc), ("$filename", job.ExportFilename),
                ("$note", job.Note), ("$key", job.IdempotencyKey));

            foreach (var labelId in job.LabelIds)
            {
                Sql.Execute(connection,
                    "INSERT INTO print_job_labels (job_id, label_id, result) VALUES ($jobId, $labelId, 'PENDING');",
                    ("$jobId", job.Id), ("$labelId", labelId));
            }

            transaction.Commit();
            return (ReadJob(connection, job.Id) ?? job, false);
        }
        catch (SqliteException exception) when (IsCollision(exception, "print_jobs.idempotency_key"))
        {
            SafeRollback(transaction);
            if (job.IdempotencyKey is null) throw;
            var existing = FindJobByKey(job.IdempotencyKey);
            if (existing is null) throw;
            return (existing, true);
        }
        catch
        {
            SafeRollback(transaction);
            throw;
        }
    }

    public PrintJobRecord? FindJob(string jobId)
    {
        using var connection = _db.OpenConnection();
        return ReadJob(connection, jobId);
    }

    public PrintJobRecord? FindJobByKey(string idempotencyKey)
    {
        using var connection = _db.OpenConnection();
        return Sql.ReadOne(connection,
            $"{JobSelect} WHERE idempotency_key = $key;", MapJob, ("$key", idempotencyKey));
    }

    public (List<PrintJobRecord> Items, int Total) QueryJobs(
        string? batchId, string? status, string? fromUtc, string? toUtc, int page, int pageSize)
    {
        using var connection = _db.OpenConnection();
        var where = "WHERE ($batchId IS NULL OR batch_id = $batchId) " +
                    "AND ($status IS NULL OR status = $status) " +
                    "AND ($from IS NULL OR requested_at_utc >= $from) " +
                    "AND ($to IS NULL OR requested_at_utc < $to)";
        var parameters = new (string, object?)[] { ("$batchId", batchId), ("$status", status), ("$from", fromUtc), ("$to", toUtc) };
        var total = Sql.ScalarLong(connection, $"SELECT COUNT(1) FROM print_jobs {where};", parameters);
        var rows = Sql.ReadList(connection,
            $"{JobSelect} {where} ORDER BY requested_at_utc DESC, id DESC LIMIT $limit OFFSET $offset;",
            MapJob,
            parameters.Append(("$limit", pageSize)).Append(("$offset", (page - 1) * pageSize)).ToArray());

        foreach (var row in rows) row.LabelIds = JobLabelIds(connection, row.Id);
        return (rows, (int)total);
    }

    public List<string> JobLabelIds(string jobId)
    {
        using var connection = _db.OpenConnection();
        return JobLabelIds(connection, jobId);
    }

    private static List<string> JobLabelIds(SqliteConnection connection, string jobId) =>
        Sql.ReadList(connection,
            "SELECT label_id FROM print_job_labels WHERE job_id = $jobId ORDER BY label_id;",
            (reader) => Sql.Text(reader, "label_id"), ("$jobId", jobId));

    public List<NgLabelRecord> JobLabels(string jobId)
    {
        using var connection = _db.OpenConnection();
        return Sql.ReadList(connection,
            $"{LabelSelect} WHERE id IN (SELECT label_id FROM print_job_labels WHERE job_id = $jobId) ORDER BY seq;",
            MapLabel, ("$jobId", jobId));
    }

    /// <summary>
    /// Operator mencatat hasil cetak fisik. Hanya label bernilai PRINTED yang
    /// menandai stok sebagai tercetak; ekspor CSV tanpa konfirmasi tidak mengubah apa pun.
    /// </summary>
    public PrintJobRecord ConfirmJob(
        string jobId,
        string status,
        int? confirmedCount,
        string note,
        string nowUtc,
        IReadOnlyDictionary<string, string> results)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            Sql.Execute(connection,
                "UPDATE print_jobs SET status = $status, confirmed_at_utc = $now, confirmed_count = $count, " +
                "note = CASE WHEN $note = '' THEN note ELSE $note END WHERE id = $jobId;",
                ("$status", status), ("$now", nowUtc), ("$count", confirmedCount),
                ("$note", note), ("$jobId", jobId));

            foreach (var (labelId, result) in results)
            {
                Sql.Execute(connection,
                    "UPDATE print_job_labels SET result = $result, reported_at_utc = $now " +
                    "WHERE job_id = $jobId AND label_id = $labelId;",
                    ("$result", result), ("$now", nowUtc), ("$jobId", jobId), ("$labelId", labelId));
            }

            if (status == PrintJobStatus.Confirmed)
            {
                var printedIds = results.Where(pair => pair.Value == PrintLabelResult.Printed)
                    .Select(pair => pair.Key).ToArray();
                if (printedIds.Length > 0)
                {
                    var placeholders = string.Join(", ", printedIds.Select((_, index) => $"$p{index}"));
                    Sql.Execute(connection,
                        $"UPDATE ng_labels SET printed_count = printed_count + 1, last_printed_at_utc = $now " +
                        $"WHERE id IN ({placeholders});",
                        printedIds.Select((_, index) => (Name: $"$p{index}", Value: (object?)printedIds[index]))
                            .Append(("$now", nowUtc)).ToArray());
                }
            }

            transaction.Commit();
            return ReadJob(connection, jobId)
                ?? throw new InvalidOperationException($"Job cetak {jobId} tidak ditemukan setelah konfirmasi.");
        }
        catch
        {
            SafeRollback(transaction);
            throw;
        }
    }

    public (int Jobs, int Pending) JobTotals()
    {
        using var connection = _db.OpenConnection();
        var jobs = Sql.ScalarLong(connection, "SELECT COUNT(1) FROM print_jobs;");
        var pending = Sql.ScalarLong(connection, "SELECT COUNT(1) FROM print_jobs WHERE status = 'EXPORTED';");
        return ((int)jobs, (int)pending);
    }

    /// <summary>Jumlah batch yang pernah diterbitkan (indikator pertumbuhan stok).</summary>
    public int CountBatches()
    {
        using var connection = _db.OpenConnection();
        return (int)Sql.ScalarLong(connection, "SELECT COUNT(1) FROM label_batches;");
    }

    // -----------------------------------------------------------------------
    // Internal
    // -----------------------------------------------------------------------

    private static long AllocateBlock(SqliteConnection connection, int quantity, string nowUtc)
    {
        // UPDATE lebih dulu mengambil lock writer SQLite, sehingga permintaan paralel
        // tidak pernah memperoleh blok digit yang sama.
        Sql.Execute(connection,
            "UPDATE label_sequences SET next_value = next_value + $quantity, updated_at_utc = $now WHERE name = $name;",
            ("$quantity", quantity), ("$now", nowUtc), ("$name", NgLabelRules.Namespace));
        return Sql.ScalarLong(connection,
            "SELECT next_value - $quantity FROM label_sequences WHERE name = $name;",
            ("$quantity", quantity), ("$name", NgLabelRules.Namespace));
    }

    public static string BuildBatchId(long firstSeq) =>
        "LB-" + firstSeq.ToString("D6", CultureInfo.InvariantCulture);

    private LabelBatchRecord? ReadBatch(SqliteConnection connection, string? batchId, string? idempotencyKey)
    {
        var sql = batchId is not null
            ? $"{BatchSelect} WHERE b.id = $batchId;"
            : $"{BatchSelect} WHERE b.idempotency_key = $key;";
        return Sql.ReadOne(connection, sql, MapBatch,
            ("$batchId", batchId), ("$key", idempotencyKey));
    }

    private PrintJobRecord? ReadJob(SqliteConnection connection, string jobId)
    {
        var job = Sql.ReadOne(connection, $"{JobSelect} WHERE id = $id;", MapJob, ("$id", jobId));
        if (job is not null) job.LabelIds = JobLabelIds(connection, job.Id);
        return job;
    }

    private static string BuildBatchWhere(string? search)
    {
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
        {
            clauses.Add("(b.id LIKE $like ESCAPE '\\' OR b.note LIKE $like ESCAPE '\\' " +
                        "OR b.created_by_name LIKE $like ESCAPE '\\')");
        }

        clauses.Add("($from IS NULL OR b.created_at_utc >= $from)");
        clauses.Add("($to IS NULL OR b.created_at_utc < $to)");
        return "WHERE " + string.Join(" AND ", clauses);
    }

    private static string? LikeTerm(string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return null;
        var escaped = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return $"%{escaped}%";
    }

    private static LabelBatchRecord MapBatch(SqliteDataReader reader) => new()
    {
        Id = Sql.Text(reader, "id"),
        Namespace = Sql.Text(reader, "namespace"),
        Quantity = Sql.Int(reader, "quantity"),
        FirstSeq = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("first_seq"))),
        LastSeq = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("last_seq"))),
        Note = Sql.Text(reader, "note"),
        CreatedByAccountId = Sql.Text(reader, "created_by_account_id"),
        CreatedByName = Sql.Text(reader, "created_by_name"),
        CreatedAtUtc = Sql.Text(reader, "created_at_utc"),
        IdempotencyKey = Sql.Text(reader, "idempotency_key"),
        RequestHash = Sql.Text(reader, "request_hash"),
        AvailableCount = Sql.Int(reader, "available_count"),
        BoundCount = Sql.Int(reader, "bound_count"),
        PrintJobCount = Sql.Int(reader, "print_job_count"),
        ConfirmedPrintCount = Sql.Int(reader, "confirmed_print_count"),
        LastPrintedAtUtc = Sql.NullableText(reader, "last_printed_at_utc"),
    };

    private static NgLabelRecord MapLabel(SqliteDataReader reader) => new()
    {
        Id = Sql.Text(reader, "id"),
        Seq = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("seq"))),
        Namespace = Sql.Text(reader, "namespace"),
        Payload = Sql.Text(reader, "payload"),
        BatchId = Sql.Text(reader, "batch_id"),
        Status = Sql.Text(reader, "status"),
        BoundSerial = Sql.NullableText(reader, "bound_serial"),
        BoundAtUtc = Sql.NullableText(reader, "bound_at_utc"),
        PrintedCount = Sql.Int(reader, "printed_count"),
        LastPrintedAtUtc = Sql.NullableText(reader, "last_printed_at_utc"),
        CreatedAtUtc = Sql.Text(reader, "created_at_utc"),
    };

    private static PrintJobRecord MapJob(SqliteDataReader reader) => new()
    {
        Id = Sql.Text(reader, "id"),
        BatchId = Sql.Text(reader, "batch_id"),
        Kind = Sql.Text(reader, "kind"),
        Channel = Sql.Text(reader, "channel"),
        Status = Sql.Text(reader, "status"),
        Quantity = Sql.Int(reader, "quantity"),
        RequestedByAccountId = Sql.Text(reader, "requested_by_account_id"),
        RequestedByName = Sql.Text(reader, "requested_by_name"),
        RequestedAtUtc = Sql.Text(reader, "requested_at_utc"),
        ConfirmedAtUtc = Sql.NullableText(reader, "confirmed_at_utc"),
        ConfirmedCount = reader.IsDBNull(reader.GetOrdinal("confirmed_count"))
            ? null
            : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("confirmed_count"))),
        ExportFilename = Sql.NullableText(reader, "export_filename"),
        Note = Sql.Text(reader, "note"),
        IdempotencyKey = Sql.NullableText(reader, "idempotency_key"),
    };

    private static bool IsIdempotencyCollision(SqliteException exception) =>
        IsCollision(exception, "label_batches.idempotency_key");

    private static bool IsCollision(SqliteException exception, string indexOrColumn) =>
        exception.SqliteErrorCode == 19 &&
        exception.Message.Contains(indexOrColumn, StringComparison.OrdinalIgnoreCase);

    private static void SafeRollback(SqliteTransaction transaction)
    {
        try
        {
            transaction.Rollback();
        }
        catch (InvalidOperationException)
        {
            // Transaksi sudah diselesaikan (auto-rollback SQLite); tidak ada yang perlu dibatalkan.
        }
        catch (SqliteException)
        {
            // Koneksi sudah ditutup/di-dispose; rollback tidak dapat lagi dilakukan.
        }
    }
}
