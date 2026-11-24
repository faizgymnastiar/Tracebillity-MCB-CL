using System.Globalization;
using System.Text.RegularExpressions;

namespace Traceability.Domain;

/// <summary>
/// Kontrak payload QR label internal NG (Fase 2, SSOT §4 dan §6).
///
/// Namespace internal dipakai awalan <see cref="PayloadPrefix"/> ("NG:") sehingga
/// pemindai dapat membedakan QR label stok dari QR lain tanpa melihat panjang string.
/// ID global label memakai awalan <see cref="IdPrefix"/> ("NG-") dengan digit sequence
/// yang sama, sehingga hasil scan selalu dapat dipetakan balik ke record label.
///
/// Contoh (fixture sementara sampai format nyata dikonfirmasi, SSOT §11):
///   seq 123      -> ID "NG-000123", payload QR "NG:000123"
/// Payload tersimpan di DB dan tidak pernah dihitung ulang dari template, sehingga
/// cetak ulang menghasilkan QR yang identik dengan cetakan pertama.
/// </summary>
public static class NgLabelRules
{
    /// <summary>Namespace sementara label internal sesuai SSOT §4.</summary>
    public const string Namespace = "NG";

    /// <summary>Awalan payload QR yang dicetak/di-scan.</summary>
    public const string PayloadPrefix = "NG:";

    /// <summary>Awalan ID global label (bentuk manusiawi dari payload).</summary>
    public const string IdPrefix = "NG-";

    /// <summary>Panjang digit minimum sequence; nilai besar menambah digit sendiri.</summary>
    public const int SeqDigits = 6;

    /// <summary>Batas atas nilai sequence — penjaga overflow sebelum ID berubah panjang.</summary>
    public const long MaxSeq = 999_999_999_999L;

    private static readonly Regex PayloadPattern =
        new(@"^NG:(\d{6,})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex IdPattern =
        new(@"^NG-(\d{6,})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>ID global label dari sequence DB, mis. 123 menjadi "NG-000123".</summary>
    public static string BuildLabelId(long seq)
    {
        GuardSeq(seq);
        return IdPrefix + Digits(seq);
    }

    /// <summary>Payload QR final yang dicetak, mis. "NG:000123".</summary>
    public static string BuildPayload(long seq)
    {
        GuardSeq(seq);
        return PayloadPrefix + Digits(seq);
    }

    /// <summary>Membaca sequence dari payload QR; false bila bukan label internal.</summary>
    public static bool TryParsePayload(string? raw, out long seq, out string labelId)
    {
        seq = 0;
        labelId = string.Empty;
        var match = PayloadPattern.Match((raw ?? string.Empty).Trim());
        if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var parsed) || parsed <= 0 || parsed > MaxSeq)
        {
            return false;
        }

        seq = parsed;
        labelId = BuildLabelId(parsed);
        return true;
    }

    /// <summary>Membaca sequence dari ID label ("NG-000123"); false bila bentuknya asing.</summary>
    public static bool TryParseLabelId(string? raw, out long seq)
    {
        seq = 0;
        var match = IdPattern.Match((raw ?? string.Empty).Trim());
        if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var parsed) || parsed <= 0 || parsed > MaxSeq)
        {
            return false;
        }

        seq = parsed;
        return true;
    }

    public static bool IsInternalLabel(string? raw) => TryParsePayload(raw, out _, out _);

    /// <summary>Normalisasi ID yang diketik operator menjadi bentuk kanonis "NG-000123".</summary>
    public static string? NormalizeLabelId(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (IdPattern.IsMatch(text)) return text.ToUpperInvariant();
        return TryParsePayload(text, out var seq, out var labelId) ? labelId : null;
    }

    private static string Digits(long seq)
    {
        var text = seq.ToString(CultureInfo.InvariantCulture);
        return text.Length >= SeqDigits ? text : text.PadLeft(SeqDigits, '0');
    }

    private static void GuardSeq(long seq)
    {
        if (seq <= 0 || seq > MaxSeq)
        {
            throw new ArgumentOutOfRangeException(nameof(seq),
                $"Sequence label {seq} di luar rentang 1..{MaxSeq}.");
        }
    }
}

/// <summary>
/// Hasil klasifikasi payload mentah dari pemindai (SSOT §6). Fase 2 baru menegakkan
/// namespace label internal; tipe lain sengaja dikembalikan Unknown sampai format
/// nyata serial/inner/outer dikonfirmasi (SSOT §11) — panjang string BUKAN dasar deteksi.
/// </summary>
public enum QrPayloadKind
{
    Unknown,
    InternalLabel,
    ProductSerial,
    InnerBox,
    OuterBox,
}

public static class QrClassifier
{
    /// <summary>
    /// Kenali payload berdasarkan prefix resmi. Payload yang bukan label internal
    /// dikembalikan <see cref="QrPayloadKind.Unknown"/> dan wajib diselesaikan lewat
    /// lookup register (serial terdaftar / keanggotaan box) pada Fase 3, bukan lewat tebakan.
    /// </summary>
    public static QrPayloadKind Classify(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return QrPayloadKind.Unknown;
        return NgLabelRules.IsInternalLabel(raw) ? QrPayloadKind.InternalLabel : QrPayloadKind.Unknown;
    }

    /// <summary>Pesan penolakan baku saat tipe payload tidak cocok dengan kolom scan.</summary>
    public static string MismatchMessage(QrPayloadKind expected, QrPayloadKind actual)
    {
        return $"Payload hasil scan berjenis {Describe(actual)}, kolom ini menerima {Describe(expected)}.";
    }

    public static string Describe(QrPayloadKind kind) => kind switch
    {
        QrPayloadKind.InternalLabel => "QR label internal (NG:)",
        QrPayloadKind.ProductSerial => "QR serial produk",
        QrPayloadKind.InnerBox => "QR inner box",
        QrPayloadKind.OuterBox => "QR outer box",
        _ => "payload tidak dikenal",
    };
}
