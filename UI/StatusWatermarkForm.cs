using System.Drawing.Drawing2D;
using DailyMacro.Services;
using AppInfo = DailyMacro.AppInfo;

namespace DailyMacro.UI;

internal sealed class StatusWatermarkForm : Form
{
    public event Action<Point>? PositionChanged;
    public event Action? HideRequested;

    private bool _active;
    private string _macroName = "";
    private bool _dragging;
    private Point _dragOffset;
    private readonly System.Windows.Forms.Timer _topMostTimer;

    public StatusWatermarkForm()
    {
        Text = AppInfo.Name;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ClientSize = new Size(220, 52);
        BackColor = Color.FromArgb(16, 18, 22);
        Opacity = 0.92;
        Cursor = Cursors.SizeAll;
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        var tip = new ToolTip { ShowAlways = true };
        tip.SetToolTip(this, "Arraste para qualquer tela, inclusive outro monitor.\nVerde = Trigger reproduzindo · Vermelho = parado.\nClique direito para ocultar. Clique duplo recentraliza.");

        _topMostTimer = new System.Windows.Forms.Timer { Interval = 8000 };
        _topMostTimer.Tick += (_, _) =>
        {
            if (Visible && IsHandleCreated)
                NativeMethods.KeepTopMost(Handle);
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExNoActivate = 0x08000000;
            const int wsExToolWindow = 0x00000080;
            const int wsExTopMost = 0x00000008;
            var cp = base.CreateParams;
            cp.ExStyle |= wsExNoActivate | wsExToolWindow | wsExTopMost;
            return cp;
        }
    }

    public void SetActive(bool active, string? macroName = null)
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke(() => SetActive(active, macroName));
            return;
        }

        var name = (macroName ?? "").Trim();
        if (_active == active && _macroName == name)
            return;
        _active = active;
        _macroName = name;
        FitContent();
        Invalidate();
    }

    public void Place(int x, int y)
    {
        if (IsDisposed)
            return;
        var point = ClampToScreens(new Point(x, y));
        if (IsHandleCreated)
            NativeMethods.MoveNoActivate(Handle, point.X, point.Y);
        else
            Location = point;
    }

    public static Point DefaultLocation()
    {
        var area = Screen.PrimaryScreen?.WorkingArea
            ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        return new Point(area.Right - 240, area.Top + 18);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FitContent();
        NativeMethods.KeepTopMost(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitContent();
        NativeMethods.KeepTopMost(Handle);
        _topMostTimer.Start();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyRound();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        FitContent();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _topMostTimer.Stop();
        _topMostTimer.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        PaintBadge(e.Graphics);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintBadge(e.Graphics);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragOffset = e.Location;
            Capture = true;
        }
        else if (e.Button == MouseButtons.Right)
            HideRequested?.Invoke();
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            var screen = PointToScreen(e.Location);
            Place(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging && e.Button == MouseButtons.Left)
        {
            _dragging = false;
            Capture = false;
            PositionChanged?.Invoke(Location);
        }

        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Place(area.Right - Width - 20, area.Top + 18);
        PositionChanged?.Invoke(Location);
        base.OnMouseDoubleClick(e);
    }

    protected override void WndProc(ref Message m)
    {
        const int wmDisplayChange = 0x007E;
        const int wmActivate = 0x0006;
        const int waInactive = 0;

        if (m.Msg == wmDisplayChange)
        {
            Place(Left, Top);
            PositionChanged?.Invoke(Location);
        }
        else if (m.Msg == wmActivate && m.WParam.ToInt32() != waInactive)
        {
            NativeMethods.KeepTopMost(Handle);
        }

        base.WndProc(ref m);
    }

    private void FitContent()
    {
        var title = TitleText();
        var status = StatusText();
        var titleWidth = TextRenderer.MeasureText(title, Theme.UiBold, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        var statusWidth = TextRenderer.MeasureText(status, Theme.Caption, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        var width = Math.Clamp(36 + Math.Max(titleWidth, statusWidth) + 28, 188, 420);
        ClientSize = new Size(width, 52);
        ApplyRound();
        NativeMethods.KeepTopMost(Handle);
    }

    private string TitleText() =>
        string.IsNullOrWhiteSpace(_macroName) ? AppInfo.Name : _macroName;

    private string StatusText() => _active ? "Ativo" : "Parado";

    private void PaintBadge(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.FromArgb(16, 18, 22));

        var accent = _active
            ? Color.FromArgb(34, 197, 94)
            : Color.FromArgb(239, 68, 68);
        var fill = _active
            ? Color.FromArgb(236, 12, 32, 22)
            : Color.FromArgb(236, 36, 14, 16);
        var text = Color.FromArgb(248, 250, 252);

        using var path = Theme.RoundRect(ClientRectangle, 13);
        using var bg = new SolidBrush(fill);
        using var border = new Pen(Color.FromArgb(220, accent), 1.6f);
        graphics.FillPath(bg, path);
        graphics.DrawPath(border, path);

        var dot = new Rectangle(14, (Height - 12) / 2, 12, 12);
        using var glow = new SolidBrush(Color.FromArgb(70, accent));
        graphics.FillEllipse(glow, Rectangle.Inflate(dot, 4, 4));
        using var dotFill = new SolidBrush(accent);
        graphics.FillEllipse(dotFill, dot);

        TextRenderer.DrawText(
            graphics,
            TitleText(),
            Theme.UiBold,
            new Rectangle(34, 6, Width - 46, 22),
            text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            graphics,
            StatusText(),
            Theme.Caption,
            new Rectangle(34, 26, Width - 46, 18),
            accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    private void ApplyRound()
    {
        if (Width < 8 || Height < 8)
            return;
        using var path = Theme.RoundRect(ClientRectangle, 13);
        var region = new Region(path);
        Region?.Dispose();
        Region = region;
    }

    private Point ClampToScreens(Point location)
    {
        var bounds = new Rectangle(location, Size);
        foreach (var screen in Screen.AllScreens)
        {
            var hit = Rectangle.Intersect(screen.Bounds, bounds);
            if (hit.Width >= 28 && hit.Height >= 16)
                return location;
        }

        var fallback = Screen.FromPoint(location).Bounds;
        var x = Math.Max(fallback.Left, Math.Min(location.X, fallback.Right - Width));
        var y = Math.Max(fallback.Top, Math.Min(location.Y, fallback.Bottom - Height));
        return new Point(x, y);
    }
}
