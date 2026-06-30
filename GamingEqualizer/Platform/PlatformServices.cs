namespace GamingEqualizer.Platform;

public static class PlatformServices
{
    public static IEQBackend CreateEQBackend()
    {
        if (OperatingSystem.IsWindows()) return new WindowsEQBackend();
        if (OperatingSystem.IsMacOS())   return new MacEQBackend();
        if (OperatingSystem.IsLinux())   return new LinuxEQBackend();
        return new StubEQBackend();
    }
}
