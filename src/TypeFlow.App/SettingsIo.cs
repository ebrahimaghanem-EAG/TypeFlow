using System.IO;
using System.Text.Json;

namespace TypeFlow.App;

/// <summary>Persisted app settings (settings.json). Backwards-compatible with the
/// previous <c>{"isInputEnabled":…}</c> payloads via case-insensitive matching.</summary>
public sealed class AppSettings
{
    public bool IsInputEnabled { get; set; } = true;

    /// <summary>"en" / "ar"; empty string means auto-detect from the OS UI language.</summary>
    public string Language { get; set; } = string.Empty;
}

public static class SettingsIo
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void Save(string path, AppSettings settings)
    {
        try
        {
            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            if (dir.Length > 0) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
        }
        catch { }
    }
}