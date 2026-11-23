using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace WindowBouncer.Services;

internal static class ElevationService
{
    private const string TaskName = "WindowBouncer";

    // Passed by the scheduled task so a relaunched instance knows not to redirect again.
    public const string FromTaskArg = "/FROMTASK";

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
            var exePath = Environment.ProcessPath;
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

            if (p.ExitCode != 0)
                return false;

            // Empty or malformed XML throws here and lands in the catch below.
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            return XDocument.Parse(xml).Descendants(ns + "Arguments").FirstOrDefault()?.Value
                .Contains(exePath, StringComparison.OrdinalIgnoreCase) == true;
        }
        catch { return false; }
    }

    // INSTALL LOCATION CHECK
    // The task runs exePath at high integrity without a UAC prompt, and any same-user process
    // can start it with "schtasks /run". That is only safe when non-admins cannot replace the
    // exe or anything it loads, e.g. Program Files or the MSIX WindowsApps folder. A per-user
    // install under %LOCALAPPDATA% fails this check.

    private const FileSystemRights AppDirWriteRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    // On ancestor folders only rights that allow removing or renaming an existing child count.
    // Creating new folders (e.g. under C:\) is normal and harmless here.
    private const FileSystemRights AncestorWriteRights =
        FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    private const int GenericWriteOrAll = 0x40000000 | 0x10000000;

    public static bool IsInstallLocationProtected(string? exePath)
    {
        try
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            exePath = Path.GetFullPath(exePath);
            var appDir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(appDir)) return false;

            // MSIX packages live under a TrustedInstaller-owned folder whose ACL users can't read.
            var windowsApps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + Path.DirectorySeparatorChar;
            if (exePath.StartsWith(windowsApps, StringComparison.OrdinalIgnoreCase))
                return true;

            var sids = NonAdminSids();

            // Delete on an ancestor matters because it allows renaming that folder inside its
            // parent. A drive root has no parent and can't be renamed, so Delete there is moot
            // (some images, e.g. Windows Sandbox, grant Authenticated Users Modify on C:\).
            for (var d = new DirectoryInfo(appDir).Parent; d is not null; d = d.Parent)
                if (Grants(d.GetAccessControl(), sids, d.Parent is null ? AncestorWriteRights & ~FileSystemRights.Delete : AncestorWriteRights))
                    return false;

            if (Grants(new DirectoryInfo(appDir).GetAccessControl(), sids, AppDirWriteRights))
                return false;

            foreach (var entry in new DirectoryInfo(appDir).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                FileSystemSecurity sec = entry is DirectoryInfo di ? di.GetAccessControl() : ((FileInfo)entry).GetAccessControl();
                if (Grants(sec, sids, AppDirWriteRights))
                    return false;
            }

            return true;
        }
        catch { return false; }
    }

    // The current user and every group in their token, minus Administrators, plus the broad
    // groups any local user belongs to. Rights held by these SIDs are reachable without UAC.
    private static HashSet<SecurityIdentifier> NonAdminSids()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sids = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.WorldSid, null),
            new(WellKnownSidType.AuthenticatedUserSid, null),
            new(WellKnownSidType.BuiltinUsersSid, null),
            new(WellKnownSidType.InteractiveSid, null),
        };
        if (identity.User is not null) sids.Add(identity.User);
        foreach (var g in identity.Groups ?? [])
            if (g is SecurityIdentifier s) sids.Add(s);
        sids.Remove(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        return sids;
    }

    // Deny entries are ignored, so this errs toward "writable".
    private static bool Grants(FileSystemSecurity sec, HashSet<SecurityIdentifier> sids, FileSystemRights rights)
    {
        // An owner can always rewrite the DACL.
        if (sec.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && sids.Contains(owner))
            return true;

        foreach (FileSystemAccessRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)) continue;
            if (rule.IdentityReference is not SecurityIdentifier sid || !sids.Contains(sid)) continue;
            if (((int)rule.FileSystemRights & ((int)rights | GenericWriteOrAll)) != 0)
                return true;
        }
        return false;
    }

    // Registers a scheduled task that runs the app elevated. Triggers UAC once.
    // Returns false if the install location is not protected, the user cancels UAC,
    // or registration fails.
    public static bool RegisterTask(string exePath, bool startWithWindows)
    {
        if (!IsInstallLocationProtected(exePath))
            return false;

        var xmlPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var bytes = Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes(BuildTaskXml(exePath, startWithWindows))).ToArray();

            using (var w = new FileStream(xmlPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                w.Write(bytes);

            // %TEMP% is writable by every process of this user, and the UAC prompt can stay open
            // for a while. Hold a read-only handle that denies other writers until schtasks has
            // read the file, and confirm nobody changed it before the handle was taken.
            using var guard = new FileStream(xmlPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (guard.Length != bytes.Length) return false;
            var onDisk = new byte[bytes.Length];
            guard.ReadExactly(onDisk);
            if (!onDisk.AsSpan().SequenceEqual(bytes)) return false;

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

    // Returns true when Task Scheduler accepted the run request.
    public static bool LaunchViaTask()
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
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    // TASK XML

    // The task runs with the user's environment, which any same-user process can set through
    // HKCU\Environment. Before starting the exe, clear the .NET runtime variables that load code
    // (profilers, diagnostic ports, custom GC, extra deps), turn runtime diagnostics off, and pin
    // PATH to the system folders. /d on cmd already skips the user's AutoRun.
    // The command line must never contain '%': cmd expands %...% before running anything, and a
    // user-defined variable whose name matches the text between two '%' would inject commands.
    // ponytail: known-vector list, not exhaustive; extend it if the runtime adds new load knobs.
    private static readonly string[] ClearedVariables =
    [
        "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS",
        "CORECLR_ENABLE_PROFILING", "CORECLR_PROFILER", "CORECLR_PROFILER_PATH",
        "CORECLR_PROFILER_PATH_32", "CORECLR_PROFILER_PATH_64", "CORECLR_PROFILER_PATH_ARM64",
        "COR_ENABLE_PROFILING", "COR_PROFILER", "COR_PROFILER_PATH",
        "DOTNET_DiagnosticPorts", "COMPlus_DiagnosticPorts",
        "DOTNET_DefaultDiagnosticPortSuspend", "COMPlus_DefaultDiagnosticPortSuspend",
        "DOTNET_GCName", "COMPlus_GCName", "DOTNET_GCPath", "COMPlus_GCPath",
        "COMPlus_EnableDiagnostics",
    ];

    private static string ScrubEnvironment
    {
        get
        {
            var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var clears = string.Join(" & ", ClearedVariables.Select(v => $"set \"{v}=\""));
            return $"{clears} & set \"DOTNET_EnableDiagnostics=0\" & set \"PATH={sys};{win};{sys}\\Wbem\"";
        }
    }

    private static string BuildTaskXml(string exePath, bool startWithWindows)
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
        var appArgs = FromTaskArg + (startWithWindows ? " /NOUI" : string.Empty);
        var cmdLine =
            $"""/d /c {ScrubEnvironment} & if exist "{exePath}" ( start "" "{exePath}" {appArgs} ) else ( schtasks /delete /tn "{TaskName}" /f )""";
        var escapedCmdLine = SecurityElement.Escape(cmdLine)!;
        var cmdExe = SecurityElement.Escape(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))!;

        return $"""
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
                  <Command>{cmdExe}</Command>
                  <Arguments>{escapedCmdLine}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
