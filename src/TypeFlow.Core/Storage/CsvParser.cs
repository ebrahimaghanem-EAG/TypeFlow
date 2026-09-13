using System.Text;

namespace TypeFlow.Core.Storage;

/// <summary>
/// Byte-exact port of the extension's state-machine CSV importer (popup.js:114-190).
/// Handles quoted fields, <c>""</c> escapes, commas inside quotes, CRLF and a final
/// unterminated line. Shortcut is trimmed + lowercased; expansion is trimmed.
/// </summary>
public static class CsvParser
{
    public sealed record CsvRow(string Shortcut, string Expansion);

    /// <summary>
    /// Read a CSV file the way the extension's ecosystem does: honor a UTF-8/UTF-16
    /// BOM, require valid UTF-8 for everything else, and fall back to a byte-1:1
    /// ANSI decode for legacy files (Excel's "CSV (Comma delimited)" writes cp1252).
    /// Mirrors popup.js's <c>readAsText</c> + the <c>excel_to_utf8_converter</c>'s
    /// latin-1 fallback, except the 0x80-0x9F slots are re-mapped to their real
    /// Windows-1252 symbols (<c>™ … ' ' " " – —</c>) instead of C1 control chars.
    /// </summary>
    public static string ReadCsvFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);   // UTF-8 BOM
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);          // UTF-16 LE
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); // UTF-16 BE

        try
        {
            // Strict UTF-8: only accept it if every byte is valid.
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Legacy ANSI — decode ISO-8859-1 1:1 (guaranteed in-box), then map the
            // 0x80-0x9F C1 controls to their Windows-1252 symbols, which is what
            // Excel's "CSV (Comma delimited)" actually contains.
            string s = Encoding.GetEncoding(28591).GetString(bytes);
            var sb = new StringBuilder(s);
            for (int i = 0; i < sb.Length; i++)
            {
                char ch = sb[i];
                if (ch >= '\x80' && ch <= '\x9F')
                    sb[i] = Cp1252[ch - 0x80];
            }
            return sb.ToString();
        }
    }

    /// <summary>Windows-1252 symbols for byte slots 0x80-0x9F (0 = undefined).</summary>
    private static readonly char[] Cp1252 =
    {
        '\u20AC', '\u0000', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
        '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u0000', '\u017D', '\u0000',
        '\u0000', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
        '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u0000', '\u017E', '\u0178'
    };

    public static IReadOnlyList<CsvRow> Parse(string text)
    {
        var rows = new List<CsvRow>();
        if (string.IsNullOrEmpty(text)) return rows;

        var rowData = new List<string>();
        var currentField = new System.Text.StringBuilder();
        bool insideQuotes = false;

        int i = 0;
        int len = text.Length;
        while (i < len)
        {
            char c = text[i];
            char next = (i + 1 < len) ? text[i + 1] : '\0';

            if (c == '"')
            {
                if (insideQuotes && next == '"')
                {
                    currentField.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (c == ',' && !insideQuotes)
            {
                rowData.Add(currentField.ToString());
                currentField.Clear();
            }
            else if ((c == '\r' || c == '\n') && !insideQuotes)
            {
                if (c == '\r' && next == '\n') i++;
                rowData.Add(currentField.ToString());
                EmitRow(rows, rowData);
                rowData = new List<string>();
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
            i++;
        }

        if (currentField.Length > 0 || rowData.Count > 0)
        {
            rowData.Add(currentField.ToString());
            EmitRow(rows, rowData);
        }

        return rows;
    }

    private static void EmitRow(List<CsvRow> rows, List<string> rowData)
    {
        if (rowData.Count < 2) return;
        string shortcut = rowData[0].Trim().ToLowerInvariant();
        string expansion = rowData[1].Trim();
        if (shortcut.Length > 0 && expansion.Length > 0)
        {
            rows.Add(new CsvRow(shortcut, expansion));
        }
    }
}