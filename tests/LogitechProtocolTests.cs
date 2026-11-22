using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

internal static class LogitechProtocolTests
{
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
            TestWindowsTransport();
            Console.WriteLine("HID: 9 pruebas aprobadas.");
            return 0;
        }
        catch (InvalidOperationException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
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
        MouseReading reading = Read(replies);
        Verify(reading != null && reading.Percent == 0 && reading.Name == ReceiverReplies.DeviceName &&
            reading.Id == "receiver:2" && reading.Connection == "Logi Bolt",
            "No se aceptó 0 % real o no se reconstruyó el nombre de varios fragmentos.");
    }

    private static void TestLegacyPercentage()
    {
        var replies = new ReceiverReplies(5, false, 0, true, 61);
        MouseReading reading = Read(replies);
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
        MouseReading reading = LogitechBattery.ReadLegacySlot(2, "receiver", "Nano", pairing, charge);
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

    private static MouseReading Read(ReceiverReplies receiver)
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
