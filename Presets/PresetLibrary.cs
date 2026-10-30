using RTPCCurveEditor.Models;

namespace RTPCCurveEditor.Presets;

/// <summary>
/// Curve presets. The Wwise-category entries are hand-tuned Bézier
/// approximations of Wwise's named curve shapes, not exact reproductions of
/// them. The psychoacoustic entries approximate their named formulas in the
/// same way (the sine fades are the closest fit).
/// </summary>
public static class PresetLibrary
{
    public static List<CurvePreset> All => new()
    {
        // ── Wwise built-in shapes ─────────────────────────────────────────

        new CurvePreset
        {
            Name        = "Linear",
            Category    = "Wwise",
            Description = "Straight ramp from 0 to 1.",
            Points      = TwoPoint(0, 0, 1, 1, 0.333, 0.333, 0.333, 0.333)
        },

        new CurvePreset
        {
            Name        = "Constant",
            Category    = "Wwise",
            Description = "Stays near the start value, then rises sharply near the end. An approximation, not a true hold.",
            Points      = TwoPoint(0, 0, 1, 1, 0.8, 0.0, 0.0, 0.8)
        },

        new CurvePreset
        {
            Name        = "S-Curve",
            Category    = "Wwise",
            Description = "Smooth ease-in/ease-out (flat start, steep middle, flat end).",
            Points      = TwoPoint(0, 0, 1, 1, 0.4, 0.0, 0.4, 0.0)
        },

        new CurvePreset
        {
            Name        = "Inverted S-Curve",
            Category    = "Wwise",
            Description = "Fast rise at endpoints, gentle slope in the middle.",
            Points      = TwoPoint(0, 0, 1, 1, 0.0, 0.4, 0.0, 0.4)
        },

        new CurvePreset
        {
            Name        = "Sine (Constant Power Fade In)",
            Category    = "Wwise",
            Description = "sin(x·π/2) power-preserving fade in.",
            Points      = TwoPoint(0, 0, 1, 1, 0.333, 0.5236, 0.333, 0.0)
        },

        new CurvePreset
        {
            Name        = "Sine (Constant Power Fade Out)",
            Category    = "Wwise",
            Description = "cos(x·π/2) power-preserving fade out from 1 to 0.",
            Points      = TwoPoint(0, 1, 1, 0, 0.333, 0.0, 0.333, -0.5236)
        },

        new CurvePreset
        {
            Name        = "Exponential (Base 1.41)",
            Category    = "Wwise",
            Description = "Gentle exponential ease-in.",
            Points      = TwoPoint(0, 0, 1, 1, 0.35, 0.1, 0.25, 0.25)
        },

        new CurvePreset
        {
            Name        = "Exponential (Base 3)",
            Category    = "Wwise",
            Description = "Steep exponential-style rise, a common starting point for distance attenuation.",
            Points      = TwoPoint(0, 0, 1, 1, 0.45, 0.0, 0.15, 0.4)
        },

        new CurvePreset
        {
            Name        = "Logarithmic (Base 1.41)",
            Category    = "Wwise",
            Description = "Gentle logarithmic ease-out.",
            Points      = TwoPoint(0, 0, 1, 1, 0.25, 0.25, 0.35, 0.1)
        },

        new CurvePreset
        {
            Name        = "Logarithmic (Base 3)",
            Category    = "Wwise",
            Description = "Steep logarithmic initial surge followed by a plateau.",
            Points      = TwoPoint(0, 0, 1, 1, 0.15, 0.4, 0.45, 0.0)
        },

        // ── Psychoacoustic extras ─────────────────────────────────────────

        new CurvePreset
        {
            Name        = "Stevens' Power (loudness)",
            Category    = "Psychoacoustic",
            Description = "Approximates Stevens' power law for perceived loudness (L = I^0.3).",
            Points      = TwoPoint(0, 0, 1, 1, 0.1, 0.45, 0.4, 0.05)
        },

        new CurvePreset
        {
            Name        = "Perceptual Volume (dB taper)",
            Category    = "Psychoacoustic",
            Description = "Approximate logarithmic taper for perceptually even dB changes.",
            Points      = TwoPoint(0, 0, 1, 1, 0.5, 0.0, 0.1, 0.45)
        },

        new CurvePreset
        {
            Name        = "Distance Attenuation (inverse square)",
            Category    = "Psychoacoustic",
            Description = "Approximates inverse-square free-field dropoff.",
            Points      = TwoPoint(0, 1, 1, 0, 0.15, -0.4, 0.45, 0.0)
        },

        new CurvePreset
        {
            Name        = "Equal-Power Crossfade (in)",
            Category    = "Psychoacoustic",
            Description = "Quarter-sine crossfade curve maintaining constant RMS power.",
            Points      = TwoPoint(0, 0, 1, 1, 0.333, 0.5236, 0.333, 0.0)
        },

        new CurvePreset
        {
            Name        = "Reverb Wet (underwater)",
            Category    = "Psychoacoustic",
            Description = "Delayed initial wet increase followed by exponential saturation.",
            Points      = TwoPoint(0, 0, 1, 1, 0.5, 0.05, 0.2, 0.45)
        },
    };

    // ── Builder ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a 2-point preset with explicit start (x0, y0), end (x1, y1),
    /// and relative Bézier handle offsets.
    /// </summary>
    private static List<CurvePoint> TwoPoint(
        double x0, double y0, double x1, double y1,
        double rhx, double rhy, double lhx, double lhy)
    {
        return new List<CurvePoint>
        {
            new CurvePoint(x0, y0)
            {
                LeftHandleX  = -rhx,
                LeftHandleY  = -rhy,
                RightHandleX =  rhx,
                RightHandleY =  rhy
            },
            new CurvePoint(x1, y1)
            {
                LeftHandleX  = -lhx,
                LeftHandleY  = -lhy,
                RightHandleX =  lhx,
                RightHandleY =  lhy
            }
        };
    }
}