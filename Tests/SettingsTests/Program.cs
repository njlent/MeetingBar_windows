using System.Text.Json.Nodes;
using MeetingBar.SettingsApp;

var failed = 0;

Test("saving keeps settings that MeetingBar changed while Settings was open", () =>
{
    const string baseline = """{"WidgetWidth":500,"DismissedEventIds":[],"Language":""}""";
    const string edited = """{"WidgetWidth":380,"DismissedEventIds":[],"Language":""}""";
    const string current = """{"WidgetWidth":500,"DismissedEventIds":["event-1"],"Language":"","AddedByNewerVersion":true}""";
    var merged = JsonNode.Parse(SettingsStore.MergeChanges(baseline, edited, current))!;
    Expect((double)merged["WidgetWidth"]! == 380, "the edited width is saved");
    Expect(merged["DismissedEventIds"]!.AsArray().Count == 1, "the dismissed event is kept");
    Expect((bool)merged["AddedByNewerVersion"]!, "settings unknown to this window are kept");
});

Test("an edit wins over a change made to the same setting meanwhile", () =>
{
    const string baseline = """{"Language":""}""";
    const string edited = """{"Language":"de"}""";
    const string current = """{"Language":"fr"}""";
    var merged = JsonNode.Parse(SettingsStore.MergeChanges(baseline, edited, current))!;
    Expect((string)merged["Language"]! == "de", "the edited language is saved");
});

Test("a missing or unreadable settings file is replaced by the edited settings", () =>
{
    const string edited = """{"WidgetWidth":380}""";
    Expect(SettingsStore.MergeChanges("{}", edited, null) == edited, "a missing file is written in full");
    Expect(SettingsStore.MergeChanges("{}", edited, "not json") == edited, "an unreadable file is written in full");
    Expect(SettingsStore.MergeChanges(null, edited, "{}") == edited, "settings that were never loaded are written in full");
});

Environment.ExitCode = failed == 0 ? 0 : 1;

void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
}

static void Expect(bool condition, string message)
{
    if (!condition) throw new Exception("expected " + message);
}
