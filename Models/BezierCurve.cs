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
    /// Sample a single cubic Bézier segment via numerical root-finding on t,
    /// then evaluate Y(t).
    /// </summary>
    private static double SampleSegment(CurvePoint p0, CurvePoint p1, double targetX)
    {
        // Control points in absolute space
        double ax = p0.X, ay = p0.Y;
        double bx = p0.X + p0.RightHandleX, by = p0.Y + p0.RightHandleY;
        double cx = p1.X + p1.LeftHandleX, cy = p1.Y + p1.LeftHandleY;
        double dx = p1.X, dy = p1.Y;

        double t = SolveTForX(targetX, ax, bx, cx, dx);
        return CubicBezier(ay, by, cy, dy, t);
    }

    /// <summary>
    /// Inserts a new point on the curve at targetX without altering the curve shape.
    /// Uses de Casteljau's algorithm to split the cubic Bézier segment smoothly.
    /// </summary>
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
                // Control points in absolute coordinates
                double p0x = p0.X, p0y = p0.Y;
                double c0x = p0.X + p0.RightHandleX, c0y = p0.Y + p0.RightHandleY;
                double c1x = p1.X + p1.LeftHandleX, c1y = p1.Y + p1.LeftHandleY;
                double p1x = p1.X, p1y = p1.Y;

                // Parameter t for targetX
                double t = SolveTForX(targetX, p0x, c0x, c1x, p1x);

                // De Casteljau subdivision (Level 1)
                double q1x = (1 - t) * p0x + t * c0x;
                double q1y = (1 - t) * p0y + t * c0y;

                double hx = (1 - t) * c0x + t * c1x;
                double hy = (1 - t) * c0y + t * c1y;

                double r2x = (1 - t) * c1x + t * p1x;
                double r2y = (1 - t) * c1y + t * p1y;

                // De Casteljau subdivision (Level 2)
                double q2x = (1 - t) * q1x + t * hx;
                double q2y = (1 - t) * q1y + t * hy;

                double r1x = (1 - t) * hx + t * r2x;
                double r1y = (1 - t) * hy + t * r2y;

                // De Casteljau subdivision (Level 3 - exact point on curve)
                double px = (1 - t) * q2x + t * r1x;
                double py = (1 - t) * q2y + t * r1y;

                // Update original endpoints' handles as relative offsets
                p0.RightHandleX = q1x - p0x;
                p0.RightHandleY = q1y - p0y;

                p1.LeftHandleX = r2x - p1x;
                p1.LeftHandleY = r2y - p1y;

                // Create new point with exact sub-segment relative handles
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

    private static double CubicBezier(double p0, double p1, double p2, double p3, double t)
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

    // --- Polyline for rendering -------------------------------------------

    public List<(double X, double Y)> GetPolyline(int steps = 200)
    {
        var result = new List<(double, double)>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            double x = (double)i / steps;
            result.Add((x, Sample(x)));
        }
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