namespace GamingEqualizer.Platform;

public class StubEQBackend : IEQBackend
{
    public bool IsAvailable => false;

    public void Apply(float[] bands, float boostDb) =>
        Logger.Log("StubEQBackend: Apply called (no-op — EQ backend not available on this platform)");

    public void ApplyPerEar(float[] left, float[] right, float boostDb) =>
        Logger.Log("StubEQBackend: ApplyPerEar called (no-op)");

    public void Bypass() =>
        Logger.Log("StubEQBackend: Bypass called (no-op)");
}
