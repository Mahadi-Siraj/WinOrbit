using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Interop;
using Windows.Media.Control;

namespace WinOrbit.Services;

/// <summary>
/// Now-playing information and transport control through the Windows media session API
/// (the same source the Windows volume flyout uses). Purely event driven and entirely local.
/// </summary>
public sealed class MediaService
{
    public bool HasSession { get; private set; }
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Album { get; private set; } = "";
    public string Source { get; private set; } = "";
    public bool IsPlaying { get; private set; }
    public ImageSource? Art { get; private set; }
    public TimeSpan Duration { get; private set; }
    public bool Available { get; private set; }
    /// <summary>Something is playing or paused with known details — what the UI should treat as "a track".</summary>
    public bool HasTrack => HasSession && (!string.IsNullOrWhiteSpace(Title) || IsPlaying);
    /// <summary>The title, or a sensible stand-in for players that publish none.</summary>
    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title : string.IsNullOrWhiteSpace(Source) ? "Unknown track" : $"Playing in {Source}";

    /// <summary>Any property changed.</summary>
    public event Action? Changed;
    /// <summary>A different track started.</summary>
    public event Action? TrackChanged;

    private readonly Dispatcher _ui;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private TimeSpan _position;
    private DateTimeOffset _positionAt;
    private double _rate = 1;
    private string _trackKey = "";
    private int _refreshVersion;

    public MediaService(Dispatcher ui) => _ui = ui;

    public TimeSpan Position
    {
        get
        {
            if (!IsPlaying || _positionAt == default) return _position;
            var p = _position + (DateTimeOffset.UtcNow - _positionAt) * _rate;
            return Duration > TimeSpan.Zero && p > Duration ? Duration : p;
        }
    }

    private WindowPlayers? _players;
    private bool _usingWindow;
    /// <summary>The current track comes from a player's window title (e.g. classic VLC), so there's no timeline or art.</summary>
    public bool FromWindowTitle => _usingWindow;

    public async void Start()
    {
        try
        {
            _players = new WindowPlayers(_ui, pid => Hub.Audio?.ProcessPeak(pid) ?? 0);
            _players.Changed += () => _ = RefreshAsync(true);
            _players.Start();
        }
        catch (Exception ex) { Log.Warn("Window-title players unavailable: " + ex.Message); }
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _ui.BeginInvoke(Rebind);
            _manager.SessionsChanged += (_, _) => _ui.BeginInvoke(Rebind);
            Available = true;
            Rebind();
        }
        catch (Exception ex) { Log.Warn("Media session API unavailable: " + ex.Message); }
    }

    // Windows' "current" session is often not the one playing (a paused browser tab can hold it while
    // Media Player plays), so every session is watched and the island follows whichever is playing.
    private List<GlobalSystemMediaTransportControlsSession> _all = new();

    private void Rebind()
    {
        try
        {
            foreach (var s in _all)
            {
                s.MediaPropertiesChanged -= OnProps;
                s.PlaybackInfoChanged -= OnPlayback;
                s.TimelinePropertiesChanged -= OnTimeline;
            }
            _all = _manager?.GetSessions().ToList() ?? new();
            foreach (var s in _all)
            {
                s.MediaPropertiesChanged += OnProps;
                s.PlaybackInfoChanged += OnPlayback;
                s.TimelinePropertiesChanged += OnTimeline;
            }
            Choose(true);
        }
        catch (Exception ex) { Log.Error("Media sessions", ex); }
    }

    private static bool SessionPlaying(GlobalSystemMediaTransportControlsSession s)
    {
        try { return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch { return false; }
    }

    /// <summary>Follow the playing session: the current one if it plays, else any that plays, else stay or fall back to current.</summary>
    private void Choose(bool forceRefresh)
    {
        GlobalSystemMediaTransportControlsSession? current = null;
        try { current = _manager?.GetCurrentSession(); } catch { }
        var best = current is not null && SessionPlaying(current) ? current
            : _all.FirstOrDefault(SessionPlaying)
            ?? (_session is not null && _all.Any(s => s.SourceAppUserModelId == _session.SourceAppUserModelId) ? _all.First(s => s.SourceAppUserModelId == _session.SourceAppUserModelId) : null)
            ?? current ?? _all.FirstOrDefault();
        var changed = !ReferenceEquals(best, _session) && best?.SourceAppUserModelId != _session?.SourceAppUserModelId;
        _session = best;
        _ = RefreshAsync(changed || forceRefresh);
    }

    private bool IsCurrent(GlobalSystemMediaTransportControlsSession s) => _session is not null && s.SourceAppUserModelId == _session.SourceAppUserModelId;

    private void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ui.BeginInvoke(() => { if (IsCurrent(s)) _ = RefreshAsync(true); else Choose(false); });
    private void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ui.BeginInvoke(() => Choose(false));
    private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => _ui.BeginInvoke(() => { if (IsCurrent(s)) _ = RefreshAsync(false); });

    private async Task RefreshAsync(bool properties)
    {
        var version = ++_refreshVersion;
        var session = _session;
        try
        {
            if (UseWindowPlayer(session, out var wp))
            {
                var wasWindow = _usingWindow;
                _usingWindow = true;
                var sameTrack = Title == wp.Title && Artist == wp.Artist && wasWindow;
                Title = wp.Title; Artist = wp.Artist; Album = ""; Source = wp.App; IsPlaying = wp.Playing;
                // VLC doesn't share artwork; take the embedded cover from the file it's playing (what Explorer shows).
                if (!sameTrack || Art is null) Art = wp.App == "VLC" && WindowPlayers.VlcFile(wp.Title, wp.Artist) is { } file ? Launcher.AppIcons.Thumbnail(file) : null;
                Duration = TimeSpan.Zero; _position = TimeSpan.Zero; _positionAt = default;
                HasSession = true;
                var wkey = Title + "|" + Artist;
                var wNew = wkey != _trackKey;
                _trackKey = wkey;
                Changed?.Invoke();
                if (wNew || !wasWindow) TrackChanged?.Invoke();
                return;
            }
            if (_usingWindow) { _usingWindow = false; properties = true; }   // back to the media session: fetch its details

            if (session is null)
            {
                HasSession = false; IsPlaying = false; Title = Artist = Album = Source = ""; Art = null; _trackKey = "";
                Changed?.Invoke();
                return;
            }

            var info = session.GetPlaybackInfo();
            IsPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _rate = info?.PlaybackRate ?? 1;
            var tl = session.GetTimelineProperties();
            if (tl is not null)
            {
                Duration = tl.EndTime - tl.StartTime;
                _position = tl.Position;
                _positionAt = tl.LastUpdatedTime;
            }
            Source = FriendlySource(session.SourceAppUserModelId);

            if (properties)
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (version != _refreshVersion) return;
                Title = props?.Title ?? "";
                Artist = props?.Artist ?? "";
                Album = props?.AlbumTitle ?? "";
                ImageSource? art = null;
                if (props?.Thumbnail is { } thumb)
                {
                    try
                    {
                        using var ras = await thumb.OpenReadAsync();
                        using var stream = ras.AsStreamForRead();
                        var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        ms.Position = 0;
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 220;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        art = bmp;
                    }
                    catch { art = null; }
                }
                if (version != _refreshVersion) return;
                Art = art;
            }

            HasSession = !string.IsNullOrWhiteSpace(Title) || IsPlaying;
            var key = Title + "|" + Artist;
            var newTrack = key != _trackKey && !string.IsNullOrWhiteSpace(Title);
            _trackKey = key;
            Changed?.Invoke();
            if (newTrack) TrackChanged?.Invoke();
        }
        catch (Exception ex) { Log.Warn("Media refresh: " + ex.Message); }
    }

    /// <summary>
    /// Prefer the Windows media session (richer: art, timeline). Use a window-title player when there is no
    /// session, or when that player is the one making sound while the session is paused.
    /// </summary>
    private bool UseWindowPlayer(GlobalSystemMediaTransportControlsSession? session, out WindowPlayer wp)
    {
        wp = _players?.Current!;
        if (wp is null) return false;
        if (session is null) return true;
        bool sessionPlaying;
        try { sessionPlaying = session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch { return true; }
        return wp.Playing && !sessionPlaying;
    }

    private static string FriendlySource(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var s = id;
        var bang = s.IndexOf('!');
        if (bang > 0) s = s[..bang];
        s = Path.GetFileNameWithoutExtension(s);
        if (s.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (s.Contains("ZuneMusic", StringComparison.OrdinalIgnoreCase) || s.Contains("ZuneVideo", StringComparison.OrdinalIgnoreCase)) return "Media Player";
        if (s.Contains("brave", StringComparison.OrdinalIgnoreCase)) return "Brave";
        if (s.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (s.Contains("msedge", StringComparison.OrdinalIgnoreCase) || s.Contains("MicrosoftEdge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (s.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        var dot = s.LastIndexOf('.');
        if (dot >= 0 && dot < s.Length - 1) s = s[(dot + 1)..];
        var us = s.IndexOf('_');
        if (us > 0) s = s[..us];
        return s.Length > 0 ? char.ToUpper(s[0]) + s[1..] : s;
    }

    /// <summary>The player lets other apps move the playhead (YouTube in Chrome/Brave/Edge, Spotify, Media Player …).</summary>
    public bool CanSeek
    {
        get
        {
            if (_usingWindow || _session is null || Duration <= TimeSpan.Zero) return false;
            try { return _session.GetPlaybackInfo()?.Controls?.IsPlaybackPositionEnabled == true; }
            catch { return false; }
        }
    }

    /// <summary>Jump to a position in the current track. Returns false if the player refused.</summary>
    public async Task<bool> SeekAsync(TimeSpan position)
    {
        var s = _session;
        if (s is null || _usingWindow) return false;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && position > Duration) position = Duration - TimeSpan.FromSeconds(1);
        try
        {
            var start = s.GetTimelineProperties()?.StartTime ?? TimeSpan.Zero;
            var ok = await s.TryChangePlaybackPositionAsync((start + position).Ticks);
            if (ok)
            {
                // Show the new position at once; the player's own timeline update follows shortly.
                _position = position;
                _positionAt = DateTimeOffset.UtcNow;
                Changed?.Invoke();
            }
            return ok;
        }
        catch (Exception ex) { Log.Warn("Seek failed: " + ex.Message); return false; }
    }

    /// <summary>A short message for the user when a control can't be carried out (shown in the island).</summary>
    public event Action<string>? Hint;

    private bool WindowControl(byte key)
    {
        if (!_usingWindow || _players is null) return false;
        if (_players.Control(key, _session is not null) is { } why) Hint?.Invoke(why);
        return true;
    }

    public async void PlayPause()
    {
        if (WindowControl(Native.VK_MEDIA_PLAY_PAUSE)) return;
        try { if (_session is not null) await _session.TryTogglePlayPauseAsync(); else Key(Native.VK_MEDIA_PLAY_PAUSE); }
        catch { Key(Native.VK_MEDIA_PLAY_PAUSE); }
    }

    public async void Next()
    {
        if (WindowControl(Native.VK_MEDIA_NEXT_TRACK)) return;
        try { if (_session is not null) await _session.TrySkipNextAsync(); else Key(Native.VK_MEDIA_NEXT_TRACK); }
        catch { Key(Native.VK_MEDIA_NEXT_TRACK); }
    }

    public async void Previous()
    {
        if (WindowControl(Native.VK_MEDIA_PREV_TRACK)) return;
        try { if (_session is not null) await _session.TrySkipPreviousAsync(); else Key(Native.VK_MEDIA_PREV_TRACK); }
        catch { Key(Native.VK_MEDIA_PREV_TRACK); }
    }

    // Fallback when no media session exists: the standard hardware media keys.
    private static void Key(byte vk)
    {
        Native.keybd_event(vk, 0, 1, UIntPtr.Zero);
        Native.keybd_event(vk, 0, 3, UIntPtr.Zero);
    }
}
