using AiMouse.Configuration;
using AiMouse.Vision;

namespace AiMouse.Ui;

/// <summary>
/// Editor for <c>settings.json</c>. Writing the file stays the single source of truth —
/// the dialog only produces a validated <see cref="AppSettings"/>; the caller persists it
/// and applies it to the running instance.
/// </summary>
internal sealed class SettingsWindow : Form
{
    private readonly TextBox _endpoint = new() { Dock = DockStyle.Fill };
    private readonly TextBox _model = new() { Dock = DockStyle.Fill };
    private readonly TextBox _apiKey = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _systemPrompt = new() { Dock = DockStyle.Fill, Multiline = true, Height = 56, ScrollBars = ScrollBars.Vertical };
    private readonly NumericUpDown _timeout = Number(5, 3600);
    private readonly NumericUpDown _maxTokens = Number(64, 128_000);
    private readonly NumericUpDown _temperature = Number(0, 2, decimals: 2, increment: 0.05m);
    private readonly NumericUpDown _dragThreshold = Number(1, 100);
    private readonly CheckBox _copyResult = new() { Text = "Copy the answer to the clipboard automatically", AutoSize = true };

    /// <summary>Performs a real round-trip against the entered settings; injected so the
    /// dialog stays free of any HTTP knowledge.</summary>
    private readonly Func<AppSettings, CancellationToken, Task<string>> _tester;

    private readonly Button _testButton;
    private readonly Label _status;

    private CancellationTokenSource? _testCts;

    /// <summary>Only meaningful once <see cref="DialogResult.OK"/> was returned.</summary>
    public AppSettings Result { get; private set; } = new();

    public SettingsWindow(AppSettings current, Func<AppSettings, CancellationToken, Task<string>> tester)
    {
        _tester = tester;

        Text = "AI Mouse — Settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(520, 400);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = false,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, "Endpoint", _endpoint);
        AddRow(layout, "Model", _model);
        AddRow(layout, "API key", _apiKey);
        AddRow(layout, "System prompt", _systemPrompt);
        AddRow(layout, "Timeout (seconds)", _timeout);
        AddRow(layout, "Max tokens", _maxTokens);
        AddRow(layout, "Temperature", _temperature);
        AddRow(layout, "Drag threshold (px)", _dragThreshold);
        AddRow(layout, string.Empty, _copyResult);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(14, 0, 14, 6),
            ForeColor = SystemColors.GrayText,
            Text = $"Saved to {ConfigStore.SettingsPath}",
        };

        var save = new Button { Text = "&Save", Width = 90, Height = 28, DialogResult = DialogResult.None };
        save.Click += OnSave;

        var cancel = new Button { Text = "&Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };

        _testButton = new Button { Text = "&Test connection", Width = 130, Height = 28, DialogResult = DialogResult.None };
        _testButton.Click += OnTestConnection;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 46,
            Padding = new Padding(12, 8, 12, 8),
        };
        // RightToLeft flow: first added sits rightmost.
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        buttons.Controls.Add(_testButton);

        // Docked last means docked outermost, so the status line ends up at the bottom.
        Controls.Add(layout);
        Controls.Add(buttons);
        Controls.Add(_status);

        AcceptButton = save;
        CancelButton = cancel;

        Populate(current);
    }

    private void Populate(AppSettings s)
    {
        _endpoint.Text = s.Endpoint;
        _model.Text = s.Model;
        _apiKey.Text = s.ApiKey;
        _systemPrompt.Text = s.SystemPrompt;
        _timeout.Value = Clamp(_timeout, s.TimeoutSeconds);
        _maxTokens.Value = Clamp(_maxTokens, s.MaxTokens);
        _temperature.Value = Clamp(_temperature, (decimal)s.Temperature);
        _dragThreshold.Value = Clamp(_dragThreshold, s.DragThreshold);
        _copyResult.Checked = s.CopyResultToClipboard;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (BuildSettings() is not { } settings)
        {
            return;
        }

        Result = settings;
        DialogResult = DialogResult.OK;
        Close();
    }

    private async void OnTestConnection(object? sender, EventArgs e)
    {
        if (BuildSettings() is not { } candidate)
        {
            return;
        }

        _testCts?.Cancel();
        _testCts?.Dispose();
        _testCts = new CancellationTokenSource();

        _testButton.Enabled = false;
        SetStatus($"Sending a test image to {candidate.Model}… this loads the model and may take a while.", SystemColors.GrayText);

        try
        {
            string answer = await _tester(candidate, _testCts.Token);
            SetStatus($"Connected. The model replied: {Shorten(answer)}", Color.SeaGreen);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Test cancelled.", SystemColors.GrayText);
        }
        catch (Exception ex)
        {
            SetStatus(Shorten(ex.Message), Color.Firebrick);
        }
        finally
        {
            if (!IsDisposed)
            {
                _testButton.Enabled = true;
            }
        }
    }

    private void SetStatus(string text, Color color)
    {
        if (IsDisposed)
        {
            return;
        }

        _status.ForeColor = color;
        _status.Text = text.ReplaceLineEndings(" ");
    }

    private static string Shorten(string value)
    {
        string flat = value.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 200 ? flat : flat[..200] + " …";
    }

    /// <summary>Validated snapshot of the form, or <c>null</c> if the user was told why not.</summary>
    private AppSettings? BuildSettings()
    {
        string endpoint = _endpoint.Text.Trim();

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Complain("The endpoint must be an absolute http:// or https:// URL.", _endpoint);
            return null;
        }

        if (_model.Text.Trim().Length == 0)
        {
            Complain("A model name is required — it has to match what the server reports.", _model);
            return null;
        }

        return new AppSettings
        {
            // Saved normalised, so the field shows the URL that is actually called.
            Endpoint = EndpointResolver.Normalize(endpoint),
            Model = _model.Text.Trim(),
            ApiKey = _apiKey.Text.Trim(),
            SystemPrompt = _systemPrompt.Text.Trim(),
            TimeoutSeconds = (int)_timeout.Value,
            MaxTokens = (int)_maxTokens.Value,
            Temperature = (double)_temperature.Value,
            DragThreshold = (int)_dragThreshold.Value,
            CopyResultToClipboard = _copyResult.Checked,
        };
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Abandon a test still in flight rather than leaving it to time out.
        _testCts?.Cancel();
        _testCts?.Dispose();
        _testCts = null;

        base.OnFormClosed(e);
    }

    private void Complain(string message, Control focus)
    {
        MessageBox.Show(this, message, "AI Mouse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        focus.Focus();
    }

    private static void AddRow(TableLayoutPanel layout, string caption, Control editor)
    {
        var label = new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 0),
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(label);
        layout.Controls.Add(editor);

        editor.Margin = new Padding(0, 3, 0, 6);
    }

    private static NumericUpDown Number(decimal min, decimal max, int decimals = 0, decimal increment = 1m) => new()
    {
        Minimum = min,
        Maximum = max,
        DecimalPlaces = decimals,
        Increment = increment,
        Width = 110,
        Anchor = AnchorStyles.Left,
    };

    /// <summary>Keeps a hand-edited out-of-range value from throwing on assignment.</summary>
    private static decimal Clamp(NumericUpDown box, decimal value) => Math.Clamp(value, box.Minimum, box.Maximum);
}
