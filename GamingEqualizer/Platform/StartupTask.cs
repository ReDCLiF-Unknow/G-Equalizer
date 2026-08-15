#pragma warning disable CA1416 // Windows-only; every entry point is guarded by OperatingSystem.IsWindows()

using System.Text;
using Microsoft.Win32;

namespace GamingEqualizer.Platform;

/// <summary>
/// "Launch with Windows", implemented as a logon-triggered scheduled task rather than an
/// <c>HKCU\…\Run</c> value.
///
/// The app ships <c>requireAdministrator</c>, and Windows silently skips Run entries whose
/// target needs elevation — no UAC prompt at logon, no launch, nothing written to any log —
/// so the Run-key approach used up to 3.0.2 could never have worked, however correctly it
/// wrote the value. A task with <c>HighestAvailable</c> starts an elevated app at logon
/// without prompting, and <c>InteractiveToken</c> means no password has to be stored.
/// </summary>
public static class StartupTask
{
    private const string TaskName = "GamingEqualizer";

    // Written by 3.0.2 and earlier. Dead weight now — cleaned up whenever the setting is touched.
    private const string LegacyRunKey   = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyRunValue = "GamingEqualizer";

    /// <summary>10s of headroom so the audio endpoint is enumerated before the APO health
    /// check runs — querying the default playback device too early at logon reports a
    /// spurious "not attached". Lower it if the tray icon feels slow to appear.</summary>
    private const string LogonDelay = "PT10S";

    public static bool IsRegistered()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try { return Run($"/Query /TN {Quote(TaskName)}").ExitCode == 0; }
        catch { return false; }
    }

    public static void Register()
    {
        string xmlPath = Path.Combine(Path.GetTempPath(), "geq-startup-task.xml");

        // schtasks reads the file according to the encoding in its declaration, so the two
        // have to agree — a UTF-16 declaration written as UTF-8 fails with a parse error.
        File.WriteAllText(xmlPath, BuildTaskXml(), Encoding.Unicode);
        try
        {
            var result = Run($"/Create /TN {Quote(TaskName)} /XML {Quote(xmlPath)} /F");
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"schtasks /Create failed ({result.ExitCode}): {result.Output}");
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { /* temp file; not worth surfacing */ }
        }

        RemoveLegacyRunValue();
    }

    public static void Unregister()
    {
        var result = Run($"/Delete /TN {Quote(TaskName)} /F");

        // A non-zero exit is also what schtasks returns when the task was never there, so
        // treat "it is gone now" as success rather than parsing the message.
        if (result.ExitCode != 0 && IsRegistered())
            throw new InvalidOperationException($"schtasks /Delete failed ({result.ExitCode}): {result.Output}");

        RemoveLegacyRunValue();
    }

    private static string BuildTaskXml()
    {
        string exe  = Escape(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName);
        string user = Escape($"{Environment.UserDomainName}\\{Environment.UserName}");

        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Starts G-EQ minimised to the tray when {user} logs on.</Description>
            <URI>\{TaskName}</URI>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{user}</UserId>
              <Delay>{LogonDelay}</Delay>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{user}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>false</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <IdleSettings>
              <StopOnIdleEnd>false</StopOnIdleEnd>
              <RestartOnIdle>false</RestartOnIdle>
            </IdleSettings>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <RunOnlyIfIdle>false</RunOnlyIfIdle>
            <WakeToRun>false</WakeToRun>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{exe}</Command>
              <Arguments>--minimized</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;
    }

    private static void RemoveLegacyRunValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey, writable: true);
            key?.DeleteValue(LegacyRunValue, throwOnMissingValue: false);
        }
        catch { /* the task is what matters; a stale Run value is harmless */ }
    }

    private static (int ExitCode, string Output) Run(string arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start schtasks.exe");

        string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, output.Trim());
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? value;
}
