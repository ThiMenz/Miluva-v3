using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Timers;

#pragma warning disable CS8618
#pragma warning disable CS8602
#pragma warning disable CS8600
#pragma warning disable CS8622

namespace ChessBot
{
    public static class ENGINE_VALS
    {
        public const int BESTMOVE_SORT_VAL = -2_000_000_000;
        public const int CAPTURE_SORT_VAL = -1_000_000_000;
        public const int KILLERMOVE_SORT_VAL = -1000;

        public const int CPU_CORES = 16;
        public const int PARALLEL_BOARDS = 32;
        public const int SELF_PLAY_THINK_TIME = 120_000;

        public static readonly int[,] MVVLVA_TABLE = new int[7, 7] {
            { 0, 0, 0, 0, 0, 0, 0 },  // Nichts
            { 0, 1500, 1400, 1300, 1200, 1100, 1000 },  // Bauern
            { 0, 3500, 3400, 3300, 3200, 3100, 3000 },  // Springer
            { 0, 4500, 4400, 4300, 4200, 4100, 4000 },  // Läufer
            { 0, 5500, 5400, 5300, 5200, 5100, 5000 },  // Türme
            { 0, 9500, 9400, 9300, 9200, 9100, 9000 },  // Dame
            { 0, 0, 0, 0, 0, 0, 0 }}; // König
    }

    public static class BOT_MAIN
    {
        public readonly static string[] FIGURE_TO_ID_LIST = new string[] { "Nichts", "Bauer", "Springer", "Läufer", "Turm", "Dame", "König" };

        public static bool isFirstBoardManagerInitialized = false;
        public static BoardManager[] boardManagers = new BoardManager[ENGINE_VALS.PARALLEL_BOARDS];

        public static List<string> selfPlayGameStrings = new List<string>();
        public static int gamesPlayed = 0;
        public static int[] gamesPlayedResultArray = new int[3];
        public static int movesPlayed = 0;
        public static int depthsSearched = 0;
        public static long evaluationsMade = 0;
        public static int searchesFinished = 0;
        public static int goalGameCount = 1_000;

        public static void Main(string[] args)
        {
            SQUARES.Init();
            FEN_MANAGER.Init();
            NuCRe.Init();
            ULONG_OPERATIONS.SetUpCountingArray();

            SetupParallelBoards();

            //boardManagers[7].TempStuff();
            //CGFF.InterpretateLine(File.ReadAllLines(Path.GetFullPath("SELF_PLAY_GAMES.txt").Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", ""))[0]);
            //CGFF.InterpretateLine("r1br2k1/p3qpp1/1pn1p2p/2p5/3P4/1BPQPN2/P4PPP/R4RK1 w - - 2 15;Ñ8,ù:,tq,KI,2W,[[,Èa,ĥ7,ĘG,ëW,58,·|,ÞK,ľc,n6,°v,KN,Á²,ąa,Øt,0");
            //SetupParallelBoards();
            MEM_SelfPlay();
        }

        private static void SetupParallelBoards()
        {
            for (int i = 0; i < ENGINE_VALS.PARALLEL_BOARDS; i++)
            {
                boardManagers[i] = new BoardManager("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
                Console.Write((i + 1) + ", ");
                isFirstBoardManagerInitialized = true;
            }
            Console.WriteLine("\n\n");
        }

        private static void MEM_SelfPlay()
        {
            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < ENGINE_VALS.CPU_CORES; i++)
            {
                ThreadPool.QueueUserWorkItem(
                    new WaitCallback(boardManagers[i].ThreadSelfPlay)
                );
            }

            while (gamesPlayed < goalGameCount) Thread.Sleep(1000);

            sw.Stop();

            string tSave;

            Console.WriteLine(tSave = "Time in ms: " + GetThreeDigitSeperatedInteger((int)sw.ElapsedMilliseconds) + " | Time Constraint: " + ENGINE_VALS.SELF_PLAY_THINK_TIME
            + " | Games Played: " + gamesPlayed + " | Moves Played: " + movesPlayed + " | Depths Searched: " + depthsSearched + " | Evaluations Made: " + evaluationsMade);

            double ttime = (sw.ElapsedTicks / 10_000_000d);
            double GpS = gamesPlayed / ttime;
            double MpS = movesPlayed / ttime;
            double MpG = movesPlayed / (double)gamesPlayed;
            double EpSec = evaluationsMade / ttime;
            double EpSrch = evaluationsMade / (double)searchesFinished;
            double DpS = depthsSearched / (double)searchesFinished;
            double DrawPrecentage = gamesPlayedResultArray[1] * 100d / gamesPlayed;
            double WhiteWinPrecentage = gamesPlayedResultArray[2] * 100d / gamesPlayed;
            double BlackWinPrecentage = gamesPlayedResultArray[0] * 100d / gamesPlayed;

            Console.WriteLine("\n===\n");
            tSave += " | Games Per Second: " + GpS;
            Console.WriteLine("| Games Per Second: " + GpS);
            tSave += " | Moves Per Second: " + MpS;
            Console.WriteLine("| Moves Per Second: " + MpS);
            tSave += " | Moves Per Game: " + MpG;
            Console.WriteLine("| Moves Per Game: " + MpG);
            tSave += " | Depths Per Search: " + DpS;
            Console.WriteLine("| Depths Per Search: " + DpS);
            tSave += " | Evaluations Per Second: " + EpSec;
            Console.WriteLine("| Evaluations Per Second: " + EpSec);
            tSave += " | Evaluations Per Search: " + EpSrch;
            Console.WriteLine("| Evaluations Per Search: " + EpSrch);
            Console.WriteLine("\n===\n");
            tSave += " | White Win%: " + WhiteWinPrecentage;
            Console.WriteLine("| White Win%: " + WhiteWinPrecentage);
            tSave += " | Draw%: : " + DrawPrecentage;
            Console.WriteLine("| Draw%: : " + DrawPrecentage);
            tSave += " | Black Win%: " + BlackWinPrecentage;
            Console.WriteLine("| Black Win%: " + BlackWinPrecentage);

            string tPath = Path.GetFullPath("SELF_PLAY_GAMES.txt").Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", "");
            selfPlayGameStrings.Add(tSave);
            File.AppendAllLines(tPath, selfPlayGameStrings.ToArray());
        }

        private static string GetThreeDigitSeperatedInteger(int pInt)
        {
            string s = pInt.ToString(), r = s[0].ToString();
            int t = s.Length % 3;

            for (int i = 1; i < s.Length; i++)
            {
                if (i % 3 == t) r += ".";
                r += s[i];
            }

            s = "";
            for (int i = 0; i < r.Length; i++) s += r[i];

            return s;
        }
    }

    public class BoardManager
    {
        #region | VARIABLES |

        private bool debugSearchDepthResults = false;
        private bool debugSortResults = false;

        private RookMovement rookMovement;
        private BishopMovement bishopMovement;
        private QueenMovement queenMovement;
        private KnightMovement knightMovement;
        private KingMovement kingMovement;
        private WhitePawnMovement whitePawnMovement;
        private BlackPawnMovement blackPawnMovement;
        private Rays rays;
        public List<Move> moveOptionList;

        private int[] pieceTypeArray = new int[64];
        public ulong whitePieceBitboard, blackPieceBitboard, allPieceBitboard;
        private ulong zobristKey;
        private int whiteKingSquare, blackKingSquare, enPassantSquare = 65, happenedHalfMoves = 0, fiftyMoveRuleCounter = 0;
        private bool whiteCastleRightKingSide, whiteCastleRightQueenSide, blackCastleRightKingSide, blackCastleRightQueenSide;
        private bool isWhiteToMove;

        private const ulong WHITE_KING_ROCHADE = 96, WHITE_QUEEN_ROCHADE = 14, BLACK_KING_ROCHADE = 6917529027641081856, BLACK_QUEEN_ROCHADE = 1008806316530991104, WHITE_QUEEN_ATTK_ROCHADE = 12, BLACK_QUEEN_ATTK_ROCHADE = 864691128455135232;
        private readonly Move mWHITE_KING_ROCHADE = new Move(4, 6, 7, 5), mWHITE_QUEEN_ROCHADE = new Move(4, 2, 0, 3),
            mBLACK_KING_ROCHADE = new Move(60, 62, 63, 61), mBLACK_QUEEN_ROCHADE = new Move(60, 58, 56, 59);

        private ulong[] knightSquareBitboards = new ulong[64];
        private ulong[] whitePawnAttackSquareBitboards = new ulong[64];
        private ulong[] blackPawnAttackSquareBitboards = new ulong[64];
        private ulong[] kingSquareBitboards = new ulong[64];

        private bool[,] pieceTypeAbilities = new bool[7, 3] 
        {
            { false, false, false }, // Null
            { false, false, false }, // Bauer
            { false, false, false }, // Springer 
            { false, false, true }, // Läufer
            { false, true, false }, // Turm
            { false, true, true }, // Dame
            { false, false, false }  // König
        };

        private ulong[] curSearchZobristKeyLine; //Die History muss bis zum letzten Capture, PawnMove, der letzten Rochade gehen oder dem Spielbeginn gehen

        //private Move[] debugMoveList = new Move[128];

        private Stopwatch globalTimer = Stopwatch.StartNew();
        private Move lastMadeMove;
        private int depths, searches;

        private Random globalRandom = new Random();

        #endregion

        public BoardManager(string fen)
        {
            Setup(fen);
        }

        #region | SETUP |

        public void Setup(string pFen)
        {
            Stopwatch setupStopwatch = Stopwatch.StartNew();

            for (int i = 0; i < 33; i++)
                for (int j = 0; j < 14; j++)
                    piecePositionEvals[i, j] = new int[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            PrecalculateMultipliers();
            GetLowNoisePositionalEvaluation(globalRandom);
            MinimaxRoot(1L); // Minimax Preloader

            SetupConsoleWrite("[PRECALCS] Zobrist Hashing");
            InitZobrist();
            SetupConsoleWriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            SetupConsoleWrite("[PRECALCS] Knight Movement");
            knightMovement = new KnightMovement(this);
            SetupConsoleWriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            SetupConsoleWrite("[PRECALCS] Square Connectives");
            SquareConnectivesPrecalculations();
            SetupConsoleWriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            SetupConsoleWrite("[PRECALCS] Pawn Attack Bitboards");
            PawnAttackBitboards();
            SetupConsoleWriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            SetupConsoleWrite("[PRECALCS] Rays");
            rays = new Rays();
            SetupConsoleWriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            queenMovement = new QueenMovement(this);
            SetupConsoleWrite("[PRECALCS] Rook Movement");
            rookMovement = new RookMovement(this, queenMovement);
            SetupConsoleWriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            SetupConsoleWrite("[PRECALCS] Bishop Movement");
            bishopMovement = new BishopMovement(this, queenMovement);
            SetupConsoleWriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            SetupConsoleWrite("[PRECALCS] King Movement");
            kingMovement = new KingMovement(this);
            SetupConsoleWriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            SetupConsoleWrite("[PRECALCS] Pawn Movement");
            whitePawnMovement = new WhitePawnMovement(this);
            blackPawnMovement = new BlackPawnMovement(this);
            PrecalculateEnPassantMoves();
            SetupConsoleWriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");
            SetupConsoleWriteLine("[DONE]\n\n");

            LoadFenString(pFen);
            setupStopwatch.Stop();
        }

        private void SetupConsoleWriteLine(string pStr)
        {
            if (BOT_MAIN.isFirstBoardManagerInitialized) return;
            Console.WriteLine(pStr);
        }

        private void SetupConsoleWrite(string pStr)
        {
            if (BOT_MAIN.isFirstBoardManagerInitialized) return;
            Console.Write(pStr);
        }

        public void GetLowNoisePositionalEvaluation(System.Random rng)
        {
            int[,][] digitArray = new int[3, 6][];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 6; j++)
                    digitArray[i, j] = GetRand64IntArray(rng, -5, 6);
            lowNoisePositionEvals1 = GetInterpolatedProcessedValues(digitArray);
            int[,][] digitArray2 = new int[3, 6][];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 6; j++)
                    digitArray2[i, j] = GetRand64IntArray(rng, -5, 6);
            lowNoisePositionEvals2 = GetInterpolatedProcessedValues(digitArray2);
        }

        private int[] GetRand64IntArray(System.Random rng, int pMin, int pMax)
        {
            return new int[64] {
                rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax),
                rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax),
                rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax),
                rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax),
                rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax),
                rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax),
                rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax),
                rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax), rng.Next(pMin, pMax)
            };
        }

        private string GetAIArrayValuesStringRepresentation(int[,][] pArr)
        {
            string r = "int[,][] ReLe_AI_RESULT_VALS = new int[3, 6][] {";

            for (int i = 0; i < 3; i++)
            {
                r += "{";
                for (int j = 0; j < 5; j++)
                {
                    r += GetIntArray64LStringRepresentation(pArr[i, j]) + ",";
                }
                r += GetIntArray64LStringRepresentation(pArr[i, 5]) + "},";
            }

            return r.Substring(0, r.Length - 1) + "};";
        }

        private string GetIntArray64LStringRepresentation(int[] p64LArr)
        {
            if (p64LArr == null) return "";
            string r = "new int[64]{";
            for (int i = 0; i < 63; i++)
            {
                r += p64LArr[i] + ",";
            }
            return r + p64LArr[63] + "}";
        }

        #endregion

        #region | PLAYING |

        public void TempStuff()
        {
            LoadFenString("r2q1rk1/1p1n1ppp/p3p3/3pP3/Pb1P2b1/3B1N2/1P2QPPP/R1B2RK1 b - - 1 14");
            debugSearchDepthResults = true;
            MinimaxRoot(30_000_000L);
        }

        private string[] gameResultStrings = new string[5]
        {
            "Black Has Won!",
            "Draw!",
            "White Has Won!",
            "Non Existant Result!",
            "Game is Ongoing!"
        };
        private int[,][] lowNoisePositionEvals1, lowNoisePositionEvals2;

        public void ThreadSelfPlay(object obj)
        {
            try {
                PlayGameAgainstItself(obj);
            } catch(Exception tE) {
                Console.WriteLine(tE.ToString());
            }
        }

        public void PlayGameAgainstItself(object obj, string pStartFEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", long tickLimitForEngine = ENGINE_VALS.SELF_PLAY_THINK_TIME)
        {
            int ttGState; // Aktuell nicht wirklich benötigt, trotzdem manchmal gut nutzbar
            while (BOT_MAIN.goalGameCount > BOT_MAIN.gamesPlayed) {
                string tGameStr;
                LoadFenString(tGameStr = FEN_MANAGER.GetRandomStartFEN());
                tGameStr += ";";
                int tGState = 3, tmc = 0;
                bool lowNoiceDecider = globalRandom.NextDouble() < 0.5d;
                while (tGState == 3)
                {
                    piecePositionEvals = lowNoiceDecider ? lowNoisePositionEvals1 : lowNoisePositionEvals2;
                    MinimaxRoot(tickLimitForEngine);
                    Move tM = transpositionTable[zobristKey].bestMove;
                    PlainMakeMove(tM);
                    tGameStr += NuCRe.GetNuCRe(tM.moveHash) + ",";
                    BOT_MAIN.movesPlayed++;
                    tGState = GameState(isWhiteToMove);
                    lowNoiceDecider = !lowNoiceDecider;
                    tmc++;
                }
                ttGState = tGState;
                tGameStr += ttGState + 1;
                Console.WriteLine(tGameStr);
                BOT_MAIN.gamesPlayedResultArray[ttGState + 1]++;
                BOT_MAIN.gamesPlayed++;
                BOT_MAIN.selfPlayGameStrings.Add(tGameStr);
            }
        }

        public void PlayGameOnConsoleAgainstHuman(string pStartFEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", bool pHumanPlaysWhite = true, long tickLimitForEngine = 10_000_000L)
        {
            debugSearchDepthResults = true; 

            LoadFenString(pStartFEN);

            if (!(pHumanPlaysWhite ^ isWhiteToMove)) {
                var pVS = Console.ReadLine();
                if (pVS == null) return;
                Move tm = GetMoveOfString(pVS.ToString());
                Console.WriteLine(tm);
                PlainMakeMove(tm);
            }

            int tGState = 3;
            while (tGState == 3) {
                MinimaxRoot(tickLimitForEngine);
                PlayThroughZobristTree();
                Move tM = transpositionTable[zobristKey].bestMove;
                Console.WriteLine(tM);
                Console.WriteLine(CreateFenString());
                PlainMakeMove(tM);
                tGState = GameState(pHumanPlaysWhite);
                if (tGState != 3) break;
                var pV = "";
                tM = NULL_MOVE;
                do {
                    pV = Console.ReadLine();
                    if (pV == null) continue;
                    tM = GetMoveOfString(pV.ToString());
                    Console.WriteLine(tM);
                } while (tM == NULL_MOVE);
                PlainMakeMove(tM);
                tGState = GameState(!pHumanPlaysWhite);
            }
        }

        private void PlayThroughZobristTree()
        {
            SetJumpState();
            Console.WriteLine("\n- - - - - - - - - - - - -");
            int tC = 1;
            while (transpositionTable.TryGetValue(zobristKey, out TranspositionEntry tM))
            {
                Console.WriteLine(tC + ": " + tM.bestMove + " > ");
                PlainMakeMove(tM.bestMove);
                tC++;
            }
            Console.WriteLine("- - - - - - - - - - - - -\n");
            LoadJumpState();
        }

        #endregion

        #region | JUMP STATE |

        private int[] jmpSt_pieceTypeArray;
        private ulong jmpSt_whitePieceBitboard, jmpSt_blackPieceBitboard;
        private ulong jmpSt_zobristKey;
        private int jmpSt_whiteKingSquare, jmpSt_blackKingSquare, jmpSt_enPassantSquare, jmpSt_happenedHalfMoves, jmpSt_fiftyMoveRuleCounter;
        private bool jmpSt_whiteCastleRightKingSide, jmpSt_whiteCastleRightQueenSide, jmpSt_blackCastleRightKingSide, jmpSt_blackCastleRightQueenSide;
        private bool jmpSt_isWhiteToMove;
        private ulong[] jmpSt_curSearchZobristKeyLine;

        public void LoadJumpState()
        {
            pieceTypeArray = jmpSt_pieceTypeArray;
            whitePieceBitboard = jmpSt_whitePieceBitboard;
            blackPieceBitboard = jmpSt_blackPieceBitboard;
            zobristKey = jmpSt_zobristKey;
            whiteKingSquare = jmpSt_whiteKingSquare;
            blackKingSquare = jmpSt_blackKingSquare;
            enPassantSquare = jmpSt_enPassantSquare;
            happenedHalfMoves = jmpSt_happenedHalfMoves;
            fiftyMoveRuleCounter = jmpSt_fiftyMoveRuleCounter;
            whiteCastleRightKingSide = jmpSt_whiteCastleRightKingSide;
            whiteCastleRightQueenSide = jmpSt_whiteCastleRightQueenSide;
            blackCastleRightKingSide = jmpSt_blackCastleRightKingSide;
            blackCastleRightQueenSide = jmpSt_blackCastleRightQueenSide;
            isWhiteToMove = jmpSt_isWhiteToMove;
            curSearchZobristKeyLine = jmpSt_curSearchZobristKeyLine;
            allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
        }

        public void SetJumpState()
        {
            jmpSt_pieceTypeArray = (int[])pieceTypeArray.Clone();
            jmpSt_whitePieceBitboard = whitePieceBitboard;
            jmpSt_blackPieceBitboard = blackPieceBitboard;
            jmpSt_zobristKey = zobristKey;
            jmpSt_whiteKingSquare = whiteKingSquare;
            jmpSt_blackKingSquare = blackKingSquare;
            jmpSt_enPassantSquare = enPassantSquare;
            jmpSt_happenedHalfMoves = happenedHalfMoves;
            jmpSt_fiftyMoveRuleCounter = fiftyMoveRuleCounter;
            jmpSt_whiteCastleRightKingSide = whiteCastleRightKingSide;
            jmpSt_whiteCastleRightQueenSide = whiteCastleRightQueenSide;
            jmpSt_blackCastleRightKingSide = blackCastleRightKingSide;
            jmpSt_blackCastleRightQueenSide = blackCastleRightQueenSide;
            jmpSt_isWhiteToMove = isWhiteToMove;
            jmpSt_curSearchZobristKeyLine = (ulong[])curSearchZobristKeyLine.Clone();
        }

        #endregion

        #region | CHECK RECOGNITION |

        private int PreMinimaxCheckCheckWhite()
        {
            ulong bitShiftedKingPos = 1ul << whiteKingSquare;
            for (int p = 0; p < 64; p++)
            {
                if (ULONG_OPERATIONS.IsBitZero(blackPieceBitboard, p)) continue;
                int tPT;
                switch (tPT = pieceTypeArray[p])
                {
                    case 1:
                        if ((bitShiftedKingPos & blackPawnAttackSquareBitboards[p]) != 0ul) return p;
                        break;
                    case 2:
                        if ((bitShiftedKingPos & knightSquareBitboards[p]) != 0ul) return p;
                        break;
                    case 6: break;
                    default:
                        if ((squareConnectivesPrecalculationLineArray[whiteKingSquare << 6 | p] & allPieceBitboard) == (1ul << p) && pieceTypeAbilities[tPT, squareConnectivesPrecalculationArray[whiteKingSquare << 6 | p]]) return p;
                        break;
                }
            }

            return -1;
        }

        private int PreMinimaxCheckCheckBlack()
        {
            ulong bitShiftedKingPos = 1ul << blackKingSquare;
            for (int p = 0; p < 64; p++)
            {
                if (ULONG_OPERATIONS.IsBitZero(whitePieceBitboard, p)) continue;
                int tPT;
                switch (tPT = pieceTypeArray[p])
                {
                    case 1:
                        if ((bitShiftedKingPos & whitePawnAttackSquareBitboards[p]) != 0ul) return p;
                        break;
                    case 2:
                        if ((bitShiftedKingPos & knightSquareBitboards[p]) != 0ul) return p;
                        break;
                    case 6: break;
                    default:
                        if ((squareConnectivesPrecalculationLineArray[blackKingSquare << 6 | p] & allPieceBitboard) == (1ul << p) && pieceTypeAbilities[tPT, squareConnectivesPrecalculationArray[blackKingSquare << 6 | p]]) return p;
                        break;
                }
            }

            return -1;
        }

        private int LecacyLeafCheckingPieceCheckWhite(int pStartPos, int pEndPos, int pPieceType)
        {
            int tI, tPossibleAttackPiece;
            if (pPieceType == 1)
            {
                if (ULONG_OPERATIONS.IsBitOne(blackPawnAttackSquareBitboards[pEndPos], whiteKingSquare)) return pEndPos;
            }
            else if (pPieceType == 2)
            {
                if (ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[pEndPos], whiteKingSquare)) return pEndPos;
            }
            else if (pPieceType != 6) {
                tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | pEndPos] & allPieceBitboard];
                if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) return tPossibleAttackPiece;
            }
            tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | pStartPos] & allPieceBitboard];
            if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) return tPossibleAttackPiece;
            return -1;
        }

        private int LecacyLeafCheckingPieceCheckBlack(int pStartPos, int pEndPos, int pPieceType)
        {
            int tI, tPossibleAttackPiece;
            if (pPieceType == 1)
            {
                if (ULONG_OPERATIONS.IsBitOne(whitePawnAttackSquareBitboards[pEndPos], blackKingSquare)) return pEndPos;
            }
            else if (pPieceType == 2)
            {
                if (ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[pEndPos], blackKingSquare)) return pEndPos;
            }
            else if (pPieceType != 6)
            {
                tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | pEndPos] & allPieceBitboard];
                if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) return tPossibleAttackPiece;
            }
            tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | pStartPos] & allPieceBitboard];
            if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) return tPossibleAttackPiece;
            return -1;
        }

        #endregion

        #region | LEGAL MOVE GENERATION |

        private void GetLegalWhiteMoves(int pCheckingPieceSquare, ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppDiagonalSliderVision = 0ul, oppStraightSliderVision = 0ul, oppStaticPieceVision = 0ul, oppAttkBitboard, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if (((int)(blackPieceBitboard >> p) & 1) == 0) continue;
                switch (pieceTypeArray[p])
                {
                    case 1:
                        oppStaticPieceVision |= blackPawnAttackSquareBitboards[p];
                        break;
                    case 2:
                        oppStaticPieceVision |= knightSquareBitboards[p];
                        break;
                    case 3:
                        rays.DiagonalRays(allPieceBitboard, p, whiteKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        break;
                    case 4:
                        rays.StraightRays(allPieceBitboard, p, whiteKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 5:
                        rays.DiagonalRays(allPieceBitboard, p, whiteKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        rays.StraightRays(allPieceBitboard, p, whiteKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 6:
                        oppStaticPieceVision |= kingSquareBitboards[p];
                        break;
                }
            }

            oppAttkBitboard = oppDiagonalSliderVision | oppStraightSliderVision | oppStaticPieceVision;
            int curCheckCount = ULONG_OPERATIONS.TrippleIsBitOne(oppDiagonalSliderVision, oppStaticPieceVision, oppStraightSliderVision, whiteKingSquare);

            if (curCheckCount == 0)
            {
                if (whiteCastleRightKingSide && ((allPieceBitboard | oppAttkBitboard) & WHITE_KING_ROCHADE) == 0ul) pMoveList.Add(mWHITE_KING_ROCHADE);
                if (whiteCastleRightQueenSide && (allPieceBitboard & WHITE_QUEEN_ROCHADE | oppAttkBitboard & WHITE_QUEEN_ATTK_ROCHADE) == 0ul) pMoveList.Add(mWHITE_QUEEN_ROCHADE);

                if (enPassantSquare != 65 && (whitePieceBitboard & blackPawnAttackSquareBitboards[enPassantSquare]) != 0ul)
                {
                    int shiftedKS = whiteKingSquare << 6, epM9 = enPassantSquare - 9, epM8 = epM9 + 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (((int)(whitePieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 0)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(blackPieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(blackPieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 += 2;
                    if (((int)(whitePieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 7)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(blackPieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(blackPieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (((int)(whitePieceBitboard >> p) & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (((int)(pinnedPieces >> p) & 1) == 0) whitePawnMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, blackPieceBitboard);
                            else whitePawnMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, whitePieceBitboard, blackPieceBitboard);
                            break;
                        case 2:
                            if (((int)(pinnedPieces >> p) & 1) == 0) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, blackPieceBitboard);
                            break;
                        case 3:
                            if (((int)(pinnedPieces >> p) & 1) == 0) bishopMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (((int)(pinnedPieces >> p) & 1) == 0) rookMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (((int)(pinnedPieces >> p) & 1) == 0) queenMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveList(p, oppAttkBitboard | whitePieceBitboard, ~oppAttkBitboard & blackPieceBitboard);
                            break;
                    }
                }
            }
            else if (curCheckCount == 1)
            {
                //Console.WriteLine(CreateFenString());
                ulong tCheckingPieceLine = squareConnectivesPrecalculationLineArray[whiteKingSquare << 6 | pCheckingPieceSquare];
                if (enPassantSquare != 65 && (whitePieceBitboard & blackPawnAttackSquareBitboards[enPassantSquare]) != 0ul && (((int)(tCheckingPieceLine >> enPassantSquare) & 1) == 1 || (pCheckingPieceSquare + 8 == enPassantSquare && pieceTypeArray[pCheckingPieceSquare] == 1)))
                {
                    int shiftedKS = whiteKingSquare << 6, epM9 = enPassantSquare - 9, epM8 = epM9 + 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (((int)(whitePieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(blackPieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(blackPieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 += 2;
                    if (((int)(whitePieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(blackPieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(blackPieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (((int)(whitePieceBitboard >> p) & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (((int)(pinnedPieces >> p) & 1) == 0) whitePawnMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, blackPieceBitboard);
                            else whitePawnMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, whitePieceBitboard, blackPieceBitboard);
                            break;
                        case 2:
                            if (((int)(pinnedPieces >> p) & 1) == 0) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, blackPieceBitboard);
                            break;
                        case 3:
                            if (((int)(pinnedPieces >> p) & 1) == 0) bishopMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (((int)(pinnedPieces >> p) & 1) == 0) rookMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (((int)(pinnedPieces >> p) & 1) == 0) queenMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveList(p, oppAttkBitboard | whitePieceBitboard, ~oppAttkBitboard & blackPieceBitboard);
                            break;
                    }
                }
                int s_molc = pMoveList.Count;
                List<Move> tMoves = new List<Move>();
                for (int m = 0; m < s_molc; m++)
                {
                    Move mm = pMoveList[m];
                    if (mm.pieceType == 6) tMoves.Add(pMoveList[m]);
                    else if (((int)(tCheckingPieceLine >> pMoveList[m].endPos) & 1) == 1 || mm.isEnPassant) tMoves.Add(pMoveList[m]);
                }
                pMoveList = tMoves;
            }
            else kingMovement.AddMoveOptionsToMoveList(whiteKingSquare, oppAttkBitboard | whitePieceBitboard, ~oppAttkBitboard & blackPieceBitboard);
        }

        private void GetLegalWhiteCaptures(int pCheckingPieceSquare, ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppDiagonalSliderVision = 0ul, oppStraightSliderVision = 0ul, oppStaticPieceVision = 0ul, oppAttkBitboard, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if (((int)(blackPieceBitboard >> p) & 1) == 0) continue;
                switch (pieceTypeArray[p])
                {
                    case 1:
                        oppStaticPieceVision |= blackPawnAttackSquareBitboards[p];
                        break;
                    case 2:
                        oppStaticPieceVision |= knightSquareBitboards[p];
                        break;
                    case 3:
                        rays.DiagonalRays(allPieceBitboard, p, whiteKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        break;
                    case 4:
                        rays.StraightRays(allPieceBitboard, p, whiteKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 5:
                        rays.DiagonalRays(allPieceBitboard, p, whiteKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        rays.StraightRays(allPieceBitboard, p, whiteKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 6:
                        oppStaticPieceVision |= kingSquareBitboards[p];
                        break;
                }
            }

            oppAttkBitboard = oppDiagonalSliderVision | oppStraightSliderVision | oppStaticPieceVision;
            int curCheckCount = ULONG_OPERATIONS.TrippleIsBitOne(oppDiagonalSliderVision, oppStaticPieceVision, oppStraightSliderVision, whiteKingSquare);

            if (curCheckCount == 0)
            {
                if (enPassantSquare != 65 && (whitePieceBitboard & blackPawnAttackSquareBitboards[enPassantSquare]) != 0ul)
                {
                    int shiftedKS = whiteKingSquare << 6, epM9 = enPassantSquare - 9, epM8 = epM9 + 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (((int)(whitePieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 0)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(blackPieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(blackPieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 += 2;
                    if (((int)(whitePieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 7)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(blackPieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(blackPieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (((int)(whitePieceBitboard >> p) & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (((int)(pinnedPieces >> p) & 1) == 0) whitePawnMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackPieceBitboard);
                            else whitePawnMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whiteKingSquare, blackPieceBitboard);
                            break;
                        case 2:
                            if (((int)(pinnedPieces >> p) & 1) == 0) knightMovement.AddMovesToMoveOptionListOnlyCaptures(p, blackPieceBitboard);
                            break;
                        case 3:
                            if (((int)(pinnedPieces >> p) & 1) == 0) bishopMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackPieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (((int)(pinnedPieces >> p) & 1) == 0) rookMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackPieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (((int)(pinnedPieces >> p) & 1) == 0) queenMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackPieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveListOnlyCaptures(p, ~oppAttkBitboard & blackPieceBitboard);
                            break;
                    }
                }
            }
            else if (curCheckCount == 1)
            {
                ulong tCheckingPieceLine = squareConnectivesPrecalculationLineArray[whiteKingSquare << 6 | pCheckingPieceSquare];
                if (enPassantSquare != 65 && (whitePieceBitboard & blackPawnAttackSquareBitboards[enPassantSquare]) != 0ul && (((int)(tCheckingPieceLine >> enPassantSquare) & 1) == 1 || (pCheckingPieceSquare + 8 == enPassantSquare && pieceTypeArray[pCheckingPieceSquare] == 1)))
                {
                    int shiftedKS = whiteKingSquare << 6, epM9 = enPassantSquare - 9, epM8 = epM9 + 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (((int)(whitePieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(blackPieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(blackPieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 += 2;
                    if (((int)(whitePieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(blackPieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(blackPieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (((int)(whitePieceBitboard >> p) & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (((int)(pinnedPieces >> p) & 1) == 0) whitePawnMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackPieceBitboard);
                            else whitePawnMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whiteKingSquare, blackPieceBitboard);
                            break;
                        case 2:
                            if (((int)(pinnedPieces >> p) & 1) == 0) knightMovement.AddMovesToMoveOptionListOnlyCaptures(p, blackPieceBitboard);
                            break;
                        case 3:
                            if (((int)(pinnedPieces >> p) & 1) == 0) bishopMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackPieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (((int)(pinnedPieces >> p) & 1) == 0) rookMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackPieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (((int)(pinnedPieces >> p) & 1) == 0) queenMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackPieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveListOnlyCaptures(p, ~oppAttkBitboard & blackPieceBitboard);
                            break;
                    }
                }
                int s_molc = pMoveList.Count;
                List<Move> tMoves = new List<Move>();
                for (int m = 0; m < s_molc; m++)
                {
                    Move mm = pMoveList[m];
                    if (mm.pieceType == 6) tMoves.Add(pMoveList[m]);
                    else if (((int)(tCheckingPieceLine >> pMoveList[m].endPos) & 1) == 1 || mm.isEnPassant) tMoves.Add(pMoveList[m]);
                }
                pMoveList = tMoves;
            }
            else kingMovement.AddMoveOptionsToMoveListOnlyCaptures(whiteKingSquare, ~oppAttkBitboard & blackPieceBitboard);
        }

        private void GetLegalWhiteMovesSpecialDoubleCheckCase(ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppAttkBitboard = 0ul, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if (((int)(blackPieceBitboard >> p) & 1) == 0) continue;
                switch (pieceTypeArray[p])
                {
                    case 1:
                        oppAttkBitboard |= blackPawnAttackSquareBitboards[p];
                        break;
                    case 2:
                        oppAttkBitboard |= knightSquareBitboards[p];
                        break;
                    case 3:
                        rays.DiagonalRays(allPieceBitboard, p, whiteKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        break;
                    case 4:
                        rays.StraightRays(allPieceBitboard, p, whiteKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        break;
                    case 5:
                        rays.DiagonalRays(allPieceBitboard, p, whiteKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        rays.StraightRays(allPieceBitboard, p, whiteKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        break;
                    case 6:
                        oppAttkBitboard |= kingSquareBitboards[p];
                        break;
                }
            }
            kingMovement.AddMoveOptionsToMoveList(whiteKingSquare, oppAttkBitboard | whitePieceBitboard, ~oppAttkBitboard & blackPieceBitboard);
        }

        private void GetLegalWhiteCapturesSpecialDoubleCheckCase(ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppAttkBitboard = 0ul, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if (((int)(blackPieceBitboard >> p) & 1) == 0) continue;
                switch (pieceTypeArray[p])
                {
                    case 1:
                        oppAttkBitboard |= blackPawnAttackSquareBitboards[p];
                        break;
                    case 2:
                        oppAttkBitboard |= knightSquareBitboards[p];
                        break;
                    case 3:
                        rays.DiagonalRays(allPieceBitboard, p, whiteKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        break;
                    case 4:
                        rays.StraightRays(allPieceBitboard, p, whiteKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        break;
                    case 5:
                        rays.DiagonalRays(allPieceBitboard, p, whiteKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        rays.StraightRays(allPieceBitboard, p, whiteKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        break;
                    case 6:
                        oppAttkBitboard |= kingSquareBitboards[p];
                        break;
                }
            }
            kingMovement.AddMoveOptionsToMoveListOnlyCaptures(whiteKingSquare, ~oppAttkBitboard & blackPieceBitboard);
        }

        private void GetLegalBlackMoves(int pCheckingPieceSquare, ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppDiagonalSliderVision = 0ul, oppStraightSliderVision = 0ul, oppStaticPieceVision = 0ul, oppAttkBitboard, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if (((int)(whitePieceBitboard >> p) & 1) == 0) continue;
                switch (pieceTypeArray[p])
                {
                    case 1:
                        oppStaticPieceVision |= whitePawnAttackSquareBitboards[p];
                        break;
                    case 2:
                        oppStaticPieceVision |= knightSquareBitboards[p];
                        break;
                    case 3:
                        rays.DiagonalRays(allPieceBitboard, p, blackKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        break;
                    case 4:
                        rays.StraightRays(allPieceBitboard, p, blackKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 5:
                        rays.DiagonalRays(allPieceBitboard, p, blackKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        rays.StraightRays(allPieceBitboard, p, blackKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 6:
                        oppStaticPieceVision |= kingSquareBitboards[p];
                        break;
                }
            }

            oppAttkBitboard = oppDiagonalSliderVision | oppStraightSliderVision | oppStaticPieceVision;
            int curCheckCount = ULONG_OPERATIONS.TrippleIsBitOne(oppDiagonalSliderVision, oppStaticPieceVision, oppStraightSliderVision, blackKingSquare);
            if (curCheckCount == 0)
            {
                if (blackCastleRightKingSide && ((allPieceBitboard | oppAttkBitboard) & BLACK_KING_ROCHADE) == 0ul) pMoveList.Add(mBLACK_KING_ROCHADE);
                if (blackCastleRightQueenSide && (allPieceBitboard & BLACK_QUEEN_ROCHADE | oppAttkBitboard & BLACK_QUEEN_ATTK_ROCHADE) == 0ul) pMoveList.Add(mBLACK_QUEEN_ROCHADE);

                if (enPassantSquare != 65 && (blackPieceBitboard & whitePawnAttackSquareBitboards[enPassantSquare]) != 0ul)
                {
                    int shiftedKS = blackKingSquare << 6, epM9 = enPassantSquare + 9, epM8 = epM9 - 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (((int)(blackPieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 7)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(whitePieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(whitePieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 -= 2;
                    if (((int)(blackPieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 0)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(whitePieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(whitePieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (((int)(blackPieceBitboard >> p) & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (((int)(pinnedPieces >> p) & 1) == 0) blackPawnMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, whitePieceBitboard);
                            else blackPawnMovement.AddMoveOptionsToMoveList(p, blackKingSquare, blackPieceBitboard, whitePieceBitboard);
                            break;
                        case 2:
                            if (((int)(pinnedPieces >> p) & 1) == 0) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, whitePieceBitboard);
                            break;
                        case 3:
                            if (((int)(pinnedPieces >> p) & 1) == 0) bishopMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (((int)(pinnedPieces >> p) & 1) == 0) rookMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (((int)(pinnedPieces >> p) & 1) == 0) queenMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveList(p, oppAttkBitboard | blackPieceBitboard, ~oppAttkBitboard & whitePieceBitboard);
                            break;
                    }
                }
            }
            else if (curCheckCount == 1)
            {
                //pCheckingPieceSquare +- 8 == enPassantSquare && pieceTypeList[pCheckingPieceSquare] == 1
                //Console.WriteLine(CreateFenString());
                ulong tCheckingPieceLine = squareConnectivesPrecalculationLineArray[blackKingSquare << 6 | pCheckingPieceSquare];
                if (enPassantSquare != 65 && (blackPieceBitboard & whitePawnAttackSquareBitboards[enPassantSquare]) != 0ul && (((int)(tCheckingPieceLine >> enPassantSquare) & 1) == 1 || (pCheckingPieceSquare - 8 == enPassantSquare && pieceTypeArray[pCheckingPieceSquare] == 1)))
                {
                    int shiftedKS = blackKingSquare << 6, epM9 = enPassantSquare + 9, epM8 = epM9 - 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (((int)(blackPieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(whitePieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(whitePieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 -= 2;
                    if (((int)(blackPieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(whitePieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(whitePieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (((int)(blackPieceBitboard >> p) & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (((int)(pinnedPieces >> p) & 1) == 0) blackPawnMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, whitePieceBitboard);
                            else blackPawnMovement.AddMoveOptionsToMoveList(p, blackKingSquare, blackPieceBitboard, whitePieceBitboard);
                            break;
                        case 2:
                            if (((int)(pinnedPieces >> p) & 1) == 0) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, whitePieceBitboard);
                            break;
                        case 3:
                            if (((int)(pinnedPieces >> p) & 1) == 0) bishopMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (((int)(pinnedPieces >> p) & 1) == 0) rookMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (((int)(pinnedPieces >> p) & 1) == 0) queenMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveList(p, oppAttkBitboard | blackPieceBitboard, ~oppAttkBitboard & whitePieceBitboard);
                            break;
                    }
                }
                int s_molc = pMoveList.Count;
                List<Move> tMoves = new List<Move>();
                for (int m = 0; m < s_molc; m++)
                {
                    Move mm = pMoveList[m];
                    if (mm.pieceType == 6) tMoves.Add(pMoveList[m]);
                    else if (((int)(tCheckingPieceLine >> pMoveList[m].endPos) & 1) == 1 || mm.isEnPassant) tMoves.Add(pMoveList[m]);
                }
                pMoveList = tMoves;
            }
            else kingMovement.AddMoveOptionsToMoveList(blackKingSquare, oppAttkBitboard | blackPieceBitboard, ~oppAttkBitboard & whitePieceBitboard);
        }

        private void GetLegalBlackCaptures(int pCheckingPieceSquare, ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppDiagonalSliderVision = 0ul, oppStraightSliderVision = 0ul, oppStaticPieceVision = 0ul, oppAttkBitboard, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if (((int)(whitePieceBitboard >> p) & 1) == 0) continue;
                switch (pieceTypeArray[p])
                {
                    case 1:
                        oppStaticPieceVision |= whitePawnAttackSquareBitboards[p];
                        break;
                    case 2:
                        oppStaticPieceVision |= knightSquareBitboards[p];
                        break;
                    case 3:
                        rays.DiagonalRays(allPieceBitboard, p, blackKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        break;
                    case 4:
                        rays.StraightRays(allPieceBitboard, p, blackKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 5:
                        rays.DiagonalRays(allPieceBitboard, p, blackKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        rays.StraightRays(allPieceBitboard, p, blackKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 6:
                        oppStaticPieceVision |= kingSquareBitboards[p];
                        break;
                }
            }

            oppAttkBitboard = oppDiagonalSliderVision | oppStraightSliderVision | oppStaticPieceVision;
            int curCheckCount = ULONG_OPERATIONS.TrippleIsBitOne(oppDiagonalSliderVision, oppStaticPieceVision, oppStraightSliderVision, blackKingSquare);
            if (curCheckCount == 0)
            {
                if (enPassantSquare != 65 && (blackPieceBitboard & whitePawnAttackSquareBitboards[enPassantSquare]) != 0ul)
                {
                    int shiftedKS = blackKingSquare << 6, epM9 = enPassantSquare + 9, epM8 = epM9 - 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (((int)(blackPieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 7)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(whitePieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(whitePieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 -= 2;
                    if (((int)(blackPieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 0)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(whitePieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(whitePieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (((int)(blackPieceBitboard >> p) & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (((int)(pinnedPieces >> p) & 1) == 0) blackPawnMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whitePieceBitboard);
                            else blackPawnMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackKingSquare, whitePieceBitboard);
                            break;
                        case 2:
                            if (((int)(pinnedPieces >> p) & 1) == 0) knightMovement.AddMovesToMoveOptionListOnlyCaptures(p, whitePieceBitboard);
                            break;
                        case 3:
                            if (((int)(pinnedPieces >> p) & 1) == 0) bishopMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whitePieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (((int)(pinnedPieces >> p) & 1) == 0) rookMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whitePieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (((int)(pinnedPieces >> p) & 1) == 0) queenMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whitePieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveListOnlyCaptures(p, ~oppAttkBitboard & whitePieceBitboard);
                            break;
                    }
                }
            }
            else if (curCheckCount == 1)
            {
                ulong tCheckingPieceLine = squareConnectivesPrecalculationLineArray[blackKingSquare << 6 | pCheckingPieceSquare];
                if (enPassantSquare != 65 && (blackPieceBitboard & whitePawnAttackSquareBitboards[enPassantSquare]) != 0ul && (((int)(tCheckingPieceLine >> enPassantSquare) & 1) == 1 || (pCheckingPieceSquare - 8 == enPassantSquare && pieceTypeArray[pCheckingPieceSquare] == 1)))
                {
                    int shiftedKS = blackKingSquare << 6, epM9 = enPassantSquare + 9, epM8 = epM9 - 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (((int)(blackPieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(whitePieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(whitePieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 -= 2;
                    if (((int)(blackPieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(whitePieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(whitePieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (((int)(blackPieceBitboard >> p) & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (((int)(pinnedPieces >> p) & 1) == 0) blackPawnMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whitePieceBitboard);
                            else blackPawnMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackKingSquare, whitePieceBitboard);
                            break;
                        case 2:
                            if (((int)(pinnedPieces >> p) & 1) == 0) knightMovement.AddMovesToMoveOptionListOnlyCaptures(p, whitePieceBitboard);
                            break;
                        case 3:
                            if (((int)(pinnedPieces >> p) & 1) == 0) bishopMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whitePieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (((int)(pinnedPieces >> p) & 1) == 0) rookMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whitePieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (((int)(pinnedPieces >> p) & 1) == 0) queenMovement.AddMoveOptionsToMoveListOnlyCaptures(p, whitePieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveListOnlyCaptures(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveListOnlyCaptures(p, ~oppAttkBitboard & whitePieceBitboard);
                            break;
                    }
                }
                int s_molc = pMoveList.Count;
                List<Move> tMoves = new List<Move>();
                for (int m = 0; m < s_molc; m++)
                {
                    Move mm = pMoveList[m];
                    if (mm.pieceType == 6) tMoves.Add(pMoveList[m]);
                    else if (((int)(tCheckingPieceLine >> pMoveList[m].endPos) & 1) == 1 || mm.isEnPassant) tMoves.Add(pMoveList[m]);
                }
                pMoveList = tMoves;
            }
            else kingMovement.AddMoveOptionsToMoveListOnlyCaptures(blackKingSquare, ~oppAttkBitboard & whitePieceBitboard);
        }

        private void GetLegalBlackMovesSpecialDoubleCheckCase(ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppAttkBitboard = 0ul, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if (((int)(whitePieceBitboard >> p) & 1) == 0) continue;
                switch (pieceTypeArray[p])
                {
                    case 1:
                        oppAttkBitboard |= whitePawnAttackSquareBitboards[p];
                        break;
                    case 2:
                        oppAttkBitboard |= knightSquareBitboards[p];
                        break;
                    case 3:
                        rays.DiagonalRays(allPieceBitboard, p, blackKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        break;
                    case 4:
                        rays.StraightRays(allPieceBitboard, p, blackKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        break;
                    case 5:
                        rays.DiagonalRays(allPieceBitboard, p, blackKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        rays.StraightRays(allPieceBitboard, p, blackKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        break;
                    case 6:
                        oppAttkBitboard |= kingSquareBitboards[p];
                        break;
                }
            }
            kingMovement.AddMoveOptionsToMoveList(blackKingSquare, oppAttkBitboard | blackPieceBitboard, ~oppAttkBitboard & whitePieceBitboard);
        }

        private void GetLegalBlackCapturesSpecialDoubleCheckCase(ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppAttkBitboard = 0ul, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if (((int)(whitePieceBitboard >> p) & 1) == 0) continue;
                switch (pieceTypeArray[p])
                {
                    case 1:
                        oppAttkBitboard |= whitePawnAttackSquareBitboards[p];
                        break;
                    case 2:
                        oppAttkBitboard |= knightSquareBitboards[p];
                        break;
                    case 3:
                        rays.DiagonalRays(allPieceBitboard, p, blackKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        break;
                    case 4:
                        rays.StraightRays(allPieceBitboard, p, blackKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        break;
                    case 5:
                        rays.DiagonalRays(allPieceBitboard, p, blackKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        rays.StraightRays(allPieceBitboard, p, blackKingSquare, ref oppAttkBitboard, ref pinnedPieces);
                        break;
                    case 6:
                        oppAttkBitboard |= kingSquareBitboards[p];
                        break;
                }
            }
            kingMovement.AddMoveOptionsToMoveListOnlyCaptures(blackKingSquare, ~oppAttkBitboard & whitePieceBitboard);
        }

        #endregion

        #region | PERMANENT MOVE MANAGEMENT |

        public Move GetMoveOfString(string pMove)
        {
            int tSP, tEP;

            string[] tSpl = pMove.Split(',');

            if (Char.IsNumber(pMove[0]))
            {
                tSP = Convert.ToInt32(tSpl[0]);
                tEP = Convert.ToInt32(tSpl[1]);
            }
            else
            {
                tSP = SQUARES.NumberNotation(tSpl[0]);
                tEP = SQUARES.NumberNotation(tSpl[1]);
            }
                
            int tPT = pieceTypeArray[tSP];
            bool tIC = ULONG_OPERATIONS.IsBitOne(allPieceBitboard, tEP), tIEP = enPassantSquare == tEP && tPT == 1;

            if (tSpl.Length == 3) return new Move(tSP, tEP, tPT, Convert.ToInt32(tSpl[2]), tIC);
            if (tIEP) return new Move(true, tSP, tEP, isWhiteToMove ? tEP - 8 : tEP + 8);
            if (tPT == 1 && Math.Abs(tSP - tEP) > 11) return new Move(tSP, tEP, 1, false, isWhiteToMove ? tSP + 8 : tSP - 8);
            if (tPT == 6 && (Math.Abs(tSP - tEP) == 2 || Math.Abs(tSP - tEP) == 3)) {
                if (isWhiteToMove) return tEP == 6 ? mWHITE_KING_ROCHADE : mWHITE_QUEEN_ROCHADE;
                return tEP == 62 ? mBLACK_KING_ROCHADE : mBLACK_QUEEN_ROCHADE;
            }
            return new Move(tSP, tEP, tPT, tIC);
        }

        public void PlainMakeMove(string pMoveName)
        {
            PlainMakeMove(GetMoveOfString(pMoveName)); 
        }

        public void PlainMakeMove(Move pMove)
        {
            lastMadeMove = pMove;
            if (isWhiteToMove) WhiteMakeMove(pMove);
            else BlackMakeMove(pMove);
        }

        public void WhiteMakeMove(Move pMove)
        {
            int tEndPos = pMove.endPos, tStartPos = pMove.startPos, tPieceType = pMove.pieceType, tPTI = pieceTypeArray[tEndPos];

            fiftyMoveRuleCounter++;
            whitePieceBitboard ^= pMove.ownPieceBitboardXOR;
            pieceTypeArray[tEndPos] = tPieceType;
            pieceTypeArray[tStartPos] = 0;
            zobristKey ^= blackTurnHash ^ enPassantSquareHashes[enPassantSquare] ^ pieceHashesWhite[tStartPos, tPieceType] ^ pieceHashesWhite[tEndPos, tPieceType];
            enPassantSquare = 65;
            isWhiteToMove = false;

            switch (pMove.moveTypeID)
            {
                case 0: // Standard-Standard-Move
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 1: // Standard-Pawn-Move
                    fiftyMoveRuleCounter = 0;
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 2: // Standard-Knight-Move
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 3: // Standard-King-Move
                    whiteKingSquare = tEndPos;
                    if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                    if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                    whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 4: // Standard-Rook-Move
                    if (whiteCastleRightQueenSide && tStartPos == 0)
                    {
                        zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = false;
                    }
                    else if (whiteCastleRightKingSide && tStartPos == 7)
                    {
                        zobristKey ^= whiteKingSideRochadeRightHash;
                        whiteCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 5: // Standard-Pawn-Capture
                    fiftyMoveRuleCounter = 0;
                    blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
                    zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                    if (blackCastleRightQueenSide && tEndPos == 56)
                    {
                        zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightQueenSide = false;
                    }
                    else if (blackCastleRightKingSide && tEndPos == 63)
                    {
                        zobristKey ^= blackKingSideRochadeRightHash;
                        blackCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 6: // Standard-Knight-Capture
                    fiftyMoveRuleCounter = 0;
                    blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
                    zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                    if (blackCastleRightQueenSide && tEndPos == 56)
                    {
                        zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightQueenSide = false;
                    }
                    else if (blackCastleRightKingSide && tEndPos == 63)
                    {
                        zobristKey ^= blackKingSideRochadeRightHash;
                        blackCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 7: // Standard-King-Capture
                    if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                    if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                    whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                    fiftyMoveRuleCounter = 0;
                    blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
                    zobristKey ^= pieceHashesBlack[whiteKingSquare = tEndPos, tPTI];
                    if (blackCastleRightQueenSide && tEndPos == 56)
                    {
                        zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightQueenSide = false;
                    }
                    else if (blackCastleRightKingSide && tEndPos == 63)
                    {
                        zobristKey ^= blackKingSideRochadeRightHash;
                        blackCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 8: // Standard-Rook-Capture
                    fiftyMoveRuleCounter = 0;
                    blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
                    zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                    if (whiteCastleRightQueenSide && tStartPos == 0)
                    {
                        zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = false;
                    }
                    else if (whiteCastleRightKingSide && tStartPos == 7)
                    {
                        zobristKey ^= whiteKingSideRochadeRightHash;
                        whiteCastleRightKingSide = false;
                    }
                    if (blackCastleRightQueenSide && tEndPos == 56)
                    {
                        zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightQueenSide = false;
                    }
                    else if (blackCastleRightKingSide && tEndPos == 63)
                    {
                        zobristKey ^= blackKingSideRochadeRightHash;
                        blackCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 9: // Standard-Standard-Capture
                    fiftyMoveRuleCounter = 0;
                    blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
                    zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                    if (blackCastleRightQueenSide && tEndPos == 56)
                    {
                        zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightQueenSide = false;
                    }
                    else if (blackCastleRightKingSide && tEndPos == 63)
                    {
                        zobristKey ^= blackKingSideRochadeRightHash;
                        blackCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 10: // Double-Pawn-Move
                    zobristKey ^= enPassantSquareHashes[enPassantSquare = pMove.enPassantOption];
                    fiftyMoveRuleCounter = 0;
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 11: // Rochade
                    if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                    if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                    whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                    whiteKingSquare = tEndPos;
                    pieceTypeArray[pMove.rochadeEndPos] = 4;
                    pieceTypeArray[pMove.rochadeStartPos] = 0;
                    zobristKey ^= pieceHashesWhite[pMove.rochadeStartPos, 4] ^ pieceHashesWhite[pMove.rochadeEndPos, 4];
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 12: // En-Passant
                    fiftyMoveRuleCounter = 0;
                    blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
                    pieceTypeArray[pMove.enPassantOption] = 0;
                    zobristKey ^= pieceHashesBlack[pMove.enPassantOption, 1];
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 13: // Standard-Promotion
                    fiftyMoveRuleCounter = 0;
                    pieceTypeArray[tEndPos] = tPieceType = pMove.promotionType;
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    zobristKey ^= pieceHashesWhite[tEndPos, 1] ^ pieceHashesWhite[tEndPos, pMove.promotionType];
                    break;
                case 14: // Capture-Promotion
                    fiftyMoveRuleCounter = 0;
                    blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
                    pieceTypeArray[tEndPos] = tPieceType = pMove.promotionType;
                    zobristKey ^= pieceHashesBlack[tEndPos, tPTI] ^ pieceHashesWhite[tEndPos, 1] ^ pieceHashesWhite[tEndPos, pMove.promotionType];
                    if (blackCastleRightQueenSide && tEndPos == 56)
                    {
                        zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightQueenSide = false;
                    }
                    else if (blackCastleRightKingSide && tEndPos == 63)
                    {
                        zobristKey ^= blackKingSideRochadeRightHash;
                        blackCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
            }

            if (pMove.isCapture || tPieceType == 1)
            {
                curSearchZobristKeyLine = Array.Empty<ulong>();
                return;
            }
            ulong[] u = new ulong[(tPTI = curSearchZobristKeyLine.Length) + 1];
            for (int i = tPTI; i-- > 0;) u[i] = curSearchZobristKeyLine[i];
            u[tPTI] = zobristKey;
            curSearchZobristKeyLine = u;
        }

        public void BlackMakeMove(Move pMove)
        {
            int tEndPos = pMove.endPos, tStartPos = pMove.startPos, tPieceType = pMove.pieceType, tPTI = pieceTypeArray[tEndPos];

            fiftyMoveRuleCounter++;
            blackPieceBitboard ^= pMove.ownPieceBitboardXOR;
            pieceTypeArray[tEndPos] = tPieceType;
            pieceTypeArray[tStartPos] = 0;
            zobristKey ^= blackTurnHash ^ enPassantSquareHashes[enPassantSquare] ^ pieceHashesBlack[tStartPos, tPieceType] ^ pieceHashesBlack[tEndPos, tPieceType];
            enPassantSquare = 65;
            isWhiteToMove = true;
            switch (pMove.moveTypeID)
            {
                case 0: // Standard-Standard-Move
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 1: // Standard-Pawn-Move
                    fiftyMoveRuleCounter = 0;
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 2: // Standard-Knight-Move
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 3: // Standard-King-Move
                    blackKingSquare = tEndPos;
                    if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                    if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                    blackCastleRightKingSide = blackCastleRightQueenSide = false;
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 4: // Standard-Rook-Move
                    if (blackCastleRightQueenSide && tStartPos == 56)
                    {
                        zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightQueenSide = false;
                    }
                    else if (blackCastleRightKingSide && tStartPos == 63)
                    {
                        zobristKey ^= blackKingSideRochadeRightHash;
                        blackCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 5: // Standard-Pawn-Capture
                    fiftyMoveRuleCounter = 0;
                    whitePieceBitboard ^= pMove.oppPieceBitboardXOR;
                    zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                    if (whiteCastleRightQueenSide && tEndPos == 0)
                    {
                        zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = false;
                    }
                    else if (whiteCastleRightKingSide && tEndPos == 7)
                    {
                        zobristKey ^= whiteKingSideRochadeRightHash;
                        whiteCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 6: // Standard-Knight-Capture
                    fiftyMoveRuleCounter = 0;
                    whitePieceBitboard ^= pMove.oppPieceBitboardXOR;
                    zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                    if (whiteCastleRightQueenSide && tEndPos == 0)
                    {
                        zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = false;
                    }
                    else if (whiteCastleRightKingSide && tEndPos == 7)
                    {
                        zobristKey ^= whiteKingSideRochadeRightHash;
                        whiteCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 7: // Standard-King-Capture
                    if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                    if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                    blackCastleRightKingSide = blackCastleRightQueenSide = false;
                    fiftyMoveRuleCounter = 0;
                    whitePieceBitboard ^= pMove.oppPieceBitboardXOR;
                    zobristKey ^= pieceHashesWhite[blackKingSquare = tEndPos, tPTI];
                    if (whiteCastleRightQueenSide && tEndPos == 0)
                    {
                        zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = false;
                    }
                    else if (whiteCastleRightKingSide && tEndPos == 7)
                    {
                        zobristKey ^= whiteKingSideRochadeRightHash;
                        whiteCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 8: // Standard-Rook-Capture
                    fiftyMoveRuleCounter = 0;
                    whitePieceBitboard ^= pMove.oppPieceBitboardXOR;
                    zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                    if (blackCastleRightQueenSide && tStartPos == 56)
                    {
                        zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightQueenSide = false;
                    }
                    else if (blackCastleRightKingSide && tStartPos == 63)
                    {
                        zobristKey ^= blackKingSideRochadeRightHash;
                        blackCastleRightKingSide = false;
                    }
                    if (whiteCastleRightQueenSide && tEndPos == 0)
                    {
                        zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = false;
                    }
                    else if (whiteCastleRightKingSide && tEndPos == 7)
                    {
                        zobristKey ^= whiteKingSideRochadeRightHash;
                        whiteCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 9: // Standard-Standard-Capture
                    fiftyMoveRuleCounter = 0;
                    whitePieceBitboard ^= pMove.oppPieceBitboardXOR;
                    zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                    if (whiteCastleRightQueenSide && tEndPos == 0)
                    {
                        zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = false;
                    }
                    else if (whiteCastleRightKingSide && tEndPos == 7)
                    {
                        zobristKey ^= whiteKingSideRochadeRightHash;
                        whiteCastleRightKingSide = false;
                    }
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 10: // Double-Pawn-Move
                    zobristKey ^= enPassantSquareHashes[enPassantSquare = pMove.enPassantOption];
                    fiftyMoveRuleCounter = 0;
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 11: // Rochade
                    if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                    if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                    blackCastleRightKingSide = blackCastleRightQueenSide = false;
                    blackKingSquare = tEndPos;
                    pieceTypeArray[pMove.rochadeEndPos] = 4;
                    pieceTypeArray[pMove.rochadeStartPos] = 0;
                    zobristKey ^= pieceHashesBlack[pMove.rochadeStartPos, 4] ^ pieceHashesBlack[pMove.rochadeEndPos, 4];
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 12: // En-Passant
                    fiftyMoveRuleCounter = 0;
                    whitePieceBitboard ^= pMove.oppPieceBitboardXOR;
                    pieceTypeArray[pMove.enPassantOption] = 0;
                    zobristKey ^= pieceHashesWhite[pMove.enPassantOption, 1];
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 13: // Standard-Promotion
                    fiftyMoveRuleCounter = 0;
                    pieceTypeArray[tEndPos] = tPieceType = pMove.promotionType;
                    zobristKey ^= pieceHashesBlack[tEndPos, 1] ^ pieceHashesBlack[tEndPos, pMove.promotionType];
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
                case 14: // Capture-Promotion
                    fiftyMoveRuleCounter = 0;
                    whitePieceBitboard ^= pMove.oppPieceBitboardXOR;
                    pieceTypeArray[tEndPos] = tPieceType = pMove.promotionType;
                    zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                    if (whiteCastleRightQueenSide && tEndPos == 0)
                    {
                        zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = false;
                    }
                    else if (whiteCastleRightKingSide && tEndPos == 7)
                    {
                        zobristKey ^= whiteKingSideRochadeRightHash;
                        whiteCastleRightKingSide = false;
                    }
                    zobristKey ^= pieceHashesWhite[tEndPos, tPTI] ^ pieceHashesBlack[tEndPos, 1] ^ pieceHashesBlack[tEndPos, pMove.promotionType];
                    allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                    break;
            }

            if (pMove.isCapture || tPieceType == 1)
            {
                curSearchZobristKeyLine = Array.Empty<ulong>();
                return;
            }
            ulong[] u = new ulong[(tPTI = curSearchZobristKeyLine.Length) + 1];
            for (int i = tPTI; i-- > 0;) u[i] = curSearchZobristKeyLine[i];
            u[tPTI] = zobristKey;
            curSearchZobristKeyLine = u;
        }

        public void WhiteUndoMove(Move pMove) // MISSING 
        {
            //int tEndPos = pMove.endPos, tStartPos = pMove.startPos, tPieceType = pMove.pieceType, tPTI = pieceTypeArray[tEndPos];
            //
            //if (fiftyMoveRuleCounter > 0) fiftyMoveRuleCounter--;
            //whitePieceBitboard ^= pMove.ownPieceBitboardXOR;
            //pieceTypeArray[tEndPos] = tPieceType;
            //pieceTypeArray[tStartPos] = 0;
            //zobristKey ^= blackTurnHash ^ enPassantSquareHashes[enPassantSquare] ^ pieceHashesWhite[tStartPos, tPieceType] ^ pieceHashesWhite[tEndPos, tPieceType];
            //enPassantSquare = 65;
            //isWhiteToMove = true;
            //
            //switch (pMove.moveTypeID)
            //{
            //    case 0: // Standard-Standard-Move
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 1: // Standard-Pawn-Move
            //        fiftyMoveRuleCounter = 0;
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 2: // Standard-Knight-Move
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 3: // Standard-King-Move
            //        whiteKingSquare = tEndPos;
            //        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
            //        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
            //        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 4: // Standard-Rook-Move
            //        if (whiteCastleRightQueenSide && tStartPos == 0)
            //        {
            //            zobristKey ^= whiteQueenSideRochadeRightHash;
            //            whiteCastleRightQueenSide = false;
            //        }
            //        else if (whiteCastleRightKingSide && tStartPos == 7)
            //        {
            //            zobristKey ^= whiteKingSideRochadeRightHash;
            //            whiteCastleRightKingSide = false;
            //        }
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 5: // Standard-Pawn-Capture
            //        fiftyMoveRuleCounter = 0;
            //        blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
            //        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
            //        if (blackCastleRightQueenSide && tEndPos == 56)
            //        {
            //            zobristKey ^= blackQueenSideRochadeRightHash;
            //            blackCastleRightQueenSide = false;
            //        }
            //        else if (blackCastleRightKingSide && tEndPos == 63)
            //        {
            //            zobristKey ^= blackKingSideRochadeRightHash;
            //            blackCastleRightKingSide = false;
            //        }
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 6: // Standard-Knight-Capture
            //        fiftyMoveRuleCounter = 0;
            //        blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
            //        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
            //        if (blackCastleRightQueenSide && tEndPos == 56)
            //        {
            //            zobristKey ^= blackQueenSideRochadeRightHash;
            //            blackCastleRightQueenSide = false;
            //        }
            //        else if (blackCastleRightKingSide && tEndPos == 63)
            //        {
            //            zobristKey ^= blackKingSideRochadeRightHash;
            //            blackCastleRightKingSide = false;
            //        }
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 7: // Standard-King-Capture
            //        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
            //        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
            //        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
            //        fiftyMoveRuleCounter = 0;
            //        blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
            //        zobristKey ^= pieceHashesBlack[whiteKingSquare = tEndPos, tPTI];
            //        if (blackCastleRightQueenSide && tEndPos == 56)
            //        {
            //            zobristKey ^= blackQueenSideRochadeRightHash;
            //            blackCastleRightQueenSide = false;
            //        }
            //        else if (blackCastleRightKingSide && tEndPos == 63)
            //        {
            //            zobristKey ^= blackKingSideRochadeRightHash;
            //            blackCastleRightKingSide = false;
            //        }
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 8: // Standard-Rook-Capture
            //        fiftyMoveRuleCounter = 0;
            //        blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
            //        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
            //        if (whiteCastleRightQueenSide && tStartPos == 0)
            //        {
            //            zobristKey ^= whiteQueenSideRochadeRightHash;
            //            whiteCastleRightQueenSide = false;
            //        }
            //        else if (whiteCastleRightKingSide && tStartPos == 7)
            //        {
            //            zobristKey ^= whiteKingSideRochadeRightHash;
            //            whiteCastleRightKingSide = false;
            //        }
            //        if (blackCastleRightQueenSide && tEndPos == 56)
            //        {
            //            zobristKey ^= blackQueenSideRochadeRightHash;
            //            blackCastleRightQueenSide = false;
            //        }
            //        else if (blackCastleRightKingSide && tEndPos == 63)
            //        {
            //            zobristKey ^= blackKingSideRochadeRightHash;
            //            blackCastleRightKingSide = false;
            //        }
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 9: // Standard-Standard-Capture
            //        fiftyMoveRuleCounter = 0;
            //        blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
            //        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
            //        if (blackCastleRightQueenSide && tEndPos == 56)
            //        {
            //            zobristKey ^= blackQueenSideRochadeRightHash;
            //            blackCastleRightQueenSide = false;
            //        }
            //        else if (blackCastleRightKingSide && tEndPos == 63)
            //        {
            //            zobristKey ^= blackKingSideRochadeRightHash;
            //            blackCastleRightKingSide = false;
            //        }
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 10: // Double-Pawn-Move
            //        zobristKey ^= enPassantSquareHashes[enPassantSquare = pMove.enPassantOption];
            //        fiftyMoveRuleCounter = 0;
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 11: // Rochade
            //        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
            //        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
            //        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
            //        whiteKingSquare = tEndPos;
            //        pieceTypeArray[pMove.rochadeEndPos] = 4;
            //        pieceTypeArray[pMove.rochadeStartPos] = 0;
            //        zobristKey ^= pieceHashesWhite[pMove.rochadeStartPos, 4] ^ pieceHashesWhite[pMove.rochadeEndPos, 4];
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 12: // En-Passant
            //        fiftyMoveRuleCounter = 0;
            //        blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
            //        pieceTypeArray[pMove.enPassantOption] = 0;
            //        zobristKey ^= pieceHashesBlack[pMove.enPassantOption, 1];
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //    case 13: // Standard-Promotion
            //        fiftyMoveRuleCounter = 0;
            //        pieceTypeArray[tEndPos] = tPieceType = pMove.promotionType;
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        zobristKey ^= pieceHashesWhite[tEndPos, 1] ^ pieceHashesWhite[tEndPos, pMove.promotionType];
            //        break;
            //    case 14: // Capture-Promotion
            //        fiftyMoveRuleCounter = 0;
            //        blackPieceBitboard ^= pMove.oppPieceBitboardXOR;
            //        pieceTypeArray[tEndPos] = tPieceType = pMove.promotionType;
            //        zobristKey ^= pieceHashesBlack[tEndPos, tPTI] ^ pieceHashesWhite[tEndPos, 1] ^ pieceHashesWhite[tEndPos, pMove.promotionType];
            //        if (blackCastleRightQueenSide && tEndPos == 56)
            //        {
            //            zobristKey ^= blackQueenSideRochadeRightHash;
            //            blackCastleRightQueenSide = false;
            //        }
            //        else if (blackCastleRightKingSide && tEndPos == 63)
            //        {
            //            zobristKey ^= blackKingSideRochadeRightHash;
            //            blackCastleRightKingSide = false;
            //        }
            //        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            //        break;
            //}
            //switch (pMove.moveTypeID)
            //{
            //    case 3: // Standard-King-Move
            //        whiteKingSquare = tWhiteKingSquare;
            //        break;
            //    case 7: // Standard-King-Capture
            //        whiteKingSquare = tWhiteKingSquare;
            //        break;
            //    case 10: // Double-Pawn-Move
            //        enPassantSquare = 65;
            //        break;
            //    case 11: // Rochade
            //        whiteKingSquare = tWhiteKingSquare;
            //        pieceTypeArray[curMove.rochadeStartPos] = 4;
            //        pieceTypeArray[curMove.rochadeEndPos] = 0;
            //        break;
            //    case 12: // En-Passant
            //        pieceTypeArray[curMove.enPassantOption] = 1;
            //        break;
            //    case 13: // Standard-Promotion
            //        pieceTypeArray[tStartPos] = 1;
            //        break;
            //    case 14: // Capture-Promotion
            //        pieceTypeArray[tStartPos] = 1;
            //        break;
            //}
        } 

        #endregion

        #region | MINIMAX FUNCTIONS |

        private const int WHITE_CHECKMATE_VAL = 100000, BLACK_CHECKMATE_VAL = -100000, CHECK_EXTENSION_LENGTH = -12, MAX_QUIESCENCE_TOTAL_LENGTH = -32;

        private const int CAPTURE_SORT_VAL = ENGINE_VALS.CAPTURE_SORT_VAL;
        private const int BESTMOVE_SORT_VAL = ENGINE_VALS.BESTMOVE_SORT_VAL;
        private const int KILLERMOVE_SORT_VAL = ENGINE_VALS.KILLERMOVE_SORT_VAL;
        private readonly int[,] MVVLVA_TABLE = ENGINE_VALS.MVVLVA_TABLE;

        private readonly Move NULL_MOVE = new Move(0, 0, 0);

        private int curSearchDepth = 0, curSubSearchDepth = -1;

        public int MinimaxRoot(long pTime)
        {
            evalCount = 0;
            searches++;
            int baseLineLen = 0;
            long tTimestamp = globalTimer.ElapsedTicks + pTime;

            ClearHeuristics();
            transpositionTable.Clear();

            ulong[] tZobristKeyLine = Array.Empty<ulong>();
            if (curSearchZobristKeyLine != null) {
                baseLineLen = curSearchZobristKeyLine.Length;
                tZobristKeyLine = new ulong[baseLineLen];
                Array.Copy(curSearchZobristKeyLine, tZobristKeyLine, baseLineLen);
            }

            int perftScore, tattk = (isWhiteToMove) ? PreMinimaxCheckCheckWhite() : PreMinimaxCheckCheckBlack(), pDepth = 1;

            do {
                curSearchDepth = pDepth;
                curSubSearchDepth = pDepth - 1;
                ulong[] completeZobristHistory = new ulong[baseLineLen + pDepth - CHECK_EXTENSION_LENGTH + 1];
                for (int i = 0; i < baseLineLen; i++) completeZobristHistory[i] = curSearchZobristKeyLine[i];
                curSearchZobristKeyLine = completeZobristHistory;

                if (isWhiteToMove)
                {
                    if (tattk == -1) perftScore = MinimaxWhite(0, BLACK_CHECKMATE_VAL - 100, WHITE_CHECKMATE_VAL + 100, pDepth, baseLineLen, tattk, NULL_MOVE);
                    else perftScore = MinimaxWhite(0, BLACK_CHECKMATE_VAL - 100, WHITE_CHECKMATE_VAL + 100, pDepth, baseLineLen, tattk, NULL_MOVE);
                }
                else
                {
                    if (tattk == -1) perftScore = MinimaxBlack(0, BLACK_CHECKMATE_VAL - 100, WHITE_CHECKMATE_VAL + 100, pDepth, baseLineLen, tattk, NULL_MOVE);
                    else perftScore = MinimaxBlack(0, BLACK_CHECKMATE_VAL - 100, WHITE_CHECKMATE_VAL + 100, pDepth, baseLineLen, tattk, NULL_MOVE);
                }

                if (debugSearchDepthResults && pTime != 1L)
                {
                    int tNpS = Convert.ToInt32((double)evalCount * 10_000_000d / (double)(pTime - tTimestamp + globalTimer.ElapsedTicks));
                    int tSearchEval = perftScore;
                    int timeForSearchSoFar = (int)((pTime - tTimestamp + globalTimer.ElapsedTicks) / 10000d);
                    Move tBestMove = transpositionTable[zobristKey].bestMove;

                    Console.Write((tSearchEval >= 0 ? "+" : "") + tSearchEval);
                    Console.Write(" " + tBestMove + "  [");
                    Console.Write("Depth = " + pDepth + ", ");
                    Console.Write("Evals = " + GetThreeDigitSeperatedInteger(evalCount) + ", ");
                    Console.Write("Time = " + GetThreeDigitSeperatedInteger(timeForSearchSoFar) + "ms, ");
                    Console.WriteLine("NpS = " + GetThreeDigitSeperatedInteger(tNpS) + "]");
                }

                pDepth++; 
            } while (globalTimer.ElapsedTicks < tTimestamp);

            depths += pDepth - 1;
            BOT_MAIN.depthsSearched += pDepth - 1;
            BOT_MAIN.searchesFinished++;
            BOT_MAIN.evaluationsMade += evalCount;

            curSearchZobristKeyLine = tZobristKeyLine;

            return perftScore;
        }

        private int MinimaxWhite(int pPly, int pAlpha, int pBeta, int pDepth, int pRepetitionHistoryPly, int pCheckingSquare, Move pLastMove)
        {
            if (IsDrawByRepetition(pRepetitionHistoryPly - 4)) return 0;
            if ((pDepth <= 0 && pCheckingSquare == -1) || pDepth < CHECK_EXTENSION_LENGTH) return QuiescenceWhite(pPly, pAlpha, pBeta, pDepth - 1, pCheckingSquare, pLastMove);

            #region NodePrep()

            List<Move> moveOptionList = new List<Move>();
            Move bestMove = NULL_MOVE;
            if (pLastMove.isPromotion && pLastMove.isCapture && pLastMove.startPos % 8 == pCheckingSquare % 8 && pLastMove.startPos == whiteKingSquare + 8 && (pLastMove.promotionType == 4 || pLastMove.promotionType == 5)) GetLegalWhiteMovesSpecialDoubleCheckCase(ref moveOptionList);
            else GetLegalWhiteMoves(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tWhiteKingSquare = whiteKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1, curEval = BLACK_CHECKMATE_VAL - pDepth;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;
            enPassantSquare = 65;
            isWhiteToMove = true;

            #endregion

            #region MoveSort()

            double[] moveSortingArray = new double[molc];
            int[] moveSortingArrayIndexes = new int[molc], thisSearchMoveSortingArrayForTransposEntry = new int[molc];
            transpositionTable.TryGetValue(zobristKey, out TranspositionEntry transposEntry);
            if (transposEntry == null)
            {
                for (int m = 0; m < molc; m++)
                {
                    moveSortingArrayIndexes[m] = m;
                    Move curMove = moveOptionList[m];
                    if (curMove.isCapture) moveSortingArray[m] = CAPTURE_SORT_VAL - MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
                    else if (killerMoveHeuristic[curMove.moveHash]) moveSortingArray[m] = KILLERMOVE_SORT_VAL;
                    else moveSortingArray[m] = -historyHeuristic[curMove.moveHash];
                    //moveSortingArray[m] = 
                }
            }
            else
            {
                for (int m = 0; m < molc; m++)
                {
                    moveSortingArrayIndexes[m] = m;
                    Move curMove = moveOptionList[m];
                    if (curMove == transposEntry.bestMove) moveSortingArray[m] = BESTMOVE_SORT_VAL;
                    else if (curMove.isCapture) moveSortingArray[m] = CAPTURE_SORT_VAL - MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
                    else if (killerMoveHeuristic[curMove.moveHash]) moveSortingArray[m] = KILLERMOVE_SORT_VAL;
                    else moveSortingArray[m] = -historyHeuristic[curMove.moveHash];
                    if (transposEntry.moveGenOrderedEvalLength > m) moveSortingArray[m] -= transposEntry.moveGenOrderedEvals[m];
                }
            }

            Array.Sort(moveSortingArray, moveSortingArrayIndexes);

            #endregion

            for (int m = 0; m < molc; m++)
            {
                int tActualIndex = moveSortingArrayIndexes[m];
                Move curMove = moveOptionList[tActualIndex];

                if (debugSortResults && pDepth == curSubSearchDepth)
                {
                    Console.WriteLine(moveSortingArray[m] + " | " + curMove);
                }

                int tPieceType = curMove.pieceType, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPTI = pieceTypeArray[tEndPos], tCheckPos = -1, tI, tPossibleAttackPiece;

                #region MakeMove()

                fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
                whitePieceBitboard = tWPB ^ curMove.ownPieceBitboardXOR;
                pieceTypeArray[tEndPos] = tPieceType;
                pieceTypeArray[tStartPos] = 0;
                zobristKey = tZobristKey ^ pieceHashesWhite[tStartPos, tPieceType] ^ pieceHashesWhite[tEndPos, tPieceType];
                switch (curMove.moveTypeID)
                {
                    case 0: // Standard-Standard-Move
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 1: // Standard-Pawn-Move
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePawnAttackSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 2: // Standard-Knight-Move
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(knightSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 3: // Standard-King-Move
                        whiteKingSquare = tEndPos;
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 4: // Standard-Rook-Move
                        blackPieceBitboard = tBPB;
                        if (whiteCastleRightQueenSide && tStartPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tStartPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 5: // Standard-Pawn-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePawnAttackSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 6: // Standard-Knight-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(knightSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 7: // Standard-King-Capture
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[whiteKingSquare = tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 8: // Standard-Rook-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tStartPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tStartPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 9: // Standard-Standard-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 10: // Double-Pawn-Move
                        zobristKey ^= enPassantSquareHashes[enPassantSquare = curMove.enPassantOption];
                        blackPieceBitboard = tBPB;
                        fiftyMoveRuleCounter = 0;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePawnAttackSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 11: // Rochade
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        whiteKingSquare = tEndPos;
                        blackPieceBitboard = tBPB;
                        pieceTypeArray[curMove.rochadeEndPos] = 4;
                        pieceTypeArray[curMove.rochadeStartPos] = 0;
                        zobristKey ^= pieceHashesWhite[curMove.rochadeStartPos, 4] ^ pieceHashesWhite[curMove.rochadeEndPos, 4];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | curMove.rochadeEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << curMove.rochadeEndPos)) tCheckPos = curMove.rochadeEndPos;
                        break;
                    case 12: // En-Passant
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[curMove.enPassantOption] = 0;
                        zobristKey ^= pieceHashesBlack[curMove.enPassantOption, 1];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePawnAttackSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | curMove.enPassantOption] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 13: // Standard-Promotion
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        zobristKey ^= pieceHashesWhite[tEndPos, 1] ^ pieceHashesWhite[tEndPos, curMove.promotionType];
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && ((int)(knightSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        break;
                    case 14: // Capture-Promotion
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI] ^ pieceHashesWhite[tEndPos, 1] ^ pieceHashesWhite[tEndPos, curMove.promotionType];
                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (tPieceType == 2 && ((int)(knightSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        break;
                }
                curSearchZobristKeyLine[pRepetitionHistoryPly] = zobristKey;

                #endregion

                //debugMoveList[pDepth + 100] = curMove;

                int tEval = MinimaxBlack(pPly + 1, pAlpha, pBeta, pDepth - 1, pRepetitionHistoryPly + 1, tCheckPos, curMove);
                thisSearchMoveSortingArrayForTransposEntry[tActualIndex] = tEval;

                //if (curSearchDepth == pDepth) Console.WriteLine(tEval + " >> " + curMove);
                //else if (curSubSearchDepth == pDepth) Console.WriteLine("" + tEval + " > " + curMove);

                #region UndoMove()

                pieceTypeArray[tEndPos] = tPTI;
                pieceTypeArray[tStartPos] = tPieceType;
                whiteCastleRightKingSide = tWKSCR;
                whiteCastleRightQueenSide = tWQSCR;
                blackCastleRightKingSide = tBKSCR;
                blackCastleRightQueenSide = tBQSCR;
                switch (curMove.moveTypeID)
                {
                    case 3: // Standard-King-Move
                        whiteKingSquare = tWhiteKingSquare;
                        break;
                    case 7: // Standard-King-Capture
                        whiteKingSquare = tWhiteKingSquare;
                        break;
                    case 10: // Double-Pawn-Move
                        enPassantSquare = 65;
                        break;
                    case 11: // Rochade
                        whiteKingSquare = tWhiteKingSquare;
                        pieceTypeArray[curMove.rochadeStartPos] = 4;
                        pieceTypeArray[curMove.rochadeEndPos] = 0;
                        break;
                    case 12: // En-Passant
                        pieceTypeArray[curMove.enPassantOption] = 1;
                        break;
                    case 13: // Standard-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                    case 14: // Capture-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                }

                #endregion

                if (tEval > curEval)
                {
                    bestMove = curMove;
                    curEval = tEval;
                }
                if (pAlpha < curEval) pAlpha = curEval;
                if (curEval >= pBeta)
                {
                    if (!curMove.isCapture)
                    {
                        killerMoveHeuristic[curMove.moveHash] = true;
                        if (pDepth > 0) historyHeuristic[curMove.moveHash] += pDepth * pDepth;
                    }
                    //thisSearchMoveSortingArrayForTransposEntry[tActualIndex] += KILLERMOVE_SORT_VAL;
                    //if (pDepth >= curSubSearchDepth) Console.WriteLine("* Beta Cutoff");
                    break;
                }
            }

            isWhiteToMove = true;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            if (molc == 0 && pCheckingSquare == 0) curEval = 0;

            if (!transpositionTable.ContainsKey(zobristKey)) transpositionTable.Add(zobristKey, new TranspositionEntry(bestMove, thisSearchMoveSortingArrayForTransposEntry));
            else transpositionTable[zobristKey] = new TranspositionEntry(bestMove, thisSearchMoveSortingArrayForTransposEntry);

            return curEval;
        }

        private int QuiescenceWhite(int pPly, int pAlpha, int pBeta, int pDepth, int pCheckingSquare, Move pLastMove)
        {
            int standPat = Evaluate();
            if (standPat >= pBeta || pDepth < MAX_QUIESCENCE_TOTAL_LENGTH) return pBeta;
            if (standPat > pAlpha) pAlpha = standPat;

            #region NodePrep()

            List<Move> moveOptionList = new List<Move>();
            if (pLastMove.isPromotion && pLastMove.isCapture && pLastMove.startPos % 8 == pCheckingSquare % 8 && pLastMove.startPos == whiteKingSquare + 8 && (pLastMove.promotionType == 4 || pLastMove.promotionType == 5)) GetLegalWhiteCapturesSpecialDoubleCheckCase(ref moveOptionList);
            else GetLegalWhiteCaptures(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tWhiteKingSquare = whiteKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;
            enPassantSquare = 65;
            isWhiteToMove = true;

            #endregion

            //#region MoveSort()
            //
            //double[] moveSortingArray = new double[molc];
            //int[] moveSortingArrayIndexes = new int[molc];
            //transpositionTable.TryGetValue(zobristKey, out Move? pvNodeMove);
            //for (int m = 0; m < molc; m++)
            //{
            //    moveSortingArrayIndexes[m] = m;
            //    Move curMove = moveOptionList[m];
            //    if (curMove == pvNodeMove) moveSortingArray[m] = -100;
            //    else if (curMove.isCapture) moveSortingArray[m] = -10;
            //}
            //Array.Sort(moveSortingArray, moveSortingArrayIndexes);
            //
            //#endregion

            for (int m = 0; m < molc; m++)
            {
                Move curMove = moveOptionList[m];

                int tPieceType = curMove.pieceType, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPTI = pieceTypeArray[tEndPos], tCheckPos = -1, tI, tPossibleAttackPiece;

                #region MakeMove()

                fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
                whitePieceBitboard = tWPB ^ curMove.ownPieceBitboardXOR;
                pieceTypeArray[tEndPos] = tPieceType;
                pieceTypeArray[tStartPos] = 0;
                zobristKey = tZobristKey ^ pieceHashesWhite[tStartPos, tPieceType] ^ pieceHashesWhite[tEndPos, tPieceType];
                switch (curMove.moveTypeID)
                {
                    case 5: // Standard-Pawn-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePawnAttackSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 6: // Standard-Knight-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(knightSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 7: // Standard-King-Capture
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[whiteKingSquare = tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 8: // Standard-Rook-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tStartPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tStartPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 9: // Standard-Standard-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 12: // En-Passant
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[curMove.enPassantOption] = 0;
                        zobristKey ^= pieceHashesBlack[curMove.enPassantOption, 1];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePawnAttackSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | curMove.enPassantOption] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 14: // Capture-Promotion
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI] ^ pieceHashesWhite[tEndPos, 1] ^ pieceHashesWhite[tEndPos, curMove.promotionType];
                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (tPieceType == 2 && ((int)(knightSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        break;
                }

                #endregion

                //debugMoveList[pDepth + 100] = curMove;

                int tEval = QuiescenceBlack(pPly + 1, pAlpha, pBeta, pDepth - 1, tCheckPos, curMove);

                #region UndoMove()

                pieceTypeArray[tEndPos] = tPTI;
                pieceTypeArray[tStartPos] = tPieceType;
                whiteCastleRightKingSide = tWKSCR;
                whiteCastleRightQueenSide = tWQSCR;
                blackCastleRightKingSide = tBKSCR;
                blackCastleRightQueenSide = tBQSCR;
                switch (curMove.moveTypeID)
                {
                    case 7: // Standard-King-Capture
                        whiteKingSquare = tWhiteKingSquare;
                        break;
                    case 12: // En-Passant
                        pieceTypeArray[curMove.enPassantOption] = 1;
                        break;
                    case 14: // Capture-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                }

                #endregion

                if (tEval >= pBeta) return pBeta;
                if (pAlpha < tEval) pAlpha = tEval;
            }

            isWhiteToMove = true;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            return pAlpha;
        }

        private int MinimaxBlack(int pPly, int pAlpha, int pBeta, int pDepth, int pRepetitionHistoryPly, int pCheckingSquare, Move pLastMove)
        {
            if (IsDrawByRepetition(pRepetitionHistoryPly - 4)) return 0;
            if ((pDepth <= 0 && pCheckingSquare == -1) || pDepth < CHECK_EXTENSION_LENGTH) return QuiescenceBlack(pPly, pAlpha, pBeta, pDepth - 1, pCheckingSquare, pLastMove);

            #region NodePrep()

            List<Move> moveOptionList = new List<Move>();
            Move bestMove = NULL_MOVE;
            if (pLastMove.isPromotion && pLastMove.isCapture && pLastMove.startPos % 8 == pCheckingSquare % 8 && pLastMove.startPos == blackKingSquare - 8 && (pLastMove.promotionType == 4 || pLastMove.promotionType == 5)) GetLegalBlackMovesSpecialDoubleCheckCase(ref moveOptionList);
            else GetLegalBlackMoves(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tBlackKingSquare = blackKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1, curEval = WHITE_CHECKMATE_VAL + pDepth;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;
            enPassantSquare = 65;
            isWhiteToMove = false;

            #endregion

            #region MoveSort()

            double[] moveSortingArray = new double[molc];
            int[] moveSortingArrayIndexes = new int[molc], thisSearchMoveSortingArrayForTransposEntry = new int[molc];
            transpositionTable.TryGetValue(zobristKey, out TranspositionEntry transposEntry);
            if (transposEntry == null)
            {
                for (int m = 0; m < molc; m++)
                {
                    moveSortingArrayIndexes[m] = m;
                    Move curMove = moveOptionList[m];
                    if (curMove.isCapture) moveSortingArray[m] = CAPTURE_SORT_VAL - MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
                    else if (killerMoveHeuristic[curMove.moveHash]) moveSortingArray[m] = KILLERMOVE_SORT_VAL;
                    else moveSortingArray[m] = -historyHeuristic[curMove.moveHash];
                    //moveSortingArray[m] = 
                }
            }
            else
            {
                for (int m = 0; m < molc; m++)
                {
                    moveSortingArrayIndexes[m] = m;
                    Move curMove = moveOptionList[m];
                    if (curMove == transposEntry.bestMove) moveSortingArray[m] = BESTMOVE_SORT_VAL;
                    else if (curMove.isCapture) moveSortingArray[m] = CAPTURE_SORT_VAL - MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
                    else if (killerMoveHeuristic[curMove.moveHash]) moveSortingArray[m] = KILLERMOVE_SORT_VAL;
                    else moveSortingArray[m] = -historyHeuristic[curMove.moveHash];
                    if (transposEntry.moveGenOrderedEvalLength > m) moveSortingArray[m] -= transposEntry.moveGenOrderedEvals[m];
                }
            }

            Array.Sort(moveSortingArray, moveSortingArrayIndexes);

            #endregion

            for (int m = 0; m < molc; m++)
            {
                int tActualIndex = moveSortingArrayIndexes[m];
                Move curMove = moveOptionList[tActualIndex];

                if (debugSortResults && pDepth == curSearchDepth)
                {
                    Console.WriteLine("=== " + moveSortingArray[m] + " | " + curMove);
                }

                int tPieceType = curMove.pieceType, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPTI = pieceTypeArray[tEndPos], tCheckPos = -1, tI, tPossibleAttackPiece;

                #region MakeMove()

                fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
                blackPieceBitboard = tBPB ^ curMove.ownPieceBitboardXOR;
                pieceTypeArray[tEndPos] = tPieceType;
                pieceTypeArray[tStartPos] = 0;
                zobristKey = tZobristKey ^ pieceHashesBlack[tStartPos, tPieceType] ^ pieceHashesBlack[tEndPos, tPieceType];
                switch (curMove.moveTypeID)
                {
                    case 0: // Standard-Standard-Move
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 1: // Standard-Pawn-Move
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPawnAttackSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 2: // Standard-Knight-Move
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(knightSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 3: // Standard-King-Move
                        blackKingSquare = tEndPos;
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 4: // Standard-Rook-Move
                        whitePieceBitboard = tWPB;
                        if (blackCastleRightQueenSide && tStartPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tStartPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 5: // Standard-Pawn-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPawnAttackSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 6: // Standard-Knight-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(knightSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 7: // Standard-King-Capture
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[blackKingSquare = tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 8: // Standard-Rook-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tStartPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tStartPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 9: // Standard-Standard-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 10: // Double-Pawn-Move
                        zobristKey ^= enPassantSquareHashes[enPassantSquare = curMove.enPassantOption];
                        whitePieceBitboard = tWPB;
                        fiftyMoveRuleCounter = 0;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPawnAttackSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 11: // Rochade
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        blackKingSquare = tEndPos;
                        whitePieceBitboard = tWPB;
                        pieceTypeArray[curMove.rochadeEndPos] = 4;
                        pieceTypeArray[curMove.rochadeStartPos] = 0;
                        zobristKey ^= pieceHashesBlack[curMove.rochadeStartPos, 4] ^ pieceHashesBlack[curMove.rochadeEndPos, 4];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | curMove.rochadeEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << curMove.rochadeEndPos)) tCheckPos = curMove.rochadeEndPos;
                        break;
                    case 12: // En-Passant
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[curMove.enPassantOption] = 0;
                        zobristKey ^= pieceHashesWhite[curMove.enPassantOption, 1];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPawnAttackSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | curMove.enPassantOption] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 13: // Standard-Promotion
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        zobristKey ^= pieceHashesBlack[tEndPos, 1] ^ pieceHashesBlack[tEndPos, curMove.promotionType];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && ((int)(knightSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        break;
                    case 14: // Capture-Promotion
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI] ^ pieceHashesBlack[tEndPos, 1] ^ pieceHashesBlack[tEndPos, curMove.promotionType];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (tPieceType == 2 && ((int)(knightSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        break;
                }
                curSearchZobristKeyLine[pRepetitionHistoryPly] = zobristKey;

                #endregion

                //debugMoveList[pDepth + 100] = curMove;

                int tEval = MinimaxWhite(pPly + 1, pAlpha, pBeta, pDepth - 1, pRepetitionHistoryPly + 1, tCheckPos, curMove);
                thisSearchMoveSortingArrayForTransposEntry[tActualIndex] = tEval;

                //if (curSearchDepth == pDepth) Console.WriteLine("=== " + tEval + " >> " + curMove);
                //else if (curSubSearchDepth == pDepth) Console.WriteLine("" + tEval + " > " + curMove);

                #region UndoMove()

                pieceTypeArray[tEndPos] = tPTI;
                pieceTypeArray[tStartPos] = tPieceType;
                whiteCastleRightKingSide = tWKSCR;
                whiteCastleRightQueenSide = tWQSCR;
                blackCastleRightKingSide = tBKSCR;
                blackCastleRightQueenSide = tBQSCR;
                switch (curMove.moveTypeID)
                {
                    case 3: // Standard-King-Move
                        blackKingSquare = tBlackKingSquare;
                        break;
                    case 7: // Standard-King-Capture
                        blackKingSquare = tBlackKingSquare;
                        break;
                    case 10: // Double-Pawn-Move
                        enPassantSquare = 65;
                        break;
                    case 11: // Rochade
                        blackKingSquare = tBlackKingSquare;
                        pieceTypeArray[curMove.rochadeStartPos] = 4;
                        pieceTypeArray[curMove.rochadeEndPos] = 0;
                        break;
                    case 12: // En-Passant
                        pieceTypeArray[curMove.enPassantOption] = 1;
                        break;
                    case 13: // Standard-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                    case 14: // Capture-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                }

                #endregion

                if (tEval < curEval)
                {
                    bestMove = curMove;
                    curEval = tEval;
                }
                if (pBeta > curEval) pBeta = curEval;
                if (curEval <= pAlpha)
                {
                    if (!curMove.isCapture)
                    {
                        killerMoveHeuristic[curMove.moveHash] = true;
                        if (pDepth > 0) historyHeuristic[curMove.moveHash] += pDepth * pDepth;
                    }
                    //if (pDepth > 0) historyHeuristic[lastMadeMove.moveHash] += pDepth * pDepth;
                    //thisSearchMoveSortingArrayForTransposEntry[tActualIndex] += KILLERMOVE_SORT_VAL;
                    //if (pDepth >= curSubSearchDepth) Console.WriteLine("* Alpha Cutoff");
                    break;
                }
            }

            isWhiteToMove = false;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            if (molc == 0 && pCheckingSquare == 0) curEval = 0;

            if (!transpositionTable.ContainsKey(zobristKey)) transpositionTable.Add(zobristKey, new TranspositionEntry(bestMove, thisSearchMoveSortingArrayForTransposEntry));
            else transpositionTable[zobristKey] = new TranspositionEntry(bestMove, thisSearchMoveSortingArrayForTransposEntry);

            return curEval;
        }

        private int QuiescenceBlack(int pPly, int pAlpha, int pBeta, int pDepth, int pCheckingSquare, Move pLastMove)
        {
            int standPat = Evaluate();
            if (standPat <= pAlpha || pDepth < MAX_QUIESCENCE_TOTAL_LENGTH) return pAlpha;
            if (standPat < pBeta) pBeta = standPat;

            #region NodePrep()

            List<Move> moveOptionList = new List<Move>();

            if (pLastMove.isPromotion && pLastMove.isCapture && pLastMove.startPos % 8 == pCheckingSquare % 8 && pLastMove.startPos == blackKingSquare - 8 && (pLastMove.promotionType == 4 || pLastMove.promotionType == 5)) GetLegalBlackCapturesSpecialDoubleCheckCase(ref moveOptionList);
            else GetLegalBlackCaptures(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tBlackKingSquare = blackKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;
            enPassantSquare = 65;
            isWhiteToMove = false;

            #endregion

            //#region MoveSort()
            //
            //double[] moveSortingArray = new double[molc];
            //int[] moveSortingArrayIndexes = new int[molc];
            //transpositionTable.TryGetValue(zobristKey, out Move? pvNodeMove);
            //for (int m = 0; m < molc; m++)
            //{
            //    moveSortingArrayIndexes[m] = m;
            //    Move curMove = moveOptionList[m];
            //    if (curMove == pvNodeMove) moveSortingArray[m] = -100;
            //    else if (curMove.isCapture) moveSortingArray[m] = -10;
            //}
            //Array.Sort(moveSortingArray, moveSortingArrayIndexes);
            //
            //#endregion

            for (int m = 0; m < molc; m++)
            {
                Move curMove = moveOptionList[m];
                int tPieceType = curMove.pieceType, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPTI = pieceTypeArray[tEndPos], tCheckPos = -1, tI, tPossibleAttackPiece;

                #region MakeMove()

                fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
                blackPieceBitboard = tBPB ^ curMove.ownPieceBitboardXOR;
                pieceTypeArray[tEndPos] = tPieceType;
                pieceTypeArray[tStartPos] = 0;
                zobristKey = tZobristKey ^ pieceHashesBlack[tStartPos, tPieceType] ^ pieceHashesBlack[tEndPos, tPieceType];
                switch (curMove.moveTypeID)
                {
                    case 5: // Standard-Pawn-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPawnAttackSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 6: // Standard-Knight-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(knightSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 7: // Standard-King-Capture
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[blackKingSquare = tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 8: // Standard-Rook-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tStartPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tStartPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 9: // Standard-Standard-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 12: // En-Passant
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[curMove.enPassantOption] = 0;
                        zobristKey ^= pieceHashesWhite[curMove.enPassantOption, 1];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPawnAttackSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | curMove.enPassantOption] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 14: // Capture-Promotion
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI] ^ pieceHashesBlack[tEndPos, 1] ^ pieceHashesBlack[tEndPos, curMove.promotionType];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (tPieceType == 2 && ((int)(knightSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        break;
                }

                #endregion

                //debugMoveList[pDepth + 100] = curMove;

                int tEval = QuiescenceWhite(pPly + 1, pAlpha, pBeta, pDepth - 1, tCheckPos, curMove);

                #region UndoMove()

                pieceTypeArray[tEndPos] = tPTI;
                pieceTypeArray[tStartPos] = tPieceType;
                whiteCastleRightKingSide = tWKSCR;
                whiteCastleRightQueenSide = tWQSCR;
                blackCastleRightKingSide = tBKSCR;
                blackCastleRightQueenSide = tBQSCR;
                switch (curMove.moveTypeID)
                {
                    case 7: // Standard-King-Capture
                        blackKingSquare = tBlackKingSquare;
                        break;
                    case 12: // En-Passant
                        pieceTypeArray[curMove.enPassantOption] = 1;
                        break;
                    case 14: // Capture-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                }

                #endregion

                if (tEval <= pAlpha) return pAlpha;
                if (pBeta > tEval) pBeta = tEval;
            }

            isWhiteToMove = false;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            return pBeta;
        }

        #endregion

        #region | HEURISTICS |

        private bool[] killerMoveHeuristic = new bool[262_144];
        private int[] historyHeuristic = new int[262_144];

        public void ClearHeuristics()
        {
            for (int i = 0; i < 262_144; i++)
            {
                killerMoveHeuristic[i] = false;
                historyHeuristic[i] = 0;
            }
        }

        #endregion

        #region | EVALUATION |

        private Dictionary<ulong, TranspositionEntry> transpositionTable = new Dictionary<ulong, TranspositionEntry>();

        private int[] pieceEvals = new int[14] { 0, 100, 300, 320, 500, 900, 0, 0, -100, -300, -320, -500, -900, 0 };
        //private int[,,] pawnPositionTable = new int[32, 6, 64];
        private int[,][] piecePositionEvals = new int[33, 14][];

        private int evalCount = 0;

        private int Evaluate()
        {
            evalCount++;
            if (fiftyMoveRuleCounter > 99) return 0;

            int tEval = 0, tPT, pieceCount = ULONG_OPERATIONS.CountBits(allPieceBitboard);

            for (int p = 0; p < 64; p++)
            {
                tEval += pieceEvals[tPT = pieceTypeArray[p] + 7 * ((int)(blackPieceBitboard >> p) & 1)] + piecePositionEvals[pieceCount, tPT][p];
            }

            return tEval;
        }

        private int GameState(bool pWhiteKingCouldBeAttacked)
        {
            if (IsDrawByRepetition(curSearchZobristKeyLine.Length - 5) || fiftyMoveRuleCounter > 99) return 0;

            int t;
            List<Move> tMoves = new List<Move>();

            if (pWhiteKingCouldBeAttacked)
            {
                GetLegalWhiteMoves(t = PreMinimaxCheckCheckWhite(), ref tMoves);
                //Console.WriteLine(t);
                if (t == -1) return tMoves.Count == 0 ? 0 : 3;
                else if (tMoves.Count == 0) return -1;
            }
            else
            {
                GetLegalBlackMoves(t = PreMinimaxCheckCheckBlack(), ref tMoves);
                //Console.WriteLine(t);
                if (t == -1) return tMoves.Count == 0 ? 0 : 3;
                else if (tMoves.Count == 0) return 1;
            }

            return 3;
        }

        private bool IsDrawByRepetition(int pPlyOfFirstPossibleRepeatedPosition)
        {
            int tC = 0;
            for (int i = pPlyOfFirstPossibleRepeatedPosition; i >= 0; i -= 2)
            {
                if (curSearchZobristKeyLine[i] == zobristKey)
                    if (++tC == 2) return true;
            }
            return false;
        }

        #endregion

        #region | INTERN REINFORCEMENT LEARNING |

        // DOESNT REALLY WORK; TEXEL TUNING will be the next try

        private const int RELE_MAX_MOVE_COUNT_PER_GAME = 500;

        public double ReLePlayGame(int[,][] pEvalPositionValuesWhite, int[,][] pEvalPositionValuesBlack, long thinkingTimePerMove)
        {
            int[,][] processedValuesWhite = InitReLeAgent(pEvalPositionValuesWhite), processedValuesBlack = InitReLeAgent(pEvalPositionValuesBlack);
            int tGS, mc = 0;

            do {
                if (IsDrawByRepetition(curSearchZobristKeyLine.Length - 5)) return 0d;
                piecePositionEvals = isWhiteToMove ? processedValuesWhite : processedValuesBlack;
                MinimaxRoot(thinkingTimePerMove);

                if (transpositionTable.Count == 0)
                {
                    Console.WriteLine("?!");
                    return 0d;
                }

                //Console.WriteLine(transpositionTable[zobristKey]);
                //Console.WriteLine(CreateFenString());
                PlainMakeMove(transpositionTable[zobristKey].bestMove);
                //Console.WriteLine(CreateFenString());
                tGS = GameState(isWhiteToMove);
                //Console.WriteLine("Gamestate: " + tGS);
                transpositionTable.Clear();
                if (tGS != 3) break;
            } while (mc++ < RELE_MAX_MOVE_COUNT_PER_GAME);

            if (tGS == 3 || tGS == 0) return 0d;
            else if (tGS == 1) return 0.1d * (double)(RELE_MAX_MOVE_COUNT_PER_GAME - mc);
            return 0.1d * (double)(-RELE_MAX_MOVE_COUNT_PER_GAME + mc);

        }

        public int[,][] InitReLeAgent(int[,][] pEvalPositionValues)
        {
            //int[,][] processedValues = new int[33, 14][];
            //for (int i = 0; i < 32; i++)
            //{
            //    int ip1 = i + 1;
            //    processedValues[ip1, 7] = processedValues[ip1, 0] = new int[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            //    processedValues[ip1, 8] = SwapArrayViewingSide(processedValues[ip1, 1] = pEvalPositionValues[i, 0]);
            //    processedValues[ip1, 9] = SwapArrayViewingSide(processedValues[ip1, 2] = pEvalPositionValues[i, 1]);
            //    processedValues[ip1, 10] = SwapArrayViewingSide(processedValues[ip1, 3] = pEvalPositionValues[i, 2]);
            //    processedValues[ip1, 11] = SwapArrayViewingSide(processedValues[ip1, 4] = pEvalPositionValues[i, 3]);
            //    processedValues[ip1, 12] = SwapArrayViewingSide(processedValues[ip1, 5] = pEvalPositionValues[i, 4]);
            //    processedValues[ip1, 13] = SwapArrayViewingSide(processedValues[ip1, 6] = pEvalPositionValues[i, 5]);
            //}
            return GetInterpolatedProcessedValues(pEvalPositionValues);
        }

        private double[] earlyGameMultipliers = new double[33];
        private double[] middleGameMultipliers = new double[33];
        private double[] lateGameMultipliers = new double[33];

        private void PrecalculateMultipliers()
        {
            for (int i = 0; i < 33; i++)
            {
                earlyGameMultipliers[i] = MultiplierFunction(i, 32d);
                middleGameMultipliers[i] = MultiplierFunction(i, 16d);
                lateGameMultipliers[i] = MultiplierFunction(i, 0d);
                //Console.WriteLine(earlyGameMultipliers[i] + " | " + middleGameMultipliers[i] + " | " + lateGameMultipliers[i]);
            }
        }

        private double MultiplierFunction(double pVal, double pXShift)
        {
            double d = Math.Exp(1d / 6d * (pVal - pXShift));
            return 5 * (d / Math.Pow(d + 1, 2d));
        }

        private int[,][] GetInterpolatedProcessedValues(int[,][] pEvalPositionValues)
        {
            int[,][] processedValues = new int[33, 14][];
            for (int i = 0; i < 32; i++)
            {
                int ip1 = i + 1;
                processedValues[ip1, 7] = processedValues[ip1, 0] = new int[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

                processedValues[ip1, 8] = SwapArrayViewingSide(processedValues[ip1, 1] = MultiplyArraysWithVal(pEvalPositionValues[0, 0], pEvalPositionValues[1, 0], pEvalPositionValues[2, 0], earlyGameMultipliers[ip1], middleGameMultipliers[ip1], lateGameMultipliers[ip1])); 
                processedValues[ip1, 9] = SwapArrayViewingSide(processedValues[ip1, 2] = MultiplyArraysWithVal(pEvalPositionValues[0, 1], pEvalPositionValues[1, 1], pEvalPositionValues[2, 1], earlyGameMultipliers[ip1], middleGameMultipliers[ip1], lateGameMultipliers[ip1])); 
                processedValues[ip1, 10] = SwapArrayViewingSide(processedValues[ip1, 3] = MultiplyArraysWithVal(pEvalPositionValues[0, 2], pEvalPositionValues[1, 2], pEvalPositionValues[2, 2], earlyGameMultipliers[ip1], middleGameMultipliers[ip1], lateGameMultipliers[ip1])); 
                processedValues[ip1, 11] = SwapArrayViewingSide(processedValues[ip1, 4] = MultiplyArraysWithVal(pEvalPositionValues[0, 3], pEvalPositionValues[1, 3], pEvalPositionValues[2, 3], earlyGameMultipliers[ip1], middleGameMultipliers[ip1], lateGameMultipliers[ip1])); 
                processedValues[ip1, 12] = SwapArrayViewingSide(processedValues[ip1, 5] = MultiplyArraysWithVal(pEvalPositionValues[0, 4], pEvalPositionValues[1, 4], pEvalPositionValues[2, 4], earlyGameMultipliers[ip1], middleGameMultipliers[ip1], lateGameMultipliers[ip1])); 
                processedValues[ip1, 13] = SwapArrayViewingSide(processedValues[ip1, 6] = MultiplyArraysWithVal(pEvalPositionValues[0, 5], pEvalPositionValues[1, 5], pEvalPositionValues[2, 5], earlyGameMultipliers[ip1], middleGameMultipliers[ip1], lateGameMultipliers[ip1])); 
                
                
                
                
                //MultiplyArrayWithVal(aEarly[0], earlyGameMultipliers[ip1]) + MultiplyArrayWithVal(aEarly[0], earlyGameMultipliers[ip1]) + MultiplyArrayWithVal(aEarly[0], earlyGameMultipliers[ip1]);

                //processedValues[ip1, 8] = SwapArrayViewingSide(processedValues[ip1, 1] = pEvalPositionValues[i, 0]);
                //processedValues[ip1, 9] = SwapArrayViewingSide(processedValues[ip1, 2] = pEvalPositionValues[i, 1]);
                //processedValues[ip1, 10] = SwapArrayViewingSide(processedValues[ip1, 3] = pEvalPositionValues[i, 2]);
                //processedValues[ip1, 11] = SwapArrayViewingSide(processedValues[ip1, 4] = pEvalPositionValues[i, 3]);
                //processedValues[ip1, 12] = SwapArrayViewingSide(processedValues[ip1, 5] = pEvalPositionValues[i, 4]);
                //processedValues[ip1, 13] = SwapArrayViewingSide(processedValues[ip1, 6] = pEvalPositionValues[i, 5]);
            }
            return processedValues;
        }

        private int[] MultiplyArraysWithVal(int[] pArr1, int[] pArr2, int[] pArr3, double pVal1, double pVal2, double pVal3)
        {
            int tL = pArr1.Length;
            int[] rArr = new int[tL];
            for (int i = 0; i < tL; i++)
            {
                rArr[i] = (int)(pArr1[i] * pVal1 + pArr2[i] * pVal2 + pArr3[i] * pVal3);
            }
            return rArr;
        }

        private int[] SwapArrayViewingSide(int[] pArr)
        {
            return new int[64]
            { 
                pArr[56], pArr[57], pArr[58], pArr[59], pArr[60], pArr[61], pArr[62], pArr[63],
                pArr[48], pArr[49], pArr[50], pArr[51], pArr[52], pArr[53], pArr[54], pArr[55],
                pArr[40], pArr[41], pArr[42], pArr[43], pArr[44], pArr[45], pArr[46], pArr[47],
                pArr[32], pArr[33], pArr[34], pArr[35], pArr[36], pArr[37], pArr[38], pArr[39],
                pArr[24], pArr[25], pArr[26], pArr[27], pArr[28], pArr[29], pArr[30], pArr[31],
                pArr[16], pArr[17], pArr[18], pArr[19], pArr[20], pArr[21], pArr[22], pArr[23],
                pArr[8],   pArr[9], pArr[10], pArr[11], pArr[12], pArr[13], pArr[14], pArr[15],
                pArr[0],   pArr[1],  pArr[2],  pArr[3],  pArr[4],  pArr[5],  pArr[6],  pArr[7]
            };
        }

        #endregion

        #region | FEN MANAGEMENT |

        private char[] fenPieces = new char[7] { 'z', 'p', 'n', 'b', 'r', 'q', 'k' };
        private string[] squareNames = new string[64] {
            "a1", "b1", "c1", "d1", "e1", "f1", "g1", "h1",
            "a2", "b2", "c2", "d2", "e2", "f2", "g2", "h2",
            "a3", "b3", "c3", "d3", "e3", "f3", "g3", "h3",
            "a4", "b4", "c4", "d4", "e4", "f4", "g4", "h4",
            "a5", "b5", "c5", "d5", "e5", "f5", "g5", "h5",
            "a6", "b6", "c6", "d6", "e6", "f6", "g6", "h6",
            "a7", "b7", "c7", "d7", "e7", "f7", "g7", "h7",
            "a8", "b8", "c8", "d8", "e8", "f8", "g8", "h8"
        };

        public string CreateFenString()
        {
            string rFEN = "";
            for (int i = 7; i >= 0; i--)
            {
                int t = 0;
                for (int j = 0; j < 8; j++)
                {
                    int sq = i * 8 + j;
                    if (pieceTypeArray[sq] != 0)
                    {
                        if (t != 0) rFEN += t;
                        t = 0;
                    }
                    if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, sq))
                    {
                        switch (pieceTypeArray[sq])
                        {
                            case 0: t++; Console.WriteLine("???"); break;
                            case 1: rFEN += 'P'; break;
                            case 2: rFEN += 'N'; break;
                            case 3: rFEN += 'B'; break;
                            case 4: rFEN += 'R'; break;
                            case 5: rFEN += 'Q'; break;
                            case 6: rFEN += 'K'; break;
                        }
                    }
                    else
                    {
                        switch (pieceTypeArray[sq])
                        {
                            case 0: t++; break;
                            case 1: rFEN += 'p'; break;
                            case 2: rFEN += 'n'; break;
                            case 3: rFEN += 'b'; break;
                            case 4: rFEN += 'r'; break;
                            case 5: rFEN += 'q'; break;
                            case 6: rFEN += 'k'; break;
                        }
                    }
                }
                if (t != 0) rFEN += t;
                if (i != 0) rFEN += "/";
            }

            rFEN += (isWhiteToMove ? " w" : " b");
            rFEN += (whiteCastleRightKingSide ? " K" : " ");
            rFEN += (whiteCastleRightQueenSide ? "Q" : "");
            rFEN += (blackCastleRightKingSide ? "k" : "");
            rFEN += (blackCastleRightQueenSide ? "q" : "");
            if (!whiteCastleRightKingSide && !whiteCastleRightQueenSide && !blackCastleRightKingSide && !blackCastleRightQueenSide) rFEN += "-";

            if (enPassantSquare < 64) rFEN += " " + squareNames[enPassantSquare] + " ";
            else rFEN += " - ";
            //= (epstr[0] - 'a') + 8 * (epstr[1] - '1');
            return rFEN + fiftyMoveRuleCounter + " " + ((happenedHalfMoves - happenedHalfMoves % 2) / 2);
        }

        public void LoadFenString(string fenStr)
        {
            lastMadeMove = NULL_MOVE;
            zobristKey = 0ul;
            whitePieceBitboard = blackPieceBitboard = allPieceBitboard = 0ul;

            for (int i = 0; i < 64; i++) pieceTypeArray[i] = 0;

            string[] spaceSpl = fenStr.Split(' ');
            string[] rowSpl = spaceSpl[0].Split('/');

            string epstr = spaceSpl[3];
            if (epstr == "-") enPassantSquare = 65;
            else enPassantSquare = (epstr[0] - 'a') + 8 * (epstr[1] - '1');

            isWhiteToMove = true;
            if (spaceSpl[1] == "b") isWhiteToMove = false;

            if (!isWhiteToMove) zobristKey ^= blackTurnHash;

            string crstr = spaceSpl[2];
            whiteCastleRightKingSide = whiteCastleRightQueenSide = blackCastleRightKingSide = blackCastleRightQueenSide = false;
            int crStrL = crstr.Length;
            for (int i = 0; i < crStrL; i++) {
                switch (crstr[i])
                {
                    case 'K':
                        whiteCastleRightKingSide = true;
                        break;
                    case 'Q':
                        whiteCastleRightQueenSide = true;
                        break;
                    case 'k':
                        blackCastleRightKingSide = true;
                        break;
                    case 'q':
                        blackCastleRightQueenSide = true;
                        break;
                }
            }

            fiftyMoveRuleCounter = Convert.ToInt32(spaceSpl[4]);
            happenedHalfMoves = Convert.ToInt32(spaceSpl[5]) * 2;
            if (!isWhiteToMove) happenedHalfMoves++;

            int tSq = 0;
            for (int i = rowSpl.Length; i-- > 0; )
            {
                string tStr = rowSpl[i];
                int tStrLen = tStr.Length;
                for (int c = 0; c < tStrLen; c++)
                {
                    char tChar = tStr[c];
                    if (Char.IsDigit(tChar)) tSq += tChar - '0';
                    else
                    {
                        bool tCol = Char.IsLower(tChar);
                        if (tCol) blackPieceBitboard = ULONG_OPERATIONS.SetBitToOne(blackPieceBitboard, tSq);
                        else whitePieceBitboard = ULONG_OPERATIONS.SetBitToOne(whitePieceBitboard, tSq);

                        if (tChar == 'k') blackKingSquare = tSq;
                        else if (tChar == 'K') whiteKingSquare = tSq;

                        pieceTypeArray[tSq] = Array.IndexOf(fenPieces, Char.ToLower(tChar));

                        if (tCol) zobristKey ^= pieceHashesBlack[tSq, pieceTypeArray[tSq]]; 
                        else zobristKey ^= pieceHashesWhite[tSq, pieceTypeArray[tSq]]; 

                        tSq++;
                    }
                }
            }

            if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
            if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
            if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
            if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;

            zobristKey ^= enPassantSquareHashes[enPassantSquare];

            allPieceBitboard = whitePieceBitboard | blackPieceBitboard;
        }

        #endregion

        #region | PRECALCULATIONS |

        private ulong[,] pieceHashesWhite = new ulong[64, 7], pieceHashesBlack = new ulong[64, 7];
        private ulong blackTurnHash, whiteKingSideRochadeRightHash, whiteQueenSideRochadeRightHash, blackKingSideRochadeRightHash, blackQueenSideRochadeRightHash;
        private ulong[] enPassantSquareHashes = new ulong[66];
        private void InitZobrist()
        {
            Random rng = new Random(31415926);
            for (int sq = 0; sq < 64; sq++)
            {
                enPassantSquareHashes[sq] = ULONG_OPERATIONS.GetRandomULONG(rng);
                for (int it = 1; it < 7; it++)
                {
                    pieceHashesWhite[sq, it] = ULONG_OPERATIONS.GetRandomULONG(rng);
                    pieceHashesBlack[sq, it] = ULONG_OPERATIONS.GetRandomULONG(rng);
                }
            }
            blackTurnHash = ULONG_OPERATIONS.GetRandomULONG(rng);
            whiteKingSideRochadeRightHash = ULONG_OPERATIONS.GetRandomULONG(rng);
            whiteQueenSideRochadeRightHash = ULONG_OPERATIONS.GetRandomULONG(rng);
            blackKingSideRochadeRightHash = ULONG_OPERATIONS.GetRandomULONG(rng);
            blackQueenSideRochadeRightHash = ULONG_OPERATIONS.GetRandomULONG(rng);
        }

        public void SetKnightMasks(ulong[] uls)
        {
            knightSquareBitboards = uls;
        }

        public void SetKingMasks(ulong[] uls)
        {
            kingSquareBitboards = uls;
        }

        private Move[,] whiteEnPassantMoves = new Move[64, 64];
        private Move[,] blackEnPassantMoves = new Move[64, 64];

        private bool[] epValidationArray = new bool[64]
        { false, false, false, false, false, false, false, false,
          false, false, false, false, false, false, false, false,
          true , true , true , true , true , true , true , true,
          true , true , true , true , true , true , true , true,
          true , true , true , true , true , true , true , true,
          true , true , true , true , true , true , true , true,
          false, false, false, false, false, false, false, false,
          false, false, false, false, false, false, false, false
        };

        private void PrecalculateEnPassantMoves()
        {
            for (int i = 0; i < 64; i++)
            {
                if (!epValidationArray[i]) continue;
                for (int j = 0; j < 64; j++)
                {
                    if (i == j) continue;
                    if (Math.Abs(i - j) > 9) continue; 
                    if (!epValidationArray[j]) continue;

                    whiteEnPassantMoves[i, j] = new Move(false, i, j, j - 8);
                    blackEnPassantMoves[i, j] = new Move(false, i, j, j + 8);
                }
            }
        }

        private void PawnAttackBitboards()
        {
            for (int sq = 0; sq < 64; sq++)
            {
                ulong u = 0ul;
                if (sq % 8 != 0) u = ULONG_OPERATIONS.SetBitToOne(u, sq + 7);
                if (sq % 8 != 7) u = ULONG_OPERATIONS.SetBitToOne(u, sq + 9);
                whitePawnAttackSquareBitboards[sq] = u;
                u = 0ul;
                if (sq % 8 != 0 && sq - 9 > -1) u = ULONG_OPERATIONS.SetBitToOne(u, sq - 9);
                if (sq % 8 != 7 && sq - 7 > -1) u = ULONG_OPERATIONS.SetBitToOne(u, sq - 7);
                blackPawnAttackSquareBitboards[sq] = u;
            }
        }

        private int[] squareConnectivesPrecalculationArray = new int[4096];
        public int[] squareConnectivesCrossDirsPrecalculationArray = new int[4096];
        private ulong[] squareConnectivesPrecalculationRayArray = new ulong[4096];
        private ulong[] squareConnectivesPrecalculationLineArray = new ulong[4096];

        private List<Dictionary<ulong, int>> rayCollidingSquareCalculations = new List<Dictionary<ulong, int>>();

        private readonly int[] maxIntWithSpecifiedBitcount = new int[8] { 0, 0b1, 0b11, 0b111, 0b1111, 0b11111, 0b111111, 0b1111111 };

        private List<ulong> differentRays = new List<ulong>();

        private void SquareConnectivesPrecalculations()
        {
            for (int square = 0; square < 64; square++)
            {
                rayCollidingSquareCalculations.Add(new Dictionary<ulong, int>());
                rayCollidingSquareCalculations[square].Add(0ul, square);
                for (int square2 = 0; square2 < 64; square2++)
                {
                    if (square == square2) continue;

                    int t = 0, t2 = 0, difSign = Math.Sign(square2 - square), itSq = square, g = 0;
                    ulong tRay = 0ul, tExclRay = 0ul;
                    List<int> tRayInts = new List<int>();
                    if (difSign == -1) g = 7;

                    if (square % 8 == square2 % 8) // Untere oder Obere Gerade
                    {
                        t2 = t = 1;
                        while ((itSq += difSign * 8) < 64 && itSq > -1)
                        {
                            tRay = ULONG_OPERATIONS.SetBitToOne(tRay, itSq); tRayInts.Add(itSq);
                            if (itSq == square2) { tExclRay = tRay; }
                        }
                    }
                    else if ((square - square % 8) == (square2 - square2 % 8)) // Linke oder Rechte Gerade
                    {
                        t = 1;
                        t2 = -1;
                        while ((itSq += difSign) % 8 != g && itSq > -1 && itSq < 64) { tRay = ULONG_OPERATIONS.SetBitToOne(tRay, itSq); tRayInts.Add(itSq); if (itSq == square2) { tExclRay = tRay; } }
                    }
                    else
                    {
                        int dif = Math.Abs(square2 - square);
                        if (dif % 7 == 0) // Diagonalen von Rechts
                        {
                            t = 2;
                            t2 = 2;
                            g = (difSign == 1) ? 7 : 0;
                            while ((itSq += difSign * 7) % 8 != g && itSq < 64 && itSq > -1) { tRay = ULONG_OPERATIONS.SetBitToOne(tRay, itSq); tRayInts.Add(itSq); if (itSq == square2) { tExclRay = tRay; } }
                            if (ULONG_OPERATIONS.IsBitZero(tRay, square2))
                            {
                                t2 = t = 0;
                                tRay = 0ul;
                            }
                        }
                        else if (dif % 9 == 0) //Diagonalen von Links
                        {
                            t = 2;
                            t2 = -2;
                            if (square == 0 && square2 == 63 || square == 63 && square2 == 0) Console.WriteLine("!");
                            while ((itSq += difSign * 9) % 8 != g && itSq < 64 && itSq > -1) { tRay = ULONG_OPERATIONS.SetBitToOne(tRay, itSq); tRayInts.Add(itSq); if (itSq == square2) { tExclRay = tRay; } }
                            if (ULONG_OPERATIONS.IsBitZero(tRay, square2))
                            {
                                t2 = t = 0;
                                tRay = 0ul;
                            }
                        }
                    }

                    if (square == 0 && square2 == 63)
                    {
                        t2 = t = 2;
                        tRayInts.Clear();
                        tExclRay = tRay = ULONG_OPERATIONS.SetBitsToOne(0ul, 9, 18, 27, 36, 45, 54, 63);
                        tRayInts = new List<int>() { 9, 18, 27, 36, 45, 54, 63 };
                    }
                    else if (square2 == 0 && square == 63)
                    {
                        t = 2;
                        tRayInts.Clear();
                        t2 = -2;
                        tExclRay = tRay = ULONG_OPERATIONS.SetBitsToOne(0ul, 0, 9, 18, 27, 36, 45, 54);
                        tRayInts = new List<int>() { 54, 45, 36, 27, 18, 9, 0 }; //0, 9, 18, 27, 36, 45, 54
                    }

                    if (tRay != 0ul && !differentRays.Contains(tRay))
                    {
                        differentRays.Add(tRay);
                        int ccount, combI = maxIntWithSpecifiedBitcount[ccount = ULONG_OPERATIONS.CountBits(tRay)];
                        do
                        {
                            ulong curAllPieceBitboard = 0ul;
                            int solution = square;
                            for (int j = 0; j < ccount; j++)
                            {
                                if (((combI >> j) & 1) == 1)
                                {
                                    curAllPieceBitboard = ULONG_OPERATIONS.SetBitToOne(curAllPieceBitboard, tRayInts[j]);
                                    if (solution == square) solution = tRayInts[j];
                                }
                            }
                            //if (curAllPieceBitboard == 36028797018963968) Console.WriteLine(square + " -> " + square2);
                            rayCollidingSquareCalculations[square].Add(curAllPieceBitboard, solution);
                        } while (--combI != 0);
                    }
                    squareConnectivesPrecalculationArray[square << 6 | square2] = t;
                    squareConnectivesPrecalculationRayArray[square << 6 | square2] = tRay;
                    squareConnectivesCrossDirsPrecalculationArray[square << 6 | square2] = t2;
                    if (t != 0) squareConnectivesPrecalculationLineArray[square << 6 | square2] = (square == 0 && square2 == 63 || square2 == 0 && square == 63) ? tExclRay : ULONG_OPERATIONS.SetBitToOne(tExclRay, square2);
                    if (squareConnectivesPrecalculationLineArray[square << 6 | square2] == 0ul && ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[square], square2)) squareConnectivesPrecalculationLineArray[square << 6 | square2] = 1ul << square2;
                }
                differentRays.Clear();
            }

            // Der folgende Code war zum Herausfinden von den zwei Spezial Line Cases: 0 -> 63 & 63 -> 0

            /*ulong[] testArray = new ulong[4096];
            for (int s = 0; s < 64; s++)
            {
                ulong u = 0ul;
                for (int t = s + 9; t < 64 && t % 8 != 0; t += 9)
                { u = ULONG_OPERATIONS.SetBitToOne(u, t); testArray[s << 6 | t] = u; }
                u = 0ul;
                for (int t = s - 9; t > -1 && t % 8 != 7; t -= 9)
                { u = ULONG_OPERATIONS.SetBitToOne(u, t); testArray[s << 6 | t] = u; }
                u = 0ul;
                for (int t = s + 7; t < 64 && t % 8 != 7; t += 7)
                { u = ULONG_OPERATIONS.SetBitToOne(u, t); testArray[s << 6 | t] = u; }
                u = 0ul;
                for (int t = s - 7; t > -1 && t % 8 != 0; t -= 7)
                { u = ULONG_OPERATIONS.SetBitToOne(u, t); testArray[s << 6 | t] = u; }
                u = 0ul;
                int rsv = s - s % 8;
                for (int t = s - 1; t % 8 != 7 && t > -1; t--)
                { u = ULONG_OPERATIONS.SetBitToOne(u, t); testArray[s << 6 | t] = u; }
                u = 0ul;
                rsv += 8;
                for (int t = s + 1; t != rsv; t++)
                { u = ULONG_OPERATIONS.SetBitToOne(u, t); testArray[s << 6 | t] = u; }
                u = 0ul;
                for (int t = s + 8; t < 64; t += 8)
                { u = ULONG_OPERATIONS.SetBitToOne(u, t); testArray[s << 6 | t] = u; }
                u = 0ul;
                for (int t = s - 8; t > -1; t -= 8)
                { u = ULONG_OPERATIONS.SetBitToOne(u, t); testArray[s << 6 | t] = u; }
            }

            for (int i = 0; i < 4096; i++)
            {
                if (testArray[i] == squareConnectivesPrecalculationLineArray[i]) continue;
                Console.WriteLine((i >> 6) + " -> " + (i & 0b111111));
                Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(squareConnectivesPrecalculationLineArray[i]));
                Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(testArray[i]));
                //break;
            }*/
        }

        #endregion

        #region | UTILITY |

        private string ReplaceAllULONGIsBitOne(string pCode)
        {
            const string METHOD_NAME = "ULONG_OPERATIONS.IsBitOne";
            int METHOD_NAME_LEN = METHOD_NAME.Length, iterations = 0;

            int pIndex = pCode.IndexOf(METHOD_NAME);
            while (pIndex != -1 && ++iterations < 10_000) {
                List<int> paramIndexes = new List<int>();
                int openedBracketCount = 1, closedBracketCount = 0, a = METHOD_NAME_LEN + pIndex;
                string tpCode = pCode.Substring(0, pIndex);
                paramIndexes.Add(a + 1);
                while (++a < pCode.Length)
                {
                    switch(pCode[a])
                    {
                        case '(':
                            openedBracketCount++;
                            break;
                        case ')':
                            if (++closedBracketCount == openedBracketCount) { pIndex = a; a = int.MaxValue - 5;}
                            break;
                        case ',':
                            if (openedBracketCount - closedBracketCount == 1) paramIndexes.Add(a + 1);
                            break;
                    }
                }

                if (openedBracketCount == closedBracketCount && paramIndexes.Count == 2)
                {
                    tpCode += "((int)(" + pCode.Substring(paramIndexes[0], paramIndexes[1] - paramIndexes[0] - 1) +
                    (pCode[paramIndexes[1]] == ' ' ? " >> (" : " >> (") + pCode.Substring(paramIndexes[1], pIndex - paramIndexes[1])
                    + ")) & 1) == 1" + pCode.Substring(pIndex + 1, pCode.Length - pIndex - 1);
                }
                pIndex = (pCode = tpCode).IndexOf(METHOD_NAME);
            }
            Console.WriteLine(iterations + " ITERATIONS");
            return pCode;
        }

        private string ReplaceAllULONGIsBitZero(string pCode)
        {
            const string METHOD_NAME = "ULONG_OPERATIONS.IsBitZero";
            int METHOD_NAME_LEN = METHOD_NAME.Length, iterations = 0;

            int pIndex = pCode.IndexOf(METHOD_NAME);
            while (pIndex != -1 && ++iterations < 10_000)
            {
                List<int> paramIndexes = new List<int>();
                int openedBracketCount = 1, closedBracketCount = 0, a = METHOD_NAME_LEN + pIndex;
                string tpCode = pCode.Substring(0, pIndex);
                paramIndexes.Add(a + 1);
                while (++a < pCode.Length)
                {
                    switch (pCode[a])
                    {
                        case '(':
                            openedBracketCount++;
                            break;
                        case ')':
                            if (++closedBracketCount == openedBracketCount) { pIndex = a; a = int.MaxValue - 5; }
                            break;
                        case ',':
                            if (openedBracketCount - closedBracketCount == 1) paramIndexes.Add(a + 1);
                            break;
                    }
                }
                if (openedBracketCount == closedBracketCount && paramIndexes.Count == 2)
                {
                    tpCode += "((int)(" + pCode.Substring(paramIndexes[0], paramIndexes[1] - paramIndexes[0] - 1) +
                    (pCode[paramIndexes[1]] == ' ' ? " >>" : " >> ") + pCode.Substring(paramIndexes[1], pIndex - paramIndexes[1])
                    + ") & 1) == 0" + pCode.Substring(pIndex + 1, pCode.Length - pIndex - 1);
                }
                pIndex = (pCode = tpCode).IndexOf(METHOD_NAME);
            }
            Console.WriteLine(iterations + " ITERATIONS");
            return pCode;
        }

        private string GetThreeDigitSeperatedInteger(int pInt)
        {
            string s = pInt.ToString(), r = s[0].ToString();
            int t = s.Length % 3;

            for (int i = 1; i < s.Length; i++)
            {
                if (i % 3 == t) r += ".";
                r += s[i];
            }

            s = "";
            for (int i = 0; i < r.Length; i++) s += r[i];

            return s;
        }

        public double GetAverageDepth()
        {
            return (double)depths / (double)searches;
        }

        #endregion
    }

    #region | REINFORCEMENT LEARNING |

    public static class ReLe_AI_VARS
    {
        public const double MUTATION_PROPABILITY = 0.01;

        public const int GENERATION_SIZE = 12;
        public const int GENERATION_SURVIVORS = 4; //n^2-n = spots

        public const int GENERATION_GOAL_COUNT = 150; //n^2-n = spots
    }

    public class ReLe_AIHandler
    {
        private System.Random rng = new System.Random();
        private ReLe_AIGeneration curGen;

        public ReLe_AIHandler()
        {
            ReLe_AIEvaluator.fens = File.ReadAllLines(@"C:\Users\tpmen\Desktop\4 - Programming\41 - Unity & C#\MiluvaV3\Miluva-v3\Chessbot\FENS.txt");

            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 6; j++)
                    ReLe_AIEvaluator.oppAIValues[i,j] = new int[64] { 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 };
            
            curGen = new ReLe_AIGeneration(rng);
            for (int i = 0; i < ReLe_AI_VARS.GENERATION_GOAL_COUNT; i++)
            {
                ReLe_AIInstance[] topPerformingAIs = curGen.GetTopAIInstances();
                ClearTextFile();
                AppendToText("- - - { Generation " + (i + 1) + ": } - - -\n\n");
                foreach (ReLe_AIInstance instance in topPerformingAIs)
                {
                    AppendToText(instance.ToString() + "\n");
                    AppendToText(GetAIArrayValues(instance) + "\n");
                    //Console.WriteLine(instance.ToString());
                    //Console.WriteLine(GetAIArrayValues(instance));
                }
                Console.WriteLine(ReLe_AIEvaluator.boardManager.GetAverageDepth());
                curGen = new ReLe_AIGeneration(rng, topPerformingAIs);
            }
        }

        private string GetAIArrayValues(ReLe_AIInstance pAI)
        {
            string r = "int[,][] ReLe_AI_RESULT_VALS = new int[3, 6][] {";
            int[,][] tVals = pAI.digitArray;

            for (int i = 0; i < 3; i++)
            {
                r += "{";
                for (int j = 0; j < 5; j++)
                {
                    r += GetIntArray64LStringRepresentation(tVals[i, j]) + ",";
                }
                r += GetIntArray64LStringRepresentation(tVals[i, 5]) + "},";
            }

            return r.Substring(0, r.Length - 1) + "};";
        }

        private string GetIntArray64LStringRepresentation(int[] p64LArr)
        {
            string r = "new int[64]{";
            for (int i = 0; i < 63; i++)
            {
                r += p64LArr[i] + ",";
            }
            return r + p64LArr[63] + "}";
        }

        private static void AppendToText(string pText)
        {
            File.AppendAllText(@"C:\Users\tpmen\Desktop\ReLeResults.txt", pText);
        }

        private static void ClearTextFile()
        {
            File.WriteAllText(@"C:\Users\tpmen\Desktop\ReLeResults.txt", "");
        }
    }

    public class ReLe_AIGeneration
    {
        public ReLe_AIInstance[] generationInstances = new ReLe_AIInstance[ReLe_AI_VARS.GENERATION_SIZE];
        private double[] generationInstanceEvaluations = new double[ReLe_AI_VARS.GENERATION_SIZE];

        //Create Generation Randomly
        public ReLe_AIGeneration(System.Random rng)
        {
            for (int i = 0; i < ReLe_AI_VARS.GENERATION_SIZE; i++)
            {
                generationInstances[i] = new ReLe_AIInstance(rng);
            }
        }

        //Create Generation based on best previous results
        public ReLe_AIGeneration(System.Random rng, ReLe_AIInstance[] topInstancesOfLastGeneration) //Length needs to be optimally equal to the square root of the generation size
        {
            int a = 0;
            for (int i = 0; i < ReLe_AI_VARS.GENERATION_SURVIVORS; i++)
            {
                for (int j = 0; j < ReLe_AI_VARS.GENERATION_SURVIVORS; j++)
                {
                    if (j == i) continue;

                    generationInstances[a] = CombineTwoAIInstances(topInstancesOfLastGeneration[i], topInstancesOfLastGeneration[j], rng);
                    ++a;
                }
            }

            for (int i = a; i < ReLe_AI_VARS.GENERATION_SIZE; i++)
            {
                generationInstances[i] = new ReLe_AIInstance(rng);
            }
        }

        private ReLe_AIInstance CombineTwoAIInstances(ReLe_AIInstance ai1, ReLe_AIInstance ai2, System.Random rng)
        {
            int[,][] tempDigitArray = new int[3, 6][];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 6; j++)
                {
                    tempDigitArray[i, j] = new int[64];
                    for (int k = 0; k < 64; k++)
                    {
                        ReLe_AIInstance tAI = rng.NextDouble() < 0.5f ? ai1 : ai2;

                        //Mutations
                        if (rng.NextDouble() < ReLe_AI_VARS.MUTATION_PROPABILITY)
                        {
                            tempDigitArray[i, j][k] = Math.Clamp(tAI.digitArray[i, j][k] + rng.Next(-20, 20), 0, 150);
                            continue;
                        }

                        //Combination
                        //tempDigitArray[i, j][k] = (rng.NextDouble() < 0.5) ? ai1.digitArray[i, j][k] : ai2.digitArray[i, j][k];

                        tempDigitArray[i, j][k] = Math.Clamp(tAI.digitArray[i, j][k] + rng.Next(-3, 3), 0, 150);

                        //tempDigitArray[i, j][k] = Math.Clamp((ai1.digitArray[i, j][k] + ai2.digitArray[i, j][k]) / 2 + rng.Next(-3, 3), 0, 150);
                    }
                }
            }
            return new ReLe_AIInstance(tempDigitArray);
        }

        public ReLe_AIInstance[] GetTopAIInstances()
        {
            //Evaluate every single instance
            for (int i = 0; i < ReLe_AI_VARS.GENERATION_SIZE; i++)
            {
                generationInstanceEvaluations[i] = ReLe_AIEvaluator.EvaluateAIInstance(generationInstances[i]);
            }

            //Sort all instances by the evaluation theyve gotten 
            Array.Sort(generationInstanceEvaluations, generationInstances);

            //Create the array with the length definited in the static var class

            int tL = generationInstanceEvaluations.Length - 1;
            ReLe_AIInstance[] returnInstances = new ReLe_AIInstance[ReLe_AI_VARS.GENERATION_SURVIVORS];
            for (int i = 0; i < ReLe_AI_VARS.GENERATION_SURVIVORS; i++)
            {
                returnInstances[i] = generationInstances[tL - i];
            }
            return returnInstances;
        }
    }

    public static class ReLe_AIEvaluator
    {
        public static int[,][] oppAIValues = new int[3, 6][];
        public static BoardManager boardManager;

        public static string[] fens = new string[10] {
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "r1bq1rk1/pp3ppp/2n1p3/3n4/1b1P4/2N2N2/PP2BPPP/R1BQ1RK1 w - - 0 10",
            "rn1q1rk1/pp2b1pp/3pbn2/4p3/8/1N1BB3/PPPN1PPP/R2Q1RK1 w - - 8 11",
            "1rbq1rk1/p3ppbp/3p1np1/2pP4/1nP5/RP3NP1/1BQNPPBP/4K2R w K - 1 13",
            "r1b2rk1/pppp1pp1/2n2q1p/8/1bP5/2N2N2/PP2PPPP/R2QKB1R w KQ - 0 9",
            "r2q1rk1/bppb1pp1/p2p2np/2PPp3/1P2P1n1/P3BN2/2Q1BPPP/RN3RK1 w - - 2 15",
            "rnbq1rk1/pp2b1pp/2p2n2/3p1p2/4p3/3PP1PP/PPPNNPB1/R1BQ1RK1 w - - 5 9",
            "rnbqk2r/5ppp/p2bpn2/1p6/2BP4/7P/PP2NPP1/RNBQ1RK1 w kq - 0 10",
            "rn2kb1r/1bqp1ppp/p3pn2/1p6/3NP3/2P1BB2/PP3PPP/RN1QK2R w KQkq - 6 9",
            "r1b1k2r/pp2bp1p/1qn1p3/2ppPp2/5P2/2PP1N1P/PP4P1/RNBQ1RK1 w kq - 1 11"
        };

        private static Random evalRNG = new Random();

        public static double EvaluateAIInstance(ReLe_AIInstance ai)
        {
            double eval = 0d;

            for (int i = 0; i < 10; i++)
            {
                string gameFen = fens[evalRNG.Next(fens.Length)];

                //double t1, t2;
                boardManager.LoadFenString(gameFen);
                eval += boardManager.ReLePlayGame(ai.digitArray, oppAIValues, 500L);
                boardManager.LoadFenString(gameFen);
                eval -= boardManager.ReLePlayGame(oppAIValues, ai.digitArray, 500L);
                //Console.WriteLine(eval + "|" + t1 + " & " + t2);
            }
            ai.SetEvaluationResults(eval);  
            return eval;
        }
    }

    public class ReLe_AIInstance
    {
        public int[,][] digitArray { private set; get; } = new int[3, 6][];


        private double evalResult;

        public ReLe_AIInstance(System.Random rng)
        {
            for (int i = 0; i < 3; i++) 
                for (int j = 0; j < 6; j++)
                    digitArray[i,j] = Get64IntArray(rng, 40);
        }

        private int[] Get64IntArray(System.Random rng, int pMax)
        {
            return new int[64] {
                rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
                rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
                rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
                rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
                rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
                rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
                rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
                rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax)
            };
        }

        public ReLe_AIInstance(int[,][] pDigitArray)
        {
            SetCharToDigitList(pDigitArray);
        }

        public void SetCharToDigitList(int[,][] digitRefArr)
        {
            digitArray = digitRefArr;
        }

        public void SetEvaluationResults(double eval)
        {
            evalResult = eval;
        }

        public override string ToString()
        {
            string s = "";
            s += "Result: " + evalResult;
            return s;
        }
    }

    #endregion

    #region | TLM_NuCRe |

    public static class NuCRe
    {
        private static char[] NuCReChars = new char[256];
        private static int[] NuCReInts = new int[1_000];

        public static void Init()
        {
            for (int i = 33; i < 127; i++) NuCReChars[i - 33] = (char)i;
            for (int i = 161; i < 323; i++) NuCReChars[i - 67] = (char)i;
            NuCReChars[11] = 'ǂ';
            NuCReChars[239] = 'œ';
            NuCReChars[240] = 'Ŝ';
            NuCReChars[245] = 'Ř';
            NuCReChars[252] = 'Ŵ';
            NuCReChars[253] = 'Ŷ';

            int a = 0;
            foreach (char c in NuCReChars) {
                NuCReInts[c] = a;
                a++;
            }
        }

        public static int GetNumber(string pNuCReString)
        {
            int rV = 0;
            for (int i = 0; i < 4 && i < pNuCReString.Length; i++) rV |= NuCReInts[pNuCReString[i]] << (i * 8);
            return rV;
        }

        public static string GetNuCRe(int pNum)
        {
            if (pNum > -1)
            {
                if (pNum < 256) return "" + NuCReChars[pNum & 0xFF];
                else if (pNum < 65536) return "" + NuCReChars[pNum & 0xFF] + NuCReChars[pNum >> 8 & 0xFF];
                else if (pNum < 16777216) return "" + NuCReChars[pNum & 0xFF] + NuCReChars[pNum >> 8 & 0xFF] + NuCReChars[pNum >> 16 & 0xFF];
                return "" + NuCReChars[pNum & 0xFF] + NuCReChars[pNum >> 8 & 0xFF] + NuCReChars[pNum >> 16 & 0xFF] + NuCReChars[pNum >> 32 & 0xFF];
            }
            return " ";
        }
    }

    #endregion

    #region | FENS |

    public static class FEN_MANAGER
    {
        private static string[] fens;
        private static Random fenRandom = new Random();

        public static void Init()
        {
            //C: \Users\tpmen\Desktop\4 - Programming\41 - Unity & C#\MiluvaV3\Miluva-v3\Chessbot\FENS.txt \bin\Debug\net6.0
            string tPath = Path.GetFullPath("FENS.txt").Replace(@"\\bin\\Debug\\net6.0", "");
            tPath = tPath.Replace(@"\bin\Debug\net6.0", "");
            fens = File.ReadAllLines(tPath);
        }

        public static string GetRandomStartFEN()
        {
            return fens[fenRandom.Next(0, fens.Length)];
        }
    }

    #endregion

    #region | MOVE HASH EXTRACTOR |

    public static class MOVE_HASH_EXTRACTOR
    {
        public static Move[] moveLookupTable = new Move[262_144]; 

        public static Move Get(string pNuCRe)
        {
            return moveLookupTable[NuCRe.GetNumber(pNuCRe)];
        }
    }

    #endregion

    #region | CUSTOM GAME FILE FORMAT |

    public static class CGFF
    {
        private static string[] gameResultStrings = new string[3]
        {
            "Black Has Won",
            "Draw",
            "White Has Won"
        };

        public static void InterpretateLine(string pStr)
        {
            string[] tSpl = pStr.Split(';');
            string startFen = tSpl[0];
            Console.WriteLine("StartFEN: " + startFen);
            string[] tSpl2 = pStr.Replace(startFen + ";", "").Split(',');
            int tSpl2Len = tSpl2.Length - 1;
            for (int i = 0; i < tSpl2Len; i++) {
                Console.WriteLine("Move (" + i + "): " + MOVE_HASH_EXTRACTOR.Get(tSpl2[i]));
            }
            string outcome = gameResultStrings[Convert.ToInt32(tSpl2[tSpl2Len])];
            Console.WriteLine("Outcome: " + outcome);
        }
    }

    #endregion

    #region | DATA CLASSES |

    public class TranspositionEntry
    {
        public Move bestMove;
        public int[] moveGenOrderedEvals;
        public int moveGenOrderedEvalLength;

        public TranspositionEntry(Move pBestMove, int[] pMoveGenOrderedEvals)
        {
            bestMove = pBestMove;
            moveGenOrderedEvals = pMoveGenOrderedEvals;
            moveGenOrderedEvalLength = moveGenOrderedEvals.Length;
        }
    }

    public class Move
    {
        public int startPos { get; private set; }
        public int endPos { get; private set; }
        public int rochadeStartPos { get; private set; }
        public int rochadeEndPos { get; private set; }
        public int pieceType { get; private set; }
        public int enPassantOption { get; private set; } = 65;
        public int promotionType { get; private set; } = 0;
        public int moveTypeID { get; private set; } = 0;
        public int moveHash { get; private set; }
        public bool isCapture { get; private set; } = false;
        public bool isSliderMove { get; private set; }
        public bool isEnPassant { get; private set; } = false;
        public bool isPromotion { get; private set; } = false;
        public bool isRochade { get; private set; } = false;
        public bool isStandard { get; private set; } = false;
        public ulong ownPieceBitboardXOR { get; private set; } = 0ul;
        public ulong oppPieceBitboardXOR { get; private set; } = 0ul;
        //public ulong zobristHashXOR { get; private set; } = 0ul;

        /* [0] Standard-Standard Move
           [1] Standard-Pawn Move
           [2] Standard-Knight Move
           [3] Standard-King Move
           [4] Standard-Rook Move
           
           [5] Standard-Pawn Capture
           [6] Standard-Knight Capture
           [7] Standard-King Capture
           [8] Standard-Rook Capture
           [9] Standard-Standard Capture
           
           [10] Double-Pawn-Move
           [11] Rochade
           [12] En-Passant
           [13] Standard-Promotion
           [14] Capture-Promotion                          */

        public void PrecalculateMove()
        {
            if (isEnPassant) oppPieceBitboardXOR = ULONG_OPERATIONS.SetBitToOne(0ul, enPassantOption);
            else if (isCapture) oppPieceBitboardXOR = ULONG_OPERATIONS.SetBitToOne(0ul, endPos);

            ownPieceBitboardXOR = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToOne(0ul, startPos), endPos);
            if (isRochade) ownPieceBitboardXOR = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToOne(ownPieceBitboardXOR, rochadeEndPos), rochadeStartPos);

            moveHash = startPos | (endPos << 6) | (pieceType << 12) | (promotionType << 15);
            MOVE_HASH_EXTRACTOR.moveLookupTable[moveHash] = this;

            switch (pieceType) {
                case 1:
                    if (isPromotion) moveTypeID = isCapture ? 14 : 13;
                    else if (isEnPassant) moveTypeID = 12;
                    else if (isCapture) moveTypeID = 5;
                    else if (enPassantOption == 65) moveTypeID = 1;
                    else moveTypeID = 10; break;
                case 2:
                    if (isCapture) moveTypeID = 6;
                    else moveTypeID = 2; break;
                case 4:
                    bool b = startPos == 0 || startPos == 7 || startPos == 56 || startPos == 63 || endPos == 0 || endPos == 7 || endPos == 56 || endPos == 63;
                    if (isCapture) moveTypeID = b ? 8 : 9;
                    else moveTypeID = b ? 4 : 0; break;
                case 6:
                    if (isRochade) moveTypeID = 11;
                    else if (isCapture) moveTypeID = 7;
                    else moveTypeID = 3; break;
                default:
                    if (isCapture) moveTypeID = 9;
                    else moveTypeID = 0; break;
            }
        }

        public Move(int pSP, int pEP, int pPT) //Standard Move
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            isStandard = true;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
            PrecalculateMove();
        }
        public Move(int pSP, int pEP, int pRSP, int pREP) // Rochade
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = 6;
            isSliderMove = false;
            isRochade = true;
            rochadeStartPos = pRSP;
            rochadeEndPos = pREP;
            PrecalculateMove();
        }
        public Move(int pSP, int pEP, int pPT, bool pIC) // Capture
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            isCapture = pIC;
            if (!isCapture) isStandard = true;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
            PrecalculateMove();
        }
        public Move(int pSP, int pEP, int pPT, bool pIC, int enPassPar) // Bauer von Base Line zwei nach vorne
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            isCapture = pIC;
            isStandard = !pIC;
            enPassantOption = enPassPar;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
            PrecalculateMove();
        }
        public Move(bool b, int pSP, int pEP, int pEPS) // En Passant (bool param einfach nur damit ich noch einen Konstruktur haben kann)
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = 1;
            isCapture = isEnPassant = true;
            enPassantOption = pEPS;
            isSliderMove = false;
            PrecalculateMove();
        }
        public Move(int pSP, int pEP, int pPT, int promType, bool pIC) // Promotion
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            isPromotion = true;
            isCapture = pIC;
            promotionType = promType;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
            PrecalculateMove();
        }
        public override string ToString()
        {
            string s = "[" + BOT_MAIN.FIGURE_TO_ID_LIST[pieceType] + "] " + SQUARES.SquareNotation(startPos) + " -> " + SQUARES.SquareNotation(endPos);
            if (enPassantOption != 65) s += " [EP = " + enPassantOption + "] ";
            if (isCapture) s += " /CAPTURE/";
            if (isEnPassant) s += " /EN PASSANT/";
            if (isRochade) s += " /ROCHADE/";
            if (isPromotion) s += " /" + BOT_MAIN.FIGURE_TO_ID_LIST[promotionType] + "-PROMOTION/";
            return s;
        }
    }

    public static class SQUARES
    {
        public static void Init()
        {

        }

        // (int)'a' = 97 / (int)'1' = 49

        public static string SquareNotation(int pNumberNot)
        {
            int tMod8 = pNumberNot % 8;
            return "" + (char)(tMod8 + 97) + (char)((pNumberNot - tMod8) / 8 + 49);
        }

        public static int NumberNotation(string pSqNot)
        {
            return (pSqNot[0] - 97) + 8 * (pSqNot[1] - 49);
        }
    }

    #endregion
}