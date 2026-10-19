namespace DailyMacro.UI;

internal sealed class PromptForm : Form
{
    private readonly TextBox _box;

    public string Value => _box.Text.Trim();

    public PromptForm(string title, string label, string initial)
    {
        Text = title;
        Font = Theme.Ui;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(420, 160);
        BackColor = Theme.Bg;

        var caption = new Label
        {
            Text = label,
            AutoSize = true,
            Location = new Point(20, 18),
            ForeColor = Theme.Text
        };

        _box = new TextBox
        {
            Text = initial,
            Location = new Point(20, 46),
            Width = 380,
            Font = Theme.Ui
        };

        var ok = Theme.Primary("OK", Theme.Accent, Color.White);
        ok.Location = new Point(214, 100);
        ok.Width = 90;
        ok.Height = 36;
        ok.DialogResult = DialogResult.OK;

        var cancel = Theme.Ghost("Cancelar");
        cancel.Location = new Point(310, 100);
        cancel.Width = 90;
        cancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { caption, _box, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
        Theme.Apply(this);
        ShowInTaskbar = false;
        TopMost = true;
        Shown += (_, _) =>
        {
            Activate();
            BringToFront();
            _box.Focus();
            _box.SelectAll();
        };
    }

    public static string? Ask(IWin32Window owner, string title, string label, string initial)
    {
        using var form = new PromptForm(title, label, initial);
        var parent = owner as Form ?? Form.ActiveForm;
        var result = parent is null
            ? form.ShowDialog(owner)
            : OwnedDialog.ShowModal(form, parent);
        return result == DialogResult.OK && form.Value.Length > 0
            ? form.Value
            : null;
    }
}
