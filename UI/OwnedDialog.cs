namespace DailyMacro.UI;

internal static class OwnedDialog
{
    private static void Prepare(Form dialog, IWin32Window? owner = null)
    {
        dialog.ShowInTaskbar = false;
        dialog.TopMost = true;
        if (owner is Form form)
            dialog.Owner = form;

        dialog.Shown -= BringForward;
        dialog.Shown += BringForward;
    }

    public static DialogResult ShowModal(Form dialog, Form owner)
    {
        Prepare(dialog, owner);
        var wasTopMost = owner.TopMost;
        var main = owner as MainForm;
        main?.PauseTriggers();
        try
        {
            owner.TopMost = false;
            dialog.TopMost = true;
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Load += (_, _) => PlaceOnScreen(dialog, owner);
            return dialog.ShowDialog(owner);
        }
        finally
        {
            main?.ResumeTriggers();
            if (!owner.IsDisposed)
                owner.TopMost = wasTopMost;
        }
    }

    private static void BringForward(object? sender, EventArgs e)
    {
        if (sender is not Form dialog)
            return;

        PlaceOnScreen(dialog, dialog.Owner as Form);
        dialog.TopMost = true;
        dialog.Activate();
        dialog.BringToFront();
    }

    private static void PlaceOnScreen(Form dialog, Form? owner)
    {
        var area = owner is { IsHandleCreated: true }
            ? Screen.FromControl(owner).WorkingArea
            : Screen.FromPoint(Cursor.Position).WorkingArea;

        var width = Math.Max(dialog.Width, dialog.PreferredSize.Width);
        var height = Math.Max(dialog.Height, dialog.PreferredSize.Height);
        int x;
        int y;
        if (owner is { IsHandleCreated: true, Visible: true })
        {
            x = owner.Left + (owner.Width - width) / 2;
            y = owner.Top + (owner.Height - height) / 2;
        }
        else
        {
            x = area.Left + (area.Width - width) / 2;
            y = area.Top + (area.Height - height) / 2;
        }

        x = Math.Max(area.Left, Math.Min(x, area.Right - width));
        y = Math.Max(area.Top, Math.Min(y, area.Bottom - height));
        dialog.Location = new Point(x, y);
    }
}
