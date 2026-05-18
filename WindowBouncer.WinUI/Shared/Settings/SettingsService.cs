using System.IO;
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

    // These were previously in the default exclude list. Strip them from saved settings on load
    // so users who never touched their exclude list aren't stuck with stale defaults.
    private static readonly HashSet<string> _legacyExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "shellexperiencehost", "startmenuexperiencehost",
        "searchhost", "lockapp", "logonui", "windowbouncer"
    };

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
            Current.ExcludedProcessNames.RemoveAll(p => _legacyExclusions.Contains(p));
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Non-fatal — settings just won't persist
        }
    }

    public bool IsExcluded(string processName, string windowTitle)
    {
        if (string.IsNullOrEmpty(processName) && string.IsNullOrEmpty(windowTitle))
            return false;

        foreach (var excluded in Current.ExcludedProcessNames)
        {
            if (string.Equals(processName, excluded, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var pattern in Current.ExcludedTitlePatterns)
        {
            if (string.IsNullOrEmpty(pattern))
                continue;
            if (windowTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
