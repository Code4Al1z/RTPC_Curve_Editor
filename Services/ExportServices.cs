using System.Text.Json;
using SkiaSharp;
using RTPCCurveEditor.Models;
using System.IO;

namespace RTPCCurveEditor.Services;

// ── PNG Export ────────────────────────────────────────────────────────────────

public static class PngExportService
{
    public static void Export(BezierCurve curve, string filePath,
        int width = 1200, int height = 800)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;

        // Background
        canvas.Clear(new SKColor(18, 18, 24));

        // Grid
        using var gridPaint = new SKPaint
        {
            Color       = new SKColor(255, 255, 255, 25),
            StrokeWidth = 1,
            IsAntialias = true
        };
        int gridLines = 8;
        for (int i = 0; i <= gridLines; i++)
        {
            float gx = (float)i / gridLines * width;
            float gy = (float)i / gridLines * height;
            canvas.DrawLine(gx, 0, gx, height, gridPaint);
            canvas.DrawLine(0, gy, width, gy, gridPaint);
        }

        // Curve
        SKColor curveColor = SKColor.Parse(curve.ColorHex);
        using var curvePaint = new SKPaint
        {
            Color       = curveColor,
            StrokeWidth = 3,
            IsAntialias = true,
            IsStroke    = true,
            StrokeCap   = SKStrokeCap.Round,
            StrokeJoin  = SKStrokeJoin.Round
        };

        var polyline = curve.GetPolyline(400);
        using var path = new SKPath();
        bool first = true;
        foreach (var (x, y) in polyline)
        {
            float px = (float)(x * (width  - 80)) + 40;
            float py = (float)((1 - y) * (height - 80)) + 40;
            if (first) { path.MoveTo(px, py); first = false; }
            else path.LineTo(px, py);
        }
        canvas.DrawPath(path, curvePaint);

        // Fill under curve
        using var fillPath = new SKPath(path);
        fillPath.LineTo((float)(width - 40), (float)(height - 40));
        fillPath.LineTo(40, (float)(height - 40));
        fillPath.Close();
        using var fillPaint = new SKPaint
        {
            Color    = curveColor.WithAlpha(40),
            IsStroke = false
        };
        canvas.DrawPath(fillPath, fillPaint);

        // Anchor points
        using var pointPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        foreach (var pt in curve.Points)
        {
            float px = (float)(pt.X * (width  - 80)) + 40;
            float py = (float)((1 - pt.Y) * (height - 80)) + 40;
            canvas.DrawCircle(px, py, 6, pointPaint);
        }

        // Label
        using var labelFont = new SKFont(SKTypeface.Default, 28);
        using var labelPaint = new SKPaint { Color = new SKColor(200, 200, 200), IsAntialias = true };
        canvas.DrawText(curve.Name, 44, 36, SKTextAlign.Left, labelFont, labelPaint);

        using var image = surface.Snapshot();
        using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(filePath);
        data.SaveTo(stream);
    }
}

// ── Project File Service ──────────────────────────────────────────────────────

public static class ProjectFileService
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task SaveAsync(CurveDocument doc, string filePath)
    {
        doc.ModifiedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(doc, _opts);
        await File.WriteAllTextAsync(filePath, json);
    }

    public static async Task<CurveDocument> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<CurveDocument>(json, _opts)
               ?? CurveDocument.CreateDefault();
    }
}
