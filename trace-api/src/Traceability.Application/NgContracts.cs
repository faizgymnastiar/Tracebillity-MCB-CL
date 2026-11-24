using Traceability.Domain;

namespace Traceability.Application;

// ---------------------------------------------------------------------------
// Fase 3 — karantina NG, rework, dan scan OK akhir.
// Kontrak ini dipakai layar Karantina & Rework serta Fase 4 (packing) untuk
// membaca unit yang sudah lulus OK akhir tanpa melihat implementasi SQLite.
// ---------------------------------------------------------------------------

/// <summary>Pilihan defect aktif pada satu pos, lengkap dengan kode grupnya (SSOT §2).</summary>
public sealed class NgDefectOptionDto
{
    public string Id { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Pos QC aktif pada keluarga SKU pelaku — satu pos per kejadian karantina.</summary>
public sealed class NgStationDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Seq { get; set; }
    public List<NgDefectOptionDto> Defects { get; set; } = new();
}

/// <summary>Unit fisik: identitas label/serial, status aktual, dan hitungan percobaan.</summary>
public sealed class NgUnitDto
{
    public string Id { get; set; } = string.Empty;
    public string? Serial { get; set; }
    public string? LabelId { get; set; }
    public string? LabelPayload { get; set; }
    /// <summary>NG_QUARANTINE / REWORK_RETEST / OK_FINAL / QC_HOLD.</summary>
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    /// <summary>Sisa percobaan sebelum unit masuk Tahan QC.</summary>
    public int AttemptsRemaining { get; set; }
    public string FamilyCode { get; set; } = string.Empty;
    public string SkuCode { get; set; } = string.Empty;
    public string StageCode { get; set; } = string.Empty;
    public string? OriginWorkOrderNumber { get; set; }
    public string? TargetWorkOrderNumber { get; set; }
    public string FirstNgAtUtc { get; set; } = string.Empty;
    public string LastEventAtUtc { get; set; } = string.Empty;
    public string? OkAtUtc { get; set; }
    public string? OkActorName { get; set; }
}

/// <summary>Satu kejadian NG; defect sama tetap baris terpisah (SSOT §5).</summary>
public sealed class NgEventDto
{
    public string Id { get; set; } = string.Empty;
    public int Seq { get; set; }
    public string LabelId { get; set; } = string.Empty;
    public string StageId { get; set; } = string.Empty;
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public string DefectId { get; set; } = string.Empty;
    public string DefectGroup { get; set; } = string.Empty;
    public string DefectName { get; set; } = string.Empty;
    public int AttemptNo { get; set; }
    public string Disposition { get; set; } = string.Empty;
    public string? WorkOrderOriginNumber { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public string LineCode { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string OccurredAtUtc { get; set; } = string.Empty;
}

/// <summary>Catatan perbaikan selesai (satu per percobaan).</summary>
public sealed class ReworkEventDto
{
    public string Id { get; set; } = string.Empty;
    public int AttemptNo { get; set; }
    public string NgEventId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string PerformedAtUtc { get; set; } = string.Empty;
}

/// <summary>Hasil OK akhir beserta WO tujuan baru.</summary>
public sealed class OkScanDto
{
    public string Serial { get; set; } = string.Empty;
    public string? LabelId { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string? WorkOrderTargetNumber { get; set; }
    public string? Client { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string OccurredAtUtc { get; set; } = string.Empty;
}

/// <summary>Riwayat lengkap satu unit — bukti histori serial setelah linking.</summary>
public sealed class NgUnitDetailDto
{
    public NgUnitDto Unit { get; set; } = new();
    public List<NgEventDto> NgEvents { get; set; } = new();
    public List<ReworkEventDto> ReworkEvents { get; set; } = new();
    public OkScanDto? OkScan { get; set; }
}

public sealed class NgWorkspaceCountsDto
{
    public int Quarantine { get; set; }
    public int Retest { get; set; }
    public int QcHold { get; set; }
    public int OkFinalToday { get; set; }
    public int NgEventsToday { get; set; }
    public int ReworkToday { get; set; }
}

/// <summary>Ringkasan layar: hitungan status, antrean unit, dan batas percobaan.</summary>
public sealed class NgWorkspaceSummaryDto
{
    public NgWorkspaceCountsDto Counts { get; set; } = new();
    public int MaxAttempts { get; set; }
    /// <summary>Hak backend pelaku, agar layar hanya membuka tab yang diizinkan.</summary>
    public List<string> Permissions { get; set; } = new();
    public List<NgUnitDto> Quarantine { get; set; } = new();
    public List<NgUnitDto> Retest { get; set; } = new();
    public List<NgUnitDto> QcHold { get; set; } = new();
}

public sealed class RecordNgInput
{
    /// <summary>Payload QR internal ("NG:000123") atau ID label ("NG-000123").</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Kode pos QC (mis. ST-01); defect wajib berasal dari pos ini.</summary>
    public string StationCode { get; set; } = string.Empty;
    /// <summary>ID master defect (mis. df-01-1) — bukan kode grup.</summary>
    public string DefectId { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    /// <summary>Wajib; retry dengan key + isi sama mengembalikan kejadian yang sama.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class RecordNgResultDto
{
    public NgUnitDto Unit { get; set; } = new();
    public NgEventDto Event { get; set; } = new();
    public string LabelPayload { get; set; } = string.Empty;
    public bool Reused { get; set; }
    /// <summary>Pesan siap tampil; instruksi stiker mengikuti aturan fase.</summary>
    public string Message { get; set; } = string.Empty;
}

public sealed class RecordReworkInput
{
    /// <summary>Label internal atau serial unit yang menjalani perbaikan.</summary>
    public string UnitRef { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class RecordReworkResultDto
{
    public NgUnitDto Unit { get; set; } = new();
    public ReworkEventDto ReworkEvent { get; set; } = new();
    public bool Reused { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class OkScanInput
{
    /// <summary>SERIAL (mode serial saja) atau REWORK (pasangan label + serial).</summary>
    public string Mode { get; set; } = "SERIAL";
    public string Serial { get; set; } = string.Empty;
    /// <summary>Wajib pada mode REWORK; diabaikan pada mode SERIAL.</summary>
    public string Label { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class OkScanResultDto
{
    public NgUnitDto Unit { get; set; } = new();
    public OkScanDto OkScan { get; set; } = new();
    public bool Reused { get; set; }
    /// <summary>True hanya setelah transaksi backend berhasil; baru setelah itu stiker boleh dilepas.</summary>
    public bool RemoveLabel { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class NgUnitQuery
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public interface INgWorkflowService
{
    NgWorkspaceSummaryDto GetSummary(CallerPrincipal caller);

    /// <summary>Pos & defect aktif untuk keluarga SKU pelaku (dropdown server-side).</summary>
    IReadOnlyList<NgStationDto> ListStations(CallerPrincipal caller);

    /// <summary>NG pertama mengikat label stok ke unit; NG ulang wajib setelah rework selesai.</summary>
    RecordNgResultDto RecordNg(CallerPrincipal caller, RecordNgInput input);

    /// <summary>Perbaikan selesai: tambah satu percobaan dan kembalikan unit ke Uji ulang.</summary>
    RecordReworkResultDto RecordRework(CallerPrincipal caller, RecordReworkInput input);

    /// <summary>OK akhir: mode serial saja atau pasangan label + serial hasil rework.</summary>
    OkScanResultDto RecordOk(CallerPrincipal caller, OkScanInput input);

    PagedResult<NgUnitDto> ListUnits(NgUnitQuery query);

    /// <summary>Riwayat unit berdasarkan ID, serial, atau label internal.</summary>
    NgUnitDetailDto GetUnit(string unitRef);
}
