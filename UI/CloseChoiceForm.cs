using AppInfo = DailyMacro.AppInfo;

namespace DailyMacro.UI;

internal enum CloseChoice
{
    Cancel,
    Tray,
    Exit
}

internal sealed class CloseChoiceForm : Form
{
    public CloseChoice Choice { get; private set; } = CloseChoice.Cancel;

    public CloseChoiceForm()
    {
        Text = AppInfo.Name;
        Font = Theme.Ui;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(440, 188);
        BackColor = Theme.Bg;
        ShowInTaskbar = false;
        TopMost = true;

        var heading = new Label
        {
            Text = "Fechar ou minimizar para a bandeja?",
            Font = Theme.Section,
            AutoSize = true,
            Location = new Point(20, 18),
            ForeColor = Theme.Text
        };

        var body = new Label
        {
            Text = "Minimizar deixa o XoMacro na área de notificação, com o Trigger ligado. Fechar encerra o programa.",
            Location = new Point(20, 48),
            Size = new Size(400, 48),
            ForeColor = Theme.Muted
        };

        var tray = Theme.Primary("Minimizar para a bandeja", Theme.Accent, Color.White);
        tray.Location = new Point(20, 118);
        tray.Width = 210;
        tray.Height = 38;
        tray.Click += (_, _) =>
        {
            Choice = CloseChoice.Tray;
            DialogResult = DialogResult.OK;
            Close();
        };

        var exit = Theme.Ghost("Fechar");
        exit.Location = new Point(240, 118);
        exit.Width = 90;
        exit.Height = 38;
        exit.Click += (_, _) =>
        {
            Choice = CloseChoice.Exit;
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancel = Theme.Ghost("Cancelar");
        cancel.Location = new Point(336, 118);
        cancel.Width = 84;
        cancel.Height = 38;
        cancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { heading, body, tray, exit, cancel });
        CancelButton = cancel;
        AcceptButton = tray;
        Theme.Apply(this);
    }

    public static CloseChoice Ask(Form owner)
    {
        using var form = new CloseChoiceForm();
        var result = OwnedDialog.ShowModal(form, owner);
        return result == DialogResult.OK ? form.Choice : CloseChoice.Cancel;
    }
}
