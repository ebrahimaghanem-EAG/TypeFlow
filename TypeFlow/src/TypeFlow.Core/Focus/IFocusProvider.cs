namespace TypeFlow.Core.Focus;

/// <summary>Abstraction over the focused-control snapshot so the engine is testable.</summary>
public interface IFocusProvider
{
    FocusedField Current { get; }
}