using System.Globalization;
using System.Text;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Infrastructure;

public sealed class ExportColumn
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool DefaultOn { get; init; }
}

public sealed class CsvBuilder
{
    private static readonly string[] LineBreaks = { "\r\n", "\n", "\r" };

    public static string Build(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(",", headers.Select(header => Escape(header))));
        foreach (var row in rows)
        {
            builder.Append("\r\n");
            builder.Append(string.Join(",", row.Select(cell => Escape(cell))));
        }

        return builder.ToString();
    }

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        var needsQuote = text.Contains(',', StringComparison.Ordinal)
            || text.Contains('"', StringComparison.Ordinal)
            || LineBreaks.Any(text.Contains);
        return needsQuote ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
    }
}

public sealed class TraceQueries : ITraceQueryService
{
    private static readonly HashSet<string> NonBoxSentinels = new(StringComparer.Ordinal)
    {
        string.Empty,
        "-",
        "IN-PROGRESS BOX",
        "KARANTINA QC",
        "REWORK GATE",
        "PENDING OUTER",
        "PENDING",
        "N/A",
    };

    public static readonly ExportColumn[] AllExportColumns =
    {
        new() { Key = "no", Label = "No", DefaultOn = true },
        new() { Key = "isoDate", Label = "Tanggal Produksi", DefaultOn = true },
        new() { Key = "waktu", Label = "Waktu Scan", DefaultOn = true },
        new() { Key = "sn", Label = "Serial Number", DefaultOn = true },
        new() { Key = "status", Label = "Status QC", DefaultOn = true },
        new() { Key = "station", Label = "Station NG", DefaultOn = true },
        new() { Key = "sku", Label = "Model / SKU", DefaultOn = true },
        new() { Key = "wo", Label = "Work Order", DefaultOn = true },
        new() { Key = "lot", Label = "Material Lot", DefaultOn = true },
        new() { Key = "innerBox", Label = "Inner Box ID", DefaultOn = true },
        new() { Key = "outerBox", Label = "Outer Box ID", DefaultOn = true },
        new() { Key = "destination", Label = "Tujuan Pengiriman", DefaultOn = true },
        new() { Key = "reworkTicket", Label = "Tiket Rework", DefaultOn = false },
        new() { Key = "line", Label = "Line / Mesin", DefaultOn = false },
        new() { Key = "shift", Label = "Shift Kerja", DefaultOn = false },
        new() { Key = "operator", Label = "Operator PIC", DefaultOn = true },
        new() { Key = "keterangan", Label = "Keterangan", DefaultOn = true },
    };

    private readonly ITraceStore _store;

    public TraceQueries(ITraceStore store)
    {
        _store = store;
    }

    public TraceRecordListResponse ListRecords(RecordQuery query, OperatorContext context)
    {
        lock (_store.Gate)
        {
            var store = _store.State;
            EnsureActiveDay(context);
            var all = BuildRecords(store);
            var search = NormalizeSearch(query.Search, store.Config.BarcodeDelimiter);
            var date = query.Date ?? string.Empty;
            var status = string.IsNullOrWhiteSpace(query.Status) ? "ALL" : query.Status.ToUpperInvariant();

            var scoped = all
                .Where(record => MatchesQuery(record, search) && (date.Length == 0 || record.IsoDate == date))
                .ToList();
            var counts = new RecordCounts
            {
                Ok = scoped.Count(record => record.Category == "OK"),
                Rework = scoped.Count(record => record.Category == "REWORK"),
                Ng = scoped.Count(record => record.Category == "NG"),
            };
            var filtered = status == "ALL" ? scoped : scoped.Where(record => record.Category == status).ToList();

            var pageSize = Math.Min(500, Math.Max(5, query.PageSize <= 0 ? 50 : query.PageSize));
            var total = filtered.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            var page = Math.Min(Math.Max(1, query.Page), totalPages);
            var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return new TraceRecordListResponse
            {
                Items = items,
                Total = total,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                LastSyncAt = store.LastSyncAt,
                Counts = counts,
                RecentBoxes = BuildRecentBoxes(store),
                Focus = BuildFocus(store, search),
            };
        }
    }

    public DailySummaryResponse GetDailySummary(OperatorContext context)
    {
        lock (_store.Gate)
        {
            var store = _store.State;
            EnsureActiveDay(context);
            var items = ComputeDailySummary(BuildRecords(store));
            return new DailySummaryResponse
            {
                Items = items,
                TotalDays = items.Count,
                LastSyncAt = store.LastSyncAt,
            };
        }
    }

    public HomeStats GetHomeStats(OperatorContext context)
    {
        lock (_store.Gate)
        {
            var store = _store.State;
            var day = EnsureActiveDay(context);
            var records = BuildRecords(store).Where(record => record.IsoDate == day.IsoDate).ToList();
            var totalOutput = day.CompletedInners.Sum(box => box.Items.Count) + day.CurrentBatch.Count;
            return new HomeStats
            {
                ActiveDate = day.IsoDate,
                TotalOutput = totalOutput,
                DirectOk = records.Count(record => record.Category == "OK"),
                NgCount = records.Count(record => record.Category == "NG"),
                InnerBoxes = day.CompletedInners.Count,
                OuterBoxes = day.CompletedOuters.Count,
            };
        }
    }

    public HealthStatus GetHealth()
    {
        lock (_store.Gate)
        {
            var store = _store.State;
            return new HealthStatus
            {
                Status = "ok",
                DataSource = "in-memory-trace-store",
                LatestSyncIso = store.LastSyncAt,
                RowsProcessed = BuildRecords(store).Count,
            };
        }
    }

    public TraceDetailResponse? GetTraceDetail(string identifier, OperatorContext context)
    {
        lock (_store.Gate)
        {
            var store = _store.State;
            EnsureActiveDay(context);
            var key = TraceHelpers.NormalizeUpperOrEmpty(Uri.UnescapeDataString(identifier));
            if (key.Length == 0) return null;

            var innerHit = FindInnerBox(store, key);
            if (innerHit is not null)
            {
                return BuildInnerDetail(store, key, innerHit.Value.Day, innerHit.Value.Box);
            }

            var outerHit = FindOuterBox(store, key);
            if (outerHit is not null)
            {
                return BuildOuterDetail(store, key, outerHit.Value.Day, outerHit.Value.Box);
            }

            var records = BuildRecords(store);
            var record = records.FirstOrDefault(item => item.Id == key)
                ?? records.FirstOrDefault(item => item.Sn == key);
            if (record is null) return null;

            return BuildUnitDetail(store, key, record);
        }
    }

    public CsvExport BuildExport(RecordQuery query, IReadOnlyCollection<string> columns, OperatorContext context)
    {
        lock (_store.Gate)
        {
            var store = _store.State;
            EnsureActiveDay(context);
            var search = NormalizeSearch(query.Search, store.Config.BarcodeDelimiter);
            var date = query.Date ?? string.Empty;
            var status = string.IsNullOrWhiteSpace(query.Status) ? "ALL" : query.Status.ToUpperInvariant();

            var selected = columns.Count > 0
                ? AllExportColumns.Where(column => columns.Contains(column.Key)).ToList()
                : AllExportColumns.ToList();

            var records = BuildRecords(store)
                .Where(record => MatchesQuery(record, search)
                    && (date.Length == 0 || record.IsoDate == date)
                    && (status == "ALL" || record.Category == status))
                .ToList();

            var headers = selected.Select(column => column.Label).ToList();
            var rows = records.Select((record, index) =>
                (IReadOnlyList<string>)selected.Select(column => CellValue(record, column.Key, index)).ToList()).ToList();

            return new CsvExport
            {
                Filename = $"LELCO_Traceability_{Wib.DateStamp()}.csv",
                Content = "\uFEFF" + CsvBuilder.Build(headers, rows),
            };
        }
    }

    private DayState EnsureActiveDay(OperatorContext context)
    {
        return DayLifecycle.EnsureActive(_store.State, context);
    }

    private static string CellValue(TraceabilityRecord record, string key, int index)
    {
        return key switch
        {
            "no" => (index + 1).ToString(CultureInfo.InvariantCulture),
            "isoDate" => FormatDateDisplay(record.IsoDate),
            "waktu" => record.Waktu,
            "sn" => record.Sn,
            "status" => record.Status,
            "station" => record.Station,
            "sku" => record.Sku,
            "wo" => record.Wo,
            "lot" => record.Lot,
            "innerBox" => record.InnerBox,
            "outerBox" => record.OuterBox,
            "destination" => record.Destination,
            "reworkTicket" => record.ReworkTicket,
            "line" => record.Line,
            "shift" => record.Shift,
            "operator" => record.Operator,
            "keterangan" => record.Keterangan,
            _ => string.Empty,
        };
    }

    private static string FormatDateDisplay(string isoDate)
    {
        var parsed = Wib.ParseIsoDate(isoDate);
        return parsed is null
            ? isoDate
            : $"{parsed.Value.Day:00}-{parsed.Value.Month:00}-{parsed.Value.Year:0000}";
    }

    public static string NormalizeSearch(string? raw, string? delimiter)
    {
        var value = (raw ?? string.Empty).Trim().ToUpperInvariant();
        if (value.Length == 0) return string.Empty;
        var token = string.IsNullOrEmpty(delimiter) ? "|" : delimiter;
        if (!value.Contains(token, StringComparison.Ordinal)) return value;
        return value.Split(token)[0].Trim();
    }

    public static bool MatchesQuery(TraceabilityRecord record, string query)
    {
        if (query.Length == 0) return true;
        var haystack = string.Join(
            " ",
            record.Sn,
            record.InnerBox,
            record.OuterBox,
            record.Wo,
            record.Lot,
            record.Sku,
            record.Station,
            record.Operator,
            record.Status,
            record.Destination,
            record.ReworkTicket).ToUpperInvariant();
        return haystack.Contains(query, StringComparison.Ordinal);
    }

    public static List<TraceabilityRecord> BuildRecords(TraceStoreState store)
    {
        var list = new List<TraceabilityRecord>();

        foreach (var day in store.Days.Values)
        {
            foreach (var inner in day.CompletedInners)
            {
                var outer = day.CompletedOuters.FirstOrDefault(box => box.Inners.Contains(inner.Id));
                foreach (var item in inner.Items)
                {
                    list.Add(new TraceabilityRecord
                    {
                        Id = $"unit-{item.Sn}",
                        Category = item.Reworked ? "REWORK" : "OK",
                        DayName = item.DayName.Length > 0 ? item.DayName : inner.DayName,
                        IsoDate = item.IsoDate.Length > 0 ? item.IsoDate : inner.IsoDate,
                        Waktu = item.Time,
                        Sn = item.Sn,
                        Status = item.Reworked ? $"PASSED (AFTER REWORK #{item.ReworkCycle})" : "PASSED (DIRECT OK)",
                        Station = "-",
                        Sku = item.Sku,
                        Wo = item.WorkOrder.Length > 0 ? item.WorkOrder : inner.WorkOrder,
                        Lot = CodeFormatter.NormalizeMaterialLotCode(item.MaterialLot.Length > 0 ? item.MaterialLot : inner.MaterialLot),
                        InnerBox = inner.Id,
                        OuterBox = outer?.Id ?? "PENDING OUTER",
                        ReworkTicket = item.ReworkTicket.Length > 0 ? item.ReworkTicket : "-",
                        Line = item.Line.Length > 0 ? item.Line : inner.Line.Length > 0 ? inner.Line : "-",
                        Shift = item.Shift.Length > 0 ? item.Shift : inner.Shift.Length > 0 ? inner.Shift : "-",
                        Destination = inner.Destination.Length > 0 ? inner.Destination : "-",
                        Operator = item.Operator,
                        Keterangan = item.Reworked
                            ? $"Lulus Rework ({(item.ReworkTicket.Length > 0 ? item.ReworkTicket : "-")})"
                            : "Standar Quality OK",
                    });
                }
            }

            foreach (var ng in day.NgItems)
            {
                list.Add(new TraceabilityRecord
                {
                    Id = $"ng-{ng.Sn}",
                    Category = "NG",
                    DayName = ng.DayName.Length > 0 ? ng.DayName : day.DayName,
                    IsoDate = ng.IsoDate,
                    Waktu = ng.Time,
                    Sn = ng.Sn,
                    Status = ng.Disposition,
                    Station = ng.StationLabel.Length > 0 ? ng.StationLabel : "-",
                    Sku = ng.Sku,
                    Wo = ng.WorkOrder,
                    Lot = CodeFormatter.NormalizeMaterialLotCode(ng.MaterialLot),
                    InnerBox = "KARANTINA QC",
                    OuterBox = "N/A",
                    ReworkTicket = "-",
                    Line = ng.Line.Length > 0 ? ng.Line : "-",
                    Shift = ng.Shift.Length > 0 ? ng.Shift : "-",
                    Destination = ng.Destination.Length > 0 ? ng.Destination : "-",
                    Operator = ng.Operator,
                    Keterangan = $"{ng.Reason}{(ng.ReworkFails is > 0 ? $" • Gagal rework {ng.ReworkFails}x" : string.Empty)}",
                });
            }

            foreach (var rework in day.ReworkItems)
            {
                list.Add(new TraceabilityRecord
                {
                    Id = $"rework-{rework.TicketId}",
                    Category = "REWORK",
                    DayName = rework.DayName.Length > 0 ? rework.DayName : day.DayName,
                    IsoDate = rework.IsoDate,
                    Waktu = rework.Time,
                    Sn = rework.Sn,
                    Status = $"AFTER REWORK VERIFIED (#{rework.Cycle})",
                    Station = rework.StationLabel.Length > 0 ? rework.StationLabel : "-",
                    Sku = rework.Sku,
                    Wo = rework.WorkOrder,
                    Lot = CodeFormatter.NormalizeMaterialLotCode(rework.MaterialLot),
                    InnerBox = "REWORK GATE",
                    OuterBox = "N/A",
                    ReworkTicket = rework.TicketId.Length > 0 ? rework.TicketId : "-",
                    Line = rework.Line.Length > 0 ? rework.Line : "-",
                    Shift = rework.Shift.Length > 0 ? rework.Shift : "-",
                    Destination = rework.Destination.Length > 0 ? rework.Destination : "-",
                    Operator = rework.Operator,
                    Keterangan = $"{rework.Action} ({rework.PrevDefect})",
                });
            }
        }

        if (store.Days.TryGetValue(store.ActiveDate, out var activeDay))
        {
            foreach (var item in activeDay.CurrentBatch)
            {
                list.Add(new TraceabilityRecord
                {
                    Id = $"unit-{item.Sn}",
                    Category = item.Reworked ? "REWORK" : "OK",
                    DayName = item.DayName.Length > 0 ? item.DayName : activeDay.DayName,
                    IsoDate = item.IsoDate,
                    Waktu = item.Time,
                    Sn = item.Sn,
                    Status = item.Reworked
                        ? $"PASSED (AFTER REWORK #{item.ReworkCycle})"
                        : "PASSED (IN-PROGRESS BOX)",
                    Station = "-",
                    Sku = item.Sku,
                    Wo = item.WorkOrder,
                    Lot = CodeFormatter.NormalizeMaterialLotCode(item.MaterialLot),
                    InnerBox = "IN-PROGRESS BOX",
                    OuterBox = "PENDING",
                    ReworkTicket = item.ReworkTicket.Length > 0 ? item.ReworkTicket : "-",
                    Line = item.Line.Length > 0 ? item.Line : "-",
                    Shift = item.Shift.Length > 0 ? item.Shift : "-",
                    Destination = store.DefaultContext.Destination.Length > 0 ? store.DefaultContext.Destination : "-",
                    Operator = item.Operator,
                    Keterangan = "Dalam Inner Box Aktif",
                });
            }
        }

        list.Sort((left, right) =>
            string.CompareOrdinal($"{right.IsoDate}{right.Waktu}", $"{left.IsoDate}{left.Waktu}"));
        return list;
    }

    private static List<RecentBox> BuildRecentBoxes(TraceStoreState store)
    {
        if (!store.Days.TryGetValue(store.ActiveDate, out var day)) return new List<RecentBox>();
        var inners = day.CompletedInners
            .TakeLast(4)
            .Reverse()
            .Select(box => new RecentBox { Id = box.Id, Type = "INNER", Sku = box.Sku, Qty = box.Items.Count });
        var outers = day.CompletedOuters
            .TakeLast(2)
            .Reverse()
            .Select(box => new RecentBox { Id = box.Id, Type = "OUTER", Sku = box.Sku, Qty = box.TotalPcs });
        return outers.Concat(inners).Take(6).ToList();
    }

    private static TraceFocus? BuildFocus(TraceStoreState store, string query)
    {
        if (query.Length == 0) return null;
        foreach (var day in store.Days.Values)
        {
            var inner = day.CompletedInners.FirstOrDefault(box => box.Id == query);
            if (inner is not null)
            {
                return new TraceFocus
                {
                    Kind = "inner",
                    Id = inner.Id,
                    Title = inner.Id,
                    Subtitle = $"{inner.Sku} • {inner.Items.Count} Pcs",
                    Status = inner.Partial ? "PARTIAL BOX" : "FULL BOX",
                };
            }

            var outer = day.CompletedOuters.FirstOrDefault(box => box.Id == query);
            if (outer is not null)
            {
                return new TraceFocus
                {
                    Kind = "outer",
                    Id = outer.Id,
                    Title = outer.Id,
                    Subtitle = $"{outer.Sku} • {outer.TotalPcs} Pcs",
                    Status = outer.Partial ? "PARTIAL OUTER" : "FULL OUTER",
                };
            }
        }

        var unit = BuildRecords(store).FirstOrDefault(record => record.Sn == query);
        if (unit is null) return null;
        return new TraceFocus
        {
            Kind = "unit",
            Id = unit.Id,
            Title = unit.Sn,
            Subtitle = $"{unit.Sku} • {unit.Destination}",
            Status = unit.Status,
        };
    }

    private static List<DailySummaryRow> ComputeDailySummary(List<TraceabilityRecord> records)
    {
        var map = new Dictionary<string, DailySummaryRow>(StringComparer.Ordinal);
        var innerSets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var outerSets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            var key = string.IsNullOrEmpty(record.IsoDate) ? Wib.IsoDate() : record.IsoDate;
            if (!map.TryGetValue(key, out var row))
            {
                row = new DailySummaryRow
                {
                    IsoDate = key,
                    DayName = string.IsNullOrEmpty(record.DayName) ? "-" : record.DayName,
                };
                map[key] = row;
                innerSets[key] = new HashSet<string>(StringComparer.Ordinal);
                outerSets[key] = new HashSet<string>(StringComparer.Ordinal);
            }

            row.Total += 1;
            if (record.Category == "OK") row.DirectOk += 1;
            else if (record.Category == "REWORK") row.AfterRework += 1;
            else row.NgCount += 1;

            var innerId = (record.InnerBox ?? string.Empty).Trim();
            var outerId = (record.OuterBox ?? string.Empty).Trim();
            if (innerId.Length > 0 && !NonBoxSentinels.Contains(innerId) && !innerId.Contains(' ', StringComparison.Ordinal))
            {
                innerSets[key].Add(innerId);
            }

            if (outerId.Length > 0 && !NonBoxSentinels.Contains(outerId) && !outerId.Contains(' ', StringComparison.Ordinal))
            {
                outerSets[key].Add(outerId);
            }
        }

        foreach (var row in map.Values)
        {
            row.InnerBoxes = innerSets[row.IsoDate].Count;
            row.OuterBoxes = outerSets[row.IsoDate].Count;
        }

        return map.Values
            .OrderByDescending(row => row.IsoDate, StringComparer.Ordinal)
            .ToList();
    }

    private static (DayState Day, InnerBox Box)? FindInnerBox(TraceStoreState store, string id)
    {
        foreach (var day in store.Days.Values)
        {
            var box = day.CompletedInners.FirstOrDefault(item => item.Id == id);
            if (box is not null) return (day, box);
        }

        return null;
    }

    private static (DayState Day, OuterBox Box)? FindOuterBox(TraceStoreState store, string id)
    {
        foreach (var day in store.Days.Values)
        {
            var box = day.CompletedOuters.FirstOrDefault(item => item.Id == id);
            if (box is not null) return (day, box);
        }

        return null;
    }

    private static string UnitHref(string sn)
    {
        return $"/modules/traceability/database/{Uri.EscapeDataString(sn)}";
    }

    private static string BoxHref(string id)
    {
        return $"/modules/traceability/database/{Uri.EscapeDataString(id)}";
    }

    private static List<RelatedUnit> RelatedUnitsOf(InnerBox box, int limit)
    {
        return box.Items
            .Take(limit)
            .Select(item => new RelatedUnit
            {
                Sn = item.Sn,
                Status = item.Reworked ? $"AFTER REWORK #{item.ReworkCycle}" : "DIRECT OK",
                Href = UnitHref(item.Sn),
            })
            .ToList();
    }

    private static TraceDetailResponse BuildInnerDetail(TraceStoreState store, string key, DayState day, InnerBox box)
    {
        var outer = day.CompletedOuters.FirstOrDefault(item => item.Inners.Contains(box.Id));
        var timeline = new List<TimelineStep>
        {
            new()
            {
                Label = "INNER BOX SEALED",
                Detail = $"{box.Id} • {box.Items.Count} Pcs • {box.Sku}",
                Time = box.Time,
                Tone = "ok",
            },
            new()
            {
                Label = "OUTER BOX",
                Detail = outer is not null ? $"{outer.Id} • {outer.Inners.Count} Inner" : "Menunggu pengelompokan outer box",
                Time = outer?.Time,
                Tone = outer is not null ? "neutral" : "rework",
            },
            new()
            {
                Label = "DESTINATION",
                Detail = string.IsNullOrEmpty(box.Destination) ? "-" : box.Destination,
                Time = null,
                Tone = "neutral",
            },
        };

        return new TraceDetailResponse
        {
            Query = key,
            Kind = "inner",
            InnerBox = box,
            OuterBox = outer,
            Timeline = timeline,
            RelatedUnits = RelatedUnitsOf(box, int.MaxValue),
            RelatedBoxes = outer is null
                ? new List<RelatedBox>()
                : new List<RelatedBox>
                {
                    new()
                    {
                        Id = outer.Id,
                        Type = "OUTER",
                        Label = $"OUTER • {outer.TotalPcs} Pcs",
                        Href = BoxHref(outer.Id),
                    },
                },
        };
    }

    private static TraceDetailResponse BuildOuterDetail(TraceStoreState store, string key, DayState day, OuterBox box)
    {
        var inners = day.CompletedInners.Where(item => box.Inners.Contains(item.Id)).ToList();
        var timeline = new List<TimelineStep>
        {
            new()
            {
                Label = "OUTER BOX CLOSED",
                Detail = $"{box.Id} • {box.TotalPcs} Pcs",
                Time = box.Time,
                Tone = "ok",
            },
            new()
            {
                Label = "INNER BOXES",
                Detail = $"{inners.Count} inner • {string.Join(", ", inners.Select(item => item.Id))}",
                Time = null,
                Tone = "neutral",
            },
            new()
            {
                Label = "DESTINATION",
                Detail = string.IsNullOrEmpty(box.Destination) ? "-" : box.Destination,
                Time = null,
                Tone = "neutral",
            },
        };

        return new TraceDetailResponse
        {
            Query = key,
            Kind = "outer",
            OuterBox = box,
            Timeline = timeline,
            RelatedUnits = inners
                .SelectMany(item => item.Items.Select(unit => new RelatedUnit
                {
                    Sn = unit.Sn,
                    Status = unit.Reworked ? $"AFTER REWORK #{unit.ReworkCycle}" : "DIRECT OK",
                    Href = UnitHref(unit.Sn),
                }))
                .ToList(),
            RelatedBoxes = inners
                .Select(item => new RelatedBox
                {
                    Id = item.Id,
                    Type = "INNER",
                    Label = $"INNER • {item.Items.Count} Pcs",
                    Href = BoxHref(item.Id),
                })
                .ToList(),
        };
    }

    private static TraceDetailResponse BuildUnitDetail(TraceStoreState store, string key, TraceabilityRecord record)
    {
        var innerBoxData = FindInnerBox(store, record.InnerBox);
        var outerBoxData = FindOuterBox(store, record.OuterBox);
        store.Days.TryGetValue(record.IsoDate, out var day);
        var rework = day?.ReworkItems.FirstOrDefault(item => item.Sn == record.Sn);

        var timeline = new List<TimelineStep>
        {
            new()
            {
                Label = "SCAN OK",
                Detail = $"{record.Sn} • {record.Sku} • {record.Operator}",
                Time = record.Waktu,
                Tone = "ok",
            },
        };

        if (record.Category == "REWORK" && record.ReworkTicket != "-")
        {
            timeline.Add(new TimelineStep
            {
                Label = "AFTER REWORK",
                Detail = rework is not null ? $"{rework.TicketId} • {rework.Action}" : record.ReworkTicket,
                Time = rework?.Time,
                Tone = "rework",
            });
        }

        if (innerBoxData is not null)
        {
            timeline.Add(new TimelineStep
            {
                Label = "INNER BOX SEALED",
                Detail = $"{innerBoxData.Value.Box.Id} • {innerBoxData.Value.Box.Items.Count} Pcs",
                Time = innerBoxData.Value.Box.Time,
                Tone = "neutral",
            });
        }
        else
        {
            timeline.Add(new TimelineStep
            {
                Label = "INNER BOX",
                Detail = record.InnerBox,
                Time = null,
                Tone = "rework",
            });
        }

        timeline.Add(new TimelineStep
        {
            Label = "OUTER BOX",
            Detail = outerBoxData is not null
                ? $"{outerBoxData.Value.Box.Id} • {outerBoxData.Value.Box.TotalPcs} Pcs"
                : record.OuterBox,
            Time = outerBoxData?.Box.Time,
            Tone = outerBoxData is not null ? "neutral" : "rework",
        });

        var relatedUnits = innerBoxData is null
            ? new List<RelatedUnit>()
            : innerBoxData.Value.Box.Items
                .Where(item => item.Sn != record.Sn)
                .Take(12)
                .Select(item => new RelatedUnit
                {
                    Sn = item.Sn,
                    Status = item.Reworked ? $"AFTER REWORK #{item.ReworkCycle}" : "DIRECT OK",
                    Href = UnitHref(item.Sn),
                })
                .ToList();

        var relatedBoxes = new List<RelatedBox>();
        if (innerBoxData is not null)
        {
            relatedBoxes.Add(new RelatedBox
            {
                Id = innerBoxData.Value.Box.Id,
                Type = "INNER",
                Label = $"INNER • {innerBoxData.Value.Box.Items.Count} Pcs",
                Href = BoxHref(innerBoxData.Value.Box.Id),
            });
        }

        if (outerBoxData is not null)
        {
            relatedBoxes.Add(new RelatedBox
            {
                Id = outerBoxData.Value.Box.Id,
                Type = "OUTER",
                Label = $"OUTER • {outerBoxData.Value.Box.TotalPcs} Pcs",
                Href = BoxHref(outerBoxData.Value.Box.Id),
            });
        }

        return new TraceDetailResponse
        {
            Query = key,
            Kind = "unit",
            Unit = record,
            InnerBox = innerBoxData?.Box,
            OuterBox = outerBoxData?.Box,
            Timeline = timeline,
            RelatedUnits = relatedUnits,
            RelatedBoxes = relatedBoxes,
        };
    }
}
