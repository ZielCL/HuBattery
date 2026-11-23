using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

internal static class LogitechProtocolTests
{
    [STAThread]
    private static int Main()
    {
        try
        {
            TestReceiverMatching();
            TestUnifiedPercentage();
            TestLegacyPercentage();
            TestNonMouseSkipped();
            TestCoarseLevelSkipped();
            TestInvalidPercentageSkipped();
            TestLegacyRegister();
            TestVendorBatteryReports();
            TestNewMouseCatalog();
            TestHeadsetReports();
            TestBluetoothAudioClassification();
            TestStandardBatteryPercent();
            TestControllerBattery();
            TestIndependentSelectionAndRendering();
            TestWindowsTransport();
            TestBootUpdatePolicy();
            TestReleaseSelection();
            TestUpdateHash();
            Console.WriteLine("HuBattery: 18 pruebas aprobadas.");
            return 0;
        }
        catch (InvalidOperationException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void TestBootUpdatePolicy()
    {
        Guid same = Guid.NewGuid();
        Guid next = Guid.NewGuid();
        Verify(!BootUpdateState.ShouldCheck(null, same) && !BootUpdateState.ShouldCheck("inválido", same) &&
            !BootUpdateState.ShouldCheck(same.ToString("D").ToUpperInvariant(), same) &&
            !BootUpdateState.ShouldCheck(same.ToString("D"), Guid.Empty) &&
            BootUpdateState.ShouldCheck(same.ToString("D"), next), "La consulta automática no respetó el arranque.");
    }

    private static void TestReleaseSelection()
    {
        GitHubRelease draft = MakeRelease("v2.0.0", false);
        draft.draft = true;
        GitHubRelease wrongSource = MakeRelease("v3.0.0", false);
        wrongSource.assets[0].browser_download_url = "https://example.com/HuBattery.exe";
        GitHubRelease badDigest = MakeRelease("v4.0.0", false);
        badDigest.assets[0].digest = "sha256:bad";
        var releases = new[] { MakeRelease("v1.2.0-beta.2", true), MakeRelease("v1.2.0-beta.10", true),
            MakeRelease("v1.1.5", false), draft, wrongSource, badDigest };
        string json = new JavaScriptSerializer().Serialize(releases);
        UpdateCandidate beta = ReleaseUpdater.SelectLatest(json, "1.2.0-beta.1");
        UpdateCandidate stable = ReleaseUpdater.SelectLatest(json, "1.1.0");
        Verify(beta != null && beta.Tag == "v1.2.0-beta.10" && stable != null && stable.Tag == "v1.1.5" &&
            ReleaseUpdater.SelectLatest(json, "1.2.0-beta.10") == null &&
            ReleaseUpdater.SelectLatest(json, "1.1.5") == null, "Se eligió una versión incorrecta.");
        UpdateCandidate graduation = ReleaseUpdater.SelectLatest(new[] { MakeRelease("v1.2.0", false) }, "1.2.0-beta.10");
        Verify(graduation != null && graduation.Tag == "v1.2.0", "Una beta no pudo actualizar a la versión estable.");
    }

    private static GitHubRelease MakeRelease(string tag, bool beta)
    {
        var asset = new ReleaseAsset { name = "HuBattery.exe", state = "uploaded", size = 1024,
            digest = "sha256:" + new string('a', 64),
            browser_download_url = "https://github.com/ZielCL/HuBattery/releases/download/" + tag + "/HuBattery.exe" };
        return new GitHubRelease { tag_name = tag, prerelease = beta, assets = new[] { asset } };
    }

    private static void TestUpdateHash()
    {
        string path = Path.GetTempFileName();
        try
        {
            const string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));
            Verify(ReleaseUpdater.MatchesHash(path, expected), "No se verificó el SHA-256 esperado.");
            File.AppendAllText(path, "d");
            Verify(!ReleaseUpdater.MatchesHash(path, expected), "Se aceptó un archivo alterado.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void TestReceiverMatching()
    {
        Verify(LogitechBattery.ReceiverName(@"\\?\hid#vid_046d&pid_c548&mi_02&col02#instance") == "Logi Bolt",
            "Bolt no reconocido.");
        Verify(LogitechBattery.ReceiverName(@"\\?\hid#vid_046d&pid_c52b&mi_02#instance") == "Unifying",
            "Unifying no reconocido.");
        Verify(LogitechBattery.ReceiverName(@"\\?\hid#vid_046d&pid_c532&mi_02&col02#instance") == "Unifying",
            "Variante de Unifying no reconocida.");
        Verify(LogitechBattery.ReceiverName(@"\\?\hid#vid_046d&pid_c548&mi_00&col02#instance") == null &&
            LogitechBattery.ReceiverName(@"\\?\hid#vid_046d&pid_c534&mi_02&col02#instance") == null,
            "Se aceptó una interfaz o un receptor fuera del catálogo.");
        Verify(LogitechBattery.ReceiverName(@"\\?\hid#vid_046d&pid_c539&mi_02&col02#instance") == "LIGHTSPEED" &&
            LogitechBattery.ReceiverName(@"\\?\hid#vid_046d&pid_c534&mi_01&col02#instance") == "Nano" &&
            LogitechBattery.ReceiverName(@"\\?\hid#vid_046d&pid_c232&mi_01&col02#instance") == null,
            "El catálogo de LIGHTSPEED o Nano no coincide.");
    }

    private static void TestUnifiedPercentage()
    {
        var replies = new ReceiverReplies(3, true, 0, false, 0);
        BatteryReading reading = Read(replies);
        Verify(reading != null && reading.Percent == 0 && reading.Name == ReceiverReplies.DeviceName &&
            reading.Id == "receiver:2" && reading.Connection == "Logi Bolt",
            "No se aceptó 0 % real o no se reconstruyó el nombre de varios fragmentos.");
    }

    private static void TestLegacyPercentage()
    {
        var replies = new ReceiverReplies(5, false, 0, true, 61);
        BatteryReading reading = Read(replies);
        Verify(reading != null && reading.Percent == 61, "El trackball no pudo usar Battery Status.");
    }

    private static void TestNonMouseSkipped()
    {
        var replies = new ReceiverReplies(0, true, 85, true, 85);
        Verify(Read(replies) == null && replies.BatteryQueries == 0, "Se consultó la batería de un teclado.");
    }

    private static void TestCoarseLevelSkipped()
    {
        var replies = new ReceiverReplies(3, false, 65, false, 0);
        Verify(Read(replies) == null, "Un nivel cualitativo se mostró como porcentaje.");
    }

    private static void TestInvalidPercentageSkipped()
    {
        var replies = new ReceiverReplies(3, true, 255, true, 0);
        Verify(Read(replies) == null, "Se aceptó un valor inválido o desconocido del dispositivo.");
    }

    private static void TestLegacyRegister()
    {
        byte pairedKind = 8;
        Func<byte[], byte[]> pairing = request =>
        {
            Verify(request[0] == 0x10 && request[1] == 0xFF && request[2] == 0x83 &&
                request[3] == 0xB5 && request[4] == 0x21, "Consulta de identificación antigua incorrecta.");
            var reply = new byte[20];
            reply[4] = 0x21;
            reply[11] = pairedKind;
            return reply;
        };
        int reads = 0;
        Func<byte[], byte[]> charge = request =>
        {
            reads++;
            Verify(request[0] == 0x10 && request[1] == 2 && request[2] == 0x81 &&
                request[3] == 0x0D, "Consulta de carga antigua incorrecta.");
            return new byte[] { 0x10, 2, 0x81, 0x0D, 78, 0, 0 };
        };
        BatteryReading reading = LogitechBattery.ReadLegacySlot(2, "receiver", "Nano", pairing, charge);
        Verify(reading != null && reading.Percent == 78 && reading.Connection == "Nano",
            "No se obtuvo el porcentaje HID++ 1.0 del trackball.");
        pairedKind = 1;
        Verify(LogitechBattery.ReadLegacySlot(2, "receiver", "Nano", pairing, charge) == null && reads == 1,
            "Se consultó la batería de un teclado en un receptor Nano.");
    }

    private static void TestVendorBatteryReports()
    {
        Verify(VendorMouseBattery.DecodeSteelSeries(new byte[] { 0, 0xD2, 0x84 }, true) == 15 &&
            VendorMouseBattery.DecodeSteelSeries(new byte[] { 0xD2, 0 }, true) == null &&
            VendorMouseBattery.DecodeSteelSeries(new byte[] { 0x40, 0xFF }, true) == null &&
            VendorMouseBattery.DecodeSteelSeries(new byte[] { 0xAA, 54 }, false) == 54,
            "Lectura SteelSeries inválida.");
        Verify(VendorMouseBattery.DecodeHyperX(new byte[] { 0x51, 2, 0 }) == 0 &&
            VendorMouseBattery.DecodeHyperX(new byte[] { 0x53, 1, 1 }) == null, "Lectura HyperX inválida.");
        Verify(VendorMouseBattery.DecodeSinowealth(new byte[] { 0, 0xA1, 0, 2, 2, 0, 0x83, 0, 64 }) == 64 &&
            VendorMouseBattery.DecodeSinowealth(new byte[] { 0xA4, 0, 2, 2, 0, 0x83, 0, 64 }) == null,
            "Lectura Sinowealth inválida.");
        Verify(VendorMouseBattery.DecodeCorsair(new byte[] { 0, 1, 2, 0, 0x26, 2 }) == 55 &&
            VendorMouseBattery.DecodeCorsair(new byte[] { 1, 2, 5, 0x26, 2 }) == null,
            "Lectura Corsair inválida.");
        var compx = new byte[] { 8, 4, 0, 0, 0, 0, 75, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFE };
        Verify(VendorMouseBattery.DecodeCompx(compx) == 75, "Lectura Compx inválida.");
        compx[16]++;
        Verify(VendorMouseBattery.DecodeCompx(compx) == null, "Se aceptó una suma de comprobación Compx inválida.");
    }

    private static void TestNewMouseCatalog()
    {
        var compx = new HidBattery.Caps { UsagePage = 0xFF02, Usage = 2, InputLength = 17, OutputLength = 17 };
        var feature = new HidBattery.Caps { UsagePage = 0xFFFF, Usage = 0, FeatureLength = 65 };
        var inputMouse = new HidBattery.Caps { UsagePage = 1, Usage = 2, InputLength = 17 };
        string[] receivers = { "3554&pid_f524", "260d&pid_1175", "260d&pid_1154", "33e4&pid_3854" };
        string[] cables = { "3554&pid_f526", "260d&pid_1193", "260d&pid_1196", "33e4&pid_4719", "33e4&pid_3619" };

        foreach (string receiver in receivers)
            VerifyModel(receiver, "USB 2,4 GHz", compx, feature);
        foreach (string cable in cables)
            VerifyModel(cable, "Cable USB", compx, inputMouse);
        VerifyModel("33e4&pid_3517", "USB 2,4 GHz", feature, compx);
        VerifyModel("33e4&pid_3508", "Cable USB", feature, inputMouse);

        string id;
        Verify(VendorMouseBattery.FindModel(@"\\?\hid#vid_260d&pid_0000&mi_01#sample", out id) == null,
            "Se aceptó un receptor Dareu fuera del catálogo.");
    }

    private static void VerifyModel(string identifier, string connection, HidBattery.Caps expected, HidBattery.Caps wrong)
    {
        string id;
        VendorMouseBattery.Model model = VendorMouseBattery.FindModel(
            @"\\?\hid#vid_" + identifier + "&mi_02&col03#sample", out id);
        Verify(model != null && model.Connection == connection && VendorMouseBattery.IsBatteryCollection(model, expected) &&
            !VendorMouseBattery.IsBatteryCollection(model, wrong), "Identificación HID incorrecta para " + identifier);
    }

    private static void TestHeadsetReports()
    {
        Verify(UsbHeadsetBattery.Decode(new byte[] { 0, 0xB0, 3, 78, 3 }, UsbHeadsetBattery.Protocol.Nova7) == 78 &&
            UsbHeadsetBattery.Decode(new byte[] { 0xB0, 2, 78, 3 }, UsbHeadsetBattery.Protocol.Nova7) == null &&
            UsbHeadsetBattery.Decode(new byte[] { 0xB0, 3, 3, 3 }, UsbHeadsetBattery.Protocol.Nova7Steps) == 75 &&
            UsbHeadsetBattery.Decode(new byte[] { 0xB0, 3, 0, 82, 0 }, UsbHeadsetBattery.Protocol.Nova5) == 82,
            "Los reportes Nova no se interpretaron correctamente.");
        Verify(UsbHeadsetBattery.Decode(new byte[] { 0x21, 0xBB, 0x0B, 86 }, UsbHeadsetBattery.Protocol.CloudAlpha) == 86 &&
            UsbHeadsetBattery.Decode(new byte[] { 0x21, 0xBB, 0x23, 1 }, UsbHeadsetBattery.Protocol.CloudAlpha) == null &&
            UsbHeadsetBattery.Decode(new byte[] { 0x51, 2, 67 }, UsbHeadsetBattery.Protocol.CloudAlpha2) == 67 &&
            UsbHeadsetBattery.Decode(new byte[] { 0x66, 0x0D, 0x0E, 0xD7, 48 }, UsbHeadsetBattery.Protocol.CloudIII) == 48 &&
            UsbHeadsetBattery.Decode(new byte[] { 0x66, 0x89, 0, 0, 48 }, UsbHeadsetBattery.Protocol.CloudIII) == null,
            "Los reportes HyperX no se interpretaron correctamente.");
        Verify(UsbHeadsetBattery.Decode(new byte[] { 0x64, 0, 0xDA, 0xB1, 1 }, UsbHeadsetBattery.Protocol.VoidElite) == 90 &&
            UsbHeadsetBattery.Decode(new byte[] { 0x64, 0, 90, 0xB1, 0 }, UsbHeadsetBattery.Protocol.VoidElite) == null,
            "Los reportes Corsair no se interpretaron correctamente.");
    }

    private static void TestBluetoothAudioClassification()
    {
        Verify(PeripheralBattery.IsBluetoothAudio(@"BTHHFENUM\BthHFPAudio") &&
            PeripheralBattery.IsBluetoothAudio(@"BTHENUM\{0000110b-0000-1000-8000-00805f9b34fb}_VID&1234") &&
            PeripheralBattery.IsBluetoothAudio(@"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_VID&1234") &&
            PeripheralBattery.IsBluetoothAudio(@"BTHLEDevice\{0000184e-0000-1000-8000-00805f9b34fb}_Dev_VID&1234") &&
            PeripheralBattery.IsBluetoothAudio(@"BTHLEDevice\{00001850-0000-1000-8000-00805f9b34fb}_Dev_VID&1234") &&
            !PeripheralBattery.IsBluetoothAudio(@"BTHLEDevice\{0000180f-0000-1000-8000-00805f9b34fb}_Dev_VID&1234") &&
            !PeripheralBattery.IsBluetoothAudio(@"BTHLEDevice\{0000184e-0000-1000-8000-012345678912}_Dev_VID&1234") &&
            !PeripheralBattery.IsBluetoothAudio(@"BTHENUM\{00001124-0000-1000-8000-00805f9b34fb}_VID&1234") &&
            !PeripheralBattery.IsBluetoothAudio(@"BTHENUM\{0000110b-0000-1000-8000-012345678912}_VID&1234"),
            "Clasificación incorrecta de servicios de audio Bluetooth.");
    }

    private static void TestStandardBatteryPercent()
    {
        Verify(HidBattery.BatteryPercent(0, 0, 255, true) == 0 &&
            HidBattery.BatteryPercent(63, 0, 255, true) == 63 &&
            HidBattery.BatteryPercent(100, 0, 255, true) == 100 &&
            HidBattery.BatteryPercent(101, 0, 255, true) == null &&
            HidBattery.BatteryPercent(128, 0, 255, false) == 50 &&
            HidBattery.BatteryPercent(12, 10, 14, false) == 50 &&
            HidBattery.BatteryPercent(9, 10, 14, false) == null,
            "Lectura incorrecta de carga relativa o del rango HID.");
    }

    private static void TestControllerBattery()
    {
        var sense = new byte[64];
        sense[0] = 1;
        sense[53] = 0x16;
        var shock = new byte[64];
        shock[0] = 1;
        shock[30] = 0x1B;
        byte[] bluetooth = new byte[78];
        bluetooth[0] = 0x31;
        bluetooth[54] = 0x09;
        Buffer.BlockCopy(new byte[] { 0x69, 0xBC, 0x20, 0xE4 }, 0, bluetooth, 74, 4);
        Verify(ControllerBattery.DecodeSony(0x0CE6, sense) == 65 &&
            ControllerBattery.DecodeSony(0x0DF2, sense) == 65 &&
            ControllerBattery.DecodeSony(0x09CC, shock) == 100 &&
            ControllerBattery.DecodeSony(0x0CE6, bluetooth) == 95 &&
            ControllerBattery.DecodeSony(0x0CE6, new byte[] { 1, 0, 0 }) == null,
            "El lector de PlayStation no interpretó los formatos USB y Bluetooth.");
        bluetooth[54] ^= 1;
        sense[53] = 0xF3;
        Verify(ControllerBattery.DecodeSony(0x0CE6, bluetooth) == null &&
            ControllerBattery.DecodeSony(0x0CE6, sense) == null &&
            ControllerBattery.XInputLevel(2, 1) == "Baja" &&
            ControllerBattery.XInputLevel(3, 3) == "Alta" &&
            ControllerBattery.XInputLevel(1, 3) == null &&
            ControllerBattery.XInputLevel(0xFF, 0) == null,
            "Se aceptó una batería inválida de PlayStation o XInput.");
    }

    private static void TestIndependentSelectionAndRendering()
    {
        var headphones = new BatteryReading("h1", "Audífonos A", 0, "Bluetooth", PeripheralKind.Headphones);
        var mouse = new BatteryReading("m1", "Mouse A", 70, "USB");
        var preferred = new BatteryReading("h2", "Audífonos B", 82, "USB", PeripheralKind.Headphones);
        BatteryReading[] readings = { headphones, mouse, preferred };
        Verify(BatteryTray.SelectReading(readings, PeripheralKind.Mouse, "h2") == mouse &&
            BatteryTray.SelectReading(readings, PeripheralKind.Headphones, "H2") == preferred &&
            BatteryTray.SelectReading(new[] { mouse }, PeripheralKind.Headphones, "h2") == null,
            "La selección de mouse y audífonos no es independiente.");

        using (var menu = new ContextMenuStrip())
        using (var indicator = new TaskbarIndicator(menu, () => { }))
        {
            indicator.SetLevels(null, headphones.Level, null, "Audífonos");
            Verify(indicator.LogicalWidth == 64, "Audífonos solos deben ocupar un segmento.");
            indicator.SetLevels(mouse.Level, preferred.Level, null, "Mouse y audífonos");
            Verify(indicator.LogicalWidth == 128, "Mouse y audífonos deben ocupar dos segmentos.");
            using (var bitmap = new Bitmap(128, 24))
            {
                indicator.DrawIndicator(bitmap, 96, Color.White);
                Verify(bitmap.GetPixel(72, 15).A > 100 || bitmap.GetPixel(70, 15).A > 100,
                    "El símbolo de audífonos no se dibujó en su segmento.");
            }
            indicator.SetLevels(mouse.Level, preferred.Level, "Media", "Los tres");
            Verify(indicator.LogicalWidth == 192, "Tres dispositivos deben ocupar tres segmentos.");
            using (var bitmap = new Bitmap(192, 24))
            {
                indicator.DrawIndicator(bitmap, 96, Color.White);
                Verify(bitmap.GetPixel(136, 11).A > 100, "El símbolo del mando no se dibujó en su segmento.");
            }
            indicator.SetLevels(null, null, "Media", "Mando");
            Verify(indicator.LogicalWidth == 64, "Un mando solo debe ocupar un segmento.");
            indicator.SetLevels(mouse.Level, null, null, "Mouse");
            Verify(indicator.LogicalWidth == 64, "Mouse solo debe volver a ocupar un segmento.");
        }
    }

    private static void TestWindowsTransport()
    {
        string pipe = "HuBattery-Test-" + Guid.NewGuid().ToString("N");
        using (var server = new NamedPipeServerStream(pipe, PipeDirection.InOut))
        using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            Task simulated = Task.Run(() =>
            {
                server.WaitForConnection();
                byte[] first = ReadReport(server);
                var unrelated = (byte[])first.Clone();
                unrelated[1] = 3;
                server.Write(unrelated, 0, unrelated.Length);
                first[4] = 83;
                server.Write(first, 0, first.Length);
                ReadReport(server); // Leave this request unanswered.
                byte[] last = ReadReport(server);
                last[4] = 37;
                server.Write(last, 0, last.Length);
            });
            client.Connect(3000);

            using (var file = new SafeFileHandle(client.SafePipeHandle.DangerousGetHandle(), false))
            using (var channel = new HidReportChannel(file))
            {
                var request = new byte[20];
                request[0] = 0x11;
                request[1] = 2;
                request[3] = 1;
                byte[] reply = LogitechBattery.Exchange(channel, request);
                Verify(reply != null && reply[4] == 83, "No se filtró la respuesta de otra ranura.");
                request[3] = 2;
                var clock = Stopwatch.StartNew();
                Verify(LogitechBattery.Exchange(channel, request) == null && clock.ElapsedMilliseconds < 2000,
                    "Una lectura sin respuesta no terminó a tiempo.");
                request[3] = 3;
                reply = LogitechBattery.Exchange(channel, request);
                Verify(reply != null && reply[4] == 37, "El canal no se recuperó después de cancelar una lectura.");
            }
            simulated.GetAwaiter().GetResult();
        }
    }

    private static byte[] ReadReport(Stream stream)
    {
        var bytes = new byte[20];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int count = stream.Read(bytes, offset, bytes.Length - offset);
            if (count == 0)
                throw new InvalidOperationException("Se cerró el canal de prueba.");
            offset += count;
        }
        return bytes;
    }

    private static BatteryReading Read(ReceiverReplies receiver)
    {
        return new LogitechBattery.Protocol(receiver.Exchange).ReadSlot(2, "receiver", "Logi Bolt");
    }

    private static void Verify(bool passed, string message)
    {
        if (!passed)
            throw new InvalidOperationException(message);
    }

    private sealed class ReceiverReplies
    {
        internal const string DeviceName = "Logitech mouse de prueba";
        internal int BatteryQueries;
        private readonly byte kind;
        private readonly bool unifiedReportsPercentage;
        private readonly byte unifiedPercent;
        private readonly bool hasLegacy;
        private readonly byte legacyPercent;

        internal ReceiverReplies(byte kind, bool hasExact, byte percent, bool hasLegacy, byte legacyPercent)
        {
            this.kind = kind;
            unifiedReportsPercentage = hasExact;
            unifiedPercent = percent;
            this.hasLegacy = hasLegacy;
            this.legacyPercent = legacyPercent;
        }

        internal byte[] Exchange(byte[] request)
        {
            Verify(request.Length == 20 && request[0] == 0x11 && request[1] == 2 && (request[3] & 15) != 0,
                "Reporte HID++ inválido.");
            var response = new byte[20];
            Array.Copy(request, response, 4);
            int function = request[3] >> 4;

            if (request[2] == 0 && function == 0)
            {
                int feature = (request[4] << 8) | request[5];
                switch (feature)
                {
                    case 0x0005: response[4] = 4; break;
                    case 0x1004: response[4] = 9; BatteryQueries++; break;
                    case 0x1000: response[4] = hasLegacy ? (byte)10 : (byte)0; BatteryQueries++; break;
                    default: throw new InvalidOperationException("Consulta de característica inesperada.");
                }
            }
            else if (request[2] == 4)
            {
                byte[] name = Encoding.UTF8.GetBytes(DeviceName);
                switch (function)
                {
                    case 0: response[4] = (byte)name.Length; break;
                    case 1: Array.Copy(name, request[4], response, 4, Math.Min(16, name.Length - request[4])); break;
                    case 2: response[4] = kind; break;
                    default: throw new InvalidOperationException("Consulta de dispositivo inesperada.");
                }
            }
            else if (request[2] == 9 && function == 0)
            {
                response[5] = unifiedReportsPercentage ? (byte)2 : (byte)0;
            }
            else if (request[2] == 9 && function == 1)
            {
                Verify(unifiedReportsPercentage, "Se pidió porcentaje a un dispositivo con niveles cualitativos.");
                response[4] = unifiedPercent;
            }
            else if (request[2] == 10 && function == 0 && hasLegacy)
            {
                response[4] = legacyPercent;
            }
            else
            {
                throw new InvalidOperationException("Comando HID++ inesperado.");
            }
            return response;
        }
    }
}
