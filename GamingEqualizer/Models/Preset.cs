namespace GamingEqualizer.Models;

public class Preset
{
    public string Name { get; set; } = "";
    public float[] Bands { get; set; } = new float[10];
}
