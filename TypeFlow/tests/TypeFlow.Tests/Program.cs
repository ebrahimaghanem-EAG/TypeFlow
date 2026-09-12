using System.Text;
using TypeFlow.Core.Engine;
using TypeFlow.Core.Focus;
using TypeFlow.Core.Hook;
using TypeFlow.Core.Input;
using TypeFlow.Core.Storage;

namespace TypeFlow.Tests;

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            _pass++;
            Console.WriteLine($"  PASS  {name}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"  FAIL  {name}");
        }
    }

    private static int Main()
    {
        Console.WriteLine("TypeFlow test suite");
        Console.WriteLine("===================");

        TestCsvParser();
        TestCsvParserQuoting();
        TestCsvWriter();
        TestStoreDefaults();
        TestTypeBuffer();
        TestEngineExpansion();
        TestEngineTriggers();
        TestEngineGates();
        TestEngineUndo();
        TestEngineStateClearing();

        Console.WriteLine("===================");
        Console.WriteLine($"Passed: {_pass}, Failed: {_fail}");
        return _fail == 0 ? 0 : 1;
    }

    private static string ResolveBundledCsv()
    {
        // Tests launch from an arbitrary CWD; anchor to the test assembly location.
        string baseDir = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..",
            "src", "TypeFlow.App", "assets", "typeflow_shortcuts.csv"));
    }

    // ─── CSV parser ───

    private static void TestCsvParser()
    {
        Console.WriteLine("CSV parser (bundled dataset)");

        string path = ResolveBundledCsv();
        Check(File.Exists(path), $"bundled CSV exists: {path}");
        if (!File.Exists(path)) return;

        string text = File.ReadAllText(path, Encoding.UTF8);
        var rows = CsvParser.Parse(text);

        Check(rows.Count == 1534, $"parses 1534 rows (header counted as data, like popup.js: got {rows.Count})");

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows) dict[row.Shortcut] = row.Expansion;
        Check(dict.Count == 1534, "all keys unique (including header row)");

        Check(dict.ContainsKey("(c)") && dict["(c)"] == "\u00A9", "contains '(c)' -> \u00A9");
        Check(dict.ContainsKey("abd") && dict["abd"] == "abdominal", "contains 'abd' -> abdominal");
    }

private static void TestCsvParserQuoting()
    {
        Console.WriteLine("CSV parser (quoting / CRLF / escapes)");

        var rows = CsvParser.Parse("\"brb\",\"be right back, seriously\"\r\n\"a\"\"b\",\"x\"\"y\"\r\nlast,\"no newline at end\"");
        Check(rows.Count == 3, $"3 rows parsed (got {rows.Count})");
        Check(rows[0].Shortcut == "brb" && rows[0].Expansion == "be right back, seriously", "quoted field with comma");
        Check(rows[1].Shortcut == "a\"b" && rows[1].Expansion == "x\"y", "escaped double quotes");
        Check(rows[2].Shortcut == "last" && rows[2].Expansion == "no newline at end", "final unterminated line");

        Check(CsvParser.Parse("\r\n\r\n").Count == 0, "blank lines produce no rows");

        var lfOnly = CsvParser.Parse("\"a,b\",c\nz,q");
        Check(lfOnly.Count == 2 && lfOnly[1].Shortcut == "z", "CRLF and LF both handled");
    }

    // ─── CSV writer ───

    private static void TestCsvWriter()
    {
        Console.WriteLine("CSV writer (extension-format parity)");

        var rows = new Dictionary<string, string>
        {
            ["plain"] = "simple",
            ["has,comma"] = "value",
            ["say\"hi"] = "quote\"here",
            ["multi"] = "line\nbreak"
        };

        string csv = CsvWriter.Export(rows);
        Check(csv.StartsWith("Shortcut,Expansion\n", StringComparison.Ordinal), "header present");
        Check(csv.Contains("\"has,comma\""), "comma field quoted");
        Check(csv.Contains("\"say\"\"hi\"") && csv.Contains("\"quote\"\"here\""), "double-quote escaping");
        // Parity: multi-line expansions are NOT quoted (the extension doesn't either),
        // so they split into separate rows on re-import, like the extension.
        Check(csv.Contains("multi,line\n"), "newline expansion written unquoted (parity)");

        var roundTripped = CsvParser.Parse(csv);
        // Header + 4 data rows = 5; the "multi" row splits at its embedded newline into
        // two rows ("multi,line" and a lone "break" field, which the importer skips).
        Check(roundTripped.Count == 5, $"round-trip count {roundTripped.Count} (header + 4 rows; newline row splits)");

        var rt = roundTripped.ToDictionary(r => r.Shortcut, r => r.Expansion);
        Check(rt["has,comma"] == "value", "round-trip comma value");
        Check(rt["say\"hi"] == "quote\"here", "round-trip quote value");

        var store = new ShortcutStore(Path.Combine(Path.GetTempPath(), "typeflow_test_" + Guid.NewGuid()));
        store.Set("key One", "value 1");
        store.Set("key,two", "comm,ma");
        Check(store.Count == 2, "store accepts and lowercases keys");
    }

    // ─── Store defaults (header stripping) ───

    private static void TestStoreDefaults()
    {
        Console.WriteLine("ShortcutStore (defaults bootstrap skips export header)");

        string path = Path.Combine(Path.GetTempPath(), "typeflow_store_" + Guid.NewGuid());
        File.Delete(path);

        string fakeDefaults = "Shortcut,Expansion\n" +
            "abc,airway breathing circulation\n" +
            "wbc,white blood cell\n";
        var store = ShortcutStore.Create(path, fakeDefaults);
        Check(store.Count == 2, $"defaults loaded without header row (count {store.Count})");
        Check(!store.Contains("shortcut"), "no bogus 'shortcut' entry from the header");
        Check(store.Contains("abc") && store.Shortcuts["abc"] == "airway breathing circulation", "defaults content intact");

        // A store file overrides defaults; existing file wins.
        var store2 = new ShortcutStore(path);
        store2.Set("custom", "habit");
        store2.Save();
        var reloaded = ShortcutStore.Create(path, fakeDefaults);
        Check(reloaded.Count == 1 && reloaded.Contains("custom"), "saved store wins over defaults");

        try { File.Delete(path); } catch { }
    }

    // ─── TypeBuffer word grammar (regex parity) ───

    private static void TestTypeBuffer()
    {
        Console.WriteLine("TypeBuffer (word grammar parity with ([a-zA-Z0-9_.\\-]+)$)");

        var b = new TypeBuffer();
        foreach (char c in "hello") b.Append(c); // hello
        b.Reset();                               // space boundary
        foreach (char c in "brb") b.Append(c);   // brb
        Check(b.CurrentWord == "brb", "delimiter boundary: trailing word is 'brb'");

        b.Reset();
        foreach (char c in "hello.brb") b.Append(c); // '.' is a word char
        Check(b.CurrentWord == "hello.brb", "dot is part of the word run (parity)");

        b.Reset();
        foreach (char c in "brb") b.Append(c); b.Append('.'); // '.' typed as part of run
        Check(b.CurrentWord == "brb.", "trailing word includes '.' after dot");

        b.Reset();
        foreach (char c in "hi!x") b.Append(c); b.Reset(); // '!' delimiter
        Check(b.CurrentWord == string.Empty, "non-word char resets run");

        foreach (char c in "b_r-9") b.Append(c);
        Check(b.CurrentWord == "b_r-9", "underscore/dash/digits are word chars");

        b.Backspace();
        Check(b.CurrentWord == "b_r-", "backspace pops one char");
    }

    // ─── Engine: focus stubs ───

    private static FocusedField PlainInputField()
        => new FocusedField { ForegroundWindow = new IntPtr(100), FocusHwnd = new IntPtr(1), ClassName = "Edit", IsPlainInput = true };

    private static FocusedField RichField()
        => new FocusedField { ForegroundWindow = new IntPtr(100), FocusHwnd = new IntPtr(2), ClassName = "RichEdit50W", IsKnownRich = true, IsPlainInput = false };

    private static FocusedField PasswordField()
        => new FocusedField { ForegroundWindow = new IntPtr(100), FocusHwnd = new IntPtr(3), ClassName = "Edit", IsPlainInput = true, IsPassword = true };

    private sealed class StubFocus : IFocusProvider
    {
        public FocusedField Current { get; set; } = PlainInputField();
    }

    private sealed class RecordingSink : IInputSink
    {
        public readonly List<string> Texts = new();
        public readonly List<int> Backspaces = new();
        public readonly List<(int Backspaces, string Text)> Replaces = new();

        public void InjectText(string text) => Texts.Add(text);
        public void InjectBackspaces(int count) => Backspaces.Add(count);
        public void InjectReplace(int backspaceCount, string text)
        {
            Backspaces.Add(backspaceCount);
            Texts.Add(text);
            Replaces.Add((backspaceCount, text));
        }

        public long TotalBackspaces() => Backspaces.Sum();
        public string LastText() => Texts.Count > 0 ? Texts[^1] : string.Empty;
    }

    private sealed class KeySim
    {
        public static GlobalKeyboardHook.KeyInfo Down(int vk) => new GlobalKeyboardHook.KeyInfo
        {
            VirtualKey = vk, ScanCode = 0, Flags = 0, IsInjected = false, IsUp = false
        };

        public static GlobalKeyboardHook.KeyInfo Up(int vk) => new GlobalKeyboardHook.KeyInfo
        {
            VirtualKey = vk, ScanCode = 0, Flags = 0x80, IsInjected = false, IsUp = true
        };
    }

    private static (ShortcutEngine engine, StubFocus focus, RecordingSink sink) MakeEngine(
        IDictionary<string, string>? shortcuts = null)
    {
        var focus = new StubFocus();
        var sink = new RecordingSink();
        var engine = new ShortcutEngine(focus, sink);
        var dict = shortcuts ?? new Dictionary<string, string> { ["brb"] = "be right back" };
        engine.ReplaceShortcuts(dict);
        return (engine, focus, sink);
    }

    // ─── Expansion core ───

    private static void TestEngineExpansion()
    {
        Console.WriteLine("Engine: expansion");

        var (engine, _, sink) = MakeEngine();

        engine.SetBufferForTest("brb");
        Check(engine.HandleTriggerForTest(PlainInputField(), ' ') == true, "space trigger consumed");
        Check(sink.TotalBackspaces() == 3, $"removes the typed shortcut (3 backspaces)");
        Check(sink.LastText() == "be right back ", "injects expansion + space");
        Check(engine.PeekBufferForTest() == "be right back ", "buffer reflects replacement text");

        var (engine2, _, sink2) = MakeEngine();
        engine2.SetBufferForTest("BRB");
        Check(engine2.HandleTriggerForTest(PlainInputField(), '.') == true, "period trigger consumed (case-insensitive)");
        Check(sink2.LastText() == "be right back.", "injects expansion + period");

        var (engine3, _, sink3) = MakeEngine();
        engine3.SetBufferForTest("xyz");
        Check(engine3.HandleTriggerForTest(PlainInputField(), ' ') == false, "no-match trigger passes through");
        Check(sink3.TotalBackspaces() == 0 && sink3.Texts.Count == 0, "no injection on no-match");
        Check(engine3.PeekBufferForTest() == string.Empty, "buffer reset at boundary");

        var (engine4, _, _) = MakeEngine(new Dictionary<string, string> { ["brb"] = "" });
        engine4.SetBufferForTest("brb");
        // dict value is empty string — the extension trims expansion on import, so store never holds empty;
        // engine still returns true and injects just the trigger (parity with Object.assign empty case).
        Check(engine4.HandleTriggerForTest(PlainInputField(), ' ') == true, "empty expansion still consumes trigger");
    }

    private static void TestEngineTriggers()
    {
        Console.WriteLine("Engine: whole-word matching (maximal munch)");

        var (engine, _, sink) = MakeEngine();
        engine.SetBufferForTest("brba");
        Check(engine.HandleTriggerForTest(PlainInputField(), ' ') == false, "'brba' does not match shortcut 'brb'");
        Check(sink.TotalBackspaces() == 0 && sink.Texts.Count == 0, "no injection for longer word");
        Check(engine.PeekBufferForTest() == string.Empty, "boundary reset after no-match trigger");
    }

    private static void TestEngineGates()
    {
        Console.WriteLine("Engine: gates (isEnabled / isInputEnabled / password)");

        var (engine, _, _) = MakeEngine();
        Check(engine.IsFieldEligibleForExpansion(PlainInputField()) == true, "plain input eligible by default");

        engine.IsInputEnabled = false;
        Check(engine.IsFieldEligibleForExpansion(PlainInputField()) == false, "plain input blocked when toggle off");
        Check(engine.IsFieldEligibleForExpansion(RichField()) == true, "rich/editor not blocked by input toggle");

        engine.IsInputEnabled = true;
        Check(engine.IsFieldEligibleForExpansion(PasswordField()) == false, "password never eligible");
    }

    private static void TestEngineUndo()
    {
        Console.WriteLine("Engine: custom Ctrl+Z undo");

        var (engine, focus, sink) = MakeEngine();
        focus.Current = PlainInputField();

        engine.SetBufferForTest("brb");
        engine.HandleTriggerForTest(PlainInputField(), ' ');

        // ctrl down, then Z
        Check(engine.HandleKey(KeySim.Down(0x11)) == false, "ctrl keydown passes through");
        Check(engine.HandleKey(KeySim.Down(0x5A)) == true, "ctrl+Z consumed when last action was expansion");

        int backspaces = sink.Backspaces.Sum();
        string lastText = sink.Texts.Count > 0 ? sink.Texts[^1] : string.Empty;
        Check(backspaces == 3 + "be right back ".Length, $"undo removed expansion+trigger ({(sink.Backspaces.Sum())} backspaces)");
        Check(lastText == "brb ", "undo re-injects shortcut + trigger");

        // second ctrl+Z has no expansion state → pass through untouched
        int textsBefore = sink.Texts.Count;
        Check(engine.HandleKey(KeySim.Down(0x5A)) == false, "second ctrl+Z passes through (no state)");
        Check(sink.Texts.Count == textsBefore, "no injection on stray ctrl+Z");

        // wrong field → undo must not fire
        var (engine2, focus2, sink2) = MakeEngine();
        focus2.Current = PlainInputField();
        engine2.SetBufferForTest("brb");
        engine2.HandleTriggerForTest(PlainInputField(), ' ');
        focus2.Current = RichField(); // focus moved
        Check(engine2.HandleKey(KeySim.Down(0x11)) == false, "ctrl down (field 2)");
        Check(engine2.HandleKey(KeySim.Down(0x5A)) == false, "ctrl+Z passes through when focus changed");
    }

    private static void TestEngineStateClearing()
    {
        Console.WriteLine("Engine: lastExpansionState clearing parity");

        var (engine, focus, sink) = MakeEngine();
        focus.Current = PlainInputField();

        engine.SetBufferForTest("brb");
        engine.HandleTriggerForTest(PlainInputField(), ' ');

        // Backspace destroys the caret context → expansion state cleared.
        Check(engine.HandleKey(KeySim.Down(0x08)) == false, "backspace passes through");
        int textsBefore = sink.Texts.Count;
        Check(engine.HandleKey(KeySim.Down(0x11)) == false, "ctrl down");
        Check(engine.HandleKey(KeySim.Down(0x5A)) == false, "ctrl+Z not consumed after context-breaking key");
        Check(sink.Texts.Count == textsBefore, "no undo injection after state cleared");

        // Focus change resets state.
        var (engine2, focus2, sink2) = MakeEngine();
        focus2.Current = PlainInputField();
        engine2.SetBufferForTest("brb");
        engine2.HandleTriggerForTest(PlainInputField(), ' ');
        engine2.Reset();
        Check(engine2.HandleKey(KeySim.Down(0x11)) == false && engine2.HandleKey(KeySim.Down(0x5A)) == false,
            "ctrl+Z passes through after Reset()");
    }
}