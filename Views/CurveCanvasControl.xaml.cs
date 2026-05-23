using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using RTPCCurveEditor.Models;
using RTPCCurveEditor.ViewModels;
using SkiaSharp.Views.Desktop;

namespace RTPCCurveEditor.Views;

public partial class CurveCanvasControl : UserControl
{
    // ── Constants ─────────────────────────────────────────────────────────
    private const float Padding    = 52f;
    private const float PointRadius = 6f;
    private const float HandleRadius = 4f;
    private const float HitRadius   = 10f;

    // ── State ─────────────────────────────────────────────────────────────
    private MainViewModel? VM => DataContext as MainViewModel;
    private CurvePoint? _draggingPoint;
    private bool _draggingHandle;
    private bool _draggingRightHandle;
    private SKPoint _lastMouseCanvas;
    private float _zoom = 1f;
    private SKPoint _pan = SKPoint.Empty;

    public CurveCanvasControl() => InitializeComponent();

    public void Redraw() => SkiaElement.InvalidateVisual();

    // ── Coordinate helpers ────────────────────────────────────────────────

    private float CanvasWidth  => (float)SkiaElement.ActualWidth;
    private float CanvasHeight => (float)SkiaElement.ActualHeight;
    private float PlotW => (CanvasWidth  - Padding * 2) * _zoom;
    private float PlotH => (CanvasHeight - Padding * 2) * _zoom;

    /// Normalised curve space (0..1) → canvas pixel
    private SKPoint ToCanvas(double nx, double ny) => new(
        Padding + (float)nx * PlotW + _pan.X,
        CanvasHeight - Padding - (float)ny * PlotH + _pan.Y
    );

    /// Canvas pixel → normalised curve space (0..1)
    private (double nx, double ny) ToNorm(SKPoint p) => (
        (p.X - Padding - _pan.X) / PlotW,
        1.0 - (p.Y - (CanvasHeight - Padding - PlotH) - _pan.Y) / PlotH
    );

    // ── Paint ─────────────────────────────────────────────────────────────

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(23, 23, 31));

        if (VM == null) return;

        DrawGrid(canvas);
        DrawAxes(canvas);

        // Comparison curves (dimmed)
        foreach (var curve in VM.Document.Curves.Where(c => c != VM.ActiveCurve && c.IsVisible))
            DrawCurve(canvas, curve, alpha: 60);

        // Active curve
        DrawCurve(canvas, VM.ActiveCurve, alpha: 255);
        DrawPoints(canvas, VM.ActiveCurve);
    }

    private void DrawGrid(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color       = new SKColor(255, 255, 255, 18),
            StrokeWidth = 1,
            IsAntialias = false
        };
        int divisions = 8;
        for (int i = 0; i <= divisions; i++)
        {
            double t = (double)i / divisions;
            var h0 = ToCanvas(t, 0);
            var h1 = ToCanvas(t, 1);
            var v0 = ToCanvas(0, t);
            var v1 = ToCanvas(1, t);
            canvas.DrawLine(h0.X, h0.Y, h1.X, h1.Y, paint);
            canvas.DrawLine(v0.X, v0.Y, v1.X, v1.Y, paint);
        }
    }

    private void DrawAxes(SKCanvas canvas)
    {
        using var axisPaint = new SKPaint
        {
            Color       = new SKColor(255, 255, 255, 55),
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        using var labelFont = new SKFont(SKTypeface.Default, 11);
        using var labelPaint = new SKPaint { Color = new SKColor(138, 136, 160), IsAntialias = true };

        var o  = ToCanvas(0, 0);
        var ex = ToCanvas(1, 0);
        var ey = ToCanvas(0, 1);
        canvas.DrawLine(o, ex, axisPaint);
        canvas.DrawLine(o, ey, axisPaint);

        // Tick labels
        int ticks = 5;
        for (int i = 0; i <= ticks; i++)
        {
            double t = (double)i / ticks;
            double mapped = VM!.Document.InputMin + t * (VM.Document.InputMax - VM.Document.InputMin);
            var xPos = ToCanvas(t, 0);
            canvas.DrawText(mapped.ToString("F0"), xPos.X - 8, xPos.Y + 16, SKTextAlign.Left, labelFont, labelPaint);

            double yMapped = VM.Document.OutputMin + t * (VM.Document.OutputMax - VM.Document.OutputMin);
            var yPos = ToCanvas(0, t);
            canvas.DrawText(yMapped.ToString("F2"), 4, yPos.Y + 4, SKTextAlign.Left, labelFont, labelPaint);
        }
    }

    private void DrawCurve(SKCanvas canvas, BezierCurve curve, byte alpha)
    {
        if (curve.Points.Count < 2) return;

        SKColor c = SKColor.Parse(curve.ColorHex).WithAlpha(alpha);

        // Fill under curve
        var poly = curve.GetPolyline(300);
        using var fillPath = new SKPath();
        var first = ToCanvas(poly[0].X, poly[0].Y);
        fillPath.MoveTo(first);
        foreach (var (x, y) in poly.Skip(1))
            fillPath.LineTo(ToCanvas(x, y));
        var last = ToCanvas(poly[^1].X, poly[^1].Y);
        fillPath.LineTo(ToCanvas(poly[^1].X, 0));
        fillPath.LineTo(ToCanvas(poly[0].X,  0));
        fillPath.Close();
        using var fillPaint = new SKPaint { Color = c.WithAlpha((byte)(alpha / 8)), IsStroke = false };
        canvas.DrawPath(fillPath, fillPaint);

        // Curve stroke
        using var linePath = new SKPath();
        linePath.MoveTo(ToCanvas(poly[0].X, poly[0].Y));
        foreach (var (x, y) in poly.Skip(1))
            linePath.LineTo(ToCanvas(x, y));
        using var strokePaint = new SKPaint
        {
            Color       = c,
            StrokeWidth = alpha == 255 ? 2.5f : 1.5f,
            IsAntialias = true,
            IsStroke    = true,
            StrokeCap   = SKStrokeCap.Round,
            StrokeJoin  = SKStrokeJoin.Round
        };
        canvas.DrawPath(linePath, strokePaint);
    }

    private void DrawPoints(SKCanvas canvas, BezierCurve curve)
    {
        using var handleLinePaint = new SKPaint
        {
            Color       = new SKColor(255, 255, 255, 50),
            StrokeWidth = 1,
            IsAntialias = true,
            PathEffect  = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0)
        };
        using var handleDotPaint = new SKPaint
        {
            Color       = new SKColor(138, 136, 160),
            IsAntialias = true
        };
        using var pointFill = new SKPaint
        {
            Color       = SKColor.Parse(curve.ColorHex),
            IsAntialias = true
        };
        using var pointSelected = new SKPaint
        {
            Color       = SKColors.White,
            IsAntialias = true
        };
        using var pointRing = new SKPaint
        {
            Color       = SKColor.Parse(curve.ColorHex),
            IsStroke    = true,
            StrokeWidth = 2,
            IsAntialias = true
        };

        foreach (var pt in curve.Points)
        {
            var cp = ToCanvas(pt.X, pt.Y);

            // Draw handles for selected point
            if (pt.IsSelected)
            {
                var lh = ToCanvas(pt.X + pt.LeftHandleX,  pt.Y + pt.LeftHandleY);
                var rh = ToCanvas(pt.X + pt.RightHandleX, pt.Y + pt.RightHandleY);
                canvas.DrawLine(cp, lh, handleLinePaint);
                canvas.DrawLine(cp, rh, handleLinePaint);
                canvas.DrawCircle(lh, HandleRadius, handleDotPaint);
                canvas.DrawCircle(rh, HandleRadius, handleDotPaint);
            }

            // Draw anchor point
            if (pt.IsSelected)
            {
                canvas.DrawCircle(cp, PointRadius + 2, pointRing);
                canvas.DrawCircle(cp, PointRadius, pointSelected);
            }
            else
            {
                canvas.DrawCircle(cp, PointRadius, pointFill);
            }
        }
    }

    // ── Mouse interaction ─────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (VM == null) return;
        SkiaElement.CaptureMouse();

        var pos = ToSKPoint(e.GetPosition(SkiaElement));
        _lastMouseCanvas = pos;
        _draggingPoint   = null;
        _draggingHandle  = false;

        // 1. Check if clicking a handle of the selected point
        if (VM.SelectedPoint != null)
        {
            var pt  = VM.SelectedPoint;
            var rh  = ToCanvas(pt.X + pt.RightHandleX, pt.Y + pt.RightHandleY);
            var lh  = ToCanvas(pt.X + pt.LeftHandleX,  pt.Y + pt.LeftHandleY);
            if (SKPoint.Distance(pos, rh) < HitRadius)
            {
                _draggingHandle = true; _draggingRightHandle = true;
                _draggingPoint  = pt;
                return;
            }
            if (SKPoint.Distance(pos, lh) < HitRadius)
            {
                _draggingHandle = true; _draggingRightHandle = false;
                _draggingPoint  = pt;
                return;
            }
        }

        // 2. Check if clicking an existing anchor point
        foreach (var pt in VM.ActiveCurve.Points)
        {
            var cp = ToCanvas(pt.X, pt.Y);
            if (SKPoint.Distance(pos, cp) < HitRadius)
            {
                foreach (var p in VM.ActiveCurve.Points) p.IsSelected = false;
                pt.IsSelected   = true;
                VM.SelectedPoint = pt;
                _draggingPoint  = pt;
                Redraw();
                return;
            }
        }

        // 3. Click on empty canvas → add new point
        foreach (var pt in VM.ActiveCurve.Points) pt.IsSelected = false;
        VM.SelectedPoint = null;
        var (nx, ny) = ToNorm(pos);
        if (nx >= 0 && nx <= 1 && ny >= 0 && ny <= 1)
            VM.AddPoint(nx, ny);
        Redraw();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (VM == null) return;
        var pos   = ToSKPoint(e.GetPosition(SkiaElement));
        var delta = new SKPoint(pos.X - _lastMouseCanvas.X, pos.Y - _lastMouseCanvas.Y);
        _lastMouseCanvas = pos;

        if (e.LeftButton != MouseButtonState.Pressed) return;

        if (_draggingHandle && _draggingPoint != null)
        {
            var (dnx, dny) = DeltaToNorm(delta);
            if (_draggingRightHandle)
            {
                _draggingPoint.RightHandleX += dnx;
                _draggingPoint.RightHandleY += dny;
            }
            else
            {
                _draggingPoint.LeftHandleX += dnx;
                _draggingPoint.LeftHandleY += dny;
            }
            Redraw();
            return;
        }

        if (_draggingPoint != null)
        {
            var (dnx, dny) = DeltaToNorm(delta);
            var newX = Math.Clamp(_draggingPoint.X + dnx, 0, 1);
            var newY = Math.Clamp(_draggingPoint.Y + dny, 0, 1);
            _draggingPoint.X = newX;
            _draggingPoint.Y = newY;
            Redraw();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        SkiaElement.ReleaseMouseCapture();
        if (_draggingPoint != null && !_draggingHandle)
        {
            // Commit the move to undo stack (simplified — records final position)
        }
        _draggingPoint  = null;
        _draggingHandle = false;
    }

    private void OnRightMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (VM == null) return;
        var pos = ToSKPoint(e.GetPosition(SkiaElement));
        foreach (var pt in VM.ActiveCurve.Points)
        {
            var cp = ToCanvas(pt.X, pt.Y);
            if (SKPoint.Distance(pos, cp) < HitRadius)
            {
                VM.SelectedPoint = pt;
                VM.DeleteSelectedPoint();
                Redraw();
                return;
            }
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        float factor = e.Delta > 0 ? 1.1f : 0.9f;
        _zoom = Math.Clamp(_zoom * factor, 0.5f, 4f);
        Redraw();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static SKPoint ToSKPoint(Point p) => new((float)p.X, (float)p.Y);

    private (double dnx, double dny) DeltaToNorm(SKPoint delta) =>
        (delta.X / PlotW, -delta.Y / PlotH);
}
