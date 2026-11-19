using System.Text.Json;
using WinOrbit.Services;
using Xunit;

namespace WinOrbit.Tests;

public class AgentTests
{
    private static AgentEvent? Claude(string json) => AgentHook.Translate("claude", json, 1000);

    [Fact]
    public void A_prompt_starts_work_and_names_the_session()
    {
        var e = Claude("""{"session_id":"s1","cwd":"C:\\code\\storefront","hook_event_name":"UserPromptSubmit","prompt":"Fix tax rounding at checkout","transcript_path":"x"}""")!;
        Assert.Equal(AgentState.Working, e.State);
        Assert.Equal("s1", e.Session);
        Assert.Equal("Fix tax rounding at checkout", e.Title);
        Assert.Equal("storefront", e.Folder);
        Assert.Equal("claude", e.Agent);
    }

    [Fact]
    public void A_long_prompt_is_cut_to_one_short_line()
    {
        var prompt = string.Join("\\n", Enumerable.Repeat("make the build faster please", 20));
        var e = Claude($$"""{"session_id":"s1","hook_event_name":"UserPromptSubmit","prompt":"{{prompt}}"}""")!;
        Assert.True(e.Title.Length <= AgentEvent.MaxTitle);
        Assert.DoesNotContain('\n', e.Title);
        Assert.EndsWith("…", e.Title);
    }

    [Fact]
    public void Steps_show_the_action_and_only_the_file_name()
    {
        var edit = Claude("""{"session_id":"s1","hook_event_name":"PostToolUse","tool_name":"Edit","tool_input":{"file_path":"C:\\code\\app\\IslandWindow.cs","old_string":"secret"}}""")!;
        Assert.Equal(AgentState.Working, edit.State);
        Assert.Equal("Editing IslandWindow.cs", edit.Text);
        Assert.Equal("", edit.Title);
        var bash = Claude("""{"session_id":"s1","hook_event_name":"PostToolUse","tool_name":"Bash","tool_input":{"command":"rm -rf build"}}""")!;
        Assert.Equal("Running a command", bash.Text);
        var other = Claude("""{"session_id":"s1","hook_event_name":"PostToolUse","tool_name":"mcp__github__create_issue"}""")!;
        Assert.StartsWith("Using ", other.Text);
    }

    [Fact]
    public void Permission_requests_need_you_but_idle_reminders_change_nothing()
    {
        var ask = Claude("""{"session_id":"s1","hook_event_name":"Notification","message":"Claude needs your permission to use Bash","notification_type":"permission_prompt"}""")!;
        Assert.Equal(AgentState.NeedsYou, ask.State);
        Assert.Equal("Claude needs your permission to use Bash", ask.Text);
        Assert.Null(Claude("""{"session_id":"s1","hook_event_name":"Notification","message":"Claude is waiting for your input","notification_type":"idle_prompt"}"""));
        var codex = AgentHook.Translate("codex", """{"session_id":"t","hook_event_name":"PermissionRequest","tool_name":"Bash"}""", 1)!;
        Assert.Equal(AgentState.NeedsYou, codex.State);
        Assert.Equal("Wants to use Bash", codex.Text);
    }

    [Fact]
    public void Stop_is_done_and_session_end_removes_it()
    {
        Assert.Equal(AgentState.Done, Claude("""{"session_id":"s1","hook_event_name":"Stop","last_assistant_message":"long answer"}""")!.State);
        Assert.Equal("Finished", Claude("""{"session_id":"s1","hook_event_name":"Stop","last_assistant_message":"long answer"}""")!.Text);
        Assert.Equal(AgentState.Ended, Claude("""{"session_id":"s1","hook_event_name":"SessionEnd","reason":"prompt_input_exit"}""")!.State);
        Assert.Null(Claude("""{"session_id":"s1","hook_event_name":"SessionStart","source":"startup"}"""));
    }

    [Fact]
    public void Codex_notify_and_other_tools_are_understood()
    {
        var codex = AgentHook.Translate("codex", """{"type":"agent-turn-complete","thread-id":"th1","turn-id":"t9","cwd":"/home/me/api","input-messages":["x"],"last-assistant-message":"y"}""", 5)!;
        Assert.Equal(("th1", AgentState.Done, "api"), (codex.Session, codex.State, codex.Folder));
        var cursor = AgentHook.Translate("cursor", """{"conversation_id":"c1","hook_event_name":"beforeSubmitPrompt","prompt":"Add tests","workspace_roots":["D:/work/shop"]}""", 5)!;
        Assert.Equal(("c1", AgentState.Working, "Add tests", "shop"), (cursor.Session, cursor.State, cursor.Title, cursor.Folder));
        var gemini = AgentHook.Translate("gemini", """{"session_id":"g","hook_event_name":"AfterAgent"}""", 5)!;
        Assert.Equal(AgentState.Done, gemini.State);
    }

    [Fact]
    public void Your_own_tool_can_use_the_plain_format()
    {
        var e = AgentHook.Translate("Build", """{"session":"nightly","state":"needs-you","title":"Nightly build","text":"Sign the installer?"}""", 5)!;
        Assert.Equal(("build", "nightly", AgentState.NeedsYou, "Nightly build", "Sign the installer?"), (e.Agent, e.Session, e.State, e.Title, e.Text));
        Assert.Null(AgentHook.Translate("x", """{"session":"a","state":"exploded"}""", 5));
        Assert.Null(AgentHook.Translate("x", "not json", 5));
        Assert.Null(AgentHook.Translate("x", "[1,2]", 5));
    }

    [Fact]
    public void Pipe_messages_round_trip_and_cards_are_not_mistaken_for_agents()
    {
        var e = new AgentEvent("claude", "s1", AgentState.NeedsYou, "Title", "Allow Bash?", "api", 123);
        Assert.Equal(e, AgentEvent.Parse(e.ToJson(), out var isAgent, out _));
        Assert.True(isAgent);
        Assert.Null(AgentEvent.Parse("""{"title":"Backup done"}""", out isAgent, out _));
        Assert.False(isAgent);
        Assert.Null(AgentEvent.Parse("""{"agent":"claude","state":"done"}""", out isAgent, out var error));
        Assert.True(isAgent);
        Assert.Contains("session", error);
        Assert.Null(AgentEvent.Parse("""{"agent":"claude","session":"a","state":"sleeping"}""", out _, out error));
        Assert.Contains("state", error);
    }

    private static AgentEvent Ev(string session, AgentState state, long at, string title = "", string text = "") => new("claude", session, state, title, text, "app", at);

    [Fact]
    public void The_board_keeps_the_first_title_and_ignores_late_updates()
    {
        var list = new List<AgentSession>();
        var now = new DateTime(2026, 11, 19, 12, 0, 0);
        AgentBoard.Update(list, Ev("s1", AgentState.Working, 10, "First request"), now);
        AgentBoard.Update(list, Ev("s1", AgentState.Working, 20, "Second request", "Editing a.cs"), now);
        Assert.Equal("First request", list[0].Title);
        Assert.Equal("Editing a.cs", list[0].Text);
        AgentBoard.Update(list, Ev("s1", AgentState.Done, 40), now);
        // A step's hook that started earlier but arrived after "done" must not flip it back.
        Assert.Null(AgentBoard.Update(list, Ev("s1", AgentState.Working, 30), now));
        Assert.Equal(AgentState.Done, list[0].State);
        AgentBoard.Update(list, Ev("s1", AgentState.Ended, 50), now);
        Assert.Empty(list);
    }

    [Fact]
    public void Sessions_that_need_you_come_first_and_old_ones_drop_off()
    {
        var list = new List<AgentSession>();
        var now = new DateTime(2026, 11, 19, 12, 0, 0);
        AgentBoard.Update(list, Ev("done", AgentState.Done, 1), now);
        AgentBoard.Update(list, Ev("busy", AgentState.Working, 1), now.AddMinutes(1));
        AgentBoard.Update(list, Ev("ask", AgentState.NeedsYou, 1), now.AddMinutes(2));
        Assert.Equal(new[] { "ask", "busy", "done" }, list.Select(s => s.Session));
        AgentBoard.Update(list, Ev("new", AgentState.Working, 1), now + AgentBoard.Forget + TimeSpan.FromMinutes(1.5));
        // "done" and "busy" were silent for over three hours; "ask" only just under.
        Assert.Equal(new[] { "ask", "new" }, list.Select(s => s.Session));
    }

    [Fact]
    public void The_list_stays_short_but_keeps_sessions_waiting_for_you()
    {
        var list = new List<AgentSession>();
        var now = new DateTime(2026, 11, 19, 12, 0, 0);
        AgentBoard.Update(list, Ev("ask", AgentState.NeedsYou, 1), now);
        for (var i = 0; i < AgentBoard.MaxSessions + 3; i++) AgentBoard.Update(list, Ev("s" + i, AgentState.Done, 1), now.AddSeconds(i + 1));
        Assert.Equal(AgentBoard.MaxSessions, list.Count);
        Assert.Contains(list, s => s.Session == "ask");
    }

    [Fact]
    public void Claude_code_setup_is_valid_json_with_background_hooks()
    {
        var json = AgentSetup.ClaudeCode(@"C:\Users\SOMEON~1\AppData\Local\Programs\WinOrbit\WinOrbit.exe");
        using var doc = JsonDocument.Parse(json);
        var hooks = doc.RootElement.GetProperty("hooks");
        foreach (var name in new[] { "UserPromptSubmit", "PostToolUse", "Notification", "Stop", "SessionEnd" })
        {
            var h = hooks.GetProperty(name)[0].GetProperty("hooks")[0];
            Assert.Equal("command", h.GetProperty("type").GetString());
            Assert.True(h.GetProperty("async").GetBoolean());
            Assert.Equal("powershell", h.GetProperty("shell").GetString());
            // Unquoted: PowerShell, cmd and Git Bash all run it as is.
            Assert.Equal("C:/Users/SOMEON~1/AppData/Local/Programs/WinOrbit/WinOrbit.exe --agent claude", h.GetProperty("command").GetString());
        }
        Assert.Equal(@"notify = ['C:\x\WinOrbit.exe', '--agent', 'codex']", AgentSetup.Codex(@"C:\x\WinOrbit.exe"));
    }

    [Fact]
    public void Agent_updates_get_a_bigger_allowance_than_cards()
    {
        var gate = new RateGate(burst: 12, every: 0.25, totalBurst: 40, totalEvery: 0.1);
        var now = new DateTime(2026, 1, 1);
        Assert.Equal(12, Enumerable.Range(0, 20).Count(_ => gate.Allow("claude/s1", now)));
        Assert.True(gate.Allow("claude/s1", now.AddSeconds(0.3)));
        var cards = new RateGate();
        Assert.Equal((int)RateGate.Burst, Enumerable.Range(0, 20).Count(_ => cards.Allow("pwsh", now)));
    }

    [Fact]
    public void Input_is_complete_once_one_whole_object_has_arrived()
    {
        bool Done(string s) => AgentClient.CompleteJson(System.Text.Encoding.UTF8.GetBytes(s));
        Assert.True(Done("{\"session_id\":\"a\",\"x\":{\"y\":1}}\r\n"));
        Assert.False(Done("{\"session_id\":\"a\",\"x\":{\"y\":1}"));
        Assert.False(Done("{\"prompt\":\"use } here\""));
        Assert.False(Done(""));
    }

    [Fact]
    public void Times_read_naturally()
    {
        Assert.Equal("just now", AgentBoard.Ago(TimeSpan.FromSeconds(20)));
        Assert.Equal("4 min", AgentBoard.Ago(TimeSpan.FromMinutes(4.2)));
        Assert.Equal("2 h", AgentBoard.Ago(TimeSpan.FromMinutes(130)));
        Assert.Equal("Claude Code", AgentBoard.DisplayName("claude"));
        Assert.Equal("Build", AgentBoard.DisplayName("build"));
    }
}
