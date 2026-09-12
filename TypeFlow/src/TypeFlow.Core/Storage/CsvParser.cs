namespace TypeFlow.Core.Storage;

/// <summary>
/// Byte-exact port of the extension's state-machine CSV importer (popup.js:114-190).
/// Handles quoted fields, <c>""</c> escapes, commas inside quotes, CRLF and a final
/// unterminated line. Shortcut is trimmed + lowercased; expansion is trimmed.
/// </summary>
public static class CsvParser
{
    public sealed record CsvRow(string Shortcut, string Expansion);

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