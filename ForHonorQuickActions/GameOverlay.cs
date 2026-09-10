using System;
using System.Drawing;
using System.Windows.Forms;

namespace ForHonorQuickActions
{

internal sealed class GameOverlay : Form
{
    private const int CollapsedWidth = 16;
    private const int ExpandedWidth = 238;
    private const int OverlayHeight = 155;
    private const int EdgeMargin = 10;

    private readonly Button closeButton;
    private readonly Button restartButton;
    private readonly Button cleanRestartButton;
    private readonly Button returnButton;
    private readonly Timer resizeTimer;
    private readonly Timer collapseTimer;
    private int targetWidth;
    private int screenRight;
    private int screenTop;

    public GameOverlay(Action<MainForm.ActionKind> runAction, Action returnToApp)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(23, 27, 35);
        ClientSize = new Size(CollapsedWidth, OverlayHeight);
        Opacity = 0.96;
        AutoScaleMode = AutoScaleMode.None;

        closeButton = MakeButton("Close For Honor", Color.FromArgb(185, 63, 63), () => runAction(MainForm.ActionKind.Close));
        restartButton = MakeButton("Close and reopen For Honor", Color.FromArgb(52, 105, 168), () => runAction(MainForm.ActionKind.Restart));
        cleanRestartButton = MakeButton("Clean restart (game + Ubisoft)", Color.FromArgb(119, 85, 173), () => runAction(MainForm.ActionKind.CleanRestart));
        returnButton = MakeButton("Return to Quick Actions", Color.FromArgb(70, 77, 91), returnToApp);
        Controls.AddRange(new Control[] { closeButton, restartButton, cleanRestartButton, returnButton });

        resizeTimer = new Timer { Interval = 12 };
        resizeTimer.Tick += (sender, args) => AnimateWidth();
        collapseTimer = new Timer { Interval = 300 };
        collapseTimer.Tick += (sender, args) => CollapseIfPointerLeft();

        MouseEnter += (sender, args) => Expand();
        MouseLeave += (sender, args) => ScheduleCollapse();
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE keeps the game focused.
            return parameters;
        }
    }

    public void ShowOnScreen(Screen screen)
    {
        var bounds = screen.Bounds;
        screenRight = bounds.Right - EdgeMargin;
        screenTop = bounds.Top + EdgeMargin;

        var width = Visible ? ClientSize.Width : CollapsedWidth;
        if (!Visible)
        {
            targetWidth = CollapsedWidth;
            resizeTimer.Stop();
            collapseTimer.Stop();
        }

        SetOverlayBounds(width);
        UpdateButtonLayout(width);
        if (!Visible) Show();
        BringToFront();
    }

    private Button MakeButton(string text, Color color, Action click)
    {
        var button = new Button
        {
            AccessibleName = text,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance =
            {
                BorderSize = 0,
                MouseOverBackColor = ControlPaint.Light(color),
                MouseDownBackColor = ControlPaint.Dark(color)
            },
            BackColor = color,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5F),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand,
            TabStop = false,
            Tag = text
        };
        button.Click += (sender, args) => click();
        button.MouseEnter += (sender, args) => Expand();
        button.MouseLeave += (sender, args) => ScheduleCollapse();
        return button;
    }

    private void Expand()
    {
        collapseTimer.Stop();
        targetWidth = ExpandedWidth;
        if (!resizeTimer.Enabled) resizeTimer.Start();
    }

    private void ScheduleCollapse()
    {
        collapseTimer.Stop();
        collapseTimer.Start();
    }

    private void CollapseIfPointerLeft()
    {
        if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
        {
            collapseTimer.Stop();
            return;
        }

        collapseTimer.Stop();
        targetWidth = CollapsedWidth;
        if (!resizeTimer.Enabled) resizeTimer.Start();
    }

    private void AnimateWidth()
    {
        var width = ClientSize.Width;
        if (width == targetWidth)
        {
            resizeTimer.Stop();
            return;
        }

        var step = targetWidth > width ? 22 : -22;
        var nextWidth = width + step;
        if ((step > 0 && nextWidth > targetWidth) || (step < 0 && nextWidth < targetWidth))
            nextWidth = targetWidth;

        SetOverlayBounds(nextWidth);
        UpdateButtonLayout(nextWidth);
    }

    private void SetOverlayBounds(int width)
    {
        SetBounds(screenRight - width, screenTop, width, OverlayHeight);
    }

    private void UpdateButtonLayout(int width)
    {
        var buttonWidth = width - 4;
        var expanded = width > CollapsedWidth;
        LayoutButton(closeButton, 3, buttonWidth, expanded);
        LayoutButton(restartButton, 40, buttonWidth, expanded);
        LayoutButton(cleanRestartButton, 77, buttonWidth, expanded);
        LayoutButton(returnButton, 114, buttonWidth, expanded);
    }

    private static void LayoutButton(Button button, int top, int width, bool expanded)
    {
        button.SetBounds(2, top, width, 34);
        button.Text = expanded ? (string)button.Tag : string.Empty;
    }
}
}