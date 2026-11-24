using System.Globalization;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Infrastructure;

public sealed class InMemoryTraceStore : ITraceStore
{
    public InMemoryTraceStore()
    {
        var today = Wib.IsoDate();
        var config = DefaultConfig.Create();
        var seed = SeedData.Build(config, today);
        State = new TraceStoreState
        {
            Config = config,
            Days = seed.Days,
            ActiveDate = today,
            DefaultContext = new OperatorContext
            {
                Operator = First(config.Operators),
                WorkOrder = "WO-2026-04",
                MaterialLot = First(config.Materials),
                Line = First(config.Lines),
                Shift = First(config.Shifts),
                Sku = config.Skus.Count > 4 ? config.Skus[4] : First(config.Skus),
                Destination = First(config.Destinations),
            },
            History = seed.History,
            ReworkCounts = seed.ReworkCounts,
            LastSyncAt = TraceHelpers.NowIso(),
        };
    }

    public TraceStoreState State { get; }

    public object Gate { get; } = new();

    private static string First(IReadOnlyList<string> values)
    {
        return values.Count > 0 ? values[0] : string.Empty;
    }
}

internal static class TraceHelpers
{
    public static string NowIso()
    {
        return DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    public static List<InnerBox> UnassignedInners(DayState day)
    {
        var used = new HashSet<string>(day.CompletedOuters.SelectMany(outer => outer.Inners), StringComparer.Ordinal);
        return day.CompletedInners.Where(box => !used.Contains(box.Id)).ToList();
    }

    public static CodeContext CodeContextFor(AppConfig config, OperatorContext context)
    {
        return new CodeContext
        {
            SeqPadding = config.SeqPadding,
            Line = context.Line,
            Destination = context.Destination,
        };
    }

    public static string FindOuterId(DayState day, string innerId)
    {
        return day.CompletedOuters.FirstOrDefault(outer => outer.Inners.Contains(innerId))?.Id ?? "PENDING OUTER";
    }

    public static string NormalizeOrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    public static string NormalizeUpperOrEmpty(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    public static int ClampSequencePadding(int value)
    {
        return value <= 0 ? 3 : value;
    }
}
