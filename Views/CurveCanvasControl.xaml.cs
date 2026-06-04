using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using SkiaSharp.Views.Desktop;
using RTPCCurveEditor.Models;
using RTPCCurveEditor.ViewModels;

namespace RTPCCurveEditor.Views;

public partial class CurveCanvasControl : UserControl
{
    // ── Constants ─────────────────────────────────────────────────────────
    private const float CanvasPadding = 52f;
    private const float PointRadius = 6f;
    private const float HandleRadius = 4f;
    private const float HitRadius = 10f;
    private const float CurveHitDist = 8f;

    // ── State ─────────────────────────────────────────────────────────────
    private MainViewModel? VM => DataContext as MainViewModel;
    private CurvePoint? _draggingPoint;
    private bool _draggingHandle;
    private bool _draggingRightHandle;
    private bool _hasDragged;
    private SKPoint _mouseDownPos;
    private SKPoint _lastMouseCanvas;
    private float _zoom = 1f;
    private SKPoint _pan = SKPoint.Empty;

    public CurveCanvasControl()
    {
        InitializeComponent();
        MouseDoubleClick += OnMouseDoubleClick;
    }

    public void Redraw() => SkiaElement.InvalidateVisual();

    // ── Coordinate helpers ────────────────────────────────────────────────

    private float CanvasWidth => (float)SkiaElement.ActualWidth;
    private float CanvasHeight => (float)SkiaElement.ActualHeight;
    private float PlotW => (CanvasWidth - CanvasPadding * 2) * _zoom;
    private float PlotH => (CanvasHeight - CanvasPadding * 2) * _zoom;

    private SKPoint ToCanvas(double nx, double ny) => new(
        CanvasPadding + (float)nx * PlotW + _pan.X,
        CanvasHeight - CanvasPadding - (float)ny * PlotH + _pan.Y
    );

    private (double nx, double ny) ToNorm(SKPoint p) => (
        (p.X - CanvasPadding - _pan.X) / PlotW,
        1.0 - (p.Y - (CanvasHeight - CanvasPadding - PlotH) - _pan.Y) / PlotH
    );

    private (double dnx, double dny) DeltaToNorm(SKPoint delta) =>
        (delta.X / PlotW, -delta.Y / PlotH);

    // ── Hit testing ───────────────────────────────────────────────────────

    private CurvePoint? HitPoint(SKPoint pos, BezierCurve curve)
    {
        foreach (var pt in curve.Points)
            if (SKPoint.Distance(pos, ToCanvas(pt.X, pt.Y)) < HitRadius)
                return pt;
        return null;
    }

    /// Returns the index of the segment the pos falls on, or -1.
    /// Segment i is between Points[i] and Points[i+1] (sorted by X).
    private int HitSegment(SKPoint pos, BezierCurve curve)
    {
        var sorted = curve.Points.OrderBy(p => p.X).ToList();
        var poly = curve.GetPolyline(200);

        // Find which pair of anchor points the hit polyline segment belongs to
        for (int i = 0; i < poly.Count - 1; i++)
        {
            var a = ToCanvas(poly[i].X, poly[i].Y);
            var b = ToCanvas(poly[i + 1].X, poly[i + 1].Y);
            if (DistPointToSegment(pos, a, b) < CurveHitDist)
            {
                // Find which anchor segment this polyline segment belongs to
                double midX = (poly[i].X + poly[i + 1].X) / 2.0;
                for (int s = 0; s < sorted.Count - 1; s++)
                {
                    if (midX >= sorted[s].X && midX <= sorted[s + 1].X)
                        return s;
                }
            }
        }
        return -1;
    }

    private bool HitAnyCurve(SKPoint pos, BezierCurve curve)
    {
        var poly = curve.GetPolyline(200);
        for (int i = 0; i < poly.Count - 1; i++)
        {
            var a = ToCanvas(poly[i].X, poly[i].Y);
            var b = ToCanvas(poly[i + 1].X, poly[i + 1].Y);
            if (DistPointToSegment(pos, a, b) < CurveHitDist)
                return true;
        }
        return false;
    }

    private static float DistPointToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        var ab = b - a;
        float len = ab.X * ab.X + ab.Y * ab.Y;
        if (len < 1e-6f) return SKPoint.Distance(p, a);
        float t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len, 0, 1);
        return SKPoint.Distance(p, new SKPoint(a.X + t * ab.X, a.Y + t * ab.Y));
    }

    // ── Colour helpers ────────────────────────────────────────────────────

    /// Returns a brightened version of the curve colour for selected segments/points.
    private static SKColor BrightenColour(SKColor c)
    {
        // Convert to HSL, boost lightness and saturation, convert back
        RgbToHsl(c.Red, c.Green, c.Blue, out float h, out float s, out float l);
        s = Math.Min(1f, s + 0.25f);
        l = Math.Min(1f, l + 0.30f);
        HslToRgb(h, s, l, out float r, out float g, out float b);
        return new SKColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), c.Alpha);
    }

    private static void RgbToHsl(byte r, byte g, byte b,
        out float h, out float s, out float l)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        l = (max + min) / 2f;
        if (max == min) { h = s = 0; return; }
        float d = max - min;
        s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
        if (max == rf) h = (gf - bf) / d + (gf < bf ? 6 : 0);
        else if (max == gf) h = (bf - rf) / d + 2;
        else h = (rf - gf) / d + 4;
        h /= 6f;
    }

    private static void HslToRgb(float h, float s, float l,
        out float r, out float g, out float b)
    {
        if (s == 0) { r = g = b = l; return; }
        float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
        float p = 2 * l - q;
        r = HueToRgb(p, q, h + 1f / 3);
        g = HueToRgb(p, q, h);
        b = HueToRgb(p, q, h - 1f / 3);
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1f / 6) return p + (q - p) * 6 * t;
        if (t < 1f / 2) return q;
        if (t < 2f / 3) return p + (q - p) * (2f / 3 - t) * 6;
        return p;
    }

    // ── Paint ─────────────────────────────────────────────────────────────

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(23, 23, 31));
        if (VM == null) return;

        DrawGrid(canvas);
        DrawAxes(canvas);

        foreach (var curve in VM.Document.Curves.Where(c => c != VM.ActiveCurve && c.IsVisible))
            DrawCurve(canvas, curve, alpha: 60, isActive: false);

        DrawCurve(canvas, VM.ActiveCurve, alpha: 255, isActive: true);
        DrawPoints(canvas, VM.ActiveCurve);
    }

    private void DrawGrid(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 18),
            StrokeWidth = 1,
            IsAntialias = false
        };
        for (int i = 0; i <= 8; i++)
        {
            double t = (double)i / 8;
            var h0 = ToCanvas(t, 0); var h1 = ToCanvas(t, 1);
            var v0 = ToCanvas(0, t); var v1 = ToCanvas(1, t);
            canvas.DrawLine(h0.X, h0.Y, h1.X, h1.Y, paint);
            canvas.DrawLine(v0.X, v0.Y, v1.X, v1.Y, paint);
        }
    }

    private void DrawAxes(SKCanvas canvas)
    {
        using var axisPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 55),
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        using var labelFont = new SKFont(SKTypeface.Default, 11);
        using var labelPaint = new SKPaint { Color = new SKColor(138, 136, 160), IsAntialias = true };

        canvas.DrawLine(ToCanvas(0, 0), ToCanvas(1, 0), axisPaint);
        canvas.DrawLine(ToCanvas(0, 0), ToCanvas(0, 1), axisPaint);

        for (int i = 0; i <= 5; i++)
        {
            double t = (double)i / 5;
            double xVal = VM!.Document.InputMin + t * (VM.Document.InputMax - VM.Document.InputMin);
            double yVal = VM!.Document.OutputMin + t * (VM.Document.OutputMax - VM.Document.OutputMin);
            var xPos = ToCanvas(t, 0);
            var yPos = ToCanvas(0, t);
            canvas.DrawText(xVal.ToString("F0"), xPos.X - 8, xPos.Y + 16, SKTextAlign.Left, labelFont, labelPaint);
            canvas.DrawText(yVal.ToString("F2"), 4, yPos.Y + 4, SKTextAlign.Left, labelFont, labelPaint);
        }
    }

    private void DrawCurve(SKCanvas canvas, BezierCurve curve, byte alpha, bool isActive)
    {
        if (curve.Points.Count < 2) return;

        var baseColor = SKColor.Parse(curve.ColorHex).WithAlpha(alpha);
        var highlightColor = BrightenColour(baseColor);
        var sorted = curve.Points.OrderBy(p => p.X).ToList();

        // Draw fill once for the whole curve
        var poly = curve.GetPolyline(300);
        using var fillPath = new SKPath();
        fillPath.MoveTo(ToCanvas(poly[0].X, poly[0].Y));
        foreach (var (x, y) in poly.Skip(1)) fillPath.LineTo(ToCanvas(x, y));
        fillPath.LineTo(ToCanvas(poly[^1].X, 0));
        fillPath.LineTo(ToCanvas(poly[0].X, 0));
        fillPath.Close();
        using var fillPaint = new SKPaint
        {
            Color = baseColor.WithAlpha((byte)(alpha / 8)),
            IsStroke = false
        };
        canvas.DrawPath(fillPath, fillPaint);

        // Glow for active curve
        if (isActive)
        {
            using var glowPath = new SKPath();
            glowPath.MoveTo(ToCanvas(poly[0].X, poly[0].Y));
            foreach (var (x, y) in poly.Skip(1)) glowPath.LineTo(ToCanvas(x, y));
            using var glowPaint = new SKPaint
            {
                Color = baseColor.WithAlpha(30),
                StrokeWidth = 8f,
                IsAntialias = true,
                IsStroke = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4)
            };
            canvas.DrawPath(glowPath, glowPaint);
        }

        // Draw each segment individually so selected ones can be highlighted
        for (int s = 0; s < sorted.Count - 1; s++)
        {
            var p0 = sorted[s];
            var p1 = sorted[s + 1];

            // A segment is "selected" when both its endpoints are selected
            bool segSelected = isActive && p0.IsSelected && p1.IsSelected;
            SKColor strokeColor = segSelected ? highlightColor : baseColor;
            float strokeWidth = segSelected ? 3.5f : (isActive ? 2.5f : 1.5f);

            // Sample just this segment
            int steps = 60;
            using var segPath = new SKPath();
            bool first = true;
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double x = p0.X + t * (p1.X - p0.X);
                double y = curve.Sample(x);
                var cp = ToCanvas(x, y);
                if (first) { segPath.MoveTo(cp); first = false; }
                else segPath.LineTo(cp);
            }

            using var segPaint = new SKPaint
            {
                Color = strokeColor,
                StrokeWidth = strokeWidth,
                IsAntialias = true,
                IsStroke = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };
            canvas.DrawPath(segPath, segPaint);
        }
    }

    private void DrawPoints(SKCanvas canvas, BezierCurve curve)
    {
        var baseColor = SKColor.Parse(curve.ColorHex);
        var highlightColor = BrightenColour(baseColor);

        using var handleLinePaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 50),
            StrokeWidth = 1,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0)
        };
        using var handleDotPaint = new SKPaint
        {
            Color = new SKColor(138, 136, 160),
            IsAntialias = true
        };

        foreach (var pt in curve.Points)
        {
            var cp = ToCanvas(pt.X, pt.Y);
            var ptColor = pt.IsSelected ? highlightColor : baseColor;

            if (pt.IsSelected)
            {
                // Draw handles only for the single SelectedPoint to avoid clutter
                if (pt == (DataContext as MainViewModel)?.SelectedPoint)
                {
                    var lh = ToCanvas(pt.X + pt.LeftHandleX, pt.Y + pt.LeftHandleY);
                    var rh = ToCanvas(pt.X + pt.RightHandleX, pt.Y + pt.RightHandleY);
                    canvas.DrawLine(cp, lh, handleLinePaint);
                    canvas.DrawLine(cp, rh, handleLinePaint);
                    canvas.DrawCircle(lh, HandleRadius, handleDotPaint);
                    canvas.DrawCircle(rh, HandleRadius, handleDotPaint);
                }

                using var ringPaint = new SKPaint
                {
                    Color = highlightColor,
                    IsStroke = true,
                    StrokeWidth = 2,
                    IsAntialias = true
                };
                using var fillPaint = new SKPaint { Color = highlightColor, IsAntialias = true };
                canvas.DrawCircle(cp, PointRadius + 2, ringPaint);
                canvas.DrawCircle(cp, PointRadius, fillPaint);
            }
            else
            {
                using var fillPaint = new SKPaint { Color = ptColor, IsAntialias = true };
                canvas.DrawCircle(cp, PointRadius, fillPaint);
            }
        }
    }

    // ── Mouse: Left button ────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (VM == null) return;
        SkiaElement.CaptureMouse();
        var pos = ToSKPoint(e.GetPosition(SkiaElement));
        bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        _mouseDownPos = pos;
        _lastMouseCanvas = pos;
        _draggingPoint = null;
        _draggingHandle = false;
        _hasDragged = false;

        // Handle hit (only for the single focus point)
        if (VM.SelectedPoint != null && !ctrl)
        {
            var pt = VM.SelectedPoint;
            var rh = ToCanvas(pt.X + pt.RightHandleX, pt.Y + pt.RightHandleY);
            var lh = ToCanvas(pt.X + pt.LeftHandleX, pt.Y + pt.LeftHandleY);
            if (SKPoint.Distance(pos, rh) < HitRadius)
            {
                _draggingHandle = true; _draggingRightHandle = true; _draggingPoint = pt; return;
            }
            if (SKPoint.Distance(pos, lh) < HitRadius)
            {
                _draggingHandle = true; _draggingRightHandle = false; _draggingPoint = pt; return;
            }
        }

        // Point hit
        var hitPt = HitPoint(pos, VM.ActiveCurve);
        if (hitPt != null)
        {
            if (ctrl)
            {
                // Ctrl+click toggles selection of this point
                hitPt.IsSelected = !hitPt.IsSelected;
                VM.SelectedPoint = hitPt.IsSelected ? hitPt : null;
            }
            else
            {
                // Plain click: clear all, select only this one
                VM.ClearPointSelection();
                hitPt.IsSelected = true;
                VM.SelectedPoint = hitPt;
                _draggingPoint = hitPt;
            }
            Redraw();
            return;
        }

        // Segment hit
        var segIdx = HitSegment(pos, VM.ActiveCurve);
        if (segIdx >= 0)
        {
            var sorted = VM.ActiveCurve.Points.OrderBy(p => p.X).ToList();
            var p0 = sorted[segIdx];
            var p1 = sorted[segIdx + 1];

            if (ctrl)
            {
                // Ctrl+click segment: toggle both endpoints
                bool willSelect = !(p0.IsSelected && p1.IsSelected);
                p0.IsSelected = willSelect;
                p1.IsSelected = willSelect;
            }
            else
            {
                // Plain click segment: clear all, select both endpoints
                VM.ClearPointSelection();
                p0.IsSelected = true;
                p1.IsSelected = true;
                VM.SelectedPoint = p0;
            }
            Redraw();
            return;
        }

        // Click on comparison curve line → switch active curve
        foreach (var curve in VM.Document.Curves)
        {
            if (!curve.IsVisible || curve == VM.ActiveCurve) continue;
            if (HitAnyCurve(pos, curve))
            {
                VM.SetActiveCurveCommand.Execute(curve);
                VM.ClearPointSelection();
                Redraw();
                return;
            }
        }

        // Click empty space (no ctrl) → deselect all
        if (!ctrl)
        {
            VM.ClearPointSelection();
            Redraw();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (VM == null) return;
        var pos = ToSKPoint(e.GetPosition(SkiaElement));
        var delta = new SKPoint(pos.X - _lastMouseCanvas.X, pos.Y - _lastMouseCanvas.Y);
        _lastMouseCanvas = pos;

        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (!_hasDragged && SKPoint.Distance(pos, _mouseDownPos) > 3f) _hasDragged = true;
        if (!_hasDragged) return;

        if (_draggingHandle && _draggingPoint != null)
        {
            var (dnx, dny) = DeltaToNorm(delta);
            if (_draggingRightHandle)
            { _draggingPoint.RightHandleX += dnx; _draggingPoint.RightHandleY += dny; }
            else
            { _draggingPoint.LeftHandleX += dnx; _draggingPoint.LeftHandleY += dny; }
            Redraw();
            return;
        }

        if (_draggingPoint != null)
        {
            var (dnx, dny) = DeltaToNorm(delta);
            _draggingPoint.X = Math.Clamp(_draggingPoint.X + dnx, 0, 1);
            _draggingPoint.Y = Math.Clamp(_draggingPoint.Y + dny, 0, 1);
            Redraw();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        SkiaElement.ReleaseMouseCapture();
        _draggingPoint = null;
        _draggingHandle = false;
    }

    // ── Double-click: add / remove ────────────────────────────────────────

    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VM == null) return;
        var pos = ToSKPoint(e.GetPosition(SkiaElement));

        var hit = HitPoint(pos, VM.ActiveCurve);
        if (hit != null)
        {
            VM.SelectedPoint = hit;
            VM.DeleteSelectedPoint();
            Redraw();
            return;
        }

        var (nx, ny) = ToNorm(pos);
        if (nx >= 0 && nx <= 1 && ny >= 0 && ny <= 1)
        {
            VM.AddPoint(nx, ny);
            Redraw();
        }
    }

    // ── Keyboard: Ctrl+A ──────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (VM == null) return;

        if (e.Key == Key.A &&
            (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
        {
            foreach (var pt in VM.ActiveCurve.Points)
                pt.IsSelected = true;
            VM.SelectedPoint = VM.ActiveCurve.Points.FirstOrDefault();
            Redraw();
            e.Handled = true;
        }
    }

    // ── Right-click ───────────────────────────────────────────────────────

    private void OnRightMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    // ── Wheel: zoom ───────────────────────────────────────────────────────

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        float factor = e.Delta > 0 ? 1.1f : 0.9f;
        _zoom = Math.Clamp(_zoom * factor, 0.5f, 4f);
        Redraw();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static SKPoint ToSKPoint(Point p) => new((float)p.X, (float)p.Y);
}