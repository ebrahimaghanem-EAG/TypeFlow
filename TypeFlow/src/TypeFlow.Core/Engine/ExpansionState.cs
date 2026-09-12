namespace TypeFlow.Core.Engine;

/// <summary>
/// Records the last expansion that TypeFlow performed, enabling the custom Ctrl+Z
/// undo that restores the original shortcut + trigger character
/// (port of the extension's <c>lastExpansionState</c>).
/// </summary>
public sealed class ExpansionState
{
    public long FieldIdentity { get; init; }
    public string ShortcutWord { get; init; } = string.Empty;
    public string TriggerChar { get; init; } = " ";
    public string ExpansionWithTrigger { get; init; } = string.Empty;
    public int CharCount => ExpansionWithTrigger.Length;
}