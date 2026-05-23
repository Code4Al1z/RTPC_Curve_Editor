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

    // --- Sampling ---------------------------------------------------------

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
        double cx = p1.X + p1.LeftHandleX,  cy = p1.Y + p1.LeftHandleY;
        double dx = p1.X, dy = p1.Y;

        // Binary search for t such that B_x(t) == targetX
        double lo = 0, hi = 1, t = 0.5;
        for (int iter = 0; iter < 32; iter++)
        {
            double xAtT = CubicBezier(ax, bx, cx, dx, t);
            if (Math.Abs(xAtT - targetX) < 1e-6) break;
            if (xAtT < targetX) lo = t; else hi = t;
            t = (lo + hi) / 2.0;
        }
        return CubicBezier(ay, by, cy, dy, t);
    }

    private static double CubicBezier(double p0, double p1, double p2, double p3, double t)
    {
        double mt = 1 - t;
        return mt * mt * mt * p0
             + 3 * mt * mt * t * p1
             + 3 * mt * t * t * p2
             + t * t * t * p3;
    }

    // --- Polyline for rendering -------------------------------------------

    /// <summary>
    /// Return a list of (x, y) samples for drawing the curve on screen.
    /// </summary>
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
