using Solid.Arduino.Firmata;
using Solid.Arduino;
using System.IO.Ports;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace Miluva
{
    public static class FirmataArdControl
    {
        private static Stopwatch sw = new Stopwatch();

        private static ArduinoSession? SESSION;
        private static ISerialConnection? CONNECTION;

        private const bool X_MOTOR = true, Y_MOTOR = false, DOWN = false, UP = true, LEFT = false, RIGHT = true;

        public static void SetupArduinoConnection()
        {
            sw.Start();
            CONNECTION = GetConnection();

            if (CONNECTION != null)
            {
                SESSION = new ArduinoSession(CONNECTION);
            }
        }

        public static void ARDUINO_GAME()
        {
            SetupArduinoConnection();

            if (SESSION == null) return;

            switch (ARDUINO_GAME_SETTINGS.GAME_MODE.ToLower())
            {
                case "classic":
                    Classic_Arduino_Game();
                    break;
            }
        }

        private static ChessClock? HUMAN_CHESS_CLOCK, BOT_CHESS_CLOCK;
        private static int[] promTypeConversionArray = new int[7] { 0, 0, 3, 2, 1, 0, 0 };
        private static int[] promTypeConversionArrayBack = new int[4] { 5, 4, 3, 2 };

        private static void Classic_Arduino_Game()
        {
            TimeFormat tfHUMAN = new TimeFormat(ARDUINO_GAME_SETTINGS.HUMAN_TIME_IN_SEC * 10_000_000L, (long)(ARDUINO_GAME_SETTINGS.HUMAN_INCREMENT_IN_SEC * 10_000_000d));
            TimeFormat tfBOT = new TimeFormat(ARDUINO_GAME_SETTINGS.BOT_TIME_IN_SEC * 10_000_000L, (long)(ARDUINO_GAME_SETTINGS.BOT_INCREMENT_IN_SEC * 10_000_000d));

            ChessClock humanChessClock = new ChessClock(); //, botChessClock = new ChessClock();
            humanChessClock.Set(tfHUMAN);
            HUMAN_CHESS_CLOCK = humanChessClock;

            BOT_MAIN.SetupParallelBoards();
            IBoardManager MainBoardManager = BOT_MAIN.boardManagers[0];
            MainBoardManager.LoadFenString(ARDUINO_GAME_SETTINGS.START_FEN);
            BOT_CHESS_CLOCK = MainBoardManager.chessClock;
            BOT_CHESS_CLOCK.Set(tfBOT);

            STATIC_MAIN_CAMERA_ANALYSER.SETUP();

            bool startFENstartswithwhite = ARDUINO_GAME_SETTINGS.START_FEN.Split('/')[7].Split(' ')[1] == "w";

            ChangePanel(1, 0, false, startFENstartswithwhite);

            if (ARDUINO_GAME_SETTINGS.HUMAN_PLAYS_WHITE ^ startFENstartswithwhite)
            {
                WaitUntilPinIsOne(3);

                Move? tM = MainBoardManager.ReturnNextMove(null, 100_000_000L);

                if (tM == null) // Bot has lost
                {
                    ChangePanel(6, 0, false, true);
                    return;
                }
                else if (MainBoardManager.GameState(ARDUINO_GAME_SETTINGS.HUMAN_PLAYS_WHITE) != 3) return;
                else ChangePanel(7, 0, false, false);

                CalculateAndExecutePath(MainBoardManager.allPieceBitboard, tM);

                WaitUntilPinIsOne(4); // Wait until move has been done

                if (tM.isPromotion)
                {
                    ChangePanel(5, promTypeConversionArray[tM.promotionType], false, false);
                }

                ChangePanel(2, 0, false, !startFENstartswithwhite);
            }

            while (true)
            {

                //**********************
                // HUMAN'S TURN
                //**********************

                int ttimeC = WaitUntilPinIsOne(9);
                HUMAN_CHESS_CLOCK.MoveFinished(ttimeC * 5_000_000L);

            ReAnalysis:
                List<ulong> camAnlysisResult = STATIC_MAIN_CAMERA_ANALYSER.ANALYSE();
                List<Move> tLegalMoves = new List<Move>();
                MainBoardManager.GetLegalMoves(ref tLegalMoves);
                MainBoardManager.SetJumpState();
                Move? tM = null;
                int tC = tLegalMoves.Count;
                bool legalMoveFound = false;
                for (int i = 0; i < tC; i++)
                {
                    tM = tLegalMoves[i];

                    MainBoardManager.LoadJumpState();
                    MainBoardManager.PlainMakeMove(tM);

                    //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(MainBoardManager.allPieceBitboard));

                    if (camAnlysisResult.Contains(MainBoardManager.allPieceBitboard))
                    {
                        if (tM.isPromotion)
                        {
                            ChangePanel(4, 0, false, !ARDUINO_GAME_SETTINGS.HUMAN_PLAYS_WHITE);
                            WaitUntilPinIsOne(10);

                            long ps7 = SESSION.GetPinState(7).Value, ps8 = SESSION.GetPinState(8).Value;
                            int tpromType = (int)(ps7 | (ps8 << 1));
                            MainBoardManager.LoadJumpState();

                            for (int j = 0; j < tC; j++)
                            {
                                tM = tLegalMoves[j];
                                MainBoardManager.PlainMakeMove(tM);
                                if (camAnlysisResult.Contains(MainBoardManager.allPieceBitboard) && tpromType == tM.promotionType)
                                {
                                    legalMoveFound = true;
                                    break;
                                }
                            }

                        }
                        else legalMoveFound = true;

                        break;
                    }
                }

                MainBoardManager.LoadJumpState();

                if (!legalMoveFound)
                {
                    ChangePanel(3, 0, false, !ARDUINO_GAME_SETTINGS.HUMAN_PLAYS_WHITE);
                    WaitForTickCount(10_000_000L);
                    WaitUntilPinIsOne(9);
                    goto ReAnalysis;
                }

                Console.WriteLine("MOVE FOUND: " + tM);

                ChangePanel(2, 0, false, !ARDUINO_GAME_SETTINGS.HUMAN_PLAYS_WHITE);



                //**********************
                // BOT'S TURN
                //**********************

                if (tM == null) return; // Pretty much irrelevant, just to not get the warning

                Move? tbM = MainBoardManager.ReturnNextMove(tM, 100_000_000L);

                if (tbM == null) // Bot has lost
                {
                    ChangePanel(6, 0, false, true);
                    return;
                }
                else if (MainBoardManager.GameState(ARDUINO_GAME_SETTINGS.HUMAN_PLAYS_WHITE) != 3) return;
                else ChangePanel(7, 0, false, false);

                CalculateAndExecutePath(MainBoardManager.allPieceBitboard, tbM);

                WaitUntilPinIsOne(4); // Wait until move has been done

                if (tbM.isPromotion)
                {
                    ChangePanel(5, promTypeConversionArray[tbM.promotionType], false, false);
                }

                ChangePanel(2, 0, false, ARDUINO_GAME_SETTINGS.HUMAN_PLAYS_WHITE);
            }

            //
            //WaitForTickCount(5_000_000L);
            //while (SESSION.GetPinState(10).Value == 0)
            //{
            //    WaitForTickCount(5_000_000L);
            //}
            //long ps7 = SESSION.GetPinState(7).Value, ps8 = SESSION.GetPinState(8).Value;
            //
            //Console.WriteLine(ps7 | (ps8 << 1));


            // Links = Weiß

            //Console.WriteLine((int)((ARDUINO_GAME_SETTINGS.HUMAN_PLAYS_WHITE ? humanChessClock : botChessClock).curRemainingTime / 1000000));
            //
            //ARDUINO_ACTION PANEL_CHANGE2 = new PANEL_CHANGE(1, 0, false, 
            //    (
            //    ARDUINO_GAME_SETTINGS.HUMAN_PLAYS_WHITE, true, (int)((ARDUINO_GAME_SETTINGS.HUMAN_PLAYS_WHITE ? humanChessClock : botChessClock).curRemainingTime / 1000000)
            //    ) 
            //);
        }

        private static void ChangePanel(int pPanelID, int pUniVal, bool pUniBool, bool pNextsWhiteTurn)
        {
            if (HUMAN_CHESS_CLOCK == null || BOT_CHESS_CLOCK == null) return;

            bool tHT = !(ARDUINO_GAME_SETTINGS.HUMAN_PLAYS_WHITE ^ pNextsWhiteTurn);
            ARDUINO_ACTION PANEL_CHANGE = new PANEL_CHANGE(pPanelID, pUniVal, pUniBool,
            (tHT, pNextsWhiteTurn, (int)((tHT ? HUMAN_CHESS_CLOCK : BOT_CHESS_CLOCK).curRemainingTime / 1000000)));
            ExecuteActions(PANEL_CHANGE);
        }

        private static int WaitUntilPinIsOne(int pID)
        {
            int count = 0;
            do {
                WaitForTickCount(5_000_000L);
                count++;
            } while (SESSION.GetPinState(pID).Value == 0);
            return count;
        }

        public static void TEST()
        {
            SetupArduinoConnection();

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

            //ARDUINO_ACTION Y_DOWN = new STEPPER_MOTOR_TURN(Y_MOTOR, 250, 160, DOWN); // Unten
            //ARDUINO_ACTION Y_UP = new STEPPER_MOTOR_TURN(Y_MOTOR, 250, 160, UP); // Oben
            //ARDUINO_ACTION X_LEFT = new STEPPER_MOTOR_TURN(X_MOTOR, 163, 160, LEFT);
            //ARDUINO_ACTION X_RIGHT = new STEPPER_MOTOR_TURN(X_MOTOR, 163, 160, RIGHT);
            //ARDUINO_ACTION MAGNET_UP = new MAGNET_STATE_SET(true);
            //ARDUINO_ACTION MAGNET_DOWN = new MAGNET_STATE_SET(false);
            

            //SESSION.CreateDigitalStateMonitor()

            //SESSION.RequestPinState(9);

           // ExecuteActions(
           //     //(MAGNET_UP, 200),
           //     //(MAGNET_DOWN, 200),
           //     (X_RIGHT, 70)
           //
           // );

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

        public static void CalculateAndExecutePath(ulong pBlockedSquares, Move pMove)
        {
            CalculateAndExecutePath(pBlockedSquares, pMove.startPos, pMove.endPos);
        }

        public static void CalculateAndExecutePath(ulong pBlockedSquares, int pFrom, int pTo)
        {
            pFrom = pFrom % 8 * 8 + (pFrom - pFrom % 8) / 8;
            pTo = pTo % 8 * 8 + (pTo - pTo % 8) / 8;

            MagnetMoveSequence mms = MagnetMovePathfinder.CalculatePath(ULONG_OPERATIONS.FlipBoard90Degress(pBlockedSquares), pFrom, pTo);
            mms.GetDirectionMoveString();

            List<(ARDUINO_ACTION, int)> tActions = new List<(ARDUINO_ACTION, int)>();
            bool tMagnetState = false;

            string outp = "";
            string[] DEUTSCHE_ANWEISUNGEN = new string[5] { "Oben", "Unten", "Links", "Rechts", "MAGNET" };
            int tC = 1, ll = -1, fC = 0;
            for (int i = 0; i < MagnetMovePathfinder.FINAL_ACTIONS.Count; i++)
            {
                int tAct = MagnetMovePathfinder.FINAL_ACTIONS[i];
                if (tAct == ll)
                {
                    tC++;
                }
                else if (i != 0)
                {
                    outp += tC + "x " + DEUTSCHE_ANWEISUNGEN[ll] + ", ";
                    fC++;
                    AppendPathAction(ref tActions, ref tMagnetState, ll, tC);
                    tC = 1;
                }
                ll = tAct;
            }
            if (ll != -1)
            {
                fC++;
                outp += tC + "x " + DEUTSCHE_ANWEISUNGEN[ll];
                AppendPathAction(ref tActions, ref tMagnetState, ll, tC);
            }

            Console.WriteLine("[" + fC + " Actions]  " + outp);

            //foreach ((ARDUINO_ACTION, int) ta in tActions)
            //    Console.WriteLine(ta);

            ExecuteActions(tActions.ToArray());
        }

        private static void AppendPathAction(ref List<(ARDUINO_ACTION, int)> pList, ref bool pMagnetState, int pActionID, int pCount)
        {
            const int tRPM = 160;
            const int tDelay = 200;

            switch (pActionID)
            {
                case 0: // Oben
                    ARDUINO_ACTION Y_UP = new STEPPER_MOTOR_TURN(Y_MOTOR, 163 * pCount, tRPM, UP);
                    pList.Add((Y_UP, tDelay));
                    break;
                case 1: // Unten
                    ARDUINO_ACTION Y_DOWN = new STEPPER_MOTOR_TURN(Y_MOTOR, 163 * pCount, tRPM, DOWN);
                    pList.Add((Y_DOWN, tDelay));
                    break;
                case 2: // Links
                    ARDUINO_ACTION X_LEFT = new STEPPER_MOTOR_TURN(X_MOTOR, 163 * pCount, tRPM, LEFT);
                    pList.Add((X_LEFT, tDelay));
                    break;
                case 3: // Rechts
                    ARDUINO_ACTION X_RIGHT = new STEPPER_MOTOR_TURN(X_MOTOR, 163 * pCount, tRPM, RIGHT);
                    pList.Add((X_RIGHT, tDelay));
                    break;
                case 4: // Magnet
                    ARDUINO_ACTION MAGNET = new MAGNET_STATE_SET(true);
                    if (pMagnetState) MAGNET = new MAGNET_STATE_SET(false);
                    pList.Add((MAGNET, tDelay));
                    pMagnetState = !pMagnetState;
                    break;
            }
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
                WaitForTickCount(25000L);
                SESSION.SetDigitalPin(9, ULONG_OPERATIONS.IsBitOne(pNum, i));
            }
            WaitForTickCount(5000L);
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

        private static void ExecuteActions(params ARDUINO_ACTION[] pActions)
        {
            if (SESSION == null) return;

            foreach (ARDUINO_ACTION pAct in pActions)
                SendAction(pAct);

            SESSION.SetDigitalPin(10, true);
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

        private static void SendAction(ARDUINO_ACTION pArdAction)
        {
            SendNumberToArduino(pArdAction.VAL);
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

        private struct PANEL_CHANGE : ARDUINO_ACTION
        {
            public ulong VAL { get; private set; }

            public PANEL_CHANGE(int pNextPanel, int pUniversalInfo, bool pUniversalBool, (bool, bool, int) pTimerInfo)
            {
                VAL = 3ul | (pUniversalBool ? 8ul : 0ul) | (pTimerInfo.Item1 ? 4096ul : 0ul) | (pTimerInfo.Item2 ? 8192ul : 0ul)
                          | ((ulong)pTimerInfo.Item3 << 14) | ((ulong)pNextPanel << 4) | ((ulong)pUniversalInfo << 7);
            }
        }
    }
}
