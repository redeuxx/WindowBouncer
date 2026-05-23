using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Xml.Linq;

namespace WindowBouncer.Services;

internal static class ElevationService
{
    private const string TaskName = "WindowBouncer";

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    // Returns true only when a task named TaskName exists AND its arguments reference
    // this executable, preventing a malicious same-named task from being launched elevated.
    public static bool IsTaskRegistered()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
                return false;

            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/query /tn \"{TaskName}\" /xml ONE",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (p is null) return false;
            var xml = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(xml))
                return false;

            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var doc = XDocument.Parse(xml);
            var arguments = doc.Descendants(ns + "Arguments").FirstOrDefault()?.Value ?? string.Empty;

            return arguments.Contains(exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // Registers a scheduled task that runs the app elevated. Triggers UAC once.
    // Returns false if the user cancels UAC or registration fails.
    public static bool RegisterTask(string exePath, bool startWithWindows)
    {
        var xmlPath = Path.GetTempFileName();
        try
        {
            WriteTaskXml(xmlPath, exePath, startWithWindows);
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/create /tn \"{TaskName}\" /xml \"{xmlPath}\" /f",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            p?.WaitForExit();
            return p?.ExitCode == 0;
        }
        catch { return false; }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    public static void UnregisterTask()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/delete /tn \"{TaskName}\" /f",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit();
        }
        catch { }
    }

    public static void LaunchViaTask()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/run /tn \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit();
        }
        catch { }
    }

    // TASK XML

    private static void WriteTaskXml(string path, string exePath, bool startWithWindows)
    {
        var username = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name)!;

        var triggers = startWithWindows
            ? $"""
              <Triggers>
                  <LogonTrigger>
                    <Enabled>true</Enabled>
                    <UserId>{username}</UserId>
                  </LogonTrigger>
                </Triggers>
              """
            : "<Triggers/>";

        // SELF-HEALING ACTION
        // If the target exe is missing (e.g. MSIX uninstall, manual move), delete the task
        // instead of failing on every fire. Runs elevated, so schtasks /delete needs no UAC.
        var appArgs = startWithWindows ? " /NOUI" : string.Empty;
        var cmdLine =
            $"""/d /c if exist "{exePath}" ( start "" "{exePath}"{appArgs} ) else ( schtasks /delete /tn "{TaskName}" /f )""";
        var escapedCmdLine = SecurityElement.Escape(cmdLine)!;

        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              {triggers}
              <Principals>
                <Principal id="Author">
                  <UserId>{username}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Hidden>true</Hidden>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>cmd.exe</Command>
                  <Arguments>{escapedCmdLine}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;

        File.WriteAllText(path, xml, System.Text.Encoding.Unicode);
    }
}
