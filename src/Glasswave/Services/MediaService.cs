using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Glasswave.Core;
using Glasswave.Interop;
using Windows.Media.Control;

namespace Glasswave.Services;

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

    public async void Start()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _ui.BeginInvoke(Attach);
            Available = true;
            Attach();
        }
        catch (Exception ex) { Log.Warn("Media session API unavailable: " + ex.Message); }
    }

    private void Attach()
    {
        try
        {
            if (_session is not null)
            {
                _session.MediaPropertiesChanged -= OnProps;
                _session.PlaybackInfoChanged -= OnPlayback;
                _session.TimelinePropertiesChanged -= OnTimeline;
            }
            _session = _manager?.GetCurrentSession();
            if (_session is not null)
            {
                _session.MediaPropertiesChanged += OnProps;
                _session.PlaybackInfoChanged += OnPlayback;
                _session.TimelinePropertiesChanged += OnTimeline;
            }
            _ = RefreshAsync(true);
        }
        catch (Exception ex) { Log.Error("Media attach", ex); }
    }

    private void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ui.BeginInvoke(() => _ = RefreshAsync(true));
    private void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ui.BeginInvoke(() => _ = RefreshAsync(false));
    private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => _ui.BeginInvoke(() => _ = RefreshAsync(false));

    private async Task RefreshAsync(bool properties)
    {
        var version = ++_refreshVersion;
        var session = _session;
        try
        {
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

    private static string FriendlySource(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var s = id;
        var bang = s.IndexOf('!');
        if (bang > 0) s = s[..bang];
        s = Path.GetFileNameWithoutExtension(s);
        if (s.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (s.Contains("ZuneMusic", StringComparison.OrdinalIgnoreCase)) return "Media Player";
        if (s.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (s.Contains("msedge", StringComparison.OrdinalIgnoreCase) || s.Contains("MicrosoftEdge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (s.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        var dot = s.LastIndexOf('.');
        if (dot >= 0 && dot < s.Length - 1) s = s[(dot + 1)..];
        var us = s.IndexOf('_');
        if (us > 0) s = s[..us];
        return s.Length > 0 ? char.ToUpper(s[0]) + s[1..] : s;
    }

    public async void PlayPause()
    {
        try { if (_session is not null) await _session.TryTogglePlayPauseAsync(); else Key(Native.VK_MEDIA_PLAY_PAUSE); }
        catch { Key(Native.VK_MEDIA_PLAY_PAUSE); }
    }

    public async void Next()
    {
        try { if (_session is not null) await _session.TrySkipNextAsync(); else Key(Native.VK_MEDIA_NEXT_TRACK); }
        catch { Key(Native.VK_MEDIA_NEXT_TRACK); }
    }

    public async void Previous()
    {
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
