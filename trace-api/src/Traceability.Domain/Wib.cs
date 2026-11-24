using System.Globalization;

namespace Traceability.Domain;

/// <summary>
/// Semua stempel waktu produksi memakai zona WIB (Asia/Jakarta) agar kode box,
/// label, dan log harian konsisten walaupun server berjalan di zona lain.
/// </summary>
public static class Wib
{
    private static readonly string[] DayNames =
    {
        "MINGGU", "SENIN", "SELASA", "RABU", "KAMIS", "JUMAT", "SABTU",
    };

    private static readonly TimeZoneInfo Zone = ResolveZone();

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Asia/Jakarta", "SE Asia Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("WIB", TimeSpan.FromHours(7), "WIB", "WIB");
    }

    public static DateTimeOffset Now()
    {
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone);
    }

    public static string IsoDate(DateTimeOffset? value = null)
    {
        var local = ToLocal(value);
        return string.Concat(
            local.ToString("yyyy", CultureInfo.InvariantCulture),
            "-",
            local.ToString("MM", CultureInfo.InvariantCulture),
            "-",
            local.ToString("dd", CultureInfo.InvariantCulture));
    }

    public static string Clock(DateTimeOffset? value = null)
    {
        var local = ToLocal(value);
        return string.Concat(
            local.ToString("HH", CultureInfo.InvariantCulture),
            ":",
            local.ToString("mm", CultureInfo.InvariantCulture),
            ":",
            local.ToString("ss", CultureInfo.InvariantCulture));
    }

    public static string DayName(DateTimeOffset? value = null)
    {
        var local = ToLocal(value);
        return DayNames[(int)local.DayOfWeek];
    }

    public static string FullTimestamp(DateTimeOffset? value = null)
    {
        var local = ToLocal(value);
        return string.Concat(
            DayName(local),
            ", ",
            local.ToString("dd", CultureInfo.InvariantCulture),
            "/",
            local.ToString("MM", CultureInfo.InvariantCulture),
            "/",
            local.ToString("yyyy", CultureInfo.InvariantCulture),
            " ",
            Clock(local),
            " WIB");
    }

    public static DateOnly? ParseIsoDate(string? isoDate)
    {
        if (string.IsNullOrWhiteSpace(isoDate)) return null;
        return DateOnly.TryParseExact(isoDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    public static string IsoDateToDayName(string? isoDate)
    {
        var parsed = ParseIsoDate(isoDate);
        if (parsed is null) return "-";
        return DayNames[(int)parsed.Value.DayOfWeek];
    }

    public static string ShiftIso(string? isoDate, int days)
    {
        var parsed = ParseIsoDate(isoDate);
        if (parsed is null) return isoDate ?? string.Empty;
        return parsed.Value.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Rentang UTC untuk satu tanggal operasional WIB (batas bawah inklusif, batas
    /// atas eksklusif) — dipakai filter tanggal pada data yang disimpan dalam UTC.
    /// </summary>
    public static bool TryWibDayRangeUtc(string? isoDate, out DateTimeOffset startUtc, out DateTimeOffset endExclusiveUtc)
    {
        var parsed = ParseIsoDate(isoDate);
        if (parsed is null)
        {
            startUtc = default;
            endExclusiveUtc = default;
            return false;
        }

        var startLocal = new DateTimeOffset(parsed.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
        startUtc = startLocal.ToUniversalTime();
        endExclusiveUtc = startLocal.AddDays(1).ToUniversalTime();
        return true;
    }

    public static string MakeWibTimestamp(string isoDate, string time)
    {
        var parsed = ParseIsoDate(isoDate);
        if (parsed is null) return $"{isoDate} {time} WIB";
        var value = parsed.Value;
        return string.Concat(
            DayNames[(int)value.DayOfWeek],
            ", ",
            value.ToString("dd", CultureInfo.InvariantCulture),
            "/",
            value.ToString("MM", CultureInfo.InvariantCulture),
            "/",
            value.ToString("yyyy", CultureInfo.InvariantCulture),
            " ",
            time,
            " WIB");
    }

    public static string DateStamp(DateTimeOffset? value = null)
    {
        var local = ToLocal(value);
        return local.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }

    public static string MonthStamp(DateTimeOffset? value = null)
    {
        var local = ToLocal(value);
        return local.ToString("yyyyMM", CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset ToLocal(DateTimeOffset? value)
    {
        return TimeZoneInfo.ConvertTime(value ?? DateTimeOffset.UtcNow, Zone);
    }
}
