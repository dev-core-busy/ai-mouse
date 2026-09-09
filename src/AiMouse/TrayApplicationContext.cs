using System.Diagnostics;
using AiMouse.Capture;
using AiMouse.Configuration;
using AiMouse.Input;
using AiMouse.Interop;
using AiMouse.Ui;
using AiMouse.Vision;

namespace AiMouse;

/// <summary>
/// Wires everything together: the global gesture hook, the selection overlay, the
/// prompt menu and the vision request. Lives for the whole process lifetime.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    /// <summary>Time given to the compositor to repaint the area the overlay covered.</summary>
    private const int OverlayRepaintDelayMs = 60;

    private readonly Form _owner;
    private readonly NotifyIcon _trayIcon;
    private readonly SelectionOverlay _overlay;
    private readonly MouseGestureHook _hook;
    private readonly ContextMenuStrip _promptMenu;

    private AppSettings _settings;
    private IReadOnlyList<PromptItem> _prompts;

    /// <summary>Screenshot waiting for the user to pick a prompt; owned by this class.</summary>
    private Bitmap? _pendingCapture;

    private bool _promptChosen;

    /// <summary>Throttles the replay warning: it repeats per click while it applies.</summary>
    private DateTime _lastReplayWarningUtc = DateTime.MinValue;

    /// <summary>Non-null while the settings dialog is open, to keep it single-instance.</summary>
    private SettingsWindow? _settingsWindow;

    public TrayApplicationContext()
    {
        _settings = ConfigStore.LoadSettings(out string? settingsError);
        _prompts = ConfigStore.LoadPrompts(out string? promptsError);

        // Off-screen 1×1 window: owns the message pump for the hook callbacks and
        // gives the context menu a foreground window so it dismisses correctly.
        _owner = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1, 1),
            ShowInTaskbar = false,
            Opacity = 0d,
        };
        _owner.Show();

        _overlay = new SelectionOverlay();

        _promptMenu = new ContextMenuStrip { ShowImageMargin = false };
        _promptMenu.Closed += OnPromptMenuClosed;
        BuildPromptMenu();

        _trayIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Visible = true,
            Text = "AI Mouse — right-click + drag to capture",
            ContextMenuStrip = BuildTrayMenu(),
        };

        _hook = new MouseGestureHook(_owner, _settings.DragThreshold);
        _hook.DragStarted += point => _overlay.BeginSelection(point);
        _hook.DragMoved += point => _overlay.UpdateSelection(point);
        _hook.DragCompleted += OnDragCompleted;
        _hook.ReplayFailed += OnReplayFailed;

        try
        {
            _hook.Install();
        }
        catch
        {
            // Nothing is running yet, so tear the half-built context down here rather
            // than leaving a stray tray icon behind; Program reports the failure.
            Dispose(true);
            throw;
        }

        string? startupWarning = settingsError ?? promptsError;
        if (startupWarning is not null)
        {
            _trayIcon.ShowBalloonTip(5000, "AI Mouse", startupWarning, ToolTipIcon.Warning);
        }
    }

    private void BuildPromptMenu()
    {
        _promptMenu.Items.Clear();

        foreach (PromptItem prompt in _prompts)
        {
            var item = new ToolStripMenuItem(prompt.Title) { Tag = prompt };
            item.Click += OnPromptItemClicked;
            _promptMenu.Items.Add(item);
        }

        _promptMenu.Items.Add(new ToolStripSeparator());

        var copyItem = new ToolStripMenuItem("Copy image to clipboard");
        copyItem.Click += OnCopyImageClicked;
        _promptMenu.Items.Add(copyItem);

        var saveItem = new ToolStripMenuItem("Save image as…");
        saveItem.Click += OnSaveImageClicked;
        _promptMenu.Items.Add(saveItem);

        // The tray icon lives in the Windows 11 overflow flyout by default, so for most
        // users this popup is the only part of the app they ever see. Settings and Exit
        // have to be reachable from here or they are effectively missing.
        _promptMenu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => ShowSettings();
        _promptMenu.Items.Add(settingsItem);

        var exitItem = new ToolStripMenuItem("Exit AI Mouse");
        exitItem.Click += (_, _) => ExitThread();
        _promptMenu.Items.Add(exitItem);
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();

        var settings = new ToolStripMenuItem("Settings…");
        settings.Click += (_, _) => ShowSettings();

        var openPrompts = new ToolStripMenuItem("Edit prompts.json");
        openPrompts.Click += (_, _) => OpenInEditor(ConfigStore.PromptsPath, WriteDefaultPrompts);

        var reload = new ToolStripMenuItem("Reload configuration");
        reload.Click += (_, _) => ReloadConfiguration();

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitThread();

        menu.Items.AddRange([settings, openPrompts, reload, new ToolStripSeparator(), exit]);
        return menu;
    }

    private async void OnDragCompleted(Rectangle bounds)
    {
        _overlay.EndSelection();

        DiscardPendingCapture();

        if (bounds.Width < ScreenCapture.MinimumEdge || bounds.Height < ScreenCapture.MinimumEdge)
        {
            return;
        }

        // The overlay window is hidden but the desktop underneath has not necessarily
        // repainted yet — capturing immediately would bake the dimming into the image.
        await Task.Delay(OverlayRepaintDelayMs).ConfigureAwait(true);

        try
        {
            _pendingCapture = ScreenCapture.Capture(bounds);
        }
        catch (Exception ex)
        {
            ShowTrayError($"Screen capture failed: {ex.Message}");
            return;
        }

        if (_pendingCapture is null)
        {
            return;
        }

        _promptChosen = false;

        NativeMethods.SetForegroundWindow(_owner.Handle);
        _promptMenu.Show(Cursor.Position);
    }

    private void OnReplayFailed(string message)
    {
        DateTime now = DateTime.UtcNow;

        if (now - _lastReplayWarningUtc < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastReplayWarningUtc = now;
        ShowTrayError(message);
    }

    private void OnPromptMenuClosed(object? sender, ToolStripDropDownClosedEventArgs e)
    {
        // Item handlers run after this event, so the capture may only be released
        // once we know nothing claimed it.
        BeginInvokeOnOwner(() =>
        {
            if (!_promptChosen)
            {
                DiscardPendingCapture();
            }
        });
    }

    private async void OnPromptItemClicked(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem { Tag: PromptItem prompt })
        {
            return;
        }

        Bitmap? capture = TakePendingCapture();
        if (capture is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        var window = new ResultWindow(prompt.Title, cts);
        window.PositionNear(Cursor.Position);
        window.Show();

        try
        {
            string dataUri;
            using (capture)
            {
                dataUri = ScreenCapture.ToDataUri(capture);
            }

            using var client = new VisionClient(_settings);
            string answer = await client.AnalyzeAsync(prompt.Prompt, dataUri, cts.Token).ConfigureAwait(true);

            if (!window.IsDisposed)
            {
                window.ShowAnswer(answer, _settings.CopyResultToClipboard);
            }
        }
        catch (OperationCanceledException)
        {
            // Window closed while the request was running.
        }
        catch (Exception ex) when (ex is VisionException or IOException)
        {
            if (!window.IsDisposed)
            {
                window.ShowError(ex.Message);
            }
        }
        catch (Exception ex)
        {
            if (!window.IsDisposed)
            {
                window.ShowError($"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void OnCopyImageClicked(object? sender, EventArgs e)
    {
        using Bitmap? capture = TakePendingCapture();
        if (capture is null)
        {
            return;
        }

        try
        {
            Clipboard.SetImage(capture);
        }
        catch (Exception ex)
        {
            ShowTrayError($"Clipboard is busy: {ex.Message}");
        }
    }

    private void OnSaveImageClicked(object? sender, EventArgs e)
    {
        using Bitmap? capture = TakePendingCapture();
        if (capture is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image|*.png",
            FileName = $"ai-mouse-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            InitialDirectory = ConfigStore.BaseDirectory,
        };

        if (dialog.ShowDialog(_owner) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(dialog.FileName, ScreenCapture.ToPng(capture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowTrayError($"Could not save the image: {ex.Message}");
        }
    }

    /// <summary>Hands ownership of the pending capture to the caller.</summary>
    private Bitmap? TakePendingCapture()
    {
        _promptChosen = true;
        Bitmap? capture = _pendingCapture;
        _pendingCapture = null;
        return capture;
    }

    private void DiscardPendingCapture()
    {
        _pendingCapture?.Dispose();
        _pendingCapture = null;
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsDisposed: false })
        {
            _settingsWindow.Activate();
            return;
        }

        using var dialog = new SettingsWindow(_settings, TestConnectionAsync);
        _settingsWindow = dialog;

        // The popup that launched this has no foreground window of its own.
        NativeMethods.SetForegroundWindow(_owner.Handle);

        try
        {
            if (dialog.ShowDialog(_owner) != DialogResult.OK)
            {
                return;
            }

            if (ConfigStore.SaveSettings(dialog.Result) is { } error)
            {
                ShowTrayError(error);
                return;
            }

            ApplySettings(dialog.Result);
            _trayIcon.ShowBalloonTip(3000, "AI Mouse", "Settings saved.", ToolTipIcon.Info);
        }
        finally
        {
            _settingsWindow = null;
        }
    }

    /// <summary>
    /// Exercises the exact path a real capture takes — same endpoint, same model, same
    /// multimodal payload shape — so a green result means captures will work, not merely
    /// that the port is open.
    /// </summary>
    private static async Task<string> TestConnectionAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        using Bitmap probe = CreateProbeImage();
        using var client = new VisionClient(settings);

        return await client.AnalyzeAsync(
            "Reply with the single word OK.",
            ScreenCapture.ToDataUri(probe),
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>A small but non-degenerate image; some servers reject 1×1 payloads.</summary>
    private static Bitmap CreateProbeImage()
    {
        var probe = new Bitmap(64, 64);

        using (Graphics g = Graphics.FromImage(probe))
        {
            g.Clear(Color.White);
            using var brush = new SolidBrush(Color.Black);
            g.FillEllipse(brush, 16, 16, 32, 32);
        }

        return probe;
    }

    /// <summary>Takes effect on the next capture; nothing needs restarting.</summary>
    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _hook.Threshold = settings.DragThreshold;
    }

    private void ReloadConfiguration()
    {
        ApplySettings(ConfigStore.LoadSettings(out string? settingsError));
        _prompts = ConfigStore.LoadPrompts(out string? promptsError);
        BuildPromptMenu();

        string message = settingsError ?? promptsError ?? $"{_prompts.Count} prompts loaded.";
        ToolTipIcon icon = settingsError is null && promptsError is null ? ToolTipIcon.Info : ToolTipIcon.Warning;

        _trayIcon.ShowBalloonTip(4000, "AI Mouse", message, icon);
    }

    private void OpenInEditor(string path, Action createDefault)
    {
        try
        {
            if (!File.Exists(path))
            {
                createDefault();
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            ShowTrayError($"Could not open {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static void WriteDefaultPrompts() => File.WriteAllText(ConfigStore.PromptsPath, DefaultFiles.Prompts);

    private void ShowTrayError(string message) => _trayIcon.ShowBalloonTip(5000, "AI Mouse", message, ToolTipIcon.Error);

    private void BeginInvokeOnOwner(Action action)
    {
        if (_owner.IsDisposed)
        {
            return;
        }

        _owner.BeginInvoke(action);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hook.Dispose();
            DiscardPendingCapture();
            _trayIcon.Visible = false;

            // NotifyIcon does not own the icon it was handed.
            Icon? icon = _trayIcon.Icon;
            _trayIcon.Dispose();
            icon?.Dispose();
            _promptMenu.Dispose();
            _overlay.Dispose();
            _owner.Dispose();
        }

        base.Dispose(disposing);
    }
}
