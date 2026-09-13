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
        TestCsvFileEncoding();
        TestCsvWriter();
        TestStoreDefaults();
        TestTypeBuffer();
        TestEngineExpansion();
        TestEngineTriggers();
        TestEngineGates();
        TestEngineUndo();
        TestEngineAltZUndo();
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

        Check(rows.Count > 0, $"parses {rows.Count} rows (got > 0)");

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows) dict[row.Shortcut] = row.Expansion;
        Check(dict.Count == rows.Count, $"all {rows.Count} keys unique (including header row)");

        Check(dict.ContainsKey("(c)") && dict["(c)"] == "\u00A9", "contains '(c)' -> \u00A9");
        Check(dict.ContainsKey("abd") && dict["abd"] == "abdominal", "contains 'abd' -> abdominal");

        // Pruned: report templates, clinical sentences, signatures, clinical phrases gone.
        foreach (var gone in new[] { "adeno", "dexaoo", "cont", "aca", "apc", "ak", "avts" })
        {
            Check(!dict.ContainsKey(gone), $"medical macro removed: {gone}");
        }

        // Kept: generic grammar fixes, typo fixes and accented-word macros.
        Check(dict.ContainsKey("could of been") && dict["could of been"] == "could have been", "kept 'could of been' grammar fix");
        Check(dict.ContainsKey("abbout") && dict["abbout"] == "about", "kept typo fix 'abbout'");
        Check(dict.ContainsKey("vis-a-vis") && dict["vis-a-vis"] == "vis-\u00E0-vis", "kept 'vis-a-vis' accent macro");
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

    // ─── CSV file encoding (UTF-8 BOM / UTF-16 / legacy ANSI) ───

    private static void TestCsvFileEncoding()
    {
        Console.WriteLine("CSV file reading (encoding detection, ANSI fallback)");

        string dir = Path.Combine(Path.GetTempPath(), "typeflow_enc_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        string unique = Path.Combine(dir, "f.csv");

        // Legacy ANSI like Excel's "CSV (Comma delimited)": ©, ®, é plus cp1252
        // smart-quote/trademark/ellipsis bytes that latin-1 alone cannot render.
        File.WriteAllBytes(unique, new byte[] { (byte)'(' , (byte)'c', (byte)')', 0x2C, 0xA9, 0x0D, 0x0A,
                                                 0x72, 0x65, 0x67, 0x2C, 0xAE, 0x0D, 0x0A,
                                                 0x63, 0x61, 0x66, 0x65, 0x2C, 0xE9, 0x0D, 0x0A,
                                                 0x64, 0x6F, 0x6E, 0x74, 0x2C, 0x92, 0x0D, 0x0A,
                                                 0x74, 0x6D, 0x2C, 0x99, 0x0D, 0x0A,
                                                 0x65, 0x75, 0x2C, 0x85, 0x0D, 0x0A });
        var ansi = CsvParser.ReadCsvFile(unique);
        var ansiRows = CsvParser.Parse(ansi);
        Check(ansiRows.Count == 6, $"ANSI file parsed ({ansiRows.Count} rows)");
        var ansiMap = ansiRows.ToDictionary(r => r.Shortcut, r => r.Expansion);
        Check(ansiMap.TryGetValue("(c)", out var c) && c == "\u00A9", "(c) -> \u00A9 decoded from A9 byte");
        Check(ansiMap.TryGetValue("reg", out var r) && r == "\u00AE", "reg -> \u00AE decoded from AE byte");
        Check(ansiMap.TryGetValue("cafe", out var e) && e == "\u00E9", "cafe -> \u00E9 decoded from E9 byte");
        Check(ansiMap.TryGetValue("dont", out var q) && q == "\u2019", "dont -> ' decoded from cp1252 92 byte");
        Check(ansiMap.TryGetValue("tm", out var t) && t == "\u2122", "tm -> \u2122 decoded from cp1252 99 byte");
        Check(ansiMap.TryGetValue("eu", out var el) && el == "\u2026", "eu -> \u2026 decoded from cp1252 85 byte");

        // UTF-8 with BOM (what the app/extension export): BOM must not leak into data.
        string utf8bom = "brb,be right back\nhi,\u00E9\u00E8\u00EA\n";
        File.WriteAllText(unique, utf8bom, new UTF8Encoding(true));
        var bomRows = CsvParser.Parse(CsvParser.ReadCsvFile(unique));
        Check(bomRows.Count == 2 && bomRows[0].Shortcut == "brb", "UTF-8 BOM file: rows parsed, no stray BOM");
        Check(bomRows.Count == 2 && bomRows[1].Expansion == "\u00E9\u00E8\u00EA", "UTF-8 BOM file: accents intact");

        // UTF-8 without BOM.
        File.WriteAllText(unique, "vt,\u00E9\nff,\u2026\u00F1\n", new UTF8Encoding(false));
        var u8 = CsvParser.Parse(CsvParser.ReadCsvFile(unique));
        Check(u8.Count == 2 && u8[0].Expansion == "\u00E9", "plain UTF-8 (no BOM): accents intact");

        // UTF-16 LE with BOM.
        File.WriteAllText(unique, "\uFEFF" + "ut,ok\nac,\u00E7\n", Encoding.Unicode);
        var u16 = CsvParser.Parse(CsvParser.ReadCsvFile(unique));
        Check(u16.Count == 2 && u16[1].Expansion == "\u00E7", "UTF-16 LE: file parsed with accents");

        var import = new ShortcutStore(unique);
        import.ImportCsv(CsvParser.ReadCsvFile(unique));
        Check(import.Count == 2, "store import through encoding-aware read");

        try { Directory.Delete(dir, true); } catch { }
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

        // Unicode (Arabic) word chars: letters + combining marks (harakat) accumulate.
        b.Reset();
        foreach (char c in "\u0645\u0631\u062D\u0628\u0627") b.Append(c); // مرحبا
        Check(b.CurrentWord == "\u0645\u0631\u062D\u0628\u0627", "Arabic letters accumulate into the word");
        b.Append('\u064E'); // fatha (combining mark)
        Check(b.CurrentWord == "\u0645\u0631\u062D\u0628\u0627\u064E", "combining mark keeps word continuity");
        Check(!TypeBuffer.IsWordChar('!'), "delimiter is not a word char (engine resets the run)");

        b.Reset();
        foreach (char c in "\u0627\u0644\u0633\u0644\u0627\u0645") b.Append(c); // السلام
        Check(b.CurrentWord == "\u0627\u0644\u0633\u0644\u0627\u0645", "Arabic word buffered after reset");
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

        // Arabic shortcut expands on Space (Unicode word chars, Unicode-key injection).
        var (ar, _, arSink) = MakeEngine(new Dictionary<string, string> { ["\u0645\u0631\u062D\u0628\u0627"] = "\u0623\u0647\u0644\u0627\u064B \u0628\u0643" });
        ar.SetBufferForTest("\u0645\u0631\u062D\u0628\u0627");
        Check(ar.HandleTriggerForTest(PlainInputField(), ' ') == true, "Arabic shortcut consumed space trigger");
        Check(arSink.TotalBackspaces() == 5, $"Arabic removes the typed shortcut (5 backspaces)");
        Check(arSink.LastText() == "\u0623\u0647\u0644\u0627\u064B \u0628\u0643 ", "Arabic injects expansion + space");
        Check(ar.PeekBufferForTest() == "\u0623\u0647\u0644\u0627\u064B \u0628\u0643 ", "Arabic buffer reflects replacement text");
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

    private static void TestEngineAltZUndo()
    {
        Console.WriteLine("Engine: Alt+Z undo (alternative to Ctrl+Z)");

        var (engine, focus, sink) = MakeEngine();
        focus.Current = PlainInputField();

        engine.SetBufferForTest("brb");
        engine.HandleTriggerForTest(PlainInputField(), ' ');

        // alt down, then Z
        Check(engine.HandleKey(KeySim.Down(0x12)) == false, "alt keydown passes through");
        Check(engine.HandleKey(KeySim.Down(0x5A)) == true, "alt+Z consumed when last action was expansion");

        int backspaces = sink.Backspaces.Sum();
        string lastText = sink.Texts.Count > 0 ? sink.Texts[^1] : string.Empty;
        Check(backspaces == 3 + "be right back ".Length, $"alt+Z undo removed expansion+trigger ({backspaces} backspaces)");
        Check(lastText == "brb ", "alt+Z re-injects shortcut + trigger");
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