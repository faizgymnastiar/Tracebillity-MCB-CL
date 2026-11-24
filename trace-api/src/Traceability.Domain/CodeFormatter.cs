using System.Globalization;
using System.Text.RegularExpressions;

namespace Traceability.Domain;

public sealed class CodeContext
{
    public int SeqPadding { get; set; } = 3;
    public string Line { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
}

public static class CodeFormatter
{
    private static readonly Regex ParenthesisNotes = new(@"\s*\([^)]*\)\s*", RegexOptions.Compiled);
    private static readonly Regex NonDestinationChars = new("[^A-Z0-9-]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NonLineChars = new("[^A-Z0-9]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Kode lot material dipakai apa adanya di barcode 2D, jadi keterangan dalam
    /// tanda kurung dibuang agar payload tetap ringkas dan stabil saat di-scan.
    /// </summary>
    public static string NormalizeMaterialLotCode(string? rawLot)
    {
        var clean = (rawLot ?? string.Empty).Trim().ToUpperInvariant();
        if (clean.Length == 0) return "LOT-000";
        var codeOnly = ParenthesisNotes.Replace(clean, " ").Trim();
        return codeOnly.Length > 0 ? codeOnly : clean;
    }

    public static string DestinationShortCode(string? destination)
    {
        var value = string.IsNullOrWhiteSpace(destination) ? "DEST" : destination;
        var head = value.Split(" - ")[0];
        return NonDestinationChars.Replace(head, string.Empty).ToUpperInvariant();
    }

    public static string LineShortCode(string? line)
    {
        var value = string.IsNullOrWhiteSpace(line) ? "L01" : line;
        return NonLineChars.Replace(value, string.Empty).ToUpperInvariant();
    }

    public static string FormatCode(string? template, int sequence, CodeContext context, DateTimeOffset? date = null)
    {
        var value = Wib.ToLocal(date);
        var pad = Math.Max(2, context.SeqPadding <= 0 ? 3 : context.SeqPadding);
        var source = string.IsNullOrWhiteSpace(template) ? "CODE-{SEQ}" : template;
        return source
            .Replace("{YYYY}", value.ToString("yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{YY}", value.ToString("yy", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{MM}", value.ToString("MM", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{DD}", value.ToString("dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{DEST}", DestinationShortCode(context.Destination), StringComparison.Ordinal)
            .Replace("{LINE}", LineShortCode(context.Line), StringComparison.Ordinal)
            .Replace("{SEQ}", sequence.ToString(new string('0', pad), CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
