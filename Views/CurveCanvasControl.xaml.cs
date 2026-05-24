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
    private const float CurveHitDist = 8f;   // px distance to count as "on the curve"

    // ── State ─────────────────────────────────────────────────────────────
    private MainViewModel? VM => DataContext as MainViewModel;
    private CurvePoint? _draggingPoint;
    private bool _draggingHandle;
    private bool _draggingRightHandle;
    private bool _hasDragged;           // distinguish click vs drag
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

    /// Returns the nearest CurvePoint within HitRadius, or null.
    private CurvePoint? HitPoint(SKPoint pos, BezierCurve curve)
    {
        foreach (var pt in curve.Points)
        {
            if (SKPoint.Distance(pos, ToCanvas(pt.X, pt.Y)) < HitRadius)
                return pt;
        }
        return null;
    }

    /// Returns true if pos is within CurveHitDist pixels of the curve polyline.
    private bool HitCurve(SKPoint pos, BezierCurve curve)
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
        float lenSq = ab.X * ab.X + ab.Y * ab.Y;
        if (lenSq < 1e-6f) return SKPoint.Distance(p, a);
        float t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lenSq, 0, 1);
        var proj = new SKPoint(a.X + t * ab.X, a.Y + t * ab.Y);
        return SKPoint.Distance(p, proj);
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
        SKColor c = SKColor.Parse(curve.ColorHex).WithAlpha(alpha);

        var poly = curve.GetPolyline(300);

        // Fill
        using var fillPath = new SKPath();
        fillPath.MoveTo(ToCanvas(poly[0].X, poly[0].Y));
        foreach (var (x, y) in poly.Skip(1)) fillPath.LineTo(ToCanvas(x, y));
        fillPath.LineTo(ToCanvas(poly[^1].X, 0));
        fillPath.LineTo(ToCanvas(poly[0].X, 0));
        fillPath.Close();
        using var fillPaint = new SKPaint { Color = c.WithAlpha((byte)(alpha / 8)), IsStroke = false };
        canvas.DrawPath(fillPath, fillPaint);

        // Stroke — glow ring for active curve
        if (isActive)
        {
            using var glowPath = new SKPath();
            glowPath.MoveTo(ToCanvas(poly[0].X, poly[0].Y));
            foreach (var (x, y) in poly.Skip(1)) glowPath.LineTo(ToCanvas(x, y));
            using var glowPaint = new SKPaint
            {
                Color = c.WithAlpha(30),
                StrokeWidth = 8f,
                IsAntialias = true,
                IsStroke = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4)
            };
            canvas.DrawPath(glowPath, glowPaint);
        }

        using var linePath = new SKPath();
        linePath.MoveTo(ToCanvas(poly[0].X, poly[0].Y));
        foreach (var (x, y) in poly.Skip(1)) linePath.LineTo(ToCanvas(x, y));
        using var strokePaint = new SKPaint
        {
            Color = c,
            StrokeWidth = isActive ? 2.5f : 1.5f,
            IsAntialias = true,
            IsStroke = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        canvas.DrawPath(linePath, strokePaint);
    }

    private void DrawPoints(SKCanvas canvas, BezierCurve curve)
    {
        using var handleLinePaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 50),
            StrokeWidth = 1,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0)
        };
        using var handleDotPaint = new SKPaint { Color = new SKColor(138, 136, 160), IsAntialias = true };
        using var pointFill = new SKPaint { Color = SKColor.Parse(curve.ColorHex), IsAntialias = true };
        using var pointWhite = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var pointRing = new SKPaint
        {
            Color = SKColor.Parse(curve.ColorHex),
            IsStroke = true,
            StrokeWidth = 2,
            IsAntialias = true
        };

        foreach (var pt in curve.Points)
        {
            var cp = ToCanvas(pt.X, pt.Y);

            if (pt.IsSelected)
            {
                var lh = ToCanvas(pt.X + pt.LeftHandleX, pt.Y + pt.LeftHandleY);
                var rh = ToCanvas(pt.X + pt.RightHandleX, pt.Y + pt.RightHandleY);
                canvas.DrawLine(cp, lh, handleLinePaint);
                canvas.DrawLine(cp, rh, handleLinePaint);
                canvas.DrawCircle(lh, HandleRadius, handleDotPaint);
                canvas.DrawCircle(rh, HandleRadius, handleDotPaint);
                canvas.DrawCircle(cp, PointRadius + 2, pointRing);
                canvas.DrawCircle(cp, PointRadius, pointWhite);
            }
            else
            {
                canvas.DrawCircle(cp, PointRadius, pointFill);
            }
        }
    }

    // ── Mouse: Left button ────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (VM == null) return;
        SkiaElement.CaptureMouse();
        var pos = ToSKPoint(e.GetPosition(SkiaElement));
        _mouseDownPos = pos;
        _lastMouseCanvas = pos;
        _draggingPoint = null;
        _draggingHandle = false;
        _hasDragged = false;

        // Check handle hit first (only if a point is selected)
        if (VM.SelectedPoint != null)
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

        // Check point hit → select and prepare drag
        var hit = HitPoint(pos, VM.ActiveCurve);
        if (hit != null)
        {
            foreach (var p in VM.ActiveCurve.Points) p.IsSelected = false;
            hit.IsSelected = true;
            VM.SelectedPoint = hit;
            _draggingPoint = hit;
            Redraw();
            return;
        }

        // Click on curve line → select the active curve (no-op if already active),
        // or switch active curve if clicking a comparison curve
        foreach (var curve in VM.Document.Curves)
        {
            if (!curve.IsVisible) continue;
            if (HitCurve(pos, curve))
            {
                if (curve != VM.ActiveCurve)
                    VM.SetActiveCurveCommand.Execute(curve);
                // Deselect any point
                foreach (var p in VM.ActiveCurve.Points) p.IsSelected = false;
                VM.SelectedPoint = null;
                Redraw();
                return;
            }
        }

        // Click on empty space → deselect
        foreach (var p in VM.ActiveCurve.Points) p.IsSelected = false;
        VM.SelectedPoint = null;
        Redraw();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (VM == null) return;
        var pos = ToSKPoint(e.GetPosition(SkiaElement));
        var delta = new SKPoint(pos.X - _lastMouseCanvas.X, pos.Y - _lastMouseCanvas.Y);
        _lastMouseCanvas = pos;

        if (e.LeftButton != MouseButtonState.Pressed) return;

        // Mark as dragged if moved more than 3px
        if (!_hasDragged && SKPoint.Distance(pos, _mouseDownPos) > 3f)
            _hasDragged = true;

        if (!_hasDragged) return;

        if (_draggingHandle && _draggingPoint != null)
        {
            var (dnx, dny) = DeltaToNorm(delta);
            if (_draggingRightHandle) { _draggingPoint.RightHandleX += dnx; _draggingPoint.RightHandleY += dny; }
            else { _draggingPoint.LeftHandleX += dnx; _draggingPoint.LeftHandleY += dny; }
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

    // ── Mouse: Double-click (add / remove points) ─────────────────────────

    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VM == null) return;
        var pos = ToSKPoint(e.GetPosition(SkiaElement));

        // Double-click on existing point → remove it
        var hit = HitPoint(pos, VM.ActiveCurve);
        if (hit != null)
        {
            VM.SelectedPoint = hit;
            VM.DeleteSelectedPoint();
            Redraw();
            return;
        }

        // Double-click on empty canvas → add point
        var (nx, ny) = ToNorm(pos);
        if (nx >= 0 && nx <= 1 && ny >= 0 && ny <= 1)
        {
            VM.AddPoint(nx, ny);
            Redraw();
        }
    }

    // ── Mouse: Right-click (context menu hook, reserved) ─────────────────

    private void OnRightMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Reserved for future context menu — no behaviour for now
        e.Handled = true;
    }

    // ── Mouse: Wheel (zoom) ───────────────────────────────────────────────

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        float factor = e.Delta > 0 ? 1.1f : 0.9f;
        _zoom = Math.Clamp(_zoom * factor, 0.5f, 4f);
        Redraw();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static SKPoint ToSKPoint(Point p) => new((float)p.X, (float)p.Y);
}