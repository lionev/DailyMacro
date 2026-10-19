using DailyMacro.Models;
using DailyMacro.Services;

namespace DailyMacro.UI;

internal sealed class StepEditForm : Form, IMessageFilter
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmChar = 0x0102;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;

    private readonly ComboBox _type;
    private readonly Label _comboBox;
    private readonly Label _comboHint;
    private readonly Button _captureCombo;
    private readonly Button _confirmCombo;
    private readonly Button _clearCombo;
    private readonly NumericUpDown _hold;
    private readonly NumericUpDown _after;
    private readonly CheckBox _tapRepeat;
    private readonly NumericUpDown _tapInterval;
    private readonly NumericUpDown _wait;
    private readonly TextBox _text;
    private readonly Panel _comboPanel;
    private readonly Panel _textPanel;
    private readonly Panel _waitPanel;
    private readonly Panel _clickPanel;
    private readonly int _cycleTotal;
    private readonly CheckBox[] _cycleBoxes;
    private List<string> _keys = [];
    private readonly List<string> _mouseButtons = [];
    private bool _capturing;

    public MacroStep Result { get; }

    public StepEditForm(MacroStep? existing, int cycleCount = 1)
    {
        Result = existing?.Clone() ?? new MacroStep();
        _keys = [.. Result.Keys];
        _mouseButtons.AddRange(Result.AllMouseButtons());
        _cycleTotal = Math.Clamp(cycleCount <= 0 ? 1 : cycleCount, 1, 8);

        Text = existing is null ? "Novo passo" : "Editar passo";
        Font = Theme.Ui;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(500, _cycleTotal > 1 ? 568 : 510);
        BackColor = Theme.Bg;
        ShowInTaskbar = false;
        TopMost = true;

        var y = 18;
        Controls.Add(LabelAt("Tipo", 20, y));
        _type = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(140, y - 3),
            Width = 320,
            Font = Theme.Ui
        };
        _type.Items.AddRange(["Combinação", "Digitar texto", "Esperar", "Clique do mouse"]);
        _type.SelectedIndexChanged += (_, _) => UpdatePanels();
        Controls.Add(_type);
        y += 42;

        _comboPanel = MakePanel(y);
        _comboHint = new Label
        {
            Text = "Clique em Capturar. Ex.: S + clique esquerdo + clique direito, depois Confirmar.",
            Location = new Point(0, 4),
            Size = new Size(460, 22),
            ForeColor = Theme.Muted
        };
        _comboPanel.Controls.Add(_comboHint);

        _comboBox = new Label
        {
            Location = new Point(0, 30),
            Size = new Size(460, 40),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Surface,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 10, 0),
            Font = Theme.UiBold
        };
        _comboPanel.Controls.Add(_comboBox);

        _captureCombo = Theme.Ghost("Capturar");
        _captureCombo.Location = new Point(0, 80);
        _captureCombo.Size = new Size(110, 32);
        _captureCombo.Click += (_, _) => StartCapture();
        _comboPanel.Controls.Add(_captureCombo);

        _confirmCombo = Theme.Primary("Confirmar", Theme.OkSoft, Theme.Ok);
        _confirmCombo.Location = new Point(118, 80);
        _confirmCombo.Size = new Size(110, 32);
        _confirmCombo.Enabled = false;
        _confirmCombo.Click += (_, _) => ConfirmCapture();
        _comboPanel.Controls.Add(_confirmCombo);

        _clearCombo = Theme.Ghost("Limpar");
        _clearCombo.Location = new Point(236, 80);
        _clearCombo.Size = new Size(90, 32);
        _clearCombo.Click += (_, _) => ClearCombo();
        _comboPanel.Controls.Add(_clearCombo);

        var mouseLeft = Theme.Ghost("Esq");
        mouseLeft.Location = new Point(332, 80);
        mouseLeft.Size = new Size(40, 32);
        mouseLeft.Click += (_, _) => ToggleMouse("Left");
        _comboPanel.Controls.Add(mouseLeft);

        var mouseRight = Theme.Ghost("Dir");
        mouseRight.Location = new Point(376, 80);
        mouseRight.Size = new Size(40, 32);
        mouseRight.Click += (_, _) => ToggleMouse("Right");
        _comboPanel.Controls.Add(mouseRight);

        var mouseMiddle = Theme.Ghost("Meio");
        mouseMiddle.Location = new Point(420, 80);
        mouseMiddle.Size = new Size(40, 32);
        mouseMiddle.Click += (_, _) => ToggleMouse("Middle");
        _comboPanel.Controls.Add(mouseMiddle);

        _comboPanel.Controls.Add(LabelAt("Segurar", 0, 128));
        _hold = Number(120, 124, 15, 10000, Result.HoldMs);
        _comboPanel.Controls.Add(_hold);
        _comboPanel.Controls.Add(MsLabel(250, 128));
        _comboPanel.Controls.Add(LabelAt("Esperar depois", 0, 168));
        _after = Number(120, 164, 0, 60000, Result.DelayAfterMs);
        _comboPanel.Controls.Add(_after);
        _comboPanel.Controls.Add(MsLabel(250, 168));

        _tapRepeat = new CheckBox
        {
            Text = "Clicar repetido no Segurar",
            Location = new Point(0, 204),
            AutoSize = true,
            ForeColor = Theme.Text,
            Checked = Result.TapIntervalMs > 0
        };
        _comboPanel.Controls.Add(_tapRepeat);

        _comboPanel.Controls.Add(LabelAt("a cada", 0, 236));
        _tapInterval = Number(120, 232, 10, 500, Result.TapIntervalMs > 0 ? Result.TapIntervalMs : 20);
        _comboPanel.Controls.Add(_tapInterval);
        _comboPanel.Controls.Add(MsLabel(250, 236));
        var tapHint = new Label
        {
            Text = "Ex.: tecla 1, Segurar 1000 ms, a cada 20 ms → clica 1 o tempo todo.",
            Location = new Point(0, 268),
            Size = new Size(460, 20),
            ForeColor = Theme.Muted
        };
        _comboPanel.Controls.Add(tapHint);
        _tapRepeat.CheckedChanged += (_, _) => _tapInterval.Enabled = _tapRepeat.Checked;
        _tapInterval.Enabled = _tapRepeat.Checked;
        Controls.Add(_comboPanel);

        _textPanel = MakePanel(y);
        _textPanel.Controls.Add(LabelAt("Texto", 0, 8));
        _text = new TextBox
        {
            Location = new Point(120, 4),
            Width = 320,
            Height = 90,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = Result.Text
        };
        _textPanel.Controls.Add(_text);
        _textPanel.Controls.Add(LabelAt("Esperar depois", 0, 108));
        var afterText = Number(120, 104, 0, 60000, Result.DelayAfterMs);
        afterText.Name = "afterText";
        _textPanel.Controls.Add(afterText);
        _textPanel.Controls.Add(MsLabel(250, 108));
        Controls.Add(_textPanel);

        _waitPanel = MakePanel(y);
        _waitPanel.Controls.Add(LabelAt("Duração", 0, 8));
        _wait = Number(120, 4, 10, 120000, Result.WaitMs > 0 ? Result.WaitMs : 200);
        _waitPanel.Controls.Add(_wait);
        _waitPanel.Controls.Add(MsLabel(250, 8));
        Controls.Add(_waitPanel);

        _clickPanel = MakePanel(y);
        _clickPanel.Controls.Add(LabelAt("Botão", 0, 8));
        var clickMouse = new ComboBox
        {
            Name = "clickMouse",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(120, 4),
            Width = 320
        };
        clickMouse.Items.AddRange(["Clique esquerdo", "Clique direito", "Clique do meio"]);
        _clickPanel.Controls.Add(clickMouse);
        _clickPanel.Controls.Add(LabelAt("Segurar", 0, 50));
        var clickHold = Number(120, 46, 15, 5000, Result.HoldMs);
        clickHold.Name = "clickHold";
        _clickPanel.Controls.Add(clickHold);
        _clickPanel.Controls.Add(MsLabel(250, 50));
        _clickPanel.Controls.Add(LabelAt("Esperar depois", 0, 90));
        var clickAfter = Number(120, 86, 0, 60000, Result.DelayAfterMs);
        clickAfter.Name = "clickAfter";
        _clickPanel.Controls.Add(clickAfter);
        _clickPanel.Controls.Add(MsLabel(250, 90));
        Controls.Add(_clickPanel);

        var buttonY = 452;
        if (_cycleTotal > 1)
        {
            buttonY = 510;
            Controls.Add(LabelAt("Tocar nos ciclos", 20, 398));
            _cycleBoxes = new CheckBox[_cycleTotal];
            var selected = Result.Cycles
                .Where(cycle => cycle >= 1 && cycle <= _cycleTotal)
                .Distinct()
                .ToHashSet();
            var all = selected.Count == 0 || selected.Count >= _cycleTotal;
            var flow = new FlowLayoutPanel
            {
                Location = new Point(140, 392),
                Size = new Size(320, 32),
                WrapContents = false,
                Margin = new Padding(0)
            };
            for (var i = 0; i < _cycleTotal; i++)
            {
                var cycle = i + 1;
                var box = new CheckBox
                {
                    Text = cycle.ToString(),
                    AutoSize = true,
                    Checked = all || selected.Contains(cycle),
                    Tag = cycle,
                    Margin = new Padding(0, 4, 12, 0)
                };
                _cycleBoxes[i] = box;
                flow.Controls.Add(box);
            }
            Controls.Add(flow);
            Controls.Add(new Label
            {
                Text = "Desmarque o ciclo em que este passo não entra. Ex.: a 4 só no ciclo 1.",
                Location = new Point(20, 428),
                Size = new Size(460, 22),
                ForeColor = Theme.Muted
            });
        }
        else
        {
            _cycleBoxes = [];
        }

        var ok = Theme.Primary("Salvar", Theme.Accent, Color.White);
        ok.Name = "saveStep";
        ok.Location = new Point(270, buttonY);
        ok.Width = 100;
        ok.Height = 38;
        ok.Click += (_, _) => SaveAndClose();

        var cancel = Theme.Ghost("Cancelar");
        cancel.Name = "cancelStep";
        cancel.Location = new Point(380, buttonY);
        cancel.Width = 100;
        cancel.DialogResult = DialogResult.Cancel;

        Controls.Add(ok);
        Controls.Add(cancel);
        CancelButton = cancel;

        _type.SelectedIndex = Result.Type switch
        {
            StepType.Text => 1,
            StepType.Wait => 2,
            StepType.Click => 3,
            _ => 0
        };
        clickMouse.SelectedIndex = Result.MouseButton switch
        {
            "Right" => 1,
            "Middle" => 2,
            _ => 0
        };
        RefreshComboBox();
        UpdatePanels();
        Theme.Apply(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Application.AddMessageFilter(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Application.RemoveMessageFilter(this);
        base.OnFormClosed(e);
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (!_capturing)
            return false;

        if (m.Msg is WmChar)
            return true;

        if (m.Msg is WmKeyDown or WmSysKeyDown)
        {
            var key = (Keys)(int)m.WParam.ToInt64();
            if (key is Keys.Enter or Keys.Return)
            {
                ConfirmCapture();
                return true;
            }

            if (key is Keys.Escape)
            {
                StopCapture();
                return true;
            }

            AddKey(key);
            return true;
        }

        if (m.Msg is WmKeyUp or WmSysKeyUp)
            return true;

        if (m.Msg is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown)
        {
            if (IsOverCommandButton())
                return false;

            AddMouse(m.Msg, m.WParam);
            return true;
        }

        return false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_capturing)
            return base.ProcessCmdKey(ref msg, keyData);

        var code = keyData & Keys.KeyCode;
        if (code is Keys.Enter or Keys.Return)
        {
            ConfirmCapture();
            return true;
        }

        if (code is Keys.Escape)
        {
            StopCapture();
            return true;
        }

        return true;
    }

    private void StartCapture()
    {
        _capturing = true;
        _keys.Clear();
        _mouseButtons.Clear();
        _confirmCombo.Enabled = true;
        _captureCombo.Enabled = false;
        _comboBox.BackColor = Theme.AccentSoft;
        _comboHint.Text = "Pressione teclas e cliques (pode ser 3 botões). Enter confirma · Esc cancela.";
        RefreshComboBox();
        _comboBox.Focus();
    }

    private void ConfirmCapture()
    {
        if (!_capturing)
            return;
        StopCapture();
        _comboHint.Text = "Combinação confirmada. Ajuste o tempo e clique em Salvar.";
    }

    private void StopCapture()
    {
        _capturing = false;
        _confirmCombo.Enabled = false;
        _captureCombo.Enabled = true;
        _comboBox.BackColor = Theme.Surface;
        if (_comboHint.Text.StartsWith("Pressione", StringComparison.Ordinal))
            _comboHint.Text = "Clique em Capturar. Monte a combinação e confirme.";
        RefreshComboBox();
    }

    private void ClearCombo()
    {
        _keys.Clear();
        _mouseButtons.Clear();
        StopCapture();
        _comboHint.Text = "Combinação limpa. Clique em Capturar para gravar outra.";
        RefreshComboBox();
    }

    private void AddKey(Keys key)
    {
        var name = KeyNames.FromKeys(key);
        if (string.IsNullOrWhiteSpace(name) || name == "None")
            return;
        if (_keys.Contains(name, StringComparer.OrdinalIgnoreCase))
            return;
        _keys.Add(name);
        RefreshComboBox();
    }

    private void AddMouse(int msg, IntPtr wParam)
    {
        var button = msg switch
        {
            WmRButtonDown => "Right",
            WmMButtonDown => "Middle",
            WmXButtonDown => (((wParam.ToInt64() >> 16) & 0xFFFF) == 2) ? "X2" : "X1",
            _ => "Left"
        };
        if (_mouseButtons.Contains(button, StringComparer.OrdinalIgnoreCase))
            return;
        _mouseButtons.Add(button);
        RefreshComboBox();
    }

    private void ToggleMouse(string button)
    {
        if (_mouseButtons.Exists(item => item.Equals(button, StringComparison.OrdinalIgnoreCase)))
            _mouseButtons.RemoveAll(item => item.Equals(button, StringComparison.OrdinalIgnoreCase));
        else
            _mouseButtons.Add(button);
        RefreshComboBox();
    }

    private bool IsOverCommandButton()
    {
        foreach (var button in new Control[] { _confirmCombo, _clearCombo, _captureCombo })
        {
            if (button.RectangleToScreen(button.ClientRectangle).Contains(Cursor.Position))
                return true;
        }

        foreach (Control control in Controls)
        {
            if (control is Button button &&
                button.Name is "saveStep" or "cancelStep" &&
                button.RectangleToScreen(button.ClientRectangle).Contains(Cursor.Position))
                return true;
        }

        return false;
    }

    private void SaveAndClose()
    {
        if (_capturing)
            ConfirmCapture();

        Result.Type = _type.SelectedIndex switch
        {
            1 => StepType.Text,
            2 => StepType.Wait,
            3 => StepType.Click,
            _ => StepType.Combo
        };

        switch (Result.Type)
        {
            case StepType.Combo:
                Result.Keys = [.. _keys];
                Result.MouseButtons = [.. _mouseButtons];
                Result.MouseButton = string.Join(",", _mouseButtons);
                Result.HasPosition = false;
                Result.X = 0;
                Result.Y = 0;
                Result.HoldMs = (int)_hold.Value;
                Result.DelayAfterMs = (int)_after.Value;
                Result.TapIntervalMs = _tapRepeat.Checked ? (int)_tapInterval.Value : 0;
                break;
            case StepType.Text:
                Result.Text = _text.Text;
                Result.DelayAfterMs = (int)((NumericUpDown)_textPanel.Controls["afterText"]!).Value;
                Result.TapIntervalMs = 0;
                break;
            case StepType.Wait:
                Result.WaitMs = (int)_wait.Value;
                Result.TapIntervalMs = 0;
                break;
            case StepType.Click:
                Result.MouseButton = ((ComboBox)_clickPanel.Controls["clickMouse"]!).SelectedIndex switch
                {
                    1 => "Right",
                    2 => "Middle",
                    _ => "Left"
                };
                Result.MouseButtons = string.IsNullOrEmpty(Result.MouseButton) ? [] : [Result.MouseButton];
                Result.HasPosition = false;
                Result.X = 0;
                Result.Y = 0;
                Result.HoldMs = (int)((NumericUpDown)_clickPanel.Controls["clickHold"]!).Value;
                Result.DelayAfterMs = (int)((NumericUpDown)_clickPanel.Controls["clickAfter"]!).Value;
                Result.TapIntervalMs = 0;
                break;
        }

        Result.Cycles = ReadCycles();

        if (Result.Type == StepType.Combo && Result.Keys.Count == 0 && Result.AllMouseButtons().Count == 0)
        {
            MessageBox.Show(this, "Capture uma combinação (por exemplo W + C ou Shift + clique) e confirme.", "Passo vazio",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private List<int> ReadCycles()
    {
        if (_cycleBoxes.Length == 0)
            return [];

        var selected = _cycleBoxes
            .Where(box => box.Checked)
            .Select(box => (int)box.Tag!)
            .ToList();
        if (selected.Count == 0 || selected.Count >= _cycleTotal)
            return [];
        return selected;
    }

    private void UpdatePanels()
    {
        if (_capturing)
            StopCapture();
        _comboPanel.Visible = _type.SelectedIndex == 0;
        _textPanel.Visible = _type.SelectedIndex == 1;
        _waitPanel.Visible = _type.SelectedIndex == 2;
        _clickPanel.Visible = _type.SelectedIndex == 3;
    }

    private void RefreshComboBox()
    {
        var parts = new List<string>(_keys);
        parts.AddRange(_mouseButtons.Select(KeyNames.MouseDisplay));
        _comboBox.Text = parts.Count == 0
            ? (_capturing ? "Aguardando teclas ou clique…" : "Nenhuma combinação")
            : string.Join(" + ", parts);
    }

    private static Panel MakePanel(int y) => new()
    {
        Location = new Point(20, y),
        Size = new Size(460, 300),
        BackColor = Theme.Bg
    };

    private static Label LabelAt(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = Theme.Muted
    };

    private static Label MsLabel(int x, int y) => new()
    {
        Text = "ms",
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = Theme.Muted
    };

    private static NumericUpDown Number(int x, int y, int min, int max, int value) => new()
    {
        Location = new Point(x, y),
        Width = 110,
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        ThousandsSeparator = true
    };
}
