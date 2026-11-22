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
    private static void Main(string[] args)
    {
        if (PortableUpdate.RunHelper(args))
            return;

        bool isFirst;
        using (var instance = new Mutex(true, @"Local\BateriaMouse.1532.00C5", out isFirst))
        {
            if (!isFirst)
                return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--clean-update")
                Task.Run(() => PortableUpdate.Cleanup(args[1]));
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
    private readonly ToolStripMenuItem headphoneStatus;
    private readonly ToolStripMenuItem refresh;
    private readonly ToolStripMenuItem checkUpdate;
    private readonly ToolStripMenuItem installUpdate;
    private readonly ToolStripMenuItem startup;
    private readonly ToolStripMenuItem miceMenu;
    private readonly ToolStripMenuItem headphonesMenu;
    private readonly System.Windows.Forms.Timer timer;
    private readonly System.Windows.Forms.Timer placementTimer;
    private readonly TaskbarIndicator indicator;
    private Icon icon;
    private int? displayedPercent;
    private BatteryReading[] readings = new BatteryReading[0];
    private string preferredMouse;
    private string preferredHeadphones;
    private bool busy;
    private bool closing;
    private bool updateBusy;
    private UpdateCandidate availableUpdate;

    internal BatteryTray()
    {
        status = new ToolStripMenuItem { Enabled = false };
        headphoneStatus = new ToolStripMenuItem { Enabled = false };
        refresh = new ToolStripMenuItem("Actualizar batería", null, (sender, e) => Refresh());
        checkUpdate = new ToolStripMenuItem("Buscar actualizaciones", null, (sender, e) => CheckUpdates(true));
        installUpdate = new ToolStripMenuItem { Visible = false };
        installUpdate.Click += (sender, e) => ConfirmUpdate();
        startup = new ToolStripMenuItem("Iniciar con Windows", null, ToggleStartup);
        miceMenu = new ToolStripMenuItem("Elegir mouse");
        headphonesMenu = new ToolStripMenuItem("Elegir audífonos");
        menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] {
            status, headphoneStatus, miceMenu, headphonesMenu, refresh, startup, new ToolStripSeparator(),
            checkUpdate, installUpdate, new ToolStripSeparator(),
            new ToolStripMenuItem("Salir", null, (sender, e) => ExitThread())
        });
        menu.Opening += (sender, e) =>
        {
            UpdateStartupMenu();
            PopulateDeviceMenu(miceMenu, PeripheralKind.Mouse, preferredMouse);
            PopulateDeviceMenu(headphonesMenu, PeripheralKind.Headphones, preferredHeadphones);
        };
        tray = new NotifyIcon { ContextMenuStrip = menu };
        tray.MouseClick += (sender, e) =>
        {
            if (e.Button == MouseButtons.Left)
                Refresh();
        };

        indicator = new TaskbarIndicator(menu, Refresh);
        Display("Consultando batería…");
        PlaceIndicator();
        placementTimer = new System.Windows.Forms.Timer { Interval = 500 };
        placementTimer.Tick += (sender, e) => PlaceIndicator();
        placementTimer.Start();
        timer = new System.Windows.Forms.Timer { Interval = 1 };
        timer.Tick += (sender, e) =>
        {
            if (timer.Interval == 1)
            {
                timer.Interval = 30000;
                if (BootUpdateState.ShouldCheck())
                    CheckUpdates(false);
            }
            Refresh();
        };
        timer.Start();
    }

    private async void CheckUpdates(bool manual)
    {
        if (updateBusy || closing)
            return;

        SetUpdateBusy(true);
        UpdateCandidate result = null;
        bool failed = false;
        try
        {
            result = await Task.Run(() => ReleaseUpdater.Check());
        }
        catch (Exception)
        {
            failed = true;
        }
        if (closing)
            return;

        if (!failed)
            availableUpdate = result;
        installUpdate.Visible = availableUpdate != null;
        if (availableUpdate != null)
            installUpdate.Text = "Instalar actualización " + availableUpdate.Tag;
        SetUpdateBusy(false);
        if (!manual)
            return;

        if (failed)
            MessageBox.Show("No se pudo consultar GitHub. Revisa tu conexión y vuelve a intentarlo.",
                "HuBattery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        else if (result == null)
            MessageBox.Show("HuBattery v" + ReleaseUpdater.CurrentVersion + " está al día.",
                "HuBattery", MessageBoxButtons.OK, MessageBoxIcon.Information);
        else
            ConfirmUpdate();
    }

    private async void ConfirmUpdate()
    {
        if (updateBusy || closing || availableUpdate == null)
            return;

        UpdateCandidate selected = availableUpdate;
        if (MessageBox.Show("HuBattery " + selected.Tag + " está disponible. ¿Quieres descargarla e instalarla?\n\n" +
            "HuBattery se reiniciará al terminar.", "HuBattery", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        SetUpdateBusy(true);
        try
        {
            await Task.Run(() => PortableUpdate.Prepare(selected, Application.ExecutablePath));
        }
        catch (Exception error)
        {
            if (!closing)
            {
                SetUpdateBusy(false);
                MessageBox.Show("No se pudo preparar la actualización.\n\n" + error.Message,
                    "HuBattery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return;
        }
        if (!closing)
            ExitThread();
    }

    private void SetUpdateBusy(bool value)
    {
        updateBusy = value;
        checkUpdate.Enabled = !value;
        installUpdate.Enabled = !value;
    }

    private async void Refresh()
    {
        if (busy || closing)
            return;

        busy = true;
        refresh.Enabled = false;
        string message = "Sin lectura · Enciende o conecta el dispositivo";
        try
        {
            readings = await Task.Run(() => PeripheralBattery.Read());
        }
        catch (System.ComponentModel.Win32Exception)
        {
            readings = new BatteryReading[0];
            message = "No se pudo consultar el dispositivo";
        }
        finally
        {
            busy = false;
        }

        if (closing)
            return;

        Display(message);
        refresh.Enabled = true;
    }

    internal static BatteryReading SelectReading(BatteryReading[] devices, PeripheralKind kind, string preferred)
    {
        return devices.FirstOrDefault(reading => reading.Kind == kind &&
            string.Equals(reading.Id, preferred, StringComparison.OrdinalIgnoreCase)) ??
            devices.FirstOrDefault(reading => reading.Kind == kind);
    }

    private void PopulateDeviceMenu(ToolStripMenuItem deviceMenu, PeripheralKind kind, string preferred)
    {
        while (deviceMenu.DropDownItems.Count > 0)
            deviceMenu.DropDownItems[0].Dispose();
        BatteryReading[] devices = readings.Where(reading => reading.Kind == kind).ToArray();
        deviceMenu.Visible = devices.Length > 1;
        BatteryReading selected = SelectReading(devices, kind, preferred);
        foreach (BatteryReading reading in devices)
        {
            var item = new ToolStripMenuItem(reading.Name + " · " + reading.Connection + " · " + reading.Percent + "%")
            {
                Checked = reading == selected
            };
            item.Click += (sender, e) =>
            {
                if (kind == PeripheralKind.Mouse)
                    preferredMouse = reading.Id;
                else
                    preferredHeadphones = reading.Id;
                Display("Sin lectura");
            };
            deviceMenu.DropDownItems.Add(item);
        }
    }

    private void Display(string message)
    {
        BatteryReading mouse = SelectReading(readings, PeripheralKind.Mouse, preferredMouse);
        BatteryReading headphones = SelectReading(readings, PeripheralKind.Headphones, preferredHeadphones);
        string mouseLabel = mouse == null ? null : Describe(mouse);
        string headphonesLabel = headphones == null ? null : Describe(headphones);
        bool both = mouse != null && headphones != null;
        status.Text = mouseLabel ?? headphonesLabel ?? message;
        headphoneStatus.Visible = both;
        headphoneStatus.Text = both ? headphonesLabel : "";

        string trayLabel = both ? "Mouse " + mouse.Percent + "% · Audífonos " + headphones.Percent + "%" : status.Text;
        tray.Text = trayLabel.Length > 63 ? trayLabel.Substring(0, 60) + "…" : trayLabel;
        string indicatorLabel = both ? mouseLabel + Environment.NewLine + headphonesLabel : status.Text;
        indicator.SetLevels(mouse == null ? (int?)null : mouse.Percent,
            headphones == null ? (int?)null : headphones.Percent, indicatorLabel);

        BatteryReading primary = mouse ?? headphones;
        int? percent = primary == null ? (int?)null : primary.Percent;
        if (icon != null && displayedPercent == percent)
            return;

        Icon previous = icon;
        icon = MakeIcon(percent);
        displayedPercent = percent;
        tray.Icon = icon;
        if (previous != null)
            previous.Dispose();
    }

    private static string Describe(BatteryReading reading)
    {
        return reading.Name + " · " + reading.Percent + "%";
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
