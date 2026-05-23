using System.Text.Json;
using RTPCCurveEditor.Models;

namespace RTPCCurveEditor.Services;

public static class JsonExportService
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Export the primary curve as a flat array of {x, y} samples.
    /// Suitable for SoundBridge and custom tooling.
    /// </summary>
    public static string ExportSamples(CurveDocument doc, int sampleCount = 64)
    {
        var curve = doc.PrimaryCurve;
        var samples = curve.GetPolyline(sampleCount)
            .Select(s => new
            {
                x         = Math.Round(s.X, 4),
                y         = Math.Round(s.Y, 4),
                xMapped   = Math.Round(Lerp(doc.InputMin,  doc.InputMax,  s.X), 4),
                yMapped   = Math.Round(Lerp(doc.OutputMin, doc.OutputMax, s.Y), 4)
            });

        var payload = new
        {
            curveName  = curve.Name,
            rtpcName   = doc.WwiseRtpcName,
            inputMin   = doc.InputMin,
            inputMax   = doc.InputMax,
            outputMin  = doc.OutputMin,
            outputMax  = doc.OutputMax,
            sampleCount,
            samples
        };

        return JsonSerializer.Serialize(payload, _opts);
    }

    /// <summary>Full document export including all curves and anchor points.</summary>
    public static string ExportDocument(CurveDocument doc) =>
        JsonSerializer.Serialize(doc, _opts);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
