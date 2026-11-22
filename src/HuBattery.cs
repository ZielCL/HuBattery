using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool isFirst;
        using (var instance = new Mutex(true, @"Local\BateriaMouse.1532.00C5", out isFirst))
        {
            if (!isFirst)
                return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var tray = new BatteryTray())
                Application.Run(tray);
        }
    }
}

internal sealed class BatteryTray : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupName = "HuBattery";
    private static readonly string StartupCommand = "\"" + Application.ExecutablePath + "\"";

    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private readonly ToolStripMenuItem status;
    private readonly ToolStripMenuItem refresh;
    private readonly ToolStripMenuItem startup;
    private readonly ToolStripMenuItem miceMenu;
    private readonly System.Windows.Forms.Timer timer;
    private readonly System.Windows.Forms.Timer placementTimer;
    private readonly TaskbarIndicator indicator;
    private Icon icon;
    private int? displayedPercent;
    private MouseReading[] readings = new MouseReading[0];
    private string preferredId;
    private bool busy;
    private bool closing;

    internal BatteryTray()
    {
        status = new ToolStripMenuItem { Enabled = false };
        refresh = new ToolStripMenuItem("Actualizar ahora", null, (sender, e) => Refresh());
        startup = new ToolStripMenuItem("Iniciar con Windows", null, ToggleStartup);
        miceMenu = new ToolStripMenuItem("Elegir mouse");
        menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] {
            status, miceMenu, refresh, startup, new ToolStripSeparator(),
            new ToolStripMenuItem("Salir", null, (sender, e) => ExitThread())
        });
        menu.Opening += (sender, e) =>
        {
            UpdateStartupMenu();
            UpdateMiceMenu();
        };
        tray = new NotifyIcon { ContextMenuStrip = menu };
        tray.MouseClick += (sender, e) =>
        {
            if (e.Button == MouseButtons.Left)
                Refresh();
        };

        indicator = new TaskbarIndicator(menu, Refresh);
        Display(null, "Consultando batería…");
        PlaceIndicator();
        placementTimer = new System.Windows.Forms.Timer { Interval = 500 };
        placementTimer.Tick += (sender, e) => PlaceIndicator();
        placementTimer.Start();
        timer = new System.Windows.Forms.Timer { Interval = 1 };
        timer.Tick += (sender, e) =>
        {
            timer.Interval = 30000;
            Refresh();
        };
        timer.Start();
    }

    private async void Refresh()
    {
        if (busy || closing)
            return;

        busy = true;
        refresh.Enabled = false;
        string message = "Sin lectura · Mueve o conecta el mouse";
        try
        {
            readings = await Task.Run(() => MouseBattery.Read());
        }
        catch (System.ComponentModel.Win32Exception)
        {
            readings = new MouseReading[0];
            message = "No se pudo consultar el dispositivo";
        }
        finally
        {
            busy = false;
        }

        if (closing)
            return;

        Display(SelectedMouse(), message);
        refresh.Enabled = true;
    }

    private MouseReading SelectedMouse()
    {
        return readings.FirstOrDefault(reading => reading.Id == preferredId) ?? readings.FirstOrDefault();
    }

    private void UpdateMiceMenu()
    {
        while (miceMenu.DropDownItems.Count > 0)
            miceMenu.DropDownItems[0].Dispose();
        miceMenu.Visible = readings.Length > 1;
        MouseReading selected = SelectedMouse();
        foreach (MouseReading reading in readings)
        {
            var item = new ToolStripMenuItem(reading.Name + " · " + reading.Connection + " · " + reading.Percent + "%")
            {
                Checked = reading == selected
            };
            item.Click += (sender, e) =>
            {
                preferredId = reading.Id;
                Display(SelectedMouse(), "Sin lectura");
            };
            miceMenu.DropDownItems.Add(item);
        }
    }

    private void Display(MouseReading reading, string message)
    {
        int? percent = reading == null ? (int?)null : reading.Percent;
        string label = reading == null ? message : reading.Name + " · " + reading.Percent + "%";
        status.Text = label;
        tray.Text = label.Length > 63 ? label.Substring(0, 60) + "…" : label;
        indicator.SetLevel(percent, tray.Text);

        if (icon != null && displayedPercent == percent)
            return;

        Icon previous = icon;
        icon = MakeIcon(percent);
        displayedPercent = percent;
        tray.Icon = icon;
        if (previous != null)
            previous.Dispose();
    }

    private void PlaceIndicator()
    {
        tray.Visible = !indicator.UpdatePlacement();
    }

    private void UpdateStartupMenu()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                startup.Checked = key != null && string.Equals(key.GetValue(StartupName) as string,
                    StartupCommand, StringComparison.OrdinalIgnoreCase);
        }
        catch (SecurityException)
        {
            startup.Enabled = false;
        }
        catch (UnauthorizedAccessException)
        {
            startup.Enabled = false;
        }
    }

    private void ToggleStartup(object sender, EventArgs e)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (startup.Checked)
                    key.DeleteValue(StartupName, false);
                else
                    key.SetValue(StartupName, StartupCommand);
            }
            UpdateStartupMenu();
        }
        catch (Exception error)
        {
            if (!(error is UnauthorizedAccessException || error is SecurityException || error is System.IO.IOException))
                throw;

            MessageBox.Show("Windows no permitió guardar la opción de inicio.", "HuBattery",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Icon MakeIcon(int? percent)
    {
        Color background = Color.FromArgb(23, 49, 61);
        if (percent <= 10)
            background = Color.FromArgb(149, 43, 40);
        else if (percent <= 20)
            background = Color.FromArgb(122, 72, 17);

        string number = percent.HasValue ? percent.Value.ToString() : "–";
        using (var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var badge = new SolidBrush(background))
        using (var outline = new Pen(Color.White, 1.5f))
        using (var font = new Font("Segoe UI", number.Length == 3 ? 14f : 18f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var format = new StringFormat(StringFormat.GenericTypographic))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.FillRectangle(badge, 1.5f, 4.5f, 29, 23);
            graphics.DrawRectangle(outline, 1.5f, 4.5f, 29, 23);
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            graphics.DrawString(number, font, Brushes.White, new RectangleF(1, 4, 30, 23), format);

            IntPtr handle = bitmap.GetHicon();
            try
            {
                using (Icon borrowed = Icon.FromHandle(handle))
                    return (Icon)borrowed.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
    }

    protected override void ExitThreadCore()
    {
        closing = true;
        placementTimer.Stop();
        indicator.Hide();
        tray.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closing = true;
            timer.Dispose();
            placementTimer.Dispose();
            indicator.Dispose();
            tray.Dispose();
            menu.Dispose();
            icon.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
