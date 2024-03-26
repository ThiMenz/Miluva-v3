using Solid.Arduino.Firmata;
using Solid.Arduino;
using System.IO.Ports;
using System.Diagnostics;

namespace ChessBot
{
    public static class FirmataArdControl
    {
        private static Stopwatch sw = new Stopwatch();

        private static ArduinoSession? SESSION;
        private static ISerialConnection? CONNECTION;

        private const bool X_MOTOR = true, Y_MOTOR = false, DOWN = false, UP = true, LEFT = false, RIGHT = true;

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

            ARDUINO_ACTION Y_DOWN = new STEPPER_MOTOR_TURN(Y_MOTOR, 500, 160, DOWN); // Unten
            ARDUINO_ACTION Y_UP = new STEPPER_MOTOR_TURN(Y_MOTOR, 500, 160, UP); // Oben
            ARDUINO_ACTION X_LEFT = new STEPPER_MOTOR_TURN(X_MOTOR, 250, 160, LEFT);
            ARDUINO_ACTION X_RIGHT = new STEPPER_MOTOR_TURN(X_MOTOR, 250, 160, RIGHT);
            ARDUINO_ACTION MAGNET_UP = new MAGNET_STATE_SET(true);
            ARDUINO_ACTION MAGNET_DOWN = new MAGNET_STATE_SET(false);


            //SESSION.CreateDigitalStateMonitor()

            string str;

            //SESSION.RequestPinState(9);

            Console.WriteLine(SESSION.GetPinState(9).Value);

            ExecuteActions(
                (MAGNET_UP, 200),
                (MAGNET_DOWN, 200)
            );

            /*                (Y_DOWN, 200),
                (X_LEFT, 200),
                (MAGNET_DOWN, 200),
                (Y_UP, 200),
                (X_RIGHT, 200),
                (Y_DOWN, 200),
                (X_LEFT, 200),
                (MAGNET_UP, 200),
                (Y_UP, 200),
                (X_RIGHT, 200),
                (MAGNET_DOWN, 200)*/
            Console.WriteLine(":)");

            //CONNECTION.Close();
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

            for (int i = 0; i < m; i++)
            {
                WaitForTickCount(5000L);
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
            SESSION.SetDigitalPinMode(6, PinMode.DigitalOutput); // TEST LED
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

        private static void ExecuteActions(params (ARDUINO_ACTION, int)[] pActions)
        {
            if (SESSION == null) return;

            foreach ((ARDUINO_ACTION, int) pAct in pActions)
                SendAction(pAct.Item1, pAct.Item2);

            SESSION.SetDigitalPin(10, true);
        }

        private static void SendAction(ARDUINO_ACTION pArdAction, int pDelayAfterwards)
        {
            if (pDelayAfterwards < 0) pDelayAfterwards = 0;
            else if (pDelayAfterwards > 255) pDelayAfterwards = 255;

            SendNumberToArduino((ulong)((int)pArdAction.VAL | (pDelayAfterwards << 4)));
        }

        private interface ARDUINO_ACTION
        {
            ulong VAL {
                get;
            }
        }

        private struct STEPPER_MOTOR_TURN : ARDUINO_ACTION
        {
            public ulong VAL { get; private set; }

            public STEPPER_MOTOR_TURN(bool pMotorAxis, int pSteps, int pRPM, bool pDir)
            {
                VAL = (pMotorAxis ? 8ul : 0ul) | (pDir ? 4096ul : 0ul) | ((ulong)pRPM << 13) | ((ulong)pSteps << 21);
            }
        }

        private struct MAGNET_STATE_SET : ARDUINO_ACTION
        {
            public ulong VAL { get; private set; }

            public MAGNET_STATE_SET(bool pState)
            {
                VAL = 2ul | (pState ? 8ul : 0ul);
            }
        }
    }
    public class MyObserver : IObserver<string>
    {
        public IDisposable _unsubscriber;

        public virtual void Subscribe(IObservable<string> provider)
        {
            if (provider != null)
            {
                _unsubscriber = provider.Subscribe(this);
            }
        }

        public virtual void OnCompleted()
        {
            _unsubscriber.Dispose();
        }

        public virtual void OnError(Exception ex)
        { }

        public virtual void OnNext(string str)
        {
            Console.WriteLine(str);
        }
    }
}
