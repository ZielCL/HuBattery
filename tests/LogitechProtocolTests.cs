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
            TestWindowsTransport();
            Console.WriteLine("Logitech HID++: 7 pruebas aprobadas.");
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
            using (var channel = new LogitechChannel(file))
            {
                var request = new byte[20];
                request[0] = 0x11;
                request[1] = 2;
                request[3] = 1;
                byte[] reply = channel.Exchange(request);
                Verify(reply != null && reply[4] == 83, "No se filtró la respuesta de otra ranura.");
                request[3] = 2;
                var clock = Stopwatch.StartNew();
                Verify(channel.Exchange(request) == null && clock.ElapsedMilliseconds < 2000,
                    "Una lectura sin respuesta no terminó a tiempo.");
                request[3] = 3;
                reply = channel.Exchange(request);
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
