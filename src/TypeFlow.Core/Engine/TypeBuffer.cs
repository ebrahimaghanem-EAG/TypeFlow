using System.Globalization;
using System.Text;

namespace TypeFlow.Core.Engine;

/// <summary>
/// Rolling buffer of the trailing "word" under the caret. Word characters mirror the
/// extension's pattern <c>([a-zA-Z0-9_.\-]+)</c> plus every Unicode letter/digit and
/// combining mark, so Arabic (and other non-Latin) shortcuts accumulate. Non-word
/// printable characters act as boundaries (the extension's regex anchors to <c>$</c>
/// through any delimiter), so any delimiter resets the trailing run, recreating
/// maximal-munch behaviour.
/// </summary>
public sealed class TypeBuffer
{
    private readonly StringBuilder _trailing = new StringBuilder(64);

    public static bool IsWordChar(char c)
    {
        switch (CharUnicodeInfo.GetUnicodeCategory(c))
        {
            case UnicodeCategory.UppercaseLetter:
            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.OtherLetter:            // Arabic, Cyrillic, CJK, …
            case UnicodeCategory.DecimalDigitNumber:
            case UnicodeCategory.NonSpacingMark:          // Arabic harakat / combining diacritics
            case UnicodeCategory.SpacingCombiningMark:
            case UnicodeCategory.EnclosingMark:
                return true;
            default:
                return c is '_' or '.' or '-';
        }
    }

    public void Append(char c)
    {
        if (_trailing.Length >= 64) _trailing.Remove(_trailing.Length - 1, 1);
        _trailing.Append(c);
    }

    public void Reset() => _trailing.Clear();

    public void Backspace()
    {
        if (_trailing.Length > 0) _trailing.Remove(_trailing.Length - 1, 1);
    }

    public string CurrentWord => _trailing.ToString();

    public int CurrentWordLength => _trailing.Length;

    /// <summary>
    /// Replaces the buffered trailing text with <paramref name="text"/> (used after an
    /// expansion so that text already replaced can be the *start* of a further match).
    /// </summary>
    public void ReplaceTrailingWord(string text)
    {
        _trailing.Clear();
        foreach (char c in text)
        {
            Append(c);
        }
    }
}