using Solid.Arduino.Firmata;
using Solid.Arduino;
using System.IO.Ports;
using System.Diagnostics;

namespace ChessBot
{
    public static class FirmataArdControl
    {
        private static STEPPER_MOTOR STEPPER1 = new STEPPER_MOTOR() { 
            dirPin = 5,
            stepPin = 2,
            maxRPM = 200,
            stepsPerRot = 200
        };

        private static Stopwatch sw = new Stopwatch();

        private static ArduinoSession? SESSION;
        private static ISerialConnection? CONNECTION;

        public static void TEST()
        {
            sw.Start();
            CONNECTION = GetConnection();

            if (CONNECTION != null)
            {
                SESSION = new ArduinoSession(CONNECTION);
            }

            PerformBasicTest(SESSION);

            Console.WriteLine("Press a key");
            Console.ReadKey(true);
        }

        private static ISerialConnection? GetConnection()
        {
            Console.WriteLine("Searching Arduino connection...");
            ISerialConnection connection = EnhancedSerialConnection.Find();

            if (connection == null)
                Console.WriteLine("No connection found. Make shure your Arduino board is attached to a USB port.");
            else
                Console.WriteLine($"Connected to port {connection.PortName} at {connection.BaudRate} baud.");

            return connection;
        }

        private static void PerformBasicTest(IFirmataProtocol? session)
        {
            if (session == null)
            {
                Console.WriteLine("Session = null");
                return;
            }

            var firmware = session.GetFirmware();
            Console.WriteLine($"Firmware: {firmware.Name} version {firmware.MajorVersion}.{firmware.MinorVersion}");
            var protocolVersion = session.GetProtocolVersion();
            Console.WriteLine($"Firmata protocol version {protocolVersion.Major}.{protocolVersion.Minor}");

            SETUP_PINS();

            WaitForTickCount(10_000_000L);

            //SendNumberToArduino((200 << 1) | 1);
            SendNumberToArduino(0);
            //SendNumberToArduino((200 << 9) | (100 << 1) | 0);
            //SendNumberToArduino((200 << 9) | (200 << 1) | 1);
            //SendNumberToArduino((200 << 9) | (100 << 1) | 0);

            SESSION.SetDigitalPin(10, true);

            //SESSION.CreateAnalogStateMonitor();

            //CONNECTION.WriteLine("Test");

            Console.WriteLine(":)");
            //int count;
            //
            //while ((count = CONNECTION.BytesToRead) == 0)
            //{
            //    WaitForTickCount(100_000L);
            //}
            //
            //Console.WriteLine(count);
            //
            //for (int i = 0; i < count; i++)
            //{
            //    Console.WriteLine(i);
            //    Console.WriteLine(CONNECTION.ReadByte());
            //}
            //SESSION.SetDigitalPin(9, true);


            //for (int i = 0; i < 100; i++)
            //{
            //    TURN_STEPPER(200, 60, true, STEPPER1);
            //    WaitForTickCount(10_000_000L);
            //    TURN_STEPPER(200, 60, false, STEPPER1);
            //    WaitForTickCount(10_000_000L);
            //}

            //CONNECTION.Close();

            Console.WriteLine(":)");
        }

        private static void SendNumberToArduino(ulong pNum)
        {
            if (SESSION == null)
            {
                return;
            }

            int m = 1;
            for (int i = 1; i < 64; i++)
            {
                if (ULONG_OPERATIONS.IsBitOne(pNum, i)) m = i + 1;
            }

            Console.WriteLine(m);

            for (int i = 0; i < m; i++)
            {
                WaitForTickCount(50000L);
                SESSION.SetDigitalPin(9, ULONG_OPERATIONS.IsBitOne(pNum, i));
            }
            SESSION.SetDigitalPin(10, false);
        }

        private static void WaitForTickCount(long pTickCount)
        {
            long tT = sw.ElapsedTicks + pTickCount;

            while (sw.ElapsedTicks < tT) { }
        }

        private static void WaitUntil(long pTimeStamp)
        { 
            while (sw.ElapsedTicks < pTimeStamp) { }
        }

        private static void SETUP_PINS()
        {
            if (SESSION == null) return;

            SESSION.SetDigitalPinMode(2, PinMode.DigitalOutput); // STEP STEPPER 1 PIN
            SESSION.SetDigitalPinMode(5, PinMode.DigitalOutput); // DIR STEPPER 1 PIN 
            SESSION.SetDigitalPinMode(8, PinMode.DigitalOutput); // TEST LED
        }

        private static void TURN_STEPPER(int pSteps, int pRPM, bool pClockwise, STEPPER_MOTOR pStepper)
        {
            if (SESSION == null)
            {
                return;
            }

            pRPM = Math.Clamp(pRPM, 0, pStepper.maxRPM);
            int tickDelay = (int)(300_000_000 / (double)(pRPM * pStepper.stepsPerRot));

            SESSION.SetDigitalPin(pStepper.dirPin, !pClockwise);

            WaitForTickCount(100_000L);

            long tT = sw.ElapsedTicks;

            for (int c = 0; c < pSteps; c++)
            {
                SESSION.SetDigitalPin(pStepper.stepPin, true);
                WaitUntil(tT += tickDelay);
                SESSION.SetDigitalPin(pStepper.stepPin, false);
                WaitUntil(tT += tickDelay);
            }
        }

        private struct STEPPER_MOTOR
        {
            public int stepPin, dirPin, stepsPerRot, maxRPM;
        }
    }
}
