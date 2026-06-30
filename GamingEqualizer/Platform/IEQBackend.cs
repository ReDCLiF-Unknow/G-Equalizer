namespace GamingEqualizer.Platform;

public interface IEQBackend
{
    bool IsAvailable { get; }
    void Apply(float[] bands, float boostDb);
    void ApplyPerEar(float[] left, float[] right, float boostDb);
    void Bypass();
}
