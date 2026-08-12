using System.Runtime.InteropServices;

namespace RTPCCurveEditor.Native;

public static class NativeEvaluator
{
    private const string DllName = "RTPCCurveEvaluatorNative.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern double EvaluateCubicBezierYAtX(
        double p0x, double p0y,
        double c0x, double c0y,
        double c1x, double c1y,
        double p1x, double p1y,
        double targetX);
}