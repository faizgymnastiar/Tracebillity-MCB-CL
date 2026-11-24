using System.Text.Json;

namespace Traceability.Domain;

public static class BarcodeRules
{
    public const string DefaultPayloadFields = "{BOX_ID}|{SKU}|{WO}|{LOT}|{QTY}|{DEST}|{PRINT_TIME}";

    private static readonly Dictionary<string, string> BridgeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BARTENDER_CSV"] = "csv",
        ["JSON_API"] = "json",
        ["ZPL_RAW"] = "zpl",
        ["TSPL_RAW"] = "prn",
    };

    private static readonly JsonSerializerOptions BridgeJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Build2DPayload(BarcodeTarget target, AppConfig config)
    {
        var delimiter = string.IsNullOrEmpty(config.BarcodeDelimiter) ? "|" : config.BarcodeDelimiter;
        var fields = string.IsNullOrWhiteSpace(config.BarcodePayloadFields)
            ? DefaultPayloadFields
            : config.BarcodePayloadFields;
        var template = delimiter == "|" ? fields : fields.Replace("|", delimiter, StringComparison.Ordinal);
        return template
            .Replace("{BOX_ID}", Value(target.BoxId), StringComparison.Ordinal)
            .Replace("{SKU}", Value(target.Sku), StringComparison.Ordinal)
            .Replace("{WO}", Value(target.WorkOrder), StringComparison.Ordinal)
            .Replace("{LOT}", CodeFormatter.NormalizeMaterialLotCode(target.MaterialLot), StringComparison.Ordinal)
            .Replace("{QTY}", Value(target.QtyText), StringComparison.Ordinal)
            .Replace("{DEST}", Value(target.Destination), StringComparison.Ordinal)
            .Replace("{LINE}", Value(target.Line), StringComparison.Ordinal)
            .Replace("{OPERATOR}", Value(target.Operator), StringComparison.Ordinal)
            .Replace("{PRINT_TIME}", Value(target.PrintTime), StringComparison.Ordinal);
    }

    public static string BuildBridgeContent(string mode, BarcodeTarget target, AppConfig config, string payload)
    {
        var lot = CodeFormatter.NormalizeMaterialLotCode(target.MaterialLot);

        if (string.Equals(mode, "JSON_API", StringComparison.OrdinalIgnoreCase))
        {
            var document = new
            {
                printerTarget = config.WebhookUrl,
                labelDimensionMm = new { width = config.LabelWidthMm, height = config.LabelHeightMm },
                symbology = config.Barcode2DType,
                data = new
                {
                    boxId = target.BoxId,
                    sku = target.Sku,
                    wo = target.WorkOrder,
                    lot,
                    qty = target.QtyText,
                    payload2D = payload,
                    printedAt = target.PrintTime,
                },
            };
            return JsonSerializer.Serialize(document, BridgeJsonOptions);
        }

        if (string.Equals(mode, "ZPL_RAW", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join(
                "\n",
                "^XA",
                $"^PW{config.LabelWidthMm * 8}",
                $"^FO40,30^A0N,30,30^FD{config.CompanyName}^FS",
                $"^FO40,80^BQN,2,5^FDQA,{payload}^FS",
                $"^FO240,85^A0N,26,26^FDBOX: {target.BoxId}^FS",
                $"^FO240,120^A0N,24,24^FDSKU: {target.Sku}^FS",
                $"^FO240,155^A0N,24,24^FDWO: {target.WorkOrder}^FS",
                "^XZ");
        }

        if (string.Equals(mode, "TSPL_RAW", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join(
                "\n",
                $"SIZE {config.LabelWidthMm} mm, {config.LabelHeightMm} mm",
                "CLS",
                $"QRCODE 40,60,H,5,A,0,\"{payload}\"",
                $"TEXT 240,70,\"3\",0,1,1,\"{target.BoxId}\"",
                $"TEXT 240,110,\"2\",0,1,1,\"SKU: {target.Sku}\"",
                "PRINT 1,1");
        }

        const string header = "BOX_ID,SKU,WORK_ORDER,MATERIAL_LOT,QTY,PRINT_TIME,BARCODE_2D_PAYLOAD";
        var row = $"\"{target.BoxId}\",\"{target.Sku}\",\"{target.WorkOrder}\",\"{lot}\",\"{target.QtyText}\",\"{target.PrintTime}\",\"{payload}\"";
        return $"{header}\n{row}";
    }

    public static BridgePayload BuildBridgePayload(string mode, BarcodeTarget target, AppConfig config)
    {
        var payload2d = Build2DPayload(target, config);
        var extension = BridgeExtensions.TryGetValue(mode, out var found) ? found : "csv";
        return new BridgePayload
        {
            Mode = mode,
            Filename = $"LELCO_PrintBridge_{target.BoxId}.{extension}",
            Content = BuildBridgeContent(mode, target, config, payload2d),
            Payload2d = payload2d,
            BoxId = target.BoxId,
        };
    }

    private static string Value(string? value)
    {
        return string.IsNullOrEmpty(value) ? "-" : value;
    }
}
