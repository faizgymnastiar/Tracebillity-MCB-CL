namespace Traceability.Domain;

/// <summary>
/// Status unit pada alur NG → rework → OK akhir (SSOT §5). Unit Tahan QC tidak
/// bisa masuk packing dan tidak menerima percobaan rework keempat.
/// </summary>
public static class UnitStatus
{
    /// <summary>Unit NG menunggu perbaikan (pos/defect kejadian terakhir aktif).</summary>
    public const string Quarantine = "NG_QUARANTINE";

    /// <summary>Perbaikan selesai dicatat; unit kembali ke Assembly dan menunggu scan OK.</summary>
    public const string Retest = "REWORK_RETEST";

    /// <summary>Lulus OK akhir; satu baris hasil OK tersimpan terpisah.</summary>
    public const string OkFinal = "OK_FINAL";

    /// <summary>Percobaan habis (atau NG setelah percobaan terakhir); menunggu disposisi QC.</summary>
    public const string QcHold = "QC_HOLD";

    public static readonly string[] All = { Quarantine, Retest, OkFinal, QcHold };

    public static bool IsKnown(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);

    /// <summary>Label status untuk layar & laporan.</summary>
    public static string Describe(string? value) => value switch
    {
        Quarantine => "Karantina",
        Retest => "Uji ulang",
        OkFinal => "OK akhir",
        QcHold => "Tahan QC",
        _ => "—",
    };

    /// <summary>Status yang masih memblokir masuk packing (Fase 4).</summary>
    public static bool BlocksPacking(string? value) =>
        value is Quarantine or Retest or QcHold;
}

/// <summary>Disposisi yang disimpan pada setiap kejadian NG.</summary>
public static class NgDisposition
{
    public const string ReworkRequired = "REWORK_REQUIRED";
    public const string QcHold = "QC_HOLD";

    public static bool IsKnown(string? value) => value is ReworkRequired or QcHold;
}

/// <summary>Mode scan OK akhir: serial saja atau pasangan label + serial hasil rework.</summary>
public static class OkScanMode
{
    public const string SerialOnly = "SERIAL_ONLY";
    public const string ReworkPair = "REWORK_PAIR";
}

/// <summary>
/// Aturan hitungan percobaan (SSOT §5.3): satu percobaan dihitung saat operator
/// mencatat perbaikan selesai. Batas default tiga, total per unit lintas pos,
/// defect, operator, shift, dan WO. Angka final menunggu konfirmasi QC.
/// </summary>
public static class NgAttemptRules
{
    public const int DefaultMaxAttempts = 3;

    public static int NormalizeMaxAttempts(int configured) => configured is >= 1 and <= 10 ? configured : DefaultMaxAttempts;

    /// <summary>Percobaan berikutnya masih boleh dicatat.</summary>
    public static bool CanRecordRework(int attempts, int maxAttempts) => attempts < maxAttempts;

    /// <summary>NG yang dicatat setelah pemakaian percobaan habis masuk Tahan QC.</summary>
    public static string StatusAfterNg(int attempts, int maxAttempts) =>
        attempts >= maxAttempts ? UnitStatus.QcHold : UnitStatus.Quarantine;

    public static string DispositionAfterNg(int attempts, int maxAttempts) =>
        attempts >= maxAttempts ? NgDisposition.QcHold : NgDisposition.ReworkRequired;
}

/// <summary>Unit fisik beserta identitas label internal, serial, dan WO asal/tujuan.</summary>
public sealed class UnitRecord
{
    public string Id { get; set; } = string.Empty;
    public string? Serial { get; set; }
    public string? LabelId { get; set; }
    /// <summary>Payload QR label internal hasil join ng_labels (mis. "NG:000123").</summary>
    public string? LabelPayload { get; set; }
    public string Status { get; set; } = UnitStatus.Quarantine;
    public int Attempts { get; set; }
    public string FamilyCode { get; set; } = string.Empty;
    public string SkuCode { get; set; } = string.Empty;
    public string StageCode { get; set; } = string.Empty;
    public string? OriginWorkOrderId { get; set; }
    public string? OriginWorkOrderNumber { get; set; }
    public string? OriginClient { get; set; }
    public string? OriginSessionId { get; set; }
    public string? TargetWorkOrderId { get; set; }
    public string? TargetWorkOrderNumber { get; set; }
    public string? TargetClient { get; set; }
    public string? TargetSessionId { get; set; }
    public string FirstNgAtUtc { get; set; } = string.Empty;
    public string LastEventAtUtc { get; set; } = string.Empty;
    public string? OkAtUtc { get; set; }
    public string? OkActorName { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string UpdatedAtUtc { get; set; } = string.Empty;
}

/// <summary>Kejadian NG dengan snapshot master saat kejadian (SSOT §2).</summary>
public sealed class NgEventRecord
{
    public string Id { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public int Seq { get; set; }
    public string LabelId { get; set; } = string.Empty;
    public string StageId { get; set; } = string.Empty;
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public string DefectId { get; set; } = string.Empty;
    public string DefectGroup { get; set; } = string.Empty;
    public string DefectName { get; set; } = string.Empty;
    public int AttemptNo { get; set; }
    public string Disposition { get; set; } = NgDisposition.ReworkRequired;
    public string? WorkOrderOriginId { get; set; }
    public string? WorkOrderOriginNumber { get; set; }
    public string ActorAccountId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public string LineCode { get; set; } = string.Empty;
    public string SkuCode { get; set; } = string.Empty;
    public string FamilyCode { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string OccurredAtUtc { get; set; } = string.Empty;
}

/// <summary>Catatan perbaikan selesai — satu per percobaan per unit.</summary>
public sealed class ReworkEventRecord
{
    public string Id { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public int AttemptNo { get; set; }
    public string NgEventId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ActorAccountId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public string LineCode { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string PerformedAtUtc { get; set; } = string.Empty;
}

/// <summary>Hasil OK akhir: serial, label, mode, dan WO tujuan baru.</summary>
public sealed class OkScanRecord
{
    public string Id { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public string Serial { get; set; } = string.Empty;
    public string? LabelId { get; set; }
    public string Mode { get; set; } = OkScanMode.SerialOnly;
    public string? WorkOrderTargetId { get; set; }
    public string? WorkOrderTargetNumber { get; set; }
    public string? Client { get; set; }
    public string ActorAccountId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public string LineCode { get; set; } = string.Empty;
    public string SkuCode { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string OccurredAtUtc { get; set; } = string.Empty;
}

/// <summary>Kunci idempotensi satu permintaan scan (retry tidak menambah record).</summary>
public sealed class ScanRequestKeyRecord
{
    public string Key { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string? UnitId { get; set; }
    public string? ResultRef { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
}
