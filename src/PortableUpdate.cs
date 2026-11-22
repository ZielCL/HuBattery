using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class PortableUpdate
{
    private const string Helper = "UpdateHelper.exe";
    private const string Payload = "HuBattery.exe";
    private static readonly string StageRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HuBattery", "Updates");

    internal static void Prepare(UpdateCandidate update, string target)
    {
        string stage = NewStage();
        try
        {
            ReleaseUpdater.Download(update, Path.Combine(stage, Payload));
            string helper = Path.Combine(stage, Helper);
            File.Copy(target, helper);
            string originalHash = ReleaseUpdater.HashFile(helper);
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(target));
            int parentId;
            using (Process parent = Process.GetCurrentProcess())
                parentId = parent.Id;
            using (Process process = Start(helper, "--apply-update " + parentId + " " + encoded +
                " " + originalHash + " " + update.Hash))
            {
                if (process == null)
                    throw new IOException("No se pudo iniciar la instalación.");
            }
        }
        catch
        {
            CleanStage(stage, 1);
            throw;
        }
    }

    internal static bool RunHelper(string[] args)
    {
        if (args.Length == 0 || args[0] != "--apply-update")
            return false;

        string helper = Application.ExecutablePath;
        string stage = Path.GetDirectoryName(helper);
        Guid id;
        int parentId;
        if (args.Length != 5 || Path.GetFileName(helper) != Helper || !IsStage(stage, out id) ||
            !int.TryParse(args[1], out parentId) || parentId <= 0 ||
            !ReleaseUpdater.IsHash(args[3]) || !ReleaseUpdater.IsHash(args[4]))
            return true;

        try
        {
            string target = Encoding.Unicode.GetString(Convert.FromBase64String(args[2]));
            if (!string.Equals(target, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(StageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La ruta de HuBattery no es válida.");

            if (!ReleaseUpdater.MatchesHash(helper, args[3]))
                throw new InvalidDataException("El asistente de actualización no es válido.");

            WaitForExit(parentId, target);
            ReplaceAndRestart(Path.Combine(stage, Payload), target, id, args[3], args[4]);
        }
        catch (Exception error)
        {
            MessageBox.Show("No se pudo instalar la actualización.\n\n" + error.Message,
                "HuBattery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            CleanStage(stage, 1);
        }
        return true;
    }

    internal static void Cleanup(string id)
    {
        Guid parsed;
        if (Guid.TryParseExact(id, "N", out parsed))
            CleanStage(Path.Combine(StageRoot, parsed.ToString("N")), 20);
    }

    private static string NewStage()
    {
        string path = Path.Combine(StageRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool IsStage(string path, out Guid id)
    {
        return Guid.TryParseExact(Path.GetFileName(path), "N", out id) &&
            string.Equals(Path.GetDirectoryName(path), StageRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void CleanStage(string stage, int attempts)
    {
        Guid id;
        if (!IsStage(stage, out id))
            return;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                File.Delete(Path.Combine(stage, Payload));
                File.Delete(Path.Combine(stage, Helper));
                Directory.Delete(stage);
                return;
            }
            catch (DirectoryNotFoundException) { return; }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            if (attempt + 1 < attempts)
                Thread.Sleep(250);
        }
    }

    private static void WaitForExit(int parentId, string target)
    {
        Process parent;
        try { parent = Process.GetProcessById(parentId); }
        catch (ArgumentException) { return; }
        using (parent)
        {
            if (parent.HasExited)
                return;
            string runningPath;
            try { runningPath = parent.MainModule.FileName; }
            catch (InvalidOperationException)
            {
                if (parent.HasExited)
                    return;
                throw;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                if (parent.HasExited)
                    return;
                throw;
            }
            if (string.Equals(runningPath, target, StringComparison.OrdinalIgnoreCase) && !parent.WaitForExit(15000))
                throw new IOException("HuBattery sigue abierto. Cierra la app y vuelve a intentarlo.");
        }
    }

    internal static void ReplaceAndRestart(string payload, string target, Guid id, string originalHash, string newHash)
    {
        string pending = target + "." + id.ToString("N") + ".pending";
        string backup = target + "." + id.ToString("N") + ".backup";
        string cleanup = "--clean-update " + id.ToString("N");
        bool replaced = false;
        try
        {
            if (!ReleaseUpdater.MatchesHash(target, originalHash) ||
                !ReleaseUpdater.MatchesHash(payload, newHash) ||
                AssemblyName.GetAssemblyName(payload).Name != "HuBattery")
                throw new InvalidDataException("El ejecutable cambió durante la actualización.");

            File.Copy(payload, pending);
            File.Replace(pending, target, backup);
            replaced = true;
            using (Process updated = Start(target, cleanup))
            {
                if (updated == null || updated.WaitForExit(700))
                    throw new IOException("La versión nueva se cerró al iniciar.");
            }
        }
        catch
        {
            if (replaced)
                File.Replace(backup, target, null);
            if (File.Exists(target) && ReleaseUpdater.MatchesHash(target, originalHash))
            {
                using (Start(target, cleanup)) { }
            }
            throw;
        }
        finally
        {
            TryDelete(pending);
        }
        TryDelete(backup);
    }

    private static Process Start(string path, string args)
    {
        return Process.Start(new ProcessStartInfo(path, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path)
        });
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
