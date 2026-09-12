using System.Text;

namespace TypeFlow.Core.Engine;

/// <summary>
/// Rolling buffer of the trailing "word" under the caret. Word characters mirror the
/// extension's pattern <c>([a-zA-Z0-9_.\-]+)</c>. Non-word printable characters act
/// as boundaries (the extension's regex anchors to <c>$</c> through any delimiter),
/// so any delimiter resets the trailing run, recreating maximal-munch behaviour.
/// </summary>
public sealed class TypeBuffer
{
    private readonly StringBuilder _trailing = new StringBuilder(64);

    public static bool IsWordChar(char c)
        => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
           || c == '_' || c == '.' || c == '-';

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