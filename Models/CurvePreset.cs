namespace RTPCCurveEditor.Models;

public class CurvePreset
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public List<CurvePoint> Points { get; set; } = new();
    public bool IsBuiltIn { get; set; } = true;
}
