using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace WinOrbit.Services;

public enum AgentState { Working, NeedsYou, Done, Ended }

/// <summary>
/// One status update from a coding agent (Claude Code, Codex…) running on this PC, in the small format the local pipe
/// accepts: <c>{"agent":"claude","session":"…","state":"working","title":"…","text":"…","folder":"…","at":…}</c>.
/// </summary>
public sealed record AgentEvent(string Agent, string Session, AgentState State, string Title, string Text, string Folder, long At)
{
    internal const int MaxAgent = 24, MaxSession = 80, MaxTitle = 60, MaxText = 80, MaxFolder = 40;

    internal static readonly (AgentState State, string Name)[] States =
        { (AgentState.Working, "working"), (AgentState.NeedsYou, "needs-you"), (AgentState.Done, "done"), (AgentState.Ended, "ended") };

    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["agent"] = Agent, ["session"] = Session, ["state"] = States.First(s => s.State == State).Name,
        ["title"] = Title, ["text"] = Text, ["folder"] = Folder, ["at"] = At,
    });

    /// <summary>
    /// Reads one pipe line. <paramref name="isAgent"/> says whether it was meant as an agent update at all (it has an
    /// "agent" field); when it was but can't be used, <paramref name="error"/> says why. All text is cleaned to plain,
    /// single-line text and cut to length.
    /// </summary>
    internal static AgentEvent? Parse(string json, out bool isAgent, out string error)
    {
        isAgent = false;
        error = "";
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 }); }
        catch (JsonException) { error = "not valid JSON"; return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("agent", out _)) return null;
            isAgent = true;
            string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var agent = IslandPipe.Clean(Str("agent"), MaxAgent);
            var session = IslandPipe.Clean(Str("session"), MaxSession);
            if (agent.Length == 0) { error = "\"agent\" is required"; return null; }
            if (session.Length == 0) { error = "\"session\" is required"; return null; }
            var stateName = Str("state").Trim().ToLowerInvariant();
            if (!States.Any(s => s.Name == stateName)) { error = "\"state\" must be working, needs-you, done or ended"; return null; }
            var at = root.TryGetProperty("at", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetInt64(out var t) ? t : 0;
            return new AgentEvent(agent, session, States.First(s => s.Name == stateName).State,
                IslandPipe.Clean(Str("title"), MaxTitle), IslandPipe.Clean(Str("text"), MaxText), IslandPipe.Clean(Str("folder"), MaxFolder), at);
        }
    }
}

/// <summary>
/// Turns what an AI tool hands its hook command into an <see cref="AgentEvent"/>. Understands Claude Code and Codex
/// hooks (JSON on standard input: <c>hook_event_name</c>, <c>session_id</c>, <c>cwd</c>…), Codex's <c>notify</c>
/// payload (<c>type</c> = <c>agent-turn-complete</c>, <c>thread-id</c>), the similar hook names of Gemini CLI and Cursor,
/// and a plain format for your own tools (<c>session</c>, <c>state</c>, <c>title</c>, <c>text</c>, <c>folder</c>).
/// Only a short title (the session's first request), the project folder's name and a few words of status are kept;
/// prompts, answers and file contents never leave the hook.
/// </summary>
internal static class AgentHook
{
    internal static AgentEvent? Translate(string agent, string json, long at)
    {
        agent = IslandPipe.Clean(agent, AgentEvent.MaxAgent).ToLowerInvariant();
        if (agent.Length == 0) agent = "agent";
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 }); }
        catch (JsonException) { return null; }
        using (doc)
        {
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            string Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

            var session = First(Str("session_id"), Str("conversation_id"), Str("thread-id"), Str("session"));
            var cwd = First(Str("cwd"), Str("folder"));
            if (cwd.Length == 0 && r.TryGetProperty("workspace_roots", out var roots) && roots.ValueKind == JsonValueKind.Array && roots.GetArrayLength() > 0 && roots[0].ValueKind == JsonValueKind.String)
                cwd = roots[0].GetString() ?? "";
            var folder = FolderName(cwd);
            if (session.Length == 0) session = folder.Length > 0 ? folder : agent;
            var title = First(Str("session_title"), Str("title"));

            AgentState state;
            var text = "";
            var hook = First(Str("hook_event_name"), Str("type"));
            switch (hook)
            {
                case "UserPromptSubmit" or "BeforeAgent" or "beforeSubmitPrompt":
                    state = AgentState.Working;
                    if (title.Length == 0) title = Str("prompt");
                    break;
                case "PreToolUse" or "PostToolUse" or "BeforeTool" or "AfterTool" or "preToolUse" or "postToolUse" or "afterFileEdit":
                    state = AgentState.Working;
                    text = ToolAction(Str("tool_name"), r);
                    break;
                case "Notification":
                    // Claude Code also notifies "waiting for your input" after a minute idle and "signed in"; those
                    // aren't a request for a decision, so the row keeps its state.
                    var kind = Str("notification_type");
                    if (kind is "idle_prompt" or "auth_success") return null;
                    state = AgentState.NeedsYou;
                    text = First(Str("message"), "Needs your attention");
                    break;
                case "PermissionRequest":
                    state = AgentState.NeedsYou;
                    var tool = Str("tool_name");
                    text = tool.Length > 0 ? $"Wants to use {tool}" : "Wants your permission";
                    break;
                case "Stop" or "AfterAgent" or "stop" or "agent-turn-complete":
                    state = AgentState.Done;
                    text = "Finished";
                    break;
                case "SessionEnd" or "sessionEnd":
                    state = AgentState.Ended;
                    break;
                case "":
                    // Your own tool: {"session":"…","state":"working","title":"…","text":"…","folder":"…"}
                    var s = AgentEvent.States.FirstOrDefault(x => x.Name == Str("state").Trim().ToLowerInvariant());
                    if (s.Name is null) return null;
                    state = s.State;
                    text = Str("text");
                    break;
                default:
                    return null;   // SessionStart, compaction and the rest change nothing a person needs to see
            }
            return new AgentEvent(agent, IslandPipe.Clean(session, AgentEvent.MaxSession), state,
                IslandPipe.Clean(title, AgentEvent.MaxTitle), IslandPipe.Clean(text, AgentEvent.MaxText), IslandPipe.Clean(folder, AgentEvent.MaxFolder), at);
        }
    }

    private static string First(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    internal static string FolderName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var p = path.Trim().TrimEnd('\\', '/');
        var cut = p.LastIndexOfAny(new[] { '\\', '/' });
        return cut >= 0 ? p[(cut + 1)..] : p;
    }

    /// <summary>"Editing Island.cs", "Running a command", "Reading notes.md": the tool and, for files, only the file's name.</summary>
    internal static string ToolAction(string tool, JsonElement root)
    {
        var file = "";
        if (root.TryGetProperty("tool_input", out var input) && input.ValueKind == JsonValueKind.Object)
            foreach (var key in new[] { "file_path", "path", "notebook_path" })
                if (input.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) { file = FolderName(v.GetString() ?? ""); break; }
        if (file.Length == 0 && root.TryGetProperty("file_path", out var fp) && fp.ValueKind == JsonValueKind.String) file = FolderName(fp.GetString() ?? "");
        var verb = tool switch
        {
            "Edit" or "MultiEdit" or "Write" or "NotebookEdit" or "write_file" or "replace" or "apply_patch" => "Editing",
            "" when file.Length > 0 => "Editing",
            "Read" or "read_file" => "Reading",
            "Bash" or "PowerShell" or "shell" or "run_shell_command" => "Running a command",
            "Grep" or "Glob" or "search_file_content" or "glob" => "Searching",
            "WebFetch" or "WebSearch" or "web_fetch" or "google_web_search" => "Looking something up",
            "Task" or "Agent" => "Working with a helper",
            "" => "Working",
            _ => "Using " + tool,
        };
        return verb is "Editing" or "Reading" && file.Length > 0 ? $"{verb} {file}" : verb;
    }
}

/// <summary>
/// <c>WinOrbit.exe --agent claude</c>: the tiny command an AI tool's hook runs. It reads the tool's JSON (standard
/// input, or the last argument for Codex's <c>notify</c>), keeps only the few fields the island shows, hands them to the
/// running WinOrbit through the local pipe and exits at once. It never starts WinOrbit, never prints anything (a hook's
/// output can be read back by the AI tool) and always exits 0, so it can't block or change what the agent does.
/// </summary>
internal static class AgentClient
{
    private const int MaxInput = 1 << 20;

    public static void Run(string[] args)
    {
        try
        {
            var i = Array.FindIndex(args, a => a.Equals("--agent", StringComparison.OrdinalIgnoreCase));
            var agent = i + 1 < args.Length && !args[i + 1].StartsWith('{') ? args[i + 1] : "agent";
            var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var json = args.Length > 0 && args[^1].TrimStart().StartsWith('{') ? args[^1] : ReadInput();
            if (string.IsNullOrWhiteSpace(json) || AgentHook.Translate(agent, json, at) is not { } ev) return;
            using var pipe = new NamedPipeClientStream(".", IslandPipe.PipeName, PipeDirection.InOut);
            pipe.Connect(400);   // WinOrbit not running, or coding agents off: give up quietly
            var line = Encoding.UTF8.GetBytes(ev.ToJson() + "\n");
            pipe.Write(line);
            pipe.Flush();
            var reply = new byte[128];
            pipe.ReadTimeout = 1000;
            _ = pipe.Read(reply);
        }
        catch { /* never get in the agent's way */ }
    }

    /// <summary>
    /// Reads until the input ends or holds one complete JSON object — PowerShell, piping into a windowed program, writes
    /// the text but never closes the input. Gives up after two seconds. The stream is left open on purpose: closing it
    /// while a read is pending can block, and the process ends right after anyway.
    /// </summary>
    private static string ReadInput()
    {
        if (!Console.IsInputRedirected) return "";
        var stdin = Console.OpenStandardInput();
        var task = Task.Run(() =>
        {
            var ms = new MemoryStream();
            var buffer = new byte[16384];
            int n;
            while ((n = stdin.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, n);
                if (ms.Length >= MaxInput) return "";
                if (CompleteJson(ms.GetBuffer().AsSpan(0, (int)ms.Length))) break;
            }
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        });
        return task.Wait(2000) ? task.Result : "";
    }

    /// <summary>Whether the bytes so far are one whole JSON value (so there's no need to wait for the end of the input).</summary>
    internal static bool CompleteJson(ReadOnlySpan<byte> utf8)
    {
        var trimmed = utf8.TrimEnd("\r\n \t"u8);
        if (trimmed.Length == 0 || trimmed[^1] != (byte)'}') return false;
        try
        {
            var reader = new Utf8JsonReader(trimmed, new JsonReaderOptions { AllowTrailingCommas = false });
            return reader.Read() && reader.TrySkip() && reader.BytesConsumed > 0;
        }
        catch (JsonException) { return false; }
    }
}

/// <summary>The text people copy from Settings to connect a tool: each runs <c>WinOrbit.exe --agent &lt;tool&gt;</c>.</summary>
internal static class AgentSetup
{
    /// <summary>
    /// The command a hook runs. Tools run hooks through whatever shell they use (PowerShell, cmd or Git Bash), and a
    /// quoted path is a syntax error in PowerShell, so the path goes in unquoted, with forward slashes, which all three
    /// accept. A path with spaces is first replaced by its short 8.3 form, which has none.
    /// </summary>
    internal static string Command(string exe, string tool) => $"{exe.Replace('\\', '/')} --agent {tool}";

    /// <summary>This WinOrbit.exe, in a form <see cref="Command"/> can use unquoted.</summary>
    internal static string ExePath()
    {
        var exe = Environment.ProcessPath ?? "WinOrbit.exe";
        if (!exe.Contains(' ')) return exe;
        var sb = new StringBuilder(520);
        var n = Interop.Native.GetShortPathName(exe, sb, (uint)sb.Capacity);
        return n > 0 && n < sb.Capacity && !sb.ToString().Contains(' ') ? sb.ToString() : exe;
    }

    /// <summary>
    /// The "hooks" block for Claude Code's settings.json. Every hook is async, so Claude never waits for WinOrbit.
    /// PostToolUse (each step) is what turns "needs you" back into "working" once you've answered. "shell": "powershell"
    /// says the command is meant for PowerShell (it also runs as is in Git Bash), which stops Claude Code warning that
    /// Git Bash wasn't found on PCs without it.
    /// </summary>
    internal static string ClaudeCode(string exe)
    {
        object Hook() => new Dictionary<string, object> { ["type"] = "command", ["command"] = Command(exe, "claude"), ["shell"] = "powershell", ["async"] = true };
        object Entry() => new[] { new Dictionary<string, object> { ["hooks"] = new[] { Hook() } } };
        var hooks = new Dictionary<string, object>
        {
            ["UserPromptSubmit"] = Entry(), ["PostToolUse"] = Entry(), ["Notification"] = Entry(), ["Stop"] = Entry(), ["SessionEnd"] = Entry(),
        };
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["hooks"] = hooks }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>One line for Codex's config.toml: Codex runs it with a JSON summary when a turn finishes.</summary>
    internal static string Codex(string exe) => $"notify = ['{exe}', '--agent', 'codex']";

    /// <summary>An example for your own scripts.</summary>
    internal static string Own(string exe) =>
        $"'{{\"session\":\"nightly\",\"state\":\"working\",\"title\":\"Nightly build\",\"text\":\"Compiling\"}}' | & \"{exe}\" --agent build";
}

/// <summary>One coding session as the island shows it.</summary>
public sealed class AgentSession
{
    public required string Agent { get; init; }
    public required string Session { get; init; }
    public string Title { get; set; } = "";
    public string Folder { get; set; } = "";
    public AgentState State { get; set; }
    public string Text { get; set; } = "";
    /// <summary>When the session entered its current state (for "4 min").</summary>
    public DateTime Since { get; set; }
    public DateTime Seen { get; set; }
    public long LastAt { get; set; }

    public string AgentName => AgentBoard.DisplayName(Agent);
    public string Heading => Title.Length > 0 ? Title : Folder.Length > 0 ? Folder : AgentName;
}

/// <summary>
/// The sessions the island lists, newest news first. Kept in memory only and forgotten when coding agents are turned
/// off or WinOrbit quits; a session that has said nothing for three hours drops off.
/// </summary>
public sealed class AgentBoard
{
    internal const int MaxSessions = 8;
    internal static readonly TimeSpan Forget = TimeSpan.FromHours(3);

    private readonly List<AgentSession> _sessions = new();
    public IReadOnlyList<AgentSession> Sessions => _sessions;

    public event Action? Changed;
    /// <summary>A session started waiting for you (a permission or a question).</summary>
    public event Action<AgentSession>? NeedsYou;
    /// <summary>A session finished its turn.</summary>
    public event Action<AgentSession>? Finished;

    public void Apply(AgentEvent e)
    {
        var before = Find(_sessions, e)?.State;
        var s = Update(_sessions, e, DateTime.Now);
        Changed?.Invoke();
        if (s is null || s.State == before) return;
        if (s.State == AgentState.NeedsYou) NeedsYou?.Invoke(s);
        else if (s.State == AgentState.Done && before is AgentState.Working or AgentState.NeedsYou) Finished?.Invoke(s);
    }

    public void ClearFinished() { _sessions.RemoveAll(s => s.State == AgentState.Done); Changed?.Invoke(); }
    public void Remove(AgentSession s) { _sessions.Remove(s); Changed?.Invoke(); }
    public void Clear() { if (_sessions.Count == 0) return; _sessions.Clear(); Changed?.Invoke(); }
    public void Prune() { if (_sessions.RemoveAll(s => DateTime.Now - s.Seen > Forget) > 0) Changed?.Invoke(); }

    private static AgentSession? Find(List<AgentSession> list, AgentEvent e) =>
        list.FirstOrDefault(s => s.Agent == e.Agent && s.Session == e.Session);

    /// <summary>
    /// Applies one update and returns the session it touched (null when it was dropped). Updates that arrive late — hooks
    /// run side by side, so a "working" can land after the "done" that followed it — are ignored.
    /// </summary>
    internal static AgentSession? Update(List<AgentSession> list, AgentEvent e, DateTime now)
    {
        list.RemoveAll(x => now - x.Seen > Forget);
        var s = Find(list, e);
        if (s is not null && e.At > 0 && e.At < s.LastAt) return null;
        if (e.State == AgentState.Ended) { if (s is not null) list.Remove(s); return null; }
        if (s is null)
        {
            s = new AgentSession { Agent = e.Agent, Session = e.Session, State = e.State, Since = now };
            list.Add(s);
            while (list.Count > MaxSessions) list.Remove(list.OrderBy(x => x.State == AgentState.NeedsYou).ThenBy(x => x.Seen).First());
        }
        else if (s.State != e.State) s.Since = now;
        s.State = e.State;
        s.Seen = now;
        if (e.At > 0) s.LastAt = e.At;
        if (s.Title.Length == 0 && e.Title.Length > 0) s.Title = e.Title;
        if (e.Folder.Length > 0) s.Folder = e.Folder;
        s.Text = e.Text;
        list.Sort(Order);
        return s;
    }

    /// <summary>Needs you first, then working, then done; newest first within each.</summary>
    internal static int Order(AgentSession a, AgentSession b)
    {
        int Rank(AgentState st) => st switch { AgentState.NeedsYou => 0, AgentState.Working => 1, _ => 2 };
        var r = Rank(a.State).CompareTo(Rank(b.State));
        return r != 0 ? r : b.Since.CompareTo(a.Since);
    }

    internal static string DisplayName(string agent) => agent switch
    {
        "claude" or "claude-code" => "Claude Code",
        "codex" => "Codex",
        "gemini" => "Gemini CLI",
        "cursor" => "Cursor",
        "copilot" => "Copilot",
        _ => agent.Length > 0 ? char.ToUpperInvariant(agent[0]) + agent[1..] : "Agent",
    };

    /// <summary>"just now", "4 min", "2 h".</summary>
    internal static string Ago(TimeSpan t) =>
        t.TotalSeconds < 45 ? "just now" : t.TotalMinutes < 60 ? $"{Math.Max(1, (int)Math.Round(t.TotalMinutes))} min" : $"{(int)t.TotalHours} h";
}
