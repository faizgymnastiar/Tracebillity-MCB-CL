using System.Text.Json;
using System.Text.Json.Nodes;
using Traceability.Application;
using Traceability.Domain;

namespace Traceability.Infrastructure;

public sealed class ConfigService : IConfigService
{
    public static readonly string[] MasterKeys =
    {
        "skus", "materials", "operators", "lines", "shifts", "destinations", "reworks",
    };

    public static readonly Dictionary<string, string> MasterLabels = new(StringComparer.Ordinal)
    {
        ["skus"] = "Model / SKU",
        ["materials"] = "Lot Material",
        ["operators"] = "Operator / NIK",
        ["lines"] = "Line Mesin",
        ["shifts"] = "Shift Kerja",
        ["destinations"] = "Tujuan Pengiriman",
        ["reworks"] = "Tindakan Rework",
    };

    private static readonly JsonSerializerOptions PatchOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ITraceStore _store;

    public ConfigService(ITraceStore store)
    {
        _store = store;
    }

    public AppConfig GetConfig()
    {
        lock (_store.Gate)
        {
            return _store.State.Config;
        }
    }

    public PublicConfig GetPublicConfig()
    {
        lock (_store.Gate)
        {
            return ToPublic(_store.State.Config);
        }
    }

    public PublicConfig UpdateConfig(JsonObject patch)
    {
        lock (_store.Gate)
        {
            var state = _store.State;
            var current = JsonSerializer.SerializeToNode(ToPublic(state.Config), PatchOptions) as JsonObject
                ?? new JsonObject();

            foreach (var pair in patch)
            {
                if (string.Equals(pair.Key, "adminPin", StringComparison.OrdinalIgnoreCase)) continue;
                current[pair.Key] = pair.Value?.DeepClone();
            }

            try
            {
                var next = current.Deserialize<AppConfig>(PatchOptions);
                if (next is null) return ToPublic(state.Config);
                next.AdminPin = state.Config.AdminPin;
                state.Config = next;
            }
            catch (JsonException)
            {
                return ToPublic(state.Config);
            }

            state.LastSyncAt = TraceHelpers.NowIso();
            return ToPublic(state.Config);
        }
    }

    public PublicConfig ResetConfig()
    {
        lock (_store.Gate)
        {
            var state = _store.State;
            state.Config = DefaultConfig.Create();
            state.LastSyncAt = TraceHelpers.NowIso();
            return ToPublic(state.Config);
        }
    }

    public MasterListsResponse GetMasterLists()
    {
        lock (_store.Gate)
        {
            var config = _store.State.Config;
            return new MasterListsResponse
            {
                Keys = MasterKeys.ToList(),
                Labels = new Dictionary<string, string>(MasterLabels, StringComparer.Ordinal),
                Lists = ReadLists(config),
            };
        }
    }

    public MasterListsResponse AddMasterValue(string key, string value)
    {
        lock (_store.Gate)
        {
            var config = _store.State.Config;
            var target = ResolveList(config, key);
            if (target is not null)
            {
                var exists = target.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
                if (!exists) target.Add(value);
                _store.State.LastSyncAt = TraceHelpers.NowIso();
            }

            return GetMasterListsLocked(config);
        }
    }

    public MasterListsResponse RemoveMasterValue(string key, int? index, string? value)
    {
        lock (_store.Gate)
        {
            var config = _store.State.Config;
            var target = ResolveList(config, key);
            if (target is not null)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    target.RemoveAll(item => string.Equals(item, value, StringComparison.Ordinal));
                }
                else if (index is not null && index.Value >= 0 && index.Value < target.Count)
                {
                    target.RemoveAt(index.Value);
                }

                _store.State.LastSyncAt = TraceHelpers.NowIso();
            }

            return GetMasterListsLocked(config);
        }
    }

    public bool VerifyPin(string? pin)
    {
        lock (_store.Gate)
        {
            return string.Equals((pin ?? string.Empty).Trim(), _store.State.Config.AdminPin, StringComparison.Ordinal);
        }
    }

    public OperatorContext GetDefaultContext()
    {
        lock (_store.Gate)
        {
            return _store.State.DefaultContext.Clone();
        }
    }

    public OperatorContext SetDefaultContext(OperatorContext context)
    {
        lock (_store.Gate)
        {
            _store.State.DefaultContext = context.Clone();
            _store.State.LastSyncAt = TraceHelpers.NowIso();
            return _store.State.DefaultContext.Clone();
        }
    }

    private MasterListsResponse GetMasterListsLocked(AppConfig config)
    {
        return new MasterListsResponse
        {
            Keys = MasterKeys.ToList(),
            Labels = new Dictionary<string, string>(MasterLabels, StringComparer.Ordinal),
            Lists = ReadLists(config),
        };
    }

    private static Dictionary<string, List<string>> ReadLists(AppConfig config)
    {
        return new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["skus"] = config.Skus,
            ["materials"] = config.Materials,
            ["operators"] = config.Operators,
            ["lines"] = config.Lines,
            ["shifts"] = config.Shifts,
            ["destinations"] = config.Destinations,
            ["reworks"] = config.Reworks,
        };
    }

    private static List<string>? ResolveList(AppConfig config, string key)
    {
        return key switch
        {
            "skus" => config.Skus,
            "materials" => config.Materials,
            "operators" => config.Operators,
            "lines" => config.Lines,
            "shifts" => config.Shifts,
            "destinations" => config.Destinations,
            "reworks" => config.Reworks,
            _ => null,
        };
    }

    private static PublicConfig ToPublic(AppConfig config)
    {
        return new PublicConfig
        {
            BrowserTitle = config.BrowserTitle,
            CompanyName = config.CompanyName,
            Subtitle = config.Subtitle,
            HeroTitle = config.HeroTitle,
            BtnLoginText = config.BtnLoginText,
            BtnDbText = config.BtnDbText,
            LoginTitle = config.LoginTitle,
            LoginSubmitText = config.LoginSubmitText,
            FooterTopText = config.FooterTopText,
            FooterBottomText = config.FooterBottomText,
            LogoDataUrl = config.LogoDataUrl,
            AccentColor = config.AccentColor,
            UiScanOkTitle = config.UiScanOkTitle,
            UiScanStatusBadge = config.UiScanStatusBadge,
            UiScanNgTitle = config.UiScanNgTitle,
            UiScanRwTitle = config.UiScanRwTitle,
            UiDbHeaderTitle = config.UiDbHeaderTitle,
            UiDbSearchTitle = config.UiDbSearchTitle,
            UiDbTableTitle = config.UiDbTableTitle,
            UiReportTitle = config.UiReportTitle,
            InnerCodeFormat = config.InnerCodeFormat,
            OuterCodeFormat = config.OuterCodeFormat,
            ReworkCodeFormat = config.ReworkCodeFormat,
            SeqPadding = config.SeqPadding,
            SnPattern = config.SnPattern,
            Barcode2DType = config.Barcode2DType,
            Barcode2DSize = config.Barcode2DSize,
            LabelWidthMm = config.LabelWidthMm,
            LabelHeightMm = config.LabelHeightMm,
            BarcodeDelimiter = config.BarcodeDelimiter,
            BarcodePayloadFields = config.BarcodePayloadFields,
            ExternalPrinterMode = config.ExternalPrinterMode,
            WebhookUrl = config.WebhookUrl,
            AutoBridgePrint = config.AutoBridgePrint,
            InnerCap = config.InnerCap,
            OuterCap = config.OuterCap,
            MaxReworkCycles = config.MaxReworkCycles,
            Skus = config.Skus,
            Materials = config.Materials,
            Operators = config.Operators,
            Lines = config.Lines,
            Shifts = config.Shifts,
            Destinations = config.Destinations,
            Reworks = config.Reworks,
            FaviconEmoji = config.FaviconEmoji,
            Colors = config.Colors,
            LabelInnerTitle = config.LabelInnerTitle,
            LabelOuterTitle = config.LabelOuterTitle,
            DailyReset = config.DailyReset,
            NgStations = config.NgStations,
            ColumnLabels = config.ColumnLabels,
            ExportTemplates = config.ExportTemplates,
            SelectedColumns = config.SelectedColumns,
        };
    }
}
