#ifndef PCH_H
#define PCH_H

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define NATIVE_API extern "C" __declspec(dllexport)

NATIVE_API double EvaluateCubicBezierYAtX(
    double p0x, double p0y,
    double c0x, double c0y,
    double c1x, double c1y,
    double p1x, double p1y,
    double targetX);

#endif //PCH_H