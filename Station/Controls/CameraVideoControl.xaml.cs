using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Station.Controls;

/// <summary>
/// Camera slot control.  Two display modes (priority order):
///   1. StreamUrl set     → MJPEG stream via &lt;img&gt; tag (backend /api/cameras/{id}/stream)
///   2. Offline / no URL → dark "Mất tín hiệu" placeholder
/// </summary>
public sealed partial class CameraVideoControl : UserControl
{
    private bool _webViewReady;
    private string? _currentContent; // track last-rendered state to avoid redundant navigations

    public CameraVideoControl()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ── Dependency Properties ─────────────────────────────────────────────────

    public static readonly DependencyProperty StreamUrlProperty =
        DependencyProperty.Register(nameof(StreamUrl), typeof(string), typeof(CameraVideoControl),
            new PropertyMetadata(null, (d, _) => ((CameraVideoControl)d).UpdateContent()));


    public static readonly DependencyProperty IsOnlineProperty =
        DependencyProperty.Register(nameof(IsOnline), typeof(bool), typeof(CameraVideoControl),
            new PropertyMetadata(false, (d, _) => ((CameraVideoControl)d).UpdateContent()));

    public static readonly DependencyProperty IsStreamEnabledProperty =
        DependencyProperty.Register(nameof(IsStreamEnabled), typeof(bool), typeof(CameraVideoControl),
            new PropertyMetadata(true, (d, _) => ((CameraVideoControl)d).UpdateContent()));

    public string? StreamUrl
    {
        get => (string?)GetValue(StreamUrlProperty);
        set => SetValue(StreamUrlProperty, value);
    }

    public bool IsOnline
    {
        get => (bool)GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }

    public bool IsStreamEnabled
    {
        get => (bool)GetValue(IsStreamEnabledProperty);
        set => SetValue(IsStreamEnabledProperty, value);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await VideoView.EnsureCoreWebView2Async();

            VideoView.CoreWebView2.Settings.IsStatusBarEnabled          = false;
            VideoView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            VideoView.CoreWebView2.Settings.IsZoomControlEnabled         = false;
            VideoView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

            _webViewReady = true;
            UpdateContent();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraVideoControl] WebView2 init failed: {ex.Message}");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _webViewReady = false;
        _currentContent = null;
    }

    // ── Content routing ───────────────────────────────────────────────────────

    private void UpdateContent()
    {
        if (!_webViewReady) return;

        if (!IsStreamEnabled)
        {
            Navigate("paused", BuildPausedPage());
            return;
        }

        if (!IsOnline)
        {
            Navigate("offline", BuildOfflinePage());
            return;
        }

        if (!string.IsNullOrEmpty(StreamUrl))
        {
            Navigate($"stream:{StreamUrl}", BuildStreamPage(StreamUrl));
            return;
        }

        Navigate("offline", BuildOfflinePage());
    }

    private void Navigate(string key, string html)
    {
        if (_currentContent == key) return;
        _currentContent = key;
        VideoView.CoreWebView2.NavigateToString(html);
    }

    // ── Page builders ─────────────────────────────────────────────────────────

    private static string BuildOfflinePage() => @"<!DOCTYPE html><html>
<body style='margin:0;background:#060606;display:flex;align-items:center;
             justify-content:center;height:100vh;flex-direction:column;gap:10px;
             font-family:Consolas,monospace'>
  <div style='color:#374151;font-size:32px;line-height:1'>◌</div>
  <div style='color:#6B7280;font-size:10px;letter-spacing:.12em'>MẤT TÍN HIỆU</div>
</body></html>";

    private static string BuildPausedPage() => @"<!DOCTYPE html><html>
<body style='margin:0;background:#0a0d14;display:flex;align-items:center;
             justify-content:center;height:100vh;flex-direction:column;gap:12px;
             font-family:Consolas,monospace'>
  <div style='width:48px;height:48px;border-radius:50%;background:#1e2a3a;
              display:flex;align-items:center;justify-content:center'>
    <div style='display:flex;gap:5px'>
      <div style='width:5px;height:20px;background:#3b82f6;border-radius:2px'></div>
      <div style='width:5px;height:20px;background:#3b82f6;border-radius:2px'></div>
    </div>
  </div>
  <div style='color:#3b82f6;font-size:10px;letter-spacing:.14em;font-weight:bold'>ĐÃ TẮT LUỒNG</div>
  <div style='color:#374151;font-size:9px;letter-spacing:.06em'>Bấm ▶ để bật lại</div>
</body></html>";

    private static string BuildStreamPage(string streamUrl) =>
        $@"<!DOCTYPE html><html>
<body style='margin:0;background:#000;overflow:hidden'>
  <img src='{streamUrl}'
       style='width:100%;height:100%;object-fit:cover;display:block'
       onerror=""this.style.display='none'"" />
</body></html>";

}
