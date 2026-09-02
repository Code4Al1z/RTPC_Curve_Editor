#include "pch.h"

BOOL APIENTRY DllMain(HMODULE hModule,
    DWORD  ul_reason_for_call,
    LPVOID lpReserved)
{
    return TRUE;
}

NATIVE_API double EvaluateCubicBezierYAtX(
    double p0x, double p0y,
    double c0x, double c0y,
    double c1x, double c1y,
    double p1x, double p1y,
    double targetX)
{
    double low = 0.0, high = 1.0, t = 0.5;
    for (int i = 0; i < 16; ++i)
    {
        t = (low + high) * 0.5;
        double u = 1.0 - t;
        double x = u * u * u * p0x + 3 * u * u * t * c0x + 3 * u * t * t * c1x + t * t * t * p1x;
        if (x < targetX) low = t;
        else high = t;
    }

    double u = 1.0 - t;
    return u * u * u * p0y + 3 * u * u * t * c0y + 3 * u * t * t * c1y + t * t * t * p1y;
}