namespace RTPCCurveEditor.Models;

/// <summary>
/// An ordered list of CurvePoints defining a piecewise cubic Bézier curve.
/// Segments are interpolated between consecutive anchor points using their handles.
/// </summary>
public class BezierCurve
{
    public List<CurvePoint> Points { get; set; } = new();
    public string Name { get; set; } = "Untitled";
    public string ColorHex { get; set; } = "#7F77DD";
    public bool IsVisible { get; set; } = true;

    // --- Sampling & Editing -----------------------------------------------

    /// <summary>
    /// Evaluate the curve Y value at a given normalised X (0..1).
    /// Uses piecewise cubic Bézier interpolation between consecutive anchor points.
    /// </summary>
    public double Sample(double x)
    {
        if (Points.Count == 0) return 0;
        if (Points.Count == 1) return Points[0].Y;

        var sorted = Points.OrderBy(p => p.X).ToList();

        if (x <= sorted[0].X) return sorted[0].Y;
        if (x >= sorted[^1].X) return sorted[^1].Y;

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var p0 = sorted[i];
            var p1 = sorted[i + 1];
            if (x >= p0.X && x <= p1.X)
                return SampleSegment(p0, p1, x);
        }
        return 0;
    }

    /// <summary>
    /// Recalculates tangent handles for all points to produce smooth, continuous curves
    /// without flat "ease-in" kinks or artificial breaks at the endpoints.
    /// </summary>
    public void AutoSmoothHandles()
    {
        if (Points.Count < 2) return;

        var sorted = Points.OrderBy(p => p.X).ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var current = sorted[i];

            if (i == 0)
            {
                // Start point: align right handle toward next point
                var next = sorted[1];
                double dx = Math.Max(1e-5, next.X - current.X);
                double dy = next.Y - current.Y;
                double slope = dy / dx;

                double lenX = dx / 3.0;
                current.RightHandleX = lenX;
                current.RightHandleY = slope * lenX;
                current.LeftHandleX = -lenX;
                current.LeftHandleY = -slope * lenX;
            }
            else if (i == sorted.Count - 1)
            {
                // End point: align left handle toward previous point
                var prev = sorted[i - 1];
                double dx = Math.Max(1e-5, current.X - prev.X);
                double dy = current.Y - prev.Y;
                double slope = dy / dx;

                double lenX = dx / 3.0;
                current.LeftHandleX = -lenX;
                current.LeftHandleY = -slope * lenX;
                current.RightHandleX = lenX;
                current.RightHandleY = slope * lenX;
            }
            else
            {
                // Interior point: centered finite-difference slope across neighbors
                var prev = sorted[i - 1];
                var next = sorted[i + 1];

                double dxLeft = Math.Max(1e-5, current.X - prev.X);
                double dxRight = Math.Max(1e-5, next.X - current.X);
                double totalDx = Math.Max(1e-5, next.X - prev.X);
                double totalDy = next.Y - prev.Y;

                double slope = totalDy / totalDx;

                current.LeftHandleX = -dxLeft / 3.0;
                current.LeftHandleY = -slope * (dxLeft / 3.0);

                current.RightHandleX = dxRight / 3.0;
                current.RightHandleY = slope * (dxRight / 3.0);
            }
        }
    }

    /// <summary>
    /// Ensures endpoint anchors snap cleanly to X = 0.0 and X = 1.0.
    /// </summary>
    public void EnsureEndpoints()
    {
        if (Points.Count == 0)
        {
            Points.Add(new CurvePoint(0.0, 0.0));
            Points.Add(new CurvePoint(1.0, 1.0));
            AutoSmoothHandles();
            return;
        }

        var sorted = Points.OrderBy(p => p.X).ToList();
        sorted[0].X = 0.0;
        sorted[^1].X = 1.0;
    }

    private static double SampleSegment(CurvePoint p0, CurvePoint p1, double targetX)
    {
        double ax = p0.X, ay = p0.Y;
        double bx = p0.X + p0.RightHandleX, by = p0.Y + p0.RightHandleY;
        double cx = p1.X + p1.LeftHandleX, cy = p1.Y + p1.LeftHandleY;
        double dx = p1.X, dy = p1.Y;

        double t = SolveTForX(targetX, ax, bx, cx, dx);
        return CubicBezier(ay, by, cy, dy, t);
    }

    public CurvePoint InsertPointSeamlessly(double targetX)
    {
        targetX = Math.Clamp(targetX, 0.0, 1.0);

        if (Points.Count == 0)
        {
            var p = new CurvePoint(targetX, 0.5);
            Points.Add(p);
            return p;
        }

        var sorted = Points.OrderBy(p => p.X).ToList();

        if (Points.Count == 1 || targetX <= sorted[0].X || targetX >= sorted[^1].X)
        {
            double y = Sample(targetX);
            var p = new CurvePoint(targetX, y);
            Points.Add(p);
            Points = Points.OrderBy(pt => pt.X).ToList();
            return p;
        }

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var p0 = sorted[i];
            var p1 = sorted[i + 1];

            if (targetX >= p0.X && targetX <= p1.X)
            {
                double p0x = p0.X, p0y = p0.Y;
                double c0x = p0.X + p0.RightHandleX, c0y = p0.Y + p0.RightHandleY;
                double c1x = p1.X + p1.LeftHandleX, c1y = p1.Y + p1.LeftHandleY;
                double p1x = p1.X, p1y = p1.Y;

                double t = SolveTForX(targetX, p0x, c0x, c1x, p1x);

                double q1x = (1 - t) * p0x + t * c0x;
                double q1y = (1 - t) * p0y + t * c0y;

                double hx = (1 - t) * c0x + t * c1x;
                double hy = (1 - t) * c0y + t * c1y;

                double r2x = (1 - t) * c1x + t * p1x;
                double r2y = (1 - t) * c1y + t * p1y;

                double q2x = (1 - t) * q1x + t * hx;
                double q2y = (1 - t) * q1y + t * hy;

                double r1x = (1 - t) * hx + t * r2x;
                double r1y = (1 - t) * hy + t * r2y;

                double px = (1 - t) * q2x + t * r1x;
                double py = (1 - t) * q2y + t * r1y;

                p0.RightHandleX = q1x - p0x;
                p0.RightHandleY = q1y - p0y;

                p1.LeftHandleX = r2x - p1x;
                p1.LeftHandleY = r2y - p1y;

                var insertedPoint = new CurvePoint
                {
                    X = px,
                    Y = py,
                    LeftHandleX = q2x - px,
                    LeftHandleY = q2y - py,
                    RightHandleX = r1x - px,
                    RightHandleY = r1y - py
                };

                Points.Add(insertedPoint);
                Points = Points.OrderBy(p => p.X).ToList();
                return insertedPoint;
            }
        }

        return null!;
    }

    private static double SolveTForX(double x, double p0x, double p1x, double p2x, double p3x)
    {
        if (x <= p0x) return 0.0;
        if (x >= p3x) return 1.0;

        double t = (x - p0x) / Math.Max(1e-6, p3x - p0x);

        for (int i = 0; i < 8; i++)
        {
            double currentX = CubicBezier(p0x, p1x, p2x, p3x, t);
            double derivativeX = CubicBezierDerivative(p0x, p1x, p2x, p3x, t);

            if (Math.Abs(derivativeX) < 1e-7) break;

            double error = currentX - x;
            if (Math.Abs(error) < 1e-6) return Math.Clamp(t, 0.0, 1.0);

            t -= error / derivativeX;
        }

        double tMin = 0.0, tMax = 1.0;
        t = Math.Clamp(t, 0.0, 1.0);

        for (int i = 0; i < 16; i++)
        {
            double currentX = CubicBezier(p0x, p1x, p2x, p3x, t);
            double error = currentX - x;

            if (Math.Abs(error) < 1e-6) break;

            if (error > 0) tMax = t;
            else tMin = t;

            t = (tMin + tMax) * 0.5;
        }

        return Math.Clamp(t, 0.0, 1.0);
    }

    internal static double CubicBezier(double p0, double p1, double p2, double p3, double t)
    {
        double mt = 1 - t;
        return mt * mt * mt * p0
             + 3 * mt * mt * t * p1
             + 3 * mt * t * t * p2
             + t * t * t * p3;
    }

    private static double CubicBezierDerivative(double p0, double p1, double p2, double p3, double t)
    {
        double mt = 1.0 - t;
        return 3.0 * mt * mt * (p1 - p0)
             + 6.0 * mt * t * (p2 - p1)
             + 3.0 * t * t * (p3 - p2);
    }

    /// <summary>
    /// Return a list of (x, y) samples drawn directly in parametric t-space.
    /// Automatically places dense samples along steep curves to prevent polyline kinks.
    /// </summary>
    public List<(double X, double Y)> GetPolyline(int stepsPerSegment = 100)
    {
        var result = new List<(double X, double Y)>();
        if (Points.Count == 0) return result;

        var sorted = Points.OrderBy(p => p.X).ToList();
        if (sorted.Count == 1)
        {
            result.Add((0.0, sorted[0].Y));
            result.Add((1.0, sorted[0].Y));
            return result;
        }

        if (sorted[0].X > 0)
            result.Add((0.0, sorted[0].Y));

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var p0 = sorted[i];
            var p1 = sorted[i + 1];

            double p0x = p0.X, p0y = p0.Y;
            double c0x = p0.X + p0.RightHandleX, c0y = p0.Y + p0.RightHandleY;
            double c1x = p1.X + p1.LeftHandleX, c1y = p1.Y + p1.LeftHandleY;
            double p1x = p1.X, p1y = p1.Y;

            for (int s = 0; s <= stepsPerSegment; s++)
            {
                if (i > 0 && s == 0) continue; // avoid duplicate boundary points

                double t = (double)s / stepsPerSegment;
                double x = CubicBezier(p0x, c0x, c1x, p1x, t);
                double y = CubicBezier(p0y, c0y, c1y, p1y, t);
                result.Add((x, y));
            }
        }

        if (sorted[^1].X < 1.0)
            result.Add((1.0, sorted[^1].Y));

        return result;
    }

    public BezierCurve Clone()
    {
        return new BezierCurve
        {
            Name = Name,
            ColorHex = ColorHex,
            IsVisible = IsVisible,
            Points = Points.Select(p => p.Clone()).ToList()
        };
    }
}