using System.Drawing.Drawing2D;
using DailyMacro.Services;

namespace DailyMacro.UI;

internal static class Theme
{
    public static bool IsDark { get; private set; }

    public static Color Bg { get; private set; }
    public static Color Surface { get; private set; }
    public static Color Header { get; private set; }
    public static Color Border { get; private set; }
    public static Color Text { get; private set; }
    public static Color Muted { get; private set; }
    public static Color Accent { get; private set; }
    public static Color AccentSoft { get; private set; }
    public static Color Ok { get; private set; }
    public static Color OkSoft { get; private set; }
    public static Color Button { get; private set; }
    public static Color ButtonBorder { get; private set; }
    public static Color Selected { get; private set; }
    public static Color SelectedText { get; private set; }

    public static readonly Font Ui = new("Segoe UI", 10f);
    public static readonly Font UiSmall = new("Segoe UI", 9f);
    public static readonly Font UiBold = new("Segoe UI Semibold", 10f);
    public static readonly Font Title = new("Segoe UI Semibold", 18f);
    public static readonly Font Section = new("Segoe UI Semibold", 11.5f);
    public static readonly Font Caption = new("Segoe UI Semibold", 8.5f);

    private static readonly HashSet<Control> Rounded = [];

    static Theme() => ApplyLight();

    public static void SetDark(bool dark)
    {
        IsDark = dark;
        if (dark)
            ApplyDark();
        else
            ApplyLight();
    }

    private static void ApplyLight()
    {
        Bg = Color.FromArgb(238, 240, 246);
        Surface = Color.FromArgb(255, 255, 255);
        Header = Color.FromArgb(248, 249, 252);
        Border = Color.FromArgb(226, 229, 238);
        Text = Color.FromArgb(17, 24, 39);
        Muted = Color.FromArgb(107, 114, 128);
        Accent = Color.FromArgb(79, 70, 229);
        AccentSoft = Color.FromArgb(238, 242, 255);
        Ok = Color.FromArgb(5, 150, 105);
        OkSoft = Color.FromArgb(209, 250, 229);
        Button = Color.FromArgb(243, 244, 248);
        ButtonBorder = Color.FromArgb(203, 208, 220);
        Selected = Color.FromArgb(224, 231, 255);
        SelectedText = Color.FromArgb(67, 56, 202);
    }

    private static void ApplyDark()
    {
        Bg = Color.FromArgb(12, 12, 16);
        Surface = Color.FromArgb(22, 22, 28);
        Header = Color.FromArgb(28, 28, 36);
        Border = Color.FromArgb(48, 48, 58);
        Text = Color.FromArgb(244, 244, 245);
        Muted = Color.FromArgb(161, 161, 170);
        Accent = Color.FromArgb(129, 140, 248);
        AccentSoft = Color.FromArgb(58, 66, 118);
        Ok = Color.FromArgb(52, 211, 153);
        OkSoft = Color.FromArgb(20, 48, 40);
        Button = Color.FromArgb(44, 44, 56);
        ButtonBorder = Color.FromArgb(92, 92, 110);
        Selected = Color.FromArgb(72, 82, 148);
        SelectedText = Color.White;
    }

    public static Button Primary(string text, Color back, Color fore)
    {
        var role = back == OkSoft || fore == Ok ? "ok"
            : back == Accent ? "accent"
            : "neutral";
        var button = new ChromeButton
        {
            Text = text,
            BackColor = back,
            ForeColor = fore,
            Font = UiBold,
            Height = 34,
            Cursor = Cursors.Hand,
            Padding = new Padding(12, 0, 12, 0),
            Tag = role,
            TextAlign = ContentAlignment.MiddleCenter,
            UseMnemonic = false,
            AutoEllipsis = false
        };
        return button;
    }

    public static Button Ghost(string text)
    {
        return new ChromeButton
        {
            Text = text,
            BackColor = Button,
            ForeColor = Text,
            Font = Ui,
            Height = 32,
            Cursor = Cursors.Hand,
            Tag = "ghost",
            TextAlign = ContentAlignment.MiddleCenter,
            UseMnemonic = false,
            AutoEllipsis = false,
            Padding = new Padding(6, 0, 6, 0)
        };
    }

    public static GraphicsPath RoundRect(Rectangle bounds, int radius, bool fitStroke = true)
    {
        var path = new GraphicsPath();
        var r = (RectangleF)bounds;
        if (fitStroke)
        {
            r.Width = Math.Max(1f, r.Width - 1f);
            r.Height = Math.Max(1f, r.Height - 1f);
        }

        if (r.Width < 2 || r.Height < 2)
        {
            path.AddRectangle(r);
            return path;
        }

        var d = Math.Min(Math.Max(2, radius) * 2f, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Apply(Control root)
    {
        var hwnd = HandleOf(root);
        NativeMethods.BeginRedrawFreeze(hwnd);
        try
        {
            root.SuspendLayout();
            PaintTree(root);
            ApplyChrome(root);
        }
        finally
        {
            root.ResumeLayout(true);
            NativeMethods.EndRedrawFreeze(hwnd);
            if (hwnd == IntPtr.Zero)
                root.Invalidate(true);
        }
    }

    private static IntPtr HandleOf(Control root)
    {
        var form = root as Form ?? root.FindForm();
        if (form is { IsHandleCreated: true })
            return form.Handle;
        return root.IsHandleCreated ? root.Handle : IntPtr.Zero;
    }

    private static void PaintTree(Control root)
    {
        Paint(root);
        foreach (Control child in root.Controls)
            PaintTree(child);
    }

    public static void ApplyChrome(Control root)
    {
        if (NeedsChrome(root) && root.IsHandleCreated)
        {
            NativeMethods.ApplyControlTheme(root.Handle, IsDark, ChromeThemeName(root));
            if (root is ListView list)
                ThemeListHeader(list);
        }
        foreach (Control child in root.Controls)
            ApplyChrome(child);
    }

    private static string ChromeThemeName(Control control)
    {
        if (!IsDark)
            return "Explorer";
        return control is ComboBox or TextBox or NumericUpDown
            ? "DarkMode_CFD"
            : "DarkMode_Explorer";
    }

    private static void ThemeListHeader(ListView list)
    {
        if (!list.IsHandleCreated)
            return;
        var header = NativeMethods.SendMessage(list.Handle, NativeMethods.LvmGetHeader, IntPtr.Zero, IntPtr.Zero);
        if (header != IntPtr.Zero)
            NativeMethods.ApplyControlTheme(header, IsDark);
        list.Invalidate(true);
    }

    private static bool NeedsChrome(Control control) =>
        control is Form or ListView or ComboBox or TextBox or NumericUpDown;

    private static void Paint(Control control)
    {
        switch (control)
        {
            case Form:
                control.BackColor = Bg;
                control.ForeColor = Text;
                break;
            case CardPanel card:
                card.Invalidate();
                break;
            case TableLayoutPanel:
            case FlowLayoutPanel:
                control.BackColor = control.Parent is Form ? Bg : Color.Transparent;
                control.ForeColor = Text;
                break;
            case Panel panel:
                panel.BackColor = panel.Padding.All == 1 ? Surface : Bg;
                break;
            case Label label:
                if (label.Tag as string == "chip")
                {
                    label.BackColor = AccentSoft;
                    label.ForeColor = Accent;
                    break;
                }
                if (label.BorderStyle != BorderStyle.None)
                {
                    label.BackColor = Surface;
                    label.ForeColor = Text;
                }
                else
                {
                    label.BackColor = Color.Transparent;
                    label.ForeColor = label.Font.Equals(Title) || label.Font.Equals(Section) || label.Font.Equals(UiBold)
                        ? Text
                        : Muted;
                }
                break;
            case CheckBox:
            case RadioButton:
                control.BackColor = Color.Transparent;
                control.ForeColor = Text;
                break;
            case ListView list:
                StyleList(list);
                break;
            case Button button:
                StyleButton(button);
                break;
            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = Surface;
                combo.ForeColor = Text;
                break;
            case NumericUpDown:
            case TextBox:
                control.BackColor = Surface;
                control.ForeColor = Text;
                break;
            case TrackBar track:
                track.BackColor = Bg;
                track.ForeColor = Text;
                break;
        }
    }

    private static void StyleList(ListView list)
    {
        list.BackColor = Surface;
        list.ForeColor = Text;
        list.GridLines = false;
        list.BorderStyle = BorderStyle.None;
        if (list.OwnerDraw)
        {
            list.Invalidate();
            return;
        }

        list.OwnerDraw = true;
        list.DrawColumnHeader += (_, e) =>
        {
            using var bg = new SolidBrush(Header);
            e.Graphics.FillRectangle(bg, e.Bounds);
            var numberCol = !list.CheckBoxes && e.ColumnIndex == 0;
            TextRenderer.DrawText(
                e.Graphics,
                e.Header?.Text ?? "",
                UiSmall,
                Rectangle.Inflate(e.Bounds, numberCol ? -2 : -8, 0),
                Muted,
                (numberCol ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left)
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPadding
                | TextFormatFlags.NoPrefix);
            using var line = new Pen(Border);
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        };
        list.DrawItem += (_, e) =>
        {
            if (e.Item is null)
                return;
            FillRow(e.Graphics, e.Bounds, e.Item);
        };
        list.DrawSubItem += (_, e) =>
        {
            if (e.Item is null)
                return;

            FillRow(e.Graphics, e.Bounds, e.Item);
            var color = e.Item.Selected ? SelectedText : Text;

            if (list.CheckBoxes && e.ColumnIndex == 0)
            {
                var boxSize = 15;
                var box = new Rectangle(
                    e.Bounds.X + 4,
                    e.Bounds.Y + (e.Bounds.Height - boxSize) / 2,
                    boxSize,
                    boxSize);
                CheckBoxRenderer.DrawCheckBox(
                    e.Graphics,
                    box.Location,
                    e.Item.Checked
                        ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal
                        : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal);
                TextRenderer.DrawText(
                    e.Graphics,
                    e.Item.Text,
                    list.Font,
                    new Rectangle(box.Right + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Right - box.Right - 8), e.Bounds.Height),
                    color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                return;
            }

            var numberCol = !list.CheckBoxes && e.ColumnIndex == 0;
            var bounds = Rectangle.Inflate(e.Bounds, numberCol ? -1 : -6, 0);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            if (numberCol)
                flags |= TextFormatFlags.HorizontalCenter;
            else
                flags |= TextFormatFlags.Left | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(
                e.Graphics,
                e.SubItem?.Text ?? "",
                list.Font,
                bounds,
                color,
                flags);
        };
    }

    private static void FillRow(Graphics graphics, Rectangle bounds, ListViewItem item)
    {
        var bg = item.Selected ? Selected : Surface;
        using var brush = new SolidBrush(bg);
        graphics.FillRectangle(brush, bounds);
    }

    private static void StyleButton(Button button)
    {
        button.FlatAppearance.BorderSize = 0;
        button.UseVisualStyleBackColor = false;
        switch (button.Tag as string)
        {
            case "ok":
                button.BackColor = OkSoft;
                button.ForeColor = Ok;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(
                    Math.Min(255, OkSoft.R + 12),
                    Math.Min(255, OkSoft.G + 8),
                    Math.Min(255, OkSoft.B + 8));
                break;
            case "accent":
                button.BackColor = Accent;
                button.ForeColor = Color.White;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(
                    Math.Max(0, Accent.R - 18),
                    Math.Max(0, Accent.G - 12),
                    Math.Max(0, Accent.B - 8));
                break;
            default:
                button.BackColor = Button;
                button.ForeColor = Text;
                button.FlatAppearance.MouseOverBackColor = AccentSoft;
                break;
        }

        button.Invalidate();
    }

    internal static void PaintChromeButton(Button button, Graphics graphics)
    {
        if (button.Width < 6 || button.Height < 6)
            return;

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.None;

        using (var wipe = new SolidBrush(BehindButton(button)))
            graphics.FillRectangle(wipe, button.ClientRectangle);

        var box = new Rectangle(1, 1, Math.Max(1, button.Width - 2), Math.Max(1, button.Height - 2));
        using var path = RoundRect(box, Math.Min(8, Math.Max(4, button.Height / 2)), fitStroke: false);
        using var fill = new SolidBrush(button.BackColor);
        var border = button.Tag as string is "ok" or "accent"
            ? Color.FromArgb(50, Color.White)
            : ButtonBorder;
        using var pen = new Pen(border, 1f);
        graphics.FillPath(fill, path);
        graphics.DrawPath(pen, path);

        TextRenderer.DrawText(
            graphics,
            button.Text,
            button.Font,
            button.ClientRectangle,
            button.ForeColor,
            TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.NoPadding
            | TextFormatFlags.SingleLine);
    }

    private static Color BehindButton(Control button)
    {
        for (var parent = button.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is CardPanel)
                return Surface;
            if (parent is Form)
                return Bg;
        }

        return Bg;
    }

    public static void Round(Control control, int radius)
    {
        if (Rounded.Add(control))
            control.Resize += (_, _) => ApplyRound(control, radius);
        ApplyRound(control, radius);
    }

    private static void ApplyRound(Control control, int radius)
    {
        if (control.Width < 4 || control.Height < 4)
            return;
        var bounds = control.ClientRectangle;
        bounds.Width = Math.Max(1, bounds.Width);
        bounds.Height = Math.Max(1, bounds.Height);
        using var path = RoundRect(bounds, radius);
        var region = new Region(path);
        control.Region?.Dispose();
        control.Region = region;
    }
}

internal sealed class ChromeButton : Button
{
    public ChromeButton()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
    }

    protected override void OnPaint(PaintEventArgs e) => Theme.PaintChromeButton(this, e.Graphics);
}

internal sealed class CardPanel : Panel
{
    public CardPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Fill;
        Padding = new Padding(16);
        Margin = new Padding(0);
        Tag = "card";
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyShape();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyShape();
    }

    private void ApplyShape()
    {
        if (Width < 8 || Height < 8)
            return;
        using var path = Theme.RoundRect(new Rectangle(0, 0, Width, Height), 14, fitStroke: false);
        var region = new Region(path);
        Region?.Dispose();
        Region = region;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.None;
        var rect = ClientRectangle;
        if (rect.Width < 8 || rect.Height < 8)
            return;

        using var path = Theme.RoundRect(rect, 14);
        using var fill = new SolidBrush(Theme.Surface);
        using var pen = new Pen(Theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class MacroListView : ListView
{
    public MacroListView()
    {
        DoubleBuffered = true;
        GridLines = false;
        BorderStyle = BorderStyle.None;
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        OwnerDraw = false;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x000F)
            PaintEmptyArea();
    }

    private void PaintEmptyArea()
    {
        if (!IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        using var graphics = Graphics.FromHwnd(Handle);
        using var fill = new SolidBrush(BackColor.IsEmpty ? Theme.Surface : BackColor);

        var top = Items.Count > 0
            ? Items[Items.Count - 1].Bounds.Bottom
            : (HeaderStyle == ColumnHeaderStyle.None ? 0 : 28);
        if (top < 0)
            top = 0;
        if (top < ClientSize.Height)
            graphics.FillRectangle(fill, 0, top, ClientSize.Width, ClientSize.Height - top);

        var right = 0;
        foreach (ColumnHeader column in Columns)
            right += column.Width;
        if (right < ClientSize.Width)
            graphics.FillRectangle(fill, right, 0, ClientSize.Width - right, ClientSize.Height);
    }
}
