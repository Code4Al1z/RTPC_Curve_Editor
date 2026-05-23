using RTPCCurveEditor.Models;

namespace RTPCCurveEditor.Presets;

/// <summary>
/// Generates built-in curve presets using mathematically defined shapes.
/// All Y values are normalised 0..1. No perceptual estimation — pure maths.
/// </summary>
public static class PresetLibrary
{
    public static List<CurvePreset> All => new()
    {
        // ── Math presets ──────────────────────────────────────────────────

        new CurvePreset
        {
            Name = "Linear",
            Category = "Math",
            Description = "Straight ramp from 0 to 1.",
            Points = Evaluate(x => x)
        },
        new CurvePreset
        {
            Name = "Logarithmic",
            Category = "Math",
            Description = "Fast rise then gradual — mimics natural loudness increase.",
            Points = Evaluate(x => x <= 0 ? 0 : Math.Log(1 + x * (Math.E - 1)))
        },
        new CurvePreset
        {
            Name = "Exponential",
            Category = "Math",
            Description = "Slow start, steep finish. Good for tension build.",
            Points = Evaluate(x => (Math.Exp(x) - 1) / (Math.E - 1))
        },
        new CurvePreset
        {
            Name = "Equal-Power",
            Category = "Math",
            Description = "sin²(x·π/2) — preserves loudness in crossfades.",
            Points = Evaluate(x => Math.Pow(Math.Sin(x * Math.PI / 2), 2))
        },
        new CurvePreset
        {
            Name = "Equal-Power (cosine)",
            Category = "Math",
            Description = "cos²(x·π/2) — fading-out half of equal-power crossfade.",
            Points = Evaluate(x => Math.Pow(Math.Cos(x * Math.PI / 2), 2))
        },
        new CurvePreset
        {
            Name = "S-Curve (smooth step)",
            Category = "Math",
            Description = "3x² − 2x³ — smooth ease-in/ease-out.",
            Points = Evaluate(x => 3 * x * x - 2 * x * x * x)
        },
        new CurvePreset
        {
            Name = "S-Curve (smoother step)",
            Category = "Math",
            Description = "6x⁵ − 15x⁴ + 10x³ — Perlin's improved version, flatter shoulders.",
            Points = Evaluate(x => 6*Math.Pow(x,5) - 15*Math.Pow(x,4) + 10*Math.Pow(x,3))
        },
        new CurvePreset
        {
            Name = "Square root",
            Category = "Math",
            Description = "√x — aggressive early rise, plateau toward top.",
            Points = Evaluate(x => Math.Sqrt(x))
        },
        new CurvePreset
        {
            Name = "Squared",
            Category = "Math",
            Description = "x² — slow start accelerating to top.",
            Points = Evaluate(x => x * x)
        },
        new CurvePreset
        {
            Name = "Cubed",
            Category = "Math",
            Description = "x³ — even slower start, steeper final rise.",
            Points = Evaluate(x => x * x * x)
        },

        // ── Psychoacoustic presets ────────────────────────────────────────
        // These use mathematically defined psychoacoustic laws.
        // Stevens' power law: perceived loudness ∝ intensity^0.3 (sones model)
        // All derivations are textbook — no subjective estimation.

        new CurvePreset
        {
            Name = "Stevens' Power (loudness)",
            Category = "Psychoacoustic",
            Description = "Perceived loudness model: L = I^0.3 (Stevens, 1955). " +
                          "Maps a linear physical intensity RTPC to perceived equal loudness steps. " +
                          "Use on Volume RTPCs driven by linear game parameters.",
            Points = Evaluate(x => x <= 0 ? 0 : Math.Pow(x, 0.3))
        },
        new CurvePreset
        {
            Name = "Inverse Stevens' (linearise loudness)",
            Category = "Psychoacoustic",
            Description = "Inverse of Stevens' power: I = L^(1/0.3) = L^3.33. " +
                          "Use when your RTPC input is already a perceived loudness value " +
                          "and you want a physical amplitude output.",
            Points = Evaluate(x => x <= 0 ? 0 : Math.Pow(x, 1.0 / 0.3))
        },
        new CurvePreset
        {
            Name = "Perceptual Volume (dB taper)",
            Category = "Psychoacoustic",
            Description = "Maps normalised 0..1 to a −60dB..0dB range using " +
                          "the standard dB-to-amplitude formula: A = 10^(dB/20). " +
                          "Sounds perceptually linear when driving a Wwise Volume RTPC.",
            Points = Evaluate(x =>
            {
                if (x <= 0) return 0;
                double dB = -60 + x * 60; // map 0..1 → -60..0 dB
                return Math.Pow(10, dB / 20.0);
            })
        },
        new CurvePreset
        {
            Name = "Reverb Wet Curve (underwater)",
            Category = "Psychoacoustic",
            Description = "Suggested RTPC shape for a dry-to-wet reverb blend as the player " +
                          "moves into an enclosed/submerged space. Slow linear dry-out, " +
                          "then an equal-power wet-in for perceptual smoothness.",
            Points = Evaluate(x => Math.Pow(Math.Sin(x * Math.PI / 2), 1.8))
        },
        new CurvePreset
        {
            Name = "Distance Attenuation (inverse square)",
            Category = "Psychoacoustic",
            Description = "1/(1 + x·k)² — inverse square law approximation. " +
                          "Physically accurate free-field attenuation with soft near-field knee.",
            Points = Evaluate(x => 1.0 / Math.Pow(1 + x * 9, 2))
        },
        new CurvePreset
        {
            Name = "Pitch Detune Taper",
            Category = "Psychoacoustic",
            Description = "Logarithmic taper suitable for detuning RTPCs: " +
                          "large perceptible changes at low values, fine-grained at high. " +
                          "Based on JND (just-noticeable difference) scaling.",
            Points = Evaluate(x => x <= 0 ? 0 : Math.Log10(1 + x * 9))
        },
    };

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sample a mathematical function at N evenly-spaced X positions
    /// and return them as CurvePoints with flat tangent handles.
    /// </summary>
    private static List<CurvePoint> Evaluate(Func<double, double> f, int count = 12)
    {
        var pts = new List<CurvePoint>();
        for (int i = 0; i < count; i++)
        {
            double x = (double)i / (count - 1);
            double y = Math.Clamp(f(x), 0, 1);
            var pt = new CurvePoint(x, y);

            // Flat tangent handles — let the Bézier engine interpolate cleanly
            double delta = 1.0 / (count - 1) * 0.3;
            pt.LeftHandleX  = -delta;
            pt.LeftHandleY  = 0;
            pt.RightHandleX = delta;
            pt.RightHandleY = 0;

            pts.Add(pt);
        }
        return pts;
    }
}
