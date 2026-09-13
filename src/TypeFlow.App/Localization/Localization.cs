using System.Windows;

namespace TypeFlow.App.Localization;

/// <summary>
/// Runtime language service for the interface. Strings live in per-language
/// resource dictionaries (Languages.en.xaml / Languages.ar.xaml) merged into
/// <see cref="Application"/>. Swapping the active dictionary's Source re-resolves
/// every <c>{DynamicResource …}</c> in the UI ("live switch"). Code paths use
/// <see cref="T"/> / <see cref="Tf"/> to read the same keys.
/// </summary>
public static class L10n
{
    public const string DefaultCode = "en";

    public static event Action? LanguageChanged;

    public static string Code { get; private set; } = DefaultCode;

    public static bool IsRtl => Code == "ar";

    /// <summary>Applies a language code ("en"/"ar"); anything else falls back to English.</summary>
    public static void Apply(string? code)
    {
        string next = code == "ar" ? "ar" : DefaultCode;
        var dict = System.Windows.Application.Current?.Resources.MergedDictionaries.Count > 0
            ? System.Windows.Application.Current.Resources.MergedDictionaries[0]
            : null;
        if (dict != null)
        {
            dict.Source = new Uri($"pack://application:,,,/TypeFlow;component/Localization/Languages.{next}.xaml");
        }
        Code = next;
        LanguageChanged?.Invoke();
    }

    /// <summary>Resolves a UI string key from the active language dictionary.</summary>
    public static string T(string key)
        => System.Windows.Application.Current?.Resources.Contains(key) == true && System.Windows.Application.Current.Resources[key] is string s
            ? s
            : key;

    /// <summary>Resolves a UI string key with format arguments (current UI culture).</summary>
    public static string Tf(string key, params object[] args)
    {
        string template = T(key);
        try { return string.Format(template, args); }
        catch (FormatException) { return template; }
    }
}