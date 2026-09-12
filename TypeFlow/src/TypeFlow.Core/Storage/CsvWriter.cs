using System.Text;

namespace TypeFlow.Core.Storage;

/// <summary>
/// Byte-exact port of the extension's CSV export (popup.js:291-313): UTF-8 with BOM,
/// header <c>Shortcut,Expansion</c>, quoting only when a field contains a comma or quote,
/// double-quote escaping.
/// </summary>
public static class CsvWriter
{
    public static string Export(IEnumerable<KeyValuePair<string, string>> shortcuts)
    {
        var sb = new StringBuilder();
        sb.Append("Shortcut,Expansion");
        sb.Append('\n');

        foreach (var pair in shortcuts)
        {
            sb.Append(Escape(pair.Key));
            sb.Append(',');
            sb.Append(Escape(pair.Value));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Write with a UTF-8 BOM, so Excel reads embedded ruler/emoji glyphs correctly.</summary>
    public static void ExportToFile(string path, IEnumerable<KeyValuePair<string, string>> shortcuts)
    {
        File.WriteAllText(path, Export(shortcuts), new UTF8Encoding(true));
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }
}