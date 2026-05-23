# RTPC Curve Editor

A standalone desktop tool for designing, editing, and exporting RTPC (Real-Time Parameter Control) curves for Wwise game audio projects — built with WPF, SkiaSharp, and C# 14 on .NET 10.

![RTPC Curve Editor screenshot](docs/screenshot.png)

---

## Why this exists

Wwise's built-in curve editor works, but it's locked inside the project, limited in mathematical precision, and gives you no way to compare curves side by side or share curve templates across projects. This tool lives outside Wwise entirely — design your curves here with full mathematical control, then export directly as Wwise-importable XML.

---

## Features

- **Interactive Bézier canvas** — add, move, and delete control points; drag tangent handles for precise curve shaping
- **Mathematical presets** — Linear, Logarithmic, Exponential, Equal-Power (crossfade), S-Curve (smooth step / smoother step), Square root, Squared, Cubed
- **Psychoacoustic presets** — Stevens' Power Law (loudness), Perceptual Volume (dB taper), Reverb Wet Curve, Distance Attenuation (inverse square law), Pitch Detune Taper — all mathematically derived, no subjective estimation
- **Comparison view** — overlay up to 4 curves on the same canvas with colour coding
- **Wwise XML export** — serialises the curve as a piecewise-linear approximation matching the Wwise `.wproj` schema, ready to import
- **Wwise XML import** — parse an existing curve from a Wwise project snippet
- **JSON export** — flat sample array with mapped real-world values, for SoundBridge and custom tooling
- **PNG export** — high-resolution curve render for documentation or presentations
- **Native project format** — save and load `.rtpce` files (JSON under the hood, fully diffable in Git)
- **Undo / redo** — full command stack via the Command pattern
- **RTPC mapping** — set real-world input/output ranges; all exports map normalised 0–1 to your actual Wwise parameter ranges
- **Keyboard shortcuts** — `Ctrl+Z/Y`, `Ctrl+S`, `Ctrl+O`, `Ctrl+N`, `Delete` to remove selected point

---

## Tech stack

| Layer | Technology |
|---|---|
| UI framework | WPF (.NET 10, C# 14) |
| MVVM | CommunityToolkit.Mvvm 8.4.2 (`[ObservableProperty]`, `[RelayCommand]`) |
| Canvas rendering | SkiaSharp 3.119.2 |
| Serialisation | System.Text.Json (inbox, .NET 10) |
| Export formats | Wwise XML, JSON, PNG |

---

## Architecture

```
RTPCCurveEditor/
├── Models/          # CurvePoint, BezierCurve, CurveDocument, CurvePreset
├── ViewModels/      # MainViewModel (MVVM, all commands and state)
├── Commands/        # UndoRedoStack, ICurveCommand, concrete command classes
├── Services/        # WwiseXmlService, JsonExportService, PngExportService, ProjectFileService
├── Presets/         # PresetLibrary — mathematically defined curve shapes
├── Views/           # MainWindow, CurveCanvasControl, PresetLibraryPanel, InspectorPanel
├── Converters/      # NullToBoolConverter
└── Resources/       # Styles.xaml (dark theme, palette, control templates)
```

The Bézier engine uses piecewise cubic interpolation with binary search for accurate x→y sampling across arbitrary control point distributions. The equal-power and psychoacoustic presets are derived from textbook formulae (Stevens 1955, ISO 226) — no perceptual estimation involved.

---

## Getting started

### Prerequisites
- [Visual Studio 2026](https://visualstudio.microsoft.com/) with the **.NET desktop development** workload
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Build

```bash
git clone https://github.com/YOUR_USERNAME/rtpc-curve-editor.git
cd rtpc-curve-editor
dotnet restore
dotnet build
```

Or open `RTPCCurveEditor.sln` in Visual Studio and press `F5`.

---

## Usage

| Action | How |
|---|---|
| Add a control point | Double-click on the canvas |
| Remove a control point | Double-click an existing point, or right-click → Remove |
| Move a point | Left-click drag |
| Adjust Bézier handles | Select a point, then drag its tangent handles |
| Select a curve segment | Left-click on the curve line |
| Apply a preset to selection | Select preset in sidebar → Apply Preset |
| Zoom canvas | Mouse wheel |
| Delete selected point | `Delete` key |

---

## Export formats

### Wwise XML
Exports the curve as a `<WwiseRTPC>` element containing `<GraphPoint>` entries sampled at 32 evenly-spaced intervals. Input and output values are mapped to the real-world ranges set in the Inspector panel. The format is compatible with Wwise 2024.x and 2025.x.

### JSON
Exports a flat array of `{ x, y, xMapped, yMapped }` samples (64 by default) plus metadata. Intended for consumption by [SoundBridge](https://github.com/YOUR_USERNAME/soundbridge) and other custom tooling.

### PNG
1200×800 high-resolution render of the curve with grid, axis labels, and anchor points. Suitable for technical documentation and pitch decks.

---

## Roadmap

- [ ] Segment-level preset application (apply a curve shape to a selected portion)
- [ ] Multi-curve select with `Ctrl+click`
- [ ] FMOD project XML export
- [ ] Curve symmetry and mirroring tools
- [ ] Preset save/load from user-defined library

---

## Author

**AL!Z / Aliz Pasztor** — Psychoacoustic-Visual Systems Engineer  
[alizpasztor.com](https://alizpasztor.com)
