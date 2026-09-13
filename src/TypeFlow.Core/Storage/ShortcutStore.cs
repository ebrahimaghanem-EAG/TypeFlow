using System.Text.Json;

namespace TypeFlow.Core.Storage;

/// <summary>
/// Persistent shortcut dictionary (JSON), mirroring <c>chrome.storage.local</c>.
/// Keys are always lowercase. Writes are atomic (temp file + replace).
/// </summary>
public sealed class ShortcutStore
{
    private readonly string _path;

    public Dictionary<string, string> Shortcuts { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public int Count => Shortcuts.Count;

    public ShortcutStore(string path)
    {
        _path = path;
    }

    public static ShortcutStore Create(string path, string? defaultsCsv, string? arabicDefaultsCsv = null)
    {
        var store = new ShortcutStore(path);
        store.Load(defaultsCsv, arabicDefaultsCsv);
        return store;
    }

    public void Load(string? defaultsCsv, string? arabicDefaultsCsv = null)
    {
        if (File.Exists(_path))
        {
            try
            {
                string json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (loaded != null)
                {
                    Shortcuts = new Dictionary<string, string>(loaded, StringComparer.Ordinal);
                    // Clean up stale header row that was mistakenly saved as an entry.
                    Shortcuts.Remove("shortcut");
                    return;
                }
            }
            catch
            {
                // corrupted store → fall through to defaults
            }
        }

        // Load English defaults
        LoadCsv(defaultsCsv);
        // Load Arabic autocorrect (merges over English if there are overlaps)
        LoadCsv(arabicDefaultsCsv);
        try { Save(); } catch { }
    }

    private void LoadCsv(string? csvText)
    {
        if (string.IsNullOrEmpty(csvText)) return;

        string csv = csvText.TrimStart('\uFEFF');
        int nl = csv.IndexOf('\n');
        if (nl >= 0)
        {
            string first = csv.Substring(0, nl).TrimEnd('\r');
            if (string.Equals(first, "Shortcut,Expansion", StringComparison.OrdinalIgnoreCase))
            {
                csv = csv.Substring(nl + 1);
            }
        }

        var rows = CsvParser.Parse(csv);
        foreach (var row in rows)
        {
            // Safety: skip any row that is literally the CSV header row.
            if (string.Equals(row.Shortcut, "shortcut", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Expansion, "expansion", StringComparison.OrdinalIgnoreCase))
                continue;
            Shortcuts[row.Shortcut] = row.Expansion;
        }
    }

    public void ClearAll()
    {
        Shortcuts = new Dictionary<string, string>(StringComparer.Ordinal);
        Save();
    }

    public bool Contains(string key) => Shortcuts.ContainsKey(key);

    public bool Set(string shortcut, string expansion)
    {
        string key = shortcut.Trim().ToLowerInvariant();
        if (key.Length == 0) return false;
        Shortcuts[key] = expansion;
        return true;
    }

    public bool Remove(string shortcut)
    {
        return Shortcuts.Remove(shortcut.Trim().ToLowerInvariant());
    }

    /// <summary>Import CSV rows, merging over existing entries (import wins, like Object.assign).</summary>
    public int ImportCsv(string csvText)
    {
        var rows = CsvParser.Parse(csvText);
        foreach (var row in rows)
        {
            if (string.Equals(row.Shortcut, "shortcut", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Expansion, "expansion", StringComparison.OrdinalIgnoreCase))
                continue;
            Shortcuts[row.Shortcut] = row.Expansion;
        }
        Save();
        return rows.Count;
    }

    public string ExportCsv() => CsvWriter.Export(Shortcuts);

    public void Save()
    {
        string? dir = Path.GetDirectoryName(_path);
        if (dir is { Length: > 0 } && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(Shortcuts, new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(_path))
        {
            File.Replace(tmp, _path, null, true);
        }
        else
        {
            File.Move(tmp, _path);
        }
    }
}