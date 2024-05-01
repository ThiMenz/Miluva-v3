using System.Diagnostics;
using System.Linq;

#pragma warning disable CS8618
#pragma warning disable CS8602
#pragma warning disable CS8600
#pragma warning disable CS8622

namespace Miluva
{
    public interface IBoardManager
    {
        ulong allPieceBitboard
        {
            get;
            set;
        }

        int TEXEL_PARAMS
        {
            get;
        }

        int[] squareConnectivesCrossDirsPrecalculationArray
        {
            get;
            set;
        }

        List<Move> moveOptionList
        {
            get;
            set;
        }
        ChessClock chessClock
        {
            get;
            set;
        }

        Move? ReturnNextMove(Move? pLastMove, long pThinkingTime);
        int GameState(bool pCanWhiteBeAttacked);

        void SetKingMasks(ulong[] pKingMasks);
        void SetKnightMasks(ulong[] pKnightMasks);

        void SetTimeFormat(TimeFormat pTF);
        void SetJumpState();
        void LoadJumpState();
        void GetLegalMoves(ref List<Move> pMoveList);
        void LoadFenString(string pFen);
        string CreateFenString();
        void PlainMakeMove(string pMove);
        void PlainMakeMove(Move pMove);

        void SetPlayTexelVals(List<TLM_ChessGame> pDatabase, int pMin, int pMax, int[] pParams, int[] pParamsLowerLimits, int[] pParamsUpperLimits, int pC, ulong pUL);
        void TexelTuningThreadedPackage(object pObj);
        void TempStuff();

        void ThreadSelfPlay(object pObj);
        void TexelTuning(List<TLM_ChessGame> pDatabase, int[] pStartVals);
        void PlayThroughSetOfGames(object pObj);
        void SetPlayThroughVals(List<TLM_ChessGame> pDatabase, int pMin, int pMax);
    }


    public class BoardManager : IBoardManager
    {
        #region | BOARD VALS |

        public int TEXEL_PARAMS { get; } = 778;

        private const int BESTMOVE_SORT_VAL = -2_000_000_000;
        private const int CAPTURE_SORT_VAL = -1_000_000_000;
        private const int KILLERMOVE_SORT_VAL = -500_000_000;
        private const int COUNTERMOVE_SORT_VAL = -500000;
        private const int FOLLOWUPMOVE_SORT_VAL = -5000;

        private readonly int[,] MVVLVA_TABLE = new int[7, 7] {
            { 0, 0, 0, 0, 0, 0, 0 },  // Nichts
            { 0, 1500, 1400, 1300, 1200, 1100, 1000 },  // Bauern
            { 0, 3500, 3400, 3300, 3200, 3100, 3000 },  // Springer
            { 0, 4500, 4400, 4300, 4200, 4100, 4000 },  // Läufer
            { 0, 5500, 5400, 5300, 5200, 5100, 5000 },  // Türme
            { 0, 9500, 9400, 9300, 9200, 9100, 9000 },  // Dame
            { 0, 0, 0, 0, 0, 0, 0 }}; // König

        private readonly int[] FUTILITY_MARGINS = new int[10] {
            000, 100, 160, 220, 280,
            340, 400, 460, 520, 580
        };

        private readonly int[] DELTA_PRUNING_VALS = new int[6]
        { 0, 300, 500, 520, 700, 1000 };

        private readonly int[] maxCheckExtPerDepth = new int[10] {
            0, 1, 2, 3, 4, 5, 6, 6, 6, 6
        };

        #endregion

        #region | VARIABLES |

        private int BOARD_MANAGER_ID = -1;

        private bool debugSearchDepthResults = true;
        private bool debugSortResults = false;

        private RookMovement rookMovement;
        private BishopMovement bishopMovement;
        private QueenMovement queenMovement;
        private KnightMovement knightMovement;
        private KingMovement kingMovement;
        private WhitePawnMovement whitePawnMovement;
        private BlackPawnMovement blackPawnMovement;
        private Rays rays;
        public List<Move> moveOptionList { get; set; }

        public int[] pieceTypeArray = new int[64];
        public ulong whitePieceBitboard, blackPieceBitboard;
        public ulong allPieceBitboard { get; set; } = 0ul;
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


        private List<int> moveHashList = new List<int>();
        private ulong[] curSearchZobristKeyLine = Array.Empty<ulong>(); //Die History muss bis zum letzten Capture, PawnMove, der letzten Rochade gehen oder dem Spielbeginn gehen

        private Move[] debugMoveList = new Move[128];
        private string debugFEN = "";

        private Stopwatch globalTimer = Stopwatch.StartNew();
        public ChessClock chessClock { get; set; } = new ChessClock() { disabled = true };

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

            BOARD_MANAGER_ID = BOT_MAIN.curBoardManagerID;

            for (int i = 0; i < 33; i++)
                for (int j = 0; j < 14; j++)
                    piecePositionEvals[i, j] = new int[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            for (int i = 0; i < 33; i++)
                for (int j = 0; j < 14; j++)
                    texelTuningRuntimeVals[i, j] = new int[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 6; j++)
                    texelTuningVals[i, j] = new int[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            for (int i = 0; i < 14; i++)
            {
                texelTuningRuntimePositionalValsV2EG[i] = new int[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                texelTuningRuntimePositionalValsV2LG[i] = new int[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            }

            PrecalculateKingSafetyBitboards();
            PrecalculateMultipliers();
            PrecalculateForLoopSkips();
            GetLowNoisePositionalEvaluation(globalRandom);

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

            LoadBestTexelParamsIn();

            setupStopwatch.Stop();
        }

        public void SetTimeFormat(TimeFormat pTF)
        {
            chessClock.Set(pTF);
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
            //debugSearchDepthResults = true;
            //SetTimeFormat(new TimeFormat() { Time = 30_000_000L, Increment = 100_000L });
            //for (int i = 0; i < 100; i++)
            //{
            //    chessClock.Reset();
            //    string rFEN = FEN_MANAGER.GetRandomStartFEN();
            //    
            //    Console.WriteLine(rFEN);
            //    PlayGameAgainstItself(0, rFEN, 10_000_000L);
            //}

            //PerftRoot(10_000_000L);
            //r1b2rk1/p3qpp1/1pn1p2p/2p5/3P4/2PQPN2/P1B2PPP/R4RK1 b - - 2 15
            //r1br2k1/p3qpp1/1pn1p2p/2p5/3P4/2PQPN2/P1B2PPP/R4RK1 b - - 3 15
            //LoadFenString("2Q2rk1/3Q1pp1/4p3/4p3/4P2p/p2P3P/r4PP1/6K1 b - - 0 33");
            //MinimaxRoot(500_000L);
            //MinimaxRoot(1_000_000L);
            //debugSearchDepthResults = true;
            //LoadFenString("1rbq1rk1/pp2b1pp/2np1n2/2p1pp2/P1P5/1QNP1NPB/1P1BPP1P/R3K2R b KQ - 4 10");
            //LoadFenString("8/8/8/2K5/8/4k3/8/3q4 b - - 5 30");
            //
            //MinimaxRoot(100_000_000L);

            //PlayThroughZobristTree();

            //for (int p = 0; p < 64; p += forLoopBBSkipPrecalcs[(whitePieceBitboard >> p >> 1) & sixteenFBits])
            //{
            //    Console.WriteLine(p);
            //}

            //int[] ttt = new int[1152] { 0, 0, 0, 0, 0, 0, 0, 0, 1, -1, -9, -10, -7, 0, 4, 4, 1, 3, -2, 1, -5, 0, 3, 4, -1, 1, -4, 0, 0, -2, 1, 3, -4, 3, 7, 3, -4, 6, -3, -10, -3, 0, 14, 21, 3, 108, -15, -8, 11, -26, 93, 38, -24, 74, 34, 54, 0, 0, 0, 0, 0, 0, 0, 0, 3, 4, 7, -3, -4, 6, -2, 28, -6, -3, -6, 5, -2, 0, 4, 1, 0, -9, 0, -3, -8, -2, 5, -10, -5, 7, 12, -3, 7, -7, -17, -5, -4, 7, -11, 7, 6, -8, 1, -18, -8, -10, 26, 4, -1, 4, 7, 25, 82, 64, 19, -37, -11, 12, 23, 101, -8, -22, -51, 27, -111, 298, 20, 22, 3, 6, 0, -3, -2, 0, -2, -8, 3, 5, -3, 5, -4, 17, -3, 15, 2, -4, 7, -1, 2, 3, -3, 7, -8, 12, -3, 4, -1, -3, -1, -2, 8, -8, -4, -2, -1, 71, -3, 9, 11, 30, 12, 7, 53, 19, -3, 3, 45, 24, -35, 16, 48, -13, 39, -15, 30, -112, 3, 366, -233, 240, 174, 35, 2, -4, 1, 0, -2, 0, -7, 0, -3, 9, -6, 1, 2, -8, 21, -6, -13, 6, 12, -5, 5, -2, 31, -6, 1, -8, 12, -11, -8, -2, -4, 21, 3, 34, 3, -16, 16, 6, -21, 38, 1, -28, 4, -26, 18, 16, -4, -3, 24, 12, 23, -8, 28, -5, 37, 14, -2, 6, 38, -50, -11, -22, 31, -29, -5, 2, 8, 0, -3, -5, 8, 30, -6, -8, -1, -1, -4, 6, 32, -2, -9, 4, 4, -1, 12, -9, 0, 14, 1, 6, 8, 15, 4, -12, -1, 1, -16, -4, -9, -1, 28, -26, 13, 14, -25, -8, -34, 5, -16, -33, 1, 64, -166, -7, 17, -10, -15, -4, 154, -44, -71, -67, -8, -47, 33, -86, 5, -152, 0, 1, -6, 10, 2, 7, -2, 1, 15, 2, 6, -10, 0, 0, 11, -4, -6, 21, -11, -10, -6, 9, -7, 10, -120, 72, 93, 49, 53, -30, -1, 32, 100, 48, -28, 4, -53, 36, 82, -212, -14, 11, 101, 12, -50, -6, 12, 36, 8, 12, 4, 0, 112, -161, -17, 0, 0, -8, 0, 8, -4, 4, 4, 4, 0, 0, 0, 0, 0, 0, 0, 0, -6, 4, 2, -18, 8, 1, -1, 4, -10, 1, 1, 4, 1, 3, -2, 1, -2, 0, 0, 4, -2, -3, 3, 1, 10, 3, -9, 10, 1, -9, 0, 0, 46, -1, -6, -2, -20, -79, -33, -30, 19, -17, -37, 4, 0, 59, 4, 130, 0, 0, 0, 0, 0, 0, 0, 0, 5, -1, -11, -12, -2, 0, -1, -8, -14, -15, -8, -3, 4, -2, -6, -3, 10, 3, -1, -4, 11, 1, 3, 8, 10, -6, -9, 5, 0, 4, 15, 0, -12, 0, 16, 5, 0, 0, -3, -20, 9, 16, -12, -10, 0, 60, 11, 38, 5, 1, 12, -2, -24, 0, 24, 8, -24, 7, 0, 4, -118, -20, -104, 0, -5, 0, 3, -4, -4, -2, 24, 5, 3, 1, -4, 3, -2, -4, -4, -12, 10, 0, 0, -4, 3, -6, -3, -4, -6, 0, 4, 1, -10, -1, 13, -2, 6, -2, 7, -2, -4, -12, 10, 0, 0, -2, 22, -1, -8, 19, 9, 10, -18, 9, 8, 52, 35, 120, 0, -2, 62, 7, 20, 57, 12, 122, -27, 9, 1, 4, 0, -5, -2, -2, 8, 1, 0, 0, 0, -5, 3, 2, 1, 3, -7, -3, 4, -3, 7, 4, 0, 0, -10, -12, 9, -6, 0, 4, -4, 4, 4, 14, -3, 31, 1, 9, 5, -8, -17, -4, 11, -1, 2, -6, -14, -2, 6, 9, 0, 0, 10, 8, 4, -1, 17, -8, -8, 4, -6, 1, 9, -90, 0, 0, -8, -1, 0, -4, -10, -6, 5, 4, 4, 0, -2, -8, -64, 15, 2, -3, -1, 4, 0, -4, -8, 4, -5, -2, -7, 4, -2, 4, 5, 2, -3, -4, -8, 4, 1, 10, 7, 32, 4, 10, 7, -2, -11, 43, -5, -4, 36, 2, 20, 13, 1, 8, 6, -39, 3, 5, 41, -10, -34, 67, -5, -12, 7, -8, 6, -3, 0, 0, -1, 0, -9, -6, 0, 1, -3, 4, 6, -3, 20, -31, -9, 7, -2, 5, 1, -4, -28, 6, -7, 14, 16, 64, 7, -12, 12, 28, 73, 1, -14, 16, -84, -33, -201, 9, -6, 16, -79, 41, 31, 19, -16, 98, 4, -52, 163, -39, 32, 87, -673, -148, -121, -11, 282, 30, 33, 12, 0, 0, 0, 0, 0, 0, 0, 0, -1, 2, 0, 0, 0, 5, 0, 6, -16, -1, 4, 0, 1, 0, 0, 4, -12, 0, 0, 5, 1, -6, 6, 0, 34, 7, -8, 0, 3, -31, 6, 5, 14, 120, 25, -58, 18, 0, 7, -1, 76, -48, -206, -4, -17, 16, -56, -24, 0, 0, 0, 0, 0, 0, 0, 0, 35, 0, -25, -4, -4, -49, 0, -6, -6, -45, 0, -1, 2, -14, -1, 1, 0, -7, 0, 0, -16, -2, 1, 0, -2, -6, -44, 8, 0, 35, 9, 0, 32, -4, 4, 12, -1, 0, -9, -1, 0, 2, -20, 0, 6, -3, 0, 1, -6, 6, 1, -60, -2, 2, 10, -24, -1, -19, 0, 0, -36, -26, -52, 132, 0, 0, -1, -30, 3, 1, 0, 0, 3, -6, -9, 1, 8, 1, 4, -13, 1, 0, -1, -7, 3, -6, 7, -3, -22, 1, -1, 0, 9, 4, 4, 0, 0, 0, 5, 7, 0, -1, 5, 0, 11, -34, 41, 0, 0, 26, 5, 22, -4, 2, 2, 0, -1, -32, 21, 0, -14, 0, 0, -31, 54, 96, -72, 0, 5, 3, 0, 1, 4, 1, 0, 5, -3, -1, -1, 0, 14, -4, -2, -2, 6, 1, 5, 0, 1, 8, 2, 0, -36, -20, -4, -16, 4, -4, -6, -1, 0, 3, 8, 6, 5, -1, 4, -1, 20, 2, 4, 21, -13, 8, 12, 1, -2, 5, 0, -8, -33, 3, -4, -31, -31, -26, -26, -8, 1, 0, 62, 96, -1, 0, 6, 0, 3, 3, -8, -1, 2, 3, 4, 1, 4, -6, 0, 4, -4, -5, 0, 4, 1, 4, 0, 5, 4, 2, -52, -2, 2, -1, 6, -4, 6, -3, 0, 2, 0, 6, 0, -1, -2, 2, -6, 3, 4, 0, -2, 1, 0, 0, -3, 32, 0, 1, 1, 4, 30, 7, 32, 146, -22, 104, 0, 5, -52, 4, 1, 0, -1, -1, 4, -2, -2, -35, -24, 16, -7, 8, -4, -6, 0, -24, -1, 3, -8, 0, -4, 3, -68, 73, -23, -18, -8, 0, -11, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            //int[] ttt = new int[1152] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 2, -1, 1, 2, 0, 0, 1, 2, 3, -1, 0, 0, 0, 0, 1, 4, -3, -11, -2, 1, -2, 2, -8, 1, -10, 3, 4, -6, -10, -1, 15, 33, -4, -18, -16, 17, -72, 47, 8, -10, -62, 51, -111, 134, -78, 60, 0, 0, 0, 0, 0, 0, 0, 0, 14, 0, -29, -10, 2, -1, 0, -19, 1, -5, -13, -3, 2, -1, -16, -5, 1, -1, 1, 6, -1, 0, 2, 2, -4, -16, -13, -9, -11, -16, 2, -11, -32, 0, -1, -6, -14, -25, 1, 10, -5, -9, -1, 1, -7, 1, 3, -14, -10, -69, -6, 92, 22, -5, 6, 17, 43, -133, -83, -71, -20, -26, -104, -87, 4, -4, -1, -7, -8, 0, -13, -8, -7, -12, -2, -1, 0, 2, -4, -14, -3, 0, -10, -7, -11, -3, -11, -3, -16, -1, 3, 1, -6, -1, -2, 8, -3, -2, -3, 14, -17, 11, -2, -5, 0, -16, 5, -18, 21, -2, 1, 2, 1, 9, 112, 105, 14, 20, -48, 12, 114, -16, 29, -10, -66, 4, -21, -18, 0, 0, -12, 2, -19, -15, -3, 0, -1, -15, -16, -16, -14, -4, -8, 1, -12, 4, -21, -4, -16, -17, -14, -11, -16, -8, 2, -14, 1, -11, 29, -1, -13, -14, -1, 21, -4, 96, -25, 5, -10, -11, 94, -42, 59, 38, 4, -7, -14, -48, -31, -89, -38, 101, 6, 7, -75, -14, -31, -86, 18, -79, -7, -7, -26, -6, -2, -2, -16, -12, -9, -37, -7, 4, -12, -1, -4, -5, -8, -6, -3, -1, -2, -19, -3, -4, -5, -17, -2, -12, -8, -3, -12, -7, -13, -15, -10, -8, 2, -11, 0, 29, -20, -11, -53, 2, -30, 0, -22, -10, -10, 23, -5, -6, -6, -28, -72, -1, -98, 4, -32, 10, -116, 3, -1, 22, 54, -114, 6, 18, -20, -2, 0, 2, -8, -28, 104, -25, -10, -2, -16, 0, -4, -9, -118, -125, 1, -11, -8, -14, -2, -16, 24, 112, -23, -26, 5, 0, -2, -15, 132, 117, 136, -1, -104, 77, -14, 107, 128, 122, -78, 133, 169, 61, 126, 134, 31, -132, -131, 130, 155, 88, 116, 142, -128, 182, -159, 2, 151, 43, -133, -163, 0, 0, 0, 0, 0, 0, 0, 0, -16, 0, 0, -1, 1, 1, 0, 0, -16, 0, 2, -1, 0, 0, 16, 0, -16, -12, 12, -14, -1, 0, -2, -15, -29, -17, 3, 17, 3, -12, -14, 15, -84, 18, 26, 10, 15, 18, 33, 32, 11, -73, 46, 15, -115, 132, -71, 142, 0, 0, 0, 0, 0, 0, 0, 0, 1, -17, -54, -23, 4, 23, 0, -51, -13, -2, -14, -3, 0, 0, -18, 14, -15, 0, -16, 6, 0, 0, 5, 0, 32, -17, -16, -28, -11, 0, 12, 5, -118, -17, 48, -8, 1, -62, 2, 59, 6, -59, -2, -1, 6, -36, 59, -2, -18, 120, 10, 24, -58, 11, 21, 51, 20, -154, -4, 18, 96, 115, -96, 89, -1, -37, -17, 8, -24, 0, -21, -4, -117, -31, 15, -16, 0, -13, 13, -82, -17, 34, -14, -11, -31, 15, -17, 16, -39, -1, 4, 5, 8, -16, 11, 5, -15, 0, -52, 30, -17, 76, -16, 6, 81, -25, 28, -31, 90, -65, 32, 74, 53, -53, 3, 107, -70, 30, -27, 12, 65, -83, 19, -112, 55, 107, 7, -4, -16, 0, -13, 2, -4, 2, -3, 0, -17, -16, 2, 16, -14, -2, 6, 0, -32, 0, -53, 15, -16, -36, 0, -13, -14, -5, -1, -32, 14, 4, 52, 14, -14, 2, -20, 56, -29, 92, -11, 23, -60, 19, -97, -41, -9, 102, 22, -24, -46, -17, -13, -111, -19, 102, 3, 38, -106, 1, -16, -72, 18, 81, -16, 6, -27, -18, -16, -17, -16, -10, -40, -33, -38, 5, -13, -16, -4, -4, -4, -72, 14, -1, -2, -34, -3, -3, -21, -19, -18, -32, -12, -17, -29, -42, -31, -30, -27, -16, 0, -16, 1, 30, -18, 9, -98, -13, -30, 66, -81, -90, 9, 54, 21, 5, -56, -47, -57, -5, -88, 17, -91, 11, -103, 100, 3, 92, 108, -107, 54, 19, -10, 0, -16, -14, -26, -59, 35, -13, -13, -17, -16, -17, -3, -23, 84, -112, 19, 0, -27, 0, 14, 0, 103, 96, 71, -108, 37, -3, -2, 36, 67, 80, 23, -13, -27, 17, 23, 74, 80, 76, 1, 115, 152, 99, 41, 63, 14, -82, -18, 113, 143, -56, 99, 114, -112, 155, -131, 75, 135, -49, -134, -168, 0, 0, 0, 0, 0, 0, 0, 0, -16, 16, -48, -49, 16, 0, 16, -32, -16, -33, -111, -16, -97, -16, 80, 15, -17, -60, 13, -112, 16, 46, -50, -64, -64, -84, -64, 111, 0, 31, -48, 47, -85, 119, -55, 37, 40, 65, 101, 99, -70, -112, 26, -72, -110, 156, -64, 78, 0, 0, 0, 0, 0, 0, 0, 0, 3, -113, -121, -68, -76, 111, -112, -12, -40, -57, -111, -3, -50, 2, -5, -16, -31, 32, 16, -6, -78, -113, -74, -48, 116, -34, -32, -65, -109, 29, -24, -13, 1, -52, -73, -73, 81, -104, -15, 111, -106, -112, 30, -5, 83, -122, 114, -20, 6, 7, 58, 105, -112, 41, 102, 102, 91, -143, 29, 82, 103, 121, -111, 106, 10, -34, -80, 22, -60, -96, 26, -114, -111, -112, -16, -64, -48, -73, -1, 5, -64, 116, -64, -94, -47, 48, -5, -32, -48, -32, -111, 3, 116, -32, -12, 87, -80, -18, -18, 113, -99, 125, -112, -111, 86, -115, 96, -96, 120, 43, 91, 106, 57, -32, 6, -84, -125, 55, 21, -33, 114, -110, -100, -99, -56, 112, 66, -3, -64, -48, -80, -29, -6, 16, -34, -64, -48, -16, 113, 78, 19, -48, -27, -16, -66, 31, -120, 67, 16, -57, 14, -96, 0, -17, -68, -100, 80, 96, 121, 78, -14, 96, -25, 120, -16, 108, -18, 44, -106, 81, -80, -103, -105, 115, 57, 102, -16, 24, -58, -114, 0, 116, 60, 82, -105, 32, -17, -105, 94, 109, -106, -13, -113, -96, -112, -97, -112, -23, -105, -44, -101, -23, -110, -95, -97, -113, -31, -118, 0, -112, -3, -113, -18, -64, -114, -47, -113, -102, 18, -113, -96, -110, -49, -48, -120, -96, -18, -69, 16, 117, 11, -3, -119, -41, -59, 115, 33, -106, -116, 114, -54, 37, -77, -112, -91, -28, -107, -112, -4, 114, -123, 114, -8, 111, 121, -82, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            //int[] ttt = new int[1152] { 0, 0, 0, 0, 0, 0, 0, 0, -13, 14, 12, -12, -4, 22, 6, 11, -8, 21, 23, -6, 2, 25, 0, 14, -11, 32, 10, -2, 1, 23, 7, 20, 13, 34, 62, 0, 12, 50, 28, 25, 108, 199, 0, -18, 116, 164, -16, 128, 215, 99, -139, -51, 453, 584, 234, 271, 0, 0, 0, 0, 0, 0, 0, 0, 143, 22, 88, 66, 62, 13, 18, 22, 71, 39, 55, 35, 32, 42, 52, 29, 29, 35, 28, 60, 63, 22, 53, 20, 34, 43, 49, 43, 39, 31, 19, 43, 51, 29, 117, 28, 39, 45, 31, 42, 164, 98, 153, 53, 104, 127, 17, 170, 25, 29, 18, 161, 131, 32, -26, 143, 81, -47, -222, 198, -412, 236, 74, -55, 86, 60, 20, 60, 65, 17, 58, 80, 108, 32, 47, 37, 31, 70, 19, 82, 30, 34, 42, 36, 42, 33, 37, 22, 23, 39, 29, 57, 43, 34, 59, 41, 79, 24, 107, 19, 65, 75, 34, 59, 57, 49, 39, 62, 33, 274, 90, 3, 15, 129, 331, -97, 153, 130, 28, 300, 187, 74, 89, 180, -68, -42, -105, 30, 23, 27, 46, 60, 53, 44, 29, 17, 33, 44, 46, 60, 94, 48, 43, 29, 33, 40, 80, 45, 28, 39, 34, 40, 49, 67, 67, 72, 49, 61, 101, 39, 88, 54, 227, 48, 178, 1, 51, 73, 523, 43, 299, 295, 233, 15, 153, 205, 303, -4, 229, 323, 258, 124, 130, 22, 271, 22, 78, 848, 28, -309, 64, 311, 67, 61, 58, 35, 66, 54, 95, 120, 81, 104, 53, 56, 46, 67, 63, 77, 47, 43, 42, 59, 49, 41, 56, 39, 43, 54, 64, 55, 68, 69, 49, 49, 48, 56, 57, 41, 57, 59, 54, 35, 116, 99, 62, 42, 102, 64, 64, 108, 88, 85, 130, 651, 280, 35, 144, 117, 56, 110, 152, -82, 112, 268, 142, 42, 54, 66, 76, 62, 32, 66, 88, 68, 72, 39, 70, 62, 48, 64, 60, 391, -54, 115, 65, 69, 72, 50, 46, 48, 24, 160, 25, 374, 37, 208, 302, 65, 52, -539, 40, 47, -56, 317, 2, -261, -384, 378, 434, 85, 681, 701, 94, -26, 1055, 12, 173, -14, -277, 376, -364, 478, -3520, -1146, 417, 18, 503, -437, 795, 813, 0, 0, 0, 0, 0, 0, 0, 0, -4, 24, 18, 3, 16, 29, 24, 18, 6, 34, 40, 3, 34, 32, 24, 24, -6, 28, 23, 48, 25, 16, 26, 17, 29, -3, 43, 7, 23, 38, 22, 27, -80, -77, 144, 2, 16, -10, -11, -18, 6, -22, 210, -57, 1, 204, 30, 145, 0, 0, 0, 0, 0, 0, 0, 0, 85, 67, 110, 113, 84, -34, 74, 43, 111, 158, 106, 86, 72, 83, 70, 118, 79, 71, 74, 98, 102, 88, 103, 68, 110, 115, 94, 70, 111, 84, 89, 149, 116, 44, 191, 32, 115, 178, 84, 75, -105, 77, 124, 95, 52, 104, 133, 119, 28, 200, 6, -16, -77, 87, -61, 93, -24, -136, 337, 218, -52, 256, 16, 71, 127, 98, 55, 108, 84, 78, 192, 130, 75, 109, 57, 82, 97, 87, 86, 158, 67, 101, 86, 139, 105, 68, 79, 102, 25, 28, 88, 97, 99, 67, 114, 11, 19, 76, 112, -4, 131, 188, 83, 143, 76, 69, 53, 132, 39, 31, 82, 98, 5, -111, -21, 37, 8, -15, 5, 225, 185, -27, -11, -125, 282, 207, 30, -7, 60, 60, 63, 24, 48, 114, 69, 78, 78, 94, 94, 40, 16, 41, 101, 96, 62, 18, 14, 39, 20, 52, 100, 109, 64, 51, 22, 17, 36, 8, 77, 34, 33, -40, 8, 0, 3, -109, 21, 56, 14, -7, 110, -27, -87, -31, 42, 44, 47, -2, -12, 113, 105, 51, 1, -1, 55, -58, -114, 6, 39, -83, 123, 160, 131, 178, 146, 134, 213, 118, 98, 215, 52, 168, 197, 160, 156, 166, 136, 100, 116, 139, 118, 209, 125, 150, 155, 125, 139, 144, 152, 121, 179, 166, 163, 168, 117, 160, 87, 124, 123, 40, 177, 165, 77, 129, 83, 112, 55, 247, 204, 110, 75, 40, 49, 101, -60, 95, 126, 159, 221, 147, -62, -48, 46, 66, 253, 85, 153, 40, 120, 150, 132, 138, 169, 45, 24, 101, 127, 131, 168, 139, 94, 227, 76, 44, 97, 98, 109, 122, 98, 66, 43, 30, 53, 183, 20, 33, 54, 74, 61, -82, -123, 11, 0, -21, 31, -115, 109, -2, -53, 33, 278, 259, -50, -23, 30, -106, 48, 37, -35, 202, 63, 37, -593, 12, -103, 128, 103, -213, -123, -159, 0, 0, 0, 0, 0, 0, 0, 0, 137, 219, 212, 210, 290, 225, 271, 196, 128, 198, 78, 200, 131, 196, 321, 219, 115, 127, 219, 128, 116, 205, 93, 80, 82, 148, -59, -38, 89, -103, 87, -47, -665, -851, 142, -211, -97, -324, 45, -178, -582, -461, -165, -778, -278, 319, 95, -75, 0, 0, 0, 0, 0, 0, 0, 0, 1231, 606, 214, 615, 1065, 98, 611, 483, 971, 426, 616, 547, 549, 518, 598, 593, 689, 639, 702, 490, 220, 612, 1590, 624, 579, 447, 400, 429, 488, 346, 298, 743, 54, 606, 363, 377, 665, -225, 606, 243, -810, 481, 362, 341, 499, 581, 475, -274, -34, 457, -180, -407, -198, 121, -537, 806, 44, -167, -238, 190, -310, 665, -355, -245, 982, 54, 610, 892, 437, 673, 320, 373, -139, 1327, 709, 816, 548, 447, 687, 563, 592, 743, 513, 1365, 603, 684, 459, 638, 467, 320, 513, 595, 651, 450, 726, 460, -9, 410, 169, 144, 743, 1039, 449, 773, -280, 85, 175, 261, -4, 695, -236, -207, -375, 353, 168, 28, 351, 6, 259, 499, 146, -159, -260, -518, 4, 246, 132, 604, 768, 544, 400, 388, 250, 496, 508, 548, 544, 540, 561, 126, 925, 27, 733, 549, 381, 493, 8, -329, 181, 349, 493, 591, 577, -262, 12, 12, 403, -35, 61, 684, -9, -22, -289, -168, -72, -420, 326, 448, 127, -13, 44, -279, 231, -229, 197, 126, 162, -52, -117, -145, 319, -406, 239, 10, 244, 21, 208, 50, 184, -189, 310, 411, 1380, 719, 1231, 828, 1936, 602, 1541, 352, 139, 1694, 1569, 1615, 996, 992, 1280, 953, 914, 751, 1070, 1023, 1397, 1389, 621, 345, 693, 1642, 834, 897, 1727, 1973, 799, 1424, 544, 1937, 1101, 541, 749, 386, 229, 279, -683, 364, -267, 593, 1249, 424, 925, 898, -150, 133, 569, 609, -297, 340, 733, 513, 379, 1128, 269, -319, 175, 328, 801, 736, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; 
            //int[] tttt = new int[197] { 94, 321, 374, 498, 1044, 0, 0, 0, 0, 0, 0, 0, 0, -4, 16, -4, -20, 6, -3, 15, -12, -11, -11, 23, 12, -4, -5, 32, -6, -24, 51, -17, -26, 9, -33, 5, -8, -64, -22, -48, -40, -41, -92, 5, 1, 40, 200, -104, -200, -176, -40, -200, 200, 192, 200, -160, -199, 72, 200, -168, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -8, -50, -4, 10, 12, -3, 48, 0, 2, 15, 28, -32, -12, 10, 30, -8, -7, -2, -1, 28, -20, 0, -11, -8, 116, -67, -28, 88, 93, 115, -42, 13, -200, -64, 148, 106, 127, -76, 22, 44, 4, -200, 200, -200, 37, 136, 173, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 93, 148, -200, 104, 169, -97, -40, -60, 196, -88, -7, -12, 46, -144, 32, 16, 80, -196, 16, -165, 36, -91, 130, 35, 200, 200, -8, -48, 160, 199, 40, 172, 128, 200, -192, -152, -200, 200, 97, -200, -184, 200, -200, -168, 200, 200, 57, 0, 0, 0, 0, 0, 0, 0, 0 };

            //PrintDefinedTexelParams(ttt);
            //chessClock.Set(30_000_000L, 50_000L);
            //chessClock.Enable();

            PlayGameOnConsoleAgainstHuman("8/8/2p5/pk6/4q3/1PK3Q1/6P1/8 w ha - 4 15", false, 700_000_000L);

            //-0.125x + 4

            //for (int i = 1; i < 33; i++)
            //{
            //    for (int sq = 0; sq < 64; sq++) 
            //    {
            //        int bssq = blackSidedSquares[sq];
            //
            //        piecePositionEvals[i, 1][sq] = T_Rooks[0, sq] + T_Rooks[2, sq];
            //        piecePositionEvals[i, 8][bssq] = T_Rooks[0, sq] + T_Rooks[2, sq];
            //    }
            //}

            //LoadFenString("2kr2n1/pp1r1p1p/2n3pb/q1p1pbB1/2PpP3/2NP1N2/PP1QBPPP/R4RK1 w Q - 0 1");

            //ulong tul = 0ul;
            //
            //int ttttt = rays.DiagonalRaySquareCount(allPieceBitboard, 11, ref tul);
            //
            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(tul));
            //Console.WriteLine(ttttt);



            //return;

            //int[] tttt = new int[TEXEL_PARAMS];
            //SetupTexelEvaluationParams(tttt);

            //int[] ttt = new int[64];
            //
            //for (int i = 0; i < 64; i++)
            //{
            //    ttt[i] = i;
            //}
            //
            //ttt = SwapArrayViewingSide(ttt);
            //
            //for (int i = 0; i < 64; i++)
            //{
            //    if (i % 8 == 0) Console.WriteLine();
            //    Console.Write(ttt[i] + ",");
            //}

            //LoadFenString("1kr4r/p1p2ppp/bp2pn2/8/1bBP4/2N1PQ2/PPPB2PP/2KR2NR w Kk - 0 1");
            //Console.WriteLine(TexelEvaluate());

            //TexelEvaluate();
            //
            //Stopwatch sw = Stopwatch.StartNew();
            //////
            //for (int i = 0; i < 10_000_000; i++)
            //{
            //    CustomKillerHeuristicFunction(i);
            //}
            ////
            //sw.Stop();
            //////
            //Console.WriteLine(sw.ElapsedMilliseconds);
            //
            //for (int i = 0; i < 1001; i++)
            //{
            //    Console.WriteLine(CustomKillerHeuristicFunction(i));
            //}

            //rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 1
            //1r2kb1r/2pq1ppp/2np1n2/1p1Pp3/4P3/1QP2N2/1P3PPP/RNB2RK1 b k - 0 9
            //PlayGameOnConsoleAgainstHuman("rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 1", true, 30_000_000L);


            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(10802606085532904069));
            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(14126000376877154961));
            //
            //ulong ul = 0ul;
            //
            //rays.StraightRaySquareCount(14126000376877154961, 4, ref ul);
            //
            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(ul));

            //LoadBestTexelParamsIn();
            //PlayGameOnConsoleAgainstHuman();

            //TuneWithTxtFile("DATABASES/SELF_PLAY_GAMES");

            //TuneWithTxtFile("SELF_PLAY_GAMES", 500d, 10);

            //Console.WriteLine(CGFF.GetGame(@"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1;-6,¸:,ĈU,ŁL,)5,U:,n8,§8,u8,ù<,gF,\~,e¤,Ŵ±,Êb,Ā^,Ñ6,Ķ;,ÖW,Ux,Ď5,ß{,ĄC,´9,;S,òH,&¦,`p,6J,L:,Ē6,§8,ǂH,ġL,cX,F8,48,Ľ],àZ,þ±,ċY,]h,m¨,ğg,æ[,ö[,6¦,Ğa,ăa,KX,Ćo,½¯,ĩ\,Ĺ\,ľp,ñ:,ď£,ò^,Ăp,¹S,āj,õ;,gb,*U,fn,R­,Gj,ß\,¸f,qS,El,ıZ,Ù8,ê\,Ě8,ļM,ïk,«¯,Kl,ûo,Mn,ým,Ud,öI,Ģ:,OX,6f,ß[,$¤,Q9,Í5,CG,Õ7,ä7,e¤,ÌQ,5j,µn,&¤,R­,El,««,ć£,cX,Mn,Vn,-n,ĺ<,·n,àZ,$¤,B­,e¤,Ý5,øm,ĭY,ĵm,Õ3,Ķk,yK,f¦,ÍÓ#,îl,cx,.¤,æW,ñj,>v,ć£,{U,0"));



            //Tune(@"r3kb1r/2q2ppp/pn2p3/3b2B1/3N4/PPp2P2/4B1PP/2RQ1R1K w kq - 0 17;Ću,Sz,v8,§¡,Åe,ĺ<,Ėv,Ăp,ĩX,^U,ÞK,ļ{,@^,î},UU,æ[,Èa,µx,Õk,~t,Kl,Ŵ±,1^,\²,÷Y,V<,8|,Ŵ±,¥T,ĬM,ï¢,\°,_¡,I9,Åm,ù<,mY,·®,Ĥ],œ:,Yy,N°,ãz,·°,¶Z,¸²,ĩ¡,2"
            //, 13d);

            //LoadFenString("r2q1rk1/1p1n1ppp/p3p3/3pP3/Pb1P2b1/3B1N2/1P2QPPP/R1B2RK1 b - - 1 14");
            //debugSearchDepthResults = true;
            //MinimaxRoot(30_000_000L);
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
            PlayGameAgainstItself(obj);
        }

        public void PlayGameAgainstItself(object obj, string pStartFEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", long tickLimitForEngine = ENGINE_VALS.SELF_PLAY_THINK_TIME)
        {
            int ttGState; // Aktuell nicht wirklich benötigt, trotzdem manchmal gut nutzbar
            //while (BOT_MAIN.goalGameCount > BOT_MAIN.gamesPlayed)
            //{
            string tGameStr;
            //GetLowNoisePositionalEvaluation(globalRandom);
            LoadFenString(tGameStr = pStartFEN);
            tGameStr += ";";
            int tGState = 3, tmc = 0;
            //bool lowNoiceDecider = globalRandom.NextDouble() < 0.5d;
            while (tGState == 3)
            {
                //piecePositionEvals = lowNoiceDecider ? lowNoisePositionEvals1 : lowNoisePositionEvals2;
                MinimaxRoot(tickLimitForEngine);
                Move tM = BestMove;
                Console.WriteLine(CreateFenString());
                //Console.WriteLine(GameState(isWhiteToMove));
                PlainMakeMove(tM);
                Console.WriteLine(tM);

                tGameStr += NuCRe.GetNuCRe(tM.moveHash) + ",";
                BOT_MAIN.movesPlayed++;
                tGState = GameState(isWhiteToMove);
                //lowNoiceDecider = !lowNoiceDecider;
                tmc++;
            }
            ttGState = tGState;
            tGameStr += ttGState + 1;
            Console.WriteLine(tGameStr);
            BOT_MAIN.gamesPlayedResultArray[ttGState + 1]++;
            BOT_MAIN.gamesPlayed++;
            BOT_MAIN.selfPlayGameStrings.Add(tGameStr);

            //}
        }

        public void PlayGameOnConsoleAgainstHuman(string pStartFEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", bool pHumanPlaysWhite = true, long tickLimitForEngine = 10_000_000L)
        {
            debugSearchDepthResults = true;

            LoadFenString(pStartFEN);

            if (!(pHumanPlaysWhite ^ isWhiteToMove))
            {
                var pVS = Console.ReadLine();
                if (pVS == null) return;
                Move tm = GetMoveOfString(pVS.ToString());
                Console.WriteLine(tm);
                PlainMakeMove(tm);
            }

            int tGState = 3;
            while (tGState == 3)
            {
                MinimaxRoot(tickLimitForEngine);
                Move tM = BestMove;
                Console.WriteLine(tM);
                Console.WriteLine(CreateFenString());
                PlainMakeMove(tM);
                tGState = GameState(isWhiteToMove);
                if (tGState != 3 || tM == NULL_MOVE) break;
                var pV = "";
                tM = NULL_MOVE;
                do
                {
                    pV = Console.ReadLine();
                    if (pV == null) continue;
                    tM = GetMoveOfString(pV.ToString());
                    Console.WriteLine(tM);
                } while (tM == NULL_MOVE);
                PlainMakeMove(tM);
                tGState = GameState(isWhiteToMove);
            }
        }

        private void PlayThroughZobristTree()
        {
            //SetJumpState();
            Console.WriteLine("\n- - - - - - - - - - - - -");
            //int tC = 1;
            //Dictionary<ulong, bool> usedULs = new Dictionary<ulong, bool>();
            //while (TTV2[zobristKey % TTSize].Item1 == zobristKey)
            //{
            //    if (usedULs.ContainsKey(zobristKey))
            //    {
            //        Console.WriteLine(tC + ": AT LEAST ONE REPETION; REST NOT VISIBLE");
            //        break;
            //    }
            //    Console.WriteLine(tC + ": " + TTV2[zobristKey % TTSize].Item2 + " > ");
            //    usedULs.Add(zobristKey, true);
            //    if (TTV2[zobristKey % TTSize].Item2 == NULL_MOVE) break;
            //    PlainMakeMove(TTV2[zobristKey % TTSize].Item2);
            //    tC++;
            //}
            foreach (Move m in PV_LINE_V2) Console.WriteLine(m);
            Console.WriteLine("- - - - - - - - - - - - -\n");
            //LoadJumpState();
        }

        public Move? ReturnNextMove(Move? lastMove, long pThinkingTime)
        {
            if (lastMove != null) PlainMakeMove(lastMove);

            if (GameState(isWhiteToMove) != 3) return null;

            MinimaxRoot(pThinkingTime);

            if (!chessClock.HasTimeLeft()) return null;

            Move tm = BestMove;

            PlainMakeMove(tm);

            return tm;
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
        private int[] jmpSt_moveHashList;
        private long jmpSt_clockTime;
        private double[] jmpSt_NNUEV;
        private int jmpSt_pieceCountHash;

        public void LoadJumpState()
        {
            shouldSearchForBookMove = true;
            chessClock.curRemainingTime = jmpSt_clockTime;
            pieceTypeArray = (int[])jmpSt_pieceTypeArray.Clone();
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
            curSearchZobristKeyLine = (ulong[])jmpSt_curSearchZobristKeyLine.Clone();
            allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
            moveHashList = jmpSt_moveHashList.ToList();
            NNUE_FIRST_LAYER_VALUES = (double[])jmpSt_NNUEV.Clone();
            countOfPiecesHash = jmpSt_pieceCountHash;
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
            jmpSt_moveHashList = moveHashList.ToArray();
            jmpSt_clockTime = chessClock.curRemainingTime;
            jmpSt_NNUEV = (double[])NNUE_FIRST_LAYER_VALUES.Clone();
            jmpSt_pieceCountHash = countOfPiecesHash;
        }

        #endregion

        #region | CHECK RECOGNITION |

        private int PreMinimaxCheckCheckWhite()
        {
            ulong bitShiftedKingPos = 1ul << whiteKingSquare;
            int curR = -1;
            for (int p = 0; p < 64; p++)
            {
                if (ULONG_OPERATIONS.IsBitZero(blackPieceBitboard, p)) continue;
                int tPT;
                switch (tPT = pieceTypeArray[p])
                {
                    case 1:
                        if ((bitShiftedKingPos & blackPawnAttackSquareBitboards[p]) != 0ul)
                            return p;
                        break;
                    case 2:
                        if ((bitShiftedKingPos & knightSquareBitboards[p]) != 0ul)
                            return p;
                        break;
                    case 6: break;
                    default:
                        if ((squareConnectivesPrecalculationLineArray[whiteKingSquare << 6 | p] & allPieceBitboard) == (1ul << p) && pieceTypeAbilities[tPT, squareConnectivesPrecalculationArray[whiteKingSquare << 6 | p]])
                        {
                            if (curR != -1) return -779;
                            curR = p;
                        }
                        break;
                }
            }

            return curR;
        }

        private int PreMinimaxCheckCheckBlack()
        {
            ulong bitShiftedKingPos = 1ul << blackKingSquare;
            int curR = -1;
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
                        if ((squareConnectivesPrecalculationLineArray[blackKingSquare << 6 | p] & allPieceBitboard) == (1ul << p) && pieceTypeAbilities[tPT, squareConnectivesPrecalculationArray[blackKingSquare << 6 | p]])
                        {
                            if (curR != -1) return -779;
                            curR = p;
                        }
                        break;
                }
            }

            return curR;
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
            else if (pPieceType != 6)
            {
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

        public void GetLegalMoves(ref List<Move> pMoveList)
        {
            int attk;
            if (isWhiteToMove)
            {
                attk = PreMinimaxCheckCheckWhite();
                if (attk == -779) GetLegalWhiteMovesSpecialDoubleCheckCase(ref pMoveList);
                else GetLegalWhiteMoves(attk, ref pMoveList);
            }
            else
            {
                attk = PreMinimaxCheckCheckBlack();
                if (attk == -779) GetLegalBlackMovesSpecialDoubleCheckCase(ref pMoveList);
                else GetLegalBlackMoves(attk, ref pMoveList);
            }
        }

        private void GetLegalWhiteMoves(int pCheckingPieceSquare, ref List<Move> pMoveList)
        {
            legalMoveGens++;

            if (pCheckingPieceSquare == -779)
            {
                GetLegalWhiteMovesSpecialDoubleCheckCase(ref pMoveList);
                return;
            }

            moveOptionList = pMoveList;
            ulong oppDiagonalSliderVision = 0ul, oppStraightSliderVision = 0ul, oppStaticPieceVision = 0ul, oppAttkBitboard, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p += forLoopBBSkipPrecalcs[(blackPieceBitboard >> p >> 1) & sixteenFBits]) //+= forLoopBBSkipPrecalcs[(blackPieceBitboard >> p >> 1) & sixteenFBits]
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
                for (int p = 0; p < 64; p += forLoopBBSkipPrecalcs[(whitePieceBitboard >> p >> 1) & sixteenFBits])
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
                for (int p = 0; p < 64; p += forLoopBBSkipPrecalcs[(whitePieceBitboard >> p >> 1) & sixteenFBits])
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
            capLegalMoveGens++;

            if (pCheckingPieceSquare == -779)
            {
                GetLegalWhiteCapturesSpecialDoubleCheckCase(ref pMoveList);
                return;
            }

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
            legalMoveGens++;

            if (pCheckingPieceSquare == -779)
            {
                GetLegalBlackMovesSpecialDoubleCheckCase(ref pMoveList);
                return;
            }
            moveOptionList = pMoveList;
            ulong oppDiagonalSliderVision = 0ul, oppStraightSliderVision = 0ul, oppStaticPieceVision = 0ul, oppAttkBitboard, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p += forLoopBBSkipPrecalcs[(whitePieceBitboard >> p >> 1) & sixteenFBits]) //+= forLoopBBSkipPrecalcs[(whitePieceBitboard >> p >> 1) & sixteenFBits]
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
                for (int p = 0; p < 64; p += forLoopBBSkipPrecalcs[(blackPieceBitboard >> p >> 1) & sixteenFBits])
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
                for (int p = 0; p < 64; p += forLoopBBSkipPrecalcs[(blackPieceBitboard >> p >> 1) & sixteenFBits])
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
            capLegalMoveGens++;

            if (pCheckingPieceSquare == -779)
            {
                GetLegalBlackCapturesSpecialDoubleCheckCase(ref pMoveList);
                return;
            }

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
            if (tPT == 6 && (Math.Abs(tSP - tEP) == 2 || Math.Abs(tSP - tEP) == 3))
            {
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
            if (pMove == NULL_MOVE) throw new System.Exception("TRIED TO MAKE A NULL MOVE");
            if (isWhiteToMove) WhiteMakeMove(pMove);
            else BlackMakeMove(pMove);
            happenedHalfMoves++;
            debugFEN = CreateFenString();
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

            moveHashList.Add(pMove.moveHash);

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

            moveHashList.Add(pMove.moveHash);

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

        private const int WHITE_CHECKMATE_TOLERANCE = 99000, BLACK_CHECKMATE_TOLERANCE = -99000;

        private readonly Move NULL_MOVE = new Move(0, 0, 0);
        private Move BestMove;

        private int curSearchDepth = 0, curSubSearchDepth = -1, cutoffs = 0, tthits = 0, nodeCount = 0, timerNodeCount, legalMoveGens = 0, capLegalMoveGens = 0;
        private long limitTimestamp = 0;
        private bool shouldSearchForBookMove = true, searchTimeOver = true;

        private int maxCheckExtension = 0;

        public int MinimaxRoot(long pTime)
        {
            if (!chessClock.disabled)
            {
                pTime = chessClock.curRemainingTime / 20;
            }

            long tTime;
            limitTimestamp = (tTime = globalTimer.ElapsedTicks) + pTime;

            searchTimeOver = false;
            BestMove = NULL_MOVE;
            searches++;
            int baseLineLen = capLegalMoveGens = legalMoveGens = tthits = cutoffs = nodeCount = 0;

            ClearHeuristics();
            //transpositionTable.Clear();

            ulong[] tZobristKeyLine = Array.Empty<ulong>();
            if (curSearchZobristKeyLine != null)
            {
                baseLineLen = curSearchZobristKeyLine.Length;
                tZobristKeyLine = new ulong[baseLineLen];
                Array.Copy(curSearchZobristKeyLine, tZobristKeyLine, baseLineLen);
            }

            int curEval = 0, tattk = isWhiteToMove ? PreMinimaxCheckCheckWhite() : PreMinimaxCheckCheckBlack(), pDepth = 1;

            if (shouldSearchForBookMove)
            {
                (string, int) bookMoveTuple = TLMDatabase.SearchForNextBookMoveV2(moveHashList);

                if (bookMoveTuple.Item2 > 1)
                {
                    int actualMoveHash = NuCRe.GetNumber(bookMoveTuple.Item1);
                    List<Move> tMoves = new List<Move>();
                    GetLegalMoves(ref tMoves);

                    int tL = tMoves.Count;
                    for (int i = 0; i < tL; i++)
                    {
                        if (tMoves[i].moveHash == actualMoveHash)
                        {
                            if (debugSearchDepthResults)
                            {
                                Console.WriteLine("TLM_DB_Count: " + bookMoveTuple.Item2);
                                Console.WriteLine(">> " + tMoves[i]);
                            }
                            BestMove = tMoves[i];
                            //transpositionTable.Add(zobristKey, new TranspositionEntryV2(BestMove = tMoves[i], Array.Empty<int>(), 0, 0, 0ul));
                            chessClock.MoveFinished(globalTimer.ElapsedTicks - tTime);
                            return 0;
                        }
                    }
                }

                shouldSearchForBookMove = false;
            }
            int evalBefore = 0;
            Move[] tPV2Line = Array.Empty<Move>();

            do {
                maxCheckExtension = maxCheckExtPerDepth[Math.Clamp(pDepth, 0, 9)];
                curSearchDepth = pDepth;
                curSubSearchDepth = pDepth - 1;
                ulong[] completeZobristHistory = new ulong[baseLineLen + pDepth - CHECK_EXTENSION_LENGTH + 1];
                for (int i = 0; i < baseLineLen; i++) completeZobristHistory[i] = curSearchZobristKeyLine[i];
                curSearchZobristKeyLine = completeZobristHistory;
                //int estimatedNextEvalCount = 35;
                //if (pDepth != 1) estimatedNextEvalCount = (int)Math.Pow(Math.Pow(3 * evalCount / (pDepth - 1), 1.0 / (pDepth - 1)), pDepth);

                for (int window = pDepth == 1 ? 1000 : 40; ; window *= 2)
                {
                    int aspirationAlpha = curEval - window, aspirationBeta = curEval + window;

                    //Console.WriteLine("A: " + aspirationAlpha + " | B: " + aspirationBeta);
                    curEval = MinimaxRootCall(pDepth, baseLineLen, tattk, aspirationAlpha, aspirationBeta);

                    //Console.WriteLine(curEval);

                    if (aspirationAlpha < curEval && curEval < aspirationBeta || globalTimer.ElapsedTicks > limitTimestamp) break;
                }

                if (globalTimer.ElapsedTicks > limitTimestamp)
                {
                    curEval = evalBefore;
                }
                else tPV2Line = PV_LINE_V2.ToArray();

                //if (TTV2[zobristKey % TTSize].Item1 == zobristKey) BestMove = TTV2[zobristKey % TTSize].Item2;

                if (BestMove == NULL_MOVE) throw new System.Exception("TRIED TO MAKE A NULL MOVE");

                evalBefore = curEval;

                if (debugSearchDepthResults && pTime != 1L)
                {
                    int tNpS = Convert.ToInt32((double)nodeCount * 10_000_000d / (double)(pTime - limitTimestamp + globalTimer.ElapsedTicks));
                    int tSearchEval = curEval;
                    int timeForSearchSoFar = (int)((pTime - limitTimestamp + globalTimer.ElapsedTicks) / 10000d);

                    Console.WriteLine((tSearchEval >= 0 ? "+" : "") + tSearchEval
                        + " " + BestMove + "  [Depth = " + pDepth 
                        + ", Nodes = " + GetThreeDigitSeperatedInteger(nodeCount) 
                        + ", NGens = " + GetThreeDigitSeperatedInteger(legalMoveGens) 
                        + ", CGens = " + GetThreeDigitSeperatedInteger(capLegalMoveGens) 
                        + ", Cutoffs = " + GetThreeDigitSeperatedInteger(cutoffs) 
                        + ", TTHits = " + GetThreeDigitSeperatedInteger(tthits) 
                        + ", Time = " + GetThreeDigitSeperatedInteger(timeForSearchSoFar) 
                        + "ms, NpS = " + GetThreeDigitSeperatedInteger(tNpS) + "]");
                }

            } while (globalTimer.ElapsedTicks < limitTimestamp && ++pDepth < 179 && curEval > BLACK_CHECKMATE_TOLERANCE && curEval < WHITE_CHECKMATE_TOLERANCE);

            PV_LINE_V2 = tPV2Line.ToList<Move>();
            depths += pDepth - 1;
            BOT_MAIN.depthsSearched += pDepth - 1;
            BOT_MAIN.searchesFinished++;
            BOT_MAIN.evaluationsMade += evalCount;

            if (debugSearchDepthResults && pTime != 1L) PlayThroughZobristTree();

            curSearchZobristKeyLine = tZobristKeyLine;
            chessClock.MoveFinished(globalTimer.ElapsedTicks - tTime);

            return curEval;
        }

        private int MinimaxRootCall(int pDepth, int pBaseLineLen, int pAttkSq, int pAspirationAlpha, int pAspirationBeta)
        {
            PV_LINE_V2.Clear();
            //Console.WriteLine("a = " + pAspirationAlpha + "|  b = " + pAspirationBeta);
            timerNodeCount = 79;
            return NegamaxSearch(0, pAspirationAlpha, pAspirationBeta, pDepth, 0, pBaseLineLen, pAttkSq, NULL_MOVE, isWhiteToMove, ref PV_LINE_V2);
            //return isWhiteToMove 
            //    ? MinimaxWhite(0, pAspirationAlpha, pAspirationBeta, pDepth, 0, pBaseLineLen, pAttkSq, NULL_MOVE)
            //    : MinimaxBlack(0, pAspirationAlpha, pAspirationBeta, pDepth, 0, pBaseLineLen, pAttkSq, NULL_MOVE);
            //if (isWhiteToMove)e
            //{
            //    if (pAttkSq < 0) return MinimaxWhite(0, pAspirationAlpha, pAspirationBeta, pDepth, 0, pBaseLineLen, pAttkSq, NULL_MOVE);
            //    else return MinimaxWhite(0, pAspirationAlpha, pAspirationBeta, pDepth, 0, pBaseLineLen, pAttkSq, NULL_MOVE);
            //}
            //else
            //{
            //    if (pAttkSq < 0) return MinimaxBlack(0, pAspirationAlpha, pAspirationBeta, pDepth, 0, pBaseLineLen, pAttkSq, NULL_MOVE);
            //    else return MinimaxBlack(0, pAspirationAlpha, pAspirationBeta, pDepth, 0, pBaseLineLen, pAttkSq, NULL_MOVE);
            //}
        }

        private bool WhiteIsPositionTheSpecialCase(Move pLastMove, int pCheckingSquare)
        {
            if (pLastMove.isPromotion && pLastMove.isCapture && (pLastMove.promotionType == 4 || pLastMove.promotionType == 5))
            {
                if(pLastMove.startPos % 8 == pCheckingSquare % 8 && pLastMove.startPos == whiteKingSquare + 8) return true;
                if (pCheckingSquare > -1 && squareToRankArray[pLastMove.startPos] == squareToRankArray[pCheckingSquare] && pLastMove.endPos == whiteKingSquare - 8) return true;
            }
            return false;
        }

        private bool BlackIsPositionTheSpecialCase(Move pLastMove, int pCheckingSquare)
        {
            if (pLastMove.isPromotion && pLastMove.isCapture && (pLastMove.promotionType == 4 || pLastMove.promotionType == 5))
            {
                if (pLastMove.startPos % 8 == pCheckingSquare % 8 && pLastMove.startPos == blackKingSquare - 8) return true;
                if (pCheckingSquare > -1 && squareToRankArray[pLastMove.startPos] == squareToRankArray[pCheckingSquare] && pLastMove.endPos == blackKingSquare + 8) return true;
            }
            return false;
        }

        private int NegamaxSearch(int pPly, int pAlpha, int pBeta, int pDepth, int pCheckExtC, int pRepetitionHistoryPly, int pCheckingSquare, Move pLastMove, bool pIW, ref List<Move> pPVL)
        {
            /*
             * Check if the position is a draw by repetition
             * "pRepetitionHistoryPly - 3" because the same position can be reached at first 4 plies ago
             */
            if (IsDrawByRepetition(pRepetitionHistoryPly - 3)) return 0;


            /*
             * End of search, transition into Quiescense Search (only captures)
             */
            if (pDepth < 1 || pCheckExtC > maxCheckExtension)
                return NegamaxQSearch(pPly, pAlpha, pBeta, pDepth, pCheckingSquare, pLastMove, pIW, ref pPVL);


            /*
             * Check if the time is running out / has run out
             * For efficiency reasons only each 1024th time; still accurate on a few milliseconds
             */
            if (searchTimeOver & pPly != 0) return 0;
            else if ((++timerNodeCount & 1023) == 0) searchTimeOver = globalTimer.ElapsedTicks > limitTimestamp;
            nodeCount++;


            /*
             * Get & Verify the Transposition Table Entry 
             * If it's instantly usable (based on the flag value): 
             * Cutoff because theres no need to investigate twice in the same or lower depth in a transposition
             */
            var (_ttKey, _ttMove, _ttEval, _ttDepth, _ttFlag, _ttAge) = TTV2[zobristKey % TTSize];
            if (_ttKey == zobristKey)
            {
                tthits++;
                if (_ttDepth >= (pDepth - pCheckExtC) && pPly != 0)
                {
                    switch (_ttFlag)
                    {
                        case 0: if (_ttEval <= pAlpha) return pAlpha; break;
                        case 1: return _ttEval;
                        case 2: if (_ttEval >= pBeta) return pBeta; break;
                    }
                }
            }


            /*
             * Internal Iterative Reductions:
             * As long as no TT-Entry has been found, it's likely that this node isn't that interesting to look at
             * Next time we'll visit this node (either through Iterative Deepening or a tranposition), it will be
             * searched at full-depth
             */
            else if (pDepth > 3) pDepth--;


            /*
             * Setup Vals:
             * These values are mainly for being able to efficiently undo the next moves
             * Some of these are ofc also for searching / sorting
             */
            Move bestMove = NULL_MOVE;
            List<Move> childPVLine = new List<Move>();
            int tWhiteKingSquare = whiteKingSquare, tBlackKingSquare = blackKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1, curEval = BLACK_CHECKMATE_VAL + pPly, tV;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare], tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide, isInZeroWindow = pAlpha + 1 == pBeta, isInCheck = pCheckingSquare != -1, canFP, canLMP;
            byte tttflag = 0b0;
            isWhiteToMove = pIW;


            /*
             * Reverse Futility Pruning / SNMH:
             * As soon as the static evaluation of the current board position is way above beta
             * it is likely that this node will be a fail-high; therefore it gets pruned
             */
            if (!isInCheck && pPly != 0 && pCheckExtC == 0 && pBeta < WHITE_CHECKMATE_TOLERANCE && pBeta > BLACK_CHECKMATE_TOLERANCE
            && ((tV = (pIW ? NNUEEM_EVALUATION() : -NNUEEM_EVALUATION())) - 79 * pDepth) >= pBeta)
                return tV;


            /*
             * Just the functions for getting all the next possible moves
             * The "Special Case" occurs when two straight attacking pieces (rook or queen) attack 
             * the opponents king at the same time. The only time this can happen is through a promotion 
             * capture directly in contact with the king. "pCheckingSquare" will then be = -779
             */
            enPassantSquare = tEPSquare;
            List<Move> moveOptionList = new List<Move>();
            if (pIW) {
                if (WhiteIsPositionTheSpecialCase(pLastMove, pCheckingSquare)) GetLegalWhiteMovesSpecialDoubleCheckCase(ref moveOptionList);
                else GetLegalWhiteMoves(pCheckingSquare, ref moveOptionList);
            } else {
                if (BlackIsPositionTheSpecialCase(pLastMove, pCheckingSquare)) GetLegalBlackMovesSpecialDoubleCheckCase(ref moveOptionList);
                else GetLegalBlackMoves(pCheckingSquare, ref moveOptionList);
            }
            enPassantSquare = 65;


            /*
             * Move Sorting:
             * In the following order:
             * 1. TT-Move (best known move / refutation move)
             * 2. Captures
             *    2.1 MVV-LVA (Most Valuable Victim - Least Valuable Attacker)
             * 3. Killer Moves (indexed by ply; currently two per ply)
             * 4. Countermove
             * 5. History Heuristic Values (indexed by custom Move-Hashing)
             */
            int molc = moveOptionList.Count, tcm = countermoveHeuristic[pLastMove.situationalMoveHash], lmrM = 0;
            double[] moveSortingArray = new double[molc];
            int[] moveSortingArrayIndexes = new int[molc];
            if (_ttKey == zobristKey) {
                for (int m = 0; m < molc; m++) {
                    moveSortingArrayIndexes[m] = m;
                    Move curMove = moveOptionList[m];
                    if (curMove == _ttMove) moveSortingArray[m] = BESTMOVE_SORT_VAL;
                    else if (curMove.isCapture) moveSortingArray[m] = CAPTURE_SORT_VAL - MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
                    else if (CheckForKiller(pPly, curMove.situationalMoveHash)) moveSortingArray[m] = KILLERMOVE_SORT_VAL;
                    else if (tcm == curMove.situationalMoveHash) moveSortingArray[m] = COUNTERMOVE_SORT_VAL;
                    else moveSortingArray[m] -= historyHeuristic[curMove.moveHash];
                }
            } else {
                for (int m = 0; m < molc; m++) {
                    moveSortingArrayIndexes[m] = m;
                    Move curMove = moveOptionList[m];
                    if (curMove.isCapture) moveSortingArray[m] = CAPTURE_SORT_VAL - MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
                    else if (CheckForKiller(pPly, curMove.situationalMoveHash)) moveSortingArray[m] = KILLERMOVE_SORT_VAL;
                    else if (tcm == curMove.situationalMoveHash) moveSortingArray[m] = COUNTERMOVE_SORT_VAL;
                    else moveSortingArray[m] -= historyHeuristic[curMove.moveHash];
                }
            }
            Array.Sort(moveSortingArray, moveSortingArrayIndexes);


            /*
             * Futility Pruning Prep:
             * If the current static evaluation is so bad that you can add significant margins to the
             * static evalation and still don't exceed alpha, you'll prune all the moves from this node
             * that are not the TT-Move, tactical moves (Captures or Promotions) or checking moves.
             */
            canFP = pDepth < 8 && !isInCheck && molc != 1 && (pIW ? NNUEEM_EVALUATION() : -NNUEEM_EVALUATION()) + FUTILITY_MARGINS[pDepth] <= pAlpha;


            /*
             * Late Move Reductions Prep:
             * Since the move ordering exists, we can assume that moves 
             * late in the ordering are not so promising.
             */
            canLMP = pDepth > 2 && !isInCheck && molc != 1;


            /*
             * Main Move Loop
             */

            double[] NNUE_SAVE_STATE = new double[SIZE_OF_FIRST_LAYER_NNUE];
            int TNNUEC = 0;
            for (int m = 0; m < molc; m++) {
                int tActualIndex = moveSortingArrayIndexes[m], tCheckPos = -1;
                Move curMove = moveOptionList[tActualIndex];
                int tPTI = pieceTypeArray[curMove.endPos], tincr = 0, tEval;


                /*
                 * Making the Move
                 */
                NegamaxSearchMakeMove(curMove, tFiftyMoveRuleCounter, tWPB, tBPB, tZobristKey, tPTI, pIW, ref tCheckPos, ref NNUE_SAVE_STATE, ref TNNUEC);
                curSearchZobristKeyLine[pRepetitionHistoryPly] = zobristKey;
                debugMoveList[pPly] = curMove;


                /*
                 * Futility Pruning & Late Move Reduction Execution:
                 * needs to be after "MakeMove" since it needs to be checked if the move is a checking move
                 */
                if (m != 0 && !curMove.isPromotion && !curMove.isCapture && tCheckPos == -1) {
                    if (canFP)
                    {
                        NegamaxSearchUndoMove(curMove, tWKSCR, tWQSCR, tBKSCR, tBQSCR, tWhiteKingSquare, tBlackKingSquare, tPTI, pIW, NNUE_SAVE_STATE, TNNUEC);
                        continue;
                    }
                    if (canLMP && ++lmrM > 2)
                    {
                        tincr = -1;
                    }
                    //if (canLMR && ++lmrMoves > LMP_MOVE_VALS[pDepth]) tincr = -1;
                }


                /*
                 * Check-Extensions:
                 * When the king is in check or evades one, the depth will get increased by one for the following branch
                 * Ofc only at leaf nodes, since otherwise the search would get enormously huge
                 */
                if (pDepth == 1 && (tCheckPos != -1 || isInCheck)) tincr = 1;


                /*
                 * PVS-Search:
                 * For every move after the first one (likely a PV-Move due to the move ordering) we'll search
                 * with a Zero-Window first, since that takes up significantly less computations.
                 * If the result is unsafe however; a costly full research needs to be done
                 */
                if (m == 0) tEval = -NegamaxSearch(pPly + 1, -pBeta, -pAlpha, pDepth - 1 + tincr, pCheckExtC + tincr, pRepetitionHistoryPly + 1, tCheckPos, curMove, !pIW, ref childPVLine);
                else {
                    tEval = -NegamaxSearch(pPly + 1, -pAlpha - 1, -pAlpha, pDepth - 1 + tincr, pCheckExtC + tincr, pRepetitionHistoryPly + 1, tCheckPos, curMove, !pIW, ref childPVLine);
                    if (tEval > pAlpha && tEval < pBeta) {
                        if (tincr < 0) tincr = 0;
                        tEval = -NegamaxSearch(pPly + 1, -pBeta, -pAlpha, pDepth - 1 + tincr, pCheckExtC + tincr, pRepetitionHistoryPly + 1, tCheckPos, curMove, !pIW, ref childPVLine);
                    }
                }


                /*
                 * Undo the Move
                 */
                NegamaxSearchUndoMove(curMove, tWKSCR, tWQSCR, tBKSCR, tBQSCR, tWhiteKingSquare, tBlackKingSquare, tPTI, pIW, NNUE_SAVE_STATE, TNNUEC);


                if (tEval > curEval) {

                    /*
                     * A new best move for this node got found
                     */
                    bestMove = curMove;
                    curEval = tEval;
                    UpdatePVLINEV2(curMove, childPVLine, ref pPVL);

                    if (curEval > pAlpha) {

                        /*
                         * The "new best move" is so good, that it can raise alpha
                         */
                        pAlpha = curEval;
                        tttflag = 1;

                        if (curEval >= pBeta) {

                            /*
                             * The move is so good, that it's evaluation is above beta
                             * It results therefore in a cutoff
                             */
                            if (!curMove.isCapture) {

                                /*
                                 * As long as the move is quiet, the heuristics will get updated
                                 */
                                cutoffs++;
                                if (!isInZeroWindow) killerHeuristic[pPly] = (killerHeuristic[pPly] << 15) | curMove.situationalMoveHash;
                                countermoveHeuristic[pLastMove.situationalMoveHash] = curMove.situationalMoveHash;
                                if (pPly < 8) historyHeuristic[curMove.moveHash] += pDepth * pDepth;
                            }

                            tttflag = 2;
                            break;
                        }
                    }
                }

                childPVLine.Clear();
            }


            /*
             * Final Undo
             */
            isWhiteToMove = pIW;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;


            /*
             * Stalemate Detection
             */
            if (molc == 0 && pCheckingSquare == -1) curEval = 0;


            /*
             * Transposition Table Update
             */
            if (searchTimeOver && curSearchDepth != 1) return curEval;
            else if (pDepth > _ttDepth || happenedHalfMoves > _ttAge) 
                TTV2[zobristKey % TTSize] = (zobristKey, bestMove, curEval, (short)(pDepth - pCheckExtC), tttflag, (short)(happenedHalfMoves + pPly - 2));
            if (pPly == 0) BestMove = bestMove;


            /*
             * Return the final evaluation
             */
            return curEval;
        }

        private List<Move> PV_LINE_V2 = new List<Move>();

        private void UpdatePVLINEV2(Move pMove, List<Move> pSubLine, ref List<Move> pRefL)
        {
            pRefL.Clear();
            pRefL.Add(pMove);
            pRefL.AddRange(pSubLine);
        }

        private int NegamaxQSearch(int pPly, int pAlpha, int pBeta, int pDepth, int pCheckingSquare, Move pLastMove, bool pIW, ref List<Move> pPVL)
        {
            nodeCount++;

            //
            int standPat = NNUEEM_EVALUATION() * (pIW ? 1 : -1);
            if (standPat >= pBeta) return pBeta; // || pDepth < MAX_QUIESCENCE_TOTAL_LENGTH

            //if (standPat < pAlpha - 975) return pAlpha;

            if (standPat > pAlpha) pAlpha = standPat;

            //
            List<Move> moveOptionList = new List<Move>();
            if (pIW)
            {
                if (WhiteIsPositionTheSpecialCase(pLastMove, pCheckingSquare)) GetLegalWhiteCapturesSpecialDoubleCheckCase(ref moveOptionList);
                else GetLegalWhiteCaptures(pCheckingSquare, ref moveOptionList);
            }
            else
            {
                if (BlackIsPositionTheSpecialCase(pLastMove, pCheckingSquare)) GetLegalBlackCapturesSpecialDoubleCheckCase(ref moveOptionList);
                else GetLegalBlackCaptures(pCheckingSquare, ref moveOptionList);
            }


            // 
            Move bestMove = NULL_MOVE;
            List<Move> childPVLine = new List<Move>();
            int molc = moveOptionList.Count, tWhiteKingSquare = whiteKingSquare, tBlackKingSquare = blackKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare], tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            enPassantSquare = 65;
            isWhiteToMove = pIW;


            //
            double[] moveSortingArray = new double[molc];
            int[] moveSortingArrayIndexes = new int[molc];
            for (int m = 0; m < molc; m++)
            {
                moveSortingArrayIndexes[m] = m;
                Move curMove = moveOptionList[m];
                moveSortingArray[m] = -MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
            }
            Array.Sort(moveSortingArray, moveSortingArrayIndexes);


            //
            double[] NNUE_SAVE_STATE = new double[SIZE_OF_FIRST_LAYER_NNUE];
            int TNNUEC = 0;
            for (int m = 0; m < molc; m++)
            {
                int tActualIndex = moveSortingArrayIndexes[m], tCheckPos = -1;
                Move curMove = moveOptionList[tActualIndex];

                int tPTI = pieceTypeArray[curMove.endPos];

                if (pCheckingSquare == -1 && standPat + DELTA_PRUNING_VALS[tPTI] < pAlpha) break;

                NegamaxSearchMakeMove(curMove, tFiftyMoveRuleCounter, tWPB, tBPB, tZobristKey, tPTI, pIW, ref tCheckPos, ref NNUE_SAVE_STATE, ref TNNUEC);

                int tEval = -NegamaxQSearch(pPly + 1, -pBeta, -pAlpha, pDepth - 1, tCheckPos, curMove, !pIW, ref childPVLine);

                NegamaxSearchUndoMove(curMove, tWKSCR, tWQSCR, tBKSCR, tBQSCR, tWhiteKingSquare, tBlackKingSquare, tPTI, pIW, NNUE_SAVE_STATE, TNNUEC);

                if (tEval >= pBeta) return pBeta;
                if (pAlpha < tEval) {
                    pAlpha = tEval;
                    bestMove = curMove;
                    UpdatePVLINEV2(curMove, childPVLine, ref pPVL);
                }
                childPVLine.Clear();
            }


            //
            isWhiteToMove = pIW;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            var (_ttKey, _ttMove, _ttEval, _ttDepth, _ttFlag, _ttAge) = TTV2[zobristKey % TTSize];
            if (pDepth > _ttDepth || happenedHalfMoves > _ttAge)
                TTV2[zobristKey % TTSize] = (zobristKey, bestMove, pAlpha, (short)(-100 - pDepth), 3, (short)(happenedHalfMoves + pPly - 2));

            return pAlpha;
        }

        private void NegamaxSearchMakeMove(Move curMove, int tFiftyMoveRuleCounter, ulong tWPB, ulong tBPB, ulong tZobristKey, int tPTI, bool pIW, ref int tCheckPos, ref double[] NNUEV, ref int NNUEC)
        {
            int tPossibleAttackPiece, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPieceType = curMove.pieceType, tI;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
            pieceTypeArray[tEndPos] = tPieceType;
            pieceTypeArray[tStartPos] = 0;

            // NNUE
            for (int i = 0; i < SIZE_OF_FIRST_LAYER_NNUE; i++)
            {
                NNUEV[i] = NNUE_FIRST_LAYER_VALUES[i];
            }
            NNUEC = countOfPiecesHash;
            int NNsideVal = pIW ? 0 : 64, NNpieceTypeVal = NNsideVal + (tPieceType - 1) * 128;
            UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNpieceTypeVal + tStartPos);
            if (!curMove.isPromotion) UPDATE_NNUE_FIRST_HIDDEN_LAYER_ON(NNpieceTypeVal + tEndPos);

            if (pIW) {
                whitePieceBitboard = tWPB ^ curMove.ownPieceBitboardXOR;
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
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + (tPTI - 1) * 128 + tEndPos);
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPTI)) & 15) - 1) << (4 * tPTI);
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
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + (tPTI - 1) * 128 + tEndPos);
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPTI)) & 15) - 1) << (4 * tPTI);
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
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + (tPTI - 1) * 128 + tEndPos);
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPTI)) & 15) - 1) << (4 * tPTI);
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
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + (tPTI - 1) * 128 + tEndPos);
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPTI)) & 15) - 1) << (4 * tPTI);
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
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + (tPTI - 1) * 128 + tEndPos);
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPTI)) & 15) - 1) << (4 * tPTI);
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

                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + 512 + curMove.rochadeStartPos);
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_ON(NNsideVal + 512 + curMove.rochadeEndPos);

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
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + curMove.enPassantOption);
                        countOfPiecesHash |= (((countOfPiecesHash >> 4) & 15) - 1) << 4;
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
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPieceType)) & 15) + 1) << (4 * tPieceType);
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_ON(NNsideVal + (tPieceType - 1) * 128 + tEndPos);
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        zobristKey ^= pieceHashesWhite[tEndPos, 1] ^ pieceHashesWhite[tEndPos, curMove.promotionType];
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && ((int)(knightSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        break;
                    case 14: // Capture-Promotion
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPTI)) & 15) - 1) << (4 * tPTI);
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + (tPTI - 1) * 128 + tEndPos);
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPieceType)) & 15) + 1) << (4 * tPieceType);
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_ON(NNsideVal + (tPieceType - 1) * 128 + tEndPos);
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
            } else { 
                blackPieceBitboard = tBPB ^ curMove.ownPieceBitboardXOR;
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
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + (tPTI - 1) * 128 + tEndPos);
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPTI)) & 15) - 1) << (4 * tPTI);
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
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + (tPTI - 1) * 128 + tEndPos);
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPTI)) & 15) - 1) << (4 * tPTI);
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
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + (tPTI - 1) * 128 + tEndPos);
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPTI)) & 15) - 1) << (4 * tPTI);
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
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + (tPTI - 1) * 128 + tEndPos);
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPTI)) & 15) - 1) << (4 * tPTI);
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
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + (tPTI - 1) * 128 + tEndPos);
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPTI)) & 15) - 1) << (4 * tPTI);
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

                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + 512 + curMove.rochadeStartPos);
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_ON(NNsideVal + 512 + curMove.rochadeEndPos);

                        pieceTypeArray[curMove.rochadeEndPos] = 4;
                        pieceTypeArray[curMove.rochadeStartPos] = 0;
                        zobristKey ^= pieceHashesBlack[curMove.rochadeStartPos, 4] ^ pieceHashesBlack[curMove.rochadeEndPos, 4];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | curMove.rochadeEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << curMove.rochadeEndPos)) tCheckPos = curMove.rochadeEndPos;
                        break;
                    case 12: // En-Passant
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + curMove.enPassantOption);
                        countOfPiecesHash |= (((countOfPiecesHash >> 4) & 15) - 1) << 4;
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
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPieceType)) & 15) + 1) << (4 * tPieceType);
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_ON(NNsideVal + (tPieceType - 1) * 128 + tEndPos);
                        zobristKey ^= pieceHashesBlack[tEndPos, 1] ^ pieceHashesBlack[tEndPos, curMove.promotionType];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && ((int)(knightSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        break;
                    case 14: // Capture-Promotion
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPTI)) & 15) - 1) << (4 * tPTI);
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(NNsideVal + (tPTI - 1) * 128 + tEndPos);
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        countOfPiecesHash |= (((countOfPiecesHash >> (4 * tPieceType)) & 15) + 1) << (4 * tPieceType);
                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_ON(NNsideVal + (tPieceType - 1) * 128 + tEndPos);
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
            }
        }

        private void NegamaxSearchUndoMove(Move curMove, bool tWKSCR, bool tWQSCR, bool tBKSCR, bool tBQSCR, int tWhiteKingSquare, int tBlackKingSquare, int tPTI, bool pIW, double[] NNUEV, int NNUEC)
        {
            int tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPieceType = curMove.pieceType;
            pieceTypeArray[tEndPos] = tPTI;
            pieceTypeArray[tStartPos] = tPieceType;
            whiteCastleRightKingSide = tWKSCR;
            whiteCastleRightQueenSide = tWQSCR;
            blackCastleRightKingSide = tBKSCR;
            blackCastleRightQueenSide = tBQSCR;
            countOfPiecesHash = NNUEC;

            for (int i = 0; i < SIZE_OF_FIRST_LAYER_NNUE; i++)
            {
                NNUE_FIRST_LAYER_VALUES[i] = NNUEV[i];
            }


            if (pIW) {
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
            } else {
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
            }
        }

        private int MinimaxWhite(int pPly, int pAlpha, int pBeta, int pDepth, int pCheckExtC, int pRepetitionHistoryPly, int pCheckingSquare, Move pLastMove)
        {
            //if (pDepth < -8) Console.WriteLine(CreateFenString());
            if (IsDrawByRepetition(pRepetitionHistoryPly - 3) || (globalTimer.ElapsedTicks > limitTimestamp && pPly != 0)) return 0;
            if (pDepth < 1 || pCheckExtC > maxCheckExtension) return QuiescenceWhite(pPly, pAlpha, pBeta, pDepth - 1, pCheckingSquare, pLastMove);

            #region NodePrep()

            //transpositionTable.TryGetValue(zobristKey, out TranspositionEntryV2 transposEntry);

            var (_ttKey, _ttMove, _ttEval, _ttDepth, _ttFlag, _ttAge) = TTV2[zobristKey % TTSize];

            if (_ttKey == zobristKey)
            {
                tthits++;
                if (_ttDepth >= (pDepth - pCheckExtC) && pPly != 0)
                {
                    switch (_ttFlag) {
                        case 0: if (_ttEval <= pAlpha) return pAlpha; break;
                        case 1: return _ttEval;
                        case 2: if (_ttEval >= pBeta) return pBeta; break;
                    }
                }
            }

            List<Move> moveOptionList = new List<Move>();
            Move bestMove = NULL_MOVE;
            if (WhiteIsPositionTheSpecialCase(pLastMove, pCheckingSquare)) GetLegalWhiteMovesSpecialDoubleCheckCase(ref moveOptionList);
            else GetLegalWhiteMoves(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tWhiteKingSquare = whiteKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1, curEval = BLACK_CHECKMATE_VAL + pPly;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;
            byte tttflag = 0b0;
            enPassantSquare = 65;
            isWhiteToMove = true;

            #endregion

            #region MoveSort()

            double[] moveSortingArray = new double[molc];
            int[] moveSortingArrayIndexes = new int[molc];

            if (_ttKey == zobristKey)
            {
                for (int m = 0; m < molc; m++)
                {
                    int k;
                    moveSortingArrayIndexes[m] = m;
                    Move curMove = moveOptionList[m];
                    if (curMove == _ttMove) moveSortingArray[m] = BESTMOVE_SORT_VAL;
                    else if (curMove.isCapture) moveSortingArray[m] = CAPTURE_SORT_VAL - MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
                    else if ((k = historyHeuristic[curMove.moveHash]) != 0) moveSortingArray[m] = k * KILLERMOVE_SORT_VAL;
                    //moveSortingArray[m] -= transposEntry.moveGenOrderedEvals[m];
                }
            }
            else
            {
                for (int m = 0; m < molc; m++)
                {
                    int k;
                    moveSortingArrayIndexes[m] = m;
                    Move curMove = moveOptionList[m];
                    if (curMove.isCapture) moveSortingArray[m] = CAPTURE_SORT_VAL - MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
                    else if ((k = historyHeuristic[curMove.moveHash]) != 0) moveSortingArray[m] = k * KILLERMOVE_SORT_VAL;
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

                debugMoveList[pPly] = curMove;

                int tincr = 0, tEval;
                if (pDepth == 1 && (tCheckPos != -1 || pCheckingSquare != -1)) tincr = 1;

                tEval = MinimaxBlack(pPly + 1, pAlpha, pBeta, pDepth - 1 + tincr, pCheckExtC + tincr, pRepetitionHistoryPly + 1, tCheckPos, curMove);
                //if (m == 0) tEval = MinimaxBlack(pPly + 1, pAlpha, pBeta, pDepth - 1 + tincr, pCheckExtC + tincr, pRepetitionHistoryPly + 1, tCheckPos, curMove);
                //else
                //{
                //    tEval = MinimaxBlack(pPly + 1, pAlpha - 1, pAlpha, pDepth - 1 + tincr, pCheckExtC + tincr, pRepetitionHistoryPly + 1, tCheckPos, curMove);
                //    if (tEval > pAlpha) tEval = MinimaxBlack(pPly + 1, pAlpha, pBeta, pDepth - 1 + tincr, pCheckExtC + tincr, pRepetitionHistoryPly + 1, tCheckPos, curMove);
                //}

                //thisSearchMoveSortingArrayForTransposEntry[tActualIndex] = tEval;

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

                //if ((pDepth > 1 || pPly != 0) && globalTimer.ElapsedTicks > limitTimestamp)
                //{
                //    isWhiteToMove = true;
                //    zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
                //    allPieceBitboard = tAPB;
                //    whitePieceBitboard = tWPB;
                //    blackPieceBitboard = tBPB;
                //    fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;
                //    return WHITE_CHECKMATE_VAL;
                //}

                if (tEval > curEval)
                {
                    bestMove = curMove;
                    curEval = tEval;

                    if (pAlpha < curEval)
                    {
                        pAlpha = curEval;
                        tttflag = 1;

                        if (curEval >= pBeta)
                        {
                            if (!curMove.isCapture)
                            {
                                //killerMoveHeuristic[curMove.situationalMoveHash][pPly] = true;
                                cutoffs++;
                                if (pPly < 7) historyHeuristic[curMove.moveHash] += 7 - pPly;
                                //counterMoveHeuristic[pLastMove.situationalMoveHash][0] = true;
                                //if (pDepth > 0) historyHeuristic[curMove.moveHash] += pDepth * pDepth;
                            }

                            tttflag = 2;

                            //thisSearchMoveSortingArrayForTransposEntry[tActualIndex] += KILLERMOVE_SORT_VAL;
                            //if (pDepth >= curSubSearchDepth) Console.WriteLine("* Beta Cutoff");
                            break;
                        }
                    }
                }
            }

            isWhiteToMove = true;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            if (molc == 0 && pCheckingSquare == -1) curEval = 0;

            if (globalTimer.ElapsedTicks > limitTimestamp && pPly == 0 && pDepth != 1) return curEval;
            else if (pDepth > _ttDepth || happenedHalfMoves > _ttAge) TTV2[zobristKey % TTSize] = (zobristKey, bestMove, curEval, (short)(pDepth - pCheckExtC), tttflag, (short)(happenedHalfMoves + TTAgePersistance));

            if (pPly == 0) BestMove = bestMove;

            //if (!transpositionTable.ContainsKey(zobristKey)) transpositionTable.Add(zobristKey, new TranspositionEntryV2(bestMove, thisSearchMoveSortingArrayForTransposEntry, pDepth, curEval, allPieceBitboard));
            //else transpositionTable[zobristKey] = new TranspositionEntryV2(bestMove, thisSearchMoveSortingArrayForTransposEntry, pDepth, curEval, allPieceBitboard);

            return curEval;
        }

        private int QuiescenceWhite(int pPly, int pAlpha, int pBeta, int pDepth, int pCheckingSquare, Move pLastMove)
        {
            int standPat = TexelEvaluate();
            if (standPat >= pBeta || pDepth < MAX_QUIESCENCE_TOTAL_LENGTH) return pBeta;
            if (standPat > pAlpha) pAlpha = standPat;

            bool isSpecialCase = WhiteIsPositionTheSpecialCase(pLastMove, pCheckingSquare);

            //if (pCheckingSquare != -1)
            //{
            //    List<Move> tcmtMoves = new List<Move>();
            //    if (isSpecialCase) GetLegalWhiteMovesSpecialDoubleCheckCase(ref tcmtMoves);
            //    else GetLegalWhiteMoves(pCheckingSquare, ref tcmtMoves);
            //    if (tcmtMoves.Count == 0) return BLACK_CHECKMATE_VAL + pPly;
            //}

            #region NodePrep()

            List<Move> moveOptionList = new List<Move>();
            if (isSpecialCase) GetLegalWhiteCapturesSpecialDoubleCheckCase(ref moveOptionList);
            else GetLegalWhiteCaptures(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tWhiteKingSquare = whiteKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;
            enPassantSquare = 65;
            isWhiteToMove = true;

            #endregion

            #region QMoveSort()

            double[] moveSortingArray = new double[molc];
            int[] moveSortingArrayIndexes = new int[molc];
            for (int m = 0; m < molc; m++)
            {
                moveSortingArrayIndexes[m] = m;
                Move curMove = moveOptionList[m];
                moveSortingArray[m] = -MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
            }
            Array.Sort(moveSortingArray, moveSortingArrayIndexes);

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
                int tActualIndex = moveSortingArrayIndexes[m];
                Move curMove = moveOptionList[tActualIndex];

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

                debugMoveList[pPly] = curMove;

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

        private int MinimaxBlack(int pPly, int pAlpha, int pBeta, int pDepth, int pCheckExtC, int pRepetitionHistoryPly, int pCheckingSquare, Move pLastMove)
        {
            //if (pDepth < -4) Console.WriteLine(CreateFenString());
            if (IsDrawByRepetition(pRepetitionHistoryPly - 3) || (globalTimer.ElapsedTicks > limitTimestamp && pPly != 0)) return 0;
            if (pDepth < 1 || pCheckExtC > maxCheckExtension) return QuiescenceBlack(pPly, pAlpha, pBeta, pDepth - 1, pCheckingSquare, pLastMove);

            #region NodePrep()

            //transpositionTable.TryGetValue(zobristKey, out TranspositionEntryV2 transposEntry);

            var (_ttKey, _ttMove, _ttEval, _ttDepth, _ttFlag, _ttAge) = TTV2[zobristKey % TTSize];

            if (_ttKey == zobristKey)
            {
                tthits++;
                if (_ttDepth >= (pDepth - pCheckExtC) && pPly != 0)
                {
                    switch (_ttFlag)
                    {
                        case 2: if (_ttEval <= pAlpha) return pAlpha; break;
                        case 1: return _ttEval;
                        case 0: if (_ttEval >= pBeta) return pBeta; break;
                    }
                }
            }

            List<Move> moveOptionList = new List<Move>();
            Move bestMove = NULL_MOVE;
            if (BlackIsPositionTheSpecialCase(pLastMove, pCheckingSquare)) GetLegalBlackMovesSpecialDoubleCheckCase(ref moveOptionList);
            else GetLegalBlackMoves(pCheckingSquare, ref moveOptionList);
            
            int molc = moveOptionList.Count, tBlackKingSquare = blackKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1, curEval = WHITE_CHECKMATE_VAL - pPly;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;
            byte tttflag = 0b0;
            enPassantSquare = 65;
            isWhiteToMove = false;

            #endregion

            #region MoveSort()

            double[] moveSortingArray = new double[molc];
            int[] moveSortingArrayIndexes = new int[molc];

            if (_ttKey == zobristKey)
            {
                for (int m = 0; m < molc; m++)
                {
                    int k;
                    moveSortingArrayIndexes[m] = m;
                    Move curMove = moveOptionList[m];
                    if (curMove == _ttMove) moveSortingArray[m] = BESTMOVE_SORT_VAL;
                    else if (curMove.isCapture) moveSortingArray[m] = CAPTURE_SORT_VAL - MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
                    else if ((k = historyHeuristic[curMove.moveHash]) != 0) moveSortingArray[m] = k * KILLERMOVE_SORT_VAL;
                    //moveSortingArray[m] -= transposEntry.moveGenOrderedEvals[m];
                }
            }
            else
            {
                for (int m = 0; m < molc; m++)
                {
                    int k;
                    moveSortingArrayIndexes[m] = m;
                    Move curMove = moveOptionList[m];
                    if (curMove.isCapture) moveSortingArray[m] = CAPTURE_SORT_VAL - MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
                    else if ((k = historyHeuristic[curMove.moveHash]) != 0) moveSortingArray[m] = k * KILLERMOVE_SORT_VAL;
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

                int tincr = 0, tEval;
                if (pDepth == 1 && (tCheckPos != -1 || pCheckingSquare != -1)) tincr = 1;

                debugMoveList[pPly] = curMove;

                tEval = MinimaxWhite(pPly + 1, pAlpha, pBeta, pDepth - 1 + tincr, pCheckExtC + tincr, pRepetitionHistoryPly + 1, tCheckPos, curMove);

                //if (m == 0) tEval = MinimaxWhite(pPly + 1, pAlpha, pBeta, pDepth - 1 + tincr, pCheckExtC + tincr, pRepetitionHistoryPly + 1, tCheckPos, curMove);
                //else
                //{
                //    tEval = MinimaxWhite(pPly + 1, pBeta - 1, pBeta, pDepth - 1 + tincr, pCheckExtC + tincr, pRepetitionHistoryPly + 1, tCheckPos, curMove);
                //    if (tEval < pBeta) tEval = MinimaxWhite(pPly + 1, pAlpha, pBeta, pDepth - 1 + tincr, pCheckExtC + tincr, pRepetitionHistoryPly + 1, tCheckPos, curMove);
                //}
                
                //thisSearchMoveSortingArrayForTransposEntry[tActualIndex] = tEval;

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


                //if ((pDepth > 1 || pPly != 0) && globalTimer.ElapsedTicks > limitTimestamp)
                //{
                //    isWhiteToMove = false;
                //    zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
                //    allPieceBitboard = tAPB;
                //    whitePieceBitboard = tWPB;
                //    blackPieceBitboard = tBPB;
                //    fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;
                //    return BLACK_CHECKMATE_VAL;
                //}

                if (tEval < curEval)
                {
                    bestMove = curMove;
                    curEval = tEval;

                    if (pBeta > curEval)
                    {
                        pBeta = curEval;
                        tttflag = 1;

                        if (curEval <= pAlpha)
                        {
                            if (!curMove.isCapture)
                            {
                                cutoffs++;
                                //killerHeuristic[pPly] = (killerHeuristic[pPly] << 12) | curMove.situationalMoveHash;
                                if (pPly < 7) historyHeuristic[curMove.moveHash] += 7 - pPly;
                                //counterMoveHeuristic[pLastMove.situationalMoveHash][0] = true;
                                //if (pDepth > 0) historyHeuristic[curMove.moveHash] += pDepth * pDepth;
                            }

                            tttflag = 2;
                            //if (pDepth > 0) historyHeuristic[lastMadeMove.moveHash] += pDepth * pDepth;
                            //thisSearchMoveSortingArrayForTransposEntry[tActualIndex] += KILLERMOVE_SORT_VAL;
                            //if (pDepth >= curSubSearchDepth) Console.WriteLine("* Alpha Cutoff");
                            break;
                        }
                    }
                }
            }

            isWhiteToMove = false;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            if (molc == 0 && pCheckingSquare == -1) curEval = 0;

            if (globalTimer.ElapsedTicks > limitTimestamp && pPly == 0 && pDepth != 1) return curEval;
            else if (pDepth > _ttDepth || happenedHalfMoves > _ttAge) TTV2[zobristKey % TTSize] = (zobristKey, bestMove, curEval, (short)(pDepth - pCheckExtC), tttflag, (short)(happenedHalfMoves + TTAgePersistance));

            if (pPly == 0) BestMove = bestMove;

            //if (!transpositionTable.ContainsKey(zobristKey)) transpositionTable.Add(zobristKey, new TranspositionEntryV2(bestMove, thisSearchMoveSortingArrayForTransposEntry, pDepth, curEval, allPieceBitboard));
            //else transpositionTable[zobristKey] = new TranspositionEntryV2(bestMove, thisSearchMoveSortingArrayForTransposEntry, pDepth, curEval, allPieceBitboard);

            return curEval;
        }

        private int QuiescenceBlack(int pPly, int pAlpha, int pBeta, int pDepth, int pCheckingSquare, Move pLastMove)
        {
            int standPat = TexelEvaluate();
            if (standPat <= pAlpha || pDepth < MAX_QUIESCENCE_TOTAL_LENGTH) return pAlpha;
            if (standPat < pBeta) pBeta = standPat;

            bool isSpecialCase = BlackIsPositionTheSpecialCase(pLastMove, pCheckingSquare);

            //if (pCheckingSquare != -1)
            //{
            //    List<Move> tcmtMoves = new List<Move>();
            //    if (isSpecialCase) GetLegalBlackMovesSpecialDoubleCheckCase(ref tcmtMoves);
            //    else GetLegalBlackMoves(pCheckingSquare, ref tcmtMoves);
            //    if (tcmtMoves.Count == 0) return WHITE_CHECKMATE_VAL - pPly;
            //}

            #region NodePrep()

            List<Move> moveOptionList = new List<Move>();

            if (isSpecialCase) GetLegalBlackCapturesSpecialDoubleCheckCase(ref moveOptionList);
            else GetLegalBlackCaptures(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tBlackKingSquare = blackKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;
            enPassantSquare = 65;
            isWhiteToMove = false;

            #endregion

            #region QMoveSort()

            double[] moveSortingArray = new double[molc];
            int[] moveSortingArrayIndexes = new int[molc];
            for (int m = 0; m < molc; m++)
            {
                moveSortingArrayIndexes[m] = m;
                Move curMove = moveOptionList[m];
                moveSortingArray[m] = -MVVLVA_TABLE[pieceTypeArray[curMove.endPos], curMove.pieceType];
            }
            Array.Sort(moveSortingArray, moveSortingArrayIndexes);

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
                int tActualIndex = moveSortingArrayIndexes[m];
                Move curMove = moveOptionList[tActualIndex];

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

                debugMoveList[pPly] = curMove;

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

        #region | NNUEEM |

        private int countOfPiecesHash = 0;
        private double[] NNUE_FIRST_LAYER_VALUES = new double[SIZE_OF_FIRST_LAYER_NNUE];

        private const int SIZE_OF_FIRST_LAYER_NNUE = 16;
        private double[] NNUE_FIRST_LAYER_BIASES = new double[16] { -2.58654379394722, 3.00212666493759, -1.6933621904751255, .6499282881853652, .13293246445654916, -2.231681547036522, -1.2669550103889327, -1.965421654934029, -1.643189135370266, .4324173101747738, -1.0669600532600048, 1.1249326785712395, -1.1389333348381732, -3.548794701949575, -2.823568253503469, -1.713592171945428 };
        private double[,] NNUE_FIRST_LAYER_WEIGHTS = new double[768, 16] { { -.10386524804300512, -.6438399512726427, .9699765005148908, .011119075555914737, .7144758577472272, -.229660326162197, -.508516703404845, .9533917086848762, -.7772144656921343, .787699452808674, .5928159208751191, .3816351799178277, .9992029780529255, -.2234046224467059, -.14749853786249867, -.5399724973820945 }, { .3978472788352174, -.348458556711154, -.9269775861192711, .5304006833408996, .17997960472533014, .4241432898245965, -.5639984115818262, .897674096071793, -.3529749241429747, .10754025026003977, .1423864612724588, .008932840365116546, -.6585694957966131, -.03318607333751289, .09795393207898817, -.9539671338950027 }, { .012920074209728849, -.5298212974614769, .3377572648819873, -.2876875762735578, .6796785094234516, -.006221892115316985, .16103255031702046, .4452199007242712, .7017311024892772, .9545948716429857, .7462104678617902, .9341191754187854, .8597617452141659, -.4691446686019032, -.792157079364483, -.2071667016037284 }, { .25520758508691377, -.3374953385907937, .8079663426361834, .013046397569963064, -.24420807962948277, -.5933112845809076, -.0315887425978707, -.5909065329458492, .5918948605600782, .6273672783671613, -.8434078478457538, -.5149628585126906, -.037447893291520984, .07498384678262004, .9334664309284866, -.353641443284447 }, { .15247381757097722, .9241116189258958, .4851937461100726, -.10478431113116327, -.6478345562266155, .33242960321766724, -.8104854680421845, .9016489916912993, -.7272595766482601, -.8061076486964389, .9146979327441283, -.49802399004013487, -.4849659688688919, .11510189847364183, -.17145249263395934, -.9979727140380021 }, { .46896154862851547, -.5311149072104602, -.15703708114824821, .46388878499439, -.6554899761317696, .22807619886895325, -.06631298201101399, .8803625741578491, .7188919993985539, .053213629878768653, .7798680224504466, -.8087023630857901, -.5356107609681766, .8922677858828443, .44211114239920724, -.398854146286735 }, { .7775467416269397, -.9254929111256132, .33257726975564617, -.6315730629004883, -.9379759497553628, .9985763857994345, -.2591822829061723, .9054962336042636, -.2514672566074716, .4678029797553025, .5330746493527534, .8177702650648306, -.9136728939838068, -.5892096264114222, -.5964506632346989, .8161185010823069 }, { -.47238720689642455, .27304945644542866, -.6919205739687648, -.9964792442238779, -.878890780747059, .07371244484094497, .8867926401206923, .6792002043639449, .28453391802501815, -.5640486182703317, .04521208662968057, .37916851676683927, -.03585744936256985, .2925175604159933, -.4801272651919273, -.7683296450094506 }, { -2.933917863199012, -1.1056273524157665, 1.7139269543852471, -4.095045472289656, .6646001694023594, -1.2887964901987914, .14744005326795467, -1.0608906449552906, 5.303397019675894, 1.8852854286828167, .5148542468462299, .22923243209392444, 2.5318761674034684, .6298282937486924, -1.411158138119373, -.5329081011469607 }, { -1.8410917788670191, -2.3682913758812014, 1.7993227989533949, -3.448098807364572, -1.4394053475088464, -.2155329040343077, -3.3976892998527277, -2.0102052029892454, 3.252527722041956, 1.8103045152774246, 1.9290836881695825, 2.279493454843106, 1.001160010598366, 1.5929775259387453, -3.0978810801117396, .3988148496110814 }, { .1788915167748558, -3.040845853443049, 3.736029328232016, -3.7108280367110145, -2.03808774878141, -2.948577217502376, -2.2886559557412807, -.13568324087615125, 1.4165911524533934, .7924699738926335, 1.2654618209583717, 2.0467271174618573, 1.7551775886233454, 1.3285489753208968, -.19360020788644566, -1.9766858995838898 }, { -.4869007330895315, -1.2222477503261373, -1.0324350293742786, -1.3448620419402362, -.2533566952333643, -.4206211867242549, .17510010992156128, 1.2719167606744408, .6536406012375257, .680931689266114, -.1454692160018182, .8461585676621473, .7608920258074088, .026639347752207627, -2.142091532825125, -1.0476953926506358 }, { -.3601148301559408, -2.168304688335002, -.9076506416808275, -4.308351546682807, -.6685147283289608, -.45299979664485296, -1.1461157054545539, .5760963001490588, 2.8061773490547184, 3.320992734409096, 2.903048611218372, 1.2262837776499038, .4175739939115212, .26532772638668867, -.12083709839096286, -.41131221076450714 }, { .742387828858222, -3.7891924487481754, -1.0241222896012745, -1.9267383473577353, -2.011249919428389, -.898320651041397, 1.0308163123778515, -.9883340884561564, 5.464195117334778, .40929328858160946, -.8223126209132722, 1.9954335368483955, -.3828358335072471, 2.286167133866419, -1.2468429318978624, 1.831261920706059 }, { 2.7794967673128856, -5.117061400968245, -3.985351241001674, -1.8043467294982698, -4.034733331026243, -2.85273755811859, 2.102806942735314, -.5703354273958667, 4.106252744922594, 2.5159619081366524, -.29790118971308654, .825624665640749, .06381307538491654, 1.3071742810398468, -.6578534742966177, -4.566700225662685 }, { 1.4752184334063505, -.6259361794830205, -1.5204591771676197, -2.933754605795763, -4.973690412022764, -.7067083502166158, 1.3028109507281151, .5777483087732189, 2.041081860576805, -.5192314264012208, -.2174390703841012, 3.0487857826807363, 1.8194694133017404, 1.8131413431273569, -4.837957641672437, -1.8114357217742076 }, { -.42341182170356284, -1.5260867298681282, 1.7114044263715467, -4.537131688403236, .9569386295728409, -1.4365328269182351, -1.38341402810254, 1.1700456812130673, 5.284069376805659, -.01732069735988689, -.0796902844702035, .2898406670054688, 1.6636910645633458, .2186493124940006, -.47291526426855646, -1.5861005867554385 }, { -1.9747100508680027, -1.7954120432514296, .12147526458501715, -3.115500357210695, -.14352287047496332, 1.1542327745267167, -2.670062017887872, 1.066136476013407, 3.667633229788893, 1.2472517933258616, 1.1952743835108122, -2.274130885630307, 2.294300218442665, 1.5600256860850772, -2.2894325596080125, -1.9359796236802422 }, { -2.4318461728242333, -1.9482475706536355, .7713712671830085, -3.771072083915406, -1.5399033358739016, -3.03449144531377, -.6347070050383403, .1401464058239203, 3.886292130217119, .9527778221407932, .12221832545525366, .5811455105491108, -.2875843648247763, 2.9950936089467697, 1.1633350859035057, -1.259615356037499 }, { -2.046758111567098, .840976729110934, -3.785056740846127, -1.2678030257438393, 2.0185858314101592, -1.309796946046289, -.4832554752923646, -.8464996118623218, 3.205666873947304, 1.096625251773301, -.08148849834867584, .40118194251444034, 1.6383971913547701, 2.86433709372965, -2.7309381294349135, -1.6986410834894512 }, { -2.201297539543879, -1.014566761246624, -2.4936319939907685, -1.1329948039565323, -.016984876520063776, 1.4000181732048496, .5180639070672336, -1.1592096194028476, 4.120491300403422, -.773649438492592, 2.7607840386794504, .6353907799665599, 1.5997099662457739, -.8138130685995684, -2.2296641394320362, -3.200219091549371 }, { 2.3659620075030126, -3.961126279702098, -1.6124321763675922, -5.629940164705034, -1.9420446521684038, -.5343383149199787, .3911382158206371, -3.05622265429294, 4.323174423671476, -.3338606978992541, 1.2254376455706633, .5871295501512063, 2.7639026039311667, .09154391811095555, -.6919478945562885, 1.810582413882194 }, { 2.211824319728981, -3.404626419673262, -2.3623681795904754, -1.4747214057399851, -3.2980663413944336, -1.3303656563957964, .9390206319552021, -.18944766407674388, 4.801070039947007, .4856206071050884, -.6223156156590228, 1.2256810719503644, 2.363076762215265, .9103065531752675, .5629204995335134, -2.7874938908085194 }, { 2.4133209076198887, -1.3008670889782417, -2.596520950221562, -2.763459814192417, -1.901594520805071, -2.0681049805493563, 1.1705218807317324, .38179379515060574, 1.8825651917494737, -1.1384283585174455, 1.0980482999943668, 2.133263619695249, 1.1882873744645144, 2.0329516902002194, -3.4426718839570616, -.908763177429864 }, { -1.338955097732994, -2.3040152700690313, 1.947875415953005, -4.510406251870975, .7638217221857535, -.12992763428407045, .3900132684591797, -1.5747821122525358, 3.9723260967694376, 2.52976862408902, 1.881113507219749, 3.0309905010244145, -.318592658763862, .608210994278611, -1.2601836231339891, -1.1067100059317394 }, { -.7872639089714646, -1.941630832401913, .29243242323959423, -4.7551574915170995, 1.9212172510429482, -.3107877338527234, -.463171691764092, -1.1278684057939774, 3.8183801992550177, 2.2455617070250544, .5742259383568644, 2.478972701935964, -.7446489662289261, 3.255373779933134, -1.492490149912548, -1.1942586178026176 }, { -.8784123817517512, -2.2207720470658954, 1.4278551095192478, -4.976578227249262, -.2512117984846453, -1.791666211994768, .17334066929240352, .14806873678580798, 2.670595962727094, .11528235413853438, 1.0824942951871346, 1.2779246725571702, -.43078717871920236, 1.786381558155681, 1.8611211786707804, -4.288130373635686 }, { -1.897752349084061, .1470440883489074, -.6938995568085818, -2.659106302089782, -1.985690500364745, .010289842660612145, -.48930631131600355, .6385863708972418, 3.5052146167235825, .08724950384417948, 2.5105303919296365, 1.5091613390767762, .6615816811672001, 2.6987254371600553, -1.71146955217083, -.3420551001961123 }, { -.32052888831060844, .5160554088753478, -2.1403892324227787, -1.7674306399461137, -1.0655597201333933, -3.7290841315334218, -1.1958979694718448, .9002808464906691, 3.312909249730009, -.3073400304255745, .5191585099905986, 1.939288159751744, 1.4858633682582574, .8751409389123225, -.6057669892627673, -3.7848404582440622 }, { 1.8910577166112517, -2.790355502924681, .5801015109313626, -2.7544713008260153, -2.2243155877746954, 1.0285493910453105, -.0023850851444575357, -.39368597901029395, 2.9415501463607754, -1.4397154659038995, .9870124009129821, 3.637320668804485, .743331510948904, 1.1411574785856216, -2.286393431956071, -.8234547305395181 }, { 2.860805654656988, -2.573693048170866, -3.2482679947317536, -1.221070448355888, -2.1306027804239767, -1.885303146660425, 1.5976616969418256, .3123325737683708, 2.650603255454413, -.8190723200775977, 2.100794543855802, 3.1439333774724907, .3060582753312625, 2.4192141719776523, -.5116722718970504, -4.148496045992244 }, { .7868761769832558, -1.3447783029869156, -1.3573166079255923, -1.385322451310243, -2.4636784245473526, .18883131267289388, -.6869605015983806, .2253236630098841, 2.846531276464639, -1.5399096920410278, 1.5336055617605917, 2.1267324185390764, -.43120081288690193, 1.3148418136535924, -4.248263691361911, -1.2273443566577151 }, { -1.5379918640996972, -.858960721556123, .8385863416457912, -6.582946704327177, -.17982086376520123, -.33882130053804765, -.4279150176314864, 2.6408283018615055, 3.236737231980737, 2.3217973258900604, 1.6964834600223717, .8016207622463503, -.9771437283545426, 1.9281463918246886, .30831494902370954, -1.40003173982691 }, { -.40874639422013687, -1.0758079159414609, -1.135396629157775, -5.520701587913356, 1.3315746462319873, -1.997188826527721, .2029713276853381, .6969149026152579, 2.6711145084046377, 4.105087844408948, 3.9632800564668873, -1.347858039855954, .6836744001069875, -.8656460551504893, -1.3363260751069372, 1.2837114094985729 }, { -1.5039196440659242, -1.682269657721105, .8959420434518386, -6.304510085125131, -2.471106912990466, -.10152842996197939, -.9053801148230166, -.4705964134450406, 2.3947089126869425, 2.3431274850065362, -.6574748346363049, -.451172980779474, -.04494398285297748, -.14622015140255193, .27147958831133145, -3.1691919396019728 }, { .7135427725259265, -.6582019723793551, -1.9769596784982155, -4.204506815020768, -1.9917359561727672, 1.4358623684937157, -.032688040216272904, .7298417660296758, 1.355663834396148, 3.306219195442572, 5.9794135020276205, -2.2239487325263005, 1.3421454455414186, -1.3523901137665146, -2.868627968593426, -.5113770127030687 }, { 1.643396915406509, -1.148401954285201, -3.3662499685633134, -4.728764773663504, -1.0638692007066533, -1.6971253332131853, -.1460759522677082, -1.1987956783824592, 2.979242233893191, -.11388675071490065, 4.670212679537521, 1.9896046410289614, -.533487248994161, -.0277330716180161, 1.2264072098139411, -1.6985031543956506 }, { .8064826447784563, -2.3628897577890324, -1.4644084629059562, -4.778244706198502, -2.6261067480631883, 1.9576418937635587, -.21078269646191242, .015419148818447862, 1.6543603824317503, -.3643250497025681, 1.5015181242827145, 5.131620632709752, .33969319266774006, .837840176833466, -3.7365143533256795, .750950758388173 }, { 2.490220938630255, -3.08306255218398, -.7565514499489913, -3.492348567118612, -2.3380610488687434, -1.194963736221443, .5481192416257771, 2.2391434252169704, 1.0077042942322396, -1.6263455993623948, 2.1153916924877474, 3.6538284026974015, .5515258852970115, 2.2021794431072985, -1.003229959280147, -2.2015407245058323 }, { -.38769569600596276, -.4675440072463053, .42580851573014594, -3.4715716195351596, -3.22596661916442, -.3134556912288238, -.042036334277701655, .4378947233983768, 1.9284957214701808, -.29304191977803323, 1.2660655383400348, 1.990505577280955, 2.7492436603470325, .32424030008509913, -3.809628898787478, -.8258169317141262 }, { -.7774645762953873, .8253109383305088, .5457562950496847, -9.4182270113318, -1.9601539749865193, .3505506114468995, -.7317694030286852, 2.7200038142342238, .6015238102499806, 6.3955218032800145, .8144301197211664, -.544418986836347, -.4248045438047558, -.1322701447418246, -2.7010736010978453, -2.464031992871528 }, { -1.5679499433471733, -.848737393524322, -1.0807087098173513, -7.454987423262074, 1.2269071105953262, -1.0826068545626872, -.9745734138608079, 1.8232713203468442, -.31342892178280024, 6.123342143951052, .17882840856508042, -1.6495892813838182, -.43579713031827744, .7176585821417267, -1.8964510257988443, -1.9726804126498307 }, { -1.3501370959493237, 1.040874010824317, -.8553308654325731, -7.259797004678197, -1.684705496507981, -.265337880042587, -1.618298637544534, .2851696584293346, 1.7662981708335042, 6.960713720771435, -.3634383888130305, .5821486585995685, -.6662548631893909, -.45405815399717614, -1.3613341718326093, -2.045554871212831 }, { -1.715183770987457, 1.6041987263705926, -1.7254213791954403, -6.553796787398383, -2.353987934816734, -.9118800739276299, -2.0663781519243734, .6237624240245887, 1.3376423702846592, 2.7535920125718607, .5046926322066447, 3.5587945919663215, .501578989213, 2.120092041362464, -2.6158151016937197, .505555477955211 }, { 1.4159476239312923, -.8660200915169911, -1.9589847238243545, -6.920205372477473, -3.0415223265910254, -.21687404539434035, -.15816109790079477, .0934924317685427, -2.1023190583759552, 3.4263265088501864, 2.3449998897353597, 4.358328151341005, -.8315979636742606, -.13786676821919974, .48614278629299507, -2.2991472991598783 }, { -.40811888850024935, .23920181463617243, -.8240757032138599, -5.918652321242525, -2.2588740349075143, .020758909298497042, -1.026561436973393, 3.538809774692006, -1.4332209971816563, 1.9158342010105325, 1.0729849924635753, 5.183971923768553, -.7423941674188491, -.1676534263802877, -4.118323613268879, 1.165898411303961 }, { -1.1855895585301328, -.606415229188781, -1.7026993720783705, -6.7538702641221535, -2.8671278742257704, .8528437278323329, .26580207642076087, 2.2305290690580106, -.6536927214933475, .12089736229394002, -.9491271733235378, 5.728347911465488, 1.1849381036095705, -1.4025125484652652, -1.5820279523696776, -2.1073357337481293 }, { -2.0490455374333196, -.0838842792401436, -2.0698671422079693, -5.42641722534431, -4.423573554687804, -1.4725238083645316, -1.75044380447249, 2.277267150273956, 1.6446054795823377, -1.207739055114906, .21963999973593473, 1.900250679505768, 2.274232935579633, 2.735293978626645, -2.312606487250774, -1.7425808903123996 }, { .3907636850865433, -.7160502588351983, -1.8173596329466806, -10.060020152840611, -1.9543237170369134, -1.0146786455828427, -1.7239406273674818, .9696594252710513, .2682358840591309, 6.4753911887613524, -.2367193760559336, 4.700335737608834, -.2866396735860482, 3.958176916534219, -2.8104957559787747, -2.8605242061842517 }, { -1.502847628313579, 1.8503472112966293, -1.4386203491749878, -8.926600657657449, -2.6168315455817206, -2.024417853581991, -1.122278282672044, 3.084072491892497, 1.7355063259303405, 7.9083393071474495, 1.4028372131009634, 3.732218870119917, .676180836324225, 1.3707114138465526, -4.028739759961528, -2.2989581498530023 }, { -1.5886177413291218, .43502773960695956, -1.543723690491024, -9.065825206213711, -3.5630141726590216, -2.605805153033103, -2.1389754433550747, .8670747606732023, 2.2768479709823235, 4.603137560937941, 2.9243113552700444, 5.85600655408116, .7523672998522086, 3.025347689907276, -4.273146861760705, -2.486109235471618 }, { -1.2340392366666308, .8041417581256888, -2.2724003363277263, -9.122751335478142, -4.070722853750999, -1.0095241634497159, -1.2269080409294184, .25333432150304686, 2.6055894671986173, 6.437644532501872, 1.2277992835385205, 4.157400734740861, 1.949141221524971, 3.020820569556864, -3.101410088742718, -.9538595989697212 }, { -1.1894716483296737, 2.0285085687122746, -3.7209692207952734, -8.789254764350892, -3.9413615121034913, -1.7271683429632059, -1.9949775681405153, -.17171164760939042, .5891895271406019, 3.80255510188598, 2.347005703745622, 5.2081938908677765, 2.965219837756369, 2.1175962471057477, -2.3939991671052443, -2.106262044464117 }, { -1.6499672866159065, -.011034135380313037, -3.2265888273103838, -9.326915161836936, -3.232283837354735, -1.1990863327176233, -1.0227896805109096, 2.1777103948873804, 2.48007600174147, 3.3274124283183784, 3.0276749641181198, 6.069412062714022, .4822624107636706, 2.3652550163417905, -3.4086639558469005, -2.5840587768496897 }, { -1.58580288125833, -1.0902792239837267, -1.636151566068381, -7.791273411668569, -1.5691530489103367, -1.6744373058509134, -1.460387090932152, 2.4667136971520343, -.722594778427058, 5.622375406648554, 1.168344057425456, 5.5943156528325755, 1.3639744157049305, 1.4608412666986028, -2.5648008129302955, -1.7053265957615948 }, { -2.86190721182558, .5469762556389092, -2.7930179289918535, -8.627724854990506, -3.458884906176875, -1.8642727145433584, -2.281137094085535, .028272576324905797, 1.9688320390025127, 6.138324533739729, -.7273007566470467, 6.36569865573236, .5141380042583187, 1.862322486140694, -3.6995568849570377, -2.827060773207982 }, { -.7905094361357596, -.334496425219628, -2.1488920392055424, -.34868804123855146, .4683042440328807, -.47801156158178354, -.8671484610332865, .8521349994732367, -.7098654252381693, .8786013094509987, .06495381656272027, .3124247230643468, -.2416583139647882, .7863813219842597, -.7629695700598527, -.36086297180249804 }, { .1047652721154444, -.28317426363783293, .8302519998637745, .9445463857040746, -.8759409563647469, .20126533414001724, .9380531883072472, -.9688061321236796, .1426332245881181, -.1322057040931952, -.4002758531742896, .4975261302892695, .18873648772978857, .24509720001086577, .08630127965276979, .45520720154739247 }, { -.9943575821448436, -.20495851259970888, .39107408521588183, -.21626478165499563, -.12771953699150296, -.3440476292755983, -.5339044460148332, .45176232440176545, .0989684532491244, -.15550764971551367, -.11043921029140757, .9753066562505661, -.09008876650655595, .3855975887712495, .34767060978990694, -.08228535053782093 }, { .1498604546555402, -.7189705429895934, .9543388932181558, .9870074673378963, .27156190163731986, -.7100209147809748, -.6414048290229835, -.8191513675413316, -.5473567612631964, .728221010353501, .11435300348831201, .513202682162959, -.8030808376241771, .8269195796953472, -.11888273641480218, -.9160513472541536 }, { .6383594775252761, -.11726313827889223, -.3352535543117068, .730404375137067, .48272303000809536, .9975667300996702, -.5123221891002396, .17454749122190094, .22242912905382095, .5055360860269025, .2195035957847591, .35845292029649634, .7801745969961598, .44178232608240364, -.4634505919944145, .24958431229569777 }, { .26469105201886056, -.4828318647601386, -.5789461477734243, -.6796697249839245, -.18296532851615055, -.172158607999372, -.050262251704051186, -.8456130968520401, -.33682844275435575, -.4378540970045579, -.7168018063357375, -.1320940939024955, -.7819138430154684, -.937548863929476, -.6027849622896468, .5145127195503039 }, { .2628301584260686, -.5665522737342206, .6876488115355099, -.12708166725194103, .6303054246642024, -.31830475398925406, -.9593681515087951, .15025581534957766, -.6525223945263654, .32946458793605804, .7340675731465613, -.17101861273187846, .2658649013859806, .32789949643309835, .4118375055816572, -.10349612446587098 }, { -.6391366720872949, .9599410622739866, -.4371321567976483, -.6471132533716855, .659019780507289, .3203164598333943, -.13248628769892057, .4355535829903714, .5683089696613959, -.9267618471003907, .27540515346661243, -.6075572954848394, .8259419287163627, .44965537859226723, -.5290352335599988, -.8345805206348564 }, { -.7145996247579514, -.010888851007068157, .934483158231945, -.8289089036376052, -.9958254345614692, .014942639077476949, .8146656237125947, -.84221000374243, -.7047670960133754, .1330438241284828, .8950464983770643, -.11912702080744442, .3016681327335864, .1049043812070245, .6493202653745178, .5870303949767135 }, { -.8455595768519009, -.9199793024720144, -.9727804576523589, -.6734069689251767, .932416119432478, .3439613523111009, .3894617930528439, -.5023145497375048, .1492207926060105, .8632010546412512, .6844357509297689, .27155397153247063, -.9158138864275058, .010473630148024604, -.28654689501624886, .8246500666711172 }, { -.37722323216326603, .21025477853123253, .26643937876936885, -.2454009761854803, -.2942473462357076, .6463612731596013, -.3912692410711931, .5505993422465192, -.46129201172660017, .4894331564294865, .48862127233121444, .40682526419523524, .2924221725805647, -.7389455866831514, .5713539889964725, -.9547769164522353 }, { -.16195183520220802, -.5826041133475652, -.6042261754121183, .8968957301692992, -.9731465103453036, .27928067962550474, .4650373908574923, .7512636084902848, -.3737131912098719, -.8380499601315672, .8232794626927038, .3193312790753242, -.07665822535007738, -.0627915407020867, -.22016370828088117, -.19624351786802618 }, { .05552544849656904, .2367094665391667, .14525819224730263, .7145624784242537, -.37249220024700125, .5754838169998295, .6410178317733712, -.4240247345904573, .7643898749677025, .3078535402683864, -.1770747838155109, .8627642501279345, .05893905491775975, -.5456655164680839, -.27117690339267697, -.6193476774501172 }, { -.1270194733196277, -.33988469826064627, -.1527154297005182, -.016205824095355092, .8210237083782681, .2478138065950215, -.5479340210364296, .3502459928145423, -.43233522155451465, -.253377715653224, .7138858130204806, -.6658864092533796, .6256196160217113, .6483481186738846, -.30001169655030324, -.10443332278273121 }, { .4735884354857385, -.8355489823290354, .45539618341682186, -.9072447977403286, .4919474739628489, .243324072140769, -.0278800504425325, -.5251581948153734, -.20914840539682844, .8076161949367455, -.4233260978356772, -.9711908865423045, -.4487342816630808, -.04663984857813297, -.9516037859083655, .8983667696388502 }, { -.5902605748744623, .16493500503039416, -.1644779903264737, -.16887926797330488, -.3153521453678467, .8386106943726013, .4135905556717019, -.9701506591155395, -.062001850891237, .7786825895704417, .7700310159867907, .15479741606221165, .29092200120496736, -.2837017992348141, .4896763824815966, -.5114794006375729 }, { 3.18803649924104, 6.772093558833485, 1.5560443490167384, 5.945643368015414, -1.2199490959312222, 1.9746568739913517, 2.637363297800408, -3.4951340035740794, -6.405140901087655, -2.520466184996545, -4.063320427430324, -6.7593175466707365, -.6994891310076186, -.8030899140469832, 2.693636550287157, .9046079063299204 }, { 4.133778110057183, 5.457626252257208, 2.613591460860193, 5.281506284338, .3252615815100361, .1499415526325227, 4.019047374886367, -.515877388058089, -3.786583584263967, -3.563008829719729, -3.117181268096105, -5.250105349041229, -.920023423676373, -1.1012282135929976, 2.4026420964427646, .5178460677109276 }, { 4.419570363180249, 5.606564924040125, 1.5746257372292554, 4.86987880329711, .3595293342620073, 1.6015671717950155, 2.8606557805871335, -2.092826889206317, -4.317097685534278, -4.254202687009117, -2.7965323920557754, -4.775377464168815, -1.485577488867279, -.2960823944345079, 2.394630569108706, .7265961300034776 }, { 3.646291810125513, 5.472281645648459, 1.8078809194873677, 5.25209794862615, .8551779644383766, .358658812361224, .5159236691723503, -2.275654880720992, -4.370731689804579, -2.882448003708605, -2.996975005810897, -5.4133083899824985, -1.4471887217352135, -2.0345747817905133, 2.617248931886703, .40001406270464057 }, { 3.300170236683322, 4.921851641459254, .2785232193197772, 4.396050608472774, .9496528323106609, 1.4653943131290987, 3.3999979079023395, -2.5502473160174954, -6.346102544919475, -4.697352776355426, -3.551997594180023, -4.749833052153729, -1.1007562817680254, .266045892205445, 3.4051265735958047, 2.5977416662580914 }, { 1.2648378746763527, 6.031834867547865, 3.902747341429086, 3.78895850709311, .8544380122717239, .9268357862050141, 2.6586007648222707, -1.3542489085956702, -4.578814140565739, -2.2030417174615504, -4.3776863613090065, -5.803622757183587, -1.1738278978612284, -1.8547016173400173, 1.1273325682715816, 2.2505648794360904 }, { 1.9056164544616763, 5.136974438592763, 3.045664625806831, 4.5513625404186175, 2.843551040244077, 2.315153966651285, 1.4043151786872148, -1.6933156711436628, -5.171114429381551, -3.8243050008994364, -3.172682466394082, -4.3723236979754505, -.3592195430419315, -2.146070183884097, 2.929887788238343, .4707234260637728 }, { 1.3973340236527534, 5.848442449498831, 4.675718420403451, 3.635965934734964, 1.5621941351004065, .8206520454952525, -.03539777968897707, -2.4365166289488593, -6.424049290828317, -2.899158949265328, -4.120485952540667, -6.032014454161748, -1.5390552215625304, -.34936067686146627, 3.782462692786343, .10653578707819983 }, { 3.991276507802165, 1.932676790986971, -.19331596194004932, 5.688432370977523, -.04315061497499216, .22664612330044398, 1.544556610784901, -.662991608814714, -2.765077593757219, -1.0573371773753157, -3.1804754945326543, -5.915275975455781, 1.15704005538917, -2.0511572860043605, -.12774319522465868, .8555363832996812 }, { 3.48235703701515, 2.949750467721829, -.6915377770682745, 3.3861696790745603, .4852821156094262, -1.0895335264363912, 3.567475116865917, -1.3603765653686233, -2.1834680244510167, -3.193820025272624, -1.3598341325803551, -3.418276498830303, -.1546884951462541, .5855969630527144, -.4792834636080157, .5717406339785697 }, { 3.8411881509455554, 3.6771356610053343, -1.194329997314368, 2.160291632017353, 1.089055525183558, .1916189499955017, 3.238836734851485, -1.900469921713438, -4.149202090959191, -2.7058741378572675, -2.620239940552526, -2.9790912365808815, .028140041403731025, -.37067072829171394, .34416872279107413, .09410197361392202 }, { 3.700973965222009, 3.4280820070507416, 1.4838962086997156, 3.64842805729301, -.5169431019395574, 1.9601996354520912, 2.2636967582435816, -.8003214424058339, -3.9048412117570512, -1.5652711849808008, -3.0149585839228923, -2.8692279438537387, -1.7607427631654984, -.6144591570810827, .7344883647200998, .3025458221295348 }, { 4.027949447792888, 2.415156397255777, 2.976827743315613, 3.204841062673691, .15371792541107662, -.5789239392422155, 1.5388953674690071, -1.0861879900646625, -4.776901593255215, -.6270253197445212, -4.294356318763065, -2.4805066116189205, -.6148586525939626, -1.6349856610375344, -1.2266119425263846, 3.1997806597338534 }, { 1.885783655078926, 1.7980370727752977, 4.456098113175945, 2.2550681918598467, 1.4652297539519241, 2.0412688296137596, .5346496259095598, -1.545919979299225, -1.228428187104485, -1.6521605105230865, -4.421512240672333, -4.798169116876495, -1.0629781792840431, 1.1859518241115405, -1.2704108748461387, .23633438192291303 }, { -.40814223929273524, 3.1398785716644393, 4.190884081516492, 1.8891928582395527, 2.5798840093846502, 2.0979202706583457, 1.310737086522932, -.847839854316022, -4.354726922007525, -2.2725886599215204, -4.545901943503678, -2.340654258817371, -1.6054780405327749, .30494120080783776, .5162145363474722, .7892515016685943 }, { .05250548002522977, 1.3820109027331988, 2.3140515125149306, 1.8135611234835058, 3.6535211431562327, .5234157050500149, 1.1915246248440279, -2.9049687686626906, -6.358565771196153, -1.5603918484153663, -3.9034690248704953, -1.133266159974491, -2.1811658332231687, 1.6081005126499694, 3.2416155062910086, .04370158387341181 }, { 4.643770042444792, -.5161281062018244, .07858930944001011, 3.0949240870605994, .18121055424324406, -.6138933958455945, .027637324470353806, -1.3021499291190979, -2.761706824161016, -.3205003423585977, .14734689857389116, -4.433370807703739, .7862648830355404, -2.6972526428617036, 1.7330717654748542, .9230782136943338 }, { 1.7816246186882185, .47999880085875213, -.4390749247986218, 4.697900949877131, -.09346648429017047, -.6885100934763947, 2.5190159993260575, .7664173522566251, -3.2935775231090654, -4.641869590760813, .14525475021133333, -2.3369239528696713, .5141859374157028, .050312088366542454, 2.2262169915591086, 2.226259285946382 }, { -.8532781729180783, 1.4235764484948978, .12445436634159938, 2.05828240194976, -.19408095727217822, -.7582893687743061, 5.2353405588433235, -1.2500385960715152, -1.934470509352963, -1.5838085789267564, -.7008575050671061, -2.1617852696297986, -1.4995350629866617, -.47092165560437965, 2.1651186623657446, 1.173246083124618 }, { 1.7273584587836586, .2520701590977961, -.4577775110898389, -.5737560225665341, .7410662044774634, 2.03296842862465, 2.993660696392684, 1.1452569203746965, -4.125842696689928, -2.427267221837624, -3.7800729465457263, -2.5833398269169146, .6340112179622394, -.33347950288918177, 1.2724407512741134, .21137559499986516 }, { 2.2950578999738442, -2.3981912884204757, 3.267997172145549, .6934277056139555, -.872068150739214, -.4145060568268084, 1.1848075782599414, -1.541065658602823, -3.829483863406335, -1.7979088837617623, -1.9839497993448543, -1.6737430309944896, .24182505670244783, -1.0371540451661454, 2.5702658493554402, -1.0598749496479802 }, { .06693166997807515, 1.8166372834818956, 2.82961229998702, 2.9356487045171553, -.49371355338853185, -.9827927024282342, 2.0551814412816802, .09354421525539806, -.6083612795233903, -1.2383388953414594, -4.323512095405445, -4.846846167351549, -1.2602922091735596, -.08118332428318409, .9692003709570908, 1.1793401613715357 }, { -1.9750589339209526, .44759049743918217, 3.637709416356564, 2.9387771273779277, 1.1813176622862338, .46217756969001006, .7886295873937932, -.3900597742175358, -4.514922776352107, -2.9311132342164212, -1.1417220028223927, -2.021553745316143, .07319322185014127, 1.090299416500702, .4983107714301785, -.5608452730810021 }, { -.3336401461093425, -.3675684176110306, 4.676444373571919, 2.965532917038259, 1.694276761326274, 1.3480721413218593, .3744949853879636, -2.29968436734865, -4.774528859973743, -.05066027164299184, -2.1254959115867753, -1.187750067453932, -1.1578684264341974, 1.0708085962907217, -.13774598363299007, 1.0100024135068215 }, { 2.5990380187722404, -1.2767775816271556, -1.1067348849743084, 2.3131650350821906, .8668977399452377, 2.3373526436219585, -.7510056721045133, -1.9962379686917147, -3.3585628328552426, -2.5484338212164483, .5824475640012614, -1.2142182647439672, .30647110472666267, -.6892781703646876, 3.656349410079783, 1.4241198803597306 }, { .43564784156716985, -.529060481959223, -.05103412954155592, 3.411622001716292, .9314647980670512, 1.285361973643964, 1.5931558835563024, 1.401316601472721, -4.082756094644739, -4.722973325885096, 1.1696657862798563, -1.2666089882683809, 1.361569961410297, -.742998527494385, 4.3502605524376605, 1.7863072827787267 }, { .6253250727942986, -.6929136670371158, -.6265197951937491, 2.0466541988196116, 1.4806965525463334, 1.55216678731115, .6486006173539441, -.9231484851030508, -1.0853602983789148, -2.650185230659987, .6617351084512473, -2.6388626639143227, -.9785695475990857, -.4445020944835533, 2.369748453116739, 2.896570658988661 }, { 1.3176440675178358, -1.7415924506030314, 1.7652247970405697, 2.8941787842592865, -2.0024621541874934, 1.969108085704236, .01955932821099022, .833680672962943, -2.9342388672499173, -1.342223426607349, -1.802286992642409, -1.6359605702065383, -.6650229619698349, -.16468595664239608, 2.732747426002232, -.30312777964007565 }, { .6902739674359628, -.25577099451258173, 1.5597379018525013, 2.996915213641162, -.2945714183897872, .5354839445920075, -1.7674531262394266, -1.2186612102665306, -3.5390852837181654, -1.9200697292885758, -.016021957898775183, -4.756435843653287, .5701211780737211, -1.2903851840144416, 1.4010197768866202, .04125325957731449 }, { -.4187101621256784, .5595968125836664, .6750364874950958, 2.6460735773976465, .20187033795186599, -1.8501779192485075, -2.0294642203541673, 1.165798544497128, -1.3710533941527538, -.8077907972252423, -3.3664656937952224, -3.9665289231177985, -1.168488559087764, .6711377380679711, 3.077586324812466, 3.3493680690900205 }, { -1.0321249335037113, -.44277817031978534, 2.360920993013062, 1.6653504847034881, 1.1403996644191168, -1.1734655802745597, -2.2382979018512557, .3240661969370092, -4.4194859701222144, -1.7309110190521284, 1.2979442645708499, -3.885500889550851, -.33941077300217026, -.20082260689369763, 3.3937021703806765, .17959047997923516 }, { -.648234325490944, -1.190191378215105, 2.738651842251418, 3.4383824287299913, 1.7363817738337146, 2.5059680774597926, -.6287601227769177, .2112625120511357, -3.0309600757689914, .8014563651249861, -2.1066751563876642, -1.5932529608559114, -3.6745908418319524, .7017889331396366, .9596102469999497, -.372363662842378 }, { -.023535422278653004, -.2579414561784363, .13962421477915965, 3.7394038531297706, -1.2773460386015074, -.919278923646359, .17300605836246444, -2.5173813614889995, -2.920709677531649, -3.126987314586177, .6031404389210199, .6512827411407787, -.11573938956200791, -2.889804513242214, 2.668054095122449, 2.078370522180748 }, { .36690594079022043, -2.0577922217835227, -1.6809528079923093, 4.214242724528468, .9401447696684052, .3019628153492461, .6896601102844918, -.8024021859764598, -2.8963601599220268, -4.28772706190458, -1.791752275453963, .21178708105288607, .5232534701852782, .28678730036082783, 2.616855897629164, 2.435072432754851 }, { 1.052292642636038, -.5729534033761117, -.7282342336033871, 2.8425939394506465, -1.4009838698122297, 2.9787688421322676, .2945967323923362, 1.528377938129903, -1.6789780841192303, -3.6202654236593736, -.30320859274650874, -3.551900494042658, 1.341894840562046, -1.1361093233608073, 3.246636577923348, .3402168756828837 }, { 1.6304890290385903, -.926819355795868, -2.34456939639722, 2.892099244979826, -1.1518196387012096, .8115928213529228, -1.8825712385655637, -3.3141767014326406, -3.1635853719196523, -5.03721759092902, -4.335672479816879, 1.73228800760895, -.8093281213880187, .7058070317363945, 2.549556963107563, .6633862134872452 }, { 1.2261955193830014, -1.4050480361760975, -1.0002816256138052, 2.748844570158352, -1.2399480651353028, 1.1362863638317549, -.966147100692702, .32301858249456955, -1.8227619031629583, -1.6965840784282344, -4.823140863351948, -2.4533024963169976, -1.4116857246916616, -.16404592662203654, 1.4242513945760258, 2.7458548510435907 }, { -1.300706490247142, .05268571869185722, -1.6932124344343982, 1.8624200315618056, 1.075498325666411, 3.756683522885989, -.6265458362343037, .8069286950671111, -.059328354111197384, .37821045640389034, -2.869877592082941, -7.101605624073645, -3.599176385581668, 2.490135791177473, 5.397199199572515, 1.339911160463344 }, { -.08816706517241915, -.3766652985414629, -.14687035123198902, 2.1359832840257353, -.4256557696416733, 1.599270485814237, -.2551218408120585, -1.4732339277045237, -2.8713400148945736, -.4655263347959012, 1.2506376235215946, -3.3373344028190277, -2.1115852926382312, -.38465716533871713, 2.607026899223386, 3.7563360957037366 }, { -1.7465563324876157, .10723433156298305, 1.4748313218026952, 3.675255887742228, -1.010163396912234, .13413626119437053, -.6315855588441178, 1.2602955918777998, -2.2528828731334594, .26815247417065413, -.05767779302848931, -2.7286183531506456, -3.2570738961639805, 1.1609981097142208, 3.238780110707248, 2.786064689836381 }, { .8509009703101105, .16339198755138862, .7915535254491348, 3.371365090268509, -.05686138587121045, 2.2304557997936594, -.39526668056829867, -2.021343215122947, -3.5063596742979914, -3.968339680743684, -1.0014124520231444, .9647269617196985, -.6020169336346604, -2.6064109840629994, 1.563515535244368, .2877517077038967 }, { -.3494691360184228, -.22304447076730843, 1.1333183356777334, 4.462201251587095, -.08698936278572586, .8762375606583785, -1.5803242176854704, -2.7040490991527775, -3.5360650873427715, -7.335343533430602, -.29348470149912187, 1.0867284454150814, -1.7948980048379501, -.29094522976870396, 2.0971687936394163, -.06483566645151768 }, { -1.1129251442954604, 1.3780787397913186, 1.8017370196869527, 1.7144327306237024, 1.689476269612922, 1.3573880662964108, -1.4637855142450307, -.5380277182344245, -1.738562666087618, -4.6308200357223255, .47677987639945124, -.9171288047000847, .08646580448794869, -1.3484477472225214, 4.071898855156335, -.5974723600578338 }, { -.8934949289146299, -.07406888123031682, 2.7364133274577043, 3.976128535944897, .3343494723703245, 1.0560020564452468, -.13484140087706037, -.05819389725144909, -1.4431588141087273, -2.4334951093764086, -1.8334505122356097, -1.8194081249201792, -.7501064922095896, -1.3661565960315365, 2.50703280587395, 2.015924239900549 }, { -.9978802330782134, 2.034984375146762, .3944377911321794, 1.7766376687432266, -.06670855220920446, .27413535956157065, -.3225715139953981, -1.3107544719126247, -.8895761673752413, -.4883894368770282, -2.636463180167178, -2.8923891899785037, -1.3066092384314483, -1.250669792280591, 2.2323976273255397, -.025109698160639675 }, { -.2011965431371953, -1.4923405552453868, .7718972314361299, 3.979832035635209, -1.264860022165598, 2.7418189835079922, -1.2205498875880865, .2745358508306691, -1.618964538310202, -.00765510262996303, -2.3913728282241373, -5.730519901215263, -2.6902257771144757, 1.8009198741317245, -.10084012853421004, .8875569257676479 }, { 1.573594229743805, -1.9869742730414395, 1.6643853613787782, 2.5237223931775703, -.5957771413070262, .5654740192112198, -.5543241961656116, -.31941907440861816, -3.0648570487093676, .051885540969065176, .501995516657121, -4.726330907202683, -2.54996008757085, .3778970822035314, 2.1534183285202206, 1.7091593787701511 }, { -1.3628645440247862, -.7067484633625446, 1.571997043716595, 3.7952427261015247, .7053255525234764, .3260475370208116, -.1776573815702206, .21561934062264154, -2.919699985176294, .33793725728708107, -2.1558748722767525, -1.3540299352600353, -3.1665175611691545, -1.2903072219366845, 3.4965661453386647, 1.8927056803771003 }, { .3129279857383722, -.31541427273750133, -.702659293424408, .26522813928957967, .340749191021277, -.7130616551425379, .6367767743284993, -.981216169296167, .7884982046526723, .7901186116030681, .36722989996340827, -.5746294382820956, .698672677852036, .1715343880422533, .14259386035733157, .8145672693816619 }, { -.8861927694029976, .023082161581442717, -.4703343105688029, .8110530631756849, -.8017041212635776, .6997563590988296, .8983408350571669, .6211999302190081, .8206534577410018, .9951217571148889, -.5155948736421232, -.7572566077708383, .084786745818066, .8208629119606714, -.003948287515333382, .9784959241414357 }, { -.27298488084243666, .45333823943607277, -.007375721945715119, -.9052912640060218, -.6150406297924707, .7636104458809534, .7868992507831045, -.19131681803452727, -.06018978701173583, .8932806814457397, -.9879988787076206, -.5583911801391417, .471218997067518, .49539022091150464, .9611069798332512, -.5826031083909802 }, { -.6860159879647116, -.8925288034560797, .15811969260463754, .21410691081865374, -.9826761089804843, .8396891292101225, -.19603813252147329, .7522931090680733, .49596446993200693, .5307861403377847, .6022352716863919, .5906408418347593, -.028485996169287375, .09136297355962553, .39283725447713014, -.6714090331812264 }, { .48811665374385194, -.2899292557467028, .934426792849246, -.5084880318591474, .9844150002239218, -.4416891488983863, -.31159337946690036, .22912639897486398, .9363465564699855, .5701438557765519, .766624712891204, .45675672220349717, .8350928523752528, -.9254950794124444, .9657462895305671, .5185495246721916 }, { .09645010665967013, -.3971866294547015, .08537478260392861, .49699940695595335, .5248911576665236, -.9989310048150102, -.04639860112238581, -.26025833990130454, -.2505084554560122, -.1706207544857301, .1490906994929575, .9131634741837484, .665369071444851, .1715954360144969, -.21667361795225193, -.5185670103785092 }, { -.8139094173785419, -.40720316177471005, .36913427774882024, -.85658218780658, .645048433203216, .2486616759079252, .8361084034153274, -.0464776481100968, -.1649804076433783, -.1921129816721241, -.6090250789442184, -.5548217367142139, -.21540348399187992, .6481148797011889, -.8303447936949824, .4887949373159475 }, { .18751415406101923, -.9342236560410115, .5633501363674949, -.027195334333702137, .13051347984615203, .659368797195045, .550002192633132, .5169948188392495, -.7845479841979708, -.5293095914196386, -.40614283122602424, .17924286659847777, .5744596266778015, .8022268553770877, .6251921715657276, .43697899847250654 }, { -.4471914417620848, .508669612485505, -.05706117297051416, -.7811520477021892, .16996147242020654, -.17615121080039287, -.3611195723395113, -.21604975038249763, -.3730231616820323, -.10839480688201708, .24296098158907964, .6632275019933314, -.43635603210139196, .5321717078066273, -.2432692833014911, -.9187688658909337 }, { -.4866450847875954, -.6066833762958508, -.6155124697457213, -.9830127967963223, -.8851658067591008, .017825032948893593, -.15002254547135774, -.7173094488861838, -.14457535978591984, .023720583306497955, -.5760876729835487, -.3543823502396546, .10161282848573228, -.9799497065541023, -.23478376921714417, .4421482545193516 }, { .8767162143537925, -.4782028510270959, -.8181586860428149, .7144262242398736, -.5163988907403154, .0583534547796265, .1368724526196825, .4924418917331925, .005307542561655998, -.9462990489320307, .5165975479658795, -.5879690587778463, .3195697060265572, -.17530516298353183, .6075533383513616, .3683549949125411 }, { -.7964160640390514, .7314894261138127, -.9054266067957117, .6875909882595221, -.6534129169820952, -.3420378729410263, -.31497540610433594, .46778012316107254, -.46913524456595734, .36641431120842727, .757799219639146E-05, .8725311540347909, .4839634331073217, .3559082987826183, -.4606902269292137, .33122205901297797 }, { -.14320907034082953, -.6770955931301235, -.48153221113642575, -.3498341093852282, .030124744895089428, .08528046638778997, .985250839370607, .4840954107725082, -.22156565628467062, .9517156305373324, -.7297844303030725, -.7055447367420533, -.2029100922820426, .7382874094043452, -.9802476712539316, .15463378011633688 }, { -.4199042384820011, .5460522423098717, -.6729217792425033, .48493257716481075, .3468376722040045, .6776081679316848, -.018800627025546746, .41007652447452747, .8433433512052084, .9557415723005076, -.3201377265379708, -.3457305169223064, .8048620564389581, -.5802370485320503, .6946827008355394, -.08968477636041339 }, { .13960969177434346, .8622036631246128, -.5024824036649165, -.42644552298646055, .1494501848390004, -.7659915187507438, .3439068237522356, .34940997481599334, .4575927808281033, .6397476465512304, -.5371347665674606, -.35630940385910637, -.10570758747917797, .08548344828873944, .18524515102218708, .363410945093245 }, { .8414116744081819, -.9253867806207503, .861165197060612, -.7446262194845268, .6166221080313419, -.45141876696792216, -.9525045069432951, .371012285938334, -.29676295117985596, -.07996185121439447, -.3539887991777382, -.13685725223534062, -.312754402983048, -.8324402210967383, -.009348707336203521, -.4736024496953579 }, { .5417503460082511, -.6995133797010604, -.11579110974659934, .4965082163928982, .21367424537983126, .6372270737177914, .6797733498147436, -.13958093729417298, -.016474660153517062, .5887806456831257, .7012372928605481, -.3577147523749731, -.702691719494458, -.08718730488945559, -.12320451825823509, .9150592792183041 }, { .6378041350795858, -.5634457978382335, -.5825211833312811, -.14091147119229697, .6227614067497818, .6292012151359221, -.4634908289976678, .5065576963226293, -.01795261536446935, -.7759567875452127, -.2445888347563756, .4407936790550231, .5049339525546457, -.9239445897255603, -.4846546655148458, -.17664913193946052 }, { -.2858568354254758, .20380207336577572, .7669978349986506, -.17490828438410166, -.12578955048631535, .4360406109834134, -.040851907241434615, -.9098915628876028, -.27534789845863905, -.2233353284896542, .6140794964800678, .552856173645891, -.15287484937069373, .09068127497498057, -.29281649180845326, -.8690686815517867 }, { .37125036591001326, .12795517663857603, -.3338830490647011, -.546839527930572, .11899629531212752, -.19880077577776234, .6322843900881163, .10412599358137964, .7185289472095902, -.712136929578095, .7220558766226157, -.9339817219307973, .8334520123875984, .5966520390921637, -.8083758222177528, .41077725995871295 }, { -.19430160632515392, -.378135730508272, -.5151484547262042, .7308002136175724, .055353838694881974, .6052459309399534, -.3711241327334487, .8156622251513967, .4815472246633823, -.4020377816102514, .039493373144154686, -.526007043226111, -.8178919180760718, -.8401318405145286, .3780828562918017, -.9876493567629505 }, { -.6227642635122363, -.8020402451475246, .3242233053430823, .1465809173711501, .9437794738789993, .39781318211258054, -.5020433142277247, -.8635106693390626, .7357807411586381, -.48535353967457295, .10069517314997789, .281550380674636, -.8305362039010729, -.736725400442892, -.745921399453167, .954120511424732 }, { .7667618992133187, -.3447199696944432, .568888637997985, .14019127889840077, -.6501474122092452, .5912814436228897, .43363781780513233, .8616449955298575, .8789791263891591, -.3711492870160322, .5690922147271853, .8826469500909324, .9043698002487859, -.2918974866744495, -.84818648675224, .23131277750180357 }, { .12925238771209235, .5192677624748114, .1527995179690398, .5526705539522645, .9801057180852251, -.6817143299314956, .04727081605202299, .3896901384069369, -.6465038975188917, .607115005488063, .7268006626599026, -.8969017171496436, -.21960817841939262, .6548366388418394, .9221455728456682, .528057740410875 }, { .5642212629439352, .410849058485264, -.3497193723598986, -.7731474325517891, -.3558038135295405, .6139866279130235, -.6949856536935506, -.4536177666305399, -.12216073892179313, -.21341721031197447, .2527819164579235, -.7836723024057488, -.7597643621426038, -.1170030871614729, -.33901348720231383, -.6415914255856638 }, { -.8196912059244608, -.47462168666141835, .21866368901022737, .1458586764581038, .4997220479462956, -.6165462979898146, -.6911082080081605, -.1234237525589097, -.33933392362119563, .9953368273895116, -.5893350118413556, -.3547915234228751, .8305597504476321, -.25386942278352786, .044463470341965294, -.8847921272363148 }, { .975376500591419, -.9518246551086467, .24182153339124213, -.6772406451605, -.377653147403912, .40910313104719, .8975271137423966, .36263949831274034, -.7371166303903744, -.5354285739688243, .14695126528492475, .15319907437472846, -.8709073181592477, .28205328619112136, -.32067183710441327, .6158781902754065 }, { -.8326010084001241, .6696838395581513, -.832069633004527, .3061275910648029, .037212937456120976, -.082402146259833, .416276711566151, -.8243736643104733, .26273801006329833, -.3971495164497627, -.7838435884118782, .19004403512206158, -.42133462876738914, -.24192221281244053, .16937132006626632, -.22480607792022034 }, { .9916774560829988, .01658847160452903, .9367540047219156, -.07042398669086491, .502295699869679, .2083683757056447, .28413186480243513, .17295649266886737, -.3471427525802342, -.3996693192526075, -.9120525975549933, -.33538912584918745, .9819618890727233, .5721475873003108, .5828490371350346, -.12515108603377345 }, { .2883166447469787, -.41783416850754884, .4007203111310349, -.9243833748323669, -.6468051053867818, .2265448282297735, .7288159547878659, -.3806495308462978, -.6760535127544511, .4244751094681438, .3350815365101094, -.12488449694366976, -.3443791596651995, -.5846555054794851, .9007462157921775, -.9224541056021911 }, { .9105176917208777, .398919849875373, .3696211546004502, -.07902544158519542, -.43330825331977363, .6147555282734769, .44272935554150195, .6686737450716325, .8770241392451197, -.6854579106332221, .8639096293080342, .8830468489271446, .9617861427361833, -.9166060832427088, -.14079994096695936, .6949881469053005 }, { .21949934034808916, -.30574767599511743, -.30227374279408337, .4224600250794799, .8852088906056306, .6007883630848339, -.0981111021030685, .9981493566238653, -.5931958492404836, .7689563419910548, .001579355737818533, .8471796023739846, -.45224623687341303, -.32187479385772266, .5857901921079947, -.8301668687998891 }, { .08848094838344545, .4180932854606483, .4907153753527216, .2146108634114956, -.9028693498026019, .2963626590028947, -.30359213011768316, -.3687753564099292, -.989107421531559, .6650501455940381, -.6364163085969421, .9375583001920536, -.1647136752387286, .3175646204900202, .8724936594279251, .7106143464950121 }, { .9917242863528961, .4125150986124895, -.007716312165548356, -.8256324322420243, .9608956457729452, .2614836473388167, .575284378884535, -.5219905238804741, -.3709456620439495, -.9910531422338351, -.6780437447394365, -.09811373758942454, .8995829982579173, .9364848845900289, -.5077900800549795, -.6358717542834644 }, { .37585848611678285, .90762583621474, .8301798025410252, .006036299183864946, .45476453143031037, .860618003252773, .4832070072627819, -.1735153387728119, -.27774325010854506, .7756227401506857, .9606888157982896, .8939099466223224, .38423467740039463, -.9175976325834632, .8072142663372821, -.8182372861419684 }, { .645396770759572, .13210447658587476, .10427347304109102, .1608844602169226, .1765589277395685, -.3440555383729902, .32713526322830555, -.7255762087317892, .28826128509737226, .9487889977245889, .153069600440767, -.8946663774853414, -.7262325605900106, .1481106194358306, .8677478251181303, .8169838900807733 }, { .9842804415814426, .20704454761870195, .8518786090118526, -.26141386860501115, .015419868323457653, -.9646515659331638, -.09328013485229492, .16234855284301375, .5759978141983773, .4651888714667807, .5375371670064708, -.2907723677168075, .030891022616530206, .12476593356996091, .9499581347929915, -.5938298625704355 }, { -.480343175748136, .39355024753180556, -.8668097601846625, -.5816927477186331, .03622182886035641, -.858537365180364, .8466453098402491, .2532739225634977, -.06187182190827545, -.5139868963325043, -.07022445124164833, -.499653439583694, -.2821921721103642, .3960960136930207, -.9639864425811175, .7777178224850869 }, { -.7646919632212557, .7087161849180181, .6254970047425477, .7393004270333066, .6229104856682781, -.38513948446357693, -.798444762299868, -.3575934219134469, -.8499526166075826, -.7171211900502501, .5559439018157695, -.4778661454458697, .9234961050185946, -.15377325509829998, -.6634107568140455, -.6589632639314342 }, { -.7073035563499632, -.8181115320585381, -.4043793042055823, -.6474417527864584, .5251164538632744, -.5252904747992517, -.0962524813623098, .9639103531865922, -.5959832871407198, -.18650275369571845, -.19596864118767865, -.2484783612042072, -.9007715887118191, -.7501133780040199, .38642810172688025, -.21472881359738505 }, { -.9390985213762943, .6738825142562475, -.306934867995335, -.4782350169869134, .5098995739447267, .11212985327312497, -.9793510593208943, .5921801184802085, .025224781974720667, .050929844911333566, .335635275824522, -.38432227649815043, -.030991471246764712, .8421645812847953, -.8130969245409245, -.6768414144008317 }, { .6960235528949077, .5821712617035923, -.8517128449866396, -.5070724341748558, -.7742896805625765, .9268756268225009, .25247377713959973, .2286607748185101, -.5037891129529033, -.8397755762690198, -.908361733543583, -.015025211567571306, .11031268957408513, .36759260701543717, .6783445391009515, -.699429976265262 }, { .29461769599437515, -.30439034993769987, -.26680588460843824, .5485446346744123, .7495544974803623, .058432113292775645, -.038191073096806916, -.5065612608375405, .3857258345955814, -.42552436888926737, -.15179985677487862, .26155434849550985, -.7857294544695912, -.8569023824624831, -.6101665163721397, .5118974137755745 }, { -.9926945490392829, -.5498795526659397, -.04002423479643613, .9750035702974591, -.19944009660197204, .5100982856831726, .8210240633645636, .212040107167474, .42474814325472066, -.875472506204173, .5567426250936092, -.8435021248412857, -.6419257052314788, .33783018190690006, -.7080544326852638, -.08340730888390824 }, { .5618589309252213, -.9076424667376526, -.542268215158549, -.33911400573184136, .03491996074151871, -.20897660078539637, .062101856502573494, .9662186430875339, .47077715447329793, .7069350726583392, -.5793628840535292, .22652505100722298, .2679714848106838, .008944988466495829, .032814631926003646, -.9442953379757704 }, { .9199580136540331, -.13356220676822828, -.4463077480563655, -.2929053228293699, -.9341551755039437, -.7631086229690989, .5683467356334386, -.2776512494970962, -.6254194740366503, .2679974327449832, -.45364709998495045, -.13649538570067388, .8978596517354009, -.31658414938618185, -.14434468146596302, -.8927508029704483 }, { .27135242071298094, -.2478925527324185, -.3634208589728394, -.798500359145391, -.584900759764055, .02126976508533951, .9143788138071502, -.9858719938223712, -.4726843438324264, .18367783593208142, .7794157711471901, .6644741060656354, -.44817174050126085, .8941144812323456, .9433719310564619, -.710069839463426 }, { -.5097221758829831, -.25685093325909936, .32127152462470043, -.16556331951555237, .8118738077799701, .46314841675125007, .6301784619473605, -.5936715150200855, .2525921131990192, .10280413437919877, -.14825678581032076, .44228974999289106, -.6868627246153765, -.42447919583251137, -.022571994038408594, -.5696034257565388 }, { -.4652797866065925, .011264525633486233, -.5653830804224205, -.03660574723565291, .9682365869117824, .7456186549357346, -.9687791377501052, -.2256755845569467, .2658604125611226, -.9385606475102741, -.7722220736654812, .5297565566063989, .481241174066239, .6814521915775973, .27664944329728103, .04189867495608057 }, { -.4456553511067334, .04066736841520924, -.21677085882868652, -.6828061344241758, .20902674820430467, .20681532210663556, .4496001028012606, -.0646955978574173, .6074638582097065, -.49690111949594895, -.18626361073307107, .16716014659753098, -.13436350845903955, -.047233686382775364, -.6254223601934161, -.8474804862473717 }, { -.14974667961265076, .3553708106623341, -.8476319453994074, -.189501949312886, -.4633468834376473, -.0047122528755561, -.049208962472957696, .08038372186747123, .6356374644581388, -.3260204340851056, -.1932115684255229, -.01508965937083162, -.020237071865250922, -.8382512373651525, -.04926367609544591, .23211856390925356 }, { .45783607998065223, -.010959055821659236, -.6929859351352443, .885763774869416, .7507447138814083, -.4966953226370925, .5361746456553236, -.04449256471900287, .5809654665516013, -.3964660400132749, -.7224719592959215, .4348022739427584, .2818882256377033, -.07847698944016424, .8202029866324332, -.644247811136375 }, { .7465037780331538, -.623402021375761, -.8128267034606458, -.647598654973998, .07032936610615259, .2951037968286512, .8160111954959317, -.17905826418708215, .48188831152261, .7841032982932474, .9648451718566047, .06116406191180501, .7697103685593836, -.7584318739044913, .9687295074564966, .13930180682137427 }, { .4351291037589258, .1758029626422919, -.6873110671839426, .04778409516397053, -.7628253656569042, .8038040949907095, -.8536772497832115, .9562597091492318, .2635165009001519, -.09578914046623321, -.9528551159905636, -.8556486287885587, .729473217013588, -.6658045554707657, -.772402722043231, .9836742570199624 }, { .40294933026297075, -.1585931524819446, .3872287162200698, -.4411652675412754, .38953251202887773, .3202287403820494, .8552103839324652, .3032394351389269, .3321062428406918, .31156639855995905, -.4111595012604117, -.5663191895643045, -.5355014372468636, .9872669225389687, -.02102413615630505, -.6679151700964858 }, { -.7512442909400687, .9579084071056028, -.9631832089097665, -.5458565454697846, .19544706054271876, -.2646706742484084, .8592063014349849, -.005059088060173966, .26084339746936447, .6367092954282165, .5839831990206008, .10862651551847713, -.936832863177028, -.809824716740964, .19032991832424617, .80217308491099 }, { .028051495192425113, .3371827349248677, .1414124352088264, .7679518412578019, .8798869620637366, -.3809106214374345, -.5385929036805508, .7133585216221003, -.8614259484848477, .893625380616258, .4355094691996293, .7496430084905372, -.44372727238538, -.5539064951843917, -.32381568618893386, -.26794208772166095 }, { .6578140242399992, .015020615425517025, .23016329430349058, .62507728696842, -.9937948991075245, .9985433394458811, .588876813412327, .7745632349175091, .7812666913288864, .1426524893613148, -.5258611613221156, -.7155389111649835, -.7600863084609064, .622761118206163, .7297099632924025, -.41230359194080535 }, { -.1635267864022678, .7471123703206515, -.4687182693171623, .08695566755662232, -.10340755793818124, .7041445898905774, .1884328515738154, .16080358368339476, -.6603499052794872, .8103641536632806, .0025147855760336846, .23003263489356596, -.5562810075035471, .3145612077513471, -.8644930848756913, -.30720773124726297 }, { -.30856277798552156, .0022525129061072846, -.25738535553096775, -.230346652753324, -.7919804515321929, -.24044139765208028, -.07955578075290726, .46024693135316475, -.6689660909831971, -.6864308093979632, -.22055221612051468, -.023322954542932317, -.6602648590350746, .1811017403420463, .03603153867076392, -.0015301204208575392 }, { -.7945218355039652, .5833194791618337, .5429573894003046, .8633079073162608, .3744405822773069, -.37584613844830295, -.2915650694334906, -.22842777327264185, -.0136636875922993, .25504571011718946, -.4672016125238643, .9859901320256761, .8630407343028514, -.5070193094295137, .9249795265637231, -.3999030096939784 }, { -.8334386538030818, .2567917966917277, -.06611004295872847, .8259027419668783, -.897756884001355, .7576142284765888, -.8610828724989961, .0759217272339816, -.08476427525542096, .5259375841518941, -.8990697731175998, .16485579312480536, .5549859670855948, -.5961681297615775, .2645241200514665, .4885091965406281 }, { -.5820760894667574, .5340751690422432, -.6155355358525973, .013402256417514247, .3920409131021598, .6465627379809449, -.8219990639689767, .2648609337726642, .9698338997710785, -.10851089976458805, -.3401744187804585, -.9591628910340344, -.9764272895429356, .7481668642014914, .8400809437418428, .819206052070848 }, { -.4482695416134672, .6821482451940126, .4166071221642871, .6786950852526901, .05210063693380418, -.40121668347693595, -.5947224110420588, -.6674552471350401, .8623762714871792, -.9339756589759936, .10932202478925124, .8774403727877431, .502154095595222, -.1664028281720149, .13551959234082922, -.29655041202645305 }, { .6327681614713512, .720043413413465, -.8891642343282107, .2935429282534132, -.6847032771042691, -.5338493986442938, .5847020736310728, .31611760661277777, -.9118828186851304, -.3202687327602123, -.16547477098388086, -.13436370290227306, -.6761369172592664, .6459159297089836, -.6669903804444999, -.903324484385243 }, { .24443388946171996, -.7287679595344927, .15657867409811943, -.7442808892414992, .5747770805994954, -.5886393533419367, -.9249368422152842, -.6902958195014384, -.9982016664272906, .541600535491751, -.5332088144292197, .05492088310596044, .5778141944554267, -.7031049640573872, -.15234753795543643, -.5898430894856961 }, { -.34379011361447476, -.6587364734835612, -.8508762473734826, -.04543375720420739, -.06291945701685386, -.8850799151235507, -.7145718873925317, -.6995266912949474, -.7144110787284159, .27613072281505446, .12466465537113192, -.20106935994356823, -.0044748566208245855, -.5702040041148286, -.7044445756897828, -.5243775117249372 }, { -.8344482064061192, -.8006283257629614, .4095013826718985, .8138255449326794, -.38909730532772513, -.5038947228288568, .5243091832676183, .5523794142617608, -.09244990288316535, .4130395370038551, .5740973816072341, .3392271987204458, .4758052531376056, .11564544630166274, -.46986409862411205, -.22221296684792402 }, { .6749100439780957, -.6100389996680955, -.38547862648688924, -.8012455929487574, -.7166429577375695, .5430518087935667, -.3156972207696229, .01416768006865321, .7434354155439977, -.18460329910141748, -.11933570409655081, .8115900441019441, -.35965427486176016, .24067342615007603, .4873517437509842, -.3768002816536411 }, { .0744955468133306, .7568335538094557, .27424060681031004, -.4373368378516653, -.8341967340111986, .6899497010462032, -.8065403603039671, .6368469759086337, -.3247378961471086, -.9610436563666804, .104878813647725, .792552225524574, -.24043915954530082, .3076739158139159, .51053514100154, .33435611541346266 }, { .6848690357390403, -.7068969675640062, .10944044222736182, .17821848866944245, -.5590222272054248, -.14530689471855385, -.6257929496110513, .6405335843960263, -.5790288505612822, -.3642134106350621, -.392853473421088, -.7675919013550612, -.7830745394131369, .5365214598728154, .6260115620155151, .6928749305199862 }, { -.6446521495638557, -.7630826805008215, .5714497325257326, -.958549216012657, .3917267883739013, .34090577837921643, -.8455646461281041, -.23535284532657474, .7704770957265437, -.19707812145797243, .24478656451051872, .25592056118114814, .3617441057496009, .25577800295670805, .23992788142739996, .11230649389838421 }, { -.5732689985052717, -.6243906175540279, .41012476788608354, .9749702528855986, .20300521493260026, .1312847104945143, -.7480769578490716, -.06032102468290623, -.4201135857027751, .492903683992292, .9864888872192199, .4312526809926358, -.751542590375756, -.41150026067571455, .9753116977084659, .08972319975976628 }, { .32841060586821547, -.9985481764747155, -.032799439830265564, .6075893636099112, .4172787570990282, .9730968771324788, .9200456808089561, .1729537686122986, -.5914201640579382, -.6091396093851116, .7347681616779169, .92011465746767, .1884645430762133, -.39927742079773343, -.22164370259542698, -.22793839010981376 }, { -.4687312035080955, -.7804553261103131, -.5220907207767358, -.7082068358737694, -.4466225089118905, -.5559651203982336, .36887476459745794, .5208665171754021, -.9188153530226366, .7497798603145129, -.14295108470317985, -.35674467947219934, .29674750318845544, -.7349038213612542, .5128265884452126, -.7974551079411918 }, { -.1998948848748141, -.8581703926028748, .8347109565132815, -.6272313532105958, .6763174262448286, .7766985367430264, -.021616402442513394, .21092992168936942, .7798523022794386, -.5759559255472664, -.29210930968836, .7162212304677167, .00568763161828878, -.14922136623556725, -.49910546317780935, .8747084554684179 }, { .30626266424401005, .42626208383102293, .20629106504920403, -.24240084922625194, .4713255334898008, .2792069313556276, .5576812463350935, -.5553200730103551, -.5444938736483764, .9853150429153714, .08192527765615498, .4199312539275952, -.003984808857400157, -.46863823874520705, -.7798983613784558, .2703758422157849 }, { .8300514218784492, .7244253020731983, -.054807289657403446, -.014322147011387454, .42676694408286653, .7746125192061821, -.8959430389329892, -.6668988542288221, .9086824651713019, -.7639424345633776, -.7101363850915738, .25919157037393137, -.34257452701386515, -.9207881511257672, .6427085568898601, .3264239218783729 }, { .7535958485282495, -.07207973999244732, -.2164932485239459, .5218621627969682, .37963252700715433, -.9519537582972919, .9874338995765513, -.2144857335382886, .5616380276254946, .7821404348848189, -.33241789061684646, .9023709118591314, .25543378378273585, .7766665474348866, -.2451478751968681, -.17365015439931009 }, { -.8557735074208437, -.0142724610864422, .7351437071688649, -.5615146791460255, .497185728740668, -.8355903518350662, .33997116799833327, -.1575842773385232, .5479162577762369, .16730000182280347, -.8190321296316923, -.3288820059994282, -.8553222812449128, .34796310100599537, .079807713744523, -.26329922209151135 }, { -.06666832018566682, -.83795937841382, .4134816825897185, .2165929876214998, .4154115642604923, -.4963596360985658, .5094037529696043, -.1572063370570127, -.44457648691603446, -.30802293486288024, -.09449008222236666, .09704593373573167, .2079665167949638, -.4900722019654822, .32723983243714816, .8435578056002251 }, { .600683047400687, -.5259474143886425, -.40791744406273467, .46216356994165575, -.17399212117787033, -.29532112108092146, .6198889838191981, .2240278147692505, .40555082660817665, -.6048528249455267, -.44892975814515435, -.03176051087730647, .29144794719977063, .8488163987728896, -.47946846394403186, -.47224927575670117 }, { -.7868074305380399, .394181187702237, .02356105747936499, .10297328703173236, .8453763942385939, .676667392216336, -.7846085677078676, .8741036418516246, .1603941221200278, .7802246692950936, -.17118790792393002, -.7356532189861051, .9785436613974567, -.12234972452181281, -.04861836995328361, .21194505912787354 }, { -.6311200421665903, -.23813325267106222, .4930698454759139, .39710217057044206, -.16361733049473082, -.12071816819126502, .3252592150289595, -.1026229357293158, -.6331214037484842, -.8347418461154441, .9380746108511908, -.10868978801852336, -.9136975013323214, .7157396496840052, -.38404281190372513, .7519025224707727 }, { .18619946641594387, .24750965362594246, -.5934191091087484, .8790500837067958, -.22462683811253514, -.06055085463035548, .10620627667769011, .3402498100783322, .8603882537906826, .9721337507538745, .6021540860243499, .7766335436795482, -.34187986236325085, .4672490872191155, -.42291618489211724, -.7440164792348016 }, { .14445728186743834, .7169765253703888, -.9007780128081639, .08089177695447591, .22959907057764695, .14638398323608004, -.8037906170087881, .020629319686520153, .038658536390978826, .1649378404597832, .6247072485868743, -.19191874448653423, .594871278653369, -.40965323335463366, -.5248673821043883, .5126005183980404 }, { .2588139320040741, -.9649124258390198, -.6971914792397491, -.8610233141617463, -.8960045345519885, .20493064594682076, .7137857743599982, -.8265278114616827, -.9954069237333114, .1569026396671287, .36794870744810826, .6949343732731024, .5813652964067013, -.9969170062335659, .5521510136387746, .004487692655597231 }, { -.8382102265795748, .8880173997822323, .6806405474462496, -.3014912341849818, .658665697762405, .5004166553419678, .7831334254379532, .6764139750247038, -.08970227200459813, .8152763564602921, .7412254199012642, .3862769621747193, .24092291672357224, -.3971199210455596, .3066004607393642, -.04963202232007946 }, { -.9706828983169371, -.9364905316432615, -.5389826651071741, -.3501468943526913, .8738307500601445, .40455342115520154, -.08729735228914359, -.4705122739361165, .573812327801734, -.7803463647005988, -.6816833111393685, .21288937906838368, -.7890251378500295, -.6391905143305778, -.6402155232201099, .6470106750210411 }, { -.3996027471807959, -.41754592043935235, .8535428189805947, .3982633856122735, .4762031590760125, .2741075645493045, .7864063980067599, -.06434815099212265, .8917641715263598, -.376080791783697, -.8476074030615586, .45051770855963524, .36016537572279095, .27542313144247976, -.4519029664795007, .44699840657235024 }, { .08741581194062453, -.47225963939233706, -.8486038307992068, -.8588566977810761, -.06428342617191785, .7678565144872842, .5262081853359102, -.804937802866619, .09346746215641732, .2959571513463377, -.6212002098875331, .8481477056452493, .8871819404540675, -.32723057509276354, .21086785890459758, .5841133008413131 }, { .12241610686925153, .20903958735273176, -.7995499082302346, .1985269192319601, -.7077315230055925, -.35756880974207017, -.03344179832140681, .7987213726948363, -.13323088168608566, -.22021190190382955, -.9375925300328019, -.8920482348875005, .39601266536368573, -.261586173668259, -.8616385498195203, -.2765845443483417 }, { .894550017466776, .9696796529629896, -.5352918113132488, -.3468490822526169, -.8098530508389434, -.382774390684554, .06730579288333516, .05263301059131065, -.07334202801208689, .6740979411277837, .9552161465100266, -.662446063215169, -.410180267194985, -.8819828540436292, .943457726954845, -.0370197598890214 }, { .15202017944436474, -.6535848266471975, -.2683965515268647, .9862662733932692, -.9814476949236981, -.8566434264700218, -.33077648006048044, -.2234951364354829, -.26872732915115516, .7601703320767077, -.9662439275792285, -.9220819856628191, .8597804004167204, -.5218960017416183, -.6479042106856101, -.8049715195380205 }, { .5178174292030056, -.012534635272315464, -.8886617358980815, .9178459975445301, -.2919558011294068, -.6382508082367755, -.33590419509800107, .21038053125258283, .859555491120854, .8288705308522162, .7934147027380014, -.6854608344069915, .13776160704058227, .35533918947663046, -.5274166753591918, -.2384480530228832 }, { -.5472047457590898, .8513280675091008, -.02833530795363748, .7345159333599622, .8116128899144266, .7005250593138275, .3797674660468964, .1635410782419473, .7424535284605989, .03069579369924491, .26790035428607917, .3255619659099529, -.3031585472891478, .021432815721916132, .534683016015076, .323747352725116 }, { .907188585815274, -.7111192994406514, -.8715291619524128, .06944903105267586, -.8445198476796376, -.5793990371234126, -.5800512855704829, -.720855526722499, -.9121525907698431, .23988885472564725, -.8048889919517086, -.34402853674996536, .9202017278856056, -.5017661287941513, .9501297423254647, -.2735607516335865 }, { -.9623005913474978, -.8149882517352722, -.07197364557671992, -.7594226195480933, -.5879155025645182, -.8729503349963952, .15548271041570216, -.29205530234983557, -.6527868304406326, -.3135989276603641, .05145712876203623, -.7989759566922412, -.4473634308427461, -.884721995125773, .4585658799040897, -.6949813556129514 }, { -.7255324027544543, .6798088658425732, -.5804763779945781, -.4221109150222284, .38408115632327977, -.4092803205450195, -.8512934951205144, -.1511364249283058, .04883337742812244, .6192830223173991, -.8969458926403302, .8000147017404005, .37311500234315953, -.21144639800360343, -.9751853033055802, -.2087608133697887 }, { .8689073761995865, -.23567409962179098, .1287948690767804, .20358402845633816, -.24313583275798334, .06288282710606552, -.48039424655788343, -.9531645274753202, -.18969134760979856, -.35674594131528203, -.6842998348318852, .5037495968313308, -.9227284121050774, .4712681261556133, -.4285154236311033, -.08024259700575298 }, { .6103910922160558, .8921443689582984, .7639914345001895, -.14687843362736452, .21473684561531403, -.9201923662408815, -.8220087541330499, .012862058344776495, .958668730265984, -.9837238793383722, -.9663967215233895, -.4414529254680246, -.2845551759052576, .6347664579642547, .9382832613163745, -.6507227721457765 }, { -.059550915487031064, -.22060624310921817, -.7990715898342631, -.47855156561666745, -.6989858839367895, .33521129861054444, .6131456164928568, .8189982155816453, .921655513854239, -.5756496208536561, -.38295712178504493, -.6042527733850487, .3323215217829656, .36234858345255483, -.29887705534371567, -.3538202944390574 }, { -.9992848447443945, -.8164035301914547, .16498683661112867, -.8545627495774626, .9239177105329566, .36136919738957163, .6124491960276925, -.5877357049273715, .8205246179338415, -.5567489672958166, .5057458406718782, .3455099343894945, .40697334641891825, -.8140164235950473, .0992667991532894, .15216198066421738 }, { .2878926770819321, -.7197516870158767, -.917286243192942, -.30692218121633674, -.6025731508891736, .566099602869933, .39223619779861707, -.21119451129466071, -.43212437824462446, -.4041868455708073, .18245942533770587, -.8882730393595022, -.471559720215476, -.34115576904909206, .2020008668352966, -.9782191504832294 }, { -.706258732698388, .3609082524970655, -.9176115470147572, -.46079194923293887, .15432199265211244, .36086887807902435, -.8366789830630514, -.5958620187352532, -.16027096397119656, -.5910786080466148, -.7866110635174741, .9605323700216948, .4139089004695369, .9613099017549687, .646140197742129, -.7940868802828178 }, { -.27224412232958284, -.14185060554251017, -.2642478482986992, -.9173356584395336, -.552624006856204, -.2612772096507605, -.22344825803745483, -.974289265212501, -.38332827928626223, .05281001099500915, -.02266341429652674, .29552858811093374, -.6421441286739817, .7326316565487132, -.17814399001424674, .44165227775082494 }, { .6682770573341017, -.3510002201343807, .19647409430755536, .15164072144546759, .7333004236518934, -.510530152907827, .7767852508822946, .6891403166196539, .29996318871150396, -.7033021961717663, -.30813781036727184, -.6406649247164753, -.5118482295524642, -.42655424054347857, -.4640883481232716, -.6156578747276928 }, { -.7744470250745905, .7782422172191759, -.9525172543897249, .6138415635854104, .036206000247658965, -.17176752020314612, .759911637293057, .3614841126871646, .5306255735301184, .03301382078800774, .9598013939539274, .429914919755648, .14408065801355763, .42624656928326465, .08407751322354784, .16071550378468635 }, { .6114704405549842, -.875494576636169, -.699112660478493, .562726155285288, .6387713911483219, -.363224924089671, .4247887680165212, -.7193977377134679, .9247025709225662, -.27990700064700613, .5922751759003906, .6911648220776494, .3716714506892398, .8503373953376636, -.7131719214267829, .4266998238583073 }, { .6549476056453358, -.5581912725045155, -.7382802373682269, -.40730563317753066, -.6697795107978117, .25698952297681155, .1817698029295829, -.24315773093270265, .36339559202098437, .741234540459798, .36658458523743787, .9831378844077652, .6320826357346714, -.694009798100194, .6291694113556778, -.15091666298462392 }, { -.6380419393465915, .7586022181511585, .4959222670356447, -.7182655316956632, -.8907924752238185, -.5011048690431026, -.9725949420680506, .820775869851216, -.8331559863381521, -.9499146469886133, -.3085512944669697, .7222924840960796, .07046966295881596, -.8648000840941985, -.4286311858091407, -.9695554646026461 }, { .8852442904293099, .8457434281605374, .45696314592864984, -.3944742297019743, -.26664959766186036, .17532189922985109, .9136268580557008, .9373516792282588, -.4929527518277024, -.6495760020365053, .007034253525845857, .09208816346270399, -.6149789879748533, -.13163927077193094, -.3557172213219346, .08528695235159822 }, { -.23434849660302137, -.6960398991931462, .15100106741464714, .9257898660911787, .744462475852185, -.18556078705337287, .784167325747996, -.11005356827114299, .7248872077874242, -.7494222657669947, -.4429214116202562, .03174860184236916, .10421401487242865, .9816322398797532, -.28984713634915393, -.5390324255509049 }, { .7588576450632047, -.07861910473982836, .6099739481162829, .5379812242477637, -.8627784923824671, -.2188213921202049, .2418120669169901, .4880618969508548, -.09904753720665882, -.009874595928454521, .48781988669593, .4538558157492796, -.29317418457740163, .26877842828711906, .8141140784748084, .9955281103907825 }, { .2071320334075013, -.22894226821476815, .4801045957548016, -.10417974779072225, -.6210803957515363, -.6794385452354774, .9250230274506381, -.8031660366542346, -.839964428198656, .017019653882680785, .9994863497315793, .5389980120019295, .11978173902839329, -.5773391523855416, .4387635936906853, -.9462679124381732 }, { -.059097037413293085, .4922032741553417, -.7167802270959596, -.18333636889665295, .6143253131020583, -.7987107732535599, -.5807529066465016, -.7594188337064978, -.9826802507499841, .6800747290089575, -.7492428430317217, .664043436203912, .9030370811637018, -.49895829359826926, .91180369069446, .07916016641518175 }, { -.3440723988008141, .2652595151596573, .6382693353412889, -.02936824471492172, -.5072391201168678, .5911988959504013, -.07894936424986088, -.5984948646528208, -.6887185498511839, -.5665035034651247, .9310938729533345, -.8699636541244569, .13491593219624076, .5736732131389324, .5083954158816766, -.33481989494517284 }, { -.9337022239941746, -.4260310672694043, .5483241606215137, .4814188602930085, -.6618176906305446, -.6568482712097852, -.2260439656937394, -.5423586609028934, .3564672503614128, -.398497919705078, .6641938892270258, -.8991924524897885, -.508992235125969, .7853338701292027, -.654142505746568, .8679531328118202 }, { -.5546137639234641, .8918987447681412, -.66257604718111, -.5984023973438954, -.43322878075780147, -.3176860530036456, .8545262783096881, .3352659075326383, -.027706768054662767, .09232378796202667, -.605799348341219, .8831462648036847, -.6006341807567257, -.6888986011982172, -.618728610832157, .6734656052324826 }, { .09403090737568776, -.916991900988058, .9587536593137094, .9560541128903244, -.8507659425710044, -.006286902483583434, -.08071143975525441, -.8249937447601474, .22413796588336554, -.7182576332588961, -.5331969601985893, -.6550323290007722, .22953724683628818, .4724718990332941, -.7493684783878503, -.567856905951253 }, { -.8717944838836906, .8831686642590071, .20303734750598834, -.1401046725618622, .7288344180860709, .7583696310428489, -.7020971382537338, -.7147023256735139, .06781976672058332, -.6594439378961814, -.7830198595428082, -.5201605813459866, -.0678346137423369, .46114602556283923, -.8027626985435659, -.618798287951531 }, { -.8272030914259072, -.06343891227665943, .836225124248376, -.029243001683003422, -.5384151439066087, .057318103094790906, -.2135169312081615, .21813790041529457, .1556911938616845, -.035230670281570786, .41026862027006583, .7477896133783024, -.39092535102858683, .5655578148088842, -.8761248019630739, -.9302437234302907 }, { -.6544788786530282, -.31853451971023916, -.6188554080860784, -.9240140384819828, -.8251819884077236, .03989013480426418, -.2718971903834757, -.17058850872612585, -.23067701729165768, -.23150176963800795, -.21389159333790397, .12398443709803919, -.6065737546098022, .8524463373974249, .9595495247812353, .8713070311476871 }, { .6757735504448328, -.596417347143662, -.7994929126840324, -.214203285006493, -.9711385440771105, .6966144015287206, .9020312132948425, -.5612715502661736, .41354782196317563, -.6331533361033292, .9111631314066537, .37259986356009067, -.043163161782646675, -.9398688185696231, .09181185018481841, .41004204284081713 }, { -.42813321699636897, -.33286560769667983, .5471150066383665, -.7366480307255077, .8928822589953771, .4580348135344101, -.9168109245463618, -.5328786989012007, .19629587362000067, .8434817465693887, .7689416426442526, .5926951871334563, .5249093193308105, -.9650552811958997, -.4373325714911951, -.6823140505656584 }, { -.5369422442664142, .3047038049107649, -.407134814763658, .6970713294038868, .07024134884264721, .2876921607952203, -.6975015002474785, -.3019073646277386, .7681535541980462, -.9834285562490861, .9657180713234719, .9567962477113223, -.04252746146076336, -.31111243561233226, .8912365942458158, .22938236153510516 }, { -.6213981571287324, .41623100112293754, .2110415429167345, -.41995541466149566, .9219495286960113, .8197640262458288, .39776377011091757, -.7629222950160339, .19566600123191158, -.18451516923470246, .8615461944352283, -.6786387088456209, .45179613403201047, -.6455509224130234, .9937405182447892, .4294717363775278 }, { .7022584363057747, -.0036696648324034964, -.4777353802279274, .8153918706370071, .483059762278476, .8216191643996349, -.22968834701524776, .24793343237529686, .7832700031089288, .2158118300522316, -.018549082244557535, .9492164273285679, -.5274588484209921, -.6115977088948137, -.5479326267138573, .10427046127647399 }, { -.9653714221163674, .40825791161142977, .8681620990811576, .3404448073381523, .9816976197298428, -.9957693046765048, -.13765955697888077, .9272082106666151, -.7432776280300468, -.5531119851591433, -.07096470259576448, .5960405783128586, .5462644360721272, .04826583310076282, .43554119114784196, .6978186529234531 }, { -.2576752637376061, -.9562716022368558, .869719559792093, -.7578267099112248, -.029927526275557392, -.7779692181110673, .738018746299576, -.408801115017404, .3539485527724946, -.5746079491590961, -.33279978637720653, .12467829530282493, -.5046633299961216, -.9708754369950274, .08005862099204153, .31303522977951026 }, { .8918491759530247, .6272819347943215, -.5054222188744855, -.8685323784799515, -.20219009649231579, .4577318663290475, -.7948658540550302, .37970348951008726, .044817879350845136, -.9101011687299352, .763091440355639, -.9745343980392025, -.4694779537006415, .17241915726960788, -.8856900861268597, -.9450633443582905 }, { -.37972883697940674, -.9082960281801444, .9434513010906873, -.4413261041471712, -.659412498832934, .3983856534305139, .9065724701001048, .4953968279602543, .38125831072552696, -.43112881840644834, -.6526948244009623, -.9076710029720467, -.11543400761147371, -.852590349534081, -.8021828685078216, .9697446805432137 }, { -.8226915689125189, .8755291917026782, -.7617212622622496, -.41289333297572806, .19250111741182696, .146157583830816, -.3760682968377884, .6138636792854777, -.23893789876249105, .25641895537378256, .24605167564846697, -.834257991553299, -.532835894388576, -.7691806027862043, .6824853115442919, -.33864516470710226 }, { -.008749221947563557, -.6191388511375573, .713631665331075, .10283838878354357, .3294256068748107, .19442558050546066, -.6682368553415114, .6488419690375375, -.6883808293651092, -.13808901514187188, .3484065616801124, .8126698964381016, .9635519962151928, .11348259578503384, -.6454764106056929, .04829883703895477 }, { .3854704943808993, .41292085965119396, .4554227614997728, .5581323179169557, -.05553877218962211, .8287450102551612, .4786065687606733, .11832473373304686, .8792159771621499, .9305703194767747, .5320282322961325, .49357777297572736, -.5196868895243953, .490862581044313, -.43784316206587737, -.8160008951239239 }, { -.2505269951010298, .8289652638662777, .3433656376117742, -.9796576228446217, .4279616291529338, -.6611229308261644, -.0685996696691149, -.546529424286915, .6068903720128889, -.8572534236129874, .896316821981382, -.1518892136552341, .13136116227122718, -.6947378842377652, .2927560495782724, .7442002543936801 }, { .9354614316817258, .5487106213466266, .8237362340451175, .5202549232124412, -.5537475120648869, -.8549771134128363, -.9165722544604298, -.1579535327741428, .9715669740636761, .35277793912773503, -.6318994601320145, -.07694817482601701, -.1485747491975693, .7477208473993611, -.21602656430127998, .5890415753818907 }, { .6667087836302561, .13466397679262476, -.9013844634652934, .3164152289573101, -.7605132537181243, -.11676841000337501, .9493804758302229, .6798585532498709, -.7586335797362715, .12223079171655171, .9878534033280508, .22094753209446893, -.7678613033992117, .7174703387367543, -.6168555184200368, -.4184377583165262 }, { .22999510396952738, .6872866484378088, -.9873335188585319, .8899550460219425, -.8873475536346433, .9069774567942102, .5004003777685797, -.49026300575033477, -.42606066214298677, .9406031937517423, .6931083079059854, -.16770970810663854, .30925290357274804, .7085868325478564, .44636349037598744, .6009400746001763 }, { -.9677851025464228, -.752318078062413, .8012782952058273, -.9429720277967826, .55877037779128, -.4667012548971998, .49151585040326395, .9926685504787292, .24952984025486513, -.9454778815023661, .2855726674345931, .08250488424251334, .23331340165635717, -.7011865933564576, .1500573396731757, .39778198260285813 }, { -.7810349692847853, -.6779946714655187, -.7960142029424631, -.6750427862259563, -.3774241496735722, .1642992291586831, -.5616313133604542, .16762015019915566, .7389298776704436, -.41918715957587827, .7364854202288091, -.6217142143116099, .7650846039063253, -.8859476312595589, -.8567850865492204, -.47617810480455325 }, { -.737604652717937, -.4403508651271779, .5649841602483463, -.44566970810939655, .6679151054070469, .9792926574907939, -.7194371916803202, -.29997304426011984, .7127580156649667, -.8897953824728362, .7433358781679549, .660359278418859, .17997834407017077, .426565192160979, -.8771918302835324, .7767415832999374 }, { -.7349341305701578, -.7738418154735511, .6490896026040609, -.17782377195762256, .3028066530372786, -.4538535779686008, -.38833619660427976, -.6311366198295594, -.1530507600233013, .4064337097189883, .8237779378697094, -.3979074887650975, -.783893788765939, .023815304391302528, -.18311262232628622, -.8508980621773143 }, { -.38517319962324903, .5850536041050685, -.006165755587143051, -.6559287856796652, .29949645294200655, .7436249278104283, -.1969887516047648, -.29758213827136304, .370286574196349, .6336554013488551, .6580172675095486, -.5753781958164867, .37667780052501887, .6509380555061526, .6214215161479186, .5776479409046518 }, { -.03656798130607419, .034269977495283044, -.6555816222794466, -.8536157431410119, -.040463791097629764, .9791966393449709, .8670763883664241, .8310310121597906, .7110300155028793, .773190456737187, .5176300412673134, .7840720283160605, -.09491001868789306, -.15525901033314415, -.042688062156401196, .41424235255799124 }, { .15220988232653299, -.437349336913039, -.6871418795760698, -.921955181412049, .8317820687937327, .4834026818532302, .3920047436130878, -.3413037724912149, -.9210646259808046, .849776181498531, .5154740162345395, -.9963854325247479, .4390456853809461, -.8211604371141983, -.8143435607727303, -.39702751498450173 }, { .7204487873709251, .8297250649175738, -.6042630386318557, -.9608251735814266, .9563315650225492, -.1544641513906131, -.941047778056868, -.6603514635587657, .11203692234502105, .47631048313391866, .30416859547630537, -.9263788292994288, .26309241707413467, .7829084662293784, -.31837891967069365, .1025222681994713 }, { -.563873689919282, .10850253055725823, -.8701456485678118, -.16217995463815105, -.7428323092243081, -.3398900703155374, -.9094817830057533, .5992967091385675, .4760284313016332, .04934360092801171, -.17749277279329445, .24579109203779792, -.7285231938554406, .5854417809364763, .3777806309291052, .5610358766122745 }, { -.6563711905621006, -.6386423380125998, .9400187884645976, .7361284201345004, .7871011135352741, -.7859967097784091, .8245142046724814, .6651141863686321, .2256819012875706, -.27825464242513975, .6401480708998704, -.16817325025330643, -.5651087158788208, -.29032646313369015, .5792417873365567, .04551354996501589 }, { .29451231516711673, -.5219313100659377, -.537139843875865, -.3609921734934949, .6303238389051462, -.362450821683197, -.720402418265643, .34247865263907395, -.8539626821629347, .4909804094903083, -.12277759903123742, -.5090051493299803, -.12548297674857167, .28170979389284456, .7582214826967149, -.9921330441942555 }, { -.3677895370528288, .5812645400787502, .08335190796442626, -.3224055720680745, .18771340322535424, .8702881046371711, -.678638217236923, .6065938568502174, -.5430510055882716, -.032020924969120124, -.4206514784121125, -.14778907244139994, -.5516688018311495, .17344086029871364, .11840148266141393, .6756223861445925 }, { -.3884795065514153, -.5808479384576137, -.505256094494021, -.4346586478666745, -.25207156232100836, .6471834165433004, .896312684688894, -.3804123656912135, .6048162264164274, .06578359337143169, -.8671204052123866, -.12222896342711143, -.7863178587468946, -.31412086968304775, .9344621742578956, -.7671184248752163 }, { -.6510067636590608, .7544649544250925, .37681899079342207, -.36705018721038773, .6634760929002324, .3196269965662375, -.33997215478032916, -.6884940304942828, -.7345937293671385, .20196975043048493, .3230056370836907, .63836235349368, -.736781366040606, .9434985556813325, .7243411712048582, -.8983157253494505 }, { .8419176714571746, .6316925162777314, .9182805443448572, -.2094860105831764, -.6339849421948007, -.7075321740679013, .9403740119895487, .9879707407054816, -.8839645978314004, .789110941573183, .9276418341638224, -.9288170596887857, .3457028333639298, -.2482210412941881, -.6433504606878662, .1995948288245366 }, { -.4160611408338455, .6492732680938909, -.6168830062363522, -.4800326292856938, -.9206007669917649, .964136067633137, .4261672853112177, .9189534433318187, .8535201001693371, -.4013978573637962, -.4050034954680597, -.603975968370503, -.947753522031419, .7603807322830425, -.13951971252980888, -.8886396608913585 }, { .1165842603029843, .5625464320271893, .6961024335351198, -.1377673540843296, .38455804404363825, -.6289727432182415, -.6517864052778322, .8875975950598525, -.6694200605718716, -.038283315301762766, .054429718736984656, .27065026230551603, .7710361205829317, .6255397249297105, .30516826502011707, -.6885342841958886 }, { .2418929227886577, .26238354248671114, -.7422175718943986, -.30567844204086736, -.8581672743459325, .9801518416022197, .05552835758451846, -.6829801897386378, -.9697544080848794, -.023394012796555952, -.19344471241941386, .5602713298202318, .12058627143562939, .3737124414317925, -.2941570045333426, .8198636536266699 }, { .6999967697143556, .8040621482534054, -.4237492633108757, -.690610476994663, -.49480948394912416, -.14210002995887594, -.6995403612141846, .16491558824740649, .1546125419118236, -.9605918009063095, .46548581669837685, -.6387964824088497, .047064248013954924, -.2602304265102151, -.6692636127755118, -.4472642180350834 }, { -.9269539837067367, -.948182774627649, .0904840737244248, -.4515076903336528, -.7621535698123703, -.6645033738120938, -.561353518520959, .7597079590430253, .14279739313031303, -.3827866815946397, -.43625933317147436, -.7873362844384708, -.08025957143206175, -.2034279656967266, .205280711797029, .6698741138644784 }, { -.857681999274462, -.12039740921986897, .032856587006035776, -.4551914985899397, .6528485374545083, -.90825787436902, -.8735053086980957, .06592029624965856, .8290588880647027, .10855316860844932, .4461097693081648, -.9801449692748878, .9898795663410269, .4355265090924805, .4179576210901459, .3058209675291743 }, { .04964857171670567, -.2394756479987319, .7179472963318976, -.03716294197218639, .8752985665921236, .20295699872852113, .5834886698574726, .028105657237354587, .10888811569730872, .590468837645977, .2654020499389509, -.31731879825815645, .7889759067887465, -.29450433986717917, .2906684799800263, .17003911662873583 }, { -.014009771802974136, -.6245231214732676, -.10971769838745815, -.36466858325500695, .3303089366739633, .026330464753901106, -.9651282569557209, .7177775545941201, .4099854688260445, -.37252036503429364, .553224679816458, -.09923963888125154, -.8497453454824311, .46799435223136165, -.5595281454493224, .4022839803738816 }, { -.44377650209025, .9183894432246302, .7758281745079285, -.8960534891330814, -.6955680343717163, .033341104480239636, -.034880545483361836, -.7695674100812013, -.20030411299210704, .3511959250593839, .021119261923073784, -.450338925282044, -.36003124375209694, .972777126221702, -.2429832709830413, -.2266988166234558 }, { .3156342300377233, .0876950762458637, .04228834918111701, .8642645465313599, .5367229504173934, -.7239251899838615, -.3484436752017286, .5877285850316454, .9273693793831772, .32000585502716805, .7624140579763867, .25590124573605055, .01876890714595314, .1874455413100573, -.6035474730042247, .4071759104908663 }, { .20588482953851184, .18496916139205233, -.06266576192953721, .09278444141374065, -.809419878045667, .411432352157, -.5250287722954385, -.6098806976701896, -.4465068382298085, .30053537424296084, -.053759110430996904, .5796421052818508, -.7237871774705991, .05864189496870775, -.21722368831150263, -.035302445597842125 }, { .8370756199523497, .3258133018567675, -.07827778794452978, .5338072266780769, -.6082311422628246, .3686847277736709, -.47872643448903296, -.19997370124740366, .25146666034803555, -.3239941263681374, -.8828008537837657, .64205984924785, .7391810772726561, .41008570096152863, .9923410670305879, .9899534871791125 }, { -.39014336830111973, -.12653148000624204, .7720243161942324, .5188284808046757, -.7795770430638496, -.5389905519987572, .7839908299165792, -.8984134008931195, -.08922742050518706, .4350214652946489, -.24677853937424565, -.23732978561255802, .11272653368951224, -.41773113420660124, .8615761980112573, -.8898299788533317 }, { -.15314341776199325, .8603688321096372, .94335729651156, .15335568234070585, -.7930749333265532, -.44358416163923264, .8559514866260882, .09010428713153118, .20818315181047442, -.220366667924198, .9584974438365206, .4034096790763835, .16193510239239983, .2731602004788598, .1854717825588137, -.519020251619527 }, { .43013342169726276, .12835328296038573, .0869533159419873, -.47770367198886365, .5157667378738531, -.37236918788362394, .692536495323268, -.4814055763121421, .9737669562992364, -.9247405292650619, .02509669558677663, .34066012094339926, .9829363602873535, .3601927073584754, .11762420815538932, -.3923732837088836 }, { -.16783187912552022, .8638294499795982, -.9245924593351134, .13481296964782397, -.1711325433548565, .24745733643058232, .8531100783251924, -.6580093837005367, -.5356962261843019, -.8182334524942465, .9751175094485076, .890509438128501, .7917335391741136, .20265194445027124, -.5825613205537863, .3695068472144092 }, { .9149404564888326, -.26916324764717015, -.9353158565635888, -.34250897395222957, .0806617753869947, -.39961898533458884, .954570418592048, .4127172740351477, -.5379290230680547, .089663210880784, .8022960877379717, .525640782769645, .9992006845943591, .7431795186436836, -.3162397173071181, .6817268885387766 }, { -.387418426949236, -.10445945789693645, .4290487392234177, .31779499489299834, -.6547555754253547, .5847604682562537, .9722364704762587, .8880808108061622, -.05206293825449593, .23327726253458247, -.6274175028491111, -.9734943313834123, -.9808154036527617, -.4155769915039038, -.10649036608260021, -.04604947808170179 }, { -.6010611086279971, -.0850879524089978, .3251063689298008, -.39748337849411763, -.8379065474411989, -.4177454152392799, -.38442087948971304, .7385608028530202, .25459722000661067, -.19233890333402148, -.7184809064657875, -.10327161307334043, .8937989893430758, .8657714861815673, .11110838660691513, -.7356885565166293 }, { .7065872737076442, .04810403660854856, -.5933219017309508, .2628724843899144, -.8421435310743604, -.7167293765502976, -.8278878785930479, -.9246522809835047, -.27582882343563897, .8644487468502338, .19623521547183964, .9464805536228686, -.03544582687438602, .061593177507120345, .6935987217886364, .18770367560522128 }, { .6188665214326541, .9405722351369459, .4302214116234051, -.9353993903693705, .9357468868669438, .7485875049090267, .11089123256807754, -.7678613264003822, .22721980615913107, -.8187826440408976, .5809593459738829, -.7498975093097289, -.6255708537123486, -.16831167734475994, .813132373695284, .6003553391409835 }, { .7904620278568746, -.5545899297893118, -.2698161576108513, -.16142451029641824, -.2812419905349335, -.10855426278642799, -.6418691529601193, -.023049541758542347, .8962609673305257, -.09566628522371379, -.20505355771789757, .48112556014144636, -.8346537971221755, -.08130118732824232, -.1692245150489302, -.9337822296265408 }, { .9444586110515658, -.2554962746152243, -.22556268365625476, .24874379616545017, .21675606285993076, .622103029832701, -.31489986954010796, -.16488607867624805, .7985498918625678, -.12387059518586296, .6513585461748785, .8058324644912973, .21663696909572372, -.1508029556951338, -.03788877503533339, -.14799667876469624 }, { -.7706830778802198, .48797227172133906, -.5185370037354111, -.04460132953940277, -.526961493402951, -.2814245239814954, -.3908384343686586, -.41265620084674626, .5775105118788308, -.11883857529581143, .40510813281121183, -.6472630414500717, -.9966380804815094, .5367831733051003, -.19438944561689864, -.03541312543235087 }, { .07751460727589476, -.8185337511022766, -.0636952732697571, -.5546449863729823, -.9010446093745879, -.0024767198434341164, -.4464339819031733, -.2851314750238081, .2773498742024325, -.6822553527422057, -.12575775535502243, .8479621766895402, -.22830576557181725, -.4128353099283837, -.17774302038245837, .3453889262614116 }, { -.8492613509587685, .7468989801529877, -.22996717176928838, -.3584414757927452, .3240958182963134, -.8526302971827515, .8892467280751029, .12517915443673422, -.8776109460052288, .8289277173467395, -.4943912907632464, -.528304266897629, .13499163150246996, .9571853239343255, .06863098791111133, -.19118236173350667 }, { -.16995627900411492, -.48450834431611933, .11034772695210826, -.9950539711954407, -.09631577335411534, -.49022881223321546, .19815760336144694, -.8300152067697686, -.04187520822362334, .6567853147507159, -.1528560773871379, -.8719323595067079, -.01850905331782693, -.06639848704690654, .45899056583006415, .48785405385800074 }, { .6374385347292069, -.561805640456071, .2852445430400763, .05425919054400641, -.5630773260178183, .17696292608331676, -.468816044295566, .009979157693366991, .10113143709438721, -.8144824295838062, -.46196334984655296, -.3755436141057018, -.25770726866790317, -.6574404341904079, -.8988207875154233, -.1476321201806987 }, { .9183801100920741, -.5824197004251086, .036446975229374345, .16203756682118997, -.09032889949406386, -.5600969816966586, .3765913845437505, -.6439580485482539, -.9490312273703045, .2994286159971553, .054848587639862556, .1635567014169943, .3593517860961184, .12845977279618603, .18922826324592434, -.8928208957135195 }, { -.7176786348152351, -.35525875459944944, -.9601160127485748, .4415505058260889, -.753593568809152, .050165844289468575, -.1119116160960989, -.8271817175045211, .2627083016905256, -.6905159120084969, .3789530534659711, .17069902186299957, -.44006783973286256, -.7262403359692011, -.9523024164259091, .6315196594696444 }, { -.12354857728438251, .15312541982644357, .09858396142230985, -.9446717825675173, .37560856961886047, -.01651106827467097, -.2852305271880229, -.6265877529053592, .19880462706327484, -.4055819990139935, -.9838337920189522, .7319499354093653, -.5832378511545939, -.42752583584221315, -.9686872615662008, -.07250175476618148 }, { -.71337216234193, -.509942185273289, -.8981657192243055, -.19783649167161377, .7950724814850327, -.9675621596810569, -.6731980576356149, -.6719779182846757, -.5301350734920176, -.08738531907038527, -.9985350578603323, .725842954881714, .5704038292726479, .5390699749249614, .5268691715186087, -.34226711361527906 }, { .5562150491965161, -.632618992997483, -.12472929782402087, .2907853454447895, .9934015124230184, .9356650270432905, .29912771587874354, -.9877992437430867, .5701892369463202, -.7461658654312222, .5405457911072249, -.13035685421471666, -.00977316464372091, .7649480772244661, -.09732245171835974, .7389060508385814 }, { -.9164204511910519, -.07515420227701597, .005739274152755547, .5547153054737453, .4799285649771168, .034306921596640505, .7961394249112603, -.28640540350627286, -.9988119437290603, -.24959763427436643, -.9892507403168673, .6939348282796318, -.633441687394442, -.7403781884954377, .09697177446926974, .08172126322829576 }, { -.22888046483837443, -.32664187750610196, .2089943003148209, .40079856029520156, .6256272929831621, -.8595752394362743, -.27693761560371266, -.26155326213193364, -.19633322271402465, .5445493395986853, .3127849707215504, .0592293861122728, -.4513444708580916, -.3480429483020264, .5650181678143036, -.49413672899882677 }, { -.8191896238403045, .315184495804292, .36050398600051947, -.06218750935293249, .6061710735492791, -.9818146243931001, .6975399613024655, -.264345387897738, -.1618420203569637, -.6593069698699299, .20575347542671363, .8929458565298032, -.27972233974023974, .12787210003573524, -.8402315825093554, .45592599395284217 }, { -.5581038950314154, -.7793974744749175, .8751022383295581, -.5692610599623464, -.288544213409486, -.43452479664286403, .3809381226386028, -.4033149835359364, -.671462904633852, -.09510583340339185, .5836404848592498, .41669613143778617, -.9839101540103916, -.4055101027706547, .27656249597993465, -.8287776715042023 }, { -.2596879011279003, -.9860694166622572, .5026247151304071, -.8962813471615843, .6424253457768208, .941607163188684, .09292330900340096, .812689610494391, -.991095091586524, .763420380496181, -.37675804474224694, -.48906910236067724, -.7711020904956416, -.3482030224580226, .4058820267259391, .1530139216719275 }, { .4214255621885097, -.8184208864754159, -.2529563372856507, .5014316944786628, .4634458144391804, -.5285134045426474, .1685224966365937, .8666066892878284, -.6558919522629747, .4119763131191494, -.7716214427057568, .6069189625628721, -.30379059661417784, .4917259338869435, -.4153099531488984, -.5350434348321862 }, { -.027129398423308526, -.14720928386484888, .7961887099714415, -.12839632218617236, -.5015589354538783, -.7718940594909918, .9630853151960888, .8937270744039709, .9205758190569764, .5707159026368289, .009436958929813333, .39036584120534634, -.6904671255749966, -.042077436156989334, .29218618319395184, .18678755715210493 }, { -.6257554321125105, .26293946210587227, .5265381834562264, .5352129950062021, -.43210265072435083, .4254807235027869, .850665984699182, .18026188697713152, -.5492421162664047, -.14100430152231236, .3936392308147738, .15465498989102344, -.9158128281957967, -.5406158644469949, .4103864238309478, -.9251014242344642 }, { .5845930543459599, -.8781434874389578, .8212698358713733, .7081653593793877, .8388014959654888, -.4100337944680599, -.08342376699311149, -.014391990360083762, .4019459938564438, -.698497099803727, .1932586566604606, -.3710341268555337, -.4733374305436884, .19993395698401906, .1789717510931781, -.46624678636881733 }, { -.3615739506152731, .7296667178591578, .14917774858027855, .10307002756362227, .24115086628529392, -.34212961187094293, -.8984888070850172, .6400844395581036, -.3770815205987983, .5908435950048447, -.9416061112207823, -.11507401423126873, .3917659239203173, -.3018361043751674, .5852876049355209, -.6046682199696034 }, { -.46680873059035943, .7890242023955489, -.05293107084179982, -.8052139721480258, -.3519259689015244, .7442758310917386, -.3896664411401889, -.57109525495872, .3184422466088219, -.8205957415282981, .34184539897840405, .6249826789972996, -.2507400809967175, .2223593768495984, -.812536736710403, -.201024227555461 }, { .12930069655963283, -.39277798352178706, -.2844096874573272, .785937676854453, .03751595394870444, .010071024220639213, .010118780434738461, -.8510745223031002, .45184949941325203, -.5116500970122375, .6535628932848518, -.3673788349560798, -.3332155189481627, .7267034071610146, -.5904756955639348, -.4437752988057291 }, { .24319017934771225, .15084063396656977, -.30191320906031316, -.7492059887553191, .11425356543973142, -.9669650725371333, -.17415949960148858, .4086316047891021, .20530993950918064, .7425636110832643, -.9175866565047015, .7431220026875376, .7898805605712105, -.5815079046208802, .736362664529, -.38064084699313727 }, { .9956416810878819, -.6763872355826441, .5595311598852917, -.8472244159845124, .2852441911195074, .18611093145900992, -.14598343442644568, -.7574251663654685, -.16966135076665578, -.9738209821172605, .1875656677231088, -.5431467932805734, -.3969443458399431, .6137525823472241, .37662122009151133, .5849038606736172 }, { .2999967197867013, -.5739508854067481, -.1620668126918372, -.4435397817922013, -.5450373452469026, -.0011599700548592384, .11487081464895299, -.41722627609772966, .7337318243018212, .5863530187333847, -.0964690552965799, .44850748718914857, .9353878917652088, .379017486075508, -.49931050823963385, .44577154816494247 }, { .7468188840141063, -.43680577261255693, -.9900271044007054, -.5843571924911866, -.20628924252065062, .05180404129277316, .22024131080989107, -.9962486885200883, .3690213086998324, -.3419555522553652, .8662215542188156, .6045984758368463, -.00518022270955143, -.08228176353333816, -.5820716567997828, -.3748746343983351 }, { -.7648718168505737, -.8013258786532069, .6462596551661579, .7031707471169852, -.7928313516770287, .09136838170418349, -.058168764147220386, -.5797932098849141, .1261964979504533, -.5091347116953733, -.9389546254180445, .7477561630209508, .35227026049793153, -.37395105597544576, .10424405448682061, .3722018084865997 }, { .7377507039678215, .8454728551605253, .8409971184683058, -.7694996614048957, -.16895194252743195, -.7456045255011996, -.30573115195606926, .4087661655421797, .5943629436136673, .08562366520172815, .05836631806050918, -.8341574092563895, -.8037933522952503, -.4070609767105182, .8593385462735947, .33701483439210844 }, { -.9480348659506821, .0980187288529164, .787779088217873, -.3583709134535573, .04669437725465175, .5107511107967533, .5319521978202848, .8321977587321603, .6600207124661155, .49775945561978574, .23463826930827003, -.3380846320622972, -.30325430511874574, .33526411517352095, .10903826417564266, -.16489612316233604 }, { .33473945231386604, -.25672651320000894, -.31169200568479805, .1790944549350333, .611085384593627, -.330440731971285, .4630543735929449, -.8537436706724395, -.9895293370701757, .14454252658563194, .24791462437713863, .05006083402137351, -.9990068384584092, -.5390169818179325, -.14019681792005056, .6056461589443745 }, { .5848173430468684, -.9773360948935792, .7799206485191101, -.37161861294392606, -.8516963463607925, -.48320855035180865, .8155211357544803, .24598405357935138, .33590524453026727, -.790937232758955, -.44611974774835894, .7292783987355116, .0923797597023095, .33407616121919337, .5854333191552761, .41655099830467224 }, { -.9485207932063928, -.8467018233300714, .6341283973695184, .10686419797253288, .2645544036124967, -.05212840569537436, -.16600106181523167, .3710096357932604, .43429557219872095, .007251575974945412, -.3083495450247644, .7186845556293766, .8372581136826498, -.8265028080002781, .6866199446363213, .06683039107080702 }, { -.5335916678915509, .4314184975388946, -.6346225555224407, .6140867065173754, .48699360801492864, -.7978342631980515, -.32870832025974606, .09611819811901379, -.12488166424749347, -.2222483322974853, -.18738133203120944, -.682612506789267, .2507914549819319, .21192342460853375, .4347642225288233, .6283534675405107 }, { -.8398269485657792, .6875351382325259, -.4290749827124749, -.9044205031440358, .36239875295109436, .6016851197548831, .41200989766327534, -.6447012629187625, .960842893812897, -.0538316796386773, -.8474719815218554, -.2471402387745365, .27235214568076804, -.09389814717519696, .9151819357566193, .8307741791894716 }, { .11655508049851582, .9577080163739615, .4592276652129643, .6669554901992565, .38661759721898337, -.7750368015116449, -.9176367090012465, -.5454539472012481, .53540256370805, .2689028183057891, -.09900203528305185, .3363090841314993, -.7675207516593661, .409048817699605, -.0496085409653888, .539384909484538 }, { -.10035005388836038, -.7161792302132157, .8382925269779917, .5935509637118432, .6337825839125966, -.05093061276916844, .7299389193267594, .07180664786648938, -.9011775651583387, .44631922161820126, -.9792716102964103, -.738726269336772, .06475701976538817, .8359768211244325, -.5175461198294176, -.37180157920185053 }, { .05086827840454089, .46198249673652936, .8702461585958439, .6272905161678759, .7836925826820409, -.32409258715711453, -.5795947377094954, .7302072662763219, -.8097113652962185, -.9506545644898263, -.2602558494577032, -.6284625575396066, -.37777705080754487, -.48053801051685685, .9652723728355594, .1293749303593188 }, { .6282197286508011, -.10051298521243934, .7362969094686946, .3910277786120564, .21229666639239064, -.44421280738056823, -.7655466959008435, -.7082427593733669, -.016441045208942073, -.7823230943710164, -.7347803769155072, .1657298730691874, -.10971536810607807, -.7174680831499989, .737669193630232, -.18349736047764065 }, { .3607821600629828, -.8125929275330854, .5490972349507883, -.11441628842904916, .9280791788062381, -.7225894643123576, -.9731032940150433, -.33108813524663483, .7103013887720484, .9632518853816734, -.6793610713905667, .1979759893910089, -.4198842498390758, .6779157464688417, -.8860278322796467, -.47563139738444327 }, { .7301433105511612, .4933913842623632, -.5846438466564405, .37515997424879965, .1867055353129652, -.379321262747891, .08404786668500397, .6206040090226654, -.8781544572865585, -.5392178597871988, .3022970101624618, .05289693826565811, -.16514871416495125, -.31523588060996577, -.817644188548375, .045698581557731766 }, { -.17633555644327004, -.18783400335928335, .6767107779722312, -.2566947764481322, -.028763275269841015, -.018804431148561118, .13217255351556045, -.832506366661707, .8147657397973629, .40241536185190085, -.21610045498162833, .6846209762130933, -.7950530835589411, -.045798672627025194, -.11227581778881301, -.30893379887084227 }, { -.9730260178358872, .38686438350487706, .7726493110533743, -.44799820735453943, .9650846059733404, .8920956093411627, -.39242100912181277, -.7144475693586387, .2804278477199864, -.552310963960404, .5733326496847366, -.5605562202206908, -.7118354584494575, .7424146681644126, .3488541368559259, .649451784756117 }, { -.5951383809255071, -.4819480503938929, -.641313551655983E-05, .8433809791769895, -.8905302798409467, -.19690935232456486, -.7655142969711692, .7139166516504294, -.9673069102129646, -.8520655822546548, -.944930273296825, .9296701786456973, .6434056030839179, -.1550724588480521, -.5440103841609274, -.013790026590488225 }, { -.6127794406854354, .0711680684313496, -.9625045798559047, -.2666233572591561, .6636480897928532, .16476089472072952, -.5194404786742679, -.14214017381011268, .4808958130799543, -.16488302970169677, .7909225848577495, -.255103989736704, .9465480587778243, -.7764813619826949, -.19212671062435205, -.3064854011002116 }, { .21248833773931874, -.8245492883148584, .01846759788899144, .2738797203295913, -.48608258953166783, .9974078429496349, -.24398338075857584, .5899927935288258, .28203383541925486, .3695227353665753, .8755589650733986, -.28216775219903845, .7108716305440854, .5486973836678493, .7702578212932327, -.7924653920445754 }, { -.3560176129114385, -.1582727349794142, -.5397610891335682, -.9156531615649384, .7810197359181374, .6159463620926016, .7039532393957859, .2909283168544008, .4162072148951039, .29550129409995374, .44219359623638166, .644801084149409, .7714992024578577, .7753573866226267, .2555057603615536, -.27317013943801527 }, { -.8863810612631884, .3067436067815359, -.9650582642966181, .9417332159119576, -.31221705736143823, .6706739724957376, .986631062131164, .9241710320320309, -.636946255155304, -.31998000068366106, -.5343267212598384, .4725809501760041, -.6915231606349685, .9296852102078581, -.47196547630897534, -.207141271662294 }, { .18035166214594045, .5571422921828726, -.43267466580267855, .42137430975787415, .20352950148841553, -.7387007028347992, .42430632981908945, .2673088759155162, -.16364271506540762, .3896334958266561, -.7154337969752742, -.5691355637567301, .8567418291417477, -.039260202155771085, -.06942904525095228, .9494134793559099 }, { -.6637197244393203, .46569970353173873, .841175740372873, .46003019365664155, -.23768868237420238, -.25799095037396036, .23057839982985695, -.5104588787130222, -.9164146850722004, -.06620583479985243, -.790751122333909, .7961700149024262, .6079020957225161, .8449267825082507, -.28787324027607064, .12279028880236309 }, { -.8532395456048616, -.390268078386629, -.9690205884542664, .1928733392014701, .7861447431725521, -.7745902006119336, .016511199181293845, -.23696191907054254, -.002286316500606844, -.6508269274636198, .6096415171967162, .10893964088289176, .375944838864098, -.8602177277385117, -.512742209948615, -.9943242569367707 }, { -.8494616064243949, -.2711437985015983, .16674294548902102, -.33276252128858497, -.05166657198179192, -.8023648105824439, -.12228363611470683, -.871961271241654, -.7747838656293731, -.28270310559343503, -.30179446386484976, -.9833655108123014, .03853670556437927, -.5410854941603771, -.9508022849860267, -.3407636167874417 }, { .10240340465470288, -.47126183666379773, -.8887053559571221, -.3861524732389583, .4624413048330005, .5152896804843747, -.22744514512264158, .03936148797258965, -.5957399666382537, .25205305789361954, .6993795244712107, .5733166529730584, .5019999849344883, -.8542239443347701, -.14931233184587, -.08135061360961293 }, { -.47547080944971376, .7068365261426364, .4607941560108253, .7545106447882972, .035040059029455284, -.9655621558905505, -.6824114569896018, -.5027142489618674, -.5114459746299107, .14565446303467855, -.6355394969302433, .8211046079898061, .8338775985495115, .15163536370937925, .8243686772878664, -.6304013027640729 }, { -.499450232510561, .9091295561193031, .3793109954901408, .9054370438820956, .0037035243444976107, .35969447855819126, .7925802107932998, .8747921558244351, .8955696461159337, .599382479871885, .7427999587612835, .014011240521565771, .019262850517337782, .3541837975181563, -.8108708280870582, .782006675984871 }, { .587563653450957, -.7266143391384232, .05650744855890877, -.6852039212194723, -.022733978754180706, -.5464984203811618, -.5235487921044886, .6843978155504986, -.9628923060684349, .4326536140713897, -.7334976592036295, -.32127491004232467, .2258006404404076, -.6181343490444071, .6492603691066243, .7326315487960764 }, { -.008201122133724814, -.3856569615314853, .714646067789158, -.9812043375731949, -.6099570181415344, -.765222751136341, -.13335596484948464, .09530314412125218, -.6452472432111886, -.9042568480220696, .7336130663908842, -.6554709521034112, .04192836226457031, .04792372172545578, .07372254889189978, .33165858623372 }, { -.8168787986754575, .20199016542569748, -.5367013934547904, -.6346947626073607, -.04862632578873516, .6591868754451735, -.3969912257755015, .8114368244588568, -.03259565647491347, -.9614985143419943, .6629953645324285, .5714736530326268, -.14377301440556534, -.9042706071173083, .4546427528892161, .12299180861198855 }, { .3662857009363656, -.16184890291466125, -.024306336440727883, -.08936681119294465, .11442124277217336, .590177495187904, .5587194169039253, .6065779229526576, .0763201500199322, .3160819775682362, -.07220331509950118, .9099984451213101, .42563050486480747, .9166902996918125, -.8934353139856415, .45742310594674596 }, { .9574786820488697, -.6773673394541828, -.10352044045126885, .14598767811519897, .3868945379765001, -.17460863355489398, .6246911470025784, -.9105331076370635, -.4447117317470648, .14167790210021347, .6251311447532963, .028881632198619833, .473377741525717, -.6382102661933597, -.5532900526460607, .42701537364962716 }, { .3601349582089044, -.5568625906754678, .3091741497414946, -.9758769866210846, -.016966042640761847, -.146584927508429, .3513261658949278, .33465553301383144, -.7656778594614042, .7718059857300399, -.8122237802968846, .7060298795556503, .5712487707391878, .9072185587868251, -.20038006191263502, .19813097489036147 }, { .8285047357285249, .21899408956424993, -.3985201345864944, .1733490601867418, -.19685957791741915, .6232723318474589, -.41447977641543243, -.2900968084786766, .20987848528578135, .4807160950721776, .3636387018035214, .9432483730653314, .7426307627838011, -.8447167405379228, -.46617626319436445, -.5675504441188077 }, { -.7387700169717251, .2336355045147036, -.1601919634721607, -.9574214079096397, -.18058785332932858, -.21274479373977973, -.34848409399470515, .16239208907653113, -.8323719138533978, .056765869707868344, -.10984767432313869, -.1604389504095296, .24317015947913156, .17596213623794132, .8876065601135079, .35333020593669384 }, { -.2255712258567304, -.13686033497823535, -.08645135576825402, .4267639410906361, -.7716809948479921, -.1444946471152606, -.2600925257815765, .15720385596717534, .49741068963976387, -.4071624583050042, -.7718078668507429, -.10719643674491586, -.18825558378048113, .5232784989316159, -.03449691820541001, .055806497611762707 }, { -.4290396972102706, .2072648537552273, -.6815857979541733, -.5877577067350255, .2418724716648255, .7724869251072084, -.5277224967119252, .6097447029308285, -.15124069175657695, .22941644022957353, -.17889675966206875, .6897931485004185, .03585098120510932, -.7899075636465644, .7719202626207742, -.07750515286290605 }, { .3099398942035867, .2231933743776573, -.1509877759396583, .3635212704771784, -.14645554574437836, -.2756772161217169, -.1478255675045239, .3060659220832873, .023669551377531795, .1436189373008825, -.6998606446853295, .4059582419193011, -.9530402034862455, -.9489109098272046, -.8084672849935717, -.52774164704759 }, { -.5752266845815135, -.25902903149437684, .8229473442645721, .16895188790220383, .1342874876082023, -.7302556353500849, -.6568057005265091, -.12831991913574625, -.9522851881115042, .45842326486060125, .023704696837012706, .8505893752546492, -.39208806857081835, .4240366306026879, .7631405419349879, -.7462857311874058 }, { -.6914614559731991, .06051791082493341, -.5197511504981385, -.7294336615679504, .04880165683048765, -.19576837577073603, -.6476461514173855, .1288495052490095, -.0446451510090311, -.13489395502165302, .30974426653653797, .8082836240492002, .3614938187137502, -.7442568190289875, .14127516127435347, .4910009295585567 }, { -.12101986176614465, -.7703808210719745, .4808505607035649, .875576025042434, -.3422161639975385, .8344568909080281, .6053819264537283, -.34291592878900934, -.6514166698074471, -.6858143090988509, .5915143626022095, -.6256031687189989, -.6964155496056672, -.8258519225157723, .4404147916853527, -.38633160534432553 }, { .1375491901725574, -.9396168322887599, -.03361327903072575, .21992940857142007, -.8068442147383559, -.30095171618008987, .6602638567097725, -.048366334984511816, .6941284410275208, -.7648182211027641, -.5019527153478425, .5542248007711132, -.15817461154821055, -.1839397989583298, .0033384642387845886, -.8324610924465787 }, { -.22935676974890162, .18481653982395652, -.9596419878691074, -.19754419816108681, .07269145506684094, -.3234567141650473, -.2692193996785923, .6923772931481491, -.6897720039264008, .7882965246110338, .10484851977842613, .483642039578019, -.7648539355322621, -.5505072078612412, -.3450819205998028, .9352227478504509 }, { -.6703034972381232, .036204070377643394, -.10964164488654249, -.00924441612637339, .5088487434631765, .6278183454642372, .3191831820534894, .9895268079585655, .9641108393170401, .41325164791758495, .3415639284190555, -.8536677304083968, -.13038247610444564, -.21878810821368266, -.4383276034540533, .47723912763271814 }, { .46247721355803284, -.40022588071830634, .46876686024782566, .25350787833540966, .7677774413215168, .13165771087151268, -.6758718009899316, -.899738331100379, -.7654028246375641, .4598130297862002, .0426460202518395, -.3254215657396704, .6015932439409197, .18365918514120372, -.7187739342785535, .485568276160002 }, { -.825698665102812, -.7810039702216001, .16584410459707333, .5665019991469311, .04574385976962847, .6534921580090656, -.9413673763005495, -.4425096422459096, -.9083483506542938, -.4765762543643095, .44532508950606187, .5210992598518722, -.2846428136552752, -.5301913242388103, .7197529679946235, -.8881820674853931 }, { -.04851645132533, .06213680586883119, .1138287031858578, -.6118897978624984, .22057942853699242, .2998330004105141, .7118082397433878, .045471951587866544, .5458101462420519, .44274506620845644, -.8509302395973539, -.6800578346342401, -.6849965739610815, -.02880974769623701, -.63864772335294, .38226267696933225 }, { -.5757988211659322, .8747775417787231, .32736357466398336, .8570644731074835, -.7741460086424081, -.1374209550731933, .8104290467925057, .21728941453405226, .10483964144148161, -.2534736248619933, .038854588742341045, -.41623064359479267, -.3567018509162607, -.4828543829795884, .688714707582528, .38493526361109787 }, { .8947540988021667, .7100196177515725, .4220421722645382, -.1774099684257593, -.18452526680656556, -.7088412283352195, .058543065592927324, .9148322126448876, -.48400397168964493, .052530092510814086, -.9425868491805729, -.756768959195528, -.5484762789621043, -.08998394856873393, -.11427024572333866, -.5878927272318857 }, { .6522281654677251, -.7190921094494742, -.6403751968198008, .031051995845321034, .22103491180696277, -.441458833089033, .7070518087537512, .5772356919019854, .3996355950210335, .802848105150064, .7981502870932919, .03554152681276235, -.23024069854445028, .08759462327607159, .16311741666206947, -.545696838690094 }, { .9007222948617934, -.5237402835049572, .998264800253476, .91731005824634, -.9990962082726185, -.10413979467953793, .5886574000425788, .9725709203465198, .9129125631084309, .3867434938649046, -.68445185674992, .31165841508997505, .3991550295924493, -.7735020079816994, -.3531552428140463, .34665465862116496 }, { .7463981976421086, -.3606492108405197, -.3257330994972878, .7887261948833182, .9172022590357147, .6466335069633364, .8707350173288806, -.670712683748963, -.955551142698845, -.6003865179163961, -.1995544577478614, -.090475481006834, -.12126958134710164, -.13924417107670428, -.5541012195543153, .4888023031128037 }, { .5170433955540785, .7389224132244916, .03020211734489564, .03989268027562254, -.7005626597862658, -.9678858802715213, -.0327494891740685, .8052428980402426, -.7431084325754258, -.7719360742386643, -.5593225693956754, .1482209948760791, -.6977993085814567, -.28097280767673327, -.049572628500341276, .4540772654363616 }, { -.8872266572626588, .1848762247366087, -.1970748108603022, .15470513211074843, -.1698985743869892, .3797408219649354, -.06500394207043736, .9251911829221764, -.8067336611918294, -.8784221240384067, -.17784203997274517, .06959392982744417, .22428091014646, .6789598716473721, .43490717269668644, -.36039158219845513 }, { .4266288968821659, .4501098294205299, .746553091282155, -.4402829733000746, .10824662754178749, .9145527480748201, .05341644817618052, .8966265245220955, -.432650521104883, -.9876664875095327, -.9405362724421646, .9721534158575684, .4482397087386041, .5319781305707976, -.7367385653773315, -.614904384587714 }, { -.1710808291880328, -.5860423719225845, .3739516223173911, -.5193051973772553, .2704712459430396, .8810433814146377, .95406695997117, -.07671849615807869, .3598577875304021, -.6883572876669126, .633979800034919, -.5235350746400294, .1279528746175127, -.05940509333497168, -.4080580462385335, .5200919215917026 }, { -.8631440417330536, .4614107979576987, .3819661196832498, .5522567191838315, .6027950409562925, -.13318696494722748, .10717678899995842, -.9794638707100627, -.9848807946884983, -.3474437043653251, .27838961778573457, -.3564507966681416, .6243775549804165, -.26956964098160796, .7026176871313812, -.055981148772545675 }, { -.12161813605878491, -.864117650975375, .2088190178482192, .014094217252080155, -.6955983239007395, .08343562025688289, -.41358247997701625, -.23940100785399498, -.5194967105687163, -.7708670594436808, .023799493564101715, .221923559654839, .6637610405489331, .2337233991050045, -.2553031207690484, -.6390162558926775 }, { -.2238699371297006, -.17231862379019658, .73399743615214, .7599269889800917, .8938897637186252, -.18567384851455593, -.011875196109966568, -.9689626843972223, .3190614972732586, -.942962918264953, .8108624322055342, -.5195962229874498, -.8448334160659399, -.1713419269362655, .10000249344499679, .5241430784965277 }, { .10376808633975743, -.4640771682993814, .1900150468448596, .4247643252828166, -.8172502832049644, .09771156180730833, -.4656654983917199, .18073988873238633, -.07278369724388822, .7433229547654576, .6167596272562657, -.5614760388758555, .13924511435119147, .029262450942927343, -.7164395439360334, .16935916560704856 }, { -.520519950597292, .10094447757721214, .22074544254088901, -.7776854205927082, -.5367988349679234, -.718469966568261, .24712602250497318, -.9668365978532678, -.37515070112607773, -.00043832420907663483, .3283339309839406, -.0893074244914347, .7592382581344019, -.9124163317204506, -.9488428794482697, .6888523636867792 }, { -.14617528429451387, -.2533721008011933, .06952290827565588, .36581638846442077, -.47548300621128536, .3725812469661014, .7754833114387287, -.37873647007923505, -.17839264558036239, -.09234395536068463, -.05800231321616156, -.1141045454348193, -.4675051210902197, -.6233048593295634, .7799308048334053, .785247252951061 }, { -.1280204975880661, -.5107798304614584, -.73705947500907, -.7501866726954824, .2156511512694308, .7567293567259386, .402265629812099, .5064550331513749, -.06582153760809573, .019088124112339955, -.8509036109105286, -.07826641611908447, -.3998705481907441, .723974132745524, .06552926669671555, -.8482537694272878 }, { -.9455792577346573, .4596849828875018, -.7909290586013391, .6439828761559876, -.35122101514665793, .3701857470978618, .9457381763285768, .8413354523936094, .22984568650546944, -.34764147874753526, -.6482928032727102, .8114794634482858, -.37545405364109574, -.010856355483062519, -.6375156646156628, .3042686594290289 }, { -.944797905754668, -.41742737087548787, .057529054574571914, .18461838998640645, -.2784755574311846, .7588844785133249, -.14962775231714875, -.6782090958545339, -.5174907032522358, .5515558603728468, .42097883258991287, .8823390588173226, .3824631578271267, .5616959710899789, -.7128068148923505, -.8349880131933931 }, { .7405888378051739, -.8485835186256294, .837945857720461, .827862196444352, .172486593008345, .5910561902519378, -.3123258344741009, -.006034101308052708, -.34956470474925516, .4588639123022402, -.072119938643193, -.39325896716111797, -.04300556827994373, .4253322788565783, -.2904089491055635, -.4579674400417093 }, { -.6453278066830477, -.16766515119746161, -.7581216471506103, .6195907990073966, .9686098249610289, .41356959780220337, .8823264015246943, .24265324609872607, -.8340157308600542, .9359452247520257, -.03456242929223641, .5709589962661028, -.9721626360427951, .4904133110934348, .09721292521861535, .6363286350342443 }, { -.3313704045351842, .6092856306593715, -.22611818681082596, .9225890875659659, -.15262746402189897, .46447105296341396, -.722589824141304, -.4761637147331519, -.2850056912335561, -.9070836974372778, .44651254530554696, -.2507699796612681, -.8750610395882787, -.687906085052995, -.7582316699345535, .5321546604842311 }, { .17167196249341998, .9870783572795154, -.2934504366451185, .975812851471727, -.2830781722931899, .15750527531150338, -.9479955866069085, -.9352986277349689, .34296937318298326, .24167883324040118, -.7003823437154302, -.3484623430782008, -.28140453980775093, .6498677730407265, .209677017959915, .24416005450531708 }, { -.540825528981753, .42359380170982863, -.44962867280802943, -.5297209702120038, .3613854176593032, .6623082780007516, -.571168032543178, -.27490378891914213, -.9983700926906416, -.6496410010378912, -.17724983971352204, .7648559025727932, .8873339798364708, -.9005077376752746, .5795709573124888, -.14015377476608726 }, { .5934187696712656, .18138573469728247, -.4284199567303737, -.41177949051150176, -.08871879358964319, -.1684090416069468, -.5452666632113297, -.15349296684382985, -.27902733632621435, .8274218737318937, -.7354426702692654, -.30142800864631325, -.8844179139483994, .9624978848968029, .12606834322799498, .5587910833877368 }, { -.8573605843097005, -.04778667028462391, .9077891527581383, .7117039189065761, .47375932497571527, .7629193558616547, -.11731973409716634, .23922673245416815, -.956281250448036, .28420653306683796, .7717735596901516, .018675743429809488, -.7757003006947014, -.818135368714078, -.6811733576017975, -.2540859704300833 }, { .32190838460289317, -.2329689543783633, -.30015754214291324, -.37105911954157156, .6838841311937305, -.8184302616437209, -.887190061705879, -.1842840052700354, .211452422165346, -.5215338288331794, .9158002383514148, -.9722722024863255, -.04189889024667637, .05272892939996243, -.17561364406265612, .5389266610718499 }, { .7536762999033602, -.9306173484068911, -.4884871941515079, -.47068978885282053, -.6389411815266799, -.4523695043679046, -.20381672415039898, -.008409101598444657, .6610985732849253, .6446634326049878, .5065688329935087, -.7761074268130272, .3675626731563044, -.6891606899878984, -.5098570544412282, .5971761155948323 }, { .5094127465885194, .6416805598682953, -.9177226836052508, -.6206904195261116, -.19662120248386472, .02837846175987635, .40837399606500946, -.7412918940608222, -.29412251620609786, .030883283983151877, .1440694129763267, -.06722407683973297, .7609912750564867, .9193918513028747, .1267195316776124, .2366203303351051 }, { -.25801614407267737, .5489138979478148, .00825691168400322, -.47873317940737614, .8891227463251985, -.10206884298757268, .2733840195020629, .12161210891144925, .14180282458423643, .8601395381250754, .07229497233080706, .0657937680543701, -.07476384735437125, -.7200097327575488, .4491874181174458, .6399563794192016 }, { .019991267490476528, .06026619415595191, .007101150013979707, -.4427680432440628, .5257293524078284, -.6103001915310917, -.7031059982314614, .7114652041823815, .031040776656791502, -.2343773098496036, .7644501400003403, -.3172732733222918, -.5241193228625398, .855515961435273, -.644800704353178, -.1369771391845227 }, { .704672629769578, .8655971897889747, .6528344931198391, .15976413017104352, -.49904818113639826, .9180779371475682, .18411744176240474, -.48698975966184643, .22201575931092288, .5925943801278859, .9802590004247558, .503348340648835, .7943441575129739, .09432913962962997, -.7208631237835954, .8900522781299505 }, { .7652739459797921, .9343921002586526, .14540775106909365, -.5028624125845369, .26130098893124454, .033677614910661235, -.16846549438891367, -.5349596181848437, .4163324250958378, .5632291766539714, -.9035766824674398, .0265302796821969, .16034182173984668, -.27169919344595383, .39849045204001654, -.8933026996649618 }, { -.5289855690542422, -.36891275242981103, -.6753332576738507, .19608914627750584, -.14484010140463743, .04263836418601952, -.047135102275618124, -.3189895439269017, -.48613208523756235, -.6980060104610142, -.5980884698635685, -.35815318246973793, -.577889920187107, -.43483278413533033, .7783196080027064, .35909737199537517 }, { -.013711211573871784, .522950368003362, -.9823044640607705, .7070721626799374, .5812293416559839, .004372925496996638, -.12144327106965713, -.48907059918620854, .9679661050650861, .1748172196977098, -.17450679540745684, -.5502962164940011, .08807956827669683, .7142673105051762, -.7928442692468896, .7448175081840183 }, { -.8316646374597612, .4650111752247872, .6799808438990675, .5812357673823829, -.8433351192627505, .0768958151779624, -.4148781197036058, .6571993376911625, .10530044466642763, -.558827263088195, .15028936512996882, -.5807981044192865, .8118812653725187, -.4989894147190663, .3250702373662546, -.03645162776673683 }, { .9774685210268537, -.5616361853733038, -.526262689462696, .6705086130896756, .08092878195247621, .3973682489885193, .26351623066049146, .8958154972585817, -.4391937023844299, -.8454210312999215, .8382576265352082, -.45413435505622135, -.2769351056236977, .1171951601536656, -.7109302393450585, -.5191476355896152 }, { .24781736026859513, .9312564850104648, .6225045263470126, .388489051909505, .32610357767953513, -.6813654529979467, .7681642647659539, -.9575922407036566, -.1403189813177539, -.3737603593431209, -.6901259112925431, .6678787286627266, -.5512800767198867, .006994508725668869, .10762094425949176, -.38820155272165735 }, { -.9781117018048815, .6691276106007422, .14169745874900053, -.46997420212477614, .6491183237819678, -.9720230770124803, .2883295968423809, .7310660888380132, -.03289444094381411, .0857116199658503, -.555638160509111, .18815976972676074, -.6354276889339039, .7653949866758281, .23165766317429792, .08577905758383642 }, { .3774184655416679, -.4148758467223266, -.7090717009741481, -.6698112688210431, .851132776820692, .20348211103688296, -.013611208077379588, -.2554564905029306, .26960510907477353, .15112741341650593, .3903614166432188, .5004965604688296, .5383134366076925, .20337014028674782, .2530124597133545, -.7671494410218109 }, { -.7369153296145183, .22282330162183417, .5648643475505937, -.22531091019085503, -.1868740127171531, -.2827787328324689, .946565362321732, -.6209363144674271, -.4360211416663926, -.14077639875234427, -.22547657696409074, .0036060765585657073, -.597082377176698, -.5115800243801745, -.31131035644912863, -.11697015055031112 }, { .9953272984044617, .6008648926883691, .08339861782381397, .3750814453484419, -.9668746311943033, .9591551046244888, -.9643600727831201, .8716484639480409, -.7932140791583506, .7938421065256009, .44146814454682204, -.05477108436538991, -.4597523304232043, -.9256439863802675, -.3554614381568737, .6437796689879041 }, { .5742125728137877, .14399573319925651, -.2295395220961718, -.14745239488751394, .6675961508943307, .697523603283438, .6455014590352308, -.16377927747756438, -.0717489714348567, .6213188944218708, -.2741196561607846, .39726711372692325, .38724983685391545, .42535833720233285, -.46226922232079715, .04331458911637598 }, { .5747810106710847, .5116821470652833, -.4750639755604753, .6006407524551485, .9297685486074991, -.16644365191597066, -.45695130866160016, .8708925784165329, .6649241209009502, .02332863772472127, .0025452076726508732, .537612427606186, -.38633566458701885, -.9797478921232139, .34623573175062017, .819247217461655 }, { .7260650608695021, -.05514928541090458, -.35708617303103174, .5462289816056245, -.8571175145926186, -.07475657391387935, .11462062811268003, -.182831496236878, .030154397736092742, -.6051658848506927, .8710614855798144, .8252541228852572, -.26236221572144003, .7435057920232697, -.559429759315144, -.5557983310058836 }, { -.2811308194908213, .06031108478046976, .8425254832674629, -.9149339474329328, -.6915404106821696, -.851639256455375, -.9899421285515237, .18601280126218023, .5524283308640314, -.9637568097244742, -.4293037311740395, .07698142007635855, .4377070906785425, -.6884881839053558, .02027579916931721, -.5643171877693332 }, { .6726025532238538, -.11045828568690985, -.21102787327818318, .09055748598256796, .3715283184178364, .6009567468254009, .13519858891114445, .6592838386468916, .976100146360452, -.7750342919012532, .6454498701501798, .7027770442006953, -.5184665757737708, -.8620118035875963, -.6499699971070125, .5726238178282994 }, { .6126900477349555, .8521247689471123, -.00326198643481157, .48504121013631174, .6080868375423967, -.35798953590191807, .09955449771800051, .6485566639462785, -.45691673179871395, .9310819195153539, -.1531672326682787, -.36456846073801064, .40107392883498094, -.659589104655945, -.5204542685832867, -.8740268292580611 }, { .20946877840099742, -.252647647258436, .5852188898077519, .34253385940877346, -.46191474247206643, -.0037056717157533114, -.610698610549635, -.7436622376911091, -.3715428061237471, .12124434563106234, -.15913885487910484, .2254454536744801, -.8824368519250734, .726037454568611, -.33337806313023655, -.8325818828993086 }, { -.16449417656169785, -.40372943449970156, .2328495881882542, -.3991852293702942, -.3283878711572237, .02242142872625874, .13138784601780107, -.21967126518947233, .37613426534931915, .9503262060003324, .2933435159898552, -.9345243510579035, -.6961824035687612, .8185169549738018, -.12016581460700904, .09452107503273299 }, { -.45401481632045226, -.7160598877594186, .4258386962682874, -.04140277770683953, .8307000110688558, .6351943121603987, -.059832156201095055, -.219912293273143, -.7504074629856485, .9845483664854773, .9481523309226441, -.5241060117540637, .8727678083190069, -.26265248567681176, .7157237995039785, -.1574076616821063 }, { -.7716815051320531, .6231916579490608, .6669041924681227, .09577909108937344, .5271795943130482, -.25677301095367455, .04659089404480965, -.00598373331201385, .34332580934002976, -.811345163636441, .653826109514754, -.5222895308187372, -.8206939819575649, -.5430875148033549, .7922368899391505, -.035072848909830645 }, { -.17420469859730603, .02037716052217431, -.8033012251815774, .06181111309476539, .7758094888208358, .2830078113264922, -.9847675842727281, -.4055890706329979, .008344518338309737, -.8860402588881351, -.6965964631623403, -.7718954712501198, -.7117891611139959, .7855674400648982, .9847411179782299, -.02475761123055631 }, { -.9000980830104279, .49145719776816166, -.7055804488111366, .7476970962780558, .04109461607478426, -.8077999582742545, .3736247528554866, -.14725302799670703, .04107649407480807, .5347422530576711, -.8114451090703707, -.1383756841118271, .12194814162183443, -.9670190134852625, .3529294825979332, -.42820525133047727 }, { -.2621009595926327, -.4895047326417101, .26834604313435473, .46947941696574347, -.7386043176938837, -.601090581710056, .7999258186386888, -.89415909642214, .5307330337629073, -.6746853750711226, -.9931674809226168, .2746867821880974, -.43466481612117414, -.9605671829857481, .5864792241783547, .1535498116221763 }, { -.02389653107581058, -.563308799033847, -.9250475831708611, .2706924993456594, -.4182884346972531, .0686321748223282, .8499549709079257, -.6218520412455879, .6222418864649792, .5311141409567279, -.21330130491789978, -.6675155061220299, -.22622370694573624, .9940741653407033, .14357583475478108, -.7780851078944477 }, { .506385559911444, .583548495093074, .43759673312315583, -.9999571184082883, .38281571509328827, .9335086985179515, .28010513750133614, -.30932101457570815, -.4510569978506882, .7034290877615157, .10936950031487003, .6088855365467853, -.6022772062009869, .6219296446164564, -.5201393042141012, .2661489427878574 }, { .07351634695196818, -.6226959329184332, .704190138265619, -.47546562241644375, .5130306902856563, .683721420726233, -.8705520716722317, .37137055491874893, .9915752197712897, -.505654117131902, .6101085781304572, -.9392812754379138, .5054584432308427, .27100629761696227, -.5714507364808712, .3193994664002766 }, { -.1402051330442502, -.3098514282613538, -.13484411274978192, -.1627710097883981, -.9033944768877902, .7167010306629367, .9648510538190471, .6765817867688917, .016512185335499696, -.8804993526472145, .1458050968536897, -.10631547838213162, .7271651522192029, -.6682285645827772, .7329498140626611, .6484560243676418 }, { -.12881702792082073, .6544979019648753, .2686698313218048, -.09345601264565984, -.3652232372891644, .8247104381411596, -.8414350360998333, -.7736058041291494, .9878957421790402, -.7290897071551201, -.10791059945372661, .29428212601675785, -.518449339460884, -.2838661045372808, .7686696615214867, -.7178885465717613 }, { .5373920357364059, -.3714741379206208, -.45159939317692177, .49211611992768045, .21353222830357277, .44992371201697057, .22379857155083727, .7869826275256651, -.899402019876443, -.22533057713301585, .6537397719269331, .850593119592802, .7933892519453056, -.34777462860166297, -.3495895898919943, .5610407741338035 }, { .11633501777171618, -.9367330083159229, -.34705238927911286, .036821967024915914, -.2983957829693711, -.3181342650571892, .5666572642395513, -.8547539766888095, .39703001721862363, .9506253381313867, .10186116177596083, -.33559393874934496, .7397575490676271, -.40652025706860284, .08690832220845035, .47741456963613005 }, { .23693519321878798, .209540246162633, -.8594647647728206, .985724673886724, -.44556063784057054, .08873845344702902, .9146755980500589, -.7295753638531886, -.5253362164042679, .7903072325712, -.941500895872434, .09277616735959393, .44934002141282314, -.06290336495596316, .8796987400690788, -.8615464186538417 }, { -.5506479740765424, -.5634596401112233, .4693423783440793, -.5766451150156193, .74535100102834, .9586507782952878, -.24110638714248456, -.24724651597067004, .2562975051554828, .9616801447987144, -.2327649073865632, .4320174815933524, .04795259788449213, .8245286073251601, .1468740644337998, -.9369804169051625 }, { .8482371740938077, -.30877520158507754, .04988404619571574, .5375313408582161, .7998274377676944, -.7879075324814222, .48864297622693265, -.6427607977318748, .37415174368032145, .7264241776265479, .14114764857980733, .1820727647968683, -.523452369330603, .7307041447411211, -.04031721530630339, .7238536501137023 }, { .21554453991314837, -.899198580108733, .8755953953778524, -.1827439579906136, .9619405040340383, .4977553571609681, .23188518576052863, -.7586857608228565, .287874632988675, -.20221122168001648, .5644548650536778, .46236551600783904, -.3677533442947043, -.9644191876092683, .2202378918722112, -.8449414092682785 }, { -.06821622768549074, -.7532318491513876, .6655288989843533, -.9319931779402433, .5479023927613234, -.7479753977668875, .138019375737217, .23460697406898423, .7893448186612617, .445713690557751, -.12344721448774099, .21950095539019365, .6973644093295723, .344031428906592, .36595222923230075, -.7267278444628016 }, { .35935200052770866, -.36664063370260536, .25720303643742426, .6354298029542618, .3585779704780354, -.9025857910108592, -.7443374628150472, -.4505333795387494, -.7527565148369333, .4155741271659059, -.5380898727884402, .8297058170308205, .1835461611139888, -.7893341071638964, .7022854270215926, -.08981090362787136 }, { .727454493563009, -.9022199181053412, .8921222437031162, .8918160329481297, .9043363061101246, -.2720066021717271, .03133745663873233, .011231667117286737, -.0032737771894921774, -.9096617031306327, .24019061026504263, .5355180327017777, .1599431962431812, .3757722112425732, .7534260390813068, -.7477254285225805 }, { .6224486627624934, -.7608755573255632, .15207414584998435, -.12370298459991647, .5171111055075304, -.6145468244072836, .7760580758809552, .3412072073074799, -.6010337699453339, -.46786710753495186, -.5985604330369343, -.547537098150954, .44095272220386184, .6084973829729823, .5488077230073203, .512594947007933 }, { .9079144975383275, .9239306525917597, .7610456306758222, -.92300444856907, .8396603660009108, .9423261889472374, -.07288338880328471, .5186844451624355, -.585923987238834, -.3993330086947222, .004682005336055228, .07886640238317466, -.05541543623003764, .9528109770487552, .777281363677482, -.3033612234698482 }, { .991242357974528, -.7366521694003019, -.1003959947179387, .25717119019222734, -.9436915250211668, -.22711670012255203, -.7191429478330538, .9374664341406722, .30130002647750387, .32230396414142914, .509826757416953, -.7001891819158053, -.44421775213238557, .7572317708230463, -.7523940204610502, .21960619089043742 }, { -.5210137757456452, .8530492947771025, -.2697635764014268, -.5875433211990562, .15558630893032954, .550547725696058, .2169203345151849, -.0020561226828197388, -.5504178879705575, -.034811760287380755, -.5221908914283477, .3259305004462929, .62153021717681, .11283944756441766, .26739658399067157, -.2536354143284547 }, { .2198400482974625, .6017594626501501, .4283087949842703, .002940630173834169, .5776352748528899, .5210017904222566, -.19093624157223332, -.7497017479731731, .8600259702520507, .9365008371966932, -.40654428226587647, .816202023365354, -.5598283115146023, .021941007659567413, -.25718421010984205, -.6590676470221248 }, { -.9093519935887737, .4192173606856078, -.7427790144849729, .18705166200597878, .310195129510757, .9779441338823691, .7319545289519616, .05545244280950623, .43281208632428547, .8602579167416537, .8853769262853755, -.8182026427733373, .03835285908914998, -.7868862747146546, .12063963051338877, .6451708400493905 }, { -.23191519681548556, .817858792402637, .23528855634137136, -.6643967995718694, .011025688910394571, .5609197218693891, -.9218322317241254, -.5402641982104028, -.06678864902137449, -.6341278474826029, -.7784617047626012, -.21378205452719778, .5451240467095808, -.5722502188875875, -.44871046674023085, -.32466217966190025 }, { -.38905423517405824, -.27714150810363436, -.4444706455130405, -.6737502923926175, .037938129187271796, -.37193890916240324, -.6740508263112701, -.4900069115700707, .30742236506436416, -.7575936174203133, .8056023083956618, .4408046456690049, -.126090603757542, -.974464204597475, -.5318209906489566, -.20759300613513676 }, { -.25054212446037516, .4708434686491785, -.34844562708162585, .8145499707999986, .1759854306831894, .39289724586302555, .4845998431924581, .7244025283051749, .06800913418710874, .22976032449793515, .19588823166026859, -.20417447904996777, -.42253720298648, .7525944838831498, -.9399764039271294, -.48392924317078734 }, { -.7090643890019204, -.760653837062444, .9072383596486711, -.7075676018689501, -.253653073759335, .6852315716434134, -.7626939318854165, -.6781206870298411, .7803648274587687, .579739693900789, .8699907676540968, -.30423794612438826, -.8882782131336782, .24442385418508428, .4419315594829776, -.3684088236342713 }, { .46746182519626744, -.8824499940056532, .6354376305585718, .3049615209189218, -.002332063503863502, .12130741464486894, .16698943229381347, -.7825905108815459, -.30700397287184455, .5445738075752977, .29546573883859995, .6693289439042294, -.061062612134624716, .7425916449756875, .4522321985983626, .026676131244838253 }, { -.46165659571864603, .3038149042885985, .40191922842095074, .7047783165684969, .012742054077726328, -.12642746377885872, -.6601622812961063, -.10993430149804206, -.8846535315958011, .26064607629218184, -.2692574675253545, .27180965652072486, .6628504052599238, .9827325027102887, .08249506744805268, .517988504189604 }, { -.6101706808098522, -.14883186122888103, .13216463037607928, -.542525863356077, .30425483247191876, -.29274225871224546, .8952578288783024, .9703550218989869, .6450753006938781, .11655154765321041, .22914936243853146, -.527591972860576, .032789324994542124, -.012026873909834368, .86405248267917, -.7450363173206345 }, { .8005329665906955, .8019819710632166, .04591996553313793, .43040508643496755, .7140944774135425, .24855785525096574, -.3447146653557005, .03501215837516969, .8209745946228519, .6478199615118732, -.2611770188324669, .6715627325417799, -.44823487975639176, .711285957480468, -.6375811191419261, .6792951332953368 }, { -.4306367818935781, -.9081166801938003, -.5808188581228602, .8822842379729063, .7913991593649514, .1532015302955243, -.2301461118006778, -.32606567916318885, -.7496512232639203, -.07282103599964507, -.3732339799586186, .24917612848032533, .9666318400881511, .23750201267834337, .6183878482548384, .6100376503386287 }, { .557724504480479, -.6166055289385726, -.8722382105833288, -.9328344955836889, .88461954921521, -.1308194166611263, .8203757357969375, .40516416427970214, -.5138143065142167, .662111811812824, .5915016983727366, -.0735421194906698, -.505296850007203, -.37067645254867987, -.5253004957476075, -.9085080512761452 }, { .8183876061797102, .8538029669464273, .35785403053390974, -.3233462734408561, -.5796281930337159, -.22899277769729665, -.7713859023775342, .6620225536339239, .4861382032080961, .8171053621033302, -.4099291330916679, .08873977867120231, .1257318807748573, .332814187121208, .9482252800608408, -.3366688910134834 }, { .908093377741463, .9135109985063883, .8836667482618292, -.35835784401319604, .454407056046249, .7690598305978726, -.2815473765585881, .39050906718263434, -.1419984481404053, -.17714177414990084, -.1477455815082509, .014339832426291776, -.9839021801321994, -.722982915410074, -.3777877779717296, -.6230422572965937 }, { -.3255372967507866, -.8917189000662149, -.9886389072272248, -.7914615927136599, -.6174166207475165, -.7097875662421258, -.870206930008709, -.9219751060874593, -.16676078089343527, .31363634551960184, -.5808558103265657, -.4012308809437761, -.5401147575321004, .8497220652742441, -.13121344391989398, .03758796478607884 }, { .6923968562599443, .4895278919867847, -.8433556485716895, .48194120415094477, .5443241448002467, -.4915958926341484, .9024498363391931, .4947086257906843, .03128372674338764, .2221365401359081, -.14097002667837377, .020458906499811524, -.9588378833990854, .8485872926489075, -.49928071349731873, .18169557577995565 }, { .9199149710864309, -.703835230417958, -.6672169146591214, .6274166575869653, .5352137238248149, .8541957284867048, -.26022208603343877, .8817430613410318, -.2665566286175365, .8744750199892326, .4647887114760094, -.36671691182943533, -.9215871853297586, .9546080372951942, -.8462271124176859, -.8393629704171162 }, { .8196516703409, -.8409543144204579, -.471810769040385, .011961775328386048, -.6527548357595738, -.4592244894630608, .6542006014157118, .6161188895092518, .48475139326756245, .8228299962628418, -.8999745153981256, -.8303762725968196, -.8920473473402311, .4764580858906591, -.8727100825946903, -.048320020332539126 }, { .12542267203517743, -.22711879855982597, .8498617790743883, -.7098311818443379, -.6028280613650492, .5788216543263598, .029394581299738487, -.6310771400569135, -.24933383584617075, -.666012611351914, -.9773391676941892, -.32430749897545996, .4042755839943488, -.5936897489070958, .94365153237983, .6377774587709444 }, { .17605243377108715, -.5461734377842409, .8974512583359724, .47915772132817724, -.03625535982921124, .4248855721624394, .8417793972887699, .9408422379337757, -.2516899367198808, -.7594713832550022, -.08478763252930288, .053550922486834907, .3054821001012533, -.1555525155389983, .10035561881987243, .9796342644023524 }, { -.34623156401714095, -.9148862198420804, .1980002712251292, -.4664154181290743, -.1917930589737038, .8608906412811412, -.47939702983110344, -.1765959978047369, -.9161218167947303, -.7907204500452016, .34891566302827837, -.020938580332025225, -.5236148956837103, -.9852557764424916, -.12789259709877854, .584229342916873 }, { -.40445462882121475, -.7538797385161016, .10976523155006657, .013913883539555805, .07700282142619841, .6476887711534629, -.4343831351074192, .9471758741861886, -.36162641887860025, -.5772672540047503, .004486707009254465, -.8775871777510673, .4216236884661002, .45867664288446863, -.02486255318209385, -.8511154349216565 }, { -.19547911935517215, -.8936627950807263, .6107593156248092, .02829623960178118, -.6156604918118229, -.9173903273086061, -.23716452211583294, .687580637463221, .4271496589557804, .4088639068266924, -.45110762253372494, -.7581631922505501, -.49853897878849174, .22589430187163528, -.8976583481624132, -.6262321060929905 }, { .2131973089641679, .4991345649571648, -.03878634160745986, -.3660115780019697, .23599449898449065, .2970145653440939, -.7055294316483294, -.4198122737468415, -.8064663976349129, .13370824779035972, -.9577576133344332, -.7919903193019395, .7570342499443234, -.5370633732397394, -.8905287237123471, -.6433269995049371 }, { -.05904534674124284, -.874403045996611, .6203308024007745, -.030811472934922923, -.5792875171329361, -.06581281022771468, -.27020820516581945, .5024425549377485, .5683043909124441, .618311648536841, -.022601507234785156, -.7005051879676008, .5546555257138628, .10370777184948499, .4132849008766608, -.6412126779303617 }, { -.2636611286297943, .29134894535165734, .3418575808247002, -.6604976206032209, -.5898845385517777, .2950934876588567, .4983315242308217, -.40072819522891345, -.16749706997214941, -.8749807465650161, .6659881890960728, .16372832458043707, .2603550533070911, .6139557736645953, .6277386448858318, .2716879860917909 }, { .3777859002352191, -.3127531164472144, -.16025232691320657, .4065850056228024, .34723803552355803, -.0888774947995481, .8576708814864991, .40873087533709174, .9966674739486745, .9441013546414225, -.781467505098568, -.8744989131654164, .5627373897488794, -.5010727674419089, .07477672302310467, .3254290877834103 }, { .6409023726144398, .05369838289487694, -.882303835643383, .740830402952015, -.4709370220729472, .31484812730383993, -.755385255194919, .15416752886736274, .9463737665970164, -.4491081654900184, .22398830241720424, -.4760883385215333, .7255835941448003, .8056483889322641, -.02288038738726872, -.864573196135966 }, { -.4074366921220103, -.25585347365901434, -.13258539621871135, .6385434740529736, -.7685241208589559, .5526820291559922, .6305201221381265, -.5611961599178446, -.5516197028907279, -.7584738061534426, .6840495207069892, .2706754989119149, .19414075296594424, .567717221393029, .9279903995446783, .493927366150299 }, { .11729167821043807, -.9261207471420525, .8300027808207238, .13506462959503884, .17576330719165845, .8515515983760182, -.28347426000337506, .5067596559768814, -.7373168145782814, -.7299667423922291, -.5238041115039656, -.38996685522674945, .8585814709654029, -.8731145154002546, .43842473087175526, .22868777526896888 }, { -.10205915058195325, -.8292579112940368, .5619162417428809, -.08245645362407794, -.5313370434940459, .21105094980178118, .8208933696327148, .4040451158186358, -.14137589072890622, -.3595144337373595, .40666583078661844, .9471385840003419, -.07241606035262427, .5720017354796016, -.5373694661862001, -.717979253268193 }, { -.330684372540784, .16151328928583397, .8598921463912295, .9212284080685509, -.29545859752664483, -.5676921403797279, .3836634974968134, -.6584842153462265, -.7884245396365508, .4949953332908934, .7363173931453102, -.7554551182842344, -.5255171731808548, -.3380561345528903, -.9983495367288311, .20790716875732262 }, { -.2780382217992141, .780361557999141, .6951879426794638, .19470014398522073, .8936323493721479, -.21692893630818166, .3095094298655192, -.011296974319349617, -.3916459636410634, .6457595992347536, -.3656060401164729, .3489271968621903, -.3721087567667898, -.8782945008257292, -.8214566350766155, -.13521183585948182 }, { .12231551508147365, -.7125320585541144, .377616475192017, -.5766813063450726, -.8262009287689558, .5842187776771828, -.386143227787654, .3957088566177951, .5020749668831797, .6175700948486462, -.24489867870955018, -.2822117143699252, .1953142478107801, -.6025176699763519, .9316352992570331, -.4368912010863204 }, { -.44677034348696987, .7492793807708571, -.5249227098215181, -.12175662297305734, -.02545747445889779, .834411926699612, .5768761890601977, .06779435315123661, -.8995969621932245, .14583632753383258, .7499375882452564, -.6568070320283925, -.42142842465097896, .8852609120864272, .6064800366258472, -.8171084677426781 }, { .04094160717759232, .3475739890804799, .3052351710209582, .5469434826096984, .37484964928004505, -.34028281139969674, -.6143977544856578, -.35108765706957, .11859227858600918, .9176001084375802, -.21532681897677808, -.22380681772251187, -.1352814293856266, -.6877754950378341, -.5540032940775914, -.3352343723624627 }, { -.5716217490325968, -.6784307255501507, -.9824497873898435, .00835197021033296, -.43350015713319734, -.030005217883949387, -.0962463882510749, .26797069961044984, .044798185706586224, .8165626502868712, -.8755273639967469, .8113760158205909, -.7955757258444864, .5131069959487229, -.1792206776820071, -.24722767565447135 }, { -.16234949698760537, .13133712191801616, -.23113815868487753, -.032783556637371536, .48331889612467216, .7056988691588175, -.48513187130928515, .4530386371530477, .6972155064293308, .8330565986206178, -.322140377534069, .555468001915518, .30745107526459536, .03835326104435044, .41378414646752093, .2585953515173003 }, { .19016204964506778, -.2912856449848438, -.17963987963661165, -.13527970211526097, -.13171506524313958, .8047266622192841, .12561645001787292, -.011364365311636648, -.8356335531163916, -.3425465233243927, .5175918843280949, -.3798588672015615, .3922249887699185, -.761087848252131, .10607522201279829, -.7056290290759795 }, { .5622499129518184, .4799975398633016, .5991588600967459, -.11690977790449719, .9722310708368858, .5725129795997361, -.7609929797600608, .8179175736777688, -.4254672929007328, -.4325608403076251, .7621388612808708, -.7753007595560282, .5190008960886507, .834326599299827, .7886207349118388, .7489238272738585 }, { .587517167125813, .7582377744574975, .6427055131418393, -.7532088179166916, -.4305919605302022, .19490566489407457, -.07667653044883571, .21436141085046811, -.1781374025139224, .560531564796054, -.47876835215680336, .8056181583786128, .3045001808021781, -.20142287234375478, -.5154653559831066, -.17048247225858715 }, { .7264560805688396, .5154431979061849, -.7567837087456988, .3219247141197381, -.14113277025747872, .5890570508411086, .6638056170852376, .31377940167712204, .4267940308951408, .19770483639599146, -.8518784439141467, .38305142476966836, -.5676548556982208, -.36194012692674793, .39023950161117393, .951217131372776 }, { -.4012189276362572, -.35320492018859984, -.3345908551073933, .1625844470453719, .31978824557723184, -.7199037718280148, .7563668028939114, -.9211161940584012, .5960225400854671, .4578732205120388, -.8386588876951282, .531104351165024, -.9672696290138305, .5149471786535402, -.9442908679664388, -.28261728275871545 }, { -.06952595847257426, .844289290905347, -.6985731822675552, -.4611948292968331, -.20965189960251274, .2579040492498659, -.875035558001483, .7595758914897675, -.8837316255308802, .09336750715804731, -.07705332732054226, .44705432791391675, -.3438430309539575, -.6869203088225511, .7987889341861236, .07169753858575234 }, { -.514287686120346, -.39521347553070485, -.1899259069360384, -.8322746339701628, -.6885188796897679, -.011880760516860711, -.8793889287656125, .874417629960804, .06197776258744425, -.15203328684974182, -.3149500789598647, -.010665309330700312, -.7857579841183597, .07123619359630395, .920662146987973, -.8923243261738205 }, { -.17474879712144542, .45105843208605867, .9830147738625172, -.7505987513170427, .0607817182466841, .7628864008384224, -.0905139385571756, -.561375590171951, -.8649565480290462, .2920462698411681, -.06447329120492307, .0656806358769868, -.8815458462048562, .31556912724770125, .9188246318695035, .6810637331467613 }, { .6426068051741092, -.15678179372964984, .6189717735679827, -.9272720788285775, -.7291232406158621, -.09017622094932753, .15202157126284854, -.35270217783210156, -.22418796506333605, -.969229185874624, .8488165307474629, .7938859475079147, .37149571344985066, .6239735572103644, .8458190124727851, -.689535316884861 }, { .985233794133721, -.23712429619532593, -.4113158479999808, -.3066903111286923, .46363579824028633, -.43932800145303186, -.695685844398445, .2624492189287819, .13508646075208008, .5090823982668706, .9031524191489124, -.735817368314998, -.7486565487578416, .9493350282699391, -.48602524936441505, .6094517226031673 }, { .7827064948078581, .10344688692912696, .7678498333556376, -.31557282099548356, .5839366081089252, .26586329928469987, -.7288867250130506, .8951946950273482, .9976267537834904, -.5841412965007178, -.8404588744537724, .6745286976734406, .39912945368475206, -.8968284973715746, .6353435880176932, .7317500860638153 }, { .41419677053442583, -.08004715906138338, -.15767761283546267, -.18467834951024664, -.5474788544104185, -.4333505775517539, -.5746496008370978, .34243089151776585, .6785595666865705, -.8233632551532732, -.9433234802922927, -.25935345164664403, .05677975279846015, .1142399993968457, -.10411426045677441, .4385185673291301 }, { .5963448032868126, -.1690960264872765, -.8919811334357799, -.8272206615759641, .31268475218062064, -.635199744761711, -.1384704817681328, -.08046831400777243, .4974032361791598, .2045310127965141, .3842710843072148, -.7481557596794246, .11422505182985532, -.5370947939358359, .4583970219961413, -.08126934112509065 }, { .8994013148739561, -.019224059074713695, -.9106753729857371, -.1303118506396892, -.4779686020621421, -.6242175506092646, -.9667906059350824, -.32074437478497075, .7285418940116255, .20285532662220707, -.4692530822966532, .10498138340082885, -.27785248433416454, -.20311871690887506, .8281620051823966, .2398118112559764 }, { -.7828116998496202, .21342321372635964, .3974537095921291, -.3696657149815914, -.2487899224411707, -.9270156063766526, -.2283801275750741, -.6903715688462906, -.07654354661712492, -.9731896151142454, -.7780134086166182, .3821412093307903, .9980173695448291, -.0319242021751438, .6705119435423241, -.28460705855729374 }, { .7132567722663237, .4081777969242064, .06420532095854448, -.006750267021661482, .9513570918808871, .4245137233255267, .2169780032789712, -.687559578085156, -.10074765293696464, .5012693517966096, -.12112869226393252, .9725920445230756, -.6819783283934064, .6148566631972558, .9035863679497431, -.9483687579541771 }, { -.7540119821209423, -.6220506682343687, .15817945107042597, .8695328749208937, .616190967867267, -.2072725742079642, .2851447851662823, -.9937707673093681, .5391280636976641, .24214033406216906, .7139958580543566, -.36445669692339666, -.04724143667661318, -.3052210927832333, .2017076730618197, .9634159859740927 }, { -.91207771716454, -.8451508723624956, -.9749216170251946, -.49371941763972127, -.12946551493696146, .7776571012221274, -.11857456664446353, -.6929669321323473, -.47615596644352376, -.6702116706609231, .9032633173513873, -.3952787027244091, -.5554881790020041, -.7570305795683716, -.0044977669207453275, .6128556965588925 }, { -.877384524882993, -.29797466374251846, -.6631854632933945, -.31579608800680226, .25260377815016266, .4080316965699322, -.6326828519124823, -.011329975072524645, -.4513770123474201, .1814887461600927, -.2582466605130236, .6586575688220839, .5042838782818675, .4269798726670484, .600955557288773, .08164707399762694 }, { .5718732640816024, .7506038760268368, .5272902041947827, -.7107813403763903, .4718364642903665, .29618202100113145, -.5803174814428551, -.8970652151733018, -.476663100593157, -.9520439892934429, -.4128994693759349, .027369208681683688, .6537553599443413, .09485387358462338, -.5445369258283888, .759004588066789 }, { -.44928748253924544, -.2077914498376936, .9392594482931971, -.1334204236285359, .08119437395916829, -.8307427703466927, .5913812223924315, .43663231812622283, -.35400490715885957, .5954676057795885, .1358434170339753, .7317104541656925, -.7974977128052478, -.06148536175347186, .9966518702392972, .8305323214077358 }, { .6005657722432289, -.4216434946091603, -.573470076865761, .5488874002314439, .042256191732570114, .6105966311269972, .9768155576044677, -.455766142083194, -.6005111037192625, -.5978917433821545, -.8080416572686802, -.9835407272058625, .2672520933528455, .019493501669517244, .14553081558964243, -.947334443960171 }, { .729881768792201, .7761698926105898, .5095393671779016, -.43827472246398647, .8362346429252072, -.35802896765059344, -.0020566461467974584, .19781603796304315, -.9156164752232472, .42567620486467805, .5507508312224774, -.6456448124747414, .15856088791216316, -.2187949901162316, -.36408116577136096, -.9310685250328126 }, { .5146537553794457, .43671955367203696, -.3245195017656395, .8266529742090374, .6619405912570273, -.35194855916081647, .5762422501160533, -.8194492942863114, -.6828706565592506, -.5608277383744322, .534532235785941, .3466574006086962, .06590726322608753, .36014089874020594, .11687315330200998, .6565376745506277 }, { .40429642076554084, -.14733548362631765, .08373065352583309, .8461667803668729, -.686198283287208, .8615731280709957, .3205905100158304, -.28176278272387223, .6580604358613746, -.6385344564527595, .47184431664967197, .7816612770385858, .5091570020740999, .7481425693385739, -.9642598082410003, .7029890477907583 }, { -.9727198240180226, -.6107250650895244, .6612369772120881, .26252971626599475, -.8050582630528247, .6516320615527256, .07436599794514631, .9241031573436103, .2262194071019703, .9312908002180793, -.3362218922717195, .6674463738313752, .8028776618811495, .3259452963637399, -.8430277510446766, .8569674468127635 }, { -.34480591731925125, -.1880184664055915, .3608650860561833, -.4729002361671242, .6846091386540145, .3030459068027729, .4498559040446788, -.403002077572461, .17239144328666733, .4027481616396529, -.8231692198434963, -.9455515459121271, .968071310944804, .517886714779604, -.43853235991696327, .5453834796703483 }, { .37455116380153686, .5191912406589254, -.6967356351981544, -.4393922409330573, .22222872294696505, -.8649559120062436, .2366727833332949, -.6077349140660186, .15876383679803108, -.20661585212668254, -.06826047256445222, -.6874806224194343, .4510002982527732, .5307246252935209, .49026244384278916, -.9301673573687435 }, { -.11981456438197391, .537857464072921, .13203155001273137, .7526992463944637, -.29777444035742806, -.46757779035591573, -.0926668174187375, -.24035290641577411, .455534127713926, .11066797850904875, .6311642282132215, -.37947345897840723, -.6775984339713776, .5933412121266457, .48331018986913277, .8843478048163051 }, { -.8424764623572618, -.9067996494361263, -.0883609919752959, -.4001448797349505, .6804224112844373, .1579669696119037, -.2635575797083982, .28520796002922055, .6223969774870659, .6389335338986162, .05681214482206287, .05466562952410059, .7427902462010889, -.027449432454302647, -.9353737164247837, .49313034332784356 }, { -.24572826631307865, .5241395369717554, -.2000650509934907, .7114677277423394, .14196432184869057, .6318688424478345, -.15367330945416735, -.07749534321680307, .663388760709589, .853923030887757, .24817935412442105, -.8937643182681918, .09631035596911208, -.725383562074867, .20943106469128403, .27594878812188495 }, { -.2602485931861329, -.9355723692691127, -.955130182167816, .8479708533065411, .48900410848615805, -.1222731532384842, -.2700304725619429, -.7682449085248098, -.7765327181434067, .8045988204969727, -.7725450973653392, .2605200828518559, .9367780549375393, -.2456706824496524, .16326853984899592, .7006515161667808 }, { .22749864131911157, .5693743579521438, -.6230146485982853, -.752395980201872, -.25260769826672913, -.7292202743256415, .2249665999888304, -.7974088433428457, .6780793833304171, -.7906297298014733, .2873530599602052, .4985248938510567, -.41623964160263305, .29447627741596727, .47876174880854316, -.5387842139563372 }, { -.49528182497212847, .8185641152037406, .7202967159858378, .9530706433277292, .6991757720427678, .3584241195201716, -.8071296371104146, .15158198744908846, -.37972293243362465, -.9921274006809835, .4244115643945692, -.5317994676263831, -.47120594613293343, -.7522524029281845, .46855483830904543, .3093340147219399 }, { -.6476471131620865, .04452134671864849, .8929389352285686, -.8477502772717178, .045496146375269664, .5974151375406609, -.11550578411003531, -.6145620965591041, -.4268136766294064, .608821448750031, .24354579637493923, -.31311797280027465, .6995001206205642, -.23593978168630136, -.9050818871684945, .9524752737048736 }, { .3478005634237067, -.7413751629968894, -.08539709697586817, .9030741568763414, .6293202226548653, .7581116730041644, -.21656138013629755, -.2362425086117661, -.003905196938378319, -.4772944635430958, -.4197538967762162, .09017862923257969, .8565339099278793, .022710945030264496, .35046561430109247, -.17912708820118417 }, { -.5708597745830921, .4159518653174821, .5691677810529123, .23890277037189644, .46219682580809174, -.8494329216094916, .06524722703212538, .18567813363475505, .5131260579616441, .726252892714204, -.016051535046121224, -.517924781309985, -.1060595067640977, -.41053168128484363, -.9563424731605004, -.4123324312299248 }, { -.6633171093899559, .5717096035453384, -.8122039610866254, -.3478660220901806, -.6875932924697441, -.3696470031370833, .22703882779414775, -.12260664632076068, -.4642219709622144, .07668004919085014, .9565370097613257, .6082255458528103, -.9734875204345352, .26949123790184193, .042063677395867805, -.2876709235368313 }, { .5153491095736376, -.5285499627350967, .5255827599250646, -.7429641924778712, .4237426541417353, .10414545222411253, .9696072618137399, -.5741319371791336, .7807214003121186, .6006435615044845, .24797840458336928, .03756959395772985, .8150889921397282, .9547842247148863, .4525317656299923, -.7751948292233917 }, { .2960588745496384, -.9882343769268, -.19201058256825698, -.7005804272696694, -.5725933640824183, .12440644041051474, .30740134751673653, .26112572601507034, .2649762735591603, -.04352961032478664, -.9695458268474375, .542102444685469, .7662053171531824, -.2779673877597175, -.10134133388994138, -.01626559939289618 }, { -.3591112625281039, -.17336092561910843, .9379989677272618, .3560639774626806, .38771219303657123, -.6687827748817308, .12216990806523254, .5320538207512806, .7623086735256164, .6090262160995648, -.12153270758539692, -.4313203933268772, .35775364210523586, .3140909888660659, -.7805165007614592, .03348932856656295 }, { .3025155753217641, -.11663089077004374, .15179464844345936, .4276208322105277, -.08222401514260036, .9262437257997091, -.9095188695575847, .38817652800705393, .17439671321705807, .2433758881119239, -.8118576677436742, .9503397192891587, -.09370950256707533, .41153427238874074, .2127280041234998, -.2577038860059009 }, { .9430496161252317, .43116162736510955, -.026655146650214956, .059741523282221154, -.5816912918987727, .6932696900305351, -.4135464617483233, -.2543070716864795, .48372402743528586, -.7973894759591227, -.9314561544332209, -.3376117138028585, .8249348110256558, .01636118621593652, -.6347462812251612, -.5217353871720647 }, { -.029051384521072032, -.6455321899875426, -.4791312920650239, .30449877136657144, -.06622286425983726, .6566464225152002, -.22292068585114855, -.33284235306259435, .6472182175571104, .0002882720723444976, -.7368250869153108, -.03750342076775959, .9313720402177714, -.16917342714099348, -.4446347705890017, .7049383961977271 }, { .29637731234275644, .7556531743872743, .6042754058711377, .40476819380666385, .8689332931310938, .04445318680938959, .44158698666026597, -.263698409700577, -.99547853746713, -.5653657775205665, -.9570490236399356, .8558153983790504, .5003216591917874, -.17444415581938522, -.5297755160893669, -.8560986886442306 }, { .00953250151704732, -.08340963243650257, -.3695769366825523, -.9299270316143313, .1340823302194809, -.5064695648707711, -.12349982268356885, -.850117281962127, .20343379284383722, .6960508360349662, .3548231595554334, -.3123053506356894, -.3508441908643669, .5675252758437159, -.7049920685176991, -.9123359929989243 }, { -.17191482485705323, .3523521497049926, -.7946262243001765, -.6885816028038565, -.32015556900679076, -.24815082580872416, .4807707564529191, .8215873101123325, .9425031540299231, -.8253506911666977, -.3119058409004636, -.24184560671514754, .803013789632105, .5771935203288527, -.2528565678842103, .7407415725394846 }, { .20377163273809296, .33127528850515287, -.6000115327049278, .1531764852778179, .44938808861403645, .2523937645890133, .35740605320137675, -.48611663850371745, -.11508068114490677, .1963460792560383, -.5301133625884069, .9150161649815469, -.33755052548540454, -.0612474511426333, -.043825594536580637, .9782776763456782 }, { -.7324928910562325, .7265219506885372, .46840024127869895, -.28404599231617333, -.4002001757996665, -.16241291527659274, .32599803107267156, -.7822940099145104, -.6903298000623648, .9727837055950943, .940700800026933, .5768736702124553, -.6123192758544431, -.1855447466680682, -.5142168042340673, -.056819561835219856 }, { .8308346582823214, -.6135668352047325, .9214591927691178, -.5880036534493509, .2261032531549727, -.40025865664609817, -.6380549802726514, .02743335104006639, .5871111717584627, -.4540729074333605, -.7983497905424735, -.45532391218161594, .47562012181154256, .6265729895524352, -.39044063723998534, -.5800955948449049 }, { -.32357709240043087, -.8132353996402077, .8928849290122884, .4468087794797244, -.974205403416105, -.5835739748051576, .7921063336546994, -.7139598067571393, .8132012012865792, -.366271632594259, -.14710806697737389, .4099809645146859, -.07790773382833072, .2593031247853892, .06298206316288368, -.7931085828432918 }, { -.8481982369678276, .3604513531464415, -.16726782501022575, -.263463040879818, -.2579220090663994, .9812707429153087, -.939076384459526, -.1250514254173869, .2331855954380604, -.23843542588876754, -.608181515456897, .6582061648073223, .47058918191218924, .39530855599770964, .5956503558400412, .7927918614012157 }, { .0893586907261601, -.5089467608539084, -.6938769420392872, .36524287585858417, .10690794198542908, -.9965377981653813, .7772038152468119, -.14552394844746575, .9073422103688467, .5346954655974303, -.12956334208812903, -.8256482296371599, -.8505574074527322, .19733842875933827, .320438451043106, .3502846758846363 }, { -.7409599266054914, -.6072619411064444, -.8815106358223854, -.5629093003796739, -.5262021985241179, .2554652039406802, .49972321207114123, .7138477724859493, .8404864607264895, .6715428023625623, .044269245072451335, .9087869491594487, -.39927884200996555, .9681730568169464, -.595980267380843, -.018974320397932676 }, { .87541787597221, .284971531864999, .08910797684303073, -.4572174113286247, .8262941035904046, -.13628154462881392, -.9673555568631429, -.5898465764908198, .4198553344975555, .6289825999463476, .07987595021129201, -.013654683416265634, .9333672759717939, -.6055787568281696, -.07256289264228122, .17355160036728057 }, { -.26832608867384344, -.4808941489116285, -.3021009711682885, .7579549725239063, .6326964477004255, -.2172592433614482, -.7575029259844057, .4699438323180676, -.5159151421722532, -.08640561761393162, -.5740697137164961, -.5608931102031736, .18535801912982874, .653104345836615, -.018409042705281653, .2056230142719746 }, { -.41185831737670675, -.09921942011525808, -.7255981089336261, -.4584105164256289, -.546443565590987, .18933637099781153, -.3591964148572646, .9002796412634486, .09587295678967211, .4661736495365647, .7487959930068879, -.7676979706683338, .9050381861639503, .21353800304591997, -.7564277298584507, -.09384936898486562 }, { -.8011624690771924, .2976117833159784, -.43769187322378555, -.04249678936984469, .15873536406207167, -.6466905005643824, -.053607340756927746, -.019256279387279918, -.2399643482470235, -.635338932303136, -.9821177844706903, -.3323151745256008, .27530908912617535, -.9365855938309644, .7124577810290729, -.12226584612024127 }, { -.08458521945471054, .1272539833799149, .6875432733913351, .2729439668604636, .9784985158126587, .8071708087924134, -.5776078582890307, -.1199087908701455, -.6967872966762103, -.8505903399741286, -.2690899731781955, .07294446980485847, -.5892453025351698, -.8241418296770411, -.6914088219015049, .5211839977092187 }, { .5631328366467494, -.6234501437365956, -.5106056949446225, -.9902056813445417, -.6828879398630998, .5154657142635517, -.7167297071049812, .23458787243668633, -.2558184024233181, -.13751547036579526, .5185574523460454, .9402436733397856, .7406010285461446, -.9182083248616875, -.11932027462137729, .5858422394182872 }, { .26836196028900905, -.10323878616543491, .2135704542893364, -.4314580521388922, -.7323527606837215, .6043030009234258, -.9548423157130872, -.4742760925028553, -.024170389830852956, .641412316616456, -.0439604700881413, .769701904632561, -.6469819210835708, .49979598632932554, -.4946622824285165, .5530213534835127 }, { -.2213584766704253, -.5034325104418467, .45934791578691203, -.6109327443698025, -.9207636008229387, .17800010256655452, -.7548717843819668, -.5093896355144389, .10760321364114711, -.5220555532559485, .5820914806799984, -.47619247878598303, .7806327183740653, -.6016851558707537, .4054741698925364, .9617189814482061 }, { .807103382017492, .1535489517215216, .7019512725657264, -.601486602206055, -.13104095111671188, .6306936262023899, .4152990226726996, -.22554346984923912, -.7485583485029694, .1577337408662145, .1751903490312059, .21829592326616254, -.12618892122031355, .1808617789475011, .43971209937432154, .5368557692079252 }, { .5010796194460896, .4674823803930859, .14873864005852622, -.16052242795060012, -.3356350612191463, .9096800571379635, -.690493478793242, -.2762083368489905, -.9113403599087275, .21943245728739869, -.32159252301278696, .4631340497559613, .8789882575561316, .13110481079183045, .46458781547259487, .5670203172446517 }, { .4872555889395431, .23268738332552008, -.9839777959643603, -.7025941711585424, .5377819840660476, .6765080453101429, -.9529567372474712, .05922442525820837, -.004769285411423185, -.07415860709520072, -.9363735968039573, -.4453151043418089, -.07153135245792375, -.7039196138571608, .5114982663955141, -.030913703232240408 }, { .9799382296526478, -.7630596883361775, .07722739876452511, .18755490302053923, .9095466899316105, .8925689674441113, .6382849162247224, .08032004485922495, -.6667178715669184, .9547550075557523, -.2959632633644398, -.7516258564921816, .5864103444585116, -.8823908867073511, -.7771653616006537, -.27886017069949176 }, { -.33156014061129757, -.9196496820229532, .6016972890098347, .23102929851156473, -.7117412860997154, -.5292495825664001, -.4671928303056969, .9304703021762222, -.12116319228123973, .14664211191722765, .912939234292665, -.4764306206687292, -.8869120740753724, .6855589869813277, -.6680068377729034, .10496584238636353 }, { .5226295868465671, -.10319170434472191, -.0032468590033933875, -.8691751326929611, .21362800729941678, -.7809608090191567, -.4467461483525015, -.41852579760344155, -.7504162768614175, -.2644210697807696, -.23022551529047686, -.829141981991868, .19254657383441276, -.5921953153592929, -.5954806709211908, -.7438570810773886 }, { -.9521354440850514, .044773029272035636, .5274200762613388, .7965022271911373, .3012024640800215, .19018500452093945, -.09381658349049316, -.3060314790224594, .15754539649633714, .1277007523555147, -.6232203496178064, -.14820522800959757, -.5463156065476564, -.5774218259862098, .9785086226467836, -.12307004187982606 }, { .6362126062301576, .7611813202194391, -.7087884376859692, .047247891316960944, -.8534775277962423, .88893138769585, .6601871094977732, .3141442551542739, -.6975473786180115, -.992770808549511, .791109645140254, -.6957043652251447, .24976016944207724, -.12173958929962914, .07882281523387813, .07089933681457317 }, { .7216173738807279, .4942272141804369, .7217445213275728, .6913477513187443, -.4984430868982812, .7340192887380264, .34219898540618443, -.09464328447692205, .7526123164836036, .2868054439133294, -.4993693739731324, .962141506612836, .7987435740166526, -.9954438186146779, -.02040188159581935, -.4921385154752864 }, { .8633557691137352, -.8770465215528958, -.5975559367654208, .00733338973869313, .8785293222225581, -.9108672599406629, -.5189130315820087, .24382825629400795, -.9211492065258648, .3443904298962015, -.17024313288444293, .146983259099843, -.7323900263471481, -.520756905927032, -.39155151780592745, .4727680071650162 }, { -.642555379968957, .5833241030401166, .16315365286879935, -.2828654111133515, .24204496693896127, .25931406108291744, -.2913986400268196, .9850389138660998, -.9313857952280156, -.6398546583576072, -.3131594004500482, .4700013032784569, .1905771844914388, -.5128377214320956, -.018717537582910504, .5731204552569651 }, { -.4436685704662402, .7023292086089119, .5190722208502057, .19403923784026422, .5407348270517569, .16754605775012932, -.790305193529723, .5363862506184283, -.5958151910258849, -.3537772110807782, .6792703479716207, .6280077811321729, -.2743879589523379, -.8127959812736201, .4997537201364459, .41678282885460693 }, { -.08900642815727111, .7224461033087273, -.8924112520453693, .2257147785667355, -.43677198892651314, -.6182416358018894, .2889249321619034, -.10759228402027343, -.7460157257498836, -.13384314037501777, -.045255862266448066, .9932419629609857, .6591270744865938, .11346033878200901, .6867750454995656, .15426970625238678 }, { .7646373043159649, -.7613484845875009, .49852902622634776, .05089166391958311, .1285947929921678, -.6326498633007609, -.27684606474087925, -.6213587894909198, .855993257309688, .9518623279672651, -.5623717954114267, .7702576867999813, .19340180594637468, .4959662911628018, -.28268588175053866, .7773929463221336 }, { .5710468838874336, .6850093680629232, -.18021686894190392, -.7903510031843972, .3070814634880241, -.5514976846140498, -.3383304691799014, .6056356235085998, .9514309058181503, -.08890769377566254, -.5846022408452041, .7909813892798114, -.6312871521567365, -.7425823013718946, .10784242545134592, -.8136770809397951 }, { -.28098770298964526, .41623789526992616, -.5334025688738928, .26457041282052796, .2552216642951062, .4789886873893796, -.6375433749542927, .5308096684280252, -.6623245668347273, .27241381012575516, .2157825702298668, .14057296704808842, -.7173051453391217, .35534892119507666, -.04507791551159346, .20479273618232807 }, { .051144193504410795, -.43713351869031336, -.4350361728340768, .8824533804753427, -.8088935110714348, -.5167839637203497, -.8635977010575515, .21888456149336477, .5047949264509495, -.05312063192160976, -.7563581108491073, -.613431918222967, .4032634026561761, .009613251496508823, .31486050498559104, .853517526691659 }, { -.8247998603982416, -.733524594094991, -.3798685752319555, -.9601331660836834, .3775435991554559, -.03169261593754724, -.8504881425354522, -.15272772881513297, -.522959394932156, .7434719950787712, -.03852492043671796, .6573042676808967, -.9557808535840009, .6102146081444098, -.4611946052958329, .6250800304321193 }, { .8881736753443623, -.5146319595849396, -.07577119335293858, -.8466456863886962, .5689803890779215, .7899244069513462, -.4401811779333864, .19690691008978467, -.8194011161686068, .9083511013018883, -.574917937310732, -.6281296578631641, .5340159776866089, -.9889180674887721, -.010434160126111092, .385614547487374 }, { .5382066192699912, .07917605946014517, -.09375611272277729, -.2482373630437762, .42780945981307084, .7801760374008215, -.5784616445428021, -.4003597510214727, -.5730476258264783, -.433204044684657, .3758720614380324, -.22439054217594068, .24124052715860445, -.17574756638972544, .5273247824195313, -.9969528888461467 }, { .09802760782743891, .2424435519632675, .7928378140715169, -.3313767520952764, -.596158935320221, -.5379838739917182, .656698768330817, .6952150114605136, -.5190078887095944, -.703882997517445, .8606462631840288, -.2670198769634142, .2788327672509847, -.2730291791407986, .18299280203147306, -.8360052616651723 }, { -.8723398588494833, -.758412376673335, -.4720642873059806, -.316099005361022, .9140488085237917, .9199083721208543, .019750891781373126, -.06703902613542478, .008774696602006804, -.5525138599837363, .8840598303673286, -.2440183421460531, .977554720616385, -.832551689397111, -.5758829204681362, -.43045042806968614 }, { .5208051276065169, .2627314761625177, .7076382606818128, -.9870270679534054, .795780061582545, -.9892140143553125, -.24971681386332567, .09280866059576254, -.5566650767708281, .13473177976625905, .018221790339357335, .4491347969504784, -.3687197442472203, .057268010762667076, .46949951094076536, .4421335285513628 }, { .6382763963502198, -.12896962859568672, .6314749979189209, .8030045400159498, .050243670001933305, -.9004166364704316, -.3856499764721957, .8605378089823446, -.20682268461851838, .6371419325825454, .5430873705442496, .7951524631924958, .02045189063745556, -.4881342331324192, -.9095046134684088, -.2220798816716525 }, { .5299185177526387, -.16494857828256082, -.20340640547692002, .8833964547595761, .8649601607752302, -.616965115137005, -.4583594770189743, .005480162026620494, .8770931698390845, .6094733028028467, -.7871360066478328, .16247132752094662, -.3467776699804379, .31476746529006516, -.87456271632394, .5680433330771024 }, { -.4268185530425812, .1676453882861184, -.5408744848620108, .29130281877062525, .7863053783881282, -.8454106167613755, -.7023871256854166, .44301303620412646, .021108905725560456, .19171061828885994, .3446671859303254, -.11637766212440415, .14057080912375297, -.8056108460601974, .5379934876699464, .6872713756518596 }, { -.8564106438510104, -.035142239457909064, .36399341149011466, -.9407702469828583, -.8714263886806559, .5638957954552453, -.9044757947506938, -.3537750673256572, -.9104851885942278, .6962905604094616, -.40035798528582234, -.46793594383679427, -.6396629899606996, -.4016057975737568, .10826714764083722, -.7443987544126802 }, { -.4832432100154178, -.6411658263364803, .8245952832668488, -.505200313660727, .9573620783082382, .1838200859872441, -.45368810406016546, -.5211633306352614, .7807585661497913, -.6241905440644353, -.8733104728240886, -.05806277660478698, .48868737802178375, .5097164246930173, -.4649370530874022, .2587007988723775 }, { .35251154299399956, -.9635103381792838, .26087792328099146, .5799915555013766, -.49781185510421144, -.9869156875619032, -.3045319943969298, -.8585652553929544, .8981881056956527, -.2030165640723507, .5200260032534645, .015665001611413487, .551543043534646, -.4865840171028595, .02999674649067474, .3146084392967132 }, { -.8983756615199343, .05008881958725331, .4479976131279353, .6515279135574834, -.4784407984318064, .7296479729027707, -.5182960653219635, -.3408196363900873, .4834826775159744, .046041543949616015, -.18536913230727103, -.9544653466312594, -.7324187388437826, .4903334239283226, -.9012356266809354, .20338764523891184 }, { -.7826974916629399, .8578548391096374, .2154159188519431, -.924893553061342, -.7279276643919528, -.98803064029074, -.3126985556631132, .4971977481942358, -.3634396828935791, .35389293764356244, -.2747709876538329, -.7513140823442535, .47817945466465517, .09288339610608043, -.15721737160497784, -.16598886354865838 }, { .07790215023903824, .21863169034143537, .09931708950622276, -.8822579192340689, .5512440784819093, .8612570344868755, -.19025493473415556, .09129853099049678, -.39471051295349646, .7623595787480184, .20981889458750214, .031467078795299974, .8031148778912309, .7371838731868041, .2500171151491526, .3231704142284155 }, { -.455736639435276, -.04387970765438265, .10122502633946406, -.4275921532877327, .600777631990181, -.977141772779736, -.4386948436119915, .2904984283977037, .4896072104154603, -.8414194972383604, -.4861440120445819, .12226281709549247, .6535328309319253, -.3212677212677053, -.2747590124440127, .520647307395494 }, { -.3562934483225526, -.8738015780930692, -.8943765501677798, .5285917723533753, -.5952589534525197, -.5769547385690541, .4583138726546505, -.721281985439564, -.4538210549574986, .6798468247386074, .29688072261047527, .5604552680939807, -.3764030390932873, .15371927268925423, -.623758297824877, .1582465018662238 }, { .7614102890569341, .9178731739254506, -.9257645623946691, .2685838435890391, -.44209581871964, -.1821615769857936, .2787618654666073, .4339531067963853, .2534992555301956, -.12377079245183786, -.22355160536427943, .6129245879759484, .19688450223567022, -.39819597456108813, .25758219778890723, .5455580935585422 }, { .16160029072634652, .21585997161822434, -.4072300000121185, -.27341171185626023, -.5494039076343071, .32186333241694043, -.9439281050817201, -.8833604370850512, -.4831422472664719, .016945303580179427, -.648591847740672, -.721540696000937, -.3993719122704331, -.44774698037237304, -.7952475321819341, -.6430164018478537 }, { -.8282610463152644, .5688464927412538, .7254131565461277, -.02841203665904435, .291949647787749, .5525727907030675, -.4652143713956265, -.018105946645288196, .312020969138485, .6313780163661635, -.4589772729568433, .6727911060074909, .148882752891069, .6208913261692586, .29617832360418817, .6433671381483634 }, { .5262084713721227, .4104393973486147, -.9539313832630869, -.46198299152019096, -.5717499353258764, -.2655921565928536, .39179655674071356, .6015112143407579, .5321639964827556, -.07472083686956044, -.7977560444178535, -.737295295809373, .8172359902784201, .3385736942712101, .2193755496799903, .05762648975928819 }, { -.7209582321318211, .8001247055126046, -.5368263306844951, -.41471863362208006, .3928310434978648, .29947599040728723, .6701024133734326, -.16186065303568897, -.4643956638717084, -.7727078237613774, .4793749086050971, -.4471665142309691, -.05896936848811496, .9295594891053502, -.43355183432604116, .9839913522861612 }, { -.08748168511982368, -.9517332589235472, .8196868492229903, .5411116443230621, -.20940543607155315, -.8492139040017777, -.5516546432956384, -.127054975521796, -.7448923072127129, .959441640010126, -.931950903662873, .06859499884209908, .844631240993895, -.012941850809669697, .3101734791916464, -.8751113651962694 }, { -.13139631178633815, .04696937003262147, -.8170839657127444, -.8957904152552902, .17924549248814814, .789557589586737, -.2837417184675579, .9406090672003193, .5154048671902027, -.2716421777965192, -.18007568512462013, .46613361995095093, -.2795037324071805, -.4872510295066317, .4028429677836671, -.5956337680655102 }, { .8613810793517673, -.9650369124312446, .5839212702859762, .9914566661285795, .7857461857164858, -.7569304492003865, -.852290967778037, -.455467749812843, -.8359259437425006, .5325726829174364, .07327006476041653, -.8864365750544332, -.2392592379658074, -.8810111079974321, .9174197014442422, .9299633161020584 }, { .460882738564967, .10500699761844934, .7138376521316283, -.6004314617427653, .6101518055242381, -.09174656129786274, .9488778310155817, .8386633600341795, .9120913569003934, -.7616492376988668, -.9664093917992078, -.2146160763566547, -.2813043330007834, -.21529463145305638, -.2211649152485058, .8169556064532904 }, { .19462836723604893, .011019465007169105, -.2724870408934912, -.6140217609097729, -.021148761453257503, -.8346167590986233, -.81542165575249, .7021513578223237, -.16883055610845, -.5911430181781414, .7616628369317486, .6546288359020251, .6327177923496119, .5123908908383883, .8790249082258315, -.8863945840390475 }, { -.7339242923030818, .1385428070561685, -.5525502196058745, .8843976103590332, .9237489267895949, -.09924387604066154, .8286971791674829, .49908245780533855, -.8903808417426746, -.6205694075588561, .4867650231760918, -.871193389850587, -.07812963143059659, .3752636487423813, -.7802419254578623, .541382833297217 }, { -.7884120067902574, -.5164767626055846, .7720233820121716, .9974113601410848, .412250498251278, -.9245465848969172, -.12599657745510862, -.7012940416307791, -.056642308524073925, .2586912176477225, -.08704606471315968, -.002569960113024683, -.8234241605642245, .6759141990315738, -.8140487240103551, .13572993411725354 }, { .19344717443907178, -.6493120908326242, .8723829419150715, -.48602232644767307, -.3575818263776058, .22304517631035226, .28448384555464457, -.7611740102980258, -.8733768503588533, -.7116754762127353, .36445905705462933, -.20809024153245548, .784033011317198, .6828976585992466, -.7277114319993112, .7260527668915722 }, { .585039096270465, -.4107844108978975, -.43545529984089715, -.9386393172301546, .49559732280507385, -.0969206681200725, .6295337707369275, -.13218731009531304, .8322203206599716, .2475167565703167, .6670946623405427, -.8230298722920324, -.3285925378353891, .9154846819638172, .0868514315758997, -.4433037125635235 }, { -.6699777738055666, .5348040494866517, -.9116297149436816, .7427074809863559, -.0426307269810704, .8999699799378234, -.48113128511287706, .6200931832763741, .2047476839347604, .16675080627834693, -.38110403253682246, -.893781298573709, .2657452670900715, -.793411795005762, .28880353316228424, .8018527921541356 }, { .8440226769424728, .816602769225693, -.6329334625059844, .519629824015353, .3828512650756173, -.8605226900457488, .8107350189797231, .8540368712648532, .7756247733277972, -.47864564251018904, -.8895335136651583, -.5904808211730779, .7517346342788824, -.9353683116520428, .2528540283987566, .8309503853088991 }, { .4791544533126946, -.8742112858433506, -.322062319081027, -.5449307819835723, -.9827290783824114, -.9199399204459453, -.6533339111673238, .2947994363015829, -.37666849689620596, .17810016410342921, -.13969164022882796, .6069269633823191, -.1941526544847263, .7120666793971202, .42882797071773093, -.8152526171235253 }, { -.8325272478366963, -.23807983570626434, .0218782867405658, -.989026950212099, .7233698482727717, -.6116093040783519, .08779380706410689, -.4788929207413053, -.45154457776572543, .05363389103878813, -.4525586649744231, -.5904492562255299, -.3244874403522777, .2928594840882133, -.17559301661370297, .5543582104219473 }, { .8866978431200463, .7799693161810153, -.5013521437382416, .3761176741959935, .2615919532692965, -.06026895697576373, -.7258871606419037, .05489677759142886, .016171356366605938, -.6958909037827086, -.9431640222199997, .18573533479867765, .7003549006198682, -.49070133622781964, .04017050647912623, .10467436633300742 }, { -.059413616221860366, .9608607497099304, -.746144112979648, .10811432247614317, -.4659585651423386, .8184208071621848, .0048636872860152724, .5856768026964034, .6691717122012013, .6329863072651474, .962488075191428, .6953050504556815, -.9028595665848842, -.699119177471198, .8233682805736298, .8066304243090041 }, { -.51200069868156, .7505783221090285, -.5218710968480575, .08088288252481113, .47982884408201154, .2596392024553049, -.46738074916980166, .3270138546042247, -.6381446809024098, .012507376823676974, -.617595607739116, .813368396223701, -.7112648056646607, -.6985773113254745, .35021283439776774, .13454397087318726 }, { -.7714070053708999, .07576327139058026, .5622926994471056, .8987798728643104, -.8358300033769352, .440133805491953, .11361420495670549, -.4972484184786381, -.9473519730382711, -.17708997052547382, .8459130292808732, .8943143440819683, -.39134560125022655, -.8254521680491487, .541464867009583, .08387727154249736 }, { -.6822204880201055, .26885223501755595, -.8676870845865539, -.7852559996874846, -.923226390650175, -.2485451072167546, .38345630566301936, -.739728161089636, -.40489452715029284, -.13097873800178705, .005141463123055079, -.5808968920918942, .8681181736656063, .7392836063454782, -.6304126861617527, -.7595103303387074 }, { -.014540601006894915, -.651027333256404, -.19144816228292827, -.7165888894422217, -.737136502820529, .49260103037536496, -.7493277197725803, -.0788047754595469, -.658593092698329, -.7400860458054008, .4780332235377591, .1686100690669059, .720204747777081, .9872552077104113, .8397339906264965, -.03968310390086893 }, { -.04402256659143067, -.9576463883434898, .8722350966866697, -.7218656741056946, .7936764733760155, -.5248452953357687, .00638549089755025, -.8361099875994571, .17153965651953085, -.8400694457559075, .9673375179453163, .21334043288914484, -.9447754249138458, -.10489667039124662, -.744467630412109, .7149293396109839 }, { -.5609108681428236, -.26437819880165203, .34773611817327255, -.915222081527419, .9212941807822324, -.36608872915114654, .5475223459126148, .7145798538716919, -.49332842610272376, -.9904014510264527, -.7382682807580081, .16612416828253362, .3321696912719674, .5208537580394763, .4269845850761853, .7378558975252985 }, { -.7307274186197408, .07609546613971618, -.7291562541450474, -.9248993353599639, .18839884383548555, -.8634989426572006, -.17251438156222676, .5932075120801377, .41001032638079393, .7701776456129732, -.9681691300811552, .2685971287611657, .9105609000081489, .7596412521079543, -.1457187018953987, -.36728790285786306 }, { .6042632141236348, .586782915851729, .5411831501352893, -.72130379123195, -.14213592735246072, .4174329389229001, .9095737614499191, -.5879795604683216, -.29174277906377544, -.9397020916975656, -.14077315067161322, -.20024514279277295, -.8111177217610164, .011788626565347293, .4229866648502598, .4630931723740068 }, { .8177344994096754, -.8460583175148517, -.39892620441033877, .8736689083088471, -.6737843119600329, .35560348506293993, -.3127458406148642, -.16493254739975227, -.6716689298956136, -.6141647696460204, -.562789245388202, -.1622307426245977, -.21104199213776642, .621619763192701, -.13686475547349675, .777191137181612 }, { -.6454122271010749, .2531605033730253, .8881082455230125, -.832130845044601, .4131530636998324, -.4940477587808183, -.1769070617598021, -.5580746063586881, -.8695763500107558, .008951489617008646, -.5765798906356143, .09278137065046388, .6150452108380235, .5111429920825488, .7963164992061749, .15621359923867595 }, { .7733518484638586, -.7853158739931247, .3661775932609703, -.15750279992873417, -.0931077596958112, .4886015766296765, .7467929257511428, .35834425548634563, .2923105073325962, .22669317545682977, .639416401077856, .49273042673397804, .47842563585495657, .719681323133808, -.5836642208536593, .04632841981382563 }, { -.05193755038939307, .6881776408743456, .2914978388790528, -.20700888115777993, .5698190706958777, .07977910223305074, .4332060962038511, -.9382858069345348, -.011095838617189768, -.6800800238942373, .06475449262215949, .9285913677959223, .32666745119965235, .7750481217216749, .3568685590508671, .6584310749894546 }, { .21826551868560773, -.91207471100808, .5277369083072339, .4051321038923461, .5690486712467142, -.21039895453254376, .4585764945169113, -.8053811336203995, .8606304362519923, -.2867753180859087, .2538469610606975, .9027088519866149, .12554530318631474, .46059474021771685, .46966681071188043, -.03824818705336197 }, { .4095949033779922, .9646702419695778, .3236057131654919, .5798446403594741, .5206430144103416, .9132218494158195, .3110427925459256, -.9517647661625717, .909070175105464, .7125868079560218, .5828984081396638, -.12565423759113625, .8136192633910959, .4480361133404953, .7270742606451834, -.16796189960115226 }, { .2648585649910775, .8479350294430121, .9571250887555101, -.2613205954749116, .19761593416798462, -.3367629324802326, .005180537776552363, -.4484478310592621, .35383176118113346, .43438497297332956, -.770229887813765, -.1674500871208613, -.9444913350626987, .5150075548716848, .019080659309832493, -.40170071997541323 }, { -.807984387414175, -.9264719565627366, -.7325166843832813, -.9337294021366258, -.501167052142482, .178693041687088, -.728657183915242, .12804949227825757, .2297322870462204, -.03838634667339269, -.7737564453558365, .19025035968022497, .13849520960160233, .9812412296685238, .6482293788379667, -.22243477515863797 }, { -.9256795949071064, .8448421131947337, -.6320157443849572, .5124954011881644, .5350738245342317, -.2843911510125403, .7398146278065287, -.14066623380932897, .60506874178757, .8418338414523563, .15571281833021544, -.5600855765220827, -.5075348686625538, .201590100309462, -.1443572059804139, -.8349829136457683 }, { -.77072745429438, .13360270751902448, .011946346246975548, .015011822356105764, -.40439361244921335, .9101649745072353, .392224062217706, -.23757257099164897, .4808918276353844, -.6218016773310471, -.27721949381073174, .17310129100686167, -.5316432582331236, -.17421082425691203, .385409640068922, .9926195688559594 }, { .714373269859987, -.9295045814110772, -.288704885029647, -.16350597046297644, .9931705617387734, -.08894657639406311, .11026061991698555, -.2671492371944524, .906127902392396, -.9087582481852048, .6680046615469668, -.16593629954888756, -.22204324455895952, -.1103892575584895, .65585919772848, .5035416288785635 }, { -.7801685095765711, .6886050528332623, -.8313370692166486, .35795063010838724, .9651749418555409, -.5075086920765386, -.1252249882464278, .38825375627879843, -.7614737751401359, -.8662769571832181, -.26166128128454047, .5186281881334953, .6034698244827439, .40087858115161357, -.5277096797366738, .5449406898425333 }, { .7533421631387893, .1464835792185739, -.5462085961215022, .2353693162416879, .6152029401454944, .5317001982842495, .39098367680767887, -.6555192144805224, .8119558097928388, -.5657218609179107, -.9518975075665546, .24860610054277243, .7027248980536642, .699800686759894, .927970230564539, -.2909975595817722 }, { -.27676739395320804, .15600832324863978, .21767674529570735, -.4423642198352795, -.09195389741247029, .22269796168957057, .21742076304624613, -.17834499393830727, .14677288612311834, -.6839582160537772, -.30326521296529485, -.49459826297559784, -.37740883582214413, .1174960955306843, -.7315572316324548, .268974137862414 }, { .3390455917423951, -.9779365093747776, .1236511494786896, -.6750814094849831, .6804337709192974, .404671957498667, -.15059290787340096, .48742916816569704, .33858384801654995, -.12217479687298893, .5557918649924665, -.364836547582744, .7841271129677829, -.4226960199493519, .7865851212504018, -.7729675924717674 }, { -.8818278460288065, -.7502730806790743, -.26626476806260757, -.7401164371199871, .7471375983825375, -.28711104305266, -.8626825128907614, .2362766782944814, -.5571447339456834, .037898491134244594, -.9556963451280256, -.9495038521570156, -.33740990782280145, -.6601771045158891, -.31040110679459865, -.33742470626175125 }, { .9325623746842346, -.7788504889206558, .10872950158409234, -.3165533430374552, -.040862926711142666, .5279416680878894, .8395510721405148, -.8077335554281706, -.37618571791026567, .7351112252557035, .4926037997770041, -.1510716891327195, .16104751913909987, -.7104055905394913, .017062035232092487, .8919787341465713 }, { -.49181306229165944, .8687794975929921, -.6324866080449771, .4652875693481746, -.3456340531881219, -.5209148237523076, -.8391960313224605, .8440133398779679, .05804245200432123, .9947295321806937, .9495292669840179, -.33333498124156646, -.6167690279743709, -.4386405875352748, -.25382704629311204, .4878024578791449 }, { .7043617481021951, -.10456437660993334, .18715267370021005, .45248121322648727, -.9948612182626961, -.8339547535678165, .8586342085737269, .5603446912330969, .7484086972090624, .7939393987999732, -.12559893353708929, .19172772436192131, -.4178188292595961, .0005563774681840439, -.8063381103490921, -.8663678959155292 }, { .4730579925777614, -.8340760173019615, -.6782831450117666, -.0300468555274771, -.6372168457858007, -.40614170082824597, -.5333337803391609, .7263731037882737, .17804462926510145, -.2429517692524068, -.7749237105550808, .1993245843400404, -.6317227224189657, -.9313585787292589, .26529975535107764, .793634360542963 }, { .48527771207236414, -.9167597726160033, .9119940168405409, .7031643365114189, .12248404598455198, -.7914671922664394, -.7331479366286933, -.7020369802651749, -.3588006660932179, .04897167529122237, .05619613956890013, .24932079693258036, .2572247485168704, .9028570013678803, -.5175210715128664, -.9687426727031094 }, { .9381321446590871, .34964620755806597, .32178943927632186, .8288843823852996, -.618049018166646, .8685218806169206, .6809032722307982, -.6979098309734131, .6478680217010846, -.24613855367030935, .2609988645451995, .625385701347023, -.6172288893561075, -.8728838702579227, -.4525089744634996, -.3739068845653206 }, { -.976889988197787, -.22385829151467784, -.17957039715329204, -.5004960638581812, .2996568422068717, -.15378590693228533, .29566684209973504, .04462679243635037, -.9807869146209638, -.9722422112906604, .3758091171869309, -.841266538656049, -.2709781009078436, .7451581803804022, -.3993448722330166, -.06341828038343666 }, { .41112155814089757, .34601436692384624, .35952370914766507, -.558377354968308, -.9472104108436938, -.7172943433464765, .5056995378778919, -.9632339863607384, .7591349931818696, .13548987177558858, .8934065788190353, .2647102403842545, -.03908430504609095, .58514963440779, -.11759643924406804, .09653130638816898 }, { -.3138409112131231, .7821725107946171, .1974916963829525, -.43728941828337353, .7901526131703271, -.6810065247110348, -.3266280699111308, -.21821906709214578, -.32696211471406733, -.3887043963936585, .3786640823662475, .3312037821079026, -.9258067576907048, .6887908186863476, .6686214532304391, -.8734455024867938 }, { .8655946529787275, -.872578969625124, -.2539345565871063, .2117428494630369, .2959682114821123, -.10518919256226922, .969167038986126, -.9911950717421445, .32669323558762065, -.6889043558632992, .20011139027477132, -.12297984678676932, .32352535640849167, -.08156479640248193, -.9584121317574203, -.34362322556335534 }, { -.26494919532464123, .20450184468498822, -.1180841275956186, -.5239227893740168, .40243068560554973, .518807263927056, .5292869405679708, .9286431662755337, .8610343161089216, .5388254268412205, .40031703670536056, .18191496816764774, -.6748565792456842, -.8286238428278323, -.5841976987402093, .7320160789593682 }, { .41785824730942034, .29821924024353397, .1808829192439847, -.11902094910048366, -.43826945457660815, -.7866383428666466, .5489973980786262, -.6749935658829185, .368792621589638, -.7269875415713583, -.3535981847494989, .8218886328961801, .5102509426565429, .8983304520804727, .2238435766618878, .27288041649196115 }, { -.492668484302057, .2806263215782123, -.7533181754309908, -.5376642379550012, .2576791044319868, -.6500923984492726, .33252435050813167, .16234555274490536, .4132360690076051, .9982775351488904, -.5558183942575188, -.948379648233926, .42871983493089827, .36651643383481525, .2903541093608626, -.23754984512388644 }, { .965692290053825, .5898115440129799, .0755616441367124, .14237225830913447, .41591801074686185, -.17319629845270668, -.7287658278022828, .3308789437181072, -.8889958788208123, .5258664424860566, -.5536878569627914, -.6791087644831211, .5305415938612383, -.5141578956213861, .41724600151354907, -.9403964468314066 }, { .21184131712718068, .7519883239492078, -.6689086766202619, -.7212388555092499, .4864849908754556, -.5546609178303992, -.6093292091329314, -.8630901717135742, .6591690221048803, .8261115853972498, .189808314350701, .21609463474997503, -.7159214903617626, .42997271688649263, .17489596666496454, .11602722663089704 }, { .5791339967623708, -.3999870286617426, -.12377345104335347, -.2085694177672459, .5206465855149547, .9900471643701048, .5098928991475642, -.8497175156014196, -.2493605020491818, .5839491855673038, -.5695297470305758, .710167657639805, -.5079445311679698, .2924402146108953, .19067080213294862, .2371918287537651 }, { -.44664306377489904, .7623705099820213, -.4687204304952304, -.7992045573474331, -.16796153574878292, .3264701043380327, -.9698259034927474, -.002914382531655768, -.8904334530143314, .12501208522343665, .1498164737055816, -.557370842819948, .3965606476118684, -.19073344796312597, -.6286128622805773, -.9811400859277564 }, { .22289689452296013, -.23851336949738955, .5175645109683047, -.41857993509113234, -.410318810233546, .5910332983268414, .3170341628947073, .6922306251140651, -.364200356998178, .10668986239883904, -.8356652671523823, -.5515711758672, -.44268889228648667, .6168162692405066, -.40290048466110573, -.29724632296319475 }, { .11947469598141969, -.3392494153273007, .156137719656817, .10937218224834311, -.3725772494913844, .758965283723608, -.24868218934170394, -.5664733162754259, -.6843243987411363, .765764155752878, .3720091004039794, -.2793851291180849, -.5412217431888431, -.10818886440299602, -.44632213455080993, .9278127248042145 }, { -1.2366674636043842, .3290782943141936, .65553879421915, -1.7590764241959231, -1.1647245864413902, .9238032867672133, -1.817021176894208, -.4048286037381261, -.7385396871458498, 2.5537521222114954, -.8781748598258617, -.4661781641623857, 1.7314614094539917, -.496308006840388, 1.1819348122610567, 1.015440410168407 }, { -1.2118657623654985, -.13890756986573144, .5532702500565683, -.519780017906592, -1.3044114281439962, -.011269970282332816, -1.014058864260016, -.5431435629393456, -1.3925785984283405, 3.2883987590847354, .566011974455625, -.858117683329738, .9179939478389041, .23676491031707256, 1.4069891181267855, .4911025977853336 }, { -1.0682309814655175, -.33784116862071567, .32966146905529253, .46388998017121497, -.37221648222722603, .07814284645035875, -1.3891701685311146, -1.5064948229146637, -1.8525904332133034, 2.1350355688224885, -.27817497633773036, -.5513785786016913, .7462929769768577, .9944476945073023, .35247555357879357, 1.2451539081482053 }, { -.6578004199798089, 2.592117109128363, -.5384445802913198, 1.960642370967316, -.5492134946223934, .3652951338696087, -.670591443964783, -1.5708187596896155, -2.0887277677285483, .7229955970215378, -.8802886031101024, -.22915660667746365, 1.2728894925310719, -.5525391958930782, 1.756999322522636, -.40214817680723186 }, { -1.7210584147742791, .26659329826433026, -1.8439406911934668, 1.248835602680788, .41032227568855756, 1.84792018288586, -.24362284117112093, -.10862055281598348, -2.4991896956119657, 2.1730006022856108, -1.5195555867258188, -.3475733705121883, -.23841647048500983, -.13753079086380787, .9080501895230852, -1.0592610436126773 }, { .2685201262611408, -.6603817095394744, -1.6099761557392955, 2.133091684883343, -3.61054034509242, .09451584075631506, -2.1050882569588047, -.6570213498701686, -3.1597406117433873, 1.8981863659905835, -2.1648129775068297, .26080776197668076, -.47476492924964864, .3136845097756379, 2.2200461806002405, .5430876814884773 }, { 1.4818617158916834, -.050401335538619085, -1.0608193255196974, .9823239230230374, -3.218159083549299, -.9263343642397975, -.40964612242523035, .18310853357612375, -4.37795093518551, 2.2139378574685145, -1.6173256311048396, 2.3310857321353473, -.3499239031078341, -.8911767175269704, 1.1036111308199155, .32689178603595836 }, { 2.1392403417209356, -.22352207721604045, -1.27064293447539, -.9293136140978105, -3.3400178554803728, .36641239288504973, -.2942346968887879, -1.1892307354948077, -3.688605541172148, 2.7955054230245784, -.4892942305500611, .7475777897150613, -1.298783414681519, -.590037754963657, 2.101548289221977, 1.0652159766031701 }, { -.1762335629977984, 1.262752728216908, .8288907829113138, .04414187083838105, -1.4367963971198507, .2379586953194135, .004327047399467232, -1.9479881566884094, -2.0150193683184487, -1.4452228882397709, -.06237769805815415, -.8992091998812856, 3.2704027799956936, -.29394188232184637, 2.7915514170314193, .9356914050330695 }, { -1.5347517946960405, .8968857808441514, -1.2934866264407214, 2.66928011020765, -1.5346103510093083, .4877171140102571, -1.3230491632380899, -.5547472846115387, -1.3914856541740568, 1.9081114411175466, .5603758453702242, -1.0913703006648339, .7350932234900442, .11721992867066827, .3953243114260611, .5607344955402387 }, { -3.1870628324501475, -1.1252244888637588, .09956880912342006, .6254930941663696, -.49469483150915444, .4890976204405784, -2.910934107016058, -1.3187377084851704, -1.523504990668427, 1.4713029301244807, -1.0945191921687334, -.1619969133705598, 1.8730206493379091, -.09763366766578338, 1.3578805124162994, 1.3057887187213884 }, { -2.1040432399131688, -1.2530264724997247, -1.888054350013731, .18821140604876346, -.05334597899230479, .39966501771061197, -.24484262410776653, -.8780540483103492, -1.4624740997441699, .3737710012197028, -.3165221664768659, .8097410489799198, 1.4068040397429784, .30578696608801476, 1.233861569978907, .6142094328654311 }, { -1.925569178500097, -1.4835605426169975, -2.527924860194111, .8230556636867709, .24827673526169902, .26215275158648177, -1.4066689713509226, -.6628626137152172, -.8039188597902844, 1.689493477976678, -1.4097957693420382, 1.9429795206014047, 1.0087600852754481, -.8886016501734822, 3.114963933215328, -1.8976870751239345 }, { -1.8125399220036547, .04504154332046573, -2.189646526693731, .8505649637964936, -1.1171342565459048, .5525407768478107, -.6727320135845305, -1.6759878036759823, -.25743900125810737, 1.688420703671515, -2.0662493495794068, 1.3978949241692624, -.47111949421309046, -.6868354845539426, 1.7610030043729787, .18702709109327073 }, { -.02946365045178999, .238609243919564, -2.5015800901975678, 1.1799513836243674, -1.1602348083726675, .4392486705611055, -1.276218680791236, .37882070929382555, -1.5650548903354313, 1.9930884888846279, -1.5913943260874304, 2.146926447480799, -.741676645815873, -.7009550740617831, 1.8082462080431763, .19367288774267413 }, { 1.8088863295425914, .3279580164554364, -1.7883354093063584, 1.5672852874176206, -2.8870087952681875, -.06128056689977133, -.8725730039837308, -1.1480019367941574, -1.9800732526285885, 1.7155809284049728, -2.3489394823786203, .9572486338832117, -1.0209809260976812, .44458807395478533, -.41171863290031246, .4333575081222175 }, { -.810953207190927, 1.6362237463169649, .9254313251488314, .35170973864888433, -.7794508327876386, .0971583203496262, -1.275735542003774, -.8526730997770029, -.3728520665750569, -.6503000771032575, .833873882897552, -2.059936878909389, 1.7898058590255987, -.8234779113876222, .5839242222367866, .9879426484348748 }, { -.08003820661933861, -.5419755571286946, .8149617672266485, .2676087536955762, -.4698435519061299, .4175189392263379, .10713071031998943, -.5245046692443125, -.1114399597068123, -1.5300640116713897, 1.5114982091300715, -1.1967093430083324, 2.57979571015403, -1.621998714248168, 2.0353205660342977, -.11488122752968438 }, { -.9362922955370284, -.037275197481126794, .6789360858530211, .9988098679798004, -.7189094803860868, .4006132835631966, -2.0294375243135256, -1.873813348977775, 1.3980634805351608, .18159540113582956, -.15890117229697806, -.6636302782422804, 1.0219015920304804, -.8040177307971518, .5707204598444647, .10512780551583212 }, { -1.014448723165526, -.8516632657087198, -.9273507324915907, 1.202861722977449, -.7186362326086071, .09860659256070398, -1.53068178976319, .20466080360782862, .05710302132322684, -.2564141192348107, -2.260435460827744, -.12647837986478916, 1.4785251753246058, -1.091555171081931, 1.3341238457613442, -.6324506102978409 }, { -.7145299520569804, 1.2769852454895136, -2.4361913409485405, .4247383000435461, 1.30828438504798, .43541733706057123, -.6671430080460238, -.904597489982756, .922545412405012, .6368182872990752, -2.10900708671598, 1.8356305505566386, -.20787298139683066, .15196104876786617, -.10201868862632325, 1.2985008508429465 }, { -1.8728369041418274, .8049579730168872, -1.7337163498740462, .8717504534492472, .8334377495477323, 1.4594249724782826, -.902672145751926, -1.2255938505353963, .5913083690080705, 1.0445661939632203, -3.200190636250972, 1.5689796416461712, -.34487160328021915, -.32905374049265745, .728095457910497, .40165981529466377 }, { -.19776722422913967, 1.6039543905131328, -.03966129835663022, .20018229619726952, -.7555563059015107, -.04353476730765572, -.7241971145251052, -.8829728005889098, .5696197711702313, .23414207205943915, -.9168175379746163, .9384218188880822, -.7553113711329263, .08343835901618203, 1.6145354873019053, .6771342367499591 }, { 1.6268760462864489, 1.3120362442675486, -1.5254312644553178, -.4005882801992088, -.2363856237598339, -.5220608050527887, -.5570768858787127, .004266922101161583, -1.1965302182083137, .10357307148548031, -3.0895866423474962, .6793197434065016, .04815359113867438, .4722840967523534, 1.003195196689505, 1.1046063608548513 }, { -.5760924176513981, 1.333323881553566, 1.3065199438517565, -.946351773003111, -.43732716678166705, .0025662240336355602, -.21699749112133648, -1.2907089683679216, -.012959707833271909, -2.4914574949911343, 2.3700910884864244, -1.215846514007624, 1.5925709959976952, -.365249024679922, -.23334503491338113, .6398833092055654 }, { 1.8622253287594155, .8647314672163094, .20605259273933266, .015666938467920385, -.8779821114315495, .7090306849639776, -.24407168095289128, .660544517805103, .610329544654185, .08459158104349823, 1.372057822458828, -2.411624949044511, 1.0091206173207423, -.17039575698487924, -2.0038860805723746, .4649711605715587 }, { -.4356071516564834, .8935714624447975, 1.761337238612825, .6470100084285906, -.9820104767438013, -.408437103224411, .21881231436523307, 1.048649875281566, 2.002468961757252, -.4115517555716949, 1.2564138147307928, -.1742289157905331, 1.1606214905569818, -.21505961176748206, .47220719915998133, -.37124539994395966 }, { -.6349049375117547, .9145879192435583, -1.2647800280604347, .4273300261329878, .5286397402788807, -.3184041958125821, -1.1500348054313443, 1.2844427988347253, 2.2142583413617825, -.9573399851369643, -1.2557221316198925, .6312111085688797, .07729748267473771, .17051975836215894, -.35050588737768923, -.3861434265261949 }, { -.8767102217610877, .04152800479529141, -.5868689498753934, -.5970366396218576, .1955268937368103, .46705623302365584, -.22569711606755805, -.16572933494990322, 1.611854808736049, -1.7233020810273265, -2.157764063422524, -.027994923392321725, -.20085992923158327, .7783390601599727, -.6020158868222634, .6292129788065117 }, { -.2931319964745073, 1.6818971566815206, -.5299294465111667, -1.5416405734831615, .6823769071451893, -.6690256567737607, -.652667517992435, .05678359158394953, 1.0333547051536, -.4689756173458444, -1.7563173419983675, .5951024185157137, .1399979008866736, .2441792740647047, -.8637674182539524, 1.6662401474127204 }, { .6166008484495563, .5342393571911711, -.19871989746995453, -.9151280217264962, .5728310366500384, -.7059489463579324, .30702227306509355, -.2685688306052686, -.7232039460051519, -.5284055016836433, -1.1331484704308203, .02753158423386558, -.7352997394515077, .9511349485298468, -.3717291595379176, -.6358765688809388 }, { -.4839411958800635, 1.3637242555628768, .8859415313148032, -.5343881233238063, -.484691703659731, -1.1877315768625256, -.2061734990415998, .663977233144752, .5490555080151716, .3419140782928426, .6017700575548691, -.9991344971346863, .35255450477584716, .8240363269612938, .313436006582699, 2.560036567874231 }, { 1.5842149954300708, .5026340123699269, -.9655962810069213, .046331867455838775, .1660699950978215, -.039378029504021105, .6906130116416649, -.8912145372185495, .8324825810902847, -2.5486728790766495, 1.6915430873309292, 1.5955310876317808, -.42959271965863044, -.9345636254640636, -.769231768326257, -.1234063780962739 }, { .06777335764006848, .5614976143410946, 1.6602757111979072, .49828276615505, -1.6824835996192415, -.13281629144842325, .8077524924840515, 1.5803459294194067, 1.5151342471763904, -2.0707212045541676, 3.2173364930348476, -.07189598773247131, -.5509492395702584, -1.5736929732387959, -2.6517325725674605, .4635702616777549 }, { .6142544441997253, .6856889724248446, -.11071852189789647, .2928533245440543, .8909096533628318, .04554930951742254, 1.1145691600884717, 1.4953842104264694, 2.0045344244610805, -.47725178314718586, 1.1026271436426998, -.8859285881338825, -.007169092306373008, .1330244706925559, -2.279015881322413, -.7475763638611383 }, { -.5163899799259054, -.1374697228787376, -.26271052267630973, -.011818283376490464, 1.223712481995359, -1.0098949169548799, -.530736389357637, 1.003185731360986, 1.8939081010383865, -1.2918910188266397, 1.026117811089741, -.875739777718775, .7617076072834909, -1.307553987353771, -2.7883035108700374, -.34435999011444896 }, { -.32414078433929666, .7573845588584315, -1.1488937785978262, -.6885149105179748, 1.314035144801836, -.050616389639775036, -.556224758921399, 1.7310906097120653, 2.183775558081483, -2.9298203393209397, -.43625349254638407, -.7011817073616765, .468997797286828, -.7515218568510741, -1.6554034534191426, .031161202768523112 }, { -1.177555578801546, .7822841447019278, -1.195258488082519, -1.8486604069660983, 1.8343738151045177, -.9209598028921484, .7965809562741604, .4722187919638753, .34744178143137255, -.9910047486984878, 1.5860438111810147, -1.6019580847314059, -.20250030030943972, 1.207882781263263, -.5574359500577193, -.8619980239768067 }, { .7114588825663725, .0503772068132103, -.08352103494600507, -2.269677099772444, 1.3174055109585874, -1.3920800913559874, .8665419690474996, .3683847735872928, 1.0718692187133303, .19244556513803118, 1.2432545474801295, -.20839231652824997, -1.671406082783719, .12761689413107874, .15296498304383402, -1.2420672124329541 }, { .8561178338105616, -.3644485925959164, 1.8229860592525842, -1.9714352182659165, 1.113852857867804, .23788628404222106, .6956054840170837, .21540941510180844, -.3090904138601476, -.04740737125989755, 1.2488651224078706, -2.4043186874109885, -1.430380219772211, 1.3595412818542294, .16146384989111523, -1.3877251418243501 }, { -.7001919237882754, -1.166335255459836, .45366811049057326, .9480701330834768, .7720595603373107, .34674549667983634, 1.5201419193889047, .5587870231504688, -.9119950403498844, -1.7674711435111936, 3.114736650567919, .8667534508169225, .15995936182792903, -.4041041953998097, -.006165536726052442, .021287626905004513 }, { 3.0896261582293683, -.9626083706474049, .7647650743928035, -.5997813478039667, 1.1673223367858596, .7513753170952269, 1.9210595989406443, .37536097100904875, 1.1386390823763466, -1.6782026750699264, 4.046857155443594, -.4838203889819433, -1.584801405556798, -.49152615401642075, -1.7415689007054234, -.2685369534544763 }, { 2.640639633991007, -.9419005283134585, .9555586068332333, -.8581772310963222, .1730805735207087, -1.190648921628849, 2.6227197974290535, 1.814545805112991, 2.9177611825418563, -1.112106252066499, 1.7715778992204383, -1.5889990301379955, -.25307931575220566, .8952477679641313, -3.04658982935005, -.7426826826605937 }, { .7423978802310577, -.7391291757771836, -.4603636174009541, 1.118831079833125, .9135110223674654, -1.4843657537415846, 1.6174853047684048, 1.9973258820907243, 3.0167048674237193, -1.5518004278059347, 1.115426349928713, -.78674026678398, .3504004670378989, .5774966848581217, -2.997600400466177, -.6657900966863206 }, { -.9200765607114233, -.26129307956961423, .9643353789359291, -.5264855526313309, 2.084963475644934, -.2499178971077855, .4812673221311319, 1.518391386033701, 2.330660181675084, -.8094907764046614, 1.228748687198522, .16486525055128284, -.00014510872028589762, .6484191238316509, -1.5718750884230472, -1.8148888028345123 }, { .3005962477842812, .6808188771954158, 1.185436739298142, -1.1243826503132144, 3.80298735913198, -1.002693332033228, 1.733353567067262, .9216491224108755, 2.782701547523906, .05872948511882173, 2.031392999044444, .8992180937865617, -1.1186779434515701, 1.8804813711888488, -1.3183786218689169, -.9042639635094304 }, { .6338500329991813, .8278170570230252, 1.0145137601735137, -2.102649348203016, 2.0555696646113146, -.6623323274353077, .7106633122337817, 2.1050456609371673, 1.2092429869234727, -2.0387056210509025, 2.7220797170807898, -.9178036352771962, -.6805129220896902, .17684358829630945, -.5019808403254092, .5158729467204722 }, { .6592767331834377, .04532365486365995, 1.0549188452662235, -1.1785463996347567, 2.3233194163857553, -1.2280020609384352, .5768487483339707, 1.1714620583159787, -.7933845856185608, -.9960635776657984, 1.661088204770794, .7519132948452942, -2.417518825255435, .6206181986689109, .4048413317023194, -1.1672902021852087 }, { 1.293456936211177, -.4330407289305622, 2.3247523696545804, .2074505556925775, -.2438590256338745, -1.2362335243781473, 1.4172747463695667, 1.239805450028904, -2.0901482628894867, -.6110444760791095, 2.3422109591515565, -.46183575418726747, -.24606809690415865, -.18711559970589559, -.3606315713778991, -1.5531061454985706 }, { 3.158903727572081, -1.4953260030380149, -.5439792687742776, .7770692232906238, -1.1689353313379374, 1.193104302615395, 2.2023108654830046, 2.8124170257651144, 2.180872275138494, -.36696454905482845, .413721838523112, -.2112110087297437, -.30098967050520764, -1.5589547330313276, -1.1133248799965278, -.43813328466110957 }, { 2.956902787961973, -1.6456743402357743, -1.8138626597081935, .25251234594119504, -.30860294874289945, -1.3839785710193984, 3.727106202323648, .5076822679962772, 2.4841503835869165, -1.04784794660153, 1.323802585362412, .44429699928078803, -.07584265402896542, -.8767473506928141, -.8457799908561323, .7871018404433957 }, { -.6182735298803154, -2.99240543288916, .29145499309516404, 1.258191388873169, 1.644338373694592, .645467897943728, 1.178769068822119, .2446046563880807, 2.6156882486213973, .6241745919301279, 1.3333966016042282, 1.5755154267319516, 1.2283740247445667, .04778810245366869, -1.2909196371322493, 1.435457080183426 }, { .9286074231647099, -3.184659473976937, .7580368340417082, 2.187730134097193, 1.618681408979203, -1.3184521809276095, 1.1943412795267616, .6803491727944967, .8730281138819812, 1.2279545122187525, 1.5115413551634986, 1.2276396331722608, -.770145741458092, 1.368854245809777, -1.215833392612035, -.6175768834351294 }, { -1.0981414513105414, -.7473724763749072, 1.275475720922746, .2694155765894335, 2.273637216120297, -1.0965739294155064, .8641648340879079, 1.8748258832369071, .2515406419027294, .19041854938374544, .8119762626173769, 1.6967825710573792, -.7880973924444017, 1.56511686202844, -1.3744782965164375, .4827013788649614 }, { -.12883676415401935, -2.2568994987497706, .17706197484028277, -.4185775793041974, 2.9082656770574067, -1.442807461470498, 2.736144423302625, 2.612814250600419, 1.2880272227638028, 1.7009906016097012, .043882945305006374, .2473292386668041, -2.00850459948379, 1.2224083606955682, .705803399439608, -.742601322449526 }, { -.9265845436785961, -.30784493123211004, 1.5170724773766673, .7605179744769368, 2.8043991653987392, -.8183139604931537, .902360481634848, 2.2363044924569664, -1.7764074352363768, .2097875717899728, .684751368069496, .18726450300663852, -1.9002029813889605, .2403392131592584, -.4836868142378203, 1.0024801481500956 }, { 1.8301073502862948, 1.488757554508242, -.1457779033244835, .9069027781943464, -.34937481244942425, 1.148603875985808, .6190213096908969, -.18374384538276517, -.5211022163931174, -1.059911992548191, .2216929559727055, -1.5596020415112197, -.9556726032576389, -.5546109773470513, 1.1949724657752523, 1.0269003462596122 }, { .0595433830663604, .4508847621234105, .11776940888287687, .6640003040569172, .14398299968514086, .9861248471227148, 1.792462287567894, -.6602275271819599, -1.053850741209842, -.8691517106470452, -1.4767672280351052, .6009042035412876, -.25435940565195764, .2574230405603811, .254343995501028, -.7908493857829468 }, { .9371350309504469, -.2876671186292523, 1.7392563721058076, .4592310923042988, .20260292875501615, .3608043834720232, 2.1958791176639814, -.2224807804029104, -1.3390652326948356, .12967827518873376, -.1417753144138993, -.9662372133825334, -.7078586895599893, -.30498271273231314, -.20258662403088878, -.08282874799832227 }, { -.18727161804030842, .582012820664115, 1.2115646177275465, 1.4638327387509533, .18283083637380837, -.45333170226911423, -.11968790953017507, .626130403993424, -2.159343393471199, -1.1578914345393347, -.19063118595238518, -1.6006595981808323, .07612183942870485, -.25730128286293563, .24007774526207415, 1.681050701970411 }, { .3398365775716058, -.1244067796472758, .27979969303716623, -.7283577393090905, .5206484751013971, -.054360914592134726, -.8554875150863288, .1288934168785795, -.8923244921010757, .7717573162191175, -.46401583001414515, .2777370041698287, .13425052919761113, .4193277276131481, .2597565558703339, -.13235036021782168 }, { -.2987677652284531, -.917029889690334, 1.7501511363555209, -.749730962881697, .906942968436707, -.345453238305955, .34907872982501637, .09617726721253854, .8968709231034573, .6609432215413411, -.31084444231037095, .20324792769958142, -1.0933004996594822, .42630451342776404, -.18314543547094775, .36399355438311565 }, { .7133768672689391, -.7943506387095465, -.023963983394199664, -.11387133129668778, .677581086971517, -.11656166140683918, -.047185279611349835, -.1834500099966982, .11429192581213934, .055563330504902274, 1.3871930662750303, -.3869849359018471, .17675515523224405, .014278706584698205, -.49609276262370255, .05929356140580859 }, { .4619845902944322, .5818063497820408, -.40853271476573855, 1.8263471235159439, -.43419893952041216, .1291403668119832, -.421725716131922, .21524388999501226, -1.9856234734036635, -1.3206079697194884, -1.023068924822449, -2.193259818623358, -.977311727070021, .49998440057912236, 1.4511585794419581, .6595900380843436 }, { -.8594288198047292, .32187895731139393, -.9463480730530286, -3.2798779204614195, .8301749145470898, -2.4262585965249026, 1.1220536901172093, -1.0736293049100003, 1.5144862877704597, 2.657340401436604, .6807611269159154, .6357300203203641, 1.2803415803350768, 1.0361258720013304, -.4787459426003035, -1.6354635765317425 }, { .18758385586510945, -.8687110469201369, .07575436447095203, -.27417755377461567, 1.411326974153005, -.39526294269753914, -.32634161953965385, .47433327319037316, .039276817668739056, .5611829029057974, .06706073574994859, 1.3410897159443758, -1.014070733601394, .6261169547902561, -.13341712042859677, -.7020042371324372 }, { -.4478562483547193, -.35190852637194925, .2888508740296227, -1.9995070520431029, .840094625525844, .013000342772693888, -.7230306404662846, .38636999907359193, 1.287627242944376, 1.2997956325058984, -.7943629466458532, .9329477809635668, -.3833976110912412, .0023207593041023045, .1498123583706247, .36634965033388683 }, { .7039002698069909, -.4189500129969209, -.7010974672700074, -1.1254289837228386, -.32619632005758553, -.5006380513016288, -1.066632946443623, -.0528232689831632, .2112322247679154, 1.0027119487204406, .268650492366615, -.727078220485314, -.8817784402707111, -.4765573362949393, .3470648267141416, -.1358460536646989 }, { -.13268881235004154, .6500496025725598, .7665412515320381, -.6424536036135658, -.8904770228032289, .08212215164596455, .3325733815438124, .2214166603150668, 1.3495576375131568, 1.5101195720971183, .9117529938290813, .7434575280325976, .6112975701073813, .686820897253223, -.1430378414515046, -1.0170140522752464 }, { -.6919499635030197, -.6877600156521981, -.6568087362153824, .20524898032137148, 1.0398027872656705, -.9308823687898763, -.4919169608204984, 1.4715617377982644, 1.1412718653823577, .09449500007479737, -.06150833505034035, -.1272212709236106, .019252106785462332, -.5055594085417547, -.11843137235062139, -.40402515767327685 }, { -.7239911241644285, -.4816124338619637, -.1007492961510473, -.26307533242513403, .05466340126284829, -.8928833497905017, .08395187276994437, .09792209989233466, 1.2609569744551374, -.3831170320943077, 2.2832269483202734, .0027118228269617335, -.02268561826233927, -.5976959635133287, .32755354295826145, -1.1560187615511104 }, { -1.7571280891403815, -1.0738998308316672, -.010142755477672244, -3.020963580052829, -.09773539004840834, -.2594896020016235, -1.1601026996017156, -.6643239172848101, .5875759653706474, .02931066217659277, .16256159673008552, 1.8691264228181321, 1.2589905181653305, .6247386301231777, -1.149429259537302, -.7123388712387502 }, { -1.6073615284130967, -.6578757249494166, .4749683059727082, -2.423860462341682, 2.2970423822916373, -1.1051342788098173, 1.6710149958128497, 1.25809487523555, -.3911621737250085, 1.776200964346974, -1.5717749714690723, .3949523222260882, -.7815587132860068, 1.6916944136355885, -2.8008865325790553, -1.3828575873911397 }, { 1.2779284450649302, -2.8810756267766044, -.29546644812294887, 1.066296015168727, 2.976633855752006, -1.4566666516963809, 1.3852215751460593, 1.1298954764023779, -3.1864754395421615, -.39546286796467317, .7855878462355738, 1.9920156065981867, -1.3725000741949693, 2.120374459759754, -1.0473266373277879, -1.20641868845167 }, { 1.1898286541497904, -2.062272785946588, 1.008542928230491, 1.8216633465100183, 1.0171815369681088, .4638060706611334, .2697584346200265, 1.7125667977147192, -1.6555368779932955, -.1897653672547059, .15104021476184903, 1.285797333333065, -1.9084689864324191, -.9789207327101487, 1.4858972807817703, .7108261157174501 }, { .07404684976557524, -.9517255806431483, 1.011705554157794, .9473987794282152, .6553971146356837, .8890583686477991, .5516577875985993, 1.3413770091040913, -.2685357110611405, -.977157455516842, 1.6358175007776001, -1.6979061620165588, -.4002800194550572, -.17814063889295198, 1.2546658489432225, 1.2223963401894848 }, { 2.015702980039126, -2.0056803560292056, .2743858192666321, 1.2062828262318128, .929897747478323, -.8789540290080784, 2.1094732246821537, 3.297013565889531, -.7517746849352596, -1.7313161003312079, 2.032401028222259, -.08918893732847916, -1.5852522343050772, -.10426307488240665, .9776991405187988, 1.7345710472831752 }, { 1.0765312315212614, -3.860643449316363, -.20747974582785852, 1.3769991124445398, .021348718307982517, -.2873518333840607, 2.9537980764903224, 1.9198417048948615, -.21869812918213866, -1.140486060451699, .9072554336543346, .07769299444037768, .23661173513692768, .5017154085326679, 1.1371723000059057, .7968806440419612 }, { .8113836473655671, -2.6254102886562785, -1.8442455205524446, 2.4307905947843507, 1.130185844409204, .4329295246058082, .5374156040136, 1.2782732254017033, -.6493413137171545, 1.0572643624077387, 1.8974741586600632, -.1477336061147678, -.08893535395966043, .010835363562161198, .16753216945647142, .31591777004155364 }, { .6869606423722416, -1.508972435515087, 1.1728518804329504, .03294259761742937, -.5225298875766303, .6957083969445349, 1.7868512838723012, .17310493976923377, -.8063434865350422, -.10499419120453034, 1.1039511079897664, 1.3404521620923846, -.011555774505103077, .9914750068213777, -.5999667826678331, -.03315154395604025 }, { -.4440760032663882, -3.157391121508493, -.26979998508629777, -1.0263506017117345, 1.7769624820094034, -1.2318119240927408, 1.0286378260430258, 1.7699767342564157, .10171117046872534, 1.7965575105698581, -.9919356359186852, 1.1297132996297747, .5006221604269925, .08972701672371985, .6558795918475294, -1.8653407135063849 }, { .9658568230921677, -2.5322310431881765, -.5584644446947206, -.6206440980191343, 2.4068427108239523, -.930153704280142, .8667641604881373, 1.0172174380065135, -3.85563157582667, 1.7948839630093947, -.998986367450371, .5745710650876934, -.2803204213172226, -.6292022512306984, 1.5773517752964707, -.477209606805806 }, { .3585877459098199, -1.6545537875975966, .016785110262411224, 1.1098612345552616, 2.01290223684575, -.026854479394168646, .47586710612846184, 1.4718921814110482, -1.3733198488671405, 1.1541516496188187, -.9384074187083314, 1.9561252722867049, -.5451505551592185, -.985329175310501, 3.1079927338893842, -.5137323634597649 }, { -1.1760894665109067, -1.952740045390072, .28386098579061353, 1.28779704379419, .7015174834585571, -.0941391916825843, 1.2050159857633935, 1.3630634020521908, -1.8545198187461023, 1.3806180488367918, -.8174201296889825, .9147553749242611, -1.52549520616273, -.5271531069012161, 2.4496228264871567, 1.3622721013103687 }, { .7844500582035169, -1.24889943407166, .05237460010836277, 1.7392973103554408, .8087034285542366, 1.0564055999588005, .061898031407791626, 1.6843038925248404, -.30399928644019764, -.5521208594033493, .6087979689426852, .5797089967805283, -.9227044829339418, -1.4491707468175505, .7580330536312053, 1.200195855639602 }, { 1.3272193754705452, -2.2106253999689756, 1.0409607993309098, 1.8957869275068244, -.08344145330153041, -.3666048318754852, 1.0815755612081346, .9076347406555613, -2.5266539971207944, .8650415005106574, 1.3047384113758367, 1.5776184664202293, -1.0305782246733288, -1.8363041941923504, 1.145784384236387, .9501852960798061 }, { .6071371139380729, -2.7961046055466086, .09539644058528592, 3.9179149338293833, .524258512995768, -.1459364603070751, .8637950813135504, 1.3398451909734181, -2.177060247944363, 1.8883785702769262, .14646433184207552, .927033847342533, -1.3861707970926551, -1.0218997659727898, -.24345142580816276, -1.1785501723740168 }, { .7485810229797427, -1.1756618685138713, .6054128508754866, 1.4280828835011075, -.5034869681653587, .41833581251147284, 1.2098594721879201, .18083400852718118, -1.3399436897505943, 1.6590868347598824, .5007410648068121, -.15685200299413737, -.519896028381283, -1.0833638892672837, -.6662241906981482, -.8093140976576739 }, { -1.404723925916789, -.8516418988901038, -.09906512407572558, .17486239532042058, 1.4342915710684112, -.7091123325011702, .686133004608748, .661790261752219, -.5489150061271871, .5127102045311462, -2.2020676473081995, .6837231327981138, -.7723466618944735, .9005211875730385, -.08508105606607871, -.599014208235633 }, { .032303624541462926, -.8431688750097818, 1.134589479235825, -1.5273007080339736, 1.5779763199065844, .1886783802865542, -.1234187994789964, 1.188724503237455, -2.6465985029995793, .7430629812435823, -2.0207147082234256, -.4813521883606184, .10244201325497217, .2192501514556824, 1.8334327051265875, -.4450251247823012 }, { -.48899730489120713, -1.3090507729988516, .13132136632287472, 1.4700956689296243, 1.1126313048568264, .38072126110270216, .3855270564596579, 1.3652113963172847, -1.694059758761964, 2.4339476323692866, -1.2441894579733115, -1.3422046228158149, 2.0076411872476747, -2.3759959740416874, 2.0963377626237563, .08730944244671238 }, { -.498220352765743, -1.705471558426329, -.26925011270403965, -.5685130110640457, 1.0894195271794989, .11663377218309924, -.8569786113871399, .8246122566421278, -2.5681618065106506, 1.9710352535256237, -.7075398695293473, .8429326607131706, 1.2225405847964814, -2.826674743258344, 1.570950615782325, 1.1859599410355852 }, { -.2676596981303401, -2.03993979810631, 2.189191443318636, 2.1781962427245056, -.8366798334525286, .3185691618342237, .5154346192579471, -.6109140345144237, -.47542270267906234, 2.0155709226588066, -.8359207985437683, -.42775861529926457, .619197833148488, -.7524457323823034, .4842541581875867, .8223849993408376 }, { .4765000274471296, -1.655804771521397, 1.0883456160658858, 3.534319753931663, -.5784366044199163, .3217461433944733, -.011967695404075225, 1.1515446477099716, -.895700263152704, 1.7198982508658538, -1.8292848165847333, 1.5392333336040487, -.3223953880352507, -1.7776372958467932, .9680391132730252, 1.682051984627623 }, { .6069235202381338, -1.0057136220713583, -.3992889081953371, 2.3698877223771766, -.17741326117338385, 1.6294348429931544, .1629785197427946, -.6583133871752795, -1.0518752541814518, 1.3897107938094173, -1.3704437265602376, 1.3649423979414723, .01426594695938583, -.6842114139765365, -1.9329039919762294, .5480438518886672 }, { .7023021835592289, -.6385603226092451, -.41168134867052686, .6991343986137539, .6212830588175985, -.17093363585256052, -.17331333714044136, -.1157200660077713, -.7916808534629545, .6340743240452436, -.07674977116186385, 1.4078893609609344, .16761102510057732, .43558248359245, -.5600310557824028, 1.1924950451626135 }, { -.9532252906893058, .16318961441889443, .6272756322117704, .3922883955666302, .7893886198901257, -1.7040736120389004, -.01029144944057988, .35189191501327677, .4881011081828513, 1.1002050532133147, -.35173775140623337, -.4189900880277003, -.8986294962917234, 1.1248853989302605, -.09634186374822452, -.6718117620102275 }, { -.17690065917128675, .3505359581896344, .8941280571875063, -.7464618413520838, .6376223059140568, -.26803313227374437, -.6035178979819531, .6738142837108142, -1.5711212281860758, 1.1773397871479907, -.9642160307481491, -.5696663080484876, .5600935448806921, -1.1553874599725675, 1.3785946669550835, -.3848873962887627 }, { .695699694772684, -.5469577546063566, -.939018906628439, -2.0689715396101307, .767460715800468, .503096081019381, -.699031836645499, 1.1874362817385689, -.581496411332115, -.5009523874142958, -.07978000739298878, -1.5700528063330172, 1.438129303563675, -1.5814086922726933, 1.941634828156606, .5865988596339048 }, { .104325881689081, -.7524272033184046, 1.536130194135909, 1.507146795524166, -.8759050732078861, 1.0918991111891654, -.8643417135118211, -.2516340580841654, .2234643426946697, 2.2140756504217034, .07684953771016138, -.3674592150165582, 1.4247881009664483, -1.2168250798172358, .6160318521929219, .667862745465513 }, { -1.5104180595792482, -.7433216946855113, .5951887154936253, 1.6722052657108823, -.5745945728078761, .6284505634360168, -1.4168479011357673, -.05102645968141096, -.17186092520673257, .4936344065795651, -.9535502407741746, .44839501206836857, .9160865565737578, -1.2428105668479446, .4842540096320433, .0006293040465057022 }, { .3960774362267004, .23505604746711295, .5738249290847403, 2.2166341554434323, -.7802230457893058, .9304941528360847, -.40915299342172595, 1.1514782654086264, .7170895191879482, .7010431716208734, -.6940278690896893, .8959459991747429, -.13295583206515668, -.6406030153767791, -.2503252303926045, 1.054483788340564 }, { 1.2582951229287935, -.5602280772215976, 1.0597808372341422, 1.5582481889664823, -1.4050550558028154, .45622460085089117, .4485743528496639, -.5676125804915336, -.03394872431246157, 1.544826963274967, .22512131127089993, 1.3679307990366496, -.8087788689153843, -.07684512934080656, .020788675482410368, 1.2278008233217117 }, { -.1356019605644498, .38470669162855525, -1.6891186638747844, .9697857180119873, -1.3776439491364134, .13420875042321684, .7520746332031987, -.49803137126914926, -.8094624212668475, 1.3213235996722188, -.8872231083712461, .6266793344908138, .9376084942290115, -.7321417926843459, -.724339213641352, 1.4859904761568674 }, { -.7468679541445773, .819079467236289, -.2528117034976649, -1.9117255884386297, .6299333782848444, -.3933072990704622, -.2903250014483901, -.29772587524364713, .11365701183421588, 1.3975196117906896, -.7326050866816592, -1.271439571460746, 1.1965324630119392, 1.534641223631725, -.33364276956852507, -.7204976523412188 }, { .39247819005771756, 1.136935609418936, -.11005049800736234, -2.7195244877852334, .7363333499534152, -.10465458442645788, -1.5710205839015727, -2.3680102663244234, -1.1861117317174428, -4.524115549994043, -.5679138658733404, .17805660634578885, .9511279364737139, 2.9510782356632097, .3038693589724268, -1.20878487921262 }, { .07981589453835454, 1.2673060683234734, 1.5491868730087817, -1.0077483758533614, -.2819252941790326, -.42407670096849964, -.6597581290657548, -.748704756079684, -.45763967450040305, -2.5258653236249202, -.37027917518527464, 1.1862423029020999, 1.4888618865315635, .895345287895302, .9218727401745371, -.13187555739327825 }, { .3157958046354405, .43023100109110574, -.5577778958885353, 1.4194932885585876, -.48980582955056046, .5729169688592652, .0090559087411877, -2.56585807993862, .4582974004545854, .17213468931008521, -.47128117750910525, 1.2958150657749499, -.8125981647698161, 1.0450013789604762, -.3048073488400628, .9787027582544713 }, { -.025170185262721703, 1.539828080640545, .5931595463739705, 2.0431427580172703, -2.0658382782688487, -1.0741616543184216, -.9945921972800358, .8458714788910794, 2.0220604371104396, -.4500435877937509, -1.8862987437120038, -.0039568729561841685, .555060836120556, .5137111190563037, .5271568489951698, 1.3452657223815558 }, { -1.3077128376142724, .7240838346992322, -.547562629630126, 1.665117241683897, -1.2471059374351365, -.4325243593588747, -.7979115167591372, -.22411391963039012, 1.2212222665899855, .8238814875242033, -.5488777216034479, -2.389870830822298, .12354906612080399, -.0763540247430741, -1.0915487894211426, 1.0262809489216431 }, { -.28417323618301454, 1.1562650230712002, -2.5126743920702332, 1.4122061420889513, -2.013705180884596, -.3331771988604017, -.46840143923036887, -.5678410692423427, 1.1340018839754413, .34342237810834114, 1.3354547128998, -2.3959707441814144, -.3212657967345677, .3311361080869866, -.6775937819568529, -.09390725299064824 }, { -.915499739419651, 1.5641446515502309, -1.3388726921760488, -.46833919328934664, -.6567586221003985, 1.75048347380875, -.3966493781461436, -.45213900606911434, .9203995441504693, .7721402534683177, .5715194125208493, -2.157399970383622, .18326187231685942, .15880485697608182, -2.4574908965798974, -.43788711913392486 }, { -.517305516948199, 2.1134021008859194, 1.2137545938788008, -1.896655150366132, -.6385557900249527, .04364095799224331, -1.0881011172739965, -2.3985730552047912, .3381343918216131, -1.790570198052075, -1.490340337890554, .22796580644772466, .5320751124174548, .5079980409713352, -1.2978952189280795, -.8023544842525685 }, { -.5683103647098696, 2.8424381458927233, 1.029138125451834, -2.266439678640937, 2.103818768754294, -1.2690589701644126, -1.4349123732810427, -2.0398631880304463, 2.0839885833716614, -3.850207258323231, -.47513057669977815, .7719973192806916, 1.9045950329924404, -.8914725932393268, .6269728489207136, -.25428026022102995 }, { -.4825238551234979, 3.2122165816103663, .7738493061223575, -.21959168310759303, -1.2192069833849202, -.8590786189580789, -.4881898265188506, -2.6744703445842806, .580738836098773, -4.868048232518178, 1.1423283226978451, .4668861651500665, 1.405799060850727, -.08549065813605267, -.6429147969165279, -.05013344829468185 }, { -.49775170765436005, 2.0471003468959137, .6268117909155785, -.4608111846841935, -.40711170662778645, .5128908033337616, -1.43915635173646, -.05252772069530648, 1.4737852122565163, -1.9978155410570446, .558271069419777, -.5254044345542063, .1109079194681482, .6013989118089561, -.7778247052998133, .5581165404678482 }, { .09152768750982215, 1.3991048936763486, -.17612301192392255, .16042408272876407, -.97067676407172, 1.416642817970364, -.5982016612931246, .3185237745767082, 3.1417536188855344, -.7713617895614148, .17244439513869025, -2.280783984550794, .06646956089374065, .0395095612985432, .1333857318997348, -.8789912508497213 }, { -1.313679794984664, 2.4498675674671135, -1.025725711716331, .7273826579221443, -.7297156990179041, -.1976710076659261, .47915980799419433, -1.6717587830901834, 2.0257696709266484, -1.2408455184695535, .11005669208104393, -2.3655060725913586, .07152096197075132, .15244222934484825, .12508628577761705, -.41506969711836644 }, { -1.6992047959069145, 1.9626131395576727, -2.043445374644465, -1.2976344550345202, -.1413001219968272, .2792263105386682, -.39185805963546666, -.7509873502549416, 1.1329472759232928, -1.3440121873877242, .651754067828698, -3.747196396804924, .4380187826773903, 1.3777469564817566, -.6406913699465129, -.4059832829816922 }, { .27632810822272924, 2.2431538215184172, -1.7150888226610594, -1.5747082674347768, -1.7125330536848886, -.4558747610761596, .1794780165287225, -.22304457058372998, 1.1509496710465448, -1.2331423478046846, .7854181889836188, -1.882567232574089, .14697838523945733, .6999013158098708, -1.2478345113511613, -.2683245054893048 }, { .7749462672330563, 2.677121133904393, .3721345494264419, -.5533335483747894, -1.3043317366074747, 1.2014518489326147, -.8956970985500571, -2.659408437473118, .34407496367310364, -2.701495409106944, -1.032113493337903, -2.1115107942304947, 1.9138242538994628, -.18079727401985216, .09399969649643573, -.7542973568706483 }, { -1.4779644270774945, 4.02866115129075, -.9987991908692734, -3.2555629539472077, -1.2859701197874982, -1.1711731187565477, -.8322954567929509, -2.2665015325368456, 1.348393235016561, -1.8429398873182763, -1.9807027253381555, -1.835233585734915, 1.0640831786609157, 1.9445625589570796, -1.9514403959822069, -1.1771723158736926 }, { -.47272193410376834, 2.5300057734674066, .4929646990733327, -1.5574607264314593, -1.078201733142712, -.376996318675667, -.4435053686848982, -2.165326383683769, 2.4477738879483892, -1.7553420065236789, -.4946298592532581, -1.6084609877313993, .19014530108553182, 1.865673293516824, -2.0530512193026467, .4174962679994336 }, { .588483393318661, 2.1870398922371326, -.10211042791588065, -1.5508055591059282, -.32545648216700235, -.482669373604084, .16070547777164168, -.3855652544832738, .6211001125625888, -1.2085411107860495, 1.037889224171616, .21722377072707774, -.3794165041650145, -.7342593352562404, -1.386400240317926, .3604050317793377 }, { .1346945580206341, 2.5664162445697536, -1.5947682591310202, -.9427223186514068, -.8808358281789271, .06814639650402339, .6269438169199284, .866982926835358, .8032449764847263, -1.292210413281455, 1.5285903123450657, -1.3946928178630236, -1.9983088336769643, .6201480437929967, -1.0109756434929351, -.9635069734614289 }, { -.9156238222343439, 2.080943850266874, -.07923362294848389, -1.673182999087284, .01680032977490566, .5579130063386183, -.5124936605920689, -1.845293444240266, 1.4654012239989398, -.12589849950558682, 2.182375874678464, -2.342589718146035, -.6811943931217681, .8284130955742933, -1.0552781032713352, -.7748104545372411 }, { -.9484848000329704, 2.9586281053081267, -2.69386228554103, -.5360926169534915, -.7418159314028022, -1.044603838960507, -.9310655979503057, -.7473648675978475, .7507200772148924, -1.134032982263651, .7343207269275778, -2.5194512656454457, -.32521299651705227, .5997468054947015, -2.7408669766940745, -1.6476249734546553 }, { -.8660593649513234, 2.608850328929599, -1.7354761950037907, -.9291776794919235, -1.193791659684104, -.5239919626909241, -.4274648986894793, -.7016759026740976, -1.2770072525310567, -2.8560799866016855, 1.1472963738132456, -2.2977096157452253, -.05413056752684505, 1.7548193402569803, -1.3620208439667136, -.9614300023331082 } };

        public int NNUEEM_EVALUATION()
        {
            // QQQQ RRRR BBBB NNNN PPPP 0000
            switch (countOfPiecesHash >> 8)
            {
                //case 0: return NNUE_EVALUATION();
                default: return TexelEvaluate();
            }
        }

        private int NNUE_EVALUATION()
        {
            double A0 = EvNNF1(NNUE_FIRST_LAYER_VALUES[0]), A1 = EvNNF1(NNUE_FIRST_LAYER_VALUES[1]), A2 = EvNNF1(NNUE_FIRST_LAYER_VALUES[2]), A3 = EvNNF1(NNUE_FIRST_LAYER_VALUES[3]), A4 = EvNNF1(NNUE_FIRST_LAYER_VALUES[4]), A5 = EvNNF1(NNUE_FIRST_LAYER_VALUES[5]), A6 = EvNNF1(NNUE_FIRST_LAYER_VALUES[6]), A7 = EvNNF1(NNUE_FIRST_LAYER_VALUES[7]), A8 = EvNNF1(NNUE_FIRST_LAYER_VALUES[8]), A9 = EvNNF1(NNUE_FIRST_LAYER_VALUES[9]), A10 = EvNNF1(NNUE_FIRST_LAYER_VALUES[10]), A11 = EvNNF1(NNUE_FIRST_LAYER_VALUES[11]), A12 = EvNNF1(NNUE_FIRST_LAYER_VALUES[12]), A13 = EvNNF1(NNUE_FIRST_LAYER_VALUES[13]), A14 = EvNNF1(NNUE_FIRST_LAYER_VALUES[14]), A15 = EvNNF1(NNUE_FIRST_LAYER_VALUES[15]);
            double B0 = EvNNF1(2.889711604972816 * A0 - .705841295283625 * A1 - .15066032904457557 * A2 + 1.8238360243701701 * A3 + 1.3227235193164846 * A4 - .036063016566244575 * A5 + 4.443986040328405 * A6 + 1.6839603950251343 * A7 - 1.6661570728356527 * A8 - 3.052417580693854 * A9 - .39986131009894293 * A10 - 2.68243668271913 * A11 - 2.1770306141867297 * A12 - 1.7059329097306686 * A13 - 2.3405204612211525 * A14 + .051656920217541674 * A15 - .6682533614989583);
            double B1 = EvNNF1(-3.688252577173482 * A0 - 4.858127745030992 * A1 - 1.8100330169514778 * A2 + 2.631143925273604 * A3 - .17481318116829014 * A4 + 1.614953723501903 * A5 - 4.023604045664639 * A6 + 1.8244841317745883 * A7 - 1.241522332893943 * A8 - .3972285240735416 * A9 + .008418238748203629 * A10 - .7384295304425206 * A11 + .11620540819373207 * A12 - 1.3191133820048433 * A13 + 1.607627128646643 * A14 + 1.48377314511168 * A15 - .6988365939212812);
            double B2 = EvNNF1(-.01791374694335368 * A0 + 4.581899501925097 * A1 - .48559344422776485 * A2 - .8169699503589672 * A3 - 1.0557649687346669 * A4 - .4022975331052323 * A5 - 1.028046304711506 * A6 - 1.2123143602059954 * A7 - 2.1041792527814436 * A8 + 2.8087657609400627 * A9 - 2.016717997896757 * A10 + 1.6232778280543498 * A11 + 1.801344349917601 * A12 - 1.1636900752103412 * A13 - .40302582366596357 * A14 - .47802999664158996 * A15 - 2.408176116508088);
            double B3 = EvNNF1(-1.2499669582021666 * A0 - 2.8702986865930353 * A1 - .8473051853586612 * A2 - .48762066660322223 * A3 + 1.269060864137975 * A4 - 1.3805805610523254 * A5 + .7356717689671809 * A6 + 2.5100194319110685 * A7 + 1.282223319511897 * A8 - 6.91881298572148 * A9 + 3.1084350108843806 * A10 + 2.9945847992650494 * A11 + 3.1091998023662653 * A12 - 1.7300838882794136 * A13 - 2.7114855283124517 * A14 - .9766916422311307 * A15 - 1.5786669292444326);
            double B4 = EvNNF1(-3.340548779458774 * A0 - 2.376999604411067 * A1 - 3.1663778036380963 * A2 + 1.8594149016755268 * A3 + 4.001658965761474 * A4 + .26535003125530765 * A5 - .9532694609886821 * A6 + 1.2115879275916104 * A7 - 2.8019757878921423 * A8 - 2.092055539846102 * A9 - 1.1061266951076165 * A10 - 1.9899028813539061 * A11 - 1.7072891842025535 * A12 - 1.9415278599895152 * A13 + .2638919509960795 * A14 + 1.9182266218043496 * A15 - 1.371852717775206);
            double B5 = EvNNF1(-.03057657649261052 * A0 - .9982500521332702 * A1 + .2533515860809675 * A2 - 1.1431708414710449 * A3 + 1.0947134412512278 * A4 + .14809482713723623 * A5 + .10026669725336512 * A6 - .07219904954826405 * A7 + 2.599737028380278 * A8 - 2.081171372843867 * A9 - .3289323741123994 * A10 - 2.8845350571683226 * A11 - 1.9505671182215942 * A12 + 3.5765647783432923 * A13 - .7180341056348287 * A14 + .05098367125905909 * A15 - 1.076153478063811);
            double B6 = EvNNF1(3.5363817160366824 * A0 - 4.313678353930578 * A1 + 1.3403343603746787 * A2 + 2.6326335502539995 * A3 + .009074231198398452 * A4 - 2.9545049075266157 * A5 + 4.222807522531471 * A6 + 1.4499435144534412 * A7 - .8204474716852534 * A8 + .12242909010337415 * A9 - .19334377228160482 * A10 + .19134410079205272 * A11 + .8403573022049277 * A12 - .6023595658463349 * A13 - 1.5854121982602356 * A14 + 1.8441117373987135 * A15 + .31588000018186096);
            double B7 = EvNNF1(-.8089201715762855 * A0 - 2.4276839187393446 * A1 - .10760105694287225 * A2 - 1.2728245149090445 * A3 + .6380142557421205 * A4 - .2674744460956343 * A5 + .1824890655939892 * A6 + 4.853376198181722 * A7 - 1.3543636043242042 * A8 + 2.2673526209381927 * A9 + 4.74284884180671 * A10 - 1.4401492272488605 * A11 - 1.498225256604162 * A12 - .6849239849321405 * A13 - .7052770868004645 * A14 + .8416883368118533 * A15 - .9941300941948065);
            double B8 = EvNNF1(3.8360600386642663 * A0 - 1.9127330130359648 * A1 - 2.4674349057856997 * A2 - .4196535247596617 * A3 - 1.1352933783677535 * A4 + .16124762259024233 * A5 + 4.212637918281631 * A6 + .9338104545241936 * A7 - 1.265172852837955 * A8 + .1328590599521066 * A9 - .027109106436649537 * A10 - .30780417415049094 * A11 - .05453939004675266 * A12 - .8435547265974764 * A13 + 1.4786989108773354 * A14 - .4440016644813342 * A15 - 1.8636097229167314);
            double B9 = EvNNF1(-.10413023540116532 * A0 - 2.791389588224884 * A1 - 1.02255950242393 * A2 - 2.059639399153867 * A3 + .6893619204910373 * A4 - .7689935697910588 * A5 + 2.5295595501040573 * A6 + .05832032810952696 * A7 + 3.381832540085905 * A8 - 1.1682710552568194 * A9 - 1.230058107237238 * A10 + 2.0688867579173915 * A11 + 2.750893207346186 * A12 - 2.181761516706059 * A13 - 3.123237412484783 * A14 - .6065651130170934 * A15 - .7844015675857802);
            double B10 = EvNNF1(-.18647625782221744 * A0 - 1.2159802918871725 * A1 + 2.403314088310308 * A2 - .24015739462577368 * A3 - 2.93255955194533 * A4 + 3.8042461226180193 * A5 - 2.0740512034868384 * A6 + .7164199612246346 * A7 - 1.7738512924217524 * A8 - 1.1154627506442463 * A9 - .4468643760627763 * A10 - 1.032619218097887 * A11 - .018783976287631225 * A12 - 1.018762658682886 * A13 - 2.279841458555557 * A14 + 5.364889207889079 * A15 - .80500488120916);
            double B11 = EvNNF1(-.06447640904929429 * A0 + .27911805489717156 * A1 + .8082730749832586 * A2 - 1.42430635384524 * A3 + .528814333242998 * A4 - .5728951738891024 * A5 + 1.715615387875271 * A6 + 2.4641670867548098 * A7 - 6.7023516136302295 * A8 + .8698409971515212 * A9 - 5.725805206376579 * A10 + .5853812032574293 * A11 - .4076138790902931 * A12 + 2.2075795884726075 * A13 - .7033615535565948 * A14 + .1226713937491268 * A15 - 1.9929651576457632);
            double B12 = EvNNF1(4.1241529256095095 * A0 + 6.346200868569342 * A1 - .4151059690246528 * A2 - 4.982196316025434 * A3 - 1.0666795685415096 * A4 + 1.6137099901668839 * A5 + 3.844774031238706 * A6 + 2.944592267383226 * A7 - .7054064685282078 * A8 - .6637460420833668 * A9 - .20722290929996798 * A10 - 1.37349084153501 * A11 - 2.2613909443893854 * A12 + 1.1943264321790583 * A13 - 1.5209044407663874 * A14 - .855473299886705 * A15 - 1.9658296927144923);
            double B13 = EvNNF1(-2.4172645141733757 * A0 - .7568649367380021 * A1 + 7.614174830508265 * A2 - .6893896496726706 * A3 + 8.384048149476985 * A4 - .872365188837296 * A5 + 1.6571922776064159 * A6 + 2.22142457426484 * A7 - 1.4247791605486104 * A8 - 1.3991899188496397 * A9 - 1.4386893765412663 * A10 - .9972368194149078 * A11 - 1.1069360685286502 * A12 - 1.5902764499167528 * A13 - .5808038886459198 * A14 - 1.2155757548312969 * A15 + .134521090964328);
            double B14 = EvNNF1(-1.4563215513858867 * A0 - 3.2780054838688715 * A1 + .0016800038054491928 * A2 - 3.182575412011092 * A3 + .2289566980004893 * A4 - 1.729085803766091 * A5 + .8206443158769587 * A6 + 3.8465869511034447 * A7 + 3.078855552964666 * A8 + 4.088374847667395 * A9 - 3.073774024547724 * A10 - 3.0735101059384964 * A11 - 1.7600076542002385 * A12 - 2.1680096644582267 * A13 - 3.304766008116912 * A14 + .375342550501977 * A15 - 1.208766333485096);
            double B15 = EvNNF1(-.5568963524891278 * A0 - 2.424850040599999 * A1 - .5069121153418957 * A2 - 1.6020305622877382 * A3 + .4467166936439525 * A4 - .13261341755450562 * A5 + .7069250133796027 * A6 + 4.574433688786755 * A7 - 1.6222571070757512 * A8 + 1.4861658899265175 * A9 + 4.046144883012913 * A10 + 1.3277963983911711 * A11 - 3.0432840236136225 * A12 - 1.3275100749207294 * A13 - .5504776149005657 * A14 + 1.0997307791020343 * A15 - 1.645890077246492);
            double C0 = EvNNF1(1.8719477965574947 * B0 + .29843290662977545 * B1 - 3.329590960751898 * B2 - 1.6815874487842426 * B3 + 2.7051139807908444 * B4 - 2.8377007645084844 * B5 - .1643661976452263 * B6 - 1.0938645236587052 * B7 - 1.3140656416566365 * B8 + .5157100183851648 * B9 + .711871148345168 * B10 - .17311130532961594 * B11 + 1.5589965234599374 * B12 + .7127622013812275 * B13 - 1.3719060032922572 * B14 - .21243326908697935 * B15 + .9504375527477927);
            double C1 = EvNNF1(-.2886419189612702 * B0 - 1.168500280797937 * B1 - 1.1724320445423189 * B2 - .5256605564843374 * B3 + .15620875711355448 * B4 - .36353734275910793 * B5 - .5775623548961957 * B6 - .4703961440281797 * B7 - .5368283705370106 * B8 + .9986552187541021 * B9 - .1893836225193358 * B10 - .024916663322648462 * B11 - .4062957024482952 * B12 - .6278822370371849 * B13 + .09540271486363724 * B14 + .20839536259455985 * B15 + .6403646987915547);
            double C2 = EvNNF1(-.1639014328991763 * B0 - .06858067526210335 * B1 - .6876441661427233 * B2 - .20129086827822196 * B3 + 1.0482608703732734 * B4 - .7642952157944836 * B5 - .4768200886610439 * B6 - .00941065104717384 * B7 + .10600011796163067 * B8 + .21778575816706977 * B9 + .33766564169942254 * B10 - .2823463355253502 * B11 + .840040265815249 * B12 + .22081067907135105 * B13 - .18847849282576495 * B14 - .4160340113700304 * B15 - .19354528627850742);
            double C3 = EvNNF1(-.6093765110423899 * B0 - 1.9773990089801106 * B1 - 2.694245761486471 * B2 + .6469469937576751 * B3 - .34946988995337064 * B4 - .5185419216958224 * B5 - 1.6017869523872437 * B6 - .09540863722945468 * B7 - 1.9279822413736192 * B8 + 1.4696412954381621 * B9 - 1.2665502783699953 * B10 + 2.596764088368637 * B11 - .803051057185451 * B12 - 1.5901075023549016 * B13 + .9787156821461085 * B14 + .846881822601192 * B15 - .3603467775563674);
            double C4 = EvNNF1(2.3595349902283513 * B0 + .8836211324118893 * B1 - 3.8177261772938045 * B2 - 2.021874985030293 * B3 + 2.401146708266742 * B4 - 3.127396176003835 * B5 + 1.2601794403297155 * B6 - 2.105592107018237 * B7 + .36136832248358725 * B8 + .7990537029288136 * B9 + .7929984889440237 * B10 - .7067019991820064 * B11 + 2.922160226329841 * B12 + 1.9861871363732695 * B13 - 1.4260036867627608 * B14 - .22632081067636722 * B15 + .35185958983390175);
            double C5 = EvNNF1(1.9199143611581506 * B0 + .6756227424343756 * B1 - 2.346725868720004 * B2 - 1.3408512241051807 * B3 + .6625882310907434 * B4 - 1.8608463681752265 * B5 + .614126534874155 * B6 - .6240694845143863 * B7 - .4491015090050784 * B8 + .0903478973533505 * B9 + .7263527978713904 * B10 - .7592260670095106 * B11 + 1.7585416681519952 * B12 + 1.521295904866778 * B13 - 1.1469208989934305 * B14 - .5037634188869505 * B15 - .6726393065540389);
            double C6 = EvNNF1(.1146279595424251 * B0 - 1.7075721329728208 * B1 - 2.1851302103174968 * B2 + 1.640085264529823 * B3 + .8221664752738453 * B4 - .6380825778650633 * B5 - 2.750520599943128 * B6 + 1.0938953667722353 * B7 - 2.416838687797715 * B8 + 2.683295505198915 * B9 - 2.4659717279509055 * B10 + 3.2411401185137074 * B11 - 1.6798119321115594 * B12 - 2.6986982477039594 * B13 + 2.3412696377759152 * B14 + 2.468971801583437 * B15 - .8647670993681262);
            double C7 = EvNNF1(-.11883053027928453 * B0 - 1.4202192010284918 * B1 - 2.123543502541368 * B2 + .4136610627117735 * B3 + .05599156509830425 * B4 - 1.0362992563641475 * B5 - 1.8734972657892746 * B6 + 1.2102336339449915 * B7 - 2.007929010610539 * B8 + 2.4485552453416597 * B9 - 1.4556606249228152 * B10 + 1.127367844144149 * B11 - .9774356189561137 * B12 - 1.7987351235006155 * B13 + .7230755343099968 * B14 + 2.108040764076426 * B15 - .645713411926902);
            return EvNNF2(-1.1490288425383526 * C0 + .3113356973407265 * C1 - .09702150321140039 * C2 + .8393795780884146 * C3 - 1.236193563614779 * C4 - .7964916546681955 * C5 + 1.5566755857600314 * C6 + .8388878582965206 * C7 + 1.025435894193421);
        }

        private double EvNNF1(double val)
        {
            if (val < 0d) return 0.4d * val;
            return val;
        }
        private int EvNNF2(double val)
        {
            return (int)val; //(1d / (1d + Math.Exp(-val)) - .5) * 1000d
        }

        private void RESET_NNUE_FIRST_HIDDEN_LAYER()
        {
            for (int i = 0; i < 16; i++)
                NNUE_FIRST_LAYER_VALUES[i] = NNUE_FIRST_LAYER_BIASES[i];
        }

        private void UPDATE_NNUE_FIRST_HIDDEN_LAYER_ON(int pNNUE_Inp)
        {
            //for (int i = 0; i < SIZE_OF_FIRST_LAYER_NNUE; i++)
            //{
            //    NNUE_FIRST_LAYER_VALUES[i] += NNUE_FIRST_LAYER_WEIGHTS[pNNUE_Inp, i];
            //}
        }

        private void UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(int pNNUE_Inp)
        {
            //for (int i = 0; i < SIZE_OF_FIRST_LAYER_NNUE; i++)
            //{
            //    NNUE_FIRST_LAYER_VALUES[i] -= NNUE_FIRST_LAYER_WEIGHTS[pNNUE_Inp, i];
            //}
        }

        #endregion

        #region | PERFT |

        public int PerftRoot(long pTime)
        {
            debugSearchDepthResults = true;
            evalCount = 0;
            searches++;
            int baseLineLen = 0;
            long tTimestamp = globalTimer.ElapsedTicks + pTime;
            ulong[] tZobristKeyLine = Array.Empty<ulong>();
            if (curSearchZobristKeyLine != null)
            {
                baseLineLen = curSearchZobristKeyLine.Length;
                tZobristKeyLine = new ulong[baseLineLen];
                Array.Copy(curSearchZobristKeyLine, tZobristKeyLine, baseLineLen);
            }

            int curPerft, tattk = isWhiteToMove ? PreMinimaxCheckCheckWhite() : PreMinimaxCheckCheckBlack(), pDepth = 1;
            do
            {
                curSearchDepth = pDepth;
                curSubSearchDepth = pDepth - 1;
                ulong[] completeZobristHistory = new ulong[baseLineLen + pDepth - CHECK_EXTENSION_LENGTH + 1];
                for (int i = 0; i < baseLineLen; i++) completeZobristHistory[i] = curSearchZobristKeyLine[i];
                curSearchZobristKeyLine = completeZobristHistory;

                long ttime = globalTimer.ElapsedTicks;

                curPerft = PerftRootCall(pDepth, baseLineLen, tattk);

                if (debugSearchDepthResults && pTime != 1L)
                {
                    int tNpS = Convert.ToInt32((double)curPerft * 10_000_000d / (double)(globalTimer.ElapsedTicks - ttime));
                    int tSearchEval = curPerft;
                    int timeForSearchSoFar = (int)((pTime - tTimestamp + globalTimer.ElapsedTicks) / 10000d);
                    Console.Write("PERFT [");
                    Console.Write("Depth = " + pDepth + ", ");
                    Console.Write("Nodes = " + GetThreeDigitSeperatedInteger(tSearchEval) + ", ");
                    Console.Write("Time = " + GetThreeDigitSeperatedInteger(timeForSearchSoFar) + "ms, ");
                    Console.WriteLine("NpS = " + GetThreeDigitSeperatedInteger(tNpS) + "]");
                }

                pDepth++;
            } while (globalTimer.ElapsedTicks < tTimestamp && pDepth < 179);

            depths += pDepth - 1;
            BOT_MAIN.depthsSearched += pDepth - 1;
            BOT_MAIN.searchesFinished++;
            BOT_MAIN.evaluationsMade += evalCount;

            curSearchZobristKeyLine = tZobristKeyLine;

            return curPerft;
        }

        private int PerftRootCall(int pDepth, int pBaseLineLen, int pAttkSq)
        {
            if (isWhiteToMove)
            {
                if (pAttkSq < 0) return PerftWhite(pDepth, pBaseLineLen, pAttkSq, NULL_MOVE);
                else return PerftBlack(pDepth, pBaseLineLen, pAttkSq, NULL_MOVE);
            }
            else
            {
                if (pAttkSq < 0) return PerftWhite(pDepth, pBaseLineLen, pAttkSq, NULL_MOVE);
                else return PerftBlack(pDepth, pBaseLineLen, pAttkSq, NULL_MOVE);
            }
        }

        private int PerftWhite(int pDepth, int pRepetitionHistoryPly, int pCheckingSquare, Move pLastMove)
        {
            if (pDepth == 0) return 1;
            if (IsDrawByRepetition(pRepetitionHistoryPly - 4)) return 1;

            #region NodePrep()

            List<Move> moveOptionList = new List<Move>();
            if (WhiteIsPositionTheSpecialCase(pLastMove, pCheckingSquare)) GetLegalWhiteMovesSpecialDoubleCheckCase(ref moveOptionList);
            else GetLegalWhiteMoves(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tWhiteKingSquare = whiteKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1, curPerft = 0;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;
            enPassantSquare = 65;
            isWhiteToMove = true;

            #endregion

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

                curPerft += PerftBlack(pDepth - 1, pRepetitionHistoryPly + 1, tCheckPos, curMove);

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
            }

            isWhiteToMove = true;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            return curPerft;
        }

        private int PerftBlack(int pDepth, int pRepetitionHistoryPly, int pCheckingSquare, Move pLastMove)
        {
            if (pDepth == 0) return 1;
            if (IsDrawByRepetition(pRepetitionHistoryPly - 4)) return 1;

            #region NodePrep()

            List<Move> moveOptionList = new List<Move>();
            if (BlackIsPositionTheSpecialCase(pLastMove, pCheckingSquare)) GetLegalBlackMovesSpecialDoubleCheckCase(ref moveOptionList);
            else GetLegalBlackMoves(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tBlackKingSquare = blackKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1, curPerft = 0;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;
            enPassantSquare = 65;
            isWhiteToMove = false;

            #endregion

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

                curPerft += PerftWhite(pDepth - 1, pRepetitionHistoryPly + 1, tCheckPos, curMove);

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
            }

            isWhiteToMove = false;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            return curPerft;
        }

        #endregion

        #region | HEURISTICS |

        private const int TTSize = 1_048_576;
        private const int TTAgePersistance = 3; // Legacy
        /*
         * [ulong] = Zobrist Key
         * [Move] = Best Move / Refutation Move
         * [int] = Eval
         * [short] = Depth
         * [byte] = Flag
         * [short] = Age
         */
        private (ulong, Move, int, short, byte, short)[] TTV2 = new(ulong, Move, int, short, byte, short)[TTSize];

        private int[] historyHeuristic = new int[262_144];
        private int[] countermoveHeuristic = new int[32_768];
        private int[] killerHeuristic = new int[180];

        public bool CheckForKiller(int pPly, int pCurSitMoveHash)
        {
            int tEntry = killerHeuristic[pPly];
            return (tEntry & 0x7FFF) == pCurSitMoveHash
                || ((tEntry >> 15) & 0x7FFF) == pCurSitMoveHash;
        }

        public void ClearHeuristics()
        {
            for (int i = 0; i < 262_144; i++) historyHeuristic[i] = 0;
            for (int i = 0; i < 32_768; i++) countermoveHeuristic[i] = 0;
            for (int i = 0; i < 180; i++) killerHeuristic[i] = 0;

        }

        public void ClearTTTable()
        {
            for (int i = 0; i < TTSize; i++)
            {
                TTV2[i].Item1 = 0ul;
            }
        }

        private static int CustomKillerHeuristicFunction(int pVal)
        {
            return pVal < 100_000 ? pVal : 100_001;
            //if (pVal < 400) return (int)Math.Log(0.1 * pVal + 1d, 1.05);
            //return pVal < 5_000_000 ? (int)(0.02 * pVal + 68.11d) : 1_000_000;
        }

        private static int Quantmoid4Function(int pVal)
        {
            int t = MinF(pVal, 127) - 127;
            if (pVal > 0) return t * t / 256;
            return 126 - t * t / 256;
        }

        private static int MinF(int pVal, int pVal2)
        {
            int absd = (pVal < 0 ? -pVal : pVal);
            return absd > pVal2 ? pVal2 : absd;
        }

        #endregion

        #region | EVALUATION |

        //private Dictionary<ulong, TranspositionEntryV2> transpositionTable = new Dictionary<ulong, TranspositionEntryV2>();

        private int[] pieceEvals = new int[14] { 0, 100, 300, 320, 500, 900, 0, 0, -100, -300, -320, -500, -900, 0 };
        //private int[,,] pawnPositionTable = new int[32, 6, 64];
        private int[,][] piecePositionEvals = new int[33, 14][];
        private bool[] relevantPiece = new bool[7] { false, false, true, true, true, true, false };

        private int evalCount = 0;
        private int Evaluate()
        {
            evalCount++;
            if (fiftyMoveRuleCounter > 99) return 0;
            int tEval = 0, tPT, pieceCount = ULONG_OPERATIONS.CountBits(allPieceBitboard);
            for (int p = 0; p < 64; p++) tEval += pieceEvals[tPT = pieceTypeArray[p] + 7 * ((int)(blackPieceBitboard >> p) & 1)] + piecePositionEvals[pieceCount, tPT][p];
            return tEval;
        }

        public int GameState(bool pWhiteKingCouldBeAttacked)
        {
            if (IsDrawByRepetition(curSearchZobristKeyLine.Length - 5) || fiftyMoveRuleCounter > 99) return 0;

            if (!chessClock.HasTimeLeft()) return pWhiteKingCouldBeAttacked ? -1 : 1;

            int t;
            List<Move> tMoves = new List<Move>();
            GetLegalMoves(ref tMoves);

            if (pWhiteKingCouldBeAttacked)
            {
                //GetLegalWhiteMoves(t = PreMinimaxCheckCheckWhite(), ref tMoves);
                //Console.WriteLine(t);
                t = PreMinimaxCheckCheckWhite();
                if (t == -1) return tMoves.Count == 0 ? 0 : 3;
                else if (tMoves.Count == 0) return -1;
            }
            else
            {
                //GetLegalBlackMoves(t = PreMinimaxCheckCheckBlack(), ref tMoves);
                //Console.WriteLine(t);
                t = PreMinimaxCheckCheckBlack();
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

        private bool HasRelevantPiecesLeft(ulong pBB)
        {
            for (int i = 0; i < 64; i += forLoopBBSkipPrecalcs[(pBB >> i >> 1) & sixteenFBits])
                if (relevantPiece[pieceTypeArray[i]]) return true;
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

            do
            {
                if (IsDrawByRepetition(curSearchZobristKeyLine.Length - 5)) return 0d;
                piecePositionEvals = isWhiteToMove ? processedValuesWhite : processedValuesBlack;
                MinimaxRoot(thinkingTimePerMove);

                //if (transpositionTable.Count == 0)
                //{
                //    Console.WriteLine("?!");
                //    return 0d;
                //}

                //Console.WriteLine(transpositionTable[zobristKey]);
                //Console.WriteLine(CreateFenString());
                PlainMakeMove(BestMove);
                //Console.WriteLine(CreateFenString());
                tGS = GameState(isWhiteToMove);
                //Console.WriteLine("Gamestate: " + tGS);
                //transpositionTable.Clear();
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

        private double[,][] GetInterpolatedProcessedValues(double[,][] pEvalPositionValues)
        {
            double[,][] processedValues = new double[33, 14][];
            for (int i = 0; i < 32; i++)
            {
                int ip1 = i + 1;
                processedValues[ip1, 7] = processedValues[ip1, 0] = new double[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

                processedValues[ip1, 8] = SwapArrayViewingSide(processedValues[ip1, 1] = MultiplyArraysWithVal(pEvalPositionValues[0, 0], pEvalPositionValues[1, 0], pEvalPositionValues[2, 0], earlyGameMultipliers[ip1], middleGameMultipliers[ip1], lateGameMultipliers[ip1]));
                processedValues[ip1, 9] = SwapArrayViewingSide(processedValues[ip1, 2] = MultiplyArraysWithVal(pEvalPositionValues[0, 1], pEvalPositionValues[1, 1], pEvalPositionValues[2, 1], earlyGameMultipliers[ip1], middleGameMultipliers[ip1], lateGameMultipliers[ip1]));
                processedValues[ip1, 10] = SwapArrayViewingSide(processedValues[ip1, 3] = MultiplyArraysWithVal(pEvalPositionValues[0, 2], pEvalPositionValues[1, 2], pEvalPositionValues[2, 2], earlyGameMultipliers[ip1], middleGameMultipliers[ip1], lateGameMultipliers[ip1]));
                processedValues[ip1, 11] = SwapArrayViewingSide(processedValues[ip1, 4] = MultiplyArraysWithVal(pEvalPositionValues[0, 3], pEvalPositionValues[1, 3], pEvalPositionValues[2, 3], earlyGameMultipliers[ip1], middleGameMultipliers[ip1], lateGameMultipliers[ip1]));
                processedValues[ip1, 12] = SwapArrayViewingSide(processedValues[ip1, 5] = MultiplyArraysWithVal(pEvalPositionValues[0, 4], pEvalPositionValues[1, 4], pEvalPositionValues[2, 4], earlyGameMultipliers[ip1], middleGameMultipliers[ip1], lateGameMultipliers[ip1]));
                processedValues[ip1, 13] = SwapArrayViewingSide(processedValues[ip1, 6] = MultiplyArraysWithVal(pEvalPositionValues[0, 5], pEvalPositionValues[1, 5], pEvalPositionValues[2, 5], earlyGameMultipliers[ip1], middleGameMultipliers[ip1], lateGameMultipliers[ip1]));
            }
            return processedValues;
        }

        private double[] MultiplyArraysWithVal(double[] pArr1, double[] pArr2, double[] pArr3, double pVal1, double pVal2, double pVal3)
        {
            int tL = pArr1.Length;
            double[] rArr = new double[tL];
            for (int i = 0; i < tL; i++)
            {
                rArr[i] = pArr1[i] * pVal1 + pArr2[i] * pVal2 + pArr3[i] * pVal3;
            }
            return rArr;
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

        private int[] SwapArrayViewingSideAndNegate(int[] pArr)
        {
            return new int[64]
            {
                -pArr[56], -pArr[57], -pArr[58], -pArr[59], -pArr[60], -pArr[61], -pArr[62], -pArr[63],
                -pArr[48], -pArr[49], -pArr[50], -pArr[51], -pArr[52], -pArr[53], -pArr[54], -pArr[55],
                -pArr[40], -pArr[41], -pArr[42], -pArr[43], -pArr[44], -pArr[45], -pArr[46], -pArr[47],
                -pArr[32], -pArr[33], -pArr[34], -pArr[35], -pArr[36], -pArr[37], -pArr[38], -pArr[39],
                -pArr[24], -pArr[25], -pArr[26], -pArr[27], -pArr[28], -pArr[29], -pArr[30], -pArr[31],
                -pArr[16], -pArr[17], -pArr[18], -pArr[19], -pArr[20], -pArr[21], -pArr[22], -pArr[23],
                -pArr[8],   -pArr[9], -pArr[10], -pArr[11], -pArr[12], -pArr[13], -pArr[14], -pArr[15],
                -pArr[0],   -pArr[1],  -pArr[2],  -pArr[3],  -pArr[4],  -pArr[5],  -pArr[6],  -pArr[7]
            };
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

        private double[] SwapArrayViewingSide(double[] pArr)
        {
            return new double[64]
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

        #region | TEXEL TUNING |

        private int[,][] texelTuningRuntimeVals = new int[33, 14][];
        private int[,][] texelTuningVals = new int[3, 6][];
        private int[,] texelTuningAdjustIterations = new int[6, 64];

        private int[][] texelTuningRuntimePositionalValsV2EG = new int[14][];
        private int[][] texelTuningRuntimePositionalValsV2LG = new int[14][];

        private int[] texelPieceEvaluationsV2EG = new int[14] { 0, 100, 300, 320, 500, 900, 0, 0, -100, -300, -320, -500, -900, 0 };
        private int[] texelPieceEvaluationsV2LG = new int[14] { 0, 100, 300, 320, 500, 900, 0, 0, -100, -300, -320, -500, -900, 0 };

        private int[] texelKingSafetyR1EvaluationsEG = new int[9];
        private int[] texelKingSafetyR2EvaluationsEG = new int[17];
        private int[] texelKingSafetyR1EvaluationsLG = new int[9];
        private int[] texelKingSafetyR2EvaluationsLG = new int[17];

        private int[] texelKingSafetySREvaluationsPT1EG = new int[12];
        private int[] texelKingSafetySREvaluationsPT2EG = new int[12];
        private int[] texelKingSafetySREvaluationsPT3EG = new int[12];
        private int[] texelKingSafetySREvaluationsPT4EG = new int[12];
        private int[] texelKingSafetySREvaluationsPT5EG = new int[12];
        private int[] texelKingSafetySREvaluationsPT6EG = new int[12];
        private int[] texelKingSafetySREvaluationsPT1LG = new int[12];
        private int[] texelKingSafetySREvaluationsPT2LG = new int[12];
        private int[] texelKingSafetySREvaluationsPT3LG = new int[12];
        private int[] texelKingSafetySREvaluationsPT4LG = new int[12];
        private int[] texelKingSafetySREvaluationsPT5LG = new int[12];
        private int[] texelKingSafetySREvaluationsPT6LG = new int[12];

        private int[,] texelMobilityStraightEG = new int[14, 15], texelMobilityStraightLG = new int[14, 15];
        private int[,] texelMobilityDiagonalEG = new int[14, 14], texelMobilityDiagonalLG = new int[14, 14];

        private int[] texelPieceEvaluations = new int[14] { 0, 100, 300, 320, 500, 900, 0, 0, -100, -300, -320, -500, -900, 0 };

        private int[] blackSidedSquares = new int[64]
        {
            56, 57, 58, 59, 60, 61, 62, 63,
            48, 49, 50, 51, 52, 53, 54, 55,
            40, 41, 42, 43, 44, 45, 46, 47,
            32, 33, 34, 35, 36, 37, 38, 39,
            24, 25, 26, 27, 28, 29, 30, 31,
            16, 17, 18, 19, 20, 21, 22, 23,
            08, 09, 10, 11, 12, 13, 14, 15,
            00, 01, 02, 03, 04, 05, 06, 07
        };

        #region | ORIGINAL PeStO Tables; for Comparison |

        private int[,] T_Pawns = new int[3, 64] {
        {
               0,   0,   0,   0,   0,   0,  0,   0,
              98, 134,  61,  95,  68, 126, 34, -11,
              -6,   7,  26,  31,  65,  56, 25, -20,
             -14,  13,   6,  21,  23,  12, 17, -23,
             -27,  -2,  -5,  12,  17,   6, 10, -25,
            -26,  -4,  -4, -10,   3,   3,  33, -12,
            -35,  -1, -20, -23, -15,  24,  38, -22,
              0,   0,   0,   0,   0,   0,   0,   0,
        } , {
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000
        } , {
              0,   0,   0,   0,   0,   0,   0,   0,
            178, 173, 158, 134, 147, 132, 165, 187,
             94, 100,  85,  67,  56,  53,  82,  84,
             32,  24,  13,   5,  -2,   4,  17,  17,
             13,   9,  -3,  -7,  -7,  -8,   3,  -1,
              4,   7,  -6,   1,   0,  -5,  -1,  -8,
             13,   8,   8,  10,  13,   0,   2,  -7,
              0,   0,   0,   0,   0,   0,   0,   0,
        } };
        private int[,] T_Knights = new int[3, 64] {
        {
            -167, -89, -34, -49,  61, -97, -15, -107,
             -73, -41,  72,  36,  23,  62,   7,  -17,
             -47,  60,  37,  65,  84, 129,  73,   44,
              -9,  17,  19,  53,  37,  69,  18,   22,
             -13,   4,  16,  13,  28,  19,  21,   -8,
             -23,  -9,  12,  10,  19,  17,  25,  -16,
             -29, -53, -12,  -3,  -1,  18, -14,  -19,
            -105, -21, -58, -33, -17, -28, -19,  -23
        } , {
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000
        } , {
            -58, -38, -13, -28, -31, -27, -63, -99,
            -25,  -8, -25,  -2,  -9, -25, -24, -52,
            -24, -20,  10,   9,  -1,  -9, -19, -41,
            -17,   3,  22,  22,  22,  11,   8, -18,
            -18,  -6,  16,  25,  16,  17,   4, -18,
            -23,  -3,  -1,  15,  10,  -3, -20, -22,
            -42, -20, -10,  -5,  -2, -20, -23, -44,
            -29, -51, -23, -15, -22, -18, -50, -64
        } };
        private int[,] T_Bishops = new int[3, 64] {
        {
            -29,   4, -82, -37, -25, -42,   7,  -8,
            -26,  16, -18, -13,  30,  59,  18, -47,
            -16,  37,  43,  40,  35,  50,  37,  -2,
             -4,   5,  19,  50,  37,  37,   7,  -2,
             -6,  13,  13,  26,  34,  12,  10,   4,
              0,  15,  15,  15,  14,  27,  18,  10,
              4,  15,  16,   0,   7,  21,  33,   1,
            -33,  -3, -14, -21, -13, -12, -39, -21
        } , {
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000
        } , {
            -14, -21, -11,  -8, -7,  -9, -17, -24,
             -8,  -4,   7, -12, -3, -13,  -4, -14,
              2,  -8,   0,  -1, -2,   6,   0,   4,
             -3,   9,  12,   9, 14,  10,   3,   2,
             -6,   3,  13,  19,  7,  10,  -3,  -9,
            -12,  -3,   8,  10, 13,   3,  -7, -15,
            -14, -18,  -7,  -1,  4,  -9, -15, -27,
            -23,  -9, -23,  -5, -9, -16,  -5, -17
        } };
        private int[,] T_Rooks = new int[3, 64] {
        {
             32,  42,  32,  51, 63,  9,  31,  43,
             27,  32,  58,  62, 80, 67,  26,  44,
             -5,  19,  26,  36, 17, 45,  61,  16,
            -24, -11,   7,  26, 24, 35,  -8, -20,
            -36, -26, -12,  -1,  9, -7,   6, -23,
            -45, -25, -16, -17,  3,  0,  -5, -33,
            -44, -16, -20,  -9, -1, 11,  -6, -71,
            -19, -13,   1,  17, 16,  7, -37, -26
        } , {
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000
        } , {
            13, 10, 18, 15, 12,  12,   8,   5,
            11, 13, 13, 11, -3,   3,   8,   3,
             7,  7,  7,  5,  4,  -3,  -5,  -3,
             4,  3, 13,  1,  2,   1,  -1,   2,
             3,  5,  8,  4, -5,  -6,  -8, -11,
            -4,  0, -5, -1, -7, -12,  -8, -16,
            -6, -6,  0,  2, -9,  -9, -11,  -3,
            -9,  2,  3, -1, -5, -13,   4, -20
        } };
        private int[,] T_Queens = new int[3, 64] {
        {
            -28,   0,  29,  12,  59,  44,  43,  45,
            -24, -39,  -5,   1, -16,  57,  28,  54,
            -13, -17,   7,   8,  29,  56,  47,  57,
            -27, -27, -16, -16,  -1,  17,  -2,   1,
             -9, -26,  -9, -10,  -2,  -4,   3,  -3,
            -14,   2, -11,  -2,  -5,   2,  14,   5,
            -35,  -8,  11,   2,   8,  15,  -3,   1,
             -1, -18,  -9,  10, -15, -25, -31, -50
        } , {
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000
        } , {
            -9,  22,  22,  27,  27,  19,  10,  20,
           -17,  20,  32,  41,  58,  25,  30,   0,
           -20,   6,   9,  49,  47,  35,  19,   9,
             3,  22,  24,  45,  57,  40,  57,  36,
           -18,  28,  19,  47,  31,  34,  39,  23,
           -16, -27,  15,   6,   9,  17,  10,   5,
           -22, -23, -30, -16, -16, -23, -36, -32,
           -33, -28, -22, -43,  -5, -32, -20, -41
        } };
        private int[,] T_Kings = new int[3, 64] {
        {
            -65,  23,  16, -15, -56, -34,   2,  13,
             29,  -1, -20,  -7,  -8,  -4, -38, -29,
             -9,  24,   2, -16, -20,   6,  22, -22,
            -17, -20, -12, -27, -30, -25, -14, -36,
            -49,  -1, -27, -39, -46, -44, -33, -51,
            -14, -14, -22, -46, -44, -30, -15, -27,
              1,   7,  -8, -64, -43, -16,   9,   8,
            -15,  36,  12, -54,   8, -28,  24,  14
        } , {
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000,
            000, 000, 000, 000, 000, 000, 000, 000
        } , {
            -74, -35, -18, -18, -11,  15,   4, -17,
            -12,  17,  14,  17,  17,  38,  23,  11,
             10,  17,  23,  15,  20,  45,  44,  13,
             -8,  22,  24,  27,  26,  33,  26,   3,
            -18,  -4,  21,  24,  27,  23,   9, -11,
            -19,  -3,  11,  21,  23,  16,   7,  -9,
            -27, -11,   4,  13,  14,   4,  -5, -17,
            -53, -34, -21, -11, -28, -14, -24, -43
        } };

        #endregion

        private int[] paramsLowerLimits = new int[5] { -10000, -10000, -10000, -10000, -10000 };
        private int[] paramsUpperLimits = new int[5] { 10000, 10000, 10000, 10000, 10000 };

        private List<TLM_ChessGame> threadDataset;
        private int threadFrom, threadTo;
        private int[] threadParams;
        private int customThreadID = 0;
        private ulong ulAllThreadIDsUnfinished = 0;

        private double lastCostVal = 0d;
        private double sumCostVals = 0d;
        private int costCalculations = 0;

        private readonly string[] TEXELPRINT_GAMEPARTS
            = new string[3] { "Early Game: ", "Mid Game: ", "Late Game: " };
        private readonly string[] TEXELPRINT_PIECES
            = new string[6] { "Bauer: ", "Springer: ", "Läufer: ", "Turm: ", "Dame: ", "König: " };


        private double[] earlyGameMultipliers = new double[33];
        private double[] middleGameMultipliers = new double[33];
        private double[] lateGameMultipliers = new double[33];

        private int[] pieceTypeGameProgressImpact = new int[14]
        { 0, 0, 1, 1, 2, 4, 0, 0, 0, 1, 1, 2, 4, 0 };

        // === CUR ===
        private void PrecalculateMultipliers()
        {
            //if (BOARD_MANAGER_ID == ENGINE_VALS.PARALLEL_BOARDS - 1) Console.WriteLine();
            for (int i = 0; i < 25; i++)
            {
                //earlyGameMultipliers[i] = MultiplierFunction(i, 32d);
                //middleGameMultipliers[i] = MultiplierFunction(i, 16d);
                //lateGameMultipliers[i] = MultiplierFunction(i, 0d);

                earlyGameMultipliers[i] = EGMultiplierFunction(i);
                lateGameMultipliers[i] = LGMultiplierFunction(i);

                //if (BOARD_MANAGER_ID == ENGINE_VALS.PARALLEL_BOARDS - 1)
                //    Console.WriteLine("[" + i + "] " + earlyGameMultipliers[i] + " | " + middleGameMultipliers[i] + " | " + lateGameMultipliers[i]);
            }
        }

        // === CUR ===
        private double MultiplierFunction(double pVal, double pXShift)
        {
            double d = Math.Exp(1d / 6d * (pVal - pXShift));
            return 5 * (d / Math.Pow(d + 1, 2d));
        }
        private double EGMultiplierFunction(double pVal)
        {
            return Math.Clamp(1 / 16d * (pVal - 4d), 0d, 1d);
        }
        private double LGMultiplierFunction(double pVal)
        {
            return Math.Clamp(1 / -16d * (pVal - 4d) + 1d, 0d, 1d);
        }
        private double EGMultiplierFunction2(double pVal)
        {
            return Math.Clamp(1 / 32d * pVal, 0d, 1d);
        }
        private double LGMultiplierFunction2(double pVal)
        {
            return Math.Clamp(1 / -32d * pVal + 1d, 0d, 1d);
        }

        // === CUR ===
        private void TuneWithTxtFile(string pTXTName)
        {
            List<TLM_ChessGame> gameDataset = new List<TLM_ChessGame>();
            string tPath = PathManager.GetTXTPath(pTXTName);
            string[] tStrs = File.ReadAllLines(tPath);
            //int g = 0, sortedOut = 0;
            List<string> tGames = new List<string>();
            foreach (string s in tStrs) if (s.Contains(';') && s.Replace(" ", "") != "") tGames.Add(s);
            foreach (string s in tGames)
            {
                TLM_ChessGame tGame = CGFF.GetGame(s);
                LoadFenString(tGame.startFen);
                foreach (int hMove in tGame.hashedMoves)
                {
                    List<Move> moveOptionList = new List<Move>();
                    GetLegalMoves(ref moveOptionList);

                    int tL = moveOptionList.Count;
                    for (int j = 0; j < tL; j++)
                    {
                        Move tMove = moveOptionList[j];
                        if (tMove.moveHash == hMove)
                        {
                            tGame.actualMoves.Add(tMove);
                            //int tQuietEval = isWhiteToMove ? 
                            //    QuiescenceWhite(0, BLACK_CHECKMATE_VAL - 100, WHITE_CHECKMATE_VAL + 100, 0, PreMinimaxCheckCheckWhite(), NULL_MOVE) 
                            //    : QuiescenceBlack(0, BLACK_CHECKMATE_VAL - 100, WHITE_CHECKMATE_VAL + 100, 0, PreMinimaxCheckCheckBlack(), NULL_MOVE);
                            ////Evaluate();
                            //bool tB;
                            //tGame.isMoveNonTactical.Add(tB = Evaluate() == tQuietEval);
                            //if (!tB) sortedOut++;
                            PlainMakeMove(tMove);
                            tL = -1;
                            break;
                        }
                    }

                    if (tL != -1) break;
                }
                gameDataset.Add(tGame);
                //Console.WriteLine((++g) + " - " + sortedOut);
            }
            Console.WriteLine("[TLM TEXEL TUNER] " + tGames.Count + " Games Loaded In!");

            //int[] ttt = new int[1152] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 2, -1, 1, 2, 0, 0, 1, 2, 3, -1, 0, 0, 0, 0, 1, 4, -3, -11, -2, 1, -2, 2, -8, 1, -10, 3, 4, -6, -10, -1, 15, 33, -4, -18, -16, 17, -72, 47, 8, -10, -62, 51, -111, 134, -78, 60, 0, 0, 0, 0, 0, 0, 0, 0, 14, 0, -29, -10, 2, -1, 0, -19, 1, -5, -13, -3, 2, -1, -16, -5, 1, -1, 1, 6, -1, 0, 2, 2, -4, -16, -13, -9, -11, -16, 2, -11, -32, 0, -1, -6, -14, -25, 1, 10, -5, -9, -1, 1, -7, 1, 3, -14, -10, -69, -6, 92, 22, -5, 6, 17, 43, -133, -83, -71, -20, -26, -104, -87, 4, -4, -1, -7, -8, 0, -13, -8, -7, -12, -2, -1, 0, 2, -4, -14, -3, 0, -10, -7, -11, -3, -11, -3, -16, -1, 3, 1, -6, -1, -2, 8, -3, -2, -3, 14, -17, 11, -2, -5, 0, -16, 5, -18, 21, -2, 1, 2, 1, 9, 112, 105, 14, 20, -48, 12, 114, -16, 29, -10, -66, 4, -21, -18, 0, 0, -12, 2, -19, -15, -3, 0, -1, -15, -16, -16, -14, -4, -8, 1, -12, 4, -21, -4, -16, -17, -14, -11, -16, -8, 2, -14, 1, -11, 29, -1, -13, -14, -1, 21, -4, 96, -25, 5, -10, -11, 94, -42, 59, 38, 4, -7, -14, -48, -31, -89, -38, 101, 6, 7, -75, -14, -31, -86, 18, -79, -7, -7, -26, -6, -2, -2, -16, -12, -9, -37, -7, 4, -12, -1, -4, -5, -8, -6, -3, -1, -2, -19, -3, -4, -5, -17, -2, -12, -8, -3, -12, -7, -13, -15, -10, -8, 2, -11, 0, 29, -20, -11, -53, 2, -30, 0, -22, -10, -10, 23, -5, -6, -6, -28, -72, -1, -98, 4, -32, 10, -116, 3, -1, 22, 54, -114, 6, 18, -20, -2, 0, 2, -8, -28, 104, -25, -10, -2, -16, 0, -4, -9, -118, -125, 1, -11, -8, -14, -2, -16, 24, 112, -23, -26, 5, 0, -2, -15, 132, 117, 136, -1, -104, 77, -14, 107, 128, 122, -78, 133, 169, 61, 126, 134, 31, -132, -131, 130, 155, 88, 116, 142, -128, 182, -159, 2, 151, 43, -133, -163, 0, 0, 0, 0, 0, 0, 0, 0, -16, 0, 0, -1, 1, 1, 0, 0, -16, 0, 2, -1, 0, 0, 16, 0, -16, -12, 12, -14, -1, 0, -2, -15, -29, -17, 3, 17, 3, -12, -14, 15, -84, 18, 26, 10, 15, 18, 33, 32, 11, -73, 46, 15, -115, 132, -71, 142, 0, 0, 0, 0, 0, 0, 0, 0, 1, -17, -54, -23, 4, 23, 0, -51, -13, -2, -14, -3, 0, 0, -18, 14, -15, 0, -16, 6, 0, 0, 5, 0, 32, -17, -16, -28, -11, 0, 12, 5, -118, -17, 48, -8, 1, -62, 2, 59, 6, -59, -2, -1, 6, -36, 59, -2, -18, 120, 10, 24, -58, 11, 21, 51, 20, -154, -4, 18, 96, 115, -96, 89, -1, -37, -17, 8, -24, 0, -21, -4, -117, -31, 15, -16, 0, -13, 13, -82, -17, 34, -14, -11, -31, 15, -17, 16, -39, -1, 4, 5, 8, -16, 11, 5, -15, 0, -52, 30, -17, 76, -16, 6, 81, -25, 28, -31, 90, -65, 32, 74, 53, -53, 3, 107, -70, 30, -27, 12, 65, -83, 19, -112, 55, 107, 7, -4, -16, 0, -13, 2, -4, 2, -3, 0, -17, -16, 2, 16, -14, -2, 6, 0, -32, 0, -53, 15, -16, -36, 0, -13, -14, -5, -1, -32, 14, 4, 52, 14, -14, 2, -20, 56, -29, 92, -11, 23, -60, 19, -97, -41, -9, 102, 22, -24, -46, -17, -13, -111, -19, 102, 3, 38, -106, 1, -16, -72, 18, 81, -16, 6, -27, -18, -16, -17, -16, -10, -40, -33, -38, 5, -13, -16, -4, -4, -4, -72, 14, -1, -2, -34, -3, -3, -21, -19, -18, -32, -12, -17, -29, -42, -31, -30, -27, -16, 0, -16, 1, 30, -18, 9, -98, -13, -30, 66, -81, -90, 9, 54, 21, 5, -56, -47, -57, -5, -88, 17, -91, 11, -103, 100, 3, 92, 108, -107, 54, 19, -10, 0, -16, -14, -26, -59, 35, -13, -13, -17, -16, -17, -3, -23, 84, -112, 19, 0, -27, 0, 14, 0, 103, 96, 71, -108, 37, -3, -2, 36, 67, 80, 23, -13, -27, 17, 23, 74, 80, 76, 1, 115, 152, 99, 41, 63, 14, -82, -18, 113, 143, -56, 99, 114, -112, 155, -131, 75, 135, -49, -134, -168, 0, 0, 0, 0, 0, 0, 0, 0, -16, 16, -48, -49, 16, 0, 16, -32, -16, -33, -111, -16, -97, -16, 80, 15, -17, -60, 13, -112, 16, 46, -50, -64, -64, -84, -64, 111, 0, 31, -48, 47, -85, 119, -55, 37, 40, 65, 101, 99, -70, -112, 26, -72, -110, 156, -64, 78, 0, 0, 0, 0, 0, 0, 0, 0, 3, -113, -121, -68, -76, 111, -112, -12, -40, -57, -111, -3, -50, 2, -5, -16, -31, 32, 16, -6, -78, -113, -74, -48, 116, -34, -32, -65, -109, 29, -24, -13, 1, -52, -73, -73, 81, -104, -15, 111, -106, -112, 30, -5, 83, -122, 114, -20, 6, 7, 58, 105, -112, 41, 102, 102, 91, -143, 29, 82, 103, 121, -111, 106, 10, -34, -80, 22, -60, -96, 26, -114, -111, -112, -16, -64, -48, -73, -1, 5, -64, 116, -64, -94, -47, 48, -5, -32, -48, -32, -111, 3, 116, -32, -12, 87, -80, -18, -18, 113, -99, 125, -112, -111, 86, -115, 96, -96, 120, 43, 91, 106, 57, -32, 6, -84, -125, 55, 21, -33, 114, -110, -100, -99, -56, 112, 66, -3, -64, -48, -80, -29, -6, 16, -34, -64, -48, -16, 113, 78, 19, -48, -27, -16, -66, 31, -120, 67, 16, -57, 14, -96, 0, -17, -68, -100, 80, 96, 121, 78, -14, 96, -25, 120, -16, 108, -18, 44, -106, 81, -80, -103, -105, 115, 57, 102, -16, 24, -58, -114, 0, 116, 60, 82, -105, 32, -17, -105, 94, 109, -106, -13, -113, -96, -112, -97, -112, -23, -105, -44, -101, -23, -110, -95, -97, -113, -31, -118, 0, -112, -3, -113, -18, -64, -114, -47, -113, -102, 18, -113, -96, -110, -49, -48, -120, -96, -18, -69, 16, 117, 11, -3, -119, -41, -59, 115, 33, -106, -116, 114, -54, 37, -77, -112, -91, -28, -107, -112, -4, 114, -123, 114, -8, 111, 121, -82, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            BOT_MAIN.curTEXELPARAMS = new int[TEXEL_PARAMS];
            BOT_MAIN.ParallelTexelTuning(gameDataset);



            //TLMTuning(gameDataset);

            //TexelTuning(gameDataset, new int[ENGINE_VALS.TEXEL_PARAMS]);

            //Stopwatch sw = Stopwatch.StartNew();

            //CalculateAverageTexelCost(gameDataset, new int[0]);
            //for (int i = 0; i < 10_000_000; i++) Evaluate();
            //sw.Stop();
            //Console.WriteLine(sw.ElapsedTicks);

            //for (int j = 0; j < 6; j++)
            //{
            //    Console.Write("{");
            //    for (int k = 0; k < 64; k++)
            //        Console.Write(texelTuningAdjustIterations[j, k] + ", ");
            //    Console.WriteLine("}");
            //}
            //
            //ConsoleWriteLineTuneArray(texelTuningVals);
            //
            //Console.WriteLine("| EPOCHE " + (i + 1));
            //Console.WriteLine("| Games: " + tGames.Count);
            //Console.WriteLine("| Average Cost: " + (sumCostVals / costCalculations));
            //Console.WriteLine("| Cost Calculations: " + costCalculations);
            //Console.WriteLine("| Sum Cost: " + sumCostVals);
            //Console.WriteLine("\n\n");
        }

        private void LoadBestTexelParamsIn()
        {
            //int[] ttt = new int[778] { 82, 341, 416, 470, 1106, 102, 302, 304, 518, 960, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, -46, 0, -20, 0, 64, -21, -71, 37, 29, 6, 98, 13, 44, 0, -26, -14, -36, -20, 94, 16, 60, 10, -62, -19, -38, -8, -28, 14, -26, -4, -32, -28, -52, 30, 56, -40, -72, -22, 100, 4, -98, -34, 6, -49, 32, -10, 72, 20, 84, -56, -60, -112, 128, -28, 152, 34, 26, -52, 200, -56, 76, 44, -56, -113, -156, 188, -39, 28, 200, -90, -60, -86, 100, -178, -119, -200, 200, 200, -20, -22, -78, -158, -200, 196, 200, -200, -200, -200, 104, 200, 138, -122, 200, 200, 124, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 156, -37, -68, -148, -200, 200, -70, 72, 76, -31, -200, -21, 178, -200, 200, 200, -200, 123, -188, -6, 164, -74, 24, -28, -8, 71, -50, -112, -200, -8, -44, 27, -23, -50, 19, 19, 41, 150, 152, 4, -98, -36, -34, 174, -106, -16, 17, 141, -48, -108, 162, 6, -112, -52, 76, 68, 8, -86, 68, -176, 189, 81, 162, -200, 152, -41, 128, 172, -173, -144, 56, 22, 94, -28, 32, 16, -63, 36, -200, -200, -200, -22, 56, -116, 76, -36, -30, 100, 52, -166, -114, 142, -52, -44, -200, -18, -72, 60, 200, -160, 96, 46, -200, -28, -32, -44, 132, 60, -200, -137, -200, -176, 200, -200, -200, 4, 200, -200, 72, 200, -78, 200, 160, -105, 200, 96, -114, 200, 200, -88, -24, 11, -34, 184, 26, 83, -140, 10, -16, -72, -200, 12, -200, 46, -200, 11, 82, 66, -102, 22, -184, 32, 114, 140, -26, -18, -36, 66, 100, 25, -24, 133, -96, 50, 2, 80, 184, 43, -136, 44, 124, -148, 180, 69, -126, -200, 6, 78, -96, 89, -68, 200, -116, 114, 158, 5, -154, 115, 200, -152, -171, 41, -80, -23, 137, 98, -80, 40, -42, 200, 52, 146, 200, 6, -112, 32, 200, 132, 4, 90, -200, -200, 104, 67, -56, 186, 124, 140, 97, -144, 200, -4, -160, -117, -200, 24, -80, 200, 28, -46, -24, -52, -200, -200, 72, 20, 34, 200, -16, 88, 56, 200, -146, 120, 174, 200, -200, -86, 125, 172, -70, 4, 124, -72, 0, -2, 70, -14, 48, -32, 200, 80, 24, -132, 74, -44, 184, -4, -136, 0, -16, 20, -18, -32, 96, 92, 56, 2, 174, 196, -200, -56, -116, 88, 200, -8, 168, -13, 74, -106, 200, -182, 80, 82, -128, -16, 90, -200, 66, -88, 58, -16, 80, -34, 96, -95, -96, -200, 76, 26, -72, 32, -27, -108, 164, 200, -36, -93, -10, -56, 22, 36, 74, -200, -96, 200, 88, -88, 156, -92, 96, -200, 200, 191, -8, 62, -8, -100, 136, -72, -36, 200, -64, -114, 153, -110, 174, 46, 108, 142, 121, -200, 108, -200, 74, -4, 104, -200, 116, 82, 200, -76, 176, -200, 76, -8, 200, 46, 29, -86, 12, -24, -80, -12, 200, 124, -16, 54, 104, 200, -94, 140, 36, 32, -64, 24, -178, 15, -200, 0, -200, 4, -32, -124, 148, 176, 192, 90, 164, -91, 200, 172, -200, 64, 34, 40, -200, 0, -200, 12, -124, 48, -200, -56, 184, -8, -184, 0, 200, -16, 200, -8, 180, -18, -20, 24, -200, -58, 174, -200, 40, -4, -200, -24, 200, 54, 200, -24, -32, 37, 192, 48, 44, 26, 200, -22, 200, -145, -200, 30, 86, 54, 200, -102, -34, 94, -98, 92, -200, -76, 196, -18, -200, -180, 16, 186, -92, -200, -200, 164, -200, 62, 192, 159, -112, -12, -70, 184, 104, 200, -200, -148, -4, 86, -144, 200, -100, 200, 200, -104, 114, 132, -168, 12, 56, -10, 82, 200, -200, -200, -196, -6, 52, -200, 84, 200, -192, 128, 200, -200, -200, 200, 88, 168, -32, -84, 172, 52, -172, 0, -40, 68, -186, 42, 96, -200, -198, 200, -56, -176, 88, 0, 86, 16, -124, -46, 40, -16, -4, 54, -184, 200, 58, -200, 77, -200, -44, 102, 0, -50, -10, -92, 100, -86, 8, 20, -20, -200, 38, -200, -49, -4, 2, -200, 78, 0, -40, -92, 32, 0, -164, -88, -38, -138, -118, 29, -34, -200, -200, -200, -71, -200, 200, -200, -108, -200, 0, 192, -24, 104, -200, 200, 190, -28, -200, -200, -200, 128, -96, 200, 200, 200, 200, -200, -200, 177, -24, 200, 132, 200, -190, 200, -130, 200, 148, 200, 200, -200, -100, 200, -196, -200, 90, 200, -200, 200, 200, -200, -200, -200, -200, -200, 118, -200, 85, -200, 166, 199, -200 };
            //int[] ttt = new int[1212] { 64, 224, 440, 448, 840, 128, 256, 256, 472, 640, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -32, 88, -48, -200, 48, -40, -24, -152, -24, 32, 0, -136, 40, -64, 0, 66, -39, -200, 32, -184, 0, 0, 96, -200, 0, -72, 0, 0, 0, -96, -136, 0, 0, 88, 0, -16, 96, -88, 0, -112, 0, 0, -88, 48, 24, -200, 0, 104, -200, 136, -48, 168, 96, -176, 136, -200, 168, 104, 96, 52, 40, -104, -32, 136, 136, -88, 0, 136, 48, 200, -200, 200, 200, -200, 72, 200, -40, 32, 72, 200, 200, 200, 200, 200, -152, 96, 200, 200, -136, 200, -168, 200, -32, 200, -200, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -200, -200, 0, 200, -200, 112, 200, 200, 160, 200, 72, 200, 0, -200, -104, 200, 64, -136, 200, -200, 0, -200, 72, 200, 200, 0, -72, 200, 0, 0, 64, 200, 64, -200, -136, 200, 120, -64, 200, 64, 168, -136, 104, 0, 0, -200, 0, -200, 200, -200, -200, -56, -116, 200, 0, -136, 64, 200, 0, -96, 200, -136, -48, 200, -8, 0, -72, 200, 200, 200, -72, -200, 0, -72, 136, -32, 0, 112, 200, -152, 200, 200, 200, 200, 0, -200, 64, 200, -200, 168, -24, -200, -80, -72, 200, -112, -200, 200, 136, -32, 200, 200, 200, 200, -200, -200, 144, -200, -56, 168, 200, 0, 200, -200, 200, 200, -200, -200, -200, 118, -200, 36, -200, -200, 88, -200, 62, -200, -104, 64, -200, 164, 0, 200, 32, -200, -200, -200, 0, 104, -136, 200, -104, -200, -200, 200, 0, 184, 136, 0, -22, 136, 136, -104, -136, -200, 64, -200, 200, -200, 32, 72, -96, -200, 0, -56, 40, 200, 72, 64, 64, -10, -96, -200, 200, -64, 48, -200, 72, 200, -64, 200, 200, -168, 96, -136, 0, 200, -168, -200, -200, 168, 200, 48, 88, 64, -168, -200, 72, 200, 200, 200, -200, -136, 200, -136, 200, -200, -104, -200, 200, -128, -200, 8, -200, -200, -64, 0, -200, 104, 200, 200, 200, 200, 200, 200, -200, -200, 200, -200, -200, 104, -200, -200, 200, 200, -200, 168, -200, 200, -72, 200, -200, 120, -200, -88, -120, -200, 200, -200, -200, 200, -152, -56, -200, -200, 0, 64, 0, 200, -200, 128, 128, -200, 64, -120, -200, 0, 44, 64, 0, 48, -32, -168, -64, 72, -200, 200, 32, -64, -200, -24, 200, -96, -104, 104, -40, 136, 200, -200, 200, -136, 136, -88, 0, -200, 136, -200, -152, -200, -136, 200, 116, 64, -64, 200, 48, 0, 32, -32, 168, 0, 200, -200, -72, -104, -200, -56, 0, -120, 128, -200, 200, 72, 200, 136, 200, 200, 104, 200, -136, 200, -72, 200, 200, -200, -64, 136, 168, -116, -84, 0, 200, -72, -96, 200, -64, -200, -168, -136, 200, -64, -104, 200, 0, -200, -200, 72, 200, 0, 200, -136, -200, 0, 64, 80, 136, -80, -200, 200, 128, -96, 200, -200, 200, -200, 200, -128, -200, 200, -200, -200, -200, 200, 200, -72, -64, -200, 128, 200, -64, 200, -200, -200, -200, 200, 200, -200, -200, -200, -72, 200, 0, -8, 104, 200, 200, 200, 0, -200, -16, -200, 112, -200, -200, 200, 0, 56, 0, -72, 0, 200, -64, 200, -64, -104, 80, -200, 200, -200, -200, 200, -200, -200, -120, 136, -115, -200, 64, -200, 0, -200, 88, -200, 72, 200, 0, -200, 0, -200, -136, 72, 136, 200, 0, 72, 0, -200, 0, -200, 200, 200, 32, 200, -104, -200, 32, -200, 200, 200, 24, -104, 104, 200, 200, -64, 200, 200, -200, -200, 72, 200, 200, 200, 200, -200, -200, 80, 200, 200, 200, -8, 136, 104, -104, -200, 200, 136, 160, 200, -200, -200, 56, 200, -64, 200, 40, 200, 64, 200, 200, 200, -200, -200, -72, -48, -80, 128, 0, -200, 0, 32, 152, -200, 200, -64, 200, 200, -200, -136, 96, -64, 96, 104, -80, -72, 120, -136, 96, 8, 0, 200, 24, -136, -200, 200, -200, 64, -160, 152, -200, 136, -72, 200, -72, 72, -136, 200, -200, -120, -200, 0, -200, -200, 200, 104, -200, 200, 104, -48, -128, 200, -200, -112, -200, 200, 200, -64, -200, 64, -200, 32, -200, -200, 200, 0, -72, 200, 200, 200, 200, 136, 200, -72, 200, 200, 200, 200, -96, 200, -200, -200, -200, 200, -200, 200, 200, -200, 200, 200, -200, 200, -200, 200, 200, 200, 200, 200, 0, 0, 200, -8, 200, 200, -200, -200, -200, 200, -200, -200, -200, 120, -200, -200, -200, -200, 0, -200, -200, -200, 0, 0, 0, 0, -200, -104, 0, 0, -24, -32, 144, 200, 0, 72, -20, 104, -32, 0, 84, -168, 0, 74, 0, 0, -16, 0, 44, -168, 32, -32, 48, 48, 64, 0, -40, 184, 104, 16, 32, 88, 0, 112, 0, 80, 0, 0, 40, 40, -72, -16, -56, -32, 200, 8, -168, 200, 132, -24, 0, 0, 0, 0, -200, 200, 0, 0, -64, 176, 168, -200, -32, 0, 48, 200, 32, 104, 0, 168, 0, 72, 0, -32, 0, 64, -32, 96, 0, 0, 0, 0, 0, 44, 0, 104, 0, 0, -32, 64, 72, 32, 0, 104, 136, -32, 0, 0, 8, -200, -136, -32, 16, -200, -120, 80, -64, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, -176, 0, 120, 0, 88, -32, 32, 0, -176, 120, 24, 0, -104, 40, 64, 0, 0, 32, 64, 0, 64, 104, -32, 0, 0, 64, -32, 56, 96, 200, 96, 32, 0, 200, 0, 32, 0, 112, 0, 200, 0, 200, 136, 168, 0, 64, -24, 0, 0, 0, 0, -200, -200, 0, 0, 0, -200, 200, -200, -32, 200, 64, -200, 96, 200, -32, -200, 0, 200, 0, -200, 0, 152, -86, 200, 32, -40, 0, 200, 0, 104, 0, -32, 96, -24, 48, 8, 96, 200, -48, 200, 72, 32, 40, -200, 200, 200, -16, 80, 48, 200, 0, 56, 72, -200, 0, 0, 0, 0, 200, 40, 0, 0, 0, -72, -64, -200, 20, 64, -64, 64, -20, -20, 0, 115, -32, 64, -16, 64, 0, 0, 0, -96, -32, 64, 0, 32, -80, 0, 0, 114, 32, -16, 32, 0, -8, 0, 0, 0, -24, 32, -24, -32, 200, 200, 72, 0, -200, 152, 32, -32, -200, -64, 0, 152, 0, 0, 0, 0, -48, -32, 0, 0, 0, 0, 64, 0, 0, -32, -32, 0, 0, 0, -40, 40, 136, 0, 40, 0, -64, -136, 0, 200, 0, 0, 32, 64, 72, -200, 32, 200, 0, 0, 0, -200, -64, -64, 32, 0, -200, 186, -64, -200, -64, -112, 0, 0, 32, 200, 200, -8, -168, 0, -120, -200, -200, 56, -136, 0, 200, -200, 200, 80, 200, 0, -72, 200, 200, 56, -200, 0, -200, 200, -104, 48, -32, 0, 0, 0, 0, 200, 40, 0, 0, 0, 0, -72, -200, 0, 0, 0, 0, -200, -200, 0, 0, 0, 0, -200, -200, 0, 0, 0, 0, 0, -200, 0, 0, 0, 0, 0, -200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            //int[] ttt = new int[778] { 72, 300, 484, 528, 904, 128, 272, 280, 504, 720, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -56, 88, -92, -200, 56, -40, -48, -148, -100, 96, -32, -100, 8, -28, -8, 32, -64, -200, 44, -184, 12, -8, 96, -200, -16, -104, -40, 40, 24, -134, -136, -8, -16, 80, -8, -32, 136, -120, -12, -136, -16, 20, -136, 68, 32, -200, 0, 88, -176, 128, -72, 128, 88, -200, 104, -200, 124, 40, 64, 84, 8, -124, -48, 128, 100, -36, -32, -32, -64, 200, -200, 200, 200, -200, 8, 156, -20, 76, 72, 200, 196, 200, 200, 200, -66, 8, 200, 200, -200, 200, -200, 200, 56, 200, -200, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -200, -200, -28, 200, -200, -22, 200, 192, 172, 200, 12, 120, 12, -200, -200, 200, 18, -194, 200, -200, 46, -168, 52, 200, 176, 56, -56, 200, -40, 32, 8, 200, 70, -176, -112, 200, 168, -48, 192, 76, 192, -72, 136, 46, 64, -200, -24, -200, 172, -200, -200, 88, 24, 200, 112, -68, 200, 200, 168, -120, 200, -88, -12, 200, 76, 40, 56, 200, 200, 200, 64, -200, 138, -56, 200, -16, 96, 136, 200, -112, 200, 200, 200, 200, 101, -200, 176, 200, -200, 200, 120, -200, 54, -128, 200, -24, -200, 200, 200, -72, 200, 200, 200, 200, -200, -200, 176, -200, 80, 176, 200, -24, 112, -200, 200, 200, -200, -200, -200, 128, -200, 112, -200, -200, 200, -200, 16, -200, 16, -80, -200, 184, -8, 200, -32, -200, -176, -200, -56, 200, 56, 200, -104, -152, -200, 200, 32, 200, 112, 8, -24, 160, 92, -76, -120, -200, 64, -200, 200, -200, 32, 136, -140, -144, 24, 8, 16, 200, 132, 24, 40, -24, -62, -200, 200, -15, -8, -116, 148, 200, -76, 200, 200, -200, 96, -200, 8, 200, -148, -200, -200, 188, 200, 86, 64, 80, -180, -200, 64, 200, 200, 200, -200, -132, 184, -184, 200, -156, -136, -200, 200, -104, -184, 60, -200, -184, -32, 0, -169, 96, 200, 200, 200, 200, 200, 200, -200, -200, 200, -200, -200, 68, -200, -200, 200, 200, -200, 176, -200, 200, -56, 200, -200, 152, -200, -168, -46, -200, 200, -200, -200, 192, -200, 68, -200, -200, -16, 16, 4, 200, -168, 112, 176, -200, 92, -128, -200, 16, 34, 144, -68, 24, -40, -200, -64, 40, -128, 152, 48, -24, -200, -35, 200, -56, -88, 4, -96, 128, 200, -200, 200, -100, 200, -80, 32, -172, 200, -200, -40, -200, -78, 200, 96, -24, -26, 200, 80, -32, 200, -76, 200, 48, 200, -200, 16, -148, -200, -64, 32, -72, 120, -180, 200, 84, 200, 136, 200, 200, 200, 200, -52, 200, 72, 200, 200, -200, -64, 200, 200, -144, -136, 40, 200, -104, -8, 200, 88, -200, -173, -112, 200, -88, -40, 200, 32, -200, -200, 72, 200, 68, 200, -156, -200, -40, 128, 4, 144, -88, -200, 200, 174, -136, 200, -200, 184, -176, 200, -56, -200, 200, -200, -152, -200, 160, 200, -171, -96, -200, 88, 200, -62, 72, -200, -200, -200, 200, 194, -200, -200, -200, -140, 200, -32, 56, 88, 200, 200, 200, -53, -200, -38, -200, 84, -200, -200, 200, -8, 100, 3, -140, 38, 172, -48, 200, -20, -160, 80, -200, 200, -200, -200, 200, -200, -200, -56, -52, -72, -124, 80, 80, 88, -8, 104, -88, 104, 200, -24, -200, -8, -200, -80, 22, 164, 200, 152, 80, 104, -200, 88, -104, 200, 200, 25, 200, -112, -200, 56, -200, 200, 200, 100, -80, 176, 200, 200, 104, 200, 200, -200, -200, 24, 200, 200, 200, 200, -200, -200, -30, 200, 200, 200, 48, 200, 200, -76, -200, 200, -184, 200, 200, -200, -200, 174, 200, 108, 192, 128, 200, 80, 200, 200, 40, -200, -200, -48, -84, -88, 64, -24, -200, 8, -16, 160, -200, 176, -100, 200, 200, -200, -174, 48, -112, 72, 44, -136, -84, 44, -104, 64, -16, -96, 200, 64, -200, -200, 200, -200, 80, -200, 144, -200, 144, -192, 152, -104, 24, -200, 200, -200, -136, -8, -112, -200, -200, 200, 112, -200, 200, 48, 0, -200, 144, -200, -136, -48, 200, 200, -134, -200, -56, -200, -28, -192, -200, 200, 92, -200, 200, 134, 200, 200, 144, 200, -94, 200, 120, 200, 200, 200, 200, -200, -200, -200, 200, -200, 200, 200, -200, 200, 200, -197, 200, -200, 200, 197, 200, 200, 200, 0, 0, 200, -200, 200, 200, -200, -200, -200, 200, -200, -200, -200, 128, -200, -200, -200, -200, 0, -200, -200, -200 };

            int[] ttt = new int[778];
            //ttt[0] = 82;
            //ttt[1] = 341;
            //ttt[2] = 416;
            //ttt[3] = 470;
            //ttt[4] = 1106;
            //ttt[5] = 102;
            //ttt[6] = 302;
            //ttt[7] = 304;
            //ttt[8] = 518;
            //ttt[9] = 960;

            SetupTexelEvaluationParams(ttt);
        }

        // === CUR ===
        private void SetupTexelEvaluationParams(int[] pParams)
        {
            //texelPieceEvaluations[8] = -(texelPieceEvaluations[1] = pParams[0]);
            //texelPieceEvaluations[9] = -(texelPieceEvaluations[2] = pParams[1]);
            //texelPieceEvaluations[10] = -(texelPieceEvaluations[3] = pParams[2]);
            //texelPieceEvaluations[11] = -(texelPieceEvaluations[4] = pParams[3]);
            //texelPieceEvaluations[12] = -(texelPieceEvaluations[5] = pParams[4]);

            // 10 Stk
            //texelPieceEvaluationsV2EG[8] = -(texelPieceEvaluationsV2EG[1] = pParams[0]);
            //texelPieceEvaluationsV2EG[9] = -(texelPieceEvaluationsV2EG[2] = pParams[1]);
            //texelPieceEvaluationsV2EG[10] = -(texelPieceEvaluationsV2EG[3] = pParams[2]);
            //texelPieceEvaluationsV2EG[11] = -(texelPieceEvaluationsV2EG[4] = pParams[3]);
            //texelPieceEvaluationsV2EG[12] = -(texelPieceEvaluationsV2EG[5] = pParams[4]);
            //texelPieceEvaluationsV2LG[8] = -(texelPieceEvaluationsV2LG[1] = pParams[5]);
            //texelPieceEvaluationsV2LG[9] = -(texelPieceEvaluationsV2LG[2] = pParams[6]);
            //texelPieceEvaluationsV2LG[10] = -(texelPieceEvaluationsV2LG[3] = pParams[7]);
            //texelPieceEvaluationsV2LG[11] = -(texelPieceEvaluationsV2LG[4] = pParams[8]);
            //texelPieceEvaluationsV2LG[12] = -(texelPieceEvaluationsV2LG[5] = pParams[9]);
            ////
            //int c = 10; // 768 Stk
            //for (int p = 1; p < 7; p++)
            //{
            //    for (int s = 0; s < 64; s++)
            //    {
            //        int p1 = pParams[c++], p2 = pParams[c++];
            //
            //        texelTuningRuntimePositionalValsV2EG[p][s] = p1;
            //        texelTuningRuntimePositionalValsV2EG[p + 7][blackSidedSquares[s]] = -p1;
            //        texelTuningRuntimePositionalValsV2LG[p][s] = p2;
            //        texelTuningRuntimePositionalValsV2LG[p + 7][blackSidedSquares[s]] = -p2;
            //    }
            //}

            //for (int s = 0; s < 64; s++)
            //{
            //    int pawnEG = T_Pawns[0, s], pawnLG = T_Pawns[2, s];
            //    int knightEG = T_Knights[0, s], knightLG = T_Knights[2, s];
            //    int bishopEG = T_Bishops[0, s], bishopLG = T_Bishops[2, s];
            //    int rookEG = T_Rooks[0, s], rookLG = T_Rooks[2, s];
            //    int queenEG = T_Queens[0, s], queenLG = T_Queens[2, s];
            //    int kingEG = T_Kings[0, s], kingLG = T_Kings[2, s];
            //
            //    int bsSq = blackSidedSquares[s];
            //
            //    texelTuningRuntimePositionalValsV2EG[1][bsSq] = pawnEG;
            //    texelTuningRuntimePositionalValsV2LG[1][bsSq] = pawnLG;
            //    texelTuningRuntimePositionalValsV2EG[8][s] = -pawnEG;
            //    texelTuningRuntimePositionalValsV2LG[8][s] = -pawnLG;
            //    texelTuningRuntimePositionalValsV2EG[2][bsSq] = knightEG;
            //    texelTuningRuntimePositionalValsV2LG[2][bsSq] = knightLG;
            //    texelTuningRuntimePositionalValsV2EG[9][s] = -knightEG;
            //    texelTuningRuntimePositionalValsV2LG[9][s] = -knightLG;
            //    texelTuningRuntimePositionalValsV2EG[3][bsSq] = bishopEG;
            //    texelTuningRuntimePositionalValsV2LG[3][bsSq] = bishopLG;
            //    texelTuningRuntimePositionalValsV2EG[10][s] = -bishopEG;
            //    texelTuningRuntimePositionalValsV2LG[10][s] = -bishopLG;
            //    texelTuningRuntimePositionalValsV2EG[4][bsSq] = rookEG;
            //    texelTuningRuntimePositionalValsV2LG[4][bsSq] = rookLG;
            //    texelTuningRuntimePositionalValsV2EG[11][s] = -rookEG;
            //    texelTuningRuntimePositionalValsV2LG[11][s] = -rookLG;
            //    texelTuningRuntimePositionalValsV2EG[5][bsSq] = queenEG;
            //    texelTuningRuntimePositionalValsV2LG[5][bsSq] = queenLG;
            //    texelTuningRuntimePositionalValsV2EG[12][s] = -queenEG;
            //    texelTuningRuntimePositionalValsV2LG[12][s] = -queenLG;
            //    texelTuningRuntimePositionalValsV2EG[6][bsSq] = kingEG;
            //    texelTuningRuntimePositionalValsV2LG[6][bsSq] = kingLG;
            //    texelTuningRuntimePositionalValsV2EG[13][s] = -kingEG;
            //    texelTuningRuntimePositionalValsV2LG[13][s] = -kingLG;
            //}

            for (int s = 0; s < 64; s++)
            {
                int pawnEG = T_Pawns[0, s], pawnLG = T_Pawns[2, s];
                int knightEG = T_Knights[0, s], knightLG = T_Knights[2, s];
                int bishopEG = T_Bishops[0, s], bishopLG = T_Bishops[2, s];
                int rookEG = T_Rooks[0, s], rookLG = T_Rooks[2, s];
                int queenEG = T_Queens[0, s], queenLG = T_Queens[2, s];
                int kingEG = T_Kings[0, s], kingLG = T_Kings[2, s];
            
                int bsSq = blackSidedSquares[s];
            
                texelTuningRuntimePositionalValsV2EG[1][bsSq] = pawnEG;
                texelTuningRuntimePositionalValsV2LG[1][bsSq] = pawnLG;
                texelTuningRuntimePositionalValsV2EG[8][s] = -pawnEG;
                texelTuningRuntimePositionalValsV2LG[8][s] = -pawnLG;
                texelTuningRuntimePositionalValsV2EG[2][bsSq] = knightEG;
                texelTuningRuntimePositionalValsV2LG[2][bsSq] = knightLG;
                texelTuningRuntimePositionalValsV2EG[9][s] = -knightEG;
                texelTuningRuntimePositionalValsV2LG[9][s] = -knightLG;
                texelTuningRuntimePositionalValsV2EG[3][bsSq] = bishopEG;
                texelTuningRuntimePositionalValsV2LG[3][bsSq] = bishopLG;
                texelTuningRuntimePositionalValsV2EG[10][s] = -bishopEG;
                texelTuningRuntimePositionalValsV2LG[10][s] = -bishopLG;
                texelTuningRuntimePositionalValsV2EG[4][bsSq] = rookEG;
                texelTuningRuntimePositionalValsV2LG[4][bsSq] = rookLG;
                texelTuningRuntimePositionalValsV2EG[11][s] = -rookEG;
                texelTuningRuntimePositionalValsV2LG[11][s] = -rookLG;
                texelTuningRuntimePositionalValsV2EG[5][bsSq] = queenEG;
                texelTuningRuntimePositionalValsV2LG[5][bsSq] = queenLG;
                texelTuningRuntimePositionalValsV2EG[12][s] = -queenEG;
                texelTuningRuntimePositionalValsV2LG[12][s] = -queenLG;
                texelTuningRuntimePositionalValsV2EG[6][bsSq] = kingEG;
                texelTuningRuntimePositionalValsV2LG[6][bsSq] = kingLG;
                texelTuningRuntimePositionalValsV2EG[13][s] = -kingEG;
                texelTuningRuntimePositionalValsV2LG[13][s] = -kingLG;
            }

            //return;
            //
            ////c = 778; >> 290 Stk
            //for (int p = 2; p < 7; p++)
            //{
            //    for (int a = 0; a < 15; a++)
            //    {
            //        if (a != 14)
            //        {
            //            // Diagonal
            //            texelMobilityDiagonalEG[p + 7, a] = -(texelMobilityDiagonalEG[p, a] = pParams[c++]);
            //            texelMobilityDiagonalLG[p + 7, a] = -(texelMobilityDiagonalLG[p, a] = pParams[c++]);
            //        }
            //        // Straight
            //        texelMobilityStraightEG[p + 7, a] = -(texelMobilityStraightEG[p, a] = pParams[c++]);
            //        texelMobilityStraightLG[p + 7, a] = -(texelMobilityStraightLG[p, a] = pParams[c++]);
            //    }
            //
            //}
            //
            ////c = 1068; >> 144 Stk
            //for (int i = 0; i < 12; i++)
            //{
            //    texelKingSafetySREvaluationsPT1EG[i] = pParams[c++];
            //    texelKingSafetySREvaluationsPT2EG[i] = pParams[c++];
            //    texelKingSafetySREvaluationsPT3EG[i] = pParams[c++];
            //    texelKingSafetySREvaluationsPT4EG[i] = pParams[c++];
            //    texelKingSafetySREvaluationsPT5EG[i] = pParams[c++];
            //    texelKingSafetySREvaluationsPT6EG[i] = pParams[c++];
            //    texelKingSafetySREvaluationsPT1LG[i] = pParams[c++];
            //    texelKingSafetySREvaluationsPT2LG[i] = pParams[c++];
            //    texelKingSafetySREvaluationsPT3LG[i] = pParams[c++];
            //    texelKingSafetySREvaluationsPT4LG[i] = pParams[c++];
            //    texelKingSafetySREvaluationsPT5LG[i] = pParams[c++];
            //    texelKingSafetySREvaluationsPT6LG[i] = pParams[c++];
            //}

            //c = 1212

            //texelTuningRuntimePositionalValsV2EG

            //texelTuningRuntimeVals = GetInterpolatedProcessedValues(texelTuningVals);
            //
            //for (int t = 1; t < 33; t++)
            //{
            //    Console.WriteLine("Pieces = " + t);
            //    Console.WriteLine("[WHITE SIDED]");
            //    for (int p = 1; p < 2; p++)
            //    {
            //        Console.WriteLine(TEXELPRINT_PIECES[p - 1]);
            //        for (int s = 0; s < 64; s++)
            //        {
            //            if (s % 8 == 0 && s != 0) Console.WriteLine();
            //            Console.Write(texelTuningRuntimeVals[t, p][s] + ", ");
            //        }
            //        Console.WriteLine();
            //    }
            //    Console.WriteLine("[BLACK SIDED]");
            //    for (int p = 8; p < 9; p++)
            //    {
            //        Console.WriteLine(TEXELPRINT_PIECES[p - 8]);
            //        for (int s = 0; s < 64; s++)
            //        {
            //            if (s % 8 == 0 && s != 0) Console.WriteLine();
            //            Console.Write(texelTuningRuntimeVals[t, p][s] + ", ");
            //        }
            //        Console.WriteLine();
            //    }
            //}
        }
        private void PrintDefinedTexelParams(int[] pParams)
        {
            int c = 0;
            for (int t = 0; t < 3; t++)
            {
                Console.WriteLine(TEXELPRINT_GAMEPARTS[t]);
                for (int p = 0; p < 6; p++)
                {
                    Console.WriteLine(TEXELPRINT_PIECES[p]);
                    texelTuningVals[t, p] = new int[64];
                    for (int s = 0; s < 64; s++)
                    {
                        if (s % 8 == 0 && s != 0) Console.WriteLine();
                        Console.Write((texelTuningVals[t, p][s] = pParams[c++]) + ", ");
                    }
                    Console.WriteLine();
                }
            }
            piecePositionEvals = GetInterpolatedProcessedValues(texelTuningVals);
        }

        public void TLMTuning(List<TLM_ChessGame> pDataset)
        {
            int tGameDataSetLen = pDataset.Count;
            int[,][] tRatio = new int[33, 6][];
            int[,][] tTotalMoves = new int[33, 6][];

            double[,][] tSummedRatios = new double[33, 6][];
            double[,][] tSummedDataAmount = new double[33, 6][];

            double[,][] tTuningResults = new double[33, 6][];

            for (int t = 1; t < 33; t++)
            {
                for (int p = 0; p < 6; p++)
                {
                    tRatio[t, p] = new int[64];
                    tTotalMoves[t, p] = new int[64];
                    tSummedRatios[t, p] = new double[64];
                    tSummedDataAmount[t, p] = new double[64];
                    tTuningResults[t, p] = new double[64];
                }
            }

            for (int j = 0; j < tGameDataSetLen; j++)
            {
                TLM_ChessGame tGame = pDataset[j];
                int tGR = tGame.gameResult - 1;
                LoadFenString(tGame.startFen);
                int tL = tGame.actualMoves.Count - 5;
                for (int i = 0; i < tL; i++)
                {
                    if (tGame.isMoveNonTactical[i])
                    {
                        int tPieceAmount = ULONG_OPERATIONS.CountBits(allPieceBitboard);
                        for (int s = 0; s < 64; s++)
                        {
                            int tPT = pieceTypeArray[s] - 1;
                            if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, s))
                            {
                                tTotalMoves[tPieceAmount, tPT][s]++;
                                tRatio[tPieceAmount, tPT][s] += tGR;
                            }
                            else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, s))
                            {
                                int b = blackSidedSquares[s];
                                tTotalMoves[tPieceAmount, tPT][b]++;
                                tRatio[tPieceAmount, tPT][b] += tGR;
                            }
                        }
                    }
                    PlainMakeMove(tGame.actualMoves[i]);
                }
            }

            double[,] allMultipliers = new double[33, 33];

            for (int sh = 0; sh < 33; sh++)
            {
                for (int i = 0; i < 33; i++)
                {
                    allMultipliers[sh, i] = MultiplierFunction(i, sh);
                }
            }

            for (int t = 1; t < 33; t++)
            {
                //Console.WriteLine("PieceCount = " + t + ": ");
                for (int p = 0; p < 6; p++)
                {
                    //Console.WriteLine(TEXELPRINT_PIECES[p]);
                    for (int s = 0; s < 64; s++)
                    {
                        //if (s % 8 == 0 && s != 0) Console.WriteLine();

                        //double res = TTT(tRatio[t, p][s], tTotalMoves[t, p][s]);

                        for (int t2 = 1; t2 < 33; t2++)
                        {
                            double cmult = allMultipliers[t, t2];
                            tSummedRatios[t, p][s] += tRatio[t2, p][s] * cmult;
                            tSummedDataAmount[t, p][s] += tTotalMoves[t2, p][s] * cmult;
                        }

                        tTuningResults[t, p][s] = TTT(tSummedRatios[t, p][s], tSummedDataAmount[t, p][s]);

                        //Console.Write(TTT(tRatio[t, p][s], tTotalMoves[t, p][s]).ToString().Replace(",", ".") + "(" + tTotalMoves[t, p][s] + ")" + ", "); // / (double)tTotalMoves[t, p][s]
                    }
                    //Console.WriteLine();
                }
            }

            int c = 0;
            int[] tparams = new int[1152];
            for (int t = 1; t < 33; t += 15)
            {
                if (t == 31) t = 32;
                Console.WriteLine("PieceCount = " + t + ": ");
                for (int p = 0; p < 6; p++)
                {
                    Console.WriteLine(TEXELPRINT_PIECES[p]);
                    for (int s = 0; s < 64; s++)
                    {
                        if (s % 8 == 0 && s != 0) Console.WriteLine();
                        Console.Write((tparams[c++] = (int)(tTuningResults[t, p][s] * 100)).ToString().Replace(",", ".") + "(" + (int)tSummedDataAmount[t, p][s] + ")" + ", "); // / (double)tTotalMoves[t, p][s]
                    }
                    Console.WriteLine();
                }
            }

            string tS = "PARAMS: {";
            for (int i = 0; i < tparams.Length; i++) tS += tparams[i] + ",";
            tS = tS.Substring(0, tS.Length - 1) + "}\n";

            Console.WriteLine(tS);

            TexelTuning(pDataset, tparams);
        }

        private double TTT(double d1, double d2)
        {
            if (d2 == 0) return 0;
            return d1 / d2;
        }

        // === CUR ===
        public void TexelTuning(List<TLM_ChessGame> pDataset, int[] pCurBestParams)
        {
            Stopwatch sw = Stopwatch.StartNew();

            string tPath = PathManager.GetTXTPath("OTHER/TEXEL_TUNING");
            int paramCount = pCurBestParams.Length;
            double bestAvrgCost = CalculateAverageTexelCost(pDataset, pCurBestParams);

            Console.WriteLine("CUR PARAMS COST: " + bestAvrgCost);

            int packetSize = Math.Clamp(paramCount / ENGINE_VALS.CPU_CORES, 1, 100000);

            paramsLowerLimits = new int[paramCount];
            paramsUpperLimits = new int[paramCount];

            for (int i = 0; i < paramCount; i++)
            {
                int lim = i < 10 ? 100000 : 200;

                paramsLowerLimits[i] = -lim;
                paramsUpperLimits[i] = lim;
            }

            Console.WriteLine("PARAM-COUNT: " + paramCount);
            Console.WriteLine("PACKET-SIZE: " + packetSize);

            while (packetSize <= paramCount)
            {
                BOT_MAIN.TEXELfinished = 0;
                int lM = 0, tC = 0;
                ulong tUL = 0ul;
                for (int m = packetSize; lM < paramCount; m += packetSize)
                {
                    if (m > paramCount) m = paramCount;
                    tUL = ULONG_OPERATIONS.SetBitToOne(tUL, ++tC);
                    lM = m;
                }
                tC = lM = 0;
                for (int m = packetSize; lM < paramCount; m += packetSize)
                {
                    if (m > paramCount) m = paramCount;
                    tC++;

                    BOT_MAIN.boardManagers[tC].SetPlayTexelVals(pDataset, lM, m, pCurBestParams, paramsLowerLimits, paramsUpperLimits, tC, tUL);
                    ThreadPool.QueueUserWorkItem(new WaitCallback(BOT_MAIN.boardManagers[tC].TexelTuningThreadedPackage));

                    lM = m;
                }
                packetSize = paramCount + 1;

                int tmod = 0;
                while (tC != BOT_MAIN.TEXELfinished)
                {
                    if (++tmod % 20 == 0)
                    {
                        WriteTexelParamsToTXT(tPath, BOT_MAIN.curTEXELPARAMS);
                        Console.WriteLine(CalculateAverageTexelCost(pDataset, BOT_MAIN.curTEXELPARAMS) + " | " + BOT_MAIN.TEXELadjustmentsmade);
                        Console.WriteLine(ULONG_OPERATIONS.GetBitVisualization(BOT_MAIN.TEXELfinishedwithoutimpovement));
                    }
                    Thread.Sleep(100);
                }
                Console.WriteLine("One Thread Generation FINISHED! :) :) :)");
                pCurBestParams = BOT_MAIN.curTEXELPARAMS;
            }

            //SetPlayTexelVals(pDataset, 0, paramCount, BOT_MAIN.curTEXELPARAMS, paramsLowerLimits, paramsUpperLimits);
            //TexelTuningThreadedPackage(0);

            sw.Stop();
            WriteTexelParamsToTXT(tPath, BOT_MAIN.curTEXELPARAMS);
            Console.WriteLine("END COST: " + CalculateAverageTexelCost(pDataset, BOT_MAIN.curTEXELPARAMS));
            Console.WriteLine(BOT_MAIN.TEXELadjustmentsmade);
            Console.WriteLine(sw.ElapsedMilliseconds);

        }

        // === CUR ===
        public void TexelTuningThreadedPackage(object obj)
        {
            Console.WriteLine("Started CThreadID [" + customThreadID + "] which is handling Params [" + threadFrom + "] - [" + threadTo + "]");

            bool firstIter = true;
            do
            {
                double bestAvrgCost = CalculateAverageTexelCost(threadDataset, threadParams);
                int adjust_val = firstIter ? 512 : 8;
                do
                {
                    adjust_val /= 2;
                    //Console.WriteLine("Adjust Value = " + adjust_val);
                    bool improvedAvrgCost = true;
                    while (improvedAvrgCost)
                    {
                        improvedAvrgCost = false;
                        for (int i = threadFrom; i < threadTo; i++)
                        {
                            bool improvedWithThisParam = true;
                            int tadjustval = adjust_val;

                            bool lastTimeMaximized = false, lastTimeMinimized = false;

                            while (improvedWithThisParam)
                            {
                                improvedWithThisParam = false;

                                int[] curParams = (int[])threadParams.Clone();
                                int paramBefore = curParams[i];
                                curParams[i] = Math.Clamp(paramBefore + adjust_val, paramsLowerLimits[i], paramsUpperLimits[i]);
                                double curAvrgCost = CalculateAverageTexelCost(threadDataset, curParams);
                                if (curAvrgCost < bestAvrgCost)
                                {
                                    improvedAvrgCost = true;
                                    improvedWithThisParam = true;
                                    Console.WriteLine("Improved Param [" + i + "] at CThreadID [" + customThreadID + "]: +" + adjust_val + " (" + GetDoublePrecentageString(bestAvrgCost - curAvrgCost) + ")");
                                    bestAvrgCost = curAvrgCost;
                                    threadParams = curParams;
                                    BOT_MAIN.TEXELadjustmentsmade++;
                                    BOT_MAIN.TEXELfinishedwithoutimpovement = ulAllThreadIDsUnfinished;
                                    if (lastTimeMaximized)
                                    {
                                        adjust_val *= 4;
                                        lastTimeMaximized = false;
                                    }
                                    else lastTimeMaximized = true;
                                    lastTimeMinimized = false;
                                }
                                else
                                {
                                    curParams[i] = Math.Clamp(paramBefore - adjust_val, paramsLowerLimits[i], paramsUpperLimits[i]);
                                    curAvrgCost = CalculateAverageTexelCost(threadDataset, curParams);
                                    if (curAvrgCost < bestAvrgCost)
                                    {
                                        improvedAvrgCost = true;
                                        improvedWithThisParam = true;
                                        Console.WriteLine("Improved Param [" + i + "] at CThreadID [" + customThreadID + "]: -" + adjust_val + " (" + GetDoublePrecentageString(bestAvrgCost - curAvrgCost) + ")");
                                        bestAvrgCost = curAvrgCost;
                                        threadParams = curParams;
                                        BOT_MAIN.TEXELadjustmentsmade++;
                                        BOT_MAIN.TEXELfinishedwithoutimpovement = ulAllThreadIDsUnfinished;
                                        if (lastTimeMinimized)
                                        {
                                            adjust_val *= 4;
                                            lastTimeMinimized = false;
                                        }
                                        else lastTimeMinimized = true;
                                        lastTimeMaximized = false;
                                        //Console.WriteLine("Improved Param " + i + ": -" + adjust_val);
                                    }
                                }

                                if (adjust_val != 1) adjust_val /= 2;


                                for (int j = threadFrom; j < threadTo; j++)
                                {
                                    BOT_MAIN.curTEXELPARAMS[j] = threadParams[j];
                                }

                                threadParams = (int[])BOT_MAIN.curTEXELPARAMS.Clone();
                                bestAvrgCost = CalculateAverageTexelCost(threadDataset, threadParams);

                            }

                            adjust_val = tadjustval;
                        }

                        //bool _b = false;
                        //for (int i = 0; i < 1152; i++)
                        //{
                        //
                        //}
                        //
                        //if ()
                        //threadParams = BOT_MAIN.curTEXELPARAMS;

                        //Console.WriteLine("(" + threadFrom + " - " + threadTo + ") End of one complete TLM Texel Improvement Cycle! [" + bestAvrgCost + "]");
                    }
                } while (adjust_val != 1); //128, 64, 32, 16, 8, 4, 2, 1
                BOT_MAIN.TEXELfinishedwithoutimpovement = ULONG_OPERATIONS.SetBitToZero(BOT_MAIN.TEXELfinishedwithoutimpovement, customThreadID);
                Console.WriteLine("FULLY FINISHED " + customThreadID + ". IMPROVEMENT THREAD PACKAGE CYCLE");

                firstIter = false;

            } while (BOT_MAIN.TEXELfinishedwithoutimpovement != 0);
            BOT_MAIN.TEXELfinished++;
        }

        // === CUR ===
        private void WriteTexelParamsToTXT(string pPath, int[] pParams)
        {
            try
            {
                string tS = "PARAMS: {";
                for (int i = 0; i < pParams.Length; i++) tS += pParams[i] + ",";
                tS = tS.Substring(0, tS.Length - 1) + "}\n";
                File.AppendAllText(pPath, tS);
            }
            catch
            {
                Console.WriteLine("Mal wieder TXT zu lange gebraucht um zu aktualisieren.");
            }
        }

        private double costSum = 0d;
        private int texelCostMovesEvaluated = 0;

        private double CalculateAverageTexelCost(List<TLM_ChessGame> tDataset, int[] pParams, bool pFullCost = true)
        {
            costSum = 0d;
            texelCostMovesEvaluated = 0;
            SetupTexelEvaluationParams(pParams);
            int tGameDataSetLen = tDataset.Count;
            int start = 0, end = tGameDataSetLen;
            if (!pFullCost)
            {
                end = (start = globalRandom.Next(0, end - 256)) + 256;
            }

            // HAD AN ISSUE: THREADED VERSION
            //int gamesPerThread = tGameDataSetLen / ENGINE_VALS.CPU_CORES;
            //int tMin = 0;
            //for (int i = 0; i < ENGINE_VALS.CPU_CORES - 1; i++)
            //{
            //    //CalculateAverageTexelCostThreadFunction(tDataset, tMin, tMin += gamesPerThread);
            //    //ThreadPool.QueueUserWorkItem(new WaitCallback(state => BOT_MAIN.boardManagers[i].CalculateAverageTexelCostThreadFunction(tDataset, tMin, tMin += gamesPerThread)));
            //}
            //CalculateAverageTexelCostThreadFunction(tDataset, tMin, tGameDataSetLen);
            //ThreadPool.QueueUserWorkItem(new WaitCallback(state => BOT_MAIN.boardManagers[ENGINE_VALS.CPU_CORES - 1].CalculateAverageTexelCostThreadFunction(tDataset, tMin, tGameDataSetLen)));

            //Console.WriteLine("===");

            for (int j = start; j < end; j++)
            {
                TLM_ChessGame tGame = tDataset[j];
                double tGR = tGame.gameResult;
                LoadFenString(tGame.startFen);
                int tL = 0; //= tGame.actualMoves.Count - 5;
                int m = tGame.actualMoves.Count - 5;
                for (int i = 0; i < m; i++)
                {
                    if (tL > 3) break;
                    if (tGame.isMoveNonTactical[i])
                    {
                        tL++;
                        double teval = TexelEvaluate();
                        double tcalc = TexelTuningSigmoid(teval);
                        //Console.WriteLine(tcalc + " - " + teval + " = " + TexelCost(tGR - tcalc));
                        costSum += TexelCost(tGR - tcalc);
                    }
                    PlainMakeMove(tGame.actualMoves[i]);
                }
                //Console.WriteLine(tL);
                texelCostMovesEvaluated += tL;
            }

            //Console.WriteLine("===");

            //Console.WriteLine(texelCostMovesEvaluated);

            //Console.WriteLine(texelCostMovesEvaluated);

            return costSum / texelCostMovesEvaluated;
        }

        private double CalculateAverageTexelCostThreadedVersionLEGACYDOESNTWORK(List<TLM_ChessGame> tDataset, int[] pParams)
        {
            Stopwatch sw = Stopwatch.StartNew();

            BOT_MAIN.TEXELcostSum = 0d;
            BOT_MAIN.TEXELfinished = 0;
            BOT_MAIN.TEXELcostMovesEvaluated = 0;
            int tGameDataSetLen = tDataset.Count;

            // HAD AN ISSUE: THREADED VERSION
            int gamesPerThread = tGameDataSetLen / ENGINE_VALS.CPU_CORES;
            //int tMin = 0;

            for (int i = 0; i < ENGINE_VALS.CPU_CORES - 1; i++)
            {
                //BOT_MAIN.boardManagers[i].SetPlayTexelVals(tDataset, tMin, tMin += gamesPerThread, pParams, paramsLowerLimits, paramsUpperLimits, 0, 0);
            }
            //BOT_MAIN.boardManagers[ENGINE_VALS.CPU_CORES - 1].SetPlayTexelVals(tDataset, tMin, tGameDataSetLen, pParams, paramsLowerLimits, paramsUpperLimits, 0, 0);

            //ThreadPool.QueueUserWorkItem(new WaitCallback(BOT_MAIN.boardManagers[ENGINE_VALS.CPU_CORES - 1].CalculateAverageTexelCostThreadFunction));
            for (int i = 0; i < ENGINE_VALS.CPU_CORES - 1; i++)
            {
                //CalculateAverageTexelCostThreadFunction(tDataset, tMin, tMin += gamesPerThread);
                //ThreadPool.QueueUserWorkItem(new WaitCallback(BOT_MAIN.boardManagers[i].CalculateAverageTexelCostThreadFunction));
            }
            //CalculateAverageTexelCostThreadFunction(tDataset, tMin, tGameDataSetLen);

            while (BOT_MAIN.TEXELfinished != ENGINE_VALS.CPU_CORES) Thread.Sleep(1);

            //for (int j = 0; j < tGameDataSetLen; j++)
            //{
            //    TLM_ChessGame tGame = tDataset[j];
            //    double tGR = tGame.gameResult;
            //    LoadFenString(tGame.startFen);
            //    int tL = tGame.actualMoves.Count - 5;
            //    texelCostMovesEvaluated += tL;
            //    for (int i = 0; i < tL; i++)
            //    {
            //        costSum += TexelCost(tGR - TexelTuningSigmoid(TexelEvaluate()));
            //        PlainMakeMove(tGame.actualMoves[i]);
            //    }
            //}

            //Console.WriteLine(texelCostMovesEvaluated);
            sw.Stop();
            //Console.WriteLine(":)");
            //Console.WriteLine(sw.ElapsedMilliseconds);


            return BOT_MAIN.TEXELcostSum / BOT_MAIN.TEXELcostMovesEvaluated;
        }

        public void SetPlayThroughVals(List<TLM_ChessGame> pDataset, int pFrom, int pTo)
        {
            threadDataset = pDataset;
            threadFrom = pFrom;
            threadTo = pTo;
        }

        // === CUR ===
        public void SetPlayTexelVals(List<TLM_ChessGame> pDataset, int pFrom, int pTo, int[] pTexelParams, int[] pLowerLimits, int[] pUpperLimits, int pThreadID, ulong pUL)
        {
            threadDataset = pDataset;
            threadFrom = pFrom;
            threadTo = pTo;
            threadParams = pTexelParams;
            paramsLowerLimits = pLowerLimits;
            paramsUpperLimits = pUpperLimits;
            customThreadID = pThreadID;
            ulAllThreadIDsUnfinished = pUL;
        }

        public void PlayThroughSetOfGames(object obj)
        {
            Console.WriteLine(threadFrom + "->" + threadTo);
            int sortedOut = 0;
            for (int j = threadFrom; j < threadTo; j++)
            {
                TLM_ChessGame tGame = threadDataset[j];
                LoadFenString(tGame.startFen);
                int tL = tGame.actualMoves.Count;
                for (int i = 0; i < tL; i++)
                {
                    int tQuietEval = isWhiteToMove ?
                        QuiescenceWhite(0, BLACK_CHECKMATE_VAL - 100, WHITE_CHECKMATE_VAL + 100, 0, PreMinimaxCheckCheckWhite(), NULL_MOVE)
                        : QuiescenceBlack(0, BLACK_CHECKMATE_VAL - 100, WHITE_CHECKMATE_VAL + 100, 0, PreMinimaxCheckCheckBlack(), NULL_MOVE);
                    bool tB;
                    tGame.isMoveNonTactical.Add(tB = TexelEvaluate() == tQuietEval && i > 4);
                    if (!tB) sortedOut++;
                    PlainMakeMove(tGame.actualMoves[i]);
                }
            }
            BOT_MAIN.TEXELsortedout += sortedOut;
            BOT_MAIN.TEXELfinished++;
        }

        public void CalculateAverageTexelCostThreadFunction(object obj)
        {
            //Console.WriteLine(pFrom + "->" + pTo);

            SetupTexelEvaluationParams(threadParams);
            double d = 0d;
            for (int j = threadFrom; j < threadTo; j++)
            {
                TLM_ChessGame tGame = threadDataset[j];
                double tGR = tGame.gameResult;
                LoadFenString(tGame.startFen);
                //Console.WriteLine(tGame.startFen);
                int tL = tGame.actualMoves.Count - 5;
                BOT_MAIN.TEXELcostMovesEvaluated += tL;
                for (int i = 0; i < tL; i++)
                {
                    d += TexelCost(tGR - TexelTuningSigmoid(TexelEvaluate()));
                    PlainMakeMove(tGame.actualMoves[i]);
                }
            }
            BOT_MAIN.TEXELcostSum += d;
            BOT_MAIN.TEXELfinished++;
        }

        private void ConsoleWriteLineTuneArray<T>(T[,][] pArr)
        {
            for (int j = 0; j < 3; j++)
            {
                Console.Write("{");
                for (int k = 0; k < 6; k++)
                {
                    Console.WriteLine("{");
                    for (int s = 0; s < 64; s++)
                        Console.Write(pArr[j, k][s] + ", ");
                    Console.WriteLine("\n}");
                }
                Console.WriteLine("}");
            }
        }

        private void Tune(string pGameCGFF, double pLearningRate)
        {
            TLM_ChessGame tGame = CGFF.GetGame(pGameCGFF);
            LoadFenString(tGame.startFen);

            double tDGameResult = tGame.gameResult;

            foreach (int hMove in tGame.hashedMoves)
            {
                Tune(tDGameResult, pLearningRate);

                List<Move> moveOptionList = new List<Move>();
                GetLegalMoves(ref moveOptionList);

                int tL = moveOptionList.Count;
                for (int i = 0; i < tL; i++)
                {
                    Move tMove = moveOptionList[i];
                    if (tMove.moveHash == hMove)
                    {
                        //Console.WriteLine(tMove);
                        PlainMakeMove(tMove);
                        tL = -1;
                        break;
                    }
                }

                if (tL != -1) break;
            }
        }

        private void Tune(double pGameResult, double pLearningRate)
        {
            int tPC = ULONG_OPERATIONS.CountBits(allPieceBitboard);
            double oneThroughPieceCount = 1d / tPC;
            double egMult = earlyGameMultipliers[tPC], mgMult = middleGameMultipliers[tPC], lgMult = lateGameMultipliers[tPC];
            double tTexelResult = TexelEvaluateF();
            double tSigmoidedTexelResult = TexelSigmoid(tTexelResult);
            double tDif = tSigmoidedTexelResult - pGameResult;
            double tM = oneThroughPieceCount * TexelCostDerivative(tDif) * TexelSigmoidDerivative(tTexelResult) * pLearningRate;

            sumCostVals += lastCostVal = TexelCost(tDif);
            costCalculations++;

            for (int i = 0; i < 64; i++)
            {
                if (ULONG_OPERATIONS.IsBitZero(allPieceBitboard, i)) continue;
                int tPT = pieceTypeArray[i] - 1, tI = i;
                if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, i)) tI = blackSidedSquares[i];

                //texelTuningVals[0, tPT][tI] -= egMult * tM;
                //texelTuningVals[1, tPT][tI] -= mgMult * tM;
                //texelTuningVals[2, tPT][tI] -= lgMult * tM;

                texelTuningAdjustIterations[tPT, tI]++;
            }
        }

        private double TexelEvaluateF()
        {
            // Set Positional Evaluation To Texel Vals
            texelTuningRuntimeVals = GetInterpolatedProcessedValues(texelTuningVals);

            // Evaluate Position almost normally; just with doubles
            return TexelEvaluate();
        }

        // Tapered Eval (0 - 24 based on remaining pieces) =>

        // IMPLEMENTED:
        // 1. Piece Existancy Values
        // 2. Piece Position Values

        // NEXT:
        // 3. King Safety (Somehow combined with mobility...)
        // 4. Piece Mobility (White / Black Squares?)
        // 5. Pawn Structure (Pawn Hashing)

        // Performance Goal: 3m EvpS (Single Threaded) (Es sind 800k EvpS xD; liegt an der Mobility)
        // ==> All Texel Tunable

        // FIX BOTH SIDED

        // Absichtlich anders eingerückt; übersichtlicher für das mehr oder weniger Herzstück der Engine
        private int TexelEvaluate()
        {
            int tEvalEG = 0, tEvalLG = 0, tProgress = 0;
            //ulong[] attkULs = new ulong[14];
            for (int p = 0; p < 64; p++)
            { //+= forLoopBBSkipPrecalcs[(allPieceBitboard >> p >> 1) & sixteenFBits]
                if (((int)(allPieceBitboard >> p) & 1) == 0) continue;
                int aPT = pieceTypeArray[p], tPT = aPT + 7 * ((int)(blackPieceBitboard >> p) & 1); //, mobilityStraight = 0, mobilityDiagonal = 0;
                tEvalEG += texelTuningRuntimePositionalValsV2EG[tPT][p] + texelPieceEvaluationsV2EG[tPT];
                tEvalLG += texelTuningRuntimePositionalValsV2LG[tPT][p] + texelPieceEvaluationsV2LG[tPT];
                //switch (aPT) {
                //    case 1: attkULs[tPT] |= tPT == 8 ? blackPawnAttackSquareBitboards[p] : whitePawnAttackSquareBitboards[p]; break;
                //    case 2:
                //        mobilityDiagonal = rays.DiagonalRaySquareCount(allPieceBitboard, p);
                //        mobilityStraight = rays.StraightRaySquareCount(allPieceBitboard, p);
                //        attkULs[tPT] |= knightSquareBitboards[p]; break;
                //    case 3:
                //        mobilityDiagonal = rays.DiagonalRaySquareCount(allPieceBitboard, p, ref attkULs[tPT]);
                //        mobilityStraight = rays.StraightRaySquareCount(allPieceBitboard, p); break;
                //    case 4:
                //        mobilityDiagonal = rays.DiagonalRaySquareCount(allPieceBitboard, p);
                //        mobilityStraight = rays.StraightRaySquareCount(allPieceBitboard, p, ref attkULs[tPT]); break;
                //    case 5:
                //        mobilityDiagonal = rays.DiagonalRaySquareCount(allPieceBitboard, p, ref attkULs[tPT]);
                //        mobilityStraight = rays.StraightRaySquareCount(allPieceBitboard, p, ref attkULs[tPT]); break;
                //    case 6:
                //        mobilityDiagonal = rays.DiagonalRaySquareCount(allPieceBitboard, p);
                //        mobilityStraight = rays.StraightRaySquareCount(allPieceBitboard, p);
                //        attkULs[tPT] |= kingSquareBitboards[p]; break;
                //}
                //if (aPT != 1) {
                //    tEvalEG += texelMobilityStraightEG[tPT, mobilityStraight] + texelMobilityDiagonalEG[tPT, mobilityDiagonal];
                //    tEvalLG += texelMobilityStraightLG[tPT, mobilityStraight] + texelMobilityDiagonalLG[tPT, mobilityDiagonal];
                //}
                tProgress += pieceTypeGameProgressImpact[tPT];
            }
            //ulong ksr1W = kingSafetySpecialRingW[whiteKingSquare], ksr1B = kingSafetySpecialRingB[blackKingSquare];
            //int wpt1r1 = ULONG_OPERATIONS.CountBits(ksr1W & attkULs[8]), wpt2r1 = ULONG_OPERATIONS.CountBits(ksr1W & attkULs[9]), wpt3r1 = ULONG_OPERATIONS.CountBits(ksr1W & attkULs[10]), 
            //    wpt4r1 = ULONG_OPERATIONS.CountBits(ksr1W & attkULs[11]), wpt5r1 = ULONG_OPERATIONS.CountBits(ksr1W & attkULs[12]), wpt6r1 = ULONG_OPERATIONS.CountBits(ksr1W & attkULs[13]);
            //int bpt1r1 = ULONG_OPERATIONS.CountBits(ksr1B & attkULs[1]), bpt2r1 = ULONG_OPERATIONS.CountBits(ksr1B & attkULs[2]), bpt3r1 = ULONG_OPERATIONS.CountBits(ksr1B & attkULs[3]), 
            //    bpt4r1 = ULONG_OPERATIONS.CountBits(ksr1B & attkULs[4]), bpt5r1 = ULONG_OPERATIONS.CountBits(ksr1B & attkULs[5]), bpt6r1 = ULONG_OPERATIONS.CountBits(ksr1B & attkULs[6]);
            //if (isWhiteToMove) {
            //    tEvalEG += texelKingSafetySREvaluationsPT1EG[wpt1r1] + texelKingSafetySREvaluationsPT2EG[wpt2r1] + texelKingSafetySREvaluationsPT3EG[wpt3r1]
            //            + texelKingSafetySREvaluationsPT4EG[wpt4r1] + texelKingSafetySREvaluationsPT5EG[wpt5r1]  + texelKingSafetySREvaluationsPT6EG[wpt6r1] 
            //            - texelKingSafetySREvaluationsPT1EG[bpt1r1]  - texelKingSafetySREvaluationsPT2EG[bpt2r1] - texelKingSafetySREvaluationsPT3EG[bpt3r1]
            //            - texelKingSafetySREvaluationsPT4EG[bpt4r1] - texelKingSafetySREvaluationsPT5EG[bpt5r1] - texelKingSafetySREvaluationsPT6EG[bpt6r1];
            //        
            //    tEvalLG += texelKingSafetySREvaluationsPT1LG[wpt1r1] + texelKingSafetySREvaluationsPT2LG[wpt2r1] + texelKingSafetySREvaluationsPT3LG[wpt3r1]
            //            + texelKingSafetySREvaluationsPT4LG[wpt4r1] + texelKingSafetySREvaluationsPT5LG[wpt5r1] + texelKingSafetySREvaluationsPT6LG[wpt6r1]
            //            - texelKingSafetySREvaluationsPT1LG[bpt1r1] - texelKingSafetySREvaluationsPT2LG[bpt2r1] - texelKingSafetySREvaluationsPT3LG[bpt3r1]
            //            - texelKingSafetySREvaluationsPT4LG[bpt4r1] - texelKingSafetySREvaluationsPT5LG[bpt5r1] - texelKingSafetySREvaluationsPT6LG[bpt6r1];
            //} else {
            //    tEvalEG -= texelKingSafetySREvaluationsPT1EG[wpt1r1] - texelKingSafetySREvaluationsPT2EG[wpt2r1] - texelKingSafetySREvaluationsPT3EG[wpt3r1]
            //            - texelKingSafetySREvaluationsPT4EG[wpt4r1] - texelKingSafetySREvaluationsPT5EG[wpt5r1] - texelKingSafetySREvaluationsPT6EG[wpt6r1]
            //            + texelKingSafetySREvaluationsPT1EG[bpt1r1] + texelKingSafetySREvaluationsPT2EG[bpt2r1] + texelKingSafetySREvaluationsPT3EG[bpt3r1]
            //            + texelKingSafetySREvaluationsPT4EG[bpt4r1] + texelKingSafetySREvaluationsPT5EG[bpt5r1] + texelKingSafetySREvaluationsPT6EG[bpt6r1];
            //
            //    tEvalLG -= texelKingSafetySREvaluationsPT1LG[wpt1r1] - texelKingSafetySREvaluationsPT2LG[wpt2r1] - texelKingSafetySREvaluationsPT3LG[wpt3r1] 
            //            - texelKingSafetySREvaluationsPT4LG[wpt4r1] - texelKingSafetySREvaluationsPT5LG[wpt5r1] - texelKingSafetySREvaluationsPT6LG[wpt6r1] 
            //            + texelKingSafetySREvaluationsPT1LG[bpt1r1] + texelKingSafetySREvaluationsPT2LG[bpt2r1] + texelKingSafetySREvaluationsPT3LG[bpt3r1] 
            //            + texelKingSafetySREvaluationsPT4LG[bpt4r1] + texelKingSafetySREvaluationsPT5LG[bpt5r1] + texelKingSafetySREvaluationsPT6LG[bpt6r1];
            //}
            if (tProgress > 24) tProgress = 24;
            return (int)(earlyGameMultipliers[tProgress] * tEvalEG + lateGameMultipliers[tProgress] * tEvalLG);
        }

        private const double TexelK = 0.2;
        private double TexelTuningSigmoid(double pVal)
        {
            return 2d / (1 + Math.Pow(10, -TexelK * pVal / 400));
        }

        private double TexelSigmoid(double pVal)
        {
            return 2d / (Math.Exp(-0.04d * pVal) + 1d);
        }

        private double TexelSigmoidDerivative(double pVal)
        {
            double d = Math.Exp(-0.04d * pVal);
            return 2d * d / (25d * (d + 1d) * (d + 1d));
        }

        private double TexelCost(double pVal)
        {
            return pVal * pVal;
        }

        private double TexelCostDerivative(double pVal)
        {
            return 2 * pVal;
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
            ClearTTTable();
            RESET_NNUE_FIRST_HIDDEN_LAYER();
            shouldSearchForBookMove = true;
            chessClock.Reset();
            debugFEN = fenStr;
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
            for (int i = 0; i < crStrL; i++)
            {
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
            int[] pieceTypeCounts = new int[7];
            for (int i = rowSpl.Length; i-- > 0;)
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


                        int tpieceType = Array.IndexOf(fenPieces, Char.ToLower(tChar));
                        pieceTypeCounts[tpieceType]++;
                        pieceTypeArray[tSq] = tpieceType;

                        UPDATE_NNUE_FIRST_HIDDEN_LAYER_ON((tCol ? 64 : 0) + (tpieceType - 1) * 128 + tSq);

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

            countOfPiecesHash = pieceTypeCounts[5] << 20 | pieceTypeCounts[4] << 16 | pieceTypeCounts[3] << 12 | pieceTypeCounts[2] << 8;
            zobristKey ^= enPassantSquareHashes[enPassantSquare];

            allPieceBitboard = whitePieceBitboard | blackPieceBitboard;

            if (zobristKey == 16260251586586513106) moveHashList.Clear();
            else
            {
                moveHashList.Clear();
                moveHashList.Add(1);
                moveHashList.Add(2);
                moveHashList.Add(3);
            }
        }

        #endregion

        #region | PRECALCULATIONS |

        private ulong[,] pieceHashesWhite = new ulong[64, 7], pieceHashesBlack = new ulong[64, 7];
        private ulong blackTurnHash, whiteKingSideRochadeRightHash, whiteQueenSideRochadeRightHash, blackKingSideRochadeRightHash, blackQueenSideRochadeRightHash;
        private ulong[] enPassantSquareHashes = new ulong[66];

        private int[] squareToRankArray = new int[64]
        {
            0,0,0,0,0,0,0,0,
            1,1,1,1,1,1,1,1,
            2,2,2,2,2,2,2,2,
            3,3,3,3,3,3,3,3,
            4,4,4,4,4,4,4,4,
            5,5,5,5,5,5,5,5,
            6,6,6,6,6,6,6,6,
            7,7,7,7,7,7,7,7
        };

        private Dictionary<ulong, int> singleCheckCaseBackup = new Dictionary<ulong, int>();

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

        private ulong[] kingSafetyRing1 = new ulong[64], kingSafetyRing2 = new ulong[64];
        private ulong[] kingSafetySpecialRingW = new ulong[64], kingSafetySpecialRingB = new ulong[64];

        private ulong[] rowPrecalcs = new ulong[8], columnPrecalcs = new ulong[8];

        const ulong columnUL = 0x101010101010101, rowUL = 0b11111111;

        private int[] forLoopBBSkipPrecalcs = new int[65536];
        private const ulong sixteenFBits = 0b1111_1111_1111_1111;

        private void PrecalculateForLoopSkips()
        {
            for (int u = 0; u < 65536; u++)
            {
                forLoopBBSkipPrecalcs[u] = 17;
                ulong ul = (ulong)u;
                for (int i = 0; i < 16; i++)
                {
                    if (ULONG_OPERATIONS.IsBitOne(ul, i))
                    {
                        forLoopBBSkipPrecalcs[u] = i + 1;
                        break;
                    }
                }
            }
        }

        private void PrecalculateKingSafetyBitboards()
        {
            for (int i = 0; i < 8; i++)
            {
                rowPrecalcs[i] = rowUL << (8 * i);
                columnPrecalcs[i] = columnUL << i;
            }

            for (int i = 0; i < 64; i++)
            {
                int tMod = i % 8;
                int tRow = (i - tMod) / 8;
                ulong tULC = columnPrecalcs[tMod], tULR = rowPrecalcs[tRow];
                ulong tULC2 = columnPrecalcs[tMod], tULR2 = rowPrecalcs[tRow];

                if (tMod < 7) tULC |= columnPrecalcs[tMod + 1];
                if (tMod > 0) tULC |= columnPrecalcs[tMod - 1];
                if (tRow < 7) tULR |= rowPrecalcs[tRow + 1];
                if (tRow > 0) tULR |= rowPrecalcs[tRow - 1];
                if (tMod < 6) tULC2 |= columnPrecalcs[tMod + 2];
                if (tMod > 1) tULC2 |= columnPrecalcs[tMod - 2];
                if (tRow < 6) tULR2 |= rowPrecalcs[tRow + 2];
                if (tRow > 1) tULR2 |= rowPrecalcs[tRow - 2];

                tULR2 |= tULR;
                tULC2 |= tULC;

                kingSafetyRing1[i] = ULONG_OPERATIONS.SetBitToZero(tULR & tULC, i);
                kingSafetyRing2[i] = ULONG_OPERATIONS.SetBitsToZero(tULR2 & tULC2, i, i + 1, i - 1, i + 7, i + 8, i + 9, i - 7, i - 8, i - 9);
            }

            for (int i = 0; i < 64; i++)
            {
                if (i < 56) kingSafetySpecialRingW[i] = ULONG_OPERATIONS.SetBitToZero(kingSafetyRing1[i] | kingSafetyRing1[i + 8], i);
                else kingSafetySpecialRingW[i] = kingSafetyRing1[i];

                if (i > 7) kingSafetySpecialRingB[i] = ULONG_OPERATIONS.SetBitToZero(kingSafetyRing1[i] | kingSafetyRing1[i - 8], i);
                else kingSafetySpecialRingB[i] = kingSafetyRing1[i];
            }
        }

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
        public int[] squareConnectivesCrossDirsPrecalculationArray { get; set; } = new int[4096];
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
        public void SNAPSHOT_WRITLINE_REPLACEMENT() { }
        public void SNAPSHOT_WRITLINE_REPLACEMENT(object pStr) { }
        private string GetDoublePrecentageString(double pVal)
        {
            return ((int)(pVal * 1_000_000) / 10_000d) + "%";
        }

        private string ReplaceAllULONGIsBitOne(string pCode)
        {
            const string METHOD_NAME = "ULONG_OPERATIONS.IsBitOne";
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
}
