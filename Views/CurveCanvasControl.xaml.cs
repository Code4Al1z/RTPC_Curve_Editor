using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using SkiaSharp.Views.Desktop;
using RTPCCurveEditor.Commands;
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
    private bool _isPanning;
    private bool _hasDragged;
    private SKPoint _mouseDownPos;
    private SKPoint _lastMouseCanvas;
    private float _zoom = 1f;
    private SKPoint _pan = SKPoint.Empty;
    private double _dragStartX, _dragStartY;
    private double _dragStartHandleX, _dragStartHandleY;
    private double _dragStartLHX, _dragStartLHY, _dragStartRHX, _dragStartRHY;

    public CurveCanvasControl()
    {
        InitializeComponent();

        Loaded += (s, e) => SkiaElement?.InvalidateVisual();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += (sender, args) => SkiaElement?.InvalidateVisual();
                vm.CurveChanged += () => SkiaElement?.InvalidateVisual();
            }
            SkiaElement?.InvalidateVisual();
        };

        MouseDoubleClick += OnMouseDoubleClick;
    }

    public void Redraw() => SkiaElement?.InvalidateVisual();

    // ── Coordinate Transformation Engine ──────────────────────────────────

    private float CanvasWidth => (float)SkiaElement.ActualWidth;
    private float CanvasHeight => (float)SkiaElement.ActualHeight;
    private float PlotW => CanvasWidth - CanvasPadding * 2f;
    private float PlotH => CanvasHeight - CanvasPadding * 2f;

    private SKRect PlotBounds => SKRect.Create(CanvasPadding, CanvasPadding, PlotW, PlotH);

    private SKPoint ToCanvas(double nx, double ny) => new(
        CanvasPadding + (float)nx * PlotW * _zoom + _pan.X,
        CanvasHeight - CanvasPadding - (float)ny * PlotH * _zoom + _pan.Y
    );

    private (double nx, double ny) ToNorm(SKPoint p) => (
        (p.X - CanvasPadding - _pan.X) / (PlotW * _zoom),
        (CanvasHeight - CanvasPadding + _pan.Y - p.Y) / (PlotH * _zoom)
    );

    private (double dnx, double dny) DeltaToNorm(SKPoint delta) =>
        (delta.X / (PlotW * _zoom), -delta.Y / (PlotH * _zoom));

    // ── Hit Testing ───────────────────────────────────────────────────────

    private CurvePoint? HitPoint(SKPoint pos, BezierCurve curve)
    {
        foreach (var pt in curve.Points)
            if (SKPoint.Distance(pos, ToCanvas(pt.X, pt.Y)) < HitRadius)
                return pt;
        return null;
    }

    private int HitSegment(SKPoint pos, BezierCurve curve)
    {
        var sorted = curve.Points.OrderBy(p => p.X).ToList();
        var poly = curve.GetPolyline(200);

        for (int i = 0; i < poly.Count - 1; i++)
        {
            var a = ToCanvas(poly[i].X, poly[i].Y);
            var b = ToCanvas(poly[i + 1].X, poly[i + 1].Y);
            if (DistPointToSegment(pos, a, b) < CurveHitDist)
            {
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

    // ── Paint Engine ──────────────────────────────────────────────────────

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(23, 23, 31));
        if (VM == null || PlotW <= 0 || PlotH <= 0) return;

        DrawGridAndAxes(canvas);

        canvas.Save();
        canvas.ClipRect(PlotBounds);

        foreach (var curve in VM.Document.Curves.Where(c => c != VM.ActiveCurve && c.IsVisible))
            DrawCurve(canvas, curve, alpha: 60, isActive: false);

        DrawCurve(canvas, VM.ActiveCurve, alpha: 255, isActive: true);
        DrawPoints(canvas, VM.ActiveCurve);

        canvas.Restore();
    }

    private void DrawGridAndAxes(SKCanvas canvas)
    {
        using var gridPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 18),
            StrokeWidth = 1f,
            IsAntialias = false
        };
        using var framePaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 55),
            StrokeWidth = 1.5f,
            IsAntialias = true,
            IsStroke = true
        };
        using var zeroLinePaint = new SKPaint
        {
            Color = new SKColor(100, 180, 255, 120),
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        using var labelFont = new SKFont(SKTypeface.Default, 11);
        using var labelPaint = new SKPaint { Color = new SKColor(138, 136, 160), IsAntialias = true };

        var (visNxMin, visNyMax) = ToNorm(new SKPoint(CanvasPadding, CanvasPadding));
        var (visNxMax, visNyMin) = ToNorm(new SKPoint(CanvasWidth - CanvasPadding, CanvasHeight - CanvasPadding));

        double inMin = VM!.Document.InputMin, inMax = VM.Document.InputMax;
        double outMin = VM.Document.OutputMin, outMax = VM.Document.OutputMax;

        double visXMin = inMin + visNxMin * (inMax - inMin);
        double visXMax = inMin + visNxMax * (inMax - inMin);
        double visYMin = outMin + visNyMin * (outMax - outMin);
        double visYMax = outMin + visNyMax * (outMax - outMin);

        double xStep = GetNiceInterval(visXMax - visXMin, targetTicks: 8);
        double yStep = GetNiceInterval(visYMax - visYMin, targetTicks: 8);

        double firstX = Math.Floor(visXMin / xStep) * xStep;
        for (double x = firstX; x <= visXMax; x += xStep)
        {
            double normX = (x - inMin) / (inMax - inMin);
            var screenPos = ToCanvas(normX, 0);

            if (screenPos.X >= CanvasPadding && screenPos.X <= CanvasWidth - CanvasPadding)
            {
                canvas.DrawLine(screenPos.X, CanvasPadding, screenPos.X, CanvasHeight - CanvasPadding, gridPaint);
                canvas.DrawText(x.ToString("G4"), screenPos.X - 8f, CanvasHeight - CanvasPadding + 18f, SKTextAlign.Left, labelFont, labelPaint);
            }
        }

        double firstY = Math.Floor(visYMin / yStep) * yStep;
        for (double y = firstY; y <= visYMax; y += yStep)
        {
            double normY = (y - outMin) / (outMax - outMin);
            var screenPos = ToCanvas(0, normY);

            if (screenPos.Y >= CanvasPadding && screenPos.Y <= CanvasHeight - CanvasPadding)
            {
                canvas.DrawLine(CanvasPadding, screenPos.Y, CanvasWidth - CanvasPadding, screenPos.Y, gridPaint);
                canvas.DrawText(y.ToString("G4"), 4f, screenPos.Y + 4f, SKTextAlign.Left, labelFont, labelPaint);
            }
        }

        if (inMin < 0 && inMax > 0)
        {
            double norm0X = (0.0 - inMin) / (inMax - inMin);
            var p0 = ToCanvas(norm0X, 0);
            if (p0.X >= CanvasPadding && p0.X <= CanvasWidth - CanvasPadding)
                canvas.DrawLine(p0.X, CanvasPadding, p0.X, CanvasHeight - CanvasPadding, zeroLinePaint);
        }
        if (outMin < 0 && outMax > 0)
        {
            double norm0Y = (0.0 - outMin) / (outMax - outMin);
            var p0 = ToCanvas(0, norm0Y);
            if (p0.Y >= CanvasPadding && p0.Y <= CanvasWidth - CanvasPadding)
                canvas.DrawLine(CanvasPadding, p0.Y, CanvasWidth - CanvasPadding, p0.Y, zeroLinePaint);
        }

        canvas.DrawRect(PlotBounds, framePaint);
    }

    private static double GetNiceInterval(double range, double targetTicks)
    {
        double rawInterval = range / targetTicks;
        if (rawInterval <= 0) return 1.0;

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawInterval)));
        double residual = rawInterval / magnitude;

        if (residual < 1.5) return 1.0 * magnitude;
        if (residual < 3.0) return 2.0 * magnitude;
        if (residual < 7.0) return 5.0 * magnitude;
        return 10.0 * magnitude;
    }

    private void DrawCurve(SKCanvas canvas, BezierCurve curve, byte alpha, bool isActive)
    {
        if (curve.Points.Count < 2) return;

        var baseColor = SKColor.Parse(curve.ColorHex).WithAlpha(alpha);
        var highlightColor = BrightenColour(baseColor);
        var sorted = curve.Points.OrderBy(p => p.X).ToList();

        var poly = curve.GetPolyline(100);
        if (poly.Count < 2) return;

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

        for (int s = 0; s < sorted.Count - 1; s++)
        {
            var p0 = sorted[s];
            var p1 = sorted[s + 1];

            bool segSelected = isActive && p0.IsSelected && p1.IsSelected;
            SKColor strokeColor = segSelected ? highlightColor : baseColor;
            float strokeWidth = segSelected ? 3.5f : (isActive ? 2.5f : 1.5f);

            double p0x = p0.X, p0y = p0.Y;
            double c0x = p0.X + p0.RightHandleX, c0y = p0.Y + p0.RightHandleY;
            double c1x = p1.X + p1.LeftHandleX, c1y = p1.Y + p1.LeftHandleY;
            double p1x = p1.X, p1y = p1.Y;

            int steps = 100;
            using var segPath = new SKPath();
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double x = BezierCurve.CubicBezier(p0x, c0x, c1x, p1x, t);
                double y = BezierCurve.CubicBezier(p0y, c0y, c1y, p1y, t);
                var cp = ToCanvas(x, y);

                if (i == 0) segPath.MoveTo(cp);
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

    // ── Mouse Interaction & Navigation ────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (VM == null) return;

        this.Focus();
        Keyboard.Focus(this);

        SkiaElement.CaptureMouse();
        var pos = ToSKPoint(e.GetPosition(SkiaElement));
        bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

        _mouseDownPos = pos;
        _lastMouseCanvas = pos;
        _draggingPoint = null;
        _draggingHandle = false;
        _hasDragged = false;

        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            _isPanning = true;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed) return;

        if (VM.SelectedPoint != null && !ctrl)
        {
            var pt = VM.SelectedPoint;
            var rh = ToCanvas(pt.X + pt.RightHandleX, pt.Y + pt.RightHandleY);
            var lh = ToCanvas(pt.X + pt.LeftHandleX, pt.Y + pt.LeftHandleY);
            if (SKPoint.Distance(pos, rh) < HitRadius)
            {
                _draggingHandle = true; _draggingRightHandle = true; _draggingPoint = pt;
                _dragStartHandleX = pt.RightHandleX; _dragStartHandleY = pt.RightHandleY;
                return;
            }
            if (SKPoint.Distance(pos, lh) < HitRadius)
            {
                _draggingHandle = true; _draggingRightHandle = false; _draggingPoint = pt;
                _dragStartHandleX = pt.LeftHandleX; _dragStartHandleY = pt.LeftHandleY;
                return;
            }
        }

        var hitPt = HitPoint(pos, VM.ActiveCurve);
        if (hitPt != null)
        {
            if (ctrl)
            {
                hitPt.IsSelected = !hitPt.IsSelected;
                VM.SelectedPoint = hitPt.IsSelected ? hitPt : null;
            }
            else
            {
                VM.ClearPointSelection();
                hitPt.IsSelected = true;
                VM.SelectedPoint = hitPt;
                _draggingPoint = hitPt;

                _dragStartX = hitPt.X;
                _dragStartY = hitPt.Y;

                _dragStartLHX = hitPt.LeftHandleX;
                _dragStartLHY = hitPt.LeftHandleY;
                _dragStartRHX = hitPt.RightHandleX;
                _dragStartRHY = hitPt.RightHandleY;
            }
            Redraw();
            return;
        }

        var segIdx = HitSegment(pos, VM.ActiveCurve);
        if (segIdx >= 0)
        {
            var sorted = VM.ActiveCurve.Points.OrderBy(p => p.X).ToList();
            var p0 = sorted[segIdx];
            var p1 = sorted[segIdx + 1];

            if (ctrl)
            {
                bool willSelect = !(p0.IsSelected && p1.IsSelected);
                p0.IsSelected = willSelect;
                p1.IsSelected = willSelect;
            }
            else
            {
                VM.ClearPointSelection();
                p0.IsSelected = true;
                p1.IsSelected = true;
                VM.SelectedPoint = p0;
            }
            Redraw();
            return;
        }

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

        if (_isPanning)
        {
            _pan.X += delta.X;
            _pan.Y += delta.Y;
            ClampPan();
            Redraw();
            return;
        }

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

        if (_isPanning)
        {
            _isPanning = false;
            return;
        }

        if (_hasDragged && _draggingPoint != null && VM != null)
        {
            if (_draggingHandle)
            {
                double newX = _draggingRightHandle ? _draggingPoint.RightHandleX : _draggingPoint.LeftHandleX;
                double newY = _draggingRightHandle ? _draggingPoint.RightHandleY : _draggingPoint.LeftHandleY;

                VM.UndoRedo.Execute(new MoveHandleCommand(
                    _draggingPoint, _draggingRightHandle,
                    _dragStartHandleX, _dragStartHandleY, newX, newY,
                    () => VM.RaiseCurveChanged()));
            }
            else
            {
                double newX = _draggingPoint.X;
                double newY = _draggingPoint.Y;

                VM.UndoRedo.Execute(new MovePointCommand(
                    _draggingPoint,
                    _dragStartX, _dragStartY, newX, newY,
                    _dragStartLHX, _dragStartLHY, _draggingPoint.LeftHandleX, _draggingPoint.LeftHandleY,
                    _dragStartRHX, _dragStartRHY, _draggingPoint.RightHandleX, _draggingPoint.RightHandleY,
                    () => VM.RaiseCurveChanged()
                ));
            }

            // Force WPF CommandManager to refresh CanExecute for Ctrl+Z
            CommandManager.InvalidateRequerySuggested();
        }

        _draggingPoint = null;
        _draggingHandle = false;
        _hasDragged = false;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mousePos = ToSKPoint(e.GetPosition(SkiaElement));

        var (nx, ny) = ToNorm(mousePos);

        float zoomFactor = e.Delta > 0 ? 1.2f : 1.0f / 1.2f;
        float newZoom = Math.Clamp(_zoom * zoomFactor, 1.0f, 32.0f);

        if (Math.Abs(newZoom - _zoom) < 0.001f) return;

        _zoom = newZoom;

        _pan.X = mousePos.X - CanvasPadding - (float)nx * PlotW * _zoom;
        _pan.Y = mousePos.Y - (CanvasHeight - CanvasPadding) + (float)ny * PlotH * _zoom;

        ClampPan();
        Redraw();
    }

    private void ClampPan()
    {
        if (_zoom <= 1.001f)
        {
            _zoom = 1.0f;
            _pan = SKPoint.Empty;
            return;
        }

        float minPanX = PlotW * (1.0f - _zoom);
        float maxPanX = 0f;
        float minPanY = 0f;
        float maxPanY = PlotH * (_zoom - 1.0f);

        _pan.X = Math.Clamp(_pan.X, minPanX, maxPanX);
        _pan.Y = Math.Clamp(_pan.Y, minPanY, maxPanY);
    }

    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VM == null || VM.ActiveCurve == null) return;
        var pos = ToSKPoint(e.GetPosition(SkiaElement));

        var hit = HitPoint(pos, VM.ActiveCurve);
        if (hit != null)
        {
            VM.SelectedPoint = hit;
            VM.DeleteSelectedPoint();
            Redraw();
            return;
        }

        var (nx, _) = ToNorm(pos);
        if (nx >= 0 && nx <= 1)
        {
            var inserted = VM.ActiveCurve.InsertPointSeamlessly(nx);
            if (inserted != null)
            {
                VM.ClearPointSelection();
                inserted.IsSelected = true;
                VM.SelectedPoint = inserted;
            }
            Redraw();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (VM == null) return;

        // Ctrl + Z (Undo)
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                if (VM.CanRedo) VM.RedoCommand.Execute(null);
            }
            else
            {
                if (VM.CanUndo) VM.UndoCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        // Ctrl + Y (Redo)
        if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (VM.CanRedo) VM.RedoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Ctrl + A (Select All Points)
        if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            foreach (var pt in VM.ActiveCurve.Points)
                pt.IsSelected = true;
            VM.SelectedPoint = VM.ActiveCurve.Points.FirstOrDefault();
            Redraw();
            e.Handled = true;
        }
    }

    private void OnRightMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private static SKPoint ToSKPoint(Point p) => new((float)p.X, (float)p.Y);

    private static SKColor BrightenColour(SKColor c)
    {
        RgbToHsl(c.Red, c.Green, c.Blue, out float h, out float s, out float l);
        s = Math.Min(1f, s + 0.25f);
        l = Math.Min(1f, l + 0.30f);
        HslToRgb(h, s, l, out float r, out float g, out float b);
        return new SKColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), c.Alpha);
    }

    private static void RgbToHsl(byte r, byte g, byte b, out float h, out float s, out float l)
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

    private static void HslToRgb(float h, float s, float l, out float r, out float g, out float b)
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
}