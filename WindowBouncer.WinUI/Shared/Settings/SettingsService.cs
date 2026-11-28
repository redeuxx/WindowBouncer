using System.IO;
using System.IO.Enumeration;
using System.Text.Json;

namespace WindowBouncer.Settings;

public sealed class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowBouncer");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppSettings Current { get; private set; } = new();

    // Set when settings.json exists but could not be read. The file is backed up before the
    // first save so a transient lock or a hand-edit typo doesn't wipe the user's settings.
    private bool _loadFailed;

    private static readonly bool IsElevated = WindowBouncer.Services.ElevationService.IsElevated();

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Current = new AppSettings();
                return;
            }

            var json = File.ReadAllText(SettingsPath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            Current = new AppSettings();
            _loadFailed = File.Exists(SettingsPath);
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            if (_loadFailed)
            {
                // Elevated, a copy into this user-writable folder could be redirected onto a
                // protected file with attacker-chosen content. Keep the original, skip the save.
                if (IsElevated) return;
                // Throws (and skips the save) if the original still can't be read.
                File.Copy(SettingsPath, SettingsPath + ".bad", overwrite: true);
                _loadFailed = false;
            }
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            // Write then rename, so a crash or power loss mid-write can't leave a truncated file.
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, SettingsPath, overwrite: true);
        }
        catch
        {
            // Non-fatal — settings just won't persist
        }
    }

    // Adds a process to the exclude list and saves, unless an existing entry already covers it.
    public void AddExcludedProcess(string processName)
    {
        if (processName.Length == 0 || IsProcessExcluded(processName))
            return;
        // Replace the list instead of adding to it: GetWindowsAsync reads it on a worker thread.
        Current.ExcludedProcessNames = [.. Current.ExcludedProcessNames, EntryFor(processName)];
        Save();
    }

    private bool IsProcessExcluded(string processName)
        => Current.ExcludedProcessNames.Any(e => EntryMatchesProcess(e, processName));

    // The entry that EntryMatchesProcess maps back to processName. A "foo.exe.exe" image has the
    // process name "foo.exe", which needs one more ".exe" to survive the stripping below.
    public static string EntryFor(string processName)
        => processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName + ".exe" : processName;

    public static bool EntryMatchesProcess(string entry, string processName)
    {
        // Accept "chrome.exe" as typed from Task Manager; process names have no extension.
        var name = entry.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return string.Equals(processName, name, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsExcluded(string processName, string windowTitle)
    {
        if (string.IsNullOrEmpty(processName) && string.IsNullOrEmpty(windowTitle))
            return false;

        if (IsProcessExcluded(processName))
            return true;

        foreach (var pattern in Current.ExcludedTitlePatterns)
        {
            if (string.IsNullOrEmpty(pattern))
                continue;
            // "*" and "?" make it a glob over the whole title; otherwise a substring match.
            bool match = pattern.AsSpan().IndexOfAny('*', '?') >= 0
                ? FileSystemName.MatchesSimpleExpression(pattern, windowTitle, ignoreCase: true)
                : windowTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase);
            if (match)
                return true;
        }

        return false;
    }
}
