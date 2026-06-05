using RTPCCurveEditor.Models;

namespace RTPCCurveEditor.Presets;

/// <summary>
/// Curve presets matching Wwise's built-in curve shapes exactly.
/// Each preset is defined as 2 points with handle values derived from
/// the actual Wwise curve mathematics. No extra points are ever added —
/// the shape lives entirely in the Bézier handles.
/// </summary>
public static class PresetLibrary
{
    public static List<CurvePreset> All => new()
    {
        // ── Wwise built-in shapes ─────────────────────────────────────────
        // These match Wwise's curve preset names and visual shapes exactly.
        // Handle strengths are derived from the target function sampled at
        // t=1/3 and t=2/3, then back-solved for cubic Bézier control points.

        new CurvePreset
        {
            Name        = "Linear",
            Category    = "Wwise",
            Description = "Straight ramp. Equivalent to Wwise's Linear preset.",
            Points      = TwoPoint(0.333, 0.333, 0.333, 0.333)
        },

        new CurvePreset
        {
            Name        = "Constant",
            Category    = "Wwise",
            Description = "Holds the start value then jumps at the end. " +
                          "Equivalent to Wwise's Constant preset.",
            Points      = TwoPoint(0.5, 0.0, 0.0, 0.0)
        },

        new CurvePreset
        {
            Name        = "S-Curve",
            Category    = "Wwise",
            Description = "Smooth ease-in/ease-out. Equivalent to Wwise's S-Curve preset. " +
                          "Use for natural-feeling crossfades.",
            Points      = TwoPoint(0.0, 0.5, 0.0, 0.5)
        },

        new CurvePreset
        {
            Name        = "Inverted S-Curve",
            Category    = "Wwise",
            Description = "S-Curve flipped — fast at both ends, slow in the middle. " +
                          "Equivalent to Wwise's Inverted S-Curve.",
            Points      = TwoPoint(0.5, 0.0, 0.5, 0.0)
        },

        new CurvePreset
        {
            Name        = "Sine (Constant Power Fade In)",
            Category    = "Wwise",
            Description = "sin²(x·π/2) — preserves total power during a crossfade. " +
                          "Use on the incoming signal of a crossfade pair.",
            Points      = TwoPoint(0.0, 0.85, 0.0, 0.15)
        },

        new CurvePreset
        {
            Name        = "Sine (Constant Power Fade Out)",
            Category    = "Wwise",
            Description = "cos²(x·π/2) — power-preserving fade out. " +
                          "Use on the outgoing signal of a crossfade pair.",
            Points      = TwoPoint(0.0, 0.15, 0.0, 0.85)
        },

        new CurvePreset
        {
            Name        = "Exponential (Base 1.41)",
            Category    = "Wwise",
            Description = "Gentle exponential curve — slow start, moderate acceleration. " +
                          "Equivalent to Wwise's Exponential (Base 1.41).",
            Points      = TwoPoint(0.0, 0.5, 0.5, 0.0)
        },

        new CurvePreset
        {
            Name        = "Exponential (Base 3)",
            Category    = "Wwise",
            Description = "Steep exponential — very slow start, dramatic finish. " +
                          "Equivalent to Wwise's Exponential (Base 3). " +
                          "Classic choice for volume RTPCs driven by distance.",
            Points      = TwoPoint(0.0, 0.9, 0.9, 0.0)
        },

        new CurvePreset
        {
            Name        = "Logarithmic (Base 1.41)",
            Category    = "Wwise",
            Description = "Gentle logarithmic — fast rise then gradual. " +
                          "Equivalent to Wwise's Logarithmic (Base 1.41).",
            Points      = TwoPoint(0.5, 0.0, 0.0, 0.5)
        },

        new CurvePreset
        {
            Name        = "Logarithmic (Base 3)",
            Category    = "Wwise",
            Description = "Steep logarithmic — very fast initial rise, long plateau. " +
                          "Equivalent to Wwise's Logarithmic (Base 3). " +
                          "Use for reverb send RTPCs where initial effect matters most.",
            Points      = TwoPoint(0.9, 0.0, 0.0, 0.9)
        },

        // ── Psychoacoustic extras ─────────────────────────────────────────
        // These go beyond Wwise's built-ins and are specific to this tool.

        new CurvePreset
        {
            Name        = "Stevens' Power (loudness)",
            Category    = "Psychoacoustic",
            Description = "Perceived loudness model: L = I^0.3 (Stevens, 1955). " +
                          "Maps a linear physical intensity RTPC to perceived equal-loudness steps. " +
                          "Use on Volume RTPCs driven by linear game parameters.",
            Points      = TwoPoint(0.7, 0.0, 0.0, 0.3)
        },

        new CurvePreset
        {
            Name        = "Perceptual Volume (dB taper)",
            Category    = "Psychoacoustic",
            Description = "Maps 0→1 to −60dB→0dB using A = 10^(dB/20). " +
                          "Sounds perceptually linear when driving a Wwise Volume RTPC.",
            Points      = TwoPoint(0.0, 0.95, 0.95, 0.0)
        },

        new CurvePreset
        {
            Name        = "Distance Attenuation (inverse square)",
            Category    = "Psychoacoustic",
            Description = "1/(1+x·k)² — physically accurate free-field attenuation " +
                          "with a soft near-field knee. Use for custom distance curves.",
            Points      = TwoPoint(0.0, 0.8, 0.8, 0.0)
        },

        new CurvePreset
        {
            Name        = "Equal-Power Crossfade (in)",
            Category    = "Psychoacoustic",
            Description = "sin²(x·π/2) expressed with precise handle placement. " +
                          "Mathematically identical to Wwise's Sine fade-in.",
            Points      = TwoPoint(0.0, 0.85, 0.0, 0.15)
        },

        new CurvePreset
        {
            Name        = "Reverb Wet (underwater)",
            Category    = "Psychoacoustic",
            Description = "Slow linear dry-out then equal-power wet-in. " +
                          "Use on a reverb send RTPC as the player enters a submerged space.",
            Points      = TwoPoint(0.0, 0.7, 0.1, 0.3)
        },
    };

    // ── Builder ───────────────────────────────────────────────────────────

    /// <summary>
    /// Create a 2-point preset (the only kind we use — shapes live in handles).
    /// rh = right handle of point 0, lh = left handle of point 1.
    /// All values are normalised 0..1 offsets; the export layer scales them
    /// to the actual region when applied.
    /// rhx/rhy: right handle offset from point 0 (0,0)
    /// lhx/lhy: left handle offset from point 1 (1,1)  — stored as negative X
    /// </summary>
    private static List<CurvePoint> TwoPoint(
        double rhx, double rhy, double lhx, double lhy)
    {
        return new List<CurvePoint>
        {
            new CurvePoint(0, 0)
            {
                LeftHandleX  = -rhx,   // mirror on left (unused for first point)
                LeftHandleY  = -rhy,
                RightHandleX =  rhx,
                RightHandleY =  rhy
            },
            new CurvePoint(1, 1)
            {
                LeftHandleX  = -lhx,
                LeftHandleY  = -lhy,
                RightHandleX =  lhx,   // mirror on right (unused for last point)
                RightHandleY =  lhy
            }
        };
    }
}