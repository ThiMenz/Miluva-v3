using Solid.Arduino.Firmata;
using Solid.Arduino;
using System.IO.Ports;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using MathNet.Numerics.Statistics.Mcmc;
using System.Transactions;

namespace Miluva
{
    public static class FirmataArdControl
    {
        private static Stopwatch sw = Stopwatch.StartNew();

        private static ArduinoSession? SESSION;
        private static ISerialConnection? CONNECTION;

        private const bool X_MOTOR = true, Y_MOTOR = false, DOWN = false, UP = true, LEFT = true, RIGHT = false;

        public static void SetupArduinoConnection()
        {
            CONNECTION = GetConnection();

            if (CONNECTION != null)
            {
                SESSION = new ArduinoSession(CONNECTION);
            }
        }

        public static void ARDUINO_GAME()
        {
            SetupArduinoConnection();

            if (SESSION == null)
            {
                Console.ReadKey();
                return;
            }

            switch (ARDUINO_GAME_SETTINGS.GAME_MODE.ToLower())
            {
                case "classic":
                    Classic_Arduino_Game();
                    break;
            }

            Console.ReadKey();
        }

        private static ChessClock WHITE_CHESS_CLOCK, BLACK_CHESS_CLOCK;
        private static int[] promTypeConversionArray = new int[7] { 0, 0, 3, 2, 1, 0, 0 };
        private static int[] promTypeConversionArrayBack = new int[4] { 5, 4, 3, 2 };
        private static bool CURRENTLY_WHITES_TURN = true;
        private static bool CURRENTLY_FIRST_TURN = true;

        private static void Classic_Arduino_Game()
        {
            TimeFormat tfWHITE = new TimeFormat(ARDUINO_GAME_SETTINGS.WHITE_TIME_IN_SEC * 10_000_000L, (long)(ARDUINO_GAME_SETTINGS.WHITE_INCREMENT_IN_SEC * 10_000_000d));
            TimeFormat tfBLACK = new TimeFormat(ARDUINO_GAME_SETTINGS.BLACK_TIME_IN_SEC * 10_000_000L, (long)(ARDUINO_GAME_SETTINGS.BLACK_INCREMENT_IN_SEC * 10_000_000d));

            Move? tM = null;
            BOT_MAIN.SetupParallelBoards();
            IBoardManager MainBoardManager = BOT_MAIN.boardManagers[0], AlternativeBoardManager = BOT_MAIN.boardManagers[1];
            BoardManager NonPlayingBoardManager = new BoardManager(ENGINE_VALS.DEFAULT_FEN);

            bool twoengines;

            if (!(twoengines = (ARDUINO_GAME_SETTINGS.BLACK_ENTITY == ARDUINO_GAME_SETTINGS.WHITE_ENTITY && ARDUINO_GAME_SETTINGS.WHITE_ENTITY == ARDUINO_GAME_SETTINGS.ENTITY_TYPE.ENGINE))) AlternativeBoardManager = MainBoardManager;
            else AlternativeBoardManager.LoadFenString(ARDUINO_GAME_SETTINGS.START_FEN);
            MainBoardManager.LoadFenString(ARDUINO_GAME_SETTINGS.START_FEN);
            NonPlayingBoardManager.LoadFenString(ARDUINO_GAME_SETTINGS.START_FEN);

            Console.WriteLine(!(ARDUINO_GAME_SETTINGS.BLACK_ENTITY == ARDUINO_GAME_SETTINGS.WHITE_ENTITY && ARDUINO_GAME_SETTINGS.WHITE_ENTITY == ARDUINO_GAME_SETTINGS.ENTITY_TYPE.ENGINE));
            Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(AlternativeBoardManager.allPieceBitboard));


            switch (ARDUINO_GAME_SETTINGS.WHITE_ENTITY)
            {
                case ARDUINO_GAME_SETTINGS.ENTITY_TYPE.PLAYER:
                    ChessClock humanChessClock = new ChessClock();
                    WHITE_CHESS_CLOCK = humanChessClock;
                    break;

                case ARDUINO_GAME_SETTINGS.ENTITY_TYPE.ENGINE:
                    WHITE_CHESS_CLOCK = MainBoardManager.chessClock;
                    break;

                case ARDUINO_GAME_SETTINGS.ENTITY_TYPE.DISCORD:
                    ChessClock dcChessClock = new ChessClock();
                    WHITE_CHESS_CLOCK = dcChessClock;
                    break;
            }
            switch (ARDUINO_GAME_SETTINGS.BLACK_ENTITY)
            {
                case ARDUINO_GAME_SETTINGS.ENTITY_TYPE.PLAYER:
                    ChessClock humanChessClock = new ChessClock();
                    BLACK_CHESS_CLOCK = humanChessClock;
                    break;

                case ARDUINO_GAME_SETTINGS.ENTITY_TYPE.ENGINE:
                    BLACK_CHESS_CLOCK = AlternativeBoardManager.chessClock;
                    break;

                case ARDUINO_GAME_SETTINGS.ENTITY_TYPE.DISCORD:
                    ChessClock dcChessClock = new ChessClock();
                    BLACK_CHESS_CLOCK = dcChessClock;
                    break;
            }

            WHITE_CHESS_CLOCK.Set(tfWHITE);
            BLACK_CHESS_CLOCK.Set(tfBLACK);

            STATIC_MAIN_CAMERA_ANALYSER.SETUP();

            bool startFENstartswithwhite = ARDUINO_GAME_SETTINGS.START_FEN.Split('/')[7].Split(' ')[1] == "w";

            ChangePanel(1, 0, false, startFENstartswithwhite);
            if ( (ARDUINO_GAME_SETTINGS.WHITE_ENTITY != ARDUINO_GAME_SETTINGS.ENTITY_TYPE.PLAYER && startFENstartswithwhite)
             ||  (ARDUINO_GAME_SETTINGS.BLACK_ENTITY != ARDUINO_GAME_SETTINGS.ENTITY_TYPE.PLAYER && !startFENstartswithwhite) ) WaitUntilPinIsOne(3);

            int tEndGameState = 3;

            while (true)
            {
                if (tM != null)
                {
                    NonPlayingBoardManager.PlainMakeMove(tM);
                    //if (twoengines) (startFENstartswithwhite ? MainBoardManager : AlternativeBoardManager).PlainMakeMove(tM);
                }
                if (!(CURRENTLY_WHITES_TURN ? WHITE_CHESS_CLOCK : BLACK_CHESS_CLOCK).HasTimeLeft() || (tEndGameState = NonPlayingBoardManager.GameState(CURRENTLY_WHITES_TURN = startFENstartswithwhite)) != 3) goto EndOfMatchup;
                switch (startFENstartswithwhite ? ARDUINO_GAME_SETTINGS.WHITE_ENTITY : ARDUINO_GAME_SETTINGS.BLACK_ENTITY)
                {
                    case ARDUINO_GAME_SETTINGS.ENTITY_TYPE.PLAYER:
                        tM = RealLifePlayerTurn(NonPlayingBoardManager);
                        break;

                    case ARDUINO_GAME_SETTINGS.ENTITY_TYPE.ENGINE:
                        tM = EngineTurn(startFENstartswithwhite ? MainBoardManager : AlternativeBoardManager, tM);
                        break;

                    case ARDUINO_GAME_SETTINGS.ENTITY_TYPE.DISCORD:
                        tM = DiscordTurn(NonPlayingBoardManager);
                        break;
                }
                CURRENTLY_FIRST_TURN = false;
                if (tM != null)
                {
                    NonPlayingBoardManager.PlainMakeMove(tM);
                    //if (twoengines) (startFENstartswithwhite ? AlternativeBoardManager : MainBoardManager).PlainMakeMove(tM);
                }
                if (!(CURRENTLY_WHITES_TURN ? WHITE_CHESS_CLOCK : BLACK_CHESS_CLOCK).HasTimeLeft() || (tEndGameState = NonPlayingBoardManager.GameState(CURRENTLY_WHITES_TURN = !startFENstartswithwhite)) != 3) goto EndOfMatchup;
                switch (startFENstartswithwhite ? ARDUINO_GAME_SETTINGS.BLACK_ENTITY : ARDUINO_GAME_SETTINGS.WHITE_ENTITY)
                {
                    case ARDUINO_GAME_SETTINGS.ENTITY_TYPE.PLAYER:
                        tM = RealLifePlayerTurn(NonPlayingBoardManager);
                        break;

                    case ARDUINO_GAME_SETTINGS.ENTITY_TYPE.ENGINE:
                        tM = EngineTurn(startFENstartswithwhite ? AlternativeBoardManager : MainBoardManager, tM);
                        break;

                    case ARDUINO_GAME_SETTINGS.ENTITY_TYPE.DISCORD:
                        tM = DiscordTurn(NonPlayingBoardManager);
                        break;
                }
            }

        EndOfMatchup:

            int tEndVal = 0;
            if (tEndGameState == -1) tEndVal = 2;
            else if (tEndGameState == 0) tEndVal = 1;
            else if (tEndGameState == 3) tEndVal = WHITE_CHESS_CLOCK.HasTimeLeft() ? 0 : 2;

            ChangePanel(6, tEndVal, false, false);
        }

        //private static string[] on_words = new string[4] { "von", "on", "auf", "from" };
        private static string[] to_words = new string[5] { "to", "nach", "zu", "auf", "von" };

        private static string[,] piece_words = new string[6, 2]
        {
            { "bauer", "pawn" },
            { "springer", "knight" },
            { "läufer", "bishop" },
            { "turm", "rook" },
            { "dame", "queen" },
            { "könig", "king" }
        };

        private static int PieceTypeFromString(string pStr)
        {
            for (int i = 0; i < piece_words.GetLength(0); i++)
            {
                for (int j = 0; j < piece_words.GetLength(1); j++)
                {
                    if (pStr == piece_words[i, j])
                        return i + 1;
                } 
            }
            return 0;
        } 

        public static int GetSquareFromChars(char pC1, char pC2)
        {
            return pC1 - 97 + (pC2 - 49) * 8;
        }

        public static Move? GetMoveFromDCMessage(string pStr, IBoardManager pB)
        {
            List<Move> tMoves = new List<Move>();
            pB.GetLegalMoves(ref tMoves);

            pStr = pStr.ToLower().Replace(",", "").Replace(".", "");
            bool spokenVersion = false; //isAPromotion = pStr.Contains("verwand") || pStr.Contains("promot")

            for (int i = 0; i < to_words.Length; i++)
                if (pStr.Contains(" " + to_words[i] + " "))
                    spokenVersion = true;

            int pieceStartPos = -1, pieceType = -1, pieceEndpos = -1, piecePromType = -1;

            if (spokenVersion)
            {
                string[] pStrSpl = pStr.Split(' ');

                for (int p = 0; p < pStrSpl.Length; p++)
                {
                    int tPT = PieceTypeFromString(pStrSpl[p]);

                    if (tPT != 0 && pieceType != -1) piecePromType = tPT;

                    if (to_words.Contains(pStrSpl[p]))
                    {
                        string pPSq = pStrSpl[--p];

                        //if (pPSq.Length != 2) { p++; continue; }

                        int ttiPT = PieceTypeFromString(pStrSpl[p]), tPSP = -1, tTPT = -1;

                        if (pPSq.Length == 2 && Char.IsNumber(pPSq[1])) tPSP = GetSquareFromChars(pPSq[0], pPSq[1]);
                        else if (ttiPT != 0) tTPT = ttiPT;
                        //else if (pPSq.StartsWith("verwandl"))
                        //{
                        //    p++;
                        //    continue;
                        //}

                        p += 2;
                        pPSq = pStrSpl[p];
                        if (pPSq.Length == 2 && Char.IsNumber(pPSq[1]))
                        {
                            if (tPSP != -1) pieceStartPos = tPSP;
                            if (tTPT != -1) pieceType = tTPT;
                            pieceEndpos = GetSquareFromChars(pPSq[0], pPSq[1]);
                        }

                        //break;
                    }
                }
            }
            else
            {
                pStr = pStr.Replace(" ", "");

                if (pStr.Length != 4 && pStr.Length != 6) return null;

                pieceStartPos = GetSquareFromChars(pStr[0], pStr[1]);
                pieceEndpos = GetSquareFromChars(pStr[2], pStr[3]);


                if (pStr.Length == 6)
                {
                    switch (pStr[5])
                    {
                        case 'q':
                            piecePromType = 5; break;
                        case 'd':
                            piecePromType = 5; break;

                        case 'r':
                            piecePromType = 4; break;
                        case 't':
                            piecePromType = 4; break;

                        case 'b':
                            piecePromType = 3; break;
                        case 'l':
                            piecePromType = 3; break;

                        case 'k':
                            piecePromType = 2; break;
                        case 'n':
                            piecePromType = 2; break;
                    }
                }
            }

            for (int i = 0; i < tMoves.Count; i++)
            {
                Move tM = tMoves[i];
                if ((pieceStartPos == -1 || pieceStartPos == tM.startPos) &&
                    (pieceEndpos == -1 || pieceEndpos == tM.endPos) &&
                    (pieceType == -1 || pieceType == tM.pieceType) &&
                    (piecePromType == -1 || piecePromType == tM.promotionType)) return tM;
            }

            return null;
        }

        private static Move? EngineTurn(IBoardManager pBoardManager, Move? pMove)
        {
            //if (pMove == null) return null; // Pretty much irrelevant, just to not get the warning

            if (!CURRENTLY_FIRST_TURN) ChangePanel(2, 0, false, CURRENTLY_WHITES_TURN);

            ulong tBB = pBoardManager.allPieceBitboard;
            Move? tbM = pBoardManager.ReturnNextMove(pMove, 100_000_000L);

            if (tbM == null) // Bot has lost
            {
                //ChangePanel(6, 0, false, true);
                return null;
            }
            //else if (pBoardManager.GameState(!CURRENTLY_WHITES_TURN) != 3) return null;
            else ChangePanel(7, 0, false, false);

            CalculateAndExecutePath(tBB, tbM);

            WaitUntilPinIsOne(4); // Wait until move has been done

            if (tbM.isPromotion)
            {
                ChangePanel(5, promTypeConversionArray[tbM.promotionType], false, false);
                WaitUntilPinIsOne(11);
            }

            return tbM;
        }

        public static Move? DiscordTurn(IBoardManager pBoardManager)
        {
            if (!CURRENTLY_FIRST_TURN) ChangePanel(2, 0, false, CURRENTLY_WHITES_TURN);

            try { 
                File.WriteAllText(PathManager.GetTXTPath("OTHER/DiscordMessages"), "");
            } catch (Exception) { }

            long tTimestamp = sw.ElapsedTicks;

            Move? tM = null;
            string tStr = "", tempSaveTStr = "";
            while (tM == null) {
                while (tStr == tempSaveTStr)
                {
                    WaitForTickCount(1_000_000L);
                    try
                    {
                        if ((CURRENTLY_WHITES_TURN ? WHITE_CHESS_CLOCK : BLACK_CHESS_CLOCK).curRemainingTime < sw.ElapsedTicks - tTimestamp)
                        {
                            (CURRENTLY_WHITES_TURN ? WHITE_CHESS_CLOCK : BLACK_CHESS_CLOCK).curRemainingTime = -1L;
                            return null;
                        }
                        tStr = File.ReadAllText(PathManager.GetTXTPath("OTHER/DiscordMessages"));
                    }
                    catch (Exception) { }
                }
                //for (int i = 0; i < tStr.Length; i++) // Eig nicht notwendig, da der DC-Bot sowieso immer nur die aktuelle Zeile beibehält
                //{
                tempSaveTStr = tStr;
                int tL = tStr.Length;
                Console.WriteLine(tStr);
                if (!(CURRENTLY_WHITES_TURN ? ARDUINO_GAME_SETTINGS.WHITE_DISCORD_IDS : ARDUINO_GAME_SETTINGS.BLACK_DISCORD_IDS).Contains(tStr.Split(':')[0]))
                    continue;

                for (int j = 0; j < tL; j++)
                {
                    char tCh = tStr[j];
                    if (tCh == ':')
                    {
                        string tMessage = tStr.Substring(j + 1);
                        tM = GetMoveFromDCMessage(tMessage, pBoardManager); // Überprüft auch direkt die Legimität des Zuges
                        Console.WriteLine(tM);
                        if (tM != null) break;
                    }
                }
            }

            (CURRENTLY_WHITES_TURN ? WHITE_CHESS_CLOCK : BLACK_CHESS_CLOCK).MoveFinished(sw.ElapsedTicks - tTimestamp);

            ChangePanel(7, 0, false, false);
            CalculateAndExecutePath(pBoardManager.allPieceBitboard, tM);
            WaitUntilPinIsOne(4); // Wait until move has been done

            if (tM.isPromotion)
            {
                ChangePanel(5, promTypeConversionArray[tM.promotionType], false, false);
                WaitUntilPinIsOne(11);
            }

            return tM;
        }

        private static Move? RealLifePlayerTurn(IBoardManager pMainBoardManager)
        {
            if (!CURRENTLY_FIRST_TURN) ChangePanel(2, 0, false, CURRENTLY_WHITES_TURN);

            int ttimeC = WaitUntilPinIsOne(9);
            (CURRENTLY_WHITES_TURN ? WHITE_CHESS_CLOCK : BLACK_CHESS_CLOCK).MoveFinished(ttimeC * 5_000_000L);
            if (!(CURRENTLY_WHITES_TURN ? WHITE_CHESS_CLOCK : BLACK_CHESS_CLOCK).HasTimeLeft()) return null;

            List<Move> tLegalMoves = new List<Move>();
            pMainBoardManager.GetLegalMoves(ref tLegalMoves);
            pMainBoardManager.SetJumpState();

        ReAnalysis:
            List<ulong> camAnlysisResult = STATIC_MAIN_CAMERA_ANALYSER.ANALYSE();
            Move? tM = null;
            int tC = tLegalMoves.Count;
            bool legalMoveFound = false;
            for (int i = 0; i < tC; i++)
            {
                tM = tLegalMoves[i];

                pMainBoardManager.LoadJumpState();
                pMainBoardManager.PlainMakeMove(tM);

                //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(MainBoardManager.allPieceBitboard));

                if (camAnlysisResult.Contains(pMainBoardManager.allPieceBitboard))
                {
                    if (tM.isPromotion)
                    {
                        ChangePanel(4, 0, false, CURRENTLY_WHITES_TURN);
                        WaitUntilPinIsOne(10);

                        long ps7 = SESSION.GetPinState(7).Value, ps8 = SESSION.GetPinState(8).Value;
                        int tpromType = 5 - (int)(ps7 | (ps8 << 1));

                        for (int j = 0; j < tC; j++)
                        {
                            pMainBoardManager.LoadJumpState();
                            tM = tLegalMoves[j];
                            pMainBoardManager.PlainMakeMove(tM);
                            if (camAnlysisResult.Contains(pMainBoardManager.allPieceBitboard) && tpromType == tM.promotionType)
                            {
                                legalMoveFound = true;
                                goto PromotionSkip;
                            }
                        }

                    }
                    else legalMoveFound = true;

                    break;
                }
            }

        PromotionSkip:
            pMainBoardManager.LoadJumpState();

            if (!legalMoveFound)
            {
                ChangePanel(3, 0, false, !CURRENTLY_WHITES_TURN);
                WaitForTickCount(10_000_000L);
                Console.WriteLine("!!!");
                WaitUntilPinIsOne(9);
                Console.WriteLine("!!!!");
                goto ReAnalysis;
            }

            Console.WriteLine("MOVE FOUND: " + tM);

            return tM;
        }

        private static void ChangePanel(int pPanelID, int pUniVal, bool pUniBool, bool pNextsWhiteTurn)
        {
            if (WHITE_CHESS_CLOCK == null || BLACK_CHESS_CLOCK == null) return;

            bool tHT = (CURRENTLY_WHITES_TURN ? ARDUINO_GAME_SETTINGS.WHITE_ENTITY : ARDUINO_GAME_SETTINGS.BLACK_ENTITY) == ARDUINO_GAME_SETTINGS.ENTITY_TYPE.PLAYER; //!(ARDUINO_GAME_SETTINGS.HUMAN_PLAYS_WHITE ^ pNextsWhiteTurn);
            ARDUINO_ACTION PANEL_CHANGE = new PANEL_CHANGE(pPanelID, pUniVal, pUniBool,
            (tHT, CURRENTLY_WHITES_TURN ^ ARDUINO_GAME_SETTINGS.WHITE_TIMER_TO_THE_RIGHT, (int)((CURRENTLY_WHITES_TURN ? WHITE_CHESS_CLOCK : BLACK_CHESS_CLOCK).curRemainingTime / 1000000))); // Auf Zehntel Sekunde genau
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

            ChangePanel(1, 0, false, false);
            WaitForTickCount(10_000_000L);
            ChangePanel(4, 0, false, false);

            Console.WriteLine("?!");

            ARDUINO_ACTION Y_DOWN = new STEPPER_MOTOR_TURN(Y_MOTOR, 177 * 5, 160, DOWN); // Unten
            ARDUINO_ACTION Y_UP = new STEPPER_MOTOR_TURN(Y_MOTOR, 177 * 5, 160, UP); // Oben
            ARDUINO_ACTION X_LEFT = new STEPPER_MOTOR_TURN(X_MOTOR, 180 * 7, 160, LEFT);
            ARDUINO_ACTION X_RIGHT = new STEPPER_MOTOR_TURN(X_MOTOR, 180 * 7, 160, RIGHT);
            ARDUINO_ACTION MAGNET_UP = new MAGNET_STATE_SET(true);
            ARDUINO_ACTION MAGNET_DOWN = new MAGNET_STATE_SET(false);
            
            
            //SESSION.CreateDigitalStateMonitor()
            
            //SESSION.RequestPinState(9);
            
            ExecuteActions(
                (MAGNET_UP, 200),
                (X_RIGHT, 200),
                (X_LEFT, 200),
                //(X_RIGHT, 200),
                //(X_LEFT, 200),
                (MAGNET_DOWN, 200)
            
            );

            Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(X_LEFT.VAL));
            
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

            CONNECTION.Close();
        }

        //En Passant Move Sequences are actually missing
        public static void CalculateAndExecutePath(ulong pBlockedSquares, Move pMove)
        {
            if (pMove.isRochade) CalculateAndExecuteRochadePath(pBlockedSquares, pMove);
            else if (pMove.isEnPassant) CalculateAndExecuteEnPassantPath(pBlockedSquares, pMove);
            else if (pMove.isCapture) CalculateAndExecuteCapturePath(pBlockedSquares, pMove.startPos, pMove.endPos);
            else CalculateAndExecutePath(pBlockedSquares, pMove.startPos, pMove.endPos);
        }

        public static void CalculateAndExecutePath(ulong pBlockedSquares, int pFrom, int pTo)
        {
            pFrom = pFrom % 8 * 8 + (pFrom - pFrom % 8) / 8;
            pTo = pTo % 8 * 8 + (pTo - pTo % 8) / 8;

            pBlockedSquares = ULONG_OPERATIONS.FlipBoard90Degress(pBlockedSquares);

            MagnetMoveSequence mms = MagnetMovePathfinder.CalculatePath(pBlockedSquares, pFrom, pTo, false);

            FinalPathCalcsAndExecutions();
        }

        public static void FinalPathCalcsAndExecutions()
        {
            List<(ARDUINO_ACTION, int)> tActions = new List<(ARDUINO_ACTION, int)>();
            bool tMagnetState = false;

            string outp = "";
            string[] DEUTSCHE_ANWEISUNGEN = new string[5] { "Oben", "Unten", "Links", "Rechts", "MAGNET" };
            int tC = 1, fC = 0;
            (int, bool) ll = (-1, false);
            Console.WriteLine(MagnetMovePathfinder.FINAL_ACTIONS.Count);
            for (int i = 0; i < MagnetMovePathfinder.FINAL_ACTIONS.Count; i++)
            {
                (int, bool) tAct = MagnetMovePathfinder.FINAL_ACTIONS[i];
                if (tAct == ll)
                {
                    tC++;
                }
                else if (i != 0)
                {
                    outp += tC + "x " + (ll.Item2 ? "Mag" : "") + DEUTSCHE_ANWEISUNGEN[ll.Item1] + ", ";
                    fC++;
                    AppendPathAction(ref tActions, ref tMagnetState, ll, tC);
                    tC = 1;
                }
                ll = tAct;
            }
            if (ll != (-1, false))
            {
                fC++;
                outp += tC + "x " + DEUTSCHE_ANWEISUNGEN[ll.Item1];
                AppendPathAction(ref tActions, ref tMagnetState, ll, tC);
            }

            Console.WriteLine("[" + fC + " Actions]  " + outp);
            Console.WriteLine(tActions.Count);

            ExecuteActions(tActions.ToArray());
        }

        public static void CalculateAndExecuteCapturePath(ulong pBlockedSquares, int pFrom, int pTo) // MISSING
        {
            // 1. 60 muss iwi so freiwerden, dass nicht der weg von pTo bis 60 blockiert wird 

            pFrom = pFrom % 8 * 8 + (pFrom - pFrom % 8) / 8;
            pTo = pTo % 8 * 8 + (pTo - pTo % 8) / 8;

            pBlockedSquares = ULONG_OPERATIONS.FlipBoard90Degress(pBlockedSquares);

            MagnetMoveSequence mms = MagnetMovePathfinder.CalculateCapturePath(pBlockedSquares, pFrom, pTo); // Feld 60 oder 59(60 ist das Höhere aus weißer Perspektive)

            FinalPathCalcsAndExecutions();
        }

        public static void CalculateAndExecuteEnPassantPath(ulong pBlockedSquares, Move pMove)// MISSING
        {
            int pFrom = SwapRowAndColumn(pMove.startPos), pTo = SwapRowAndColumn(pMove.endPos);
            int pEPSq = SwapRowAndColumn(pMove.enPassantOption);

            pBlockedSquares = ULONG_OPERATIONS.FlipBoard90Degress(pBlockedSquares);

            MagnetMoveSequence mms = MagnetMovePathfinder.CalculateEnPassantPath(pBlockedSquares, pFrom, pTo, pEPSq);

            FinalPathCalcsAndExecutions();


            // 1. Zu König ohne Magnet
            // 2. König 2-3 Felder
            // 3. Von dort aus: Turm Algorithmus (Turm Algorithmus von 0 bis zum ersten Magnet UP, folglich ohne Magnet bis dahin und dann die abfolge)
        }

        public static void CalculateAndExecuteRochadePath(ulong pBlockedSquares, Move pMove)// MISSING
        {
            int pFromROOK = SwapRowAndColumn(pMove.rochadeStartPos), pToROOK = SwapRowAndColumn(pMove.rochadeEndPos);
            int pFromKING = SwapRowAndColumn(pMove.startPos), pToKING = SwapRowAndColumn(pMove.endPos);

            pBlockedSquares = ULONG_OPERATIONS.FlipBoard90Degress(pBlockedSquares);

            MagnetMoveSequence mms = MagnetMovePathfinder.CalculateRochadePath(pBlockedSquares, pFromKING, pToKING, pFromROOK, pToROOK); // Feld 60 oder 59(60 ist das Höhere aus weißer Perspektive)

            FinalPathCalcsAndExecutions();


            // 1. Zu König ohne Magnet
            // 2. König 2-3 Felder
            // 3. Von dort aus: Turm Algorithmus (Turm Algorithmus von 0 bis zum ersten Magnet UP, folglich ohne Magnet bis dahin und dann die abfolge)
        }

        private static int SwapRowAndColumn(int pVal)
        {
            return pVal % 8 * 8 + (pVal - pVal % 8) / 8;
        }

        private static void AppendPathAction(ref List<(ARDUINO_ACTION, int)> pList, ref bool pMagnetState, (int, bool) pAction, int pCount)
        {
            const int tRPM = 160;
            const int tDelay = 200;

            if (pMagnetState != pAction.Item2)
            {
                //Console.WriteLine("=> " + pList.Count);

                pMagnetState = pAction.Item2;
                ARDUINO_ACTION MAGNET = new MAGNET_STATE_SET(pMagnetState);
                pList.Add((MAGNET, tDelay));
            }

            switch (pAction.Item1)
            {
                case 0: // Oben
                    ARDUINO_ACTION Y_UP = new STEPPER_MOTOR_TURN(Y_MOTOR, 177 * pCount, tRPM, UP);
                    pList.Add((Y_UP, tDelay));
                    break;
                case 1: // Unten
                    ARDUINO_ACTION Y_DOWN = new STEPPER_MOTOR_TURN(Y_MOTOR, 177 * pCount, tRPM, DOWN);
                    pList.Add((Y_DOWN, tDelay));
                    break;
                case 2: // Links
                    ARDUINO_ACTION X_LEFT = new STEPPER_MOTOR_TURN(X_MOTOR, 180 * pCount, tRPM, LEFT);
                    pList.Add((X_LEFT, tDelay));
                    break;
                case 3: // Rechts
                    ARDUINO_ACTION X_RIGHT = new STEPPER_MOTOR_TURN(X_MOTOR, 180 * pCount, tRPM, RIGHT);
                    pList.Add((X_RIGHT, tDelay));
                    break;
                //case 4: // Magnet
                //    ARDUINO_ACTION MAGNET = new MAGNET_STATE_SET(true);
                //    if (pMagnetState) MAGNET = new MAGNET_STATE_SET(false);
                //    pList.Add((MAGNET, tDelay));
                //    pMagnetState = !pMagnetState;
                //    break;
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
                bool b;
                if (b = pSteps > 1023) pSteps -= 1024;
                VAL = (b ? 4ul : 0ul) | (pMotorAxis ? 8ul : 0ul) | (pDir ? 4096ul : 0ul) | ((ulong)pRPM << 13) | ((ulong)pSteps << 21);
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
