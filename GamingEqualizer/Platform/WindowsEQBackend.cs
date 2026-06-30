namespace GamingEqualizer.Platform;

public class WindowsEQBackend : IEQBackend
{
    private readonly EQConfigWriter _writer = new();

    public bool IsAvailable => EQConfigWriter.IsEqualizerApoInstalled();

    public void Apply(float[] bands, float boostDb) =>
        _writer.Apply(bands, boostDb);

    public void ApplyPerEar(float[] left, float[] right, float boostDb) =>
        _writer.ApplyPerEar(left, right, boostDb);

    public void Bypass() =>
        _writer.Bypass();
}
