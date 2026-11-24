using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Infrastructure.Foundation;

/// <summary>
/// Alur karantina NG → rework → OK akhir (Fase 3). Service menegakkan aturan bisnis
/// (jenis QR, pos/defect, WO asal vs tujuan, batas percobaan) dan menyerahkan invariant
/// transisi ke <see cref="NgWorkflowStore"/> yang berjalan satu transaksi per operasi.
/// </summary>
public sealed class NgWorkflowService : INgWorkflowService
{
    private const string OperationNgFirst = "NG_FIRST";
    private const string OperationNgRepeat = "NG_REPEAT";
    private const string OperationRework = "REWORK";
    private const string OperationOkSerial = "OK_SERIAL";
    private const string OperationOkRework = "OK_REWORK";

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    private readonly NgWorkflowStore _store;
    private readonly LabelStore _labels;
    private readonly MasterCatalogStore _catalog;
    private readonly IConfigService _config;
    private readonly TraceDbOptions _options;

    public NgWorkflowService(NgWorkflowStore store, LabelStore labels, MasterCatalogStore catalog,
        IConfigService config, TraceDbOptions options)
    {
        _store = store;
        _labels = labels;
        _catalog = catalog;
        _config = config;
        _options = options;
    }

    private int MaxAttempts => NgAttemptRules.NormalizeMaxAttempts(_options.NgMaxAttempts);

    // -----------------------------------------------------------------------
    // Ringkasan layar
    // -----------------------------------------------------------------------

    public NgWorkspaceSummaryDto GetSummary(CallerPrincipal caller)
    {
        caller.Require(PermissionCatalog.NgRecord, PermissionCatalog.ScanUnit, PermissionCatalog.HistoryRead);

        Wib.TryWibDayRangeUtc(Wib.IsoDate(), out var dayStart, out var dayEnd);
        var counts = _store.Counts(dayStart.ToString("O", CultureInfo.InvariantCulture),
            dayEnd.ToString("O", CultureInfo.InvariantCulture));

        return new NgWorkspaceSummaryDto
        {
            Counts = new NgWorkspaceCountsDto
            {
                Quarantine = counts.Quarantine,
                Retest = counts.Retest,
                QcHold = counts.QcHold,
                OkFinalToday = counts.OkFinalToday,
                NgEventsToday = counts.NgEventsToday,
                ReworkToday = counts.ReworkToday,
            },
            MaxAttempts = MaxAttempts,
            Permissions = caller.Permissions.ToList(),
            Quarantine = _store.UnitsByStatus(UnitStatus.Quarantine, 50).Select(ToUnitDto).ToList(),
            Retest = _store.UnitsByStatus(UnitStatus.Retest, 50).Select(ToUnitDto).ToList(),
            QcHold = _store.UnitsByStatus(UnitStatus.QcHold, 50).Select(ToUnitDto).ToList(),
        };
    }

    /// <summary>Pos QC keluarga SKU pelaku; defect difilter per pos saat diseleksi.</summary>
    public IReadOnlyList<NgStationDto> ListStations(CallerPrincipal caller) => StationsOf(caller.Production);

    private List<NgStationDto> StationsOf(ProductionContextDto? context)
    {
        var familyCode = context?.FamilyCode;
        if (string.IsNullOrWhiteSpace(familyCode)) return new List<NgStationDto>();

        var family = _catalog.Families(true)
            .FirstOrDefault(item => string.Equals(item.Code, familyCode, StringComparison.OrdinalIgnoreCase));
        if (family is null) return new List<NgStationDto>();

        return _catalog.Stages(family.Id, false)
            .Select(stage => new NgStationDto
            {
                Id = stage.Id,
                Code = stage.Code,
                Name = stage.Name,
                Seq = stage.Seq,
                Defects = stage.Defects
                    .Where(defect => string.Equals(defect.Status, EntityStatus.Active, StringComparison.Ordinal))
                    .Select(defect => new NgDefectOptionDto
                    {
                        Id = defect.Id,
                        GroupCode = defect.GroupCode,
                        Name = defect.Name,
                    })
                    .ToList(),
            })
            .ToList();
    }

    // -----------------------------------------------------------------------
    // NG pertama / NG ulang
    // -----------------------------------------------------------------------

    public RecordNgResultDto RecordNg(CallerPrincipal caller, RecordNgInput input)
    {
        caller.Require(PermissionCatalog.NgRecord);
        var context = RequireContext(caller);

        var key = NormalizeKey(input.IdempotencyKey);
        var note = (input.Note ?? string.Empty).Trim();
        if (note.Length > 300) throw FoundationErrors.BadRequest("NOTE_LENGTH", "Catatan maksimal 300 karakter.");

        var labelId = NormalizeLabel(input.Label);
        var station = ResolveStation(context, input.StationCode, input.DefectId, out var defect);
        var hash = Hash("NG", labelId, station.Code, defect.Id, note);

        // Retry permintaan yang sudah berhasil selalu mengembalikan hasil lama — dicek
        // sebelum validasi status supaya label yang kini BOUND tidak memicu konflik palsu.
        var replay = TryReplay(key, hash, OperationNgFirst, OperationNgRepeat);
        if (replay is not null) return BuildNgReplay(replay);

        var label = FindLabel(labelId);
        if (string.Equals(label.Status, LabelStatus.Void, StringComparison.Ordinal))
        {
            throw FoundationErrors.Conflict("LABEL_VOID", $"Label {label.Id} sudah dibatalkan.");
        }

        var isRepeat = string.Equals(label.Status, LabelStatus.Bound, StringComparison.Ordinal);
        var operation = isRepeat ? OperationNgRepeat : OperationNgFirst;

        var command = new NgWriteCommand
        {
            Operation = operation,
            Key = key,
            Hash = hash,
            NowUtc = DateTimeOffset.UtcNow.ToString("O"),
            ActorAccountId = caller.AccountId,
            ActorName = caller.FullName,
            ShiftCode = context.ShiftCode,
            LineCode = context.LineCode,
            SessionId = context.ProductionSessionId,
            SkuCode = context.SkuCode,
            FamilyCode = context.FamilyCode,
            MaxAttempts = MaxAttempts,
            Note = note,
            LabelId = labelId,
            StageId = station.Id,
            StageCode = station.Code,
            StageName = station.Name,
            DefectId = defect.Id,
            DefectGroup = defect.GroupCode,
            DefectName = defect.Name,
            WorkOrderId = context.ActiveWorkOrderId,
            WorkOrderNumber = context.ActiveWorkOrderNumber,
            WorkOrderClient = context.WorkOrderClient,
        };

        var outcome = isRepeat ? _store.RecordRepeatNg(command) : _store.RecordFirstNg(command);
        return BuildNgResult(outcome, labelId);
    }

    private RecordNgResultDto BuildNgResult(NgWriteOutcome outcome, string labelId)
    {
        var unit = _store.FindUnit(outcome.UnitId)
            ?? throw FoundationErrors.NotFound("Unit hasil pencatatan tidak ditemukan.");
        var ngEvent = _store.FindNgEvent(outcome.RecordId)
            ?? throw FoundationErrors.NotFound("Kejadian NG hasil pencatatan tidak ditemukan.");

        var unitDto = ToUnitDto(unit);
        var message = unit.Status switch
        {
            UnitStatus.QcHold =>
                $"Kejadian NG ke-{ngEvent.Seq} tercatat. Percobaan habis ({MaxAttempts}); unit masuk Tahan QC " +
                "dan menunggu disposisi QC — jangan dilepas ke packing.",
            _ when ngEvent.Seq == 1 =>
                $"Unit dikarantina dengan label {unit.LabelPayload ?? labelId}. " +
                "Tempelkan stiker NG sebelum unit meninggalkan line.",
            _ =>
                $"Kejadian NG ke-{ngEvent.Seq} tercatat. Sisa percobaan {unitDto.AttemptsRemaining} dari {MaxAttempts}.",
        };

        return new RecordNgResultDto
        {
            Unit = unitDto,
            Event = ToNgEventDto(ngEvent),
            LabelPayload = unit.LabelPayload ?? labelId,
            Reused = outcome.Reused,
            Message = message,
        };
    }

    private RecordNgResultDto BuildNgReplay(NgWriteOutcome outcome)
    {
        var unit = _store.FindUnit(outcome.UnitId)
            ?? throw FoundationErrors.NotFound("Unit hasil sebelumnya tidak ditemukan.");
        var ngEvent = _store.FindNgEvent(outcome.RecordId)
            ?? throw FoundationErrors.NotFound("Kejadian NG hasil sebelumnya tidak ditemukan.");
        return new RecordNgResultDto
        {
            Unit = ToUnitDto(unit),
            Event = ToNgEventDto(ngEvent),
            LabelPayload = unit.LabelPayload ?? unit.LabelId ?? string.Empty,
            Reused = true,
            Message = $"Permintaan ini sudah tercatat sebelumnya (kejadian NG ke-{ngEvent.Seq}); tidak ada record baru.",
        };
    }

    // -----------------------------------------------------------------------
    // Rework selesai
    // -----------------------------------------------------------------------

    public RecordReworkResultDto RecordRework(CallerPrincipal caller, RecordReworkInput input)
    {
        caller.Require(PermissionCatalog.NgRecord);
        var context = RequireContext(caller);

        var key = NormalizeKey(input.IdempotencyKey);
        var unitRef = (input.UnitRef ?? string.Empty).Trim();
        if (unitRef.Length == 0)
        {
            throw FoundationErrors.BadRequest("UNIT_REQUIRED",
                "Pindai label NG atau serial unit yang selesai diperbaiki.");
        }

        var action = (input.Action ?? string.Empty).Trim();
        if (action.Length == 0) action = "REWORK";
        if (action.Length > 60) throw FoundationErrors.BadRequest("ACTION_LENGTH", "Aksi perbaikan maksimal 60 karakter.");
        var note = (input.Note ?? string.Empty).Trim();
        if (note.Length > 300) throw FoundationErrors.BadRequest("NOTE_LENGTH", "Catatan maksimal 300 karakter.");

        var hash = Hash(OperationRework, unitRef, action, note);
        var replay = TryReplay(key, hash, OperationRework);
        if (replay is not null)
        {
            var replayed = _store.ResolveUnit(replay.UnitId)
                ?? throw FoundationErrors.NotFound("Unit hasil sebelumnya tidak ditemukan.");
            var reworkEvent = _store.FindReworkEvent(replay.RecordId)
                ?? throw FoundationErrors.NotFound("Catatan rework hasil sebelumnya tidak ditemukan.");
            return new RecordReworkResultDto
            {
                Unit = ToUnitDto(replayed),
                ReworkEvent = ToReworkDto(reworkEvent),
                Reused = true,
                Message = "Permintaan ini sudah tercatat sebelumnya; tidak ada percobaan tambahan.",
            };
        }

        var unit = _store.ResolveUnit(unitRef)
            ?? throw FoundationErrors.Conflict("UNIT_UNKNOWN", $"Unit {unitRef} tidak ditemukan.");

        switch (unit.Status)
        {
            case UnitStatus.OkFinal:
                throw FoundationErrors.Conflict("UNIT_FINISHED",
                    "Unit ini sudah lulus OK akhir; tidak ada rework yang perlu dicatat.");
            case UnitStatus.QcHold:
                throw FoundationErrors.Conflict("QC_HOLD",
                    "Unit ini dalam Tahan QC; percobaan rework sudah habis.");
            case UnitStatus.Retest:
                throw FoundationErrors.Conflict("REWORK_ALREADY",
                    "Perbaikan unit ini sudah tercatat dan menunggu scan OK.");
        }

        if (!NgAttemptRules.CanRecordRework(unit.Attempts, MaxAttempts))
        {
            throw FoundationErrors.Conflict("QC_HOLD",
                $"Percobaan rework sudah {unit.Attempts} dari batas {MaxAttempts}; unit menunggu disposisi QC.");
        }

        var command = new NgWriteCommand
        {
            Operation = OperationRework,
            Key = key,
            Hash = hash,
            NowUtc = DateTimeOffset.UtcNow.ToString("O"),
            ActorAccountId = caller.AccountId,
            ActorName = caller.FullName,
            ShiftCode = context.ShiftCode,
            LineCode = context.LineCode,
            SessionId = context.ProductionSessionId,
            SkuCode = context.SkuCode,
            FamilyCode = context.FamilyCode,
            MaxAttempts = MaxAttempts,
            Note = note,
            UnitRef = unitRef,
            ReworkAction = action,
        };

        var outcome = _store.RecordRework(command);
        var saved = _store.FindUnit(outcome.UnitId)
            ?? throw FoundationErrors.NotFound("Unit hasil pencatatan tidak ditemukan.");
        var savedEvent = _store.FindReworkEvent(outcome.RecordId)
            ?? throw FoundationErrors.NotFound("Catatan rework hasil pencatatan tidak ditemukan.");

        return new RecordReworkResultDto
        {
            Unit = ToUnitDto(saved),
            ReworkEvent = ToReworkDto(savedEvent),
            Reused = outcome.Reused,
            Message = $"Perbaikan selesai dicatat (percobaan {savedEvent.AttemptNo} dari {MaxAttempts}). " +
                      "Unit kembali ke Uji ulang; lakukan pengujian lalu scan OK.",
        };
    }

    // -----------------------------------------------------------------------
    // Scan OK akhir
    // -----------------------------------------------------------------------

    public OkScanResultDto RecordOk(CallerPrincipal caller, OkScanInput input)
    {
        caller.Require(PermissionCatalog.ScanUnit);
        var context = RequireContext(caller);

        var mode = (input.Mode ?? string.Empty).Trim().ToUpperInvariant();
        if (mode is not ("SERIAL" or "REWORK" or "SERIAL_ONLY" or "REWORK_PAIR"))
        {
            throw FoundationErrors.BadRequest("SCAN_MODE", $"Mode scan OK \"{input.Mode}\" tidak dikenal.");
        }

        var isRework = mode is "REWORK" or "REWORK_PAIR";
        var key = NormalizeKey(input.IdempotencyKey);
        var serial = NormalizeSerial(input.Serial);
        var note = (input.Note ?? string.Empty).Trim();
        if (note.Length > 300) throw FoundationErrors.BadRequest("NOTE_LENGTH", "Catatan maksimal 300 karakter.");

        ValidateSerial(serial);

        var rawLabel = (input.Label ?? string.Empty).Trim();
        string labelId = string.Empty;
        if (isRework)
        {
            labelId = NormalizeLabel(rawLabel);
            if (string.Equals(serial, labelId, StringComparison.OrdinalIgnoreCase))
            {
                throw FoundationErrors.BadRequest("PAIR_CONFLICT",
                    "Pasangan tidak valid: label dan serial merujuk ke isi yang sama.");
            }
        }
        else if (rawLabel.Length > 0)
        {
            throw FoundationErrors.BadRequest("PAIR_CONFLICT",
                "Mode serial tidak menerima label; pindai serial pada kolom serial saja.");
        }

        if (string.IsNullOrWhiteSpace(context.ActiveWorkOrderNumber))
        {
            throw FoundationErrors.Conflict("NO_ACTIVE_WO",
                "Pilih WO aktif pada sesi produksi sebelum mencatat OK akhir.");
        }

        var operation = isRework ? OperationOkRework : OperationOkSerial;
        var hash = Hash(operation, serial, labelId, context.ActiveWorkOrderNumber, note);

        var replay = TryReplay(key, hash, operation);
        if (replay is not null)
        {
            var unit = _store.FindUnit(replay.UnitId)
                ?? throw FoundationErrors.NotFound("Unit hasil sebelumnya tidak ditemukan.");
            var okScan = _store.FindOkScan(unit.Id)
                ?? throw FoundationErrors.NotFound("Hasil OK sebelumnya tidak ditemukan.");
            return new OkScanResultDto
            {
                Unit = ToUnitDto(unit),
                OkScan = ToOkDto(okScan),
                Reused = true,
                RemoveLabel = isRework,
                Message = $"Permintaan ini sudah tercatat sebelumnya; serial {okScan.Serial} tidak ditambahkan ulang.",
            };
        }

        if (isRework)
        {
            var unit = _store.FindUnitByLabel(labelId)
                ?? throw FoundationErrors.Conflict("LABEL_UNBOUND",
                    $"Label {labelId} tidak terikat ke unit NG mana pun; gunakan mode serial.");
            if (string.Equals(unit.Status, UnitStatus.OkFinal, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("UNIT_FINISHED",
                    "Unit ini sudah lulus OK akhir; tidak dapat dipindai ulang.");
            }

            if (string.Equals(unit.Status, UnitStatus.QcHold, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("QC_HOLD",
                    "Unit ini dalam Tahan QC; OK akhir menunggu disposisi QC.");
            }

            if (!string.Equals(unit.Status, UnitStatus.Retest, StringComparison.Ordinal))
            {
                throw FoundationErrors.Conflict("OK_NOT_READY",
                    "Unit masih menunggu rework selesai; OK akhir belum dapat dicatat.");
            }

            if (unit.OriginWorkOrderNumber is not null && string.Equals(unit.OriginWorkOrderNumber,
                    context.ActiveWorkOrderNumber, StringComparison.OrdinalIgnoreCase))
            {
                throw FoundationErrors.Conflict("WO_SAME",
                    $"WO tujuan {context.ActiveWorkOrderNumber} sama dengan WO asal unit; pilih WO tujuan lain.");
            }
        }

        var command = new NgWriteCommand
        {
            Operation = operation,
            Key = key,
            Hash = hash,
            NowUtc = DateTimeOffset.UtcNow.ToString("O"),
            ActorAccountId = caller.AccountId,
            ActorName = caller.FullName,
            ShiftCode = context.ShiftCode,
            LineCode = context.LineCode,
            SessionId = context.ProductionSessionId,
            SkuCode = context.SkuCode,
            FamilyCode = context.FamilyCode,
            MaxAttempts = MaxAttempts,
            Note = note,
            LabelId = labelId,
            Serial = serial,
            WorkOrderId = context.ActiveWorkOrderId,
            WorkOrderNumber = context.ActiveWorkOrderNumber,
            WorkOrderClient = context.WorkOrderClient,
        };

        var outcome = isRework ? _store.RecordOkRework(command) : _store.RecordOkSerial(command);
        var savedUnit = _store.FindUnit(outcome.UnitId)
            ?? throw FoundationErrors.NotFound("Unit hasil pencatatan tidak ditemukan.");
        var savedOk = _store.FindOkScan(outcome.UnitId)
            ?? throw FoundationErrors.NotFound("Hasil OK pencatatan tidak ditemukan.");

        var wo = savedOk.WorkOrderTargetNumber ?? context.ActiveWorkOrderNumber;
        return new OkScanResultDto
        {
            Unit = ToUnitDto(savedUnit),
            OkScan = ToOkDto(savedOk),
            Reused = outcome.Reused,
            RemoveLabel = isRework,
            Message = isRework
                ? $"Unit {savedUnit.LabelPayload ?? labelId} lulus OK akhir sebagai serial {savedOk.Serial} " +
                  $"pada WO {wo}. Lepas stiker sementara sekarang."
                : $"Serial {savedOk.Serial} tercatat OK akhir pada WO {wo}.",
        };
    }

    // -----------------------------------------------------------------------
    // Riwayat unit
    // -----------------------------------------------------------------------

    public PagedResult<NgUnitDto> ListUnits(NgUnitQuery query)
    {
        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim().ToUpperInvariant();
        if (status is not null && !UnitStatus.IsKnown(status))
        {
            throw FoundationErrors.BadRequest("UNIT_STATUS", $"Status unit \"{query.Status}\" tidak dikenal.");
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, 200);
        var (items, total) = _store.QueryUnits(query.Search, status, page, pageSize);
        return new PagedResult<NgUnitDto>
        {
            Items = items.Select(ToUnitDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)),
        };
    }

    public NgUnitDetailDto GetUnit(string unitRef)
    {
        var unit = _store.ResolveUnit(unitRef)
            ?? throw FoundationErrors.NotFound($"Unit {unitRef} tidak ditemukan.");

        return new NgUnitDetailDto
        {
            Unit = ToUnitDto(unit),
            NgEvents = _store.NgEvents(unit.Id).Select(ToNgEventDto).ToList(),
            ReworkEvents = _store.ReworkEvents(unit.Id).Select(ToReworkDto).ToList(),
            OkScan = _store.FindOkScan(unit.Id) is { } okScan ? ToOkDto(okScan) : null,
        };
    }

    // -----------------------------------------------------------------------
    // Validasi masukan
    // -----------------------------------------------------------------------

    private static ProductionContextDto RequireContext(CallerPrincipal caller)
    {
        return caller.Production ?? throw FoundationErrors.Conflict("NO_PRODUCTION_SESSION",
            "Mulai sesi produksi (shift, line, SKU) sebelum mencatat karantina atau OK akhir.");
    }

    private static string NormalizeKey(string? raw)
    {
        var key = (raw ?? string.Empty).Trim();
        if (key.Length is < 8 or > 120)
        {
            throw FoundationErrors.BadRequest("IDEMPOTENCY_KEY",
                "Setiap scan wajib menyertakan kunci idempotensi 8-120 karakter agar retry tidak menggandakan record.");
        }

        return key;
    }

    /// <summary>Label internal: terima payload QR ("NG:000123") maupun ID kanonis ("NG-000123").</summary>
    private static string NormalizeLabel(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw FoundationErrors.BadRequest("LABEL_REQUIRED", "Pindai QR label internal NG lebih dulu.");
        }

        return NgLabelRules.NormalizeLabelId(text)
               ?? throw FoundationErrors.BadRequest("QR_NOT_INTERNAL",
                   "Kolom label hanya menerima QR label internal (NG:000123); QR lain ditolak.");
    }

    private static string NormalizeSerial(string? raw) => (raw ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Serial disimpan sebagai string; QR label di kolom serial ditolak (SSOT §6).</summary>
    private void ValidateSerial(string serial)
    {
        if (serial.Length == 0)
        {
            throw FoundationErrors.BadRequest("SERIAL_REQUIRED", "Pindai serial produk lebih dulu.");
        }

        if (serial.Length is < 4 or > 64)
        {
            throw FoundationErrors.BadRequest("SERIAL_INVALID",
                "Serial produk minimal 4 dan maksimal 64 karakter.");
        }

        if (NgLabelRules.NormalizeLabelId(serial) is not null ||
            serial.StartsWith("INNER:", StringComparison.OrdinalIgnoreCase) ||
            serial.StartsWith("OUTER:", StringComparison.OrdinalIgnoreCase))
        {
            throw FoundationErrors.BadRequest("QR_TYPE",
                "Kolom serial hanya menerima serial produk.");
        }

        var pattern = _config.GetConfig().SnPattern;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw FoundationErrors.Conflict("SERIAL_FORMAT_NOT_CONFIGURED",
                "Format serial produk belum dikonfigurasi. Hubungi admin sebelum menyimpan hasil OK.");
        }

        try
        {
            if (!Regex.IsMatch(serial, $@"\A(?:{pattern})\z", RegexOptions.None, PatternTimeout))
            {
                throw FoundationErrors.BadRequest("SERIAL_PATTERN",
                    $"Format serial {serial} tidak sesuai pola ({pattern}).");
            }
        }
        catch (ArgumentException)
        {
            throw FoundationErrors.Conflict("SERIAL_FORMAT_NOT_CONFIGURED",
                "Pola serial produk tidak valid. Hubungi admin sebelum menyimpan hasil OK.");
        }
        catch (RegexMatchTimeoutException)
        {
            throw FoundationErrors.BadRequest("SERIAL_PATTERN", "Pemeriksaan pola serial melewati batas waktu.");
        }
    }

    private NgStationDto ResolveStation(ProductionContextDto context, string? stationCode, string? defectId,
        out NgDefectOptionDto defect)
    {
        var code = (stationCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            throw FoundationErrors.BadRequest("STATION_REQUIRED", "Pilih pos QC tempat defect ditemukan.");
        }

        var station = StationsOf(context)
            .FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
            ?? throw FoundationErrors.BadRequest("STATION_UNKNOWN",
                $"Pos QC {code} tidak aktif pada keluarga produk sesi ini.");

        var selected = (defectId ?? string.Empty).Trim();
        if (selected.Length == 0)
        {
            throw FoundationErrors.BadRequest("DEFECT_REQUIRED", "Pilih satu defect pada pos ini.");
        }

        var match = station.Defects.FirstOrDefault(item =>
            string.Equals(item.Id, selected, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw FoundationErrors.BadRequest("DEFECT_NOT_IN_STATION",
                $"Defect {selected} bukan pilihan aktif pada pos {code}.");
        }

        defect = match;
        return station;
    }

    // -----------------------------------------------------------------------
    // Idempotensi
    // -----------------------------------------------------------------------

    private NgWriteOutcome? TryReplay(string key, string hash, params string[] operations)
    {
        var row = _store.FindKey(key);
        if (row is null) return null;
        if (!operations.Contains(row.Operation, StringComparer.Ordinal) ||
            !string.Equals(row.RequestHash, hash, StringComparison.Ordinal))
        {
            throw FoundationErrors.Conflict("IDEMPOTENCY_CONFLICT",
                "Kunci idempotensi ini sudah dipakai untuk permintaan yang berbeda.");
        }

        return new NgWriteOutcome
        {
            Reused = true,
            UnitId = row.UnitId ?? string.Empty,
            RecordId = row.ResultRef ?? string.Empty,
        };
    }

    private static string Hash(params string?[] parts)
    {
        var joined = string.Join('\u001f', parts.Select(part => (part ?? string.Empty).Trim()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    // -----------------------------------------------------------------------
    // Pemetaan DTO
    // -----------------------------------------------------------------------

    private NgUnitDto ToUnitDto(UnitRecord unit) => new()
    {
        Id = unit.Id,
        Serial = unit.Serial,
        LabelId = unit.LabelId,
        LabelPayload = unit.LabelPayload,
        Status = unit.Status,
        StatusLabel = UnitStatus.Describe(unit.Status),
        Attempts = unit.Attempts,
        MaxAttempts = MaxAttempts,
        AttemptsRemaining = Math.Max(0, MaxAttempts - unit.Attempts),
        FamilyCode = unit.FamilyCode,
        SkuCode = unit.SkuCode,
        StageCode = unit.StageCode,
        OriginWorkOrderNumber = unit.OriginWorkOrderNumber,
        TargetWorkOrderNumber = unit.TargetWorkOrderNumber,
        FirstNgAtUtc = unit.FirstNgAtUtc,
        LastEventAtUtc = unit.LastEventAtUtc,
        OkAtUtc = unit.OkAtUtc,
        OkActorName = unit.OkActorName,
    };

    private static NgEventDto ToNgEventDto(NgEventRecord record) => new()
    {
        Id = record.Id,
        Seq = record.Seq,
        LabelId = record.LabelId,
        StageId = record.StageId,
        StageCode = record.StageCode,
        StageName = record.StageName,
        DefectId = record.DefectId,
        DefectGroup = record.DefectGroup,
        DefectName = record.DefectName,
        AttemptNo = record.AttemptNo,
        Disposition = record.Disposition,
        WorkOrderOriginNumber = record.WorkOrderOriginNumber,
        ActorName = record.ActorName,
        ShiftCode = record.ShiftCode,
        LineCode = record.LineCode,
        Note = record.Note,
        OccurredAtUtc = record.OccurredAtUtc,
    };

    private static ReworkEventDto ToReworkDto(ReworkEventRecord record) => new()
    {
        Id = record.Id,
        AttemptNo = record.AttemptNo,
        NgEventId = record.NgEventId,
        Action = record.Action,
        ActorName = record.ActorName,
        Note = record.Note,
        PerformedAtUtc = record.PerformedAtUtc,
    };

    private static OkScanDto ToOkDto(OkScanRecord record) => new()
    {
        Serial = record.Serial,
        LabelId = record.LabelId,
        Mode = record.Mode,
        WorkOrderTargetNumber = record.WorkOrderTargetNumber,
        Client = record.Client,
        ActorName = record.ActorName,
        OccurredAtUtc = record.OccurredAtUtc,
    };

    private NgLabelRecord FindLabel(string labelId)
    {
        return _labels.FindLabel(labelId)
               ?? throw FoundationErrors.Conflict("LABEL_UNKNOWN", $"Label {labelId} belum pernah diterbitkan.");
    }
}
