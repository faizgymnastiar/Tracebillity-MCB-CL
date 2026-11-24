using Traceability.Domain;

namespace Traceability.Infrastructure;

public sealed class SeedResult
{
    public Dictionary<string, DayState> Days { get; set; } = new();
    public List<BarcodeHistoryEntry> History { get; set; } = new();
    public Dictionary<string, int> ReworkCounts { get; set; } = new();
}

/// <summary>
/// Menyiapkan data historis tiga hari terakhir plus hari aktif, identik dengan
/// mockup supaya dashboard, database trace, dan laporan langsung terisi.
/// </summary>
public static class SeedData
{
    private sealed class SeedPlan
    {
        public int Offset { get; init; }
        public string Sku { get; init; } = string.Empty;
        public string Line { get; init; } = string.Empty;
        public string Shift { get; init; } = string.Empty;
        public string Destination { get; init; } = string.Empty;
        public string Operator { get; init; } = string.Empty;
        public int InnerBoxes { get; init; }
        public int ActiveItems { get; init; }
        public int NgCount { get; init; }
        public int ReworkCount { get; init; }
    }

    private static readonly SeedPlan[] PastPlans =
    {
        new()
        {
            Offset = 3, Sku = "MCB CL P1 - 10 A", Line = "LINE 01 - MCB CL", Shift = "Shift 1",
            Destination = "CUST-02 - PLN Bandung", Operator = "OP-2026-TAOFIQ",
            InnerBoxes = 3, ActiveItems = 0, NgCount = 2, ReworkCount = 1,
        },
        new()
        {
            Offset = 2, Sku = "MCB CL P1 - 16 A", Line = "LINE 02 - MCB CL", Shift = "Shift 2",
            Destination = "CUST-01 - PLN Jakarta", Operator = "OP-2026-OWEN",
            InnerBoxes = 3, ActiveItems = 0, NgCount = 1, ReworkCount = 1,
        },
        new()
        {
            Offset = 1, Sku = "MCB CL P3 - 20 A", Line = "LINE 01 - MCB CL", Shift = "Shift 1",
            Destination = "CUST-03 - PLN Tangerang", Operator = "OP-2026-IHSAN",
            InnerBoxes = 3, ActiveItems = 0, NgCount = 2, ReworkCount = 0,
        },
    };

    private static readonly SeedPlan TodayPlan = new()
    {
        Offset = 0, Sku = "MCB CL P1 - 20 A", Line = "LINE 01 - MCB CL", Shift = "Shift 1",
        Destination = "CUST-01 - PLN Jakarta", Operator = "OP-2026-REGA",
        InnerBoxes = 2, ActiveItems = 5, NgCount = 1, ReworkCount = 1,
    };

    public static DayState CreateDayState(string isoDate)
    {
        return new DayState
        {
            IsoDate = isoDate,
            DayName = Wib.IsoDateToDayName(isoDate),
            InnerSeq = 1,
            OuterSeq = 1,
            ReworkSeq = 1,
        };
    }

    public static SeedResult Build(AppConfig config, string todayIso)
    {
        var result = new SeedResult();

        for (var index = 0; index < PastPlans.Length; index += 1)
        {
            var plan = PastPlans[index];
            var isoDate = Wib.ShiftIso(todayIso, -plan.Offset);
            var workOrder = $"WO-2026-0{index + 1}";
            var seeded = BuildDay(config, isoDate, plan, workOrder);
            result.Days[isoDate] = seeded.Day;
            result.History.AddRange(seeded.History);
            foreach (var pair in seeded.ReworkCounts) result.ReworkCounts[pair.Key] = pair.Value;
        }

        var today = BuildDay(config, todayIso, TodayPlan, "WO-2026-04");
        result.Days[todayIso] = today.Day;
        result.History.AddRange(today.History);
        foreach (var pair in today.ReworkCounts) result.ReworkCounts[pair.Key] = pair.Value;

        return result;
    }

    private sealed class SeededDay
    {
        public DayState Day { get; init; } = new();
        public List<BarcodeHistoryEntry> History { get; init; } = new();
        public Dictionary<string, int> ReworkCounts { get; init; } = new();
    }

    private static SeededDay BuildDay(AppConfig config, string isoDate, SeedPlan plan, string workOrder)
    {
        var day = CreateDayState(isoDate);
        var dayDate = Wib.ParseIsoDate(isoDate) ?? DateOnly.FromDateTime(Wib.Now().DateTime);
        var dateValue = new DateTimeOffset(dayDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), TimeSpan.Zero);
        var codeContext = new CodeContext
        {
            SeqPadding = config.SeqPadding,
            Line = plan.Line,
            Destination = plan.Destination,
        };
        var stamp = isoDate.Replace("-", string.Empty, StringComparison.Ordinal);
        var history = new List<BarcodeHistoryEntry>();
        var reworkCounts = new Dictionary<string, int>();
        var clock = (8 * 3600) + (12 * 60);
        var unitSeq = 1;
        var materialLot = config.Materials.Count > 0 ? config.Materials[0] : string.Empty;

        string NextTimestamp()
        {
            clock += 97;
            return Wib.MakeWibTimestamp(isoDate, TimeAt(clock));
        }

        BatchItem NextItem()
        {
            var sn = $"SN{stamp}{unitSeq.ToString("0000", System.Globalization.CultureInfo.InvariantCulture)}";
            unitSeq += 1;
            return new BatchItem
            {
                Sn = sn,
                DayName = day.DayName,
                IsoDate = isoDate,
                Time = NextTimestamp(),
                Operator = plan.Operator,
                WorkOrder = workOrder,
                MaterialLot = materialLot,
                Line = plan.Line,
                Shift = plan.Shift,
                Sku = plan.Sku,
                ReworkTicket = "-",
            };
        }

        for (var boxIndex = 0; boxIndex < plan.InnerBoxes; boxIndex += 1)
        {
            var innerId = CodeFormatter.FormatCode(config.InnerCodeFormat, day.InnerSeq, codeContext, dateValue);
            day.InnerSeq += 1;
            var items = new List<BatchItem>();
            for (var itemIndex = 0; itemIndex < config.InnerCap; itemIndex += 1) items.Add(NextItem());
            var inner = new InnerBox
            {
                Id = innerId,
                DayName = day.DayName,
                IsoDate = isoDate,
                Time = items.Count > 0 ? items[^1].Time : Wib.MakeWibTimestamp(isoDate, TimeAt(clock)),
                Operator = plan.Operator,
                WorkOrder = workOrder,
                MaterialLot = materialLot,
                Line = plan.Line,
                Shift = plan.Shift,
                Sku = plan.Sku,
                Destination = plan.Destination,
                Items = items,
            };
            day.CompletedInners.Add(inner);
            history.Add(new BarcodeHistoryEntry
            {
                Type = "INNER",
                Id = inner.Id,
                Sku = inner.Sku,
                Destination = inner.Destination,
                Qty = inner.Items.Count,
                Event = "FULL",
                Time = inner.Time,
                IsoDate = isoDate,
            });
        }

        if (plan.ActiveItems > 0)
        {
            for (var itemIndex = 0; itemIndex < plan.ActiveItems; itemIndex += 1)
            {
                day.CurrentBatch.Add(NextItem());
            }

            day.InnerPrintedTime = day.CurrentBatch.Count > 0 ? day.CurrentBatch[^1].Time : null;
        }

        if (day.CompletedInners.Count > 0)
        {
            var outerId = CodeFormatter.FormatCode(config.OuterCodeFormat, day.OuterSeq, codeContext, dateValue);
            day.OuterSeq += 1;
            var grouped = day.CompletedInners.Take(config.OuterCap).ToList();
            var outer = new OuterBox
            {
                Id = outerId,
                DayName = day.DayName,
                IsoDate = isoDate,
                Time = Wib.MakeWibTimestamp(isoDate, TimeAt(clock + 300)),
                Operator = plan.Operator,
                WorkOrder = workOrder,
                MaterialLot = materialLot,
                Sku = plan.Sku,
                Destination = plan.Destination,
                Partial = grouped.Count < config.OuterCap,
                Inners = grouped.Select(box => box.Id).ToList(),
                TotalPcs = grouped.Sum(box => box.Items.Count),
            };
            day.CompletedOuters.Add(outer);
            day.OuterPrintedTime = outer.Time;
            history.Add(new BarcodeHistoryEntry
            {
                Type = "OUTER",
                Id = outer.Id,
                Sku = outer.Sku,
                Destination = outer.Destination,
                Qty = outer.TotalPcs,
                Event = "FULL",
                Time = outer.Time,
                IsoDate = isoDate,
            });
        }

        for (var ngIndex = 0; ngIndex < plan.NgCount; ngIndex += 1)
        {
            var station = config.NgStations[ngIndex % config.NgStations.Count];
            var sn = $"SN{stamp}9{(ngIndex + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture)}";
            day.NgItems.Add(new NgItem
            {
                Sn = sn,
                StationId = station.Id,
                StationLabel = station.Label,
                Reason = station.Defects.Count > 0 ? station.Defects[0] : $"{station.Id}: Other",
                Disposition = "REWORK REQUIRED",
                DayName = day.DayName,
                IsoDate = isoDate,
                Time = NextTimestamp(),
                Operator = plan.Operator,
                WorkOrder = workOrder,
                MaterialLot = materialLot,
                Line = plan.Line,
                Shift = plan.Shift,
                Sku = plan.Sku,
                Destination = plan.Destination,
            });
        }

        for (var reworkIndex = 0; reworkIndex < plan.ReworkCount; reworkIndex += 1)
        {
            var ticketId = CodeFormatter.FormatCode(config.ReworkCodeFormat, day.ReworkSeq, codeContext, dateValue);
            day.ReworkSeq += 1;
            var sn = $"SN{stamp}8{(reworkIndex + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture)}";
            var station = config.NgStations.Count > 1 ? config.NgStations[1] : config.NgStations[0];
            day.ReworkItems.Add(new ReworkItem
            {
                TicketId = ticketId,
                Sn = sn,
                Cycle = 1,
                StationLabel = station.Label,
                PrevDefect = station.Defects.Count > 0 ? station.Defects[0] : "-",
                Action = config.Reworks.Count > 0 ? config.Reworks[0] : string.Empty,
                DayName = day.DayName,
                IsoDate = isoDate,
                Time = NextTimestamp(),
                Operator = plan.Operator,
                WorkOrder = workOrder,
                MaterialLot = materialLot,
                Line = plan.Line,
                Shift = plan.Shift,
                Sku = plan.Sku,
                Destination = plan.Destination,
            });
            reworkCounts[sn] = 1;
        }

        return new SeededDay { Day = day, History = history, ReworkCounts = reworkCounts };
    }

    private static string TimeAt(int seconds)
    {
        var hours = (seconds / 3600) % 24;
        var minutes = (seconds % 3600) / 60;
        var remainder = seconds % 60;
        return string.Join(
            ":",
            new[]
            {
                hours.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                minutes.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                remainder.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
            });
    }
}
