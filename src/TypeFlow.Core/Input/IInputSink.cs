using TypeFlow.Core.Focus;

namespace TypeFlow.Core.Input;

/// <summary>Where synthetic input is delivered (real: SendInput; tests: recording stub).</summary>
public interface IInputSink
{
    void InjectText(string text);
    void InjectBackspaces(int count);
    void InjectReplace(int backspaceCount, string text);
}

/// <summary>SendInput-backed sink used by the running app.</summary>
public sealed class SendInputSink : IInputSink
{
    public static readonly SendInputSink Instance = new SendInputSink();

    public void InjectText(string text) => TextInjector.InjectText(text);
    public void InjectBackspaces(int count) => TextInjector.InjectBackspaces(count);
    public void InjectReplace(int backspaceCount, string text) => TextInjector.InjectReplace(backspaceCount, text);
}