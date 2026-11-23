using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class TaskbarIndicator : Form
{
    private const int PreviousWindow = 3;
    private const int ExtendedFrameBounds = 9;
    private const int KeepPositionAndFocus = 0x0013;
    private const int ShowWindow = 0x0040;
    internal const int SegmentWidth = 64;
    internal const int IndicatorHeight = 24;
    private static readonly IntPtr TopmostWindow = new IntPtr(-1);
    private readonly ToolTip tooltip = new ToolTip();
    private string mouseLevel = "—";
    private string headphoneLevel;
    private string controllerLevel;
    private string paintedMouseLevel;
    private string paintedHeadphoneLevel;
    private string paintedControllerLevel;
    private Rectangle paintedBounds;
    private Color paintedInk;
    private int paintedDpi;

    internal TaskbarIndicator(ContextMenuStrip menu, Action refresh)
    {
        Text = "HuBattery";
        AccessibleName = Text;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        ContextMenuStrip = menu;
        MouseClick += (sender, e) =>
        {
            if (e.Button == MouseButtons.Left)
                refresh();
        };
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x00080000 | 0x08000000 | 0x00000080; // Layered, no activation, tool window.
            return parameters;
        }
    }

    internal void SetLevels(string mouse, string headphones, string controller, string status)
    {
        mouseLevel = mouse ?? (headphones == null && controller == null ? "—" : null);
        headphoneLevel = headphones;
        controllerLevel = controller;
        AccessibleName = status;
        tooltip.SetToolTip(this, status);
        if (Visible)
            UpdatePlacement();
    }

    internal int LogicalWidth
    {
        get { return SegmentWidth * ((mouseLevel == null ? 0 : 1) + (headphoneLevel == null ? 0 : 1) +
            (controllerLevel == null ? 0 : 1)); }
    }

    // Return true when this taskbar supports the wide indicator, including while auto-hidden.
    internal bool UpdatePlacement()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        IntPtr notificationArea = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        Rectangle bar, tray;
        if (!TryGetBounds(taskbar, out bar) || !TryGetBounds(notificationArea, out tray) ||
            bar.Width <= bar.Height)
        {
            Hide();
            return false;
        }

        int dpi = GetDpiForWindow(taskbar);
        if (dpi == 0)
            dpi = 96;
        int width = Scale(LogicalWidth, dpi);
        int height = Scale(IndicatorHeight, dpi);
        var bounds = new Rectangle(tray.Left - width - Scale(4, dpi),
            bar.Top + (bar.Height - height) / 2, width, height);
        if (!bar.Contains(bounds))
        {
            Hide();
            return false;
        }

        Screen monitor = Screen.FromHandle(taskbar);
        if (!IsWindowVisible(taskbar) || !monitor.Bounds.Contains(bounds) || IsFullScreen(taskbar, monitor.Bounds))
        {
            Hide();
            return true;
        }

        Color ink = TaskbarInk();
        if (bounds != paintedBounds || dpi != paintedDpi || mouseLevel != paintedMouseLevel ||
            headphoneLevel != paintedHeadphoneLevel || controllerLevel != paintedControllerLevel || ink != paintedInk)
        {
            if (!PaintIndicator(bounds, dpi, ink))
            {
                Hide();
                return false;
            }

            paintedBounds = bounds;
            paintedDpi = dpi;
            paintedMouseLevel = mouseLevel;
            paintedHeadphoneLevel = headphoneLevel;
            paintedControllerLevel = controllerLevel;
            paintedInk = ink;
        }

        if (!Visible)
            Show();

        // Explorer may raise the taskbar after a click, even while this window is still visible.
        if (!IsWindowVisible(Handle) || !IsAboveTaskbar(taskbar))
            SetWindowPos(Handle, TopmostWindow, 0, 0, 0, 0, KeepPositionAndFocus | ShowWindow);

        return true;
    }

    private bool IsAboveTaskbar(IntPtr taskbar)
    {
        for (IntPtr window = GetWindow(taskbar, PreviousWindow); window != IntPtr.Zero;
            window = GetWindow(window, PreviousWindow))
        {
            if (window == Handle)
                return true;
        }
        return false;
    }

    private static bool IsFullScreen(IntPtr taskbar, Rectangle monitor)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        uint foregroundProcess, shellProcess;
        GetWindowThreadProcessId(foreground, out foregroundProcess);
        GetWindowThreadProcessId(taskbar, out shellProcess);
        if (foregroundProcess == shellProcess)
            return false;

        NativeRect rect;
        if (DwmGetWindowAttribute(foreground, ExtendedFrameBounds, out rect, Marshal.SizeOf(typeof(NativeRect))) == 0)
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom).Contains(monitor);

        Rectangle window;
        return TryGetBounds(foreground, out window) && window.Contains(monitor);
    }

    private static Color TaskbarInk()
    {
        if (SystemInformation.HighContrast)
            return SystemColors.WindowText;

        const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        return Equals(Registry.GetValue(key, "SystemUsesLightTheme", 0), 1)
            ? Color.FromArgb(30, 30, 30)
            : Color.White;
    }

    private bool PaintIndicator(Rectangle bounds, int dpi, Color ink)
    {
        using (var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb))
        {
            DrawIndicator(bitmap, dpi, ink);
            return ApplyBitmap(bitmap, bounds.Location);
        }
    }

    internal void DrawIndicator(Bitmap bitmap, int dpi, Color ink)
    {
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var outline = new Pen(ink, 1.5f))
        using (var brush = new SolidBrush(ink))
        using (var font = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var label = new StringFormat(StringFormat.GenericTypographic))
        {
            // Fully transparent pixels pass clicks through a layered window to Explorer.
            graphics.Clear(Color.FromArgb(1, 0, 0, 0));
            graphics.ScaleTransform(dpi / 96f, dpi / 96f);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            outline.StartCap = outline.EndCap = LineCap.Round;
            label.LineAlignment = StringAlignment.Center;
            if (mouseLevel != null)
            {
                DrawMouse(graphics, outline);
                graphics.DrawString(mouseLevel, font, brush, new RectangleF(24, 0, 40, IndicatorHeight), label);
                graphics.TranslateTransform(SegmentWidth, 0);
            }
            if (headphoneLevel != null)
            {
                DrawHeadphones(graphics, outline, brush);
                graphics.DrawString(headphoneLevel, font, brush, new RectangleF(24, 0, 40, IndicatorHeight), label);
                graphics.TranslateTransform(SegmentWidth, 0);
            }
            if (controllerLevel != null)
            {
                DrawController(graphics, outline, brush);
                graphics.DrawString(controllerLevel, font, brush, new RectangleF(24, 0, 40, IndicatorHeight), label);
            }
        }
    }

    private static void DrawController(Graphics graphics, Pen outline, Brush ink)
    {
        using (var shape = new GraphicsPath())
        {
            shape.AddBezier(7, 6, 4, 6, 3, 10, 2, 15);
            shape.AddBezier(2, 15, 1, 19, 4, 20, 7, 17);
            shape.AddLine(7, 17, 9, 15);
            shape.AddLine(9, 15, 15, 15);
            shape.AddLine(15, 15, 17, 17);
            shape.AddBezier(17, 17, 20, 20, 23, 19, 22, 15);
            shape.AddBezier(22, 15, 21, 10, 20, 6, 17, 6);
            shape.CloseFigure();
            graphics.DrawPath(outline, shape);
        }
        graphics.DrawLine(outline, 6, 11, 10, 11);
        graphics.DrawLine(outline, 8, 9, 8, 13);
        graphics.FillEllipse(ink, 14, 10, 2, 2);
        graphics.FillEllipse(ink, 17, 9, 2, 2);
    }

    private static void DrawMouse(Graphics graphics, Pen outline)
    {
        using (var mouse = new GraphicsPath())
        {
            mouse.AddArc(4, 2.5f, 8, 8, 180, 90);
            mouse.AddArc(10, 2.5f, 8, 8, 270, 90);
            mouse.AddArc(10, 13.5f, 8, 8, 0, 90);
            mouse.AddArc(4, 13.5f, 8, 8, 90, 90);
            mouse.CloseFigure();
            graphics.DrawPath(outline, mouse);
        }
        graphics.DrawLine(outline, 11, 3, 11, 9);
    }

    private static void DrawHeadphones(Graphics graphics, Pen outline, Brush ink)
    {
        graphics.DrawArc(outline, 4, 4, 16, 16, 180, 180);
        graphics.DrawLine(outline, 4, 12, 4, 17);
        graphics.DrawLine(outline, 20, 12, 20, 17);
        graphics.FillRectangle(ink, 4.5f, 12.5f, 3.5f, 7);
        graphics.FillRectangle(ink, 16, 12.5f, 3.5f, 7);
    }

    private bool ApplyBitmap(Bitmap bitmap, Point position)
    {
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
            return false;
        try
        {
            IntPtr image = bitmap.GetHbitmap(Color.FromArgb(0));
            try
            {
                IntPtr previous = SelectObject(dc, image);
                if (previous == IntPtr.Zero)
                    return false;
                try
                {
                    Point source = Point.Empty;
                    Size size = bitmap.Size;
                    var blend = new Blend { Opacity = 255, AlphaFormat = 1 };
                    return UpdateLayeredWindow(Handle, IntPtr.Zero, ref position, ref size,
                        dc, ref source, 0, ref blend, 2);
                }
                finally { SelectObject(dc, previous); }
            }
            finally { DeleteObject(image); }
        }
        finally { DeleteDC(dc); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            tooltip.Dispose();
        base.Dispose(disposing);
    }

    private static int Scale(int pixels, int dpi)
    {
        return (pixels * dpi + 48) / 96;
    }

    private static bool TryGetBounds(IntPtr window, out Rectangle bounds)
    {
        NativeRect rect;
        if (window != IntPtr.Zero && GetWindowRect(window, out rect))
        {
            bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return bounds.Width > 0 && bounds.Height > 0;
        }
        bounds = Rectangle.Empty;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Blend { public byte Operation, Flags, Opacity, AlphaFormat; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string windowClass, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string windowClass, string title);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int relationship);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect rect, int size);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, int flags);
    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref Point location,
        ref Size size, IntPtr source, ref Point sourceLocation, int colorKey, ref Blend blend, int flags);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr resource);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr resource);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);
}
