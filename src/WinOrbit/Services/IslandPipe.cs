using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Interop;

namespace WinOrbit.Services;

/// <summary>A short card another program on this PC asked the island to show.</summary>
public sealed record PipeCard(string Title, string Text, string Icon, double Seconds, string Sender);

/// <summary>
/// Cards from other apps: an opt-in local named pipe, <c>\\.\pipe\WinOrbit.Island</c>, through which scripts and apps
/// you run can show a short card on the island ("Build finished", "Backup done"). Off by default; while off the pipe
/// does not exist. Only programs running as the same Windows user can connect (<see cref="PipeOptions.CurrentUserOnly"/>);
/// it is never reachable from the network. A message is one line of JSON — title, optional text, icon and seconds —
/// shown as plain text with the sending program's name; it carries no links, commands or buttons. Each program gets a
/// few cards in a short time, then is told it is busy.
/// </summary>
public sealed class IslandPipe : IDisposable
{
    public const string PipeName = "WinOrbit.Island";
    internal const int MaxBytes = 2048;
    internal const int MaxTitle = 60, MaxText = 120;
    internal static readonly string[] Icons = { "info", "done", "warning", "error", "bell" };

    private readonly Dispatcher _ui;
    private readonly RateGate _gate = new();
    // Coding agents report every tool step, so each session gets a bigger allowance than a card sender.
    private readonly RateGate _agentGate = new(burst: 12, every: 0.25, totalBurst: 40, totalEvery: 0.1);
    private CancellationTokenSource? _cts;

    /// <summary>Whether cards from scripts and apps are accepted.</summary>
    public bool Cards { get; set; }
    /// <summary>Whether coding-agent status updates are accepted.</summary>
    public bool Agents { get; set; }

    /// <summary>A card arrived and passed every check. Raised on the UI thread.</summary>
    public event Action<PipeCard>? Received;
    /// <summary>A coding-agent update arrived and passed every check. Raised on the UI thread.</summary>
    public event Action<AgentEvent>? AgentReceived;

    public IslandPipe(Dispatcher ui) => _ui = ui;

    public bool Running => _cts is not null;

    public void SetEnabled(bool on)
    {
        if (on == Running) return;
        if (!on) { _cts!.Cancel(); _cts.Dispose(); _cts = null; Log.Info("Island pipe closed"); return; }
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(() => Serve(ct));
        Log.Info(@"Island pipe open at \\.\pipe\" + PipeName + " (current user only)");
    }

    public void Dispose() => SetEnabled(false);

    private async Task Serve(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                failures = 0;
                await Handle(pipe, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Typically: another program already holds the name. Back off and try again rather than spin.
                if (++failures <= 3) Log.Warn("Island pipe: " + ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 2 * failures)), ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
            finally { pipe?.Dispose(); }
        }
    }

    private async Task Handle(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));   // a client that connects and says nothing is dropped
        string reply;
        try
        {
            if (FromAnotherComputer(pipe)) { Log.Warn("Island pipe: refused a connection from another computer"); return; }
            var line = await ReadLine(pipe, timeout.Token).ConfigureAwait(false);
            var sender = Sender(pipe);
            if (sender is null) reply = "error: could not identify the sending program";
            else if (line is null) reply = $"error: send one line of JSON, at most {MaxBytes} bytes";
            else if (AgentEvent.Parse(line, out var isAgent, out var agentError) is { } ev)
            {
                if (!Agents) reply = "error: coding agents are turned off in WinOrbit";
                else if (!_agentGate.Allow(ev.Agent + "/" + ev.Session, DateTime.UtcNow)) reply = "busy: too many updates, try again in a moment";
                else { _ = _ui.BeginInvoke(() => AgentReceived?.Invoke(ev)); reply = "ok"; }
            }
            else if (isAgent) reply = "error: " + agentError;
            else if (!Cards) reply = "error: cards from scripts and apps are turned off in WinOrbit";
            else if (Parse(line, sender, out var error) is not { } card) reply = "error: " + error;
            else if (!_gate.Allow(sender, DateTime.UtcNow)) reply = "busy: too many cards, try again in a few seconds";
            else
            {
                Log.Info($"Island pipe: card from {sender}");
                _ = _ui.BeginInvoke(() => Received?.Invoke(card));
                reply = "ok";
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { reply = "error: timed out"; }
        try
        {
            var bytes = Encoding.UTF8.GetBytes(reply + "\n");
            await pipe.WriteAsync(bytes, ct).ConfigureAwait(false);
            await pipe.FlushAsync(ct).ConfigureAwait(false);
            pipe.WaitForPipeDrain();
        }
        catch (IOException) { /* the client left without reading the reply */ }
    }

    /// <summary>Reads up to the first newline (or the end of the stream). Null when the message is too long or empty.</summary>
    private static async Task<string?> ReadLine(Stream pipe, CancellationToken ct)
    {
        var buffer = new byte[MaxBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var n = await pipe.ReadAsync(buffer.AsMemory(count), ct).ConfigureAwait(false);
            if (n == 0) break;
            var nl = Array.IndexOf(buffer, (byte)'\n', count, n);
            count += n;
            if (nl >= 0) { count = nl; break; }
        }
        if (count == 0 || count > MaxBytes) return null;
        return Encoding.UTF8.GetString(buffer, 0, count);
    }

    /// <summary>
    /// Whether the client reached the pipe over the network. The pipe's access list already admits only your own
    /// account; this refuses even that account when it connects from another computer.
    /// </summary>
    private static bool FromAnotherComputer(NamedPipeServerStream pipe)
    {
        var name = new StringBuilder(256);
        return Native.GetNamedPipeClientComputerName(pipe.SafePipeHandle, name, (uint)name.Capacity * 2);
    }

    /// <summary>The sending program's name ("python", "pwsh"), from the process on the other end of the pipe.</summary>
    private static string? Sender(NamedPipeServerStream pipe)
    {
        try
        {
            if (!Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid == 0) return null;
            using var p = Process.GetProcessById((int)pid);
            return Clean(p.ProcessName, 40) is { Length: > 0 } name ? name : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Turns one JSON line into a card, or explains why not. Title is required; text, icon (info, done, warning, error,
    /// bell) and seconds (2–10) are optional; anything else is ignored. Text is cleaned to plain single-line text.
    /// </summary>
    internal static PipeCard? Parse(string json, string sender, out string error)
    {
        error = "";
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 }); }
        catch (JsonException) { error = "not valid JSON"; return null; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { error = "expected a JSON object"; return null; }
            string Str(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var title = Clean(Str("title"), MaxTitle);
            if (title.Length == 0) { error = "\"title\" is required"; return null; }
            var text = Clean(Str("text"), MaxText);
            var icon = Str("icon").Trim().ToLowerInvariant();
            if (!Icons.Contains(icon)) icon = "info";
            var seconds = doc.RootElement.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetDouble(out var d) ? d : 4;
            if (double.IsNaN(seconds)) seconds = 4;
            return new PipeCard(title, text, icon, Math.Clamp(seconds, 2, 10), sender);
        }
    }

    /// <summary>
    /// Plain, single-line text: control characters and the invisible direction marks that can disguise text are removed,
    /// runs of spaces collapse, and it is cut to <paramref name="max"/> characters with an ellipsis.
    /// </summary>
    internal static string Clean(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(Math.Min(value.Length, max + 8));
        var space = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c)) { space = sb.Length > 0; continue; }
            if (c is >= '\u200B' and <= '\u200F' || c is >= '\u202A' and <= '\u202E' || c is >= '\u2066' and <= '\u2069' || c == '\uFEFF') continue;
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
            if (sb.Length > max + 4) break;
        }
        var s = sb.ToString();
        if (s.Length <= max) return s;
        var cut = s[..(max - 1)];
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        return cut.TrimEnd() + "…";
    }
}

/// <summary>
/// How many cards each program may show: a small burst, then one every few seconds; and all programs together are
/// held to a slightly larger budget, so a runaway script can't keep the island busy.
/// </summary>
internal sealed class RateGate
{
    internal const double Burst = 3, Every = 5;              // per program: 3 at once, then 1 per 5 s
    internal const double TotalBurst = 6, TotalEvery = 2;    // everyone together: 6 at once, then 1 per 2 s

    private readonly double _burst, _every, _totalBurst, _totalEvery;
    private readonly Dictionary<string, (double Tokens, DateTime At)> _each = new(StringComparer.OrdinalIgnoreCase);
    private (double Tokens, DateTime At) _all;

    public RateGate(double burst = Burst, double every = Every, double totalBurst = TotalBurst, double totalEvery = TotalEvery)
    {
        (_burst, _every, _totalBurst, _totalEvery) = (burst, every, totalBurst, totalEvery);
        _all = (totalBurst, DateTime.MinValue);
    }

    public bool Allow(string sender, DateTime now)
    {
        lock (_each)
        {
            (double Tokens, DateTime At) mine = _each.TryGetValue(sender, out var b) ? Refill(b, now, _burst, _every) : (_burst, now);
            var all = Refill(_all, now, _totalBurst, _totalEvery);
            if (mine.Tokens < 1 || all.Tokens < 1) { _each[sender] = mine; _all = all; return false; }
            _each[sender] = (mine.Tokens - 1, now);
            _all = (all.Tokens - 1, now);
            // Forget senders that are back to a full allowance (they'd start full anyway).
            if (_each.Count > 64) foreach (var k in _each.Where(e => Refill(e.Value, now, _burst, _every).Tokens >= _burst).Select(e => e.Key).ToList()) _each.Remove(k);
            return true;
        }
    }

    private static (double Tokens, DateTime At) Refill((double Tokens, DateTime At) b, DateTime now, double cap, double every) =>
        b.At == DateTime.MinValue ? (cap, now) : (Math.Min(cap, b.Tokens + Math.Max(0, (now - b.At).TotalSeconds) / every), now);
}
