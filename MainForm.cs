using DailyMacro.Models;
using DailyMacro.Services;
using AppInfo = DailyMacro.AppInfo;

namespace DailyMacro.UI;

internal sealed class MainForm : Form
{
    private readonly MacroLibrary _library;
    private readonly AppSettings _settings;
    private readonly MacroPlayer _player = new();
    private readonly ListView _macroList = new MacroListView();
    private readonly ListView _steps = new MacroListView();
    private readonly Label _status = new();
    private readonly Label _hint = new();
    private readonly TrackBar _speed = new();
    private readonly Label _speedValue = new();
    private readonly CheckBox _alwaysOnTop = new();
    private readonly CheckBox _showWatermark = new();
    private readonly Button _themeToggle;
    private readonly Button _play;
    private readonly Button _stop;
    private readonly ComboBox _triggerMouse = new();
    private readonly Button _triggerKeyButton = Theme.Ghost("Tecla");
    private readonly ComboBox _processBox = new();
    private readonly NumericUpDown _loopDelay = new();
    private readonly CheckBox _holdRepeat = new();
    private readonly CheckBox _resumeStages = new();
    private readonly CheckBox _macroEnabled = new();
    private readonly NumericUpDown _cycleCount = new();
    private bool _loadingList;
    private readonly CheckBox _infiniteLoop = new();
    private bool _loadingTrigger;
    private CaptureKind _captureKind;
    private string? _playingMacroId;
    private readonly Dictionary<string, int> _stageCursor = new();
    private readonly Dictionary<string, int> _cycleCursor = new();

    private HotkeyService? _hotkeys;
    private CancellationTokenSource? _playCts;
    private bool _dirty;
    private NotifyIcon? _tray;
    private Icon? _trayIcon;
    private bool _inTray;
    private bool _layoutBusy;
    private int _triggerPause;
    private int _processLoad;
    private bool _reallyClose;
    private StatusWatermarkForm? _watermark;

    public MainForm()
    {
        _library = MacroStore.LoadMacros();
        _settings = MacroStore.LoadSettings();
        if (PromoteEventMacros(_library))
            MacroStore.SaveMacros(_library);

        if (_library.Macros.Count == 0 && !MacroStore.HasExistingMacrosFile())
        {
            _library.Macros.Add(new MacroDefinition { Name = "Exemplo: copiar e colar", Enabled = true });
            _library.Macros[0].Steps.AddRange(
            [
                new MacroStep { Type = StepType.Combo, Keys = ["Ctrl", "C"], HoldMs = 40, DelayAfterMs = 150 },
                new MacroStep { Type = StepType.Combo, Keys = ["Alt", "Tab"], HoldMs = 40, DelayAfterMs = 250 },
                new MacroStep { Type = StepType.Combo, Keys = ["Ctrl", "V"], HoldMs = 40, DelayAfterMs = 120 }
            ]);
            MacroStore.SaveMacros(_library);
        }

        Theme.SetDark(_settings.DarkTheme);
        Text = AppInfo.WindowTitle;
        Font = Theme.Ui;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimumSize = new Size(960, 620);
        Size = new Size(1120, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        KeyPreview = true;
        DoubleBuffered = true;

        _play = Theme.Primary("Reproduzir", Theme.OkSoft, Theme.Ok);
        _stop = Theme.Primary("Parar", Color.FromArgb(241, 245, 249), Theme.Text);
        _themeToggle = Theme.Ghost(Theme.IsDark ? "Escuro" : "Claro");

        Controls.Add(BuildLayout());
        Theme.Apply(this);
        TopMost = _settings.AlwaysOnTop;
        _alwaysOnTop.Checked = _settings.AlwaysOnTop;
        ApplySettingsToControls();
        RefreshMacroList();
        UpdateStatus("Pronto. Segure o Trigger na janela do programa para repetir.");
        SetupTray();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _hotkeys = new HotkeyService(this);
        _hotkeys.MacroTrigger += OnMacroTrigger;
        _hotkeys.Notice += UpdateStatus;
        _hotkeys.BindCaptured += OnBindCaptured;
        _hotkeys.Register();
        _hotkeys.UpdateMacros(_library.Macros);
        _hotkeys.PreferredMacroId = SelectedMacro()?.Id;
        if (!_hotkeys.HooksReady)
            UpdateStatus("Não foi possível ligar o Trigger. Feche e abra o XoMacro.");
        SizeListColumns();
        Theme.ApplyChrome(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(
            Math.Min(MinimumSize.Width, area.Width),
            Math.Min(MinimumSize.Height, area.Height));
        FitToWorkingArea();
        SizeListColumns();
        SetupWatermark();
    }

    private void FitToWorkingArea()
    {
        var area = Screen.FromControl(this).WorkingArea;
        if (Width > area.Width)
            Width = area.Width;
        if (Height > area.Height)
            Height = area.Height;
        if (Left < area.Left)
            Left = area.Left;
        if (Top < area.Top)
            Top = area.Top;
        if (Right > area.Right)
            Left = Math.Max(area.Left, area.Right - Width);
        if (Bottom > area.Bottom)
            Top = Math.Max(area.Top, area.Bottom - Height);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Visible && !_layoutBusy && WindowState != FormWindowState.Minimized)
            SizeListColumns();
    }

    protected override void WndProc(ref Message m)
    {
        const int wmEnterSizeMove = 0x0231;
        const int wmExitSizeMove = 0x0232;
        const int wmDpiChanged = 0x02E0;
        const int wmSysCommand = 0x0112;
        const int scMinimize = 0xF020;

        if (m.Msg == wmEnterSizeMove)
        {
            _layoutBusy = true;
        }
        else if (m.Msg == wmExitSizeMove)
        {
            base.WndProc(ref m);
            _layoutBusy = false;
            SizeListColumns();
            return;
        }
        else if (m.Msg == wmDpiChanged)
        {
            try
            {
                base.WndProc(ref m);
            }
            finally
            {
                BeginInvoke(() =>
                {
                    if (IsDisposed)
                        return;
                    SizeListColumns();
                });
            }
            return;
        }
        else if (m.Msg == wmSysCommand && (m.WParam.ToInt64() & 0xFFF0) == scMinimize)
        {
            BeginInvoke(HideToTray);
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            var choice = CloseChoiceForm.Ask(this);

            if (choice == CloseChoice.Tray)
                BeginInvoke(HideToTray);
            else if (choice == CloseChoice.Exit)
            {
                _reallyClose = true;
                BeginInvoke(Close);
            }
            return;
        }

        StopPlayback();
        TearDownWatermark();
        if (_hotkeys is not null)
        {
            _hotkeys.MacroTrigger -= OnMacroTrigger;
            _hotkeys.Notice -= UpdateStatus;
            _hotkeys.BindCaptured -= OnBindCaptured;
            _hotkeys.Dispose();
            _hotkeys = null;
        }
        if (_dirty)
            Persist();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _trayIcon?.Dispose();
        _trayIcon = null;
        base.OnFormClosing(e);
    }

    private void OnMacroTrigger(string macroId, bool down)
    {
        var macro = _library.Macros.FirstOrDefault(m => m.Id == macroId);
        if (macro is null || !macro.IsOn)
            return;

        if (macro.RepeatWhileHeld || macro.ResumeStages)
        {
            if (down)
            {
                var cursor = StageCursor(macro);
                UpdateStatus(macro.ResumeStages && cursor > 0
                    ? $"Trigger: retomando etapa {cursor + 1}. Solte para pausar."
                    : "Trigger: reproduzindo. Segure o botão para continuar, solte para parar.");
                _ = PlayAsync(macro, fromTrigger: true);
            }
            else if (_playingMacroId == macro.Id)
                StopPlayback();
            return;
        }

        if (!down)
            return;

        if (_player.IsPlaying && _playingMacroId == macro.Id)
        {
            StopPlayback();
            return;
        }

        _ = PlayAsync(macro, fromTrigger: true);
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20, 16, 20, 14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
        return root;
    }

    private Control BuildHeader()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(4, 0, 0, 0)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var title = new Label
        {
            Text = AppInfo.Name,
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true
        };
        var version = new Label
        {
            Text = AppInfo.Label,
            Font = Theme.Caption,
            AutoSize = true,
            Padding = new Padding(8, 3, 8, 3),
            Tag = "chip"
        };
        Theme.Round(version, 8);

        _themeToggle.Size = new Size(86, 30);
        _themeToggle.TextAlign = ContentAlignment.MiddleCenter;
        _themeToggle.Click += (_, _) =>
        {
            Theme.SetDark(!Theme.IsDark);
            _settings.DarkTheme = Theme.IsDark;
            _themeToggle.Text = Theme.IsDark ? "Escuro" : "Claro";
            Theme.Apply(this);
            PersistSettings();
        };

        _alwaysOnTop.Text = "Sempre visível";
        _alwaysOnTop.AutoSize = true;
        _alwaysOnTop.CheckedChanged += (_, _) =>
        {
            _settings.AlwaysOnTop = _alwaysOnTop.Checked;
            TopMost = _alwaysOnTop.Checked;
            PersistSettings();
        };

        var top = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        top.Controls.Add(title);
        top.Controls.Add(version);
        top.Controls.Add(_themeToggle);
        top.Controls.Add(_alwaysOnTop);
        AlignRow(top, () =>
        {
            var right = top.ClientSize.Width;
            var alwaysX = Math.Max(title.Width + version.Width + 36, right - _alwaysOnTop.Width);
            var themeX = alwaysX - 12 - _themeToggle.Width;
            PlaceAt(title, top, 0);
            PlaceAt(version, top, title.Right + 10);
            PlaceAt(_themeToggle, top, themeX);
            PlaceAt(_alwaysOnTop, top, alwaysX);
        });

        _hint.Text = "Segure o Trigger na janela em que quiser reproduzir. Solte para parar.";
        _hint.ForeColor = Theme.Muted;
        _hint.Dock = DockStyle.Fill;
        _hint.AutoSize = false;
        _hint.AutoEllipsis = true;
        _hint.UseMnemonic = false;
        _hint.TextAlign = ContentAlignment.MiddleLeft;
        _hint.Margin = Padding.Empty;

        _showWatermark.Text = "Marca d'água";
        _showWatermark.AutoSize = true;
        _showWatermark.AutoEllipsis = false;
        _showWatermark.Anchor = AnchorStyles.Left;
        _showWatermark.Margin = Padding.Empty;
        _showWatermark.Checked = _settings.ShowWatermark;
        new ToolTip().SetToolTip(_showWatermark, "Indicador na tela: verde com o Trigger, vermelho parado. Arraste para qualquer monitor.");
        _showWatermark.CheckedChanged += (_, _) =>
        {
            _settings.ShowWatermark = _showWatermark.Checked;
            PersistSettings();
            ApplyWatermarkVisibility();
        };

        var watermarkRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        watermarkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        watermarkRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        watermarkRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        watermarkRow.Controls.Add(_showWatermark, 1, 0);

        grid.Controls.Add(top, 0, 0);
        grid.SetColumnSpan(top, 2);
        grid.Controls.Add(_hint, 0, 1);
        grid.Controls.Add(watermarkRow, 1, 1);
        return grid;
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Bg
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));

        var left = Card();
        var leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Padding = new Padding(0)
        };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 204));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        leftLayout.Controls.Add(BuildProcessRow(), 0, 0);
        leftLayout.Controls.Add(Section("Macros"), 0, 1);

        _macroList.Dock = DockStyle.Fill;
        _macroList.View = View.Details;
        _macroList.FullRowSelect = true;
        _macroList.HideSelection = false;
        _macroList.BorderStyle = BorderStyle.None;
        _macroList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _macroList.Font = Theme.UiSmall;
        _macroList.CheckBoxes = true;
        _macroList.Columns.Add("Nome", 120);
        _macroList.Columns.Add("Duração", 70);
        _macroList.Columns.Add("Eventos", 70);
        _macroList.Columns.Add("Atalho", 80);
        _macroList.SelectedIndexChanged += (_, _) =>
        {
            RefreshSteps();
            LoadTriggerUi();
        };
        _macroList.ItemChecked += OnMacroChecked;
        leftLayout.Controls.Add(_macroList, 0, 2);
        leftLayout.Controls.Add(BuildTriggerPanel(), 0, 3);

        var leftButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 0)
        };
        for (var i = 0; i < 4; i++)
            leftButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        leftButtons.Controls.Add(ActionButton("Novo", 0, () => BeginInvoke(AddMacro), last: false), 0, 0);
        leftButtons.Controls.Add(ActionButton("Renomear", 0, () => BeginInvoke(RenameMacro), last: false), 1, 0);
        leftButtons.Controls.Add(ActionButton("Excluir", 0, () => BeginInvoke(DeleteMacro), last: false), 2, 0);
        leftButtons.Controls.Add(ActionButton("Importar", 0, () => BeginInvoke(ImportMacro), last: true), 3, 0);
        leftLayout.Controls.Add(leftButtons, 0, 4);
        left.Controls.Add(leftLayout);

        var right = Card();
        var rightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(0)
        };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        rightLayout.Controls.Add(BuildStepsHeader(), 0, 0);

        _steps.Dock = DockStyle.Fill;
        _steps.View = View.Details;
        _steps.FullRowSelect = true;
        _steps.HideSelection = false;
        _steps.BorderStyle = BorderStyle.None;
        _steps.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _steps.SmallImageList = new ImageList { ImageSize = new Size(1, 16), ColorDepth = ColorDepth.Depth32Bit };
        _steps.Columns.Add("#", 48);
        _steps.Columns.Add("Ação", 200);
        _steps.Columns.Add("Segurar", 78);
        _steps.Columns.Add("Depois", 70);
        _steps.Columns.Add("Ciclo", 64);
        _steps.DoubleClick += (_, _) => BeginInvoke(EditStep);
        rightLayout.Controls.Add(_steps, 0, 1);

        var stepButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0)
        };
        stepButtons.Controls.AddRange(new Control[]
        {
            ActionButton("+ Passo", 96, () => BeginInvoke(AddStep)),
            ActionButton("Editar", 86, () => BeginInvoke(EditStep)),
            ActionButton("↑", 42, () => MoveStep(-1)),
            ActionButton("↓", 42, () => MoveStep(1)),
            ActionButton("Remover", 96, RemoveStep)
        });
        rightLayout.Controls.Add(stepButtons, 0, 2);
        right.Controls.Add(rightLayout);

        left.Margin = new Padding(0, 0, 10, 0);
        right.Margin = new Padding(10, 0, 0, 0);
        body.Controls.Add(left, 0, 0);
        body.Controls.Add(right, 1, 0);
        return body;
    }

    private Control BuildStepsHeader()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        grid.Controls.Add(Section("Passos"), 0, 0);

        var cycles = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        cycles.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        cycles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cycles.Controls.Add(Muted("Ciclos"), 0, 0);
        _cycleCount.Minimum = 1;
        _cycleCount.Maximum = 8;
        _cycleCount.Value = 1;
        _cycleCount.Dock = DockStyle.Fill;
        _cycleCount.Margin = new Padding(0, 2, 0, 2);
        _cycleCount.ValueChanged += (_, _) => SaveTriggerUi();
        new ToolTip().SetToolTip(_cycleCount,
            "Cada volta pode pular passos. Ex.: 2 ciclos — a 4 só no ciclo 1; a próxima volta faz 1 2 3 5 6 e recomeça.");
        cycles.Controls.Add(_cycleCount, 1, 0);
        grid.Controls.Add(cycles, 1, 0);
        return grid;
    }

    private Control BuildProcessRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var programLabel = Muted("Programa");
        programLabel.TextAlign = ContentAlignment.MiddleLeft;
        row.Controls.Add(programLabel, 0, 0);

        _processBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _processBox.Dock = DockStyle.None;
        _processBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _processBox.Margin = new Padding(0, 0, 6, 0);
        _processBox.DropDownWidth = 360;
        _processBox.SelectedIndexChanged += (_, _) => SaveTriggerUi();
        row.Controls.Add(_processBox, 1, 0);

        var refresh = Theme.Ghost("↻");
        refresh.Dock = DockStyle.None;
        refresh.Anchor = AnchorStyles.None;
        refresh.Size = new Size(28, 28);
        refresh.Margin = Padding.Empty;
        refresh.TextAlign = ContentAlignment.MiddleCenter;
        refresh.Font = new Font("Segoe UI", 11f);
        refresh.Click += (_, _) =>
        {
            ProcessHelper.InvalidateAppsCache();
            FillProcessCombo(SelectedMacro()?.TargetProcess);
        };
        new ToolTip().SetToolTip(refresh, "Atualizar lista de programas");
        row.Controls.Add(refresh, 2, 0);
        row.Layout += (_, _) =>
        {
            var height = Math.Max(26, _processBox.Height);
            if (refresh.Height != height)
                refresh.Size = new Size(height, height);
        };
        return row;
    }

    private Control BuildTriggerPanel()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(0, 2, 0, 0)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var title = new Label
        {
            Text = "Programa e botão",
            Font = Theme.UiBold,
            ForeColor = Theme.Text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        grid.Controls.Add(title, 0, 0);
        grid.SetColumnSpan(title, 2);

        grid.Controls.Add(Muted("Trigger"), 0, 1);
        var triggerRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        triggerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        triggerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        triggerRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _triggerMouse.DropDownStyle = ComboBoxStyle.DropDownList;
        _triggerMouse.Dock = DockStyle.None;
        _triggerMouse.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _triggerMouse.Margin = new Padding(0, 0, 6, 0);
        _triggerMouse.Items.AddRange(new object[]
        {
            "Nenhum",
            "Botão do meio",
            "Lateral 4",
            "Lateral 5",
            "Clique direito"
        });
        _triggerMouse.SelectedIndexChanged += (_, _) => SaveTriggerUi();
        _triggerKeyButton.Dock = DockStyle.None;
        _triggerKeyButton.Anchor = AnchorStyles.None;
        _triggerKeyButton.Size = new Size(72, 28);
        _triggerKeyButton.Margin = Padding.Empty;
        _triggerKeyButton.TextAlign = ContentAlignment.MiddleCenter;
        _triggerKeyButton.Click += (_, _) => StartCapture(CaptureKind.Trigger);
        new ToolTip().SetToolTip(_triggerKeyButton, "Definir uma tecla do teclado como Trigger");
        triggerRow.Controls.Add(_triggerMouse, 0, 0);
        triggerRow.Controls.Add(_triggerKeyButton, 1, 0);
        triggerRow.Layout += (_, _) =>
        {
            var height = Math.Max(26, _triggerMouse.Height);
            if (_triggerKeyButton.Height != height)
                _triggerKeyButton.Height = height;
        };
        grid.Controls.Add(triggerRow, 1, 1);

        grid.Controls.Add(Muted("Intervalo"), 0, 2);
        var delayRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        delayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        delayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        delayRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _loopDelay.Dock = DockStyle.None;
        _loopDelay.Anchor = AnchorStyles.Left;
        _loopDelay.Width = 80;
        _loopDelay.Margin = Padding.Empty;
        _loopDelay.Minimum = 0;
        _loopDelay.Maximum = 60000;
        _loopDelay.Increment = 50;
        _loopDelay.ThousandsSeparator = true;
        _loopDelay.ValueChanged += (_, _) => SaveTriggerUi();
        delayRow.Controls.Add(_loopDelay, 0, 0);
        delayRow.Controls.Add(new Label
        {
            Text = "ms",
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Muted,
            Margin = new Padding(8, 0, 0, 0)
        }, 1, 0);
        grid.Controls.Add(delayRow, 1, 2);

        _holdRepeat.Text = "Repetir enquanto eu seguro";
        _holdRepeat.Dock = DockStyle.Fill;
        _holdRepeat.Checked = true;
        _holdRepeat.Margin = new Padding(0, 2, 0, 0);
        _holdRepeat.CheckedChanged += (_, _) => SaveTriggerUi();
        grid.SetColumnSpan(_holdRepeat, 2);
        grid.Controls.Add(_holdRepeat, 0, 3);

        _resumeStages.Text = "Por etapa (continuar de onde parei)";
        _resumeStages.Dock = DockStyle.Fill;
        _resumeStages.Margin = new Padding(0, 0, 0, 0);
        _resumeStages.CheckedChanged += (_, _) => SaveTriggerUi();
        grid.SetColumnSpan(_resumeStages, 2);
        grid.Controls.Add(_resumeStages, 0, 4);
        new ToolTip().SetToolTip(_resumeStages, "Solte o Trigger para pausar na etapa. Segure de novo para seguir dali. Parar volta ao início.");

        _macroEnabled.Text = "Macro ativo";
        _macroEnabled.Dock = DockStyle.Fill;
        _macroEnabled.Checked = true;
        _macroEnabled.Margin = new Padding(0, 0, 0, 0);
        _macroEnabled.CheckedChanged += (_, _) => SaveTriggerUi();
        grid.SetColumnSpan(_macroEnabled, 2);
        grid.Controls.Add(_macroEnabled, 0, 5);
        return grid;
    }

    private Control BuildFooter()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0, 6, 0, 0),
            BackColor = Theme.Bg
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
        };

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Bg,
            Anchor = AnchorStyles.Left
        };
        foreach (var button in new[] { _play, _stop })
        {
            button.Width = 118;
            button.Height = 36;
            button.Margin = new Padding(0, 0, 10, 0);
            button.Anchor = AnchorStyles.Left;
        }
        _play.Click += async (_, _) => await PlayAsync(fromTrigger: true);
        _stop.Click += (_, _) => StopAll();
        actions.Controls.AddRange(new Control[] { _play, _stop });
        bar.Controls.Add(actions, 0, 0);

        _status.Dock = DockStyle.Fill;
        _status.AutoSize = false;
        _status.Font = Theme.UiSmall;
        _status.ForeColor = Theme.Muted;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        _status.Margin = new Padding(4, 0, 12, 0);
        bar.Controls.Add(_status, 1, 0);

        var extras = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Theme.Bg,
            Anchor = AnchorStyles.Left,
            MinimumSize = new Size(0, 36)
        };
        extras.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        extras.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        extras.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _infiniteLoop.Text = "Loop infinito";
        _infiniteLoop.AutoSize = true;
        _infiniteLoop.Anchor = AnchorStyles.Left;
        _infiniteLoop.Margin = new Padding(0, 0, 16, 0);
        _infiniteLoop.CheckedChanged += (_, _) =>
        {
            _settings.InfiniteLoop = _infiniteLoop.Checked;
            PersistSettings();
        };

        var speedLabel = new Label
        {
            Text = "Velocidade",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 0)
        };
        _speed.AutoSize = false;
        _speed.Size = new Size(120, 22);
        _speed.Minimum = 25;
        _speed.Maximum = 200;
        _speed.TickFrequency = 25;
        _speed.TickStyle = TickStyle.None;
        _speed.BackColor = Theme.Bg;
        _speed.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _speed.Margin = new Padding(0, 0, 8, 0);
        _speed.ValueChanged += (_, _) =>
        {
            _settings.Speed = _speed.Value / 100.0;
            _speedValue.Text = $"{_settings.Speed:0.00}x";
            PersistSettings();
        };
        _speedValue.AutoSize = true;
        _speedValue.MinimumSize = new Size(44, 0);
        _speedValue.TextAlign = ContentAlignment.MiddleLeft;
        _speedValue.Anchor = AnchorStyles.Left;
        _speedValue.Margin = Padding.Empty;

        var speedBox = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Theme.Bg,
            Anchor = AnchorStyles.Left,
            MinimumSize = new Size(0, 36)
        };
        speedBox.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        speedBox.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        speedBox.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        speedBox.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        speedBox.Controls.Add(speedLabel, 0, 0);
        speedBox.Controls.Add(_speed, 1, 0);
        speedBox.Controls.Add(_speedValue, 2, 0);

        extras.Controls.Add(_infiniteLoop, 0, 0);
        extras.Controls.Add(speedBox, 1, 0);
        bar.Controls.Add(extras, 2, 0);

        return bar;
    }

    private void SizeListColumns()
    {
        SizeColumns(_macroList, 0, 76, 64, 96);
        SizeColumns(_steps, 48, 0, 78, 64, 52);
    }

    private static void SizeColumns(ListView list, params int[] widths)
    {
        if (list.Columns.Count != widths.Length || list.ClientSize.Width <= 0)
            return;

        var used = widths.Sum(w => w > 0 ? w : 0);
        var scroll = list.Items.Count > 0 && list.Items[^1].Bounds.Bottom > list.ClientSize.Height
            ? SystemInformation.VerticalScrollBarWidth
            : 0;
        var flex = Math.Max(60, list.ClientSize.Width - used - scroll);
        for (var i = 0; i < widths.Length; i++)
            list.Columns[i].Width = widths[i] > 0 ? widths[i] : flex;
    }

    private static Panel Card() => new CardPanel();

    private static Label Section(string text) => new()
    {
        Text = text,
        Font = Theme.Section,
        ForeColor = Theme.Text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label Muted(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Theme.Muted
    };

    private static void AlignRow(Control parent, Action layout)
    {
        var busy = false;
        void Run()
        {
            if (busy || parent.IsDisposed)
                return;
            busy = true;
            try
            {
                layout();
            }
            finally
            {
                busy = false;
            }
        }

        parent.Resize += (_, _) => Run();
        parent.Layout += (_, _) => Run();
        parent.HandleCreated += (_, _) => parent.BeginInvoke(Run);
    }

    private static void PlaceAt(Control child, Control parent, int x)
    {
        var height = parent.ClientSize.Height > 0 ? parent.ClientSize.Height : parent.Height;
        child.Top = Math.Max(0, (height - child.Height) / 2);
        child.Left = Math.Max(0, x);
    }

    private static Button ActionButton(string text, int width, Action click, bool last = false)
    {
        var button = Theme.Ghost(text);
        button.Font = Theme.UiSmall;
        button.Height = 30;
        button.Padding = new Padding(2, 0, 2, 0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseMnemonic = false;
        button.AutoEllipsis = false;
        button.Margin = last ? new Padding(0) : new Padding(0, 0, 4, 0);
        if (width <= 0)
            button.Dock = DockStyle.Fill;
        else
            button.Width = width;
        button.Click += (_, _) => click();
        return button;
    }

    private MacroDefinition? SelectedMacro()
    {
        if (_macroList.SelectedItems.Count > 0)
            return _macroList.SelectedItems[0].Tag as MacroDefinition;
        if (_macroList.FocusedItem?.Tag is MacroDefinition focused)
            return focused;
        return null;
    }

    private MacroDefinition? EnsureMacroSelected()
    {
        var macro = SelectedMacro();
        if (macro is not null)
            return macro;
        if (_macroList.Items.Count == 0)
            return null;

        _macroList.Items[0].Selected = true;
        _macroList.Items[0].Focused = true;
        return SelectedMacro();
    }

    private void RefreshMacroList()
    {
        var selectedId = SelectedMacro()?.Id;
        _loadingList = true;
        _macroList.BeginUpdate();
        _macroList.Items.Clear();
        foreach (var macro in _library.Macros)
        {
            var item = new ListViewItem(macro.Name) { Checked = macro.IsOn };
            item.SubItems.Add($"{macro.DurationSeconds:0.00}s");
            item.SubItems.Add(macro.EventCount.ToString());
            var trigger = macro.TriggerLabel();
            item.SubItems.Add(string.IsNullOrEmpty(trigger) ? "—" : trigger);
            item.Tag = macro;
            _macroList.Items.Add(item);
        }
        _macroList.EndUpdate();

        void Finish()
        {
            if (IsDisposed)
                return;
            _loadingList = false;
            if (_macroList.Items.Count == 0)
            {
                RefreshSteps();
                LoadTriggerUi();
                return;
            }

            var index = _library.Macros.FindIndex(m => m.Id == selectedId);
            var select = index >= 0 ? index : 0;
            _macroList.Items[select].Selected = true;
            _macroList.Items[select].EnsureVisible();
            LoadTriggerUi();
        }

        if (IsHandleCreated)
            BeginInvoke(Finish);
        else
            Finish();
    }

    private void LoadTriggerUi()
    {
        var macro = SelectedMacro();
        _loadingTrigger = true;
        _triggerMouse.SelectedIndex = macro?.TriggerMouse switch
        {
            "Middle" => 1,
            "X1" => 2,
            "X2" => 3,
            "Right" => 4,
            _ => 0
        };
        _loopDelay.Value = Math.Clamp(macro?.LoopDelayMs ?? 250, 0, 60000);
        _holdRepeat.Checked = macro?.RepeatWhileHeld ?? false;
        _resumeStages.Checked = macro?.ResumeStages ?? false;
        _macroEnabled.Checked = macro?.IsOn ?? true;
        _cycleCount.Value = Math.Clamp(macro?.EffectiveCycleCount() ?? 1, 1, 8);
        FillProcessCombo(macro?.TargetProcess);
        UpdateTriggerKeyButton(macro);
        if (_hotkeys is not null)
        {
            _hotkeys.PreferredMacroId = macro?.Id;
        }
        _loadingTrigger = false;
    }

    private void SaveTriggerUi()
    {
        if (_loadingTrigger)
            return;

        var macro = SelectedMacro();
        if (macro is null)
            return;

        var oldMouse = macro.TriggerMouse;
        var oldKey = macro.TriggerKey;
        if (_triggerMouse.SelectedIndex > 0)
        {
            macro.TriggerKey = "";
            macro.TriggerMouse = _triggerMouse.SelectedIndex switch
            {
                1 => "Middle",
                2 => "X1",
                3 => "X2",
                4 => "Right",
                _ => ""
            };
        }
        else
        {
            macro.TriggerMouse = "";
        }
        macro.LoopDelayMs = (int)_loopDelay.Value;
        macro.RepeatWhileHeld = _holdRepeat.Checked;
        var resumeNow = _resumeStages.Checked;
        if (macro.ResumeStages && !resumeNow)
        {
            _stageCursor.Remove(macro.Id);
            _cycleCursor.Remove(macro.Id);
        }
        macro.ResumeStages = resumeNow;
        macro.Enabled = _macroEnabled.Checked;
        var oldCycles = macro.CycleCount;
        macro.CycleCount = (int)_cycleCount.Value;
        var process = (_processBox.SelectedItem as ProcessChoice)?.Exe ?? "";
        var processChanged = !string.Equals(macro.TargetProcess, process, StringComparison.OrdinalIgnoreCase);
        macro.TargetProcess = process;
        if (_hotkeys is not null)
        {
            _hotkeys.PreferredMacroId = macro.Id;
        }
        Persist();
        if (!macro.IsOn && _playingMacroId == macro.Id)
            StopPlayback();
        _loadingList = true;
        foreach (ListViewItem item in _macroList.Items)
        {
            if (item.Tag is MacroDefinition found && found.Id == macro.Id)
                item.Checked = macro.IsOn;
        }
        _loadingList = false;
        if (oldMouse != macro.TriggerMouse || oldKey != macro.TriggerKey || processChanged)
            RefreshMacroList();
        else
            UpdateMacroRow(macro);
        UpdateTriggerKeyButton(macro);
        if (oldCycles != macro.CycleCount)
            RefreshSteps();
    }

    private void OnMacroChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_loadingList || e.Item.Tag is not MacroDefinition macro)
            return;

        macro.Enabled = e.Item.Checked;
        if (!e.Item.Selected)
        {
            foreach (ListViewItem other in _macroList.SelectedItems)
                other.Selected = false;
            e.Item.Selected = true;
            e.Item.Focused = true;
        }
        _hotkeys?.UpdateMacros(_library.Macros);
        if (_hotkeys is not null)
            _hotkeys.PreferredMacroId = SelectedMacro()?.Id;
        _loadingTrigger = true;
        if (SelectedMacro()?.Id == macro.Id)
            _macroEnabled.Checked = macro.IsOn;
        _loadingTrigger = false;
        Persist();
        if (!macro.IsOn && _playingMacroId == macro.Id)
            StopPlayback();
        UpdateStatus(macro.IsOn ? "Macro ligado: " + macro.Name : "Macro desligado: " + macro.Name);
    }

    private void RefreshSteps()
    {
        _steps.Items.Clear();
        var macro = SelectedMacro();
        if (macro is null)
            return;

        if (_steps.Columns.Count >= 5)
        {
            _steps.Columns[1].Text = "Ação";
            _steps.Columns[2].Text = "Segurar";
            _steps.Columns[3].Text = "Depois";
            _steps.Columns[4].Text = "Ciclo";
        }

        var cycleCount = macro.EffectiveCycleCount();
        for (var i = 0; i < macro.Steps.Count; i++)
        {
            var step = macro.Steps[i];
            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add(step.ActionText());
            item.SubItems.Add(step.HoldText());
            item.SubItems.Add(step.AfterText());
            item.SubItems.Add(step.CycleText(cycleCount));
            _steps.Items.Add(item);
        }
    }

    private static bool PromoteEventMacros(MacroLibrary library)
    {
        var changed = false;
        foreach (var macro in library.Macros)
            changed |= PromoteEventsToSteps(macro);
        return changed;
    }

    private static bool PromoteEventsToSteps(MacroDefinition macro)
    {
        if (macro.Events.Count == 0)
            return false;

        if (macro.Steps.Count == 0)
            macro.Steps.AddRange(StepBuilder.FromMacroEvents(macro.Events));

        macro.Events.Clear();
        return true;
    }

    private void AddMacro()
    {
        var name = PromptForm.Ask(this, "Novo macro", "Nome da tarefa", "Novo macro");
        if (name is null)
            return;

        var macro = new MacroDefinition { Name = name, RepeatWhileHeld = true, Enabled = true };
        _library.Macros.Add(macro);
        Persist();
        RefreshMacroList();
        SelectMacro(macro);
    }

    private void SelectMacro(MacroDefinition macro)
    {
        foreach (ListViewItem item in _macroList.Items)
        {
            if (item.Tag is MacroDefinition found && found.Id == macro.Id)
            {
                item.Selected = true;
                item.EnsureVisible();
                break;
            }
        }
    }

    private void RenameMacro()
    {
        var macro = SelectedMacro();
        if (macro is null)
            return;

        var name = PromptForm.Ask(this, "Renomear", "Nome da tarefa", macro.Name);
        if (name is null)
            return;

        macro.Name = name;
        Persist();
        RefreshMacroList();
    }

    private void DeleteMacro()
    {
        var macro = SelectedMacro();
        if (macro is null)
            return;

        if (MessageBox.Show(this, $"Excluir \"{macro.Name}\"?", "Excluir macro",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _library.Macros.Remove(macro);
        Persist();
        RefreshMacroList();
    }

    private void AddStep()
    {
        var macro = EnsureMacroSelected();
        if (macro is null)
        {
            UpdateStatus("Crie ou selecione um macro à esquerda para adicionar um passo.");
            return;
        }

        try
        {
            using var stepEditor = new StepEditForm(null, macro.EffectiveCycleCount());
            if (OwnedDialog.ShowModal(stepEditor, this) != DialogResult.OK)
                return;

            macro.Events.Clear();
            macro.Steps.Add(stepEditor.Result);
            Persist();
            RefreshSteps();
            RefreshMacroList();
            ResetStage(macro);
            if (_steps.Items.Count > 0)
                _steps.Items[_steps.Items.Count - 1].Selected = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível abrir o editor de passo:\n\n" + ex.Message,
                "Passo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void EditStep()
    {
        var macro = EnsureMacroSelected();
        if (macro is null || _steps.SelectedIndices.Count == 0)
        {
            if (macro is null)
                UpdateStatus("Selecione um macro à esquerda.");
            else
                UpdateStatus("Selecione um passo na lista para editar.");
            return;
        }

        var index = _steps.SelectedIndices[0];
        try
        {
            if (index < 0 || index >= macro.Steps.Count)
                return;

            using var stepEditor = new StepEditForm(macro.Steps[index], macro.EffectiveCycleCount());
            if (OwnedDialog.ShowModal(stepEditor, this) != DialogResult.OK)
                return;

            macro.Events.Clear();
            macro.Steps[index] = stepEditor.Result;
            Persist();
            RefreshSteps();
            ResetStage(macro);
            SelectStepIndex(index);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível abrir o editor de passo:\n\n" + ex.Message,
                "Passo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RemoveStep()
    {
        var macro = SelectedMacro();
        if (macro is null || _steps.SelectedIndices.Count == 0)
            return;

        var index = _steps.SelectedIndices[0];
        if (index < 0 || index >= macro.Steps.Count)
            return;
        macro.Steps.RemoveAt(index);
        macro.Events.Clear();

        Persist();
        RefreshSteps();
        ResetStage(macro);
    }

    private void MoveStep(int delta)
    {
        var macro = SelectedMacro();
        if (macro is null || _steps.SelectedIndices.Count == 0)
            return;

        var index = _steps.SelectedIndices[0];
        var stepTarget = index + delta;
        if (stepTarget < 0 || stepTarget >= macro.Steps.Count)
            return;

        (macro.Steps[index], macro.Steps[stepTarget]) = (macro.Steps[stepTarget], macro.Steps[index]);
        macro.Events.Clear();
        Persist();
        RefreshSteps();
        ResetStage(macro);
        SelectStepIndex(stepTarget);
    }

    private void SelectStepIndex(int index)
    {
        if (index < 0 || index >= _steps.Items.Count)
            return;
        _steps.Items[index].Selected = true;
        _steps.Items[index].EnsureVisible();
    }

    private async Task PlayAsync(MacroDefinition? target = null, bool fromTrigger = false)
    {
        var macro = target ?? SelectedMacro();
        if (macro is null || (macro.Steps.Count == 0 && macro.Events.Count == 0))
        {
            UpdateStatus("Este macro ainda não tem passos.");
            return;
        }

        if (fromTrigger && !macro.IsOn)
            return;

        if (_player.IsPlaying)
        {
            StopPlayback();
            for (var i = 0; i < 40 && _player.IsPlaying; i++)
                await Task.Delay(25);
        }

        _playCts = new CancellationTokenSource();
        var token = _playCts.Token;
        _playingMacroId = macro.Id;
        SyncWatermark();
        var startIndex = fromTrigger && macro.ResumeStages ? StageCursor(macro) : 0;
        var startCycle = fromTrigger && macro.ResumeStages ? StageCycle(macro) : 1;

        var holdLoop = fromTrigger && (macro.RepeatWhileHeld || !macro.ResumeStages);
        var playSettings = new AppSettings
        {
            Speed = _settings.Speed,
            Repeat = holdLoop ? RepeatMode.UntilStopped : (fromTrigger ? RepeatMode.Once : _settings.Repeat),
            RepeatCount = _settings.RepeatCount,
            DelayBetweenLoopsMs = macro.LoopDelayMs,
            CountdownSeconds = _settings.CountdownSeconds,
            InfiniteLoop = holdLoop || (!fromTrigger && _settings.InfiniteLoop)
        };

        try
        {
            if (!fromTrigger && _settings.CountdownSeconds > 0)
            {
                WindowState = FormWindowState.Minimized;
                for (var i = _settings.CountdownSeconds; i > 0; i--)
                {
                    UpdateStatus($"Começa em {i}… clique na janela de destino.");
                    await Task.Delay(1000, token);
                }
            }

            var progress = new Progress<string>(UpdateStatus);
            var targetWindow = ProcessHelper.ResolvePlayTarget(
                _hotkeys?.LastTargetWindow ?? NativeMethods.GetForegroundWindow());
            ProcessHelper.FocusWindow(targetWindow);
            await _player.PlayAsync(macro, playSettings, token, progress, targetWindow, startIndex, startCycle);
            RememberStage(macro);
            UpdateStatus(PauseStatus(macro, continuing: false));
        }
        catch (OperationCanceledException)
        {
            RememberStage(macro);
            UpdateStatus(PauseStatus(macro, continuing: true));
        }
        catch (Exception ex)
        {
            UpdateStatus("Não foi possível reproduzir: " + ex.Message);
        }
        finally
        {
            _playingMacroId = null;
            _playCts?.Dispose();
            _playCts = null;
            SyncWatermark();
        }
    }

    private void StopAll()
    {
        if (_playingMacroId is string id)
        {
            _stageCursor[id] = 0;
            _cycleCursor[id] = 1;
        }
        StopPlayback();
    }

    private void StopPlayback()
    {
        _playCts?.Cancel();
        try { InputSender.ReleaseAll(); } catch { /* ignore */ }
    }

    private int StageCursor(MacroDefinition macro)
    {
        if (!macro.ResumeStages)
            return 0;
        return _stageCursor.TryGetValue(macro.Id, out var index) ? Math.Max(0, index) : 0;
    }

    private int StageCycle(MacroDefinition macro)
    {
        if (!macro.ResumeStages)
            return 1;
        var max = macro.EffectiveCycleCount();
        return _cycleCursor.TryGetValue(macro.Id, out var cycle)
            ? Math.Clamp(cycle, 1, max)
            : 1;
    }

    private string PauseStatus(MacroDefinition macro, bool continuing)
    {
        if (!macro.ResumeStages)
            return continuing ? "Parado." : "Reprodução concluída.";

        var step = StageCursor(macro);
        var cycle = StageCycle(macro);
        if (step <= 0 && cycle <= 1)
            return continuing ? "Parado." : "Reprodução concluída.";

        var where = macro.EffectiveCycleCount() > 1
            ? $"ciclo {cycle} · etapa {step + 1}"
            : $"etapa {step + 1}";
        return continuing
            ? $"Pausado no {where}. Segure o Trigger para continuar."
            : $"Pausado no {where}.";
    }

    private void RememberStage(MacroDefinition macro)
    {
        if (!macro.ResumeStages)
            return;
        _stageCursor[macro.Id] = Math.Max(0, _player.ResumeIndex);
        _cycleCursor[macro.Id] = Math.Max(1, _player.ResumeCycle);
    }

    private void ResetStage(MacroDefinition macro)
    {
        _stageCursor[macro.Id] = 0;
        _cycleCursor[macro.Id] = 1;
    }

    private void ApplySettingsToControls()
    {
        _speed.Value = (int)Math.Clamp(_settings.Speed * 100, 25, 200);
        _speedValue.Text = $"{_settings.Speed:0.00}x";
        _infiniteLoop.Checked = _settings.InfiniteLoop;
    }

    private void FillProcessCombo(string? selectedExe)
    {
        var current = selectedExe ?? "";
        var load = ++_processLoad;
        Task.Run(() =>
        {
            List<ProcessChoice> apps;
            try
            {
                apps = ProcessHelper.ListApps();
            }
            catch
            {
                return;
            }

            PostToUi(() =>
            {
                if (IsDisposed || load != _processLoad)
                    return;
                ApplyProcessList(apps, current);
            });
        });
    }

    private void PostToUi(Action action)
    {
        if (IsDisposed)
            return;

        if (IsHandleCreated)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
                Shown += (_, _) => action();
            }
            return;
        }

        void Once(object? sender, EventArgs e)
        {
            Shown -= Once;
            if (!IsDisposed)
                action();
        }

        Shown += Once;
    }

    private void ApplyProcessList(List<ProcessChoice> apps, string current)
    {
        var keepLoading = _loadingTrigger;
        _loadingTrigger = true;
        _processBox.BeginUpdate();
        try
        {
            _processBox.Items.Clear();
            _processBox.Items.Add(new ProcessChoice("", ""));
            foreach (var app in apps)
                _processBox.Items.Add(app);

            if (!string.IsNullOrWhiteSpace(current) &&
                !_processBox.Items.Cast<ProcessChoice>().Any(item =>
                    item.Exe.Equals(current, StringComparison.OrdinalIgnoreCase)))
            {
                _processBox.Items.Add(new ProcessChoice(current, "(salvo)"));
            }

            var index = 0;
            for (var i = 0; i < _processBox.Items.Count; i++)
            {
                if (_processBox.Items[i] is ProcessChoice choice &&
                    choice.Exe.Equals(current, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            _processBox.SelectedIndex = index;
        }
        finally
        {
            _processBox.EndUpdate();
            _loadingTrigger = keepLoading;
        }
    }

    private void Persist()
    {
        MacroStore.SaveMacros(_library);
        _hotkeys?.UpdateMacros(_library.Macros);
        _dirty = false;
    }

    private void PersistSettings()
    {
        MacroStore.SaveSettings(_settings);
    }

    private enum CaptureKind
    {
        None,
        Trigger
    }

    private void StartCapture(CaptureKind kind)
    {
        if (_hotkeys is null)
        {
            UpdateStatus("Trigger ainda não está pronto. Feche e abra o XoMacro.");
            return;
        }

        if (kind == CaptureKind.Trigger && SelectedMacro() is null)
        {
            UpdateStatus("Selecione um macro para definir o Trigger.");
            return;
        }

        _captureKind = kind;
        _hotkeys.CaptureNextBind = true;
        UpdateStatus("Pressione a tecla do Trigger. Esc cancela.");
    }

    private void OnBindCaptured(string name)
    {
        var kind = _captureKind;
        _captureKind = CaptureKind.None;
        if (_hotkeys is not null)
            _hotkeys.CaptureNextBind = false;

        if (string.Equals(name, "Esc", StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatus("Captura cancelada.");
            return;
        }

        if (kind != CaptureKind.Trigger)
            return;

        var macro = SelectedMacro();
        if (macro is null)
            return;

        if (KeyNames.IsMouseBind(name))
        {
            macro.TriggerMouse = name;
            macro.TriggerKey = "";
        }
        else
        {
            macro.TriggerKey = name;
            macro.TriggerMouse = "";
        }

        Persist();
        LoadTriggerUi();
        RefreshMacroList();
        UpdateStatus("Trigger: " + macro.TriggerLabel());
    }

    private void UpdateTriggerKeyButton(MacroDefinition? macro)
    {
        _triggerKeyButton.Text = string.IsNullOrWhiteSpace(macro?.TriggerKey) ? "Tecla" : macro!.TriggerKey;
    }

    private void UpdateMacroRow(MacroDefinition macro)
    {
        foreach (ListViewItem item in _macroList.Items)
        {
            if (item.Tag is not MacroDefinition found || found.Id != macro.Id)
                continue;
            if (item.SubItems.Count > 1)
                item.SubItems[1].Text = $"{macro.DurationSeconds:0.00}s";
            if (item.SubItems.Count > 2)
                item.SubItems[2].Text = macro.EventCount.ToString();
            if (item.SubItems.Count > 3)
            {
                var trigger = macro.TriggerLabel();
                item.SubItems[3].Text = string.IsNullOrEmpty(trigger) ? "—" : trigger;
            }
            _loadingList = true;
            item.Checked = macro.IsOn;
            _loadingList = false;
            break;
        }
    }

    private void ExportMacro()
    {
        var macro = SelectedMacro();
        if (macro is null)
            return;

        using var dialog = new SaveFileDialog
        {
            Filter = "JSON|*.json",
            FileName = macro.Name + ".json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        MacroStore.ExportMacro(macro, dialog.FileName);
        UpdateStatus("Macro exportado.");
    }

    private void ImportMacro()
    {
        using var dialog = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var imported = MacroStore.ImportMacro(dialog.FileName);
        if (imported is null)
        {
            UpdateStatus("Não foi possível importar esse JSON.");
            return;
        }

        imported.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(imported.Name))
            imported.Name = "Macro importado";
        PromoteEventsToSteps(imported);
        _library.Macros.Add(imported);
        Persist();
        RefreshMacroList();
        SelectMacro(imported);
        UpdateStatus("Macro importado: " + imported.Name);
    }

    private void UpdateStatus(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateStatus(text));
            return;
        }

        _status.Text = text;
    }

    private void SetupWatermark()
    {
        if (_watermark is not null)
            return;

        _watermark = new StatusWatermarkForm();
        _watermark.PositionChanged += SaveWatermarkPosition;
        _watermark.HideRequested += HideWatermarkFromOverlay;
        ApplyWatermarkPosition();
        ApplyWatermarkVisibility();
        SyncWatermark();
    }

    private void TearDownWatermark()
    {
        if (_watermark is null)
            return;

        _watermark.PositionChanged -= SaveWatermarkPosition;
        _watermark.HideRequested -= HideWatermarkFromOverlay;
        _watermark.Close();
        _watermark.Dispose();
        _watermark = null;
    }

    private void HideWatermarkFromOverlay()
    {
        _showWatermark.Checked = false;
    }

    private void ApplyWatermarkVisibility()
    {
        if (_watermark is null || _watermark.IsDisposed)
            return;

        if (_settings.ShowWatermark)
        {
            ApplyWatermarkPosition();
            if (!_watermark.Visible)
                _watermark.Show();
            NativeMethods.KeepTopMost(_watermark.Handle);
        }
        else if (_watermark.Visible)
            _watermark.Hide();
    }

    private void ApplyWatermarkPosition()
    {
        if (_watermark is null || _watermark.IsDisposed)
            return;

        if (_settings.WatermarkX == int.MinValue || _settings.WatermarkY == int.MinValue)
        {
            var fallback = StatusWatermarkForm.DefaultLocation();
            _watermark.Place(fallback.X, fallback.Y);
        }
        else
            _watermark.Place(_settings.WatermarkX, _settings.WatermarkY);
    }

    private void SaveWatermarkPosition(Point location)
    {
        _settings.WatermarkX = location.X;
        _settings.WatermarkY = location.Y;
        PersistSettings();
    }

    private void SyncWatermark()
    {
        var name = "";
        if (_playingMacroId is string id)
            name = _library.Macros.FirstOrDefault(macro => macro.Id == id)?.Name ?? "";
        _watermark?.SetActive(_playingMacroId is not null, name);
    }

    private void SetupTray()
    {
        _trayIcon = CreateTrayIcon();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => RestoreFromTray());
        var watermarkItem = new ToolStripMenuItem("Marca d'água")
        {
            Checked = _settings.ShowWatermark,
            CheckOnClick = true
        };
        watermarkItem.CheckedChanged += (_, _) =>
        {
            if (_showWatermark.Checked == watermarkItem.Checked)
                return;
            _showWatermark.Checked = watermarkItem.Checked;
        };
        _showWatermark.CheckedChanged += (_, _) => watermarkItem.Checked = _showWatermark.Checked;
        menu.Items.Add(watermarkItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Fechar", null, (_, _) =>
        {
            _reallyClose = true;
            Close();
        });

        _tray = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = AppInfo.Name,
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                RestoreFromTray();
        };
    }

    internal void PauseTriggers()
    {
        if (_triggerPause++ == 0 && _hotkeys is not null)
            _hotkeys.MacroTriggersEnabled = false;
    }

    internal void ResumeTriggers()
    {
        if (_triggerPause <= 0)
            return;
        if (--_triggerPause == 0 && !IsDisposed && _hotkeys is not null)
            _hotkeys.MacroTriggersEnabled = true;
    }

    private void HideToTray()
    {
        if (_inTray || IsDisposed || !IsHandleCreated)
            return;

        _inTray = true;
        if (_tray is not null)
            _tray.Visible = true;
        Hide();
        ApplyWatermarkVisibility();
    }

    private void RestoreFromTray()
    {
        if (IsDisposed)
            return;

        _inTray = false;
        if (_tray is not null)
            _tray.Visible = true;
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        FitToWorkingArea();
        ApplyWatermarkVisibility();
    }

    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var fill = new SolidBrush(Color.FromArgb(37, 99, 235));
        g.FillEllipse(fill, 1, 1, 14, 14);
        using var pen = new Pen(Color.White, 1.5f);
        g.DrawEllipse(pen, 3, 3, 10, 10);
        var handle = bmp.GetHicon();
        using var created = Icon.FromHandle(handle);
        var clone = (Icon)created.Clone();
        NativeMethods.DestroyIcon(handle);
        return clone;
    }
}
