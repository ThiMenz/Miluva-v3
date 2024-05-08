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
        private double[] NNUE_FIRST_LAYER_BIASES = new double[16] { 7.313147795421105, -3.2198240743925544, 4.371314927184611, -11.687397612604206, 2.2616931791319046, -.039753870204836575, -6.2413273874352, 1.5309420087844987, -5.388626084651485, 6.958896781372488, .07729432768863823, 13.539009703980918, 3.838243736737551, -2.0578005417411926, 9.32960642497906, 7.010575632777833 };
        private double[,] NNUE_FIRST_LAYER_WEIGHTS = new double[768, 16] { { .8322540449191282, .06310394196634439, .8150647182022621, .36944795071278147, -.17185309016691352, -.8435954234798781, -.7154275523815463, .48043975194910016, -.5353358276536622, -.954977505375447, .7951166975869315, -.044492008720040666, .7921464296938949, -.9596875001682534, .5516930118864818, -.8807665350336602 }, { -.5808592465700544, .004259842657056145, -.620457784641258, -.23828776858289102, -.4750988190100376, .5773030018330787, .44766557609761337, .25231438902482983, .7274898530059355, -.9872535082464782, .6214400147292174, -.8998856933236807, .5419352046940957, .6126169282902243, .32571119836132967, .4708999750145908 }, { -.8507024891834749, -.14008682821239016, .44983305585180466, .5758567303409319, -.0787856432003573, .5473355761523448, .8404106308233215, -.12467121869897291, .07592238286958075, .20186005119994532, -.2630071360811028, .2880384838304506, -.06709946930981459, .9202007542032815, .1254512079979464, -.8329185134812176 }, { -.0397307653567065, .20949711980828734, .25820516560273954, .9328016111645541, .5152576811824274, .6218596010358208, .37076904218758355, -.915167915568885, -.9956053414040609, -.132136713037472, -.06075441231154444, -.6015873068425415, -.4627732917964229, -.2741827573603719, -.8631116451151051, -.33267676266016166 }, { .8148704624333216, -.5781012918276496, .6953870807948077, -.656269308320593, -.1886006950153143, -.06228399446993249, -.9926239928523823, .8096496630599488, .805792465644392, -.47576104485150594, -.638813921118627, -.32106366231357497, -.6661518417004604, .030749931329606683, .3671400468911066, .6824346406626312 }, { -.15252010503146218, -.13540508896732417, -.7294075482100324, -.18898576318474491, .7266170152279994, .11391751548275542, .9753266837911243, .0975115793017316, -.8488743829707375, -.059243747739806674, -.4645780839593039, -.6508825002000442, -.9141339562132076, -.8605201923848784, .5858227412958041, .9756446854642795 }, { -.9197401056331844, .7369036163585385, -.0788389626993038, .5834484998495806, .9097583707377479, .037926667188025176, -.3094373537865487, -.8152530990596336, .9869674356455347, .7808059182376006, -.05303218464061743, -.9969907976440913, .17397938166868587, -.5704463038906011, .6526053709171009, .27068339506285977 }, { -.16110130811682577, -.9922763740459632, -.7920558178687827, .501969826811314, .5462671143649618, -.5246525283936303, -.5778718386540254, -.42562948851742877, .5976706871996653, .8106550384437243, -.6267863970664047, -.8882579202990166, -.04896814977238062, .2867526187126781, .07182749292415003, -.39880140158254207 }, { -.8257539859670082, .1571268420174454, 2.1600753202244745, 3.8054512304311072, 1.4062324251843055, -.22532395568017785, -.6978816465836947, -1.2564474696197105, -.652316163795797, -2.904461942065356, .7549643573180802, .5267010817438287, -.6622363276743239, 2.369748737734003, -.5750073464196906, 1.6873129640189555 }, { -3.738254359339598, 1.598963119445879, .8460633523140793, 3.473682461487992, 1.277887083574483, -.6270925710148483, -1.006514599823572, -2.132504904084237, -1.6124394195933807, -1.7696264364124201, -1.7334643423212068, -.1426378134283831, -1.6584324255690224, -.8625301318253942, -.8124142809355657, 1.7530207423099873 }, { -3.8633084261727406, .8895203770090968, .9431789998702523, 3.564639809203391, 1.0665251734422874, -.34954363753763845, 1.351514473288495, -1.8023543725811129, -.570178194177816, -.7566081910090818, .09594162790115714, 1.580562277780141, -1.8771655200060642, .9337248810604608, .6365330677369553, 1.8450885104753736 }, { -2.2451492500404466, .4711714384680723, 1.6352092427576401, -.39102299041659816, -.800786247232429, .9133570684240232, .5719163380588821, -.940618846608394, -1.5080484230064861, -.8431516145053123, .4982506766649099, .049562859193921, -.2822575539023902, -1.1467267765218632, .8319139943691459, -.16154356814482018 }, { -1.6206004772845273, .12228265867098988, 2.0856245127036988, 4.598411198668566, -.6620252609329161, -.46236782635427237, .2262967106779794, -3.25219993616653, -.8512665331403644, -3.155330672532832, -1.2118731682633053, -1.4735719780633132, -6.316960906811798, -.84482368942071, -.6176202229528859, .12457610961730657 }, { -2.673013217007843, 2.374057441852166, 4.460890211694329, 5.026350929304245, .9840987698252054, -.19918944440779104, .16220172444648928, -.8532638480709239, -1.31654410052686, .20391581429111538, -.7681628157645886, -2.3978882431797754, 1.0436548209270342, 1.0578679759147451, -.42194414670746816, -1.0731565313642994 }, { 2.012375884765571, 1.9961707688728516, 3.5133657140033185, 3.0613555125893095, 1.2623141594846194, -.08125755739932582, .9353011434867325, -.3472957682964186, .322686449282731, -.746690193130519, -1.1107367886691097, -3.8413796331587005, -3.2372995407080296, -2.6342677450519347, -.3223697835563129, -1.1656575307996375 }, { 1.0029742489596507, .7048144339486954, 2.933982416615799, 3.4967674046199004, 1.76647346375169, -.22341628882417583, .18781058730404654, .5100038490576256, .7939404610886663, -3.642843792719055, -1.82629869612281, -.6188262074460011, .7084121129340958, .44866279702143935, .14039334530696032, -.7320319010993556 }, { -1.9905752536337513, .6678705720372248, 2.6102889534245466, 3.8079347954209095, 2.011594391274698, -.5319402650967497, -.47280782228283635, -.7693052620956993, -.32425181332886543, -2.065381309431256, 1.2132921015428595, 1.371853344890251, -.33834021827966176, 2.5963682834856154, -.09345275619059515, 2.0206119596495995 }, { -3.444494095128266, .4086545266661135, 1.7162578025655895, 5.743595654868548, 1.2644724150617828, .48772471879738005, -.7241753503818396, -1.7596770814052427, -.7386930921482524, -.2333687213370049, .7029724662791509, -.21394778614142293, -.3438192818812871, -1.1309103777958198, -.3448451475889215, 1.6106546520984402 }, { -1.3843206302681246, 1.63904884932396, 1.129501953163799, 4.191354505590416, -.9750390095214382, -.3246654793260965, -1.163054899010211, -2.9348540750133423, -1.32097033868474, -2.440909302222896, -.2871049545014499, 1.152760920525604, -1.8697945777077827, .6028302150611408, -.4293196808673954, 1.4042184147909313 }, { -2.5158109502481945, 3.037267211021748, .6501795798635958, 2.074174169559318, -2.5894650555464356, -.2988789242820876, -1.4288813497972928, -4.178268854304928, 1.183854863689792, -2.2216928093785557, -1.4491332356692728, -.3455526495339003, 2.4016467962811627, -1.353317287590021, .7536354084434722, 1.0487383139471649 }, { -.5217781835357999, 1.59483162253355, 1.6330921311653985, 3.3133812140040924, 1.3595171112793332, -.29029547602026334, -.050684062712833655, -2.149314834044328, -.9713578492185698, -2.915306735513757, -1.6908375129335178, .01377447897125306, -5.438326265407073, -.06416273422688037, -.6508513087404226, .4874422649250307 }, { -3.1975440776058672, .20049044186004583, 2.672769776643432, 5.129898491924395, .9467710771020481, -1.1005998995269985, -.5743921336338063, -1.5794836120100118, .6921003374442196, .42150661272038614, -.476658264682712, -1.4232586295978953, 1.2942175158728408, .8198788393292694, .07293251783793339, .6621739634326255 }, { .9216495852590431, .8720973709624174, 3.818732200293504, 4.62014955419297, .9385486090630052, .28542097514263215, 1.2820927846275845, .2781877406139851, .06216473568808649, -.6607240975772538, .34247553466603037, -2.824351905922079, -3.7956842561915045, -2.133891440460716, .04454888711258188, -.4653386447064405 }, { -.5377562038380984, .9337697270766817, 2.5494356516152576, 3.4857316213375547, .5781128116633968, -1.2188718416435618, -.3859744926231815, 1.3445818487691032, 1.4744067647443413, -1.80727824741814, -.48008373545200883, -.28096869828623255, 1.1057731596980331, .5988834017396221, .7134685520273899, -1.0865347389430005 }, { -2.7064481226578234, .3999195353631119, 3.1229426932092763, 2.6439781456921794, .6391469837841853, .343434726792732, -1.8704084750211232, -2.3338140802776848, .7574114570532787, -1.7807787993498374, 1.1768240536027212, .6449229454837914, -1.5347523909087826, 2.4177609161464724, .04810600106368121, .8056026926792264 }, { -3.6981588567145187, 1.9742285445566237, 3.7444630065302023, 4.566808122274162, -.08987385801400222, -.5893193230432335, -.06699143932388299, -2.783904928001611, -.03984052338669727, -1.3154912001559258, 2.2216013195353983, .27118672182461484, -.07234451709141097, -2.51450314328526, -.8175744722035699, 1.5483531046753267 }, { -1.5593792259720045, .8603763580611181, .7057248753407834, 4.856043099105825, .8312814089407699, 1.0415429833392762, -.525688339555517, -.1826702419194615, 2.2236748872191923, -3.012136929699526, -.26696580928759944, .8531806155017926, -4.022677280865532, 3.472785813997092, -.3248832346637469, .9986630583049564 }, { -4.722957018397137, 2.442281206483961, 1.7863293134656852, 2.3986171944237915, -2.1836794213363406, -.7563461311157793, -1.589711028571859, -3.179503916082943, 1.9018880873687298, -.5785425083540365, -.6564899660298579, -1.5758497132678906, 2.9874452924623505, -5.861175096778739, -.7880364631533043, 1.1696064799322494 }, { .8805575898260378, 2.086890679758165, 1.3311157696031355, 4.3896547065477725, .01756589411929586, -.36152082341919584, .05481778373234805, -3.1476737050221617, 1.5702295930662549, -3.678545942084125, .47165565589455255, -.6604840269130796, -8.495678168089169, 2.117014854281338, -.4815973348075822, .061968072576097565 }, { -2.9945610491437797, .6820061503187138, 3.305853087245277, 6.177506948925304, .5227841658148549, -1.8571387475668786, .39295704972142903, -.5988557151491655, .07213481117230847, -.5213909747186102, 1.1421098091993906, -2.2707380209028716, 3.08621120705133, -2.041068479070508, -.7546560963215277, 1.8071758610528825 }, { 2.9601110940418076, -.1381610993823395, 3.5673330574829833, 4.957100642043866, 1.5809900394251537, .5932146847881051, .9618017097096191, .9401911570185173, .09067771569142531, -1.3304780652709511, .5168081735868794, -.7204618340052235, -3.2776376869146575, .17717028408551194, -.2079163190874039, 1.1868040554931403 }, { -.02762739845617762, .5069364356241085, 2.887895604502491, 3.2174159029874616, 1.0996366265079618, -.8547026233172071, -.17778779203123873, -.5979980259976428, .9352214590282352, -2.781989254871365, .13867828038105248, -.28685498566477696, 1.4557818565683551, -.3218817548548868, .8913125606391181, .18381406548261162 }, { -1.4974102883123088, .796160458553969, 3.547544870541052, 3.194108579456597, 1.4578848286945068, .048718114121999334, -2.049603577356143, -2.4021842727061147, 2.647354356414289, -1.4584016160791562, 2.546530237551528, -.4593232812541764, -1.2789889260220946, 1.5239426629591917, -.4822093272637804, .9877530416975309 }, { -2.3691664204285305, 3.0375664395307265, 3.439459515379964, 3.2535682844104503, 1.4295772819203372, .19394288409831692, -.2955953063910477, -2.904727236768726, 2.95776304091227, -.5851792384869189, .9878324671260357, .8863847313109121, -1.4819855909922954, -.5733184940430858, -.29506945859544287, .2470551256569691 }, { -2.3483934905284842, 3.2649592660334332, 2.3622127967797657, 1.6002664865146425, 1.3051196062992976, -2.6534897666459147, -.9198442032406714, -1.726159980061504, 4.540778234268576, -1.333351939966812, .27531354196192515, .0006913979831209525, -4.390605469093932, 2.673859784207316, -1.0005462136254728, 1.1376682392363835 }, { -2.7203530941857212, 1.5749085242811085, .2879418027099437, 3.328119329718941, .7646509811460017, -3.04782865932689, .871827314772858, -2.8272052061928545, 7.4977952065432545, -1.5310658384542826, -1.1482646229383795, 1.1814668462177844, -.22855317920746882, -4.744517444142534, .43011067258694213, 1.078378880369846 }, { .8190849629041366, 2.2648714186098466, 1.7002032238726361, 5.765812588867815, -.330802181833649, 1.2625078404695305, -2.100424899691103, -.4939760757427255, 3.1555986692821443, -.2925784966687773, .21061240576385964, -1.951975772379496, -4.512112873132665, 6.559628067921326, -.5226842630708731, 2.4013115603680912 }, { -2.4287016819377643, 3.1251844774564774, 1.406130824132064, 3.5590269649727895, 1.0432067358402946, 1.2570244511303001, -.649020656387994, .1104637211834575, .0048664450002990954, -.04153631778402723, 1.162651489005889, -.09182935504652616, 1.2778286992332182, -3.217119043419321, -.7324377129346682, 1.9509485191347486 }, { .6571246438744732, 1.783299457913604, 1.9215550992834627, 4.256847052768754, 1.8101738207259384, .6253702362223961, -.0055591156029943375, -1.6019575720468626, .4887332207008078, -1.2650035675997255, 1.2403732266213245, -1.1473903247646298, -.20908056004589523, -.21201327804935613, .989850638675934, 2.675341262016497 }, { -.32756280400291693, 1.8516806700697193, 2.237375016151281, 3.1824384141434154, 1.0863460502314495, -1.036768645629196, .03741731387069754, -1.302442430409574, 1.5874454979225678, -4.129427553658622, 1.0312409148777693, .1416978012903132, 1.214568476863491, -.725779736881495, .0761806022647109, .09130436710918757 }, { -1.432364805257677, 1.0809214451050682, 5.49322152126685, .19264995321084907, 2.4321370316215987, .7135177781167662, -2.387554499121828, -.5642171834701967, 4.505772288325091, -1.33982562673521, -.6823731753728735, .3012849226417537, -1.2359822191196566, 1.411538179142203, .6438527058547581, .5148738092660486 }, { -3.1387524918544196, 2.0994335997438793, 5.712826547676298, .6551777837114251, 2.1390905985836666, 1.0245863079125992, -2.95526972331598, -2.8743320183233987, 3.573893271207854, .9884897372349782, .23101214445020038, -.1278265815719847, -1.9405269093597022, 1.228636421341224, 1.0416573099137671, 1.88027502241206 }, { -1.833245018548813, 5.110477476698265, 3.281724816190741, .9421811227725143, -.79368814539639, 1.5314850837716838, -3.366454362657193, -.7573900393797705, 6.917085491068148, -.2714537629679558, -.28263303889450325, .7269687985735216, -4.4667072592123755, -.6011557553940052, -.490198965873218, 1.4444050864976505 }, { -1.8852524531257853, 4.945799353271016, 1.0532379218094392, 2.1452607079456367, .44921786829002514, 1.2253461395668996, -1.5868342322013151, -.047897324517112726, 3.79150882934425, .48992688942062046, 1.0988796891761887, 2.8536225519733978, -.13146513367487656, -4.290107533718423, 1.2847808482230523, 3.4195651316613493 }, { .5614946173946362, 3.3129271901852753, -.41304107379375893, 1.1891735877271667, 1.5332420015010213, .7386490313393935, .3437196320601771, 1.3665655737688465, 4.470357260631932, .8046373957647817, 1.0289986615022029, -1.8117509454932383, -5.039196203524969, -.15415954979519048, -.27813236854062295, 3.0802838593474733 }, { -1.0245830556724713, 2.9102836980314364, .26506976171053875, .9415921208949002, 1.8287317515766217, 2.6782834155650477, -.699586269816432, -.40833253015118204, .25953955762992886, .8824379091005606, .15398506336273923, .52118204785878, 2.4131036020094334, -.876713005202585, .9602079274915172, 6.747443011793223 }, { .41102461126335926, 5.451017762020221, -.5540740879249261, .7894857815515903, 3.6801257318020704, -.7772631454821536, -.3234753175548228, -.3677991589667547, 1.3020433723823581, -.8368598835150564, .7616425628104173, -1.529412537622776, -1.088903536592406, -1.0273939814813198, .6439930087138801, 3.3406590663015048 }, { -1.8918445352966562, 3.4625076925587015, 3.5682135887587134, 3.7416068089657624, 3.265362683237877, -.7543926760212929, -1.1752325877238037, -.896437689982922, .32038421658076793, -2.930795191409491, 1.3722194647985053, 1.0238481738633822, 1.6142752280457227, -.9345345079678787, .8746434853790402, 1.5615323839428532 }, { -.04420673105676141, 3.898511684589718, 5.024267037918854, 1.5933612129608488, -2.9157348594887362, .6963771217682442, -3.756225855921257, -.033874553847351226, 2.296863433199878, -.612444910325452, 1.4111336809918158, .5663123838223195, -1.3990569683932308, .07099885078524217, 4.501582494839544, .07466443804383135 }, { -2.246108805823591, 4.008333499154021, 4.688268931583942, 1.9917487259282822, -.26362051877325304, 1.0803954325500353, -4.315181404573617, 1.0582072303447423, 1.786281682965452, .6766792162468434, .790473409151958, .11866050459309924, -2.503242642488908, -.9902469737801896, 3.4404434665836168, .46813845761633394 }, { -1.3538586543154827, 3.9611691792954744, 4.119457835095616, 2.970488667482607, -.011181045678081005, -.43164357155380223, -3.03386004866069, -.5475354215373697, .9638377952255082, -2.181952792553314, -1.237479982384742, .34351676223995253, -1.5627005512931427, .0983548858959916, 2.882564098997556, 5.247725178770413 }, { .48335468026109274, 4.278193440394095, 1.99211469734038, 2.828600056845871, 2.818215796811543, .2373136643304192, -3.720365601274444, 2.9843536088243354, -1.7474761461048673, 1.7264862945433304, .6521720408267135, -1.333118231871611, -4.386139546855828, -1.770789267604162, 3.564485422105859, .7600191245610639 }, { .028795950107516448, 3.9699472705738548, 2.330445021433823, .6328557843963207, .4838023300314335, .2983815679524958, -2.504416131380621, 3.2021284298205495, 1.6544904425290003, -.11557452626554186, -.3775871443646169, .05302114394399245, .30749241473971967, -1.3113592929055395, 3.0387778631691624, 4.570799181754892 }, { -1.6107973683206698, 2.2750082142254726, .9714039381513806, .11575135277127493, 1.5773867442512464, 1.764698742886524, -1.7022051867705934, 1.1468512485044755, .40311901986890014, -.9082709784192649, .22093672844855858, -2.020916533195157, -1.4004900982305735, .22707982912377675, 1.441405466112764, 3.2961094351200897 }, { -.0985667779924625, 2.132781773713788, -.8229733471631187, -.9256399625313778, .8869566860544629, -.1088419134566996, -.9173021190428535, -.7232140357494484, .5377990771385213, -2.723075916325728, 1.1767104927370757, -.5158004776954337, -.35598112253085207, -1.4584786940426748, 3.5297841016208444, 1.4236644200051252 }, { 1.8889788555747185, 2.489326405668999, 1.8907619502618949, .6408376936432505, 1.5591766053835965, 1.7115437072074458, -1.2904428308093634, -.5762220009334705, .20552125616855524, -4.491647939578795, .560916704487071, -2.431396702686272, .3541141668797144, -.17441999132910257, .9233581713367305, 3.143290384625925 }, { .0255641858028509, 1.5303136640268065, .33566665548339036, -.4439868105166903, -.3509254568209746, -.7072790613850878, -.8592940051237381, .7798573346941491, 1.0555024731937024, -.1982311908130388, -1.2463573450019778, .13743079423327467, -.4234505001233639, -.777761514879249, -.8138265531102875, .5106034626084613 }, { .15553904225358445, .9196401387490124, .8678615524275377, .7398341923994244, -.16412329047005336, .4412995878345837, .09933963829034217, .8748376588692091, -.1087860442309448, .24003396100303143, .36753259229503954, -.37915211615174294, .6344265767905525, .43811569700151914, .22878358567487148, -1.0637366725292239 }, { .1415760672631695, .47506005003953755, .22097533716664056, 1.2968171837013194, .11024481462482114, .5581229216948393, -1.1875111522689998, -.7847302047396143, .5936375118461032, -.5446249262551867, -.24191097759899702, -1.1101308928501195, .27673472888541834, -.19972639018353605, -.6431812512000794, -.5401752917364769 }, { .566070423698254, -.23735468213563116, .8114583793326722, 1.2557541491917705, -.0218462658062954, .2750183410888326, .1345656847381124E-05, -.5714610393511685, .9063894749984822, .21175498655995478, .2862828562346049, -.514588538084768, -.6055101603411114, -.11024330734302147, -.1430523790207164, .05616061746162927 }, { -1.0451719815190816, 1.4109868836487105, .008934996076471774, -.5122762137254196, .09664314659338435, .43436793532560825, -1.1718016917999106, -.3933303269455733, -.43155684015922124, .6136109018397239, -1.2954075029524406, -1.18570960836694, -.1836154557545874, -.3892483867733469, -.7666214319582914, .4016435748439953 }, { -1.010833636511663, 1.1351560967098684, -.5688353057860964, 1.0248408313025783, -.6545354685959651, -.873536506865246, -1.1607788013530298, .00974791496056451, -.6948938629054293, .3273982286308729, -.5278092779937632, -.06784772449058271, -.15345418498884175, .6434359681670033, -.11827652246784678, .05381355494246477 }, { -.7290017967025643, 1.1936938196336766, -.3816309225877793, -.1579091430788561, -.22866520582952568, .5696073192031672, -.9190500995881814, -.7052540603375645, .6173617144636929, -.5835897098214023, -1.1871417276340313, .3152598485983793, -.5189112978015132, .19473048309135949, -.06322042411200887, .468880111866511 }, { -.7218252518863457, -.5075630983334236, .3452237215570648, .6262715836627495, -.9013205010245016, .48478775734820534, .8092825196815832, -.5340261461879594, .6525744834665261, -.5545438780927248, -.798515925802125, .8712644509850929, -.00962467488797003, .05679523797743724, -.4649976739247823, .3535292339909717 }, { .9781286951800228, -.967407132454287, .7744633194442272, -.2757108265495005, -.584759299613415, .2848621594570655, -.29672798687014, -.3170121443411271, .2853510226143261, -.3883691361611943, -.09637209466911667, -.6732878193034275, .6665687381114231, -.3621565796640167, -.1857191699130687, -.09609725409608716 }, { .04162493095887787, -.23745380813474282, .3721417994704648, -.9173526669031877, .7681662666063434, .3538288847373534, .35465358679364156, .4090117540886724, -.2611784625168472, -.729310908207111, -.9938777818311115, -.6490644246111221, -.34011913134232996, .26659489406186143, .48259417767348123, .5069344706180947 }, { .5539026433954681, .10837738915947948, -.5816296109864192, -.346828288196112, .7421286857770997, .6544956827033546, .7327718987301668, .13467842899558913, .8720149757509892, -.8008916700975217, -.4380102310237568, -.5309835149277995, -.7003096865607945, .0980461272698605, .31864875718657504, .6127718835596345 }, { -.45923641697642914, .37964890509965854, .5909788981194737, -.3535547725823267, -.7801863966498033, .823378773150157, -.5903164487595531, -.6412893402417523, .12055178730172211, -.4787430025823822, .12774059160181506, -.7034302758231592, -.9355871510452816, .9743841884923186, .583432889417006, .4755983037454754 }, { .9428806347336209, -.7803589513513198, -.5047422523522735, -.945516386548291, .021005404845788167, -.38597569313894686, -.6412716629372035, .45093357904039433, .5342887511716468, -.8363746231584073, .880009793720268, -.090732367212639, -.9272647441527706, .03734157541040295, -.6580251978136911, .9466901549644617 }, { .16860352055465966, -.08178184268236555, -.2142030972408442, -.14001183961320973, .6366551669855578, .04120428015611499, -.3550549483146459, -.9020789574033246, .6527946332573153, .5216780057075818, -.08415693690124715, .9100907772022326, .8533568369018432, .9627845807679738, -.023012432552560913, -.8272654208037671 }, { -.274313350039205, -.4521443949823538, .843151925774571, -.3102094487930167, .18885894694636973, -.5190873886123208, .3481545005047477, .786554378347033, -.10028180424960764, -.9635540943951626, -.2311394674385021, .834588580998576, -.49758771649878364, .4794789460384905, -.007428656066181594, -.6130161834525774 }, { -.28822323889591495, -.2590944539528244, .4669640743810539, -.3851233385895776, -.14033627796020265, -.28280841318772465, -.27080350121662233, .33406157900771594, -.5712472683611334, -.26254716424519087, .08551106978933976, .5947403878000821, .3294848794014358, -.978837269345977, .7474575018471166, .5425437278014811 }, { 1.3208195283099173, -2.5088142018718007, -2.995352975339985, -2.118928375438421, 1.0810554876874083, .12062424167477072, 2.649355518929086, -4.596640535548591, -1.6509693122320086, 3.700975191525423, .43524918133487994, 3.2652431754965576, .7859337402736414, 2.0576358200243825, -2.7986548541558314, 1.1781833456557527 }, { 2.7288313642992383, -6.119372932854079, -2.055629093555468, -2.499618628675256, .09430278205096423, -1.5417747741771166, .8587182667304528, -.4766234808881504, -5.127696651182884, 1.6546625882588395, .03851640936206723, 1.7180511038258979, 1.4798302870820452, .1021957892317767, -.5659761647011852, -2.4502843584029597 }, { 1.4136480856785785, -2.532174595131342, -3.8416157133750035, -2.900976236773658, -.13207320057002825, -.940764094746699, 1.8221089722312407, -2.8425410169315963, .11787307779514511, 1.761708034202487, -.04476298681931958, 4.468075014716434, 1.6272921810184375, 1.7828334267863453, .2575727921780937, -3.255563334582117 }, { 2.4717243412325565, -5.104471529448968, -2.1853944902590086, -3.295895627001388, -1.58150266368762, -.9669708797408402, 1.096748362333256, -1.2700168977653983, -2.3105550644624726, 1.3931923354734965, -.6417104618142152, 3.3931465390765565, 2.1188311507764834, -.13428284713055838, .42577483235903485, -.4044067165275389 }, { 1.7486924679542042, -2.2758987458682842, -.544395809989366, -.678591457962632, -1.0026645873346733, -.12165385225258545, -.82234444461436, -.3874764995324628, -2.7962345738550614, -.5482037398304316, -1.1123705772420214, 2.7427197845760243, 2.306804850054021, .45016826466203863, -2.860080671398092, -5.590554552596816 }, { -.5682269455131522, -2.9665210240323896, -.6057338662172339, -1.564788509164923, -.9048383867087199, .2705169594631031, .665449462582667, -1.710529419689445, -1.9717068312634016, 1.8831904396427384, -.8022055878737137, 4.0610966101083905, 1.1526732440390826, .2420739254292421, -1.1573297289518554, -2.727289110679435 }, { -.6643752496970472, -1.166349303259084, -1.836236207391701, -.8119461208062781, -1.3059339818566362, -.9507543008629322, -.6143048584755453, 1.2460581794525543, -2.268652520421263, 2.362527656393024, -.8460243086120467, 2.1441014564549863, -1.1259940708473393, -.431444618991464, -1.956051151687716, -5.726770188483017 }, { 2.232907767253236, -1.632089232031814, -.11386717537280916, -.5306644879210566, .13802993505225852, .14825632518151932, .10536596344729582, 1.1476027843782535, -.9104300340232041, 2.405504504860344, -.6462354306975859, 2.1059625989117476, .7470734804625521, -1.1695067931474867, -1.830365736207575, -3.64769313640579 }, { 2.3539364207791986, -2.423786491073311, -2.0269720912618423, -1.2558579032793407, -.5275938491684682, -.9317221113951812, 2.6436361921104425, -1.2079789401774401, -1.9704545197740615, 2.4624190936503503, .5336550061300407, -.42145985008760417, .04565118599665787, -.12264285623224708, -1.0040555660036306, 1.7023985554953396 }, { 1.2042212646496955, -2.919254653548863, -2.921406372721822, -4.772614105720912, -.04430769640271558, -.04563080952615649, 1.1891447914762594, -3.2843561706649327, -.9967893607738175, 1.428447803703493, -.12204633281451636, 3.0020426075517155, -.24534778957751305, .0819184273850124, .1628600586958722, 1.1066605119801358 }, { 2.204296678243186, -5.748209111987069, -2.625534148415775, -2.4518534463627577, -2.5016838217897677, .5654672303989108, 1.452156531152646, 1.4512103194569441, -1.4519252530717137, 1.6265752106270561, .6557625971024137, 4.180028189207036, 1.2947691745253151, 1.1862150068259738, 1.4266381900731027, .5222255676508586 }, { .30420661981273733, -3.3530417480520596, -2.021264364109492, -3.232797698388695, -5.3514727561411375, -.7241170438751389, 1.4254314873116798, -1.4422687856534848, -2.253459775024489, 1.223464626710954, .8619991409283253, 3.4861588427353594, .5424452461918219, 4.216151735978274, 1.5472546966578617, .052009940894820715 }, { -.1877232458520384, -3.9365632783918576, -2.2518566342951463, -3.5525019873693062, .9464344240208481, .2613373054962298, 2.679791117866054, -.35456031413370714, -.726054568126653, 1.1101151969062841, 1.0091956488114184, 4.936284963318506, -.08359586922890772, 1.7192440232044592, -1.0512873491306192, -1.6941872629328731 }, { .0024679435978707605, -3.771661883272636, -.505093809683323, -2.976109662948468, -.5506264035258958, -2.671294798428282, 1.5217507466439333, -.1777146223845198, -2.978489081282192, -.1713758665266972, 1.9294541974663162, 5.538303618500505, -.6349055587120105, .5088772350865032, .3734487760895427, -1.207534453232689 }, { -.578468096679534, -2.831060810410771, -1.2129959362576446, -2.190471927749297, -1.7143121948792357, .42464406651846653, .8278121374216697, .16591398801788948, -.2508551275468339, 3.259943314286433, .5268333643480696, 3.025137475039968, .838103880469498, 1.7877283595928664, -.08040475463430574, -2.741099102250535 }, { -1.2157463111961684, -1.3693607883806638, -1.078788899899201, -2.668680001825171, -.3031869627214427, -.12157610941295034, 1.2826242506702301, 2.3075458479089472, 1.501487666559613, 1.2602686835877326, .12889821769079035, 1.8091088149182994, 1.2943131453373198, .2504543418922999, -3.214411416503776, -.6841330222155837 }, { 2.5539892397828314, -.8446965752978585, -3.3005977287400214, -1.4542572724824532, -.6896915158451087, .05666740933767082, 3.2933932052886568, .6300536928850137, -.9153001920834658, 2.292389114382794, 1.3802002642824915, -.27565330063223936, -.6138796781874524, -2.614463483322462, -.1491995443375229, .2644203755623144 }, { .3275266833770798, -1.1813791660294286, -1.4385505320778722, -3.630300391088974, -.8321523613298638, -1.0553049722869003, 3.6782799636460046, -1.4979567945469217, -2.378078637448813, .601826113245175, 1.0183151837109083, .34183255940558943, -.06447042432681613, -.14530257257582074, -1.4220756840661224, -.30502029693708854 }, { 1.080464255411601, -3.0029029101508193, -3.209376794872031, -1.534794348136461, -.1320414384364121, -.48745628958635306, 3.346106324900473, .7810396406445309, 2.2130250426465397, 1.8306351532970189, 1.2737023522152249, .6280673551934259, 1.1094572109696292, 2.39963062252901, .26173998603026205, -.3626350409993417 }, { 1.7801592332229401, -.4028806079314145, -3.01942748669662, -2.84449387600972, -6.265042908708033, -.794984576486604, 1.5886408751310679, -2.3532291428548713, -.9090017169166591, .7944824369298019, -.4365443429236272, .13212238577482083, 1.3005219181543717, 1.6425771775077411, 1.0064610516432786, -.04829280994829967 }, { .4981752534225453, -2.2615042538441004, -3.4599042859578093, -3.358224942980285, 2.268524678454129, 2.182305691071387, 3.9964318800240726, -3.110743110564864, 1.7703974651188559, 2.282551086188291, .39987674790045186, 1.7082775133333545, -.2864326366607465, .36851998904426664, .8425643867291746, -1.273317934801122 }, { .056877459108992806, -2.1150125552440437, .6396795616893743, -2.3453030829678343, -2.963808541805599, -4.1506124577394115, 2.500707947468111, -.2083099496992159, -2.61156927376657, .8249787422842931, 1.7202993713721666, 1.7819934195167446, -1.1551868820624087, 1.879571141487344, -.4816828699842977, -.6067738847719667 }, { -.21525220301229112, -1.1137634665031912, -2.4034821210753345, -2.877702667364757, .7073757484903932, 1.4636547245016944, 1.3464597304734947, -.8413337196306899, .6241136235100122, 1.1350707537691558, -.34778097114449597, 1.5104474948873223, 1.4305679546620222, 1.4832783498530937, -1.0175600111134318, -2.2463933799519533 }, { 1.6873923461839093, -1.2857587929532013, .16444215463428158, -4.089162354094817, -.48623971168458907, -.6937161354290163, 2.0440682292576673, .8670990841058737, .9946231432760816, 2.6930556455048635, -.46088169377894533, -.8032464183243615, .678049288650751, -.7857840440739026, -.258666844600613, -2.5688207989409984 }, { 1.0819733240388605, .09577329291424487, -3.939096279513927, -.35062089083953674, -.4755223812147951, .1596004931021075, 1.8470386816869453, 1.0083466570921145, -.7474719475762162, 2.5592051721299716, 1.3570260847541524, -.5960968042859023, -.745852606245514, -.8545597907665955, -.4289108716224173, -.5988768992414605 }, { -.3480877437582015, -.45143600112010623, -1.9508647373132493, -3.157040883138148, -2.015927300004053, .03735422091162846, 3.038344067158884, .5664585860281363, -1.504354867796251, 1.3815344777986749, -.10440936510557326, -.2035916734354249, -.06577018113173669, 1.1756136375509043, -1.614449895480583, .14126118691734244 }, { 1.1341851347752467, -1.5631486317551762, -3.616765105513748, -2.0690439169852004, 2.6848422407795858, -1.7464451875754876, 4.120426992141007, .5910129353094866, 1.4234870691629011, .9376868197116663, .365524383978965, -.7166783378403072, -.6749790519248715, -1.0649467751012835, -1.3683545688903458, -.1016551483086313 }, { -.17054226859423172, -.5956430633893722, -.6173372347435595, -2.8615241458395664, -5.840280563256092, .44136294879402116, 4.756083789825903, -1.1671746012737796, -1.119779097055713, .9667956918786392, .62341419739045, 1.2058978023380227, -1.9811065538051988, -1.7811048560155847, -.14532910084080417, -.2783040642128626 }, { .9105307930381725, -2.974821510102095, -1.905364353399995, -1.399174551710916, 4.252950526988208, 1.587213372160884, 2.140011259512682, -3.103231669762292, 1.4320584857116254, 3.832825547386697, -3.4317334415749268, -1.4909356230623971, .09086295941434332, 3.660251019968584, -.8162665042566044, -.4018639156596541 }, { -.7996843732737424, -1.7664216840163158, 1.5923827067057224, -2.2005108694776103, -4.707002265539101, -4.6044821638125475, 3.739552998459742, .05879393746263195, -.8808604247935752, 2.4361730104239743, .7978001315977262, -1.2401566007125164, .9073886439594211, -.6123236634146207, -.9076143695399795, -.9237194407304979 }, { 1.4223385701638853, -2.529327367335444, -.9396148854665168, -3.417584678374853, .8171228897525541, 2.0662485407254323, 2.7453453641610985, -1.4564206082825952, .1866983853364973, 1.8813895164549033, 1.3470294207996842, -.8024132724205008, .37127479261832097, -.6284177155455059, .13034562202207478, -2.2794129061915545 }, { 2.4795110676935432, -.05833929812376038, -.6176874422453899, -2.1507841171036954, -.9158550032602069, -1.8047361261726458, .9339020846031522, 1.9150315385318837, 1.5964497451678141, 1.716052719958791, -1.1525732180324733, -.8798500445891858, .9880715547301387, -.40308554915245254, -.07221581103644545, -2.7364060062255553 }, { .33214950530894216, .6358294517177401, -4.388313956350616, .5657193681927312, -.02181402852473515, -1.5374138673120683, 1.0345649841585378, 2.4216805578189597, -1.878248048418754, 2.6844031049106367, .4830598744575376, -.8833679095018059, .18278930137023383, -.0022452872445272685, -.843462048722919, .6236958524183989 }, { .3311287748630804, -3.0041890884794893, -2.6815648637826364, -2.0254146052726907, -2.3204630213700748, .7495655025366517, 1.592125139608229, 1.4459599036959794, -.04281375729084494, 2.857692115959037, -.3193324708458132, -.8981482721025267, -.23193896133681027, .8669673823864984, -1.5761800065999474, .25975310069149565 }, { -.3738020075310765, -1.3393512326190435, -3.3922450067144245, -1.0701044921154828, 1.3999038720512091, -1.92730167561297, 3.1000074573005922, .33779526209087224, -2.053816626683046, 1.9936266080280864, .15753102610046008, 1.2454553303427414, -2.413240807333713, -1.0263857143201474, .2615657492415875, .03465874281252675 }, { -.12672106971715474, -2.3147559376669085, -.8334235767234817, -2.469285934194198, -9.040660212770154, 7.597753601037637, 1.9200903804226443, -.18120188777407725, -2.8921817831436787, 1.1914170468036525, -.3712120897617343, -.08713187489989155, -1.0863061237649587, -1.5669713061087716, -1.5711679435134045, -.17563668935624016 }, { .817917097747698, -.8922028239738217, -4.698424289226629, -1.9050955142062003, 4.618255758321853, -.05518426244337191, 3.120489781622092, -2.831478746094804, .4091661098314982, 2.3808610437721796, .8761893067330898, -2.9585878669642978, .6461759013997888, .8764841815662272, -.2547132675685127, -2.1084596448468256 }, { 1.2142458375965952, -4.3120833979477835, .06836547924894294, .48715515095290635, -3.2566866436992683, -1.1710368313965867, 2.1485576305773875, .724709471912946, 1.399519609606364, 4.040413014629206, -.36119160076763107, -3.2639443483980672, 1.0698108527525114, .614409817156588, -1.3233309774090327, -.8025188839362752 }, { 2.954899003039293, -.9159645271556697, -1.528552366999724, -2.343241770227263, 1.0713723473051489, .03722160898440667, 3.259834738996496, -3.6850448942332847, -1.3648036988713677, 1.47828726433593, .7751348711816762, -1.381726721396124, .6551449482119888, -1.4792432874693402, .9961355293282962, -2.5583491835740206 }, { 3.0737440010156467, -2.1253814937784576, -.11608442967263072, -1.4216192135383245, -1.7862937054710015, -.8156915083283609, 1.105061436424396, 1.7471392836586401, 2.756786221946489, .7404060216224954, -.5445666786885719, -1.4370182502629414, 1.7630191991667283, -.09531463883122404, .5376114556334284, -3.364931802927034 }, { -.592297587779709, .8198377606387289, -4.139470262138902, .08174498621737361, -.9718631055192074, -.6342645609208692, 1.384342548023672, 1.735717434909917, -2.1078508662532847, 2.4677446475493308, .22726564590448523, .26601859909462044, .5316200405989117, -.5828493137104962, -.3493325072886521, .17659517013464557 }, { -.4107658473610208, -2.8389669523288594, -3.70584921916824, -1.1501682507194049, -2.890240445446804, 1.146606851405859, 1.0047051076402764, 2.542512743538494, -.7180212365363039, 2.3234694892102836, 1.222139435175859, -.5995709341458425, .011252628319900931, 1.1698627052780954, -1.1297511922194308, .1138740169128936 }, { -.06639074445898724, -.223405944815854, -3.0728690648783457, -.9157114400173991, 1.177178439736304, -4.131218488576596, -.10493764532332277, .42367209945243633, -3.4918460181814557, 2.595013340216754, -.381848847523713, .46187130488131967, -1.4532472623980444, 1.906020154751755, -.6436993425292618, .1659621523538879 }, { 1.6099340546656489, -1.7470357286416822, -3.0841440328493492, -1.6243018050886615, -3.5918242364389847, .2732383263912436, -.5823539701182275, 2.308236631814934, -1.2970164857017736, 3.7063404694712254, -.8646301307899162, -.9249923537339891, .3572179819791622, -1.703240319244538, -1.3596406269914727, 1.8402089858349473 }, { 3.0154122038384767, -.4032723321858628, -3.292757843573101, -.29180707220215296, .7605497316994171, -.291485585496186, 3.6993449889254992, -2.575834801898691, .28849963881444124, 2.2076694408750233, 1.4353958366191595, -2.163204964117666, -.45880151517836654, .18838287078565186, -.7958433532222373, -2.033223931668435 }, { 1.762324445980914, -4.108232767247462, .23951410542173213, -1.381212566886999, -4.337547338385969, -3.234845098883874, 3.219883968063479, .12840343555023623, .7790406517314823, 1.4893458836645528, -1.2293885124792987, -2.950962035740015, 1.3073416369908064, -2.156115914525277, -1.5052123418039338, -.9161343976465078 }, { 2.236580465131465, -.2529061480905506, -.6471911998350838, -1.8419610850519181, .9635252123562047, -1.3723308328125627, 3.3801355383448066, -4.917658321361531, -2.447943383745843, .826845403828573, -.6204212775263624, -2.281914777023023, 1.3241626086376206, .2979757645186891, .3287702899295687, -3.014025155770025 }, { 2.0392026702190353, -1.9203979055415956, -.05579535044358087, -1.8466731238669938, -1.4785878829499621, -.7350804038028845, 1.0027944965972542, 1.670355731443707, 1.4508336605215713, .18024633864709041, -.7144262227402518, -1.4513035168506243, 1.2528971274187815, -.3466189046686092, -.6565075752144702, -4.508989121590632 }, { -.9535501811664737, .3683395448493152, -.9698779540700015, -.7566245032063594, .912288990181815, .19884262287353316, -.7496052181295372, .03539923366238429, .6772316988449969, .8835267778274467, .49968841598669034, -.7642478370584505, -.8093291841076549, .9636176692651115, .32966543343398347, .6334669262098376 }, { .15382515493471738, -.8961944305458649, .017263422820336993, .36634991810597395, -.5281423973653638, .8922952313044723, .7233705631744676, .4346257630411994, -.11208377781842782, .48724191801981953, .30175805862024996, -.4895218777377308, .3375606151029156, -.012893242312405073, -.22016951291194053, -.34090810514308223 }, { -.07151014182705384, -.9756820124354078, .14439165495445194, .03791674509165732, .34010366791768054, .5402177314624401, .5738811862028921, .9547607467970511, .9183773687336425, -.10173695604480848, .7107190999702075, .23750387325619626, -.29761497193020414, .8641293669628076, -.33724401754940847, .5232255548244444 }, { .29099106424671883, -.7581699865061691, .8526097196472511, .22925392712664383, -.29557760514609654, .04351497717490971, -.0968058815724806, .7266809540918835, .33240954462254546, -.6386415173498146, -.3361866493018293, .5076906624603621, -.2834398565692773, .3442934025606077, -.6957606563658758, .17736073766540628 }, { .4509285820610194, -.1087544158809437, -.2797441051737428, -.6519341876121385, -.908764176554987, -.4448474072729345, .5073156518760749, -.4433604705511254, .9793444309679851, -.3017817476052018, -.7029138145843707, .40562885945420923, .7151850292159574, -.6885492368256318, .5184321076047895, .7805219455339742 }, { .3257441233479055, -.08105980288265968, .2626843622840376, -.2818920412049306, -.7049304690815004, .327805067085593, .631740982343981, .12643856593157032, -.0004975994199238887, .6734151883222392, .3086203270635277, .38677805504886775, .6980877360290358, .05433919935366793, .7863884160528789, -.7688956820888397 }, { .07714892692578701, -.6679543059728577, .5416583807453768, -.3979431595141725, -.6470852875984112, -.28481302539939346, -.9999740086852569, -.9915033807237845, .905501601367013, -.8461926552123782, -.13232160269854099, .06450754576273443, -.26480878172729105, .31621703604064266, .23212569213904222, .1406524960458635 }, { .12438515527859528, -.4675617140822932, -.49731882449505793, .9964955727958023, .3136004053911816, -.026089867586432502, .015427430633873529, .5252343458405366, -.9049583613485472, -.4216412651249275, .5598082705946705, .6346915629898429, .060831048605418214, .2750987920817134, .8889323859783391, -.32347204198543555 }, { .26665254437145136, .5225052624865717, -.15137395815094945, -.5234572102462072, .4005868270534999, -.5093940743471898, -.7253275941770456, .3471301770801687, -.024067767779830322, -.8929666585210706, -.9240013339585365, -1.347811846440308, 1.0277235295662799, -.1421721666611374, -.04525937728083909, -1.273552232108449 }, { -1.133690697103203, .7242701108606082, -.6111475153559404, -1.2252042127253489, .6373974924179966, .9973852322277686, -.22444270931283578, -.15712979897398016, -.9595016098275702, -4.279417952218219, -.8376684006631703, .04107390261744735, 1.7094538087211735, .8840732996273764, 4.728156689466974, -.0995837418717108 }, { -.6369330753781953, .20032461113051553, .9020253139548972, .09799795440409448, .8316365674170814, -.9314245623912036, 2.5078908974522833, -.36850692441190547, -2.241507137689826, -5.205729194775141, -.8034301500962912, -1.7325016256746153, -.9705646378970748, .5620956941283896, 2.9767712631935828, .004126987637968434 }, { -3.1135035235811745, -1.0577067947997445, -.6650037902068415, .808576822647497, 2.1518766609452182, .30590792161493213, 1.702788450655226, .09413972347292883, .5494148328920924, -2.7063865284204547, -.9597857822446753, -2.6371690276493633, -.23612173066928974, -.08615370833943392, 1.8678719879376782, .23583548646897268 }, { -.09632514001823088, -1.4051909648824514, -.04188818877558438, 1.289126993896107, .5785017751260746, -1.2790922310668287, .6914128938187448, 1.536548106533856, -1.3953227912559245, -2.8535494010039084, -.9941132325885718, -2.7324991273953234, -1.7588063278443833, -.493488568395011, 2.8967331384133876, -1.4106843543925716 }, { -1.2402421074218264, .15487686798206549, -.6710530356785365, 1.2639815302042847, -.531554911997479, 1.7959054574985895, .2657922470690258, .8109030141553187, -1.2951355886747258, -2.6180213784312913, -.6429616759278556, -2.152144157930958, .5607625300077435, .2778327599673239, 3.549541476662379, -.045861886735396803 }, { -1.0224632773671833, -.3759059657290232, -1.517830103978329, -1.5643239323306157, -1.1558457419863628, .1638203648805073, 1.3280314273386342, .4596292956558298, -1.5006596129055572, -3.90566901802458, .660434782780267, -4.475989274571352, -1.9132222978109272, -.124317904881133, 3.201670361015094, -1.5731810382990898 }, { .4156652980353726, .2631090616472205, .24200692962275175, .6919674974863366, .8024915023638434, -.10667792185013242, .2685465455384385, -1.2647381757453424, -.3823159184061174, .14944111847980807, -.6640642345574904, -.7496791575136813, -.8232463338950881, .6648300864483874, .40829219057596844, -.3765128239459929 }, { -2.270102198313549, -1.7123553906716835, 1.932517217905061, .1434176337330354, -.24672325845025753, -.33219353656265216, .7736048770101459, .1123815275324275, -.40865015938295746, -3.0914766363474437, -.5925348441192188, -.6383694925007604, -1.2280971164987922, .23370220974815378, 1.5296456555498625, -.4659466361710682 }, { -2.706527085246932, 1.2573908436809453, .7112165180554021, 1.2703090556849295, 1.2424545810299141, -.6001797080930731, -.1090860892047491, -.7050276095777798, -1.0582299215708113, -2.6139051260613853, -.09971429798343656, -1.5501615740938124, .5578055355517687, .3582528095888631, 1.9713246636019097, 2.1046584361823273 }, { -3.544196709472709, -.5272017201950697, 1.2790373779864168, .105431731504609, -1.4729575505218915, 1.699641519237772, .506129945015117, .6091192465115807, -2.44107866911772, -2.7978267531070795, .24572576310715002, -1.8447400267902934, .5733325046605265, 1.7118992918480331, 2.9992132155521425, .6494229727772273 }, { -2.5614309388399072, 1.1221941411275769, -.7661860536487803, 1.0143444122021488, -.9197617304664765, -.7451832271211596, .6829189858660657, -1.909305953453649, .707823255759606, -2.399242149657906, -.20004109663267647, -3.8171286188890123, -1.1052226307890927, .9556955023435217, 2.9578331789655556, .38218910914573057 }, { -.3598498949507542, -.09237163144545649, .6171902910409813, .26148861778870186, -1.783054753397773, 1.4703453981489862, .20956660221122084, .8529073622003293, -.6658534912275011, -3.749312418637584, -.4993381525198507, -2.830979549958044, -1.99507579292312, -1.1547905764414772, 3.5921760348631566, 1.4051656024712331 }, { -.6275251194204444, 1.2801736105706685, -.13419287640046856, 1.0129644424502016, 1.6862362181408237, -1.2491321509516982, .28254181223928765, .1512716048302266, .34970676084602487, -2.353049167404321, -.4048735374292094, -3.266205902380746, -.6653752268439459, .8682526135422544, 3.6597803534190065, 1.6386100693065209 }, { -1.118928447289415, 1.4043061321502395, -1.215346367580071, -.4000755129815877, -.5443009172169507, .2992599084724793, 2.3848451663953396, 1.4196624094213413, .9772231362525273, -3.299501952952681, .19346405623603566, -4.086977372441059, -1.5610295926319855, -.2670120600551885, 3.001204839883137, 1.7464571817752867 }, { -2.2229346750244003, 1.2287313108961855, -1.2034403284951547, -.6756923199250344, .610676406544644, .6224822913397838, .3622852944041077, .1448915736063545, -.4191905790566996, -3.570562083737984, -.24263697383982707, -1.9473185020796446, .8215581626322968, -.5670996052217013, 3.325822579962283, .5168579115507562 }, { -2.3300415290760212, -1.0656423728498006, 1.0004558299782869, .782333032457198, 1.912679908208179, 1.2112674075138012, .40456276218286735, -1.2239631585057824, 2.1545203317020483, -1.8991677374902431, -.6275574865873601, -1.6619777671606402, .6067316678271951, 2.208853141037483, 2.9710945347104865, .46985369982155956 }, { -1.7096862249445886, 1.8513095368383041, .6695624628347983, 2.914264594406887, -.20976580572199716, -1.3073183122883252, .9369442470361071, .452198583182328, -3.3780628434924345, -3.800903222602709, .09311834785308222, -1.1976193416308107, -.6390363750033767, .35790609666495843, 2.736204941119479, 1.2922372161526514 }, { -2.7356908502166766, 1.2634661564595726, .4540962264304941, 1.566892920851685, -1.5964254506081395, 1.5359949996194426, .3527669725910291, .7595102690392891, 1.0600326076868827, -2.502134722798248, -1.0233653487814673, -2.9003333463187904, -2.9857839242067676, -.6916015559250117, 3.3901323625172526, 2.2941792264374397 }, { -.8788347419776276, 2.6907954926231286, 2.4143071101259417, 2.2834207293758424, .5488507015012086, -1.5412959520307086, -.34598973677938133, -.11032522147935481, -1.0321090049475785, -1.9637311409177507, .2568044578739837, -3.022122338667434, -.2080976681547942, -1.4820385701069516, 2.9743300379350583, 2.797921612239214 }, { -2.6858633114995767, .4402738072063501, 1.7929702000916647, 1.917289449344811, .16832256271240958, .5531027003564805, -.7825362463255353, -.14838689484871176, 1.5123649090794378, -3.5579214536031625, -.464883770914313, -3.246532096875099, -.43404733775526677, -.9940776095033415, 2.1898177463499398, 1.8159951505005745 }, { -2.672409137502471, 2.3553983326085888, .05653738456688376, .3789233161131424, -.11260790498666619, -1.7197004356899506, -1.6054252971407037, -.11448395525868008, -.8236220540846692, -2.272126905324284, .4571003532441752, -4.863290711336229, -.7266056371409658, -.40959771131343453, 3.58185712025396, 1.1666069157152383 }, { -.5609813005130782, 1.1080049061646484, -.2231601406406504, .23042027028337392, -1.9555330624412095, 1.2229374764251046, .8625716946650545, 2.920902977031572, .2830211547711071, -3.433272466901597, -.6919752984612759, -4.534082740202242, -1.948502003900929, -2.315097246641555, 2.6373835273835247, 1.1267233290263898 }, { 1.386293527048141, .5170427562416333, .6769134622372925, -.3276123556478352, .6386664958501984, 1.5368108804227791, .11686277643276745, 1.2107106505524081, -.8354659848362374, -2.762829957308304, 1.08170197379509, -2.664795157848815, .9126930625857096, -.11152894716911159, 3.5512996771737533, .6507217488520082 }, { -2.8214095447566208, .4764546210886685, -.42171452220058603, .8950083006971232, 2.047135567652298, -.7781300801311316, .2247209253413696, .9596331894408576, .2915579895808431, -4.2416988450602595, -.48240526119463145, -1.5245257939048822, -.7873546418164852, -.8551867747096438, 2.2738230033819256, 1.549067430166803 }, { -1.2328658061978863, 1.1687179322118846, 1.236986265597947, 1.1020553467620493, 1.2142726082598034, .5827241937647, -.742592458569317, 2.001421440498328, -.8288218274998954, -1.7515191311836094, -.450501895011182, -2.704725462035388, -2.794731466461238, -.19450155727099608, 2.7433189753891383, 1.6948579581829017 }, { -2.853706439996046, 1.5223477839280437, -.4002299839195205, 1.5162004050348505, 2.2504563122421115, -.3910971323550386, -1.5214829168295325, -.3376303661819824, 4.220271543027233, -2.6824111585473944, .41591640260575213, -3.128398086833678, -.025449172767894632, .3507169819883422, 1.965830202900045, 3.630774370965377 }, { -2.451884334499797, 2.9164477287122677, 2.5776279865984915, -.5592078718885206, -3.9509147694351885, 1.2724650392008776, -.5715176113427763, .3148113790511061, 2.0033899965685307, -3.4376638332330396, -.5871534042634574, -4.228380707493639, -.018132682950654393, -.3836069461981868, 1.824145604222129, 2.8539937624171734 }, { -1.1981264228408557, 2.7396818094519677, -.33883635598416206, -.798277597666951, 3.417747472336719, 1.4486605890111932, -1.4406981414728126, -.021541234162137133, 2.1789849155911503, -2.575882163632817, -.848314711603707, -4.162120195659861, -.11143057466204877, -2.4229272921640668, 2.2027080395699286, 2.46286299018179 }, { 1.2916080173803275, 2.0388074331288073, 2.371519312411626, 1.3475051023483033, -.3361573589212404, -1.5042703939643949, -.11328433439080682, 1.945328444717542, -1.49559662324594, -2.3047416541650922, -.3436681995499354, -3.0020468396986346, -1.3505285775688847, -1.962912355240115, 3.2254789261249965, 3.99760542094733 }, { -1.318753903684589, 3.176069067339312, 1.8110484357630316, .08025726993749173, .7186790057092358, -1.2423200376751073, -.04586948236038345, 1.4370482233927375, 1.7458993111956513, -1.8567624810621388, 1.3026677627036045, -2.5621628415029374, -1.3339937519192429, -.5625813151359979, 2.6318504176680015, 3.2224564774239868 }, { 1.3579974196638447, 1.5747877635579501, -.5783064441527528, -.4444204824426104, -1.175086100372937, 1.0254951866695843, .43583879264013775, 1.8571146186326062, -.25727622028088176, -2.756079365160002, .8517870934068448, -4.922546115912128, .14621763998663842, -1.127920460976827, 2.6346006205087424, 4.340853400418241 }, { -1.424290158635163, 2.7146904099116798, 1.3849933478990277, .5150235247951536, .9885783433171969, -.2518083549966725, -1.834654167037777, .6776979258610617, 2.0115920488631467, -2.5018751014889773, 1.0567179512438067, -2.09092802499751, .8911934275272055, -.045684650179516444, 2.5665242156618464, 2.5589167058678695 }, { -2.7273306426257076, 1.2835279192242741, .551240886451857, 2.4070319632182136, .41012304123013904, 1.2862830327587857, -.3694212164319228, 1.2164161211646445, 2.52460697383833, -3.0330041358422335, .8992269339378544, -3.23850959954135, -1.3169687909295822, .020003122936971606, 2.167416916676327, 3.0644639220795753 }, { -1.4815934464002944, 4.503157110219133, 1.6868166373549638, 2.3998013877945663, .9932468895173044, .2228736739605541, -.29240558638622327, -.505752917539774, 2.190970784953703, -3.361447927471763, .3131354344979066, -1.613081707198754, -1.6384495345071413, -.7217974103978346, 2.4775919800830075, 3.3254183706864615 }, { -3.615603475726796, 4.248708374826027, 2.3069940058471614, .5652970747929124, -.5863573432847483, 1.5557146962378432, -.8409337170990251, 1.5347355786472132, 3.208602401571168, -1.7696315217250567, -.5050923724839533, -3.4447003772254496, -.1710005620565116, -2.201534277641412, 1.3848587034384408, 3.2719385055584853 }, { -1.4039416277093228, 4.171532179447605, -.2160740865664732, 1.3409281465754934, 2.151323907630139, .16728287230422012, -1.302928292014541, -.5514746023953532, .8733772611810864, -2.570141637837977, .4512665488920788, -4.944731599917902, -1.6540640399983095, -1.075641131070162, 2.2426268997604426, 3.3522118620584465 }, { -1.1861627666248558, 2.581900446315175, 3.2126817669219423, 1.8218256231685743, -.09544157720437778, .4169467806656186, -.05374266730169925, 2.6138708994299233, 2.8179843280476735, -3.0124051773187923, 1.6418915223034045, -5.958970772056705, .6025524631510129, -1.998865485348104, -.9638262955870834, 4.8876477218926535 }, { -.013372700809940434, 3.254754032497229, .2030484254271957, -.9031898286942671, 2.824750244149964, 1.7243439384332686, .34863073888793444, 2.8647980873549015, -.437279364843644, -3.4265257581930233, -.06806228995618759, -3.9750669538045518, -.9493313807161164, -1.3761998106008422, 2.02074097951019, 4.178902316576103 }, { -.130972261830916, 2.073443555690042, .31485361576960497, -.07530082725199759, .21400134208545846, .5187492249237577, -1.678723261030039, 3.135394843059854, 2.477836399758056, -2.935391422449477, .9655934421016766, -3.1509736993267348, -1.2307860597387403, -1.3346880036136393, 2.0292849712356036, 3.3553505520552753 }, { -.6116483904231779, 3.2069427305762694, 2.3325736426971226, .44110291317910705, .4461660539567576, .49310763376092415, -.4311115238425286, .43521202272286363, .83977826898118, -1.4853926630669994, .8097779496691128, -1.568403089352473, .1563293202511579, -.9179048399314256, 3.454492493743535, 1.8900845829817894 }, { -3.5889988721164783, 2.2839417082754663, 1.765695149923486, 2.0443121810963754, .6643854829690455, -.06480621661673656, -2.380043058082025, 1.5953345968986448, 2.4442992351594075, -2.081028952597198, .670652306453026, -1.2264377797532628, -.6529907632371783, .04030769927239881, 1.0920971956273753, 2.991426828917968 }, { -2.160852140311963, 3.2737887445533604, 5.292907258003806, 2.7849995143962634, .9211442366335418, .17765799041789035, -1.5232618517010414, 1.760331861378384, 4.16976090725642, .19502980820738108, 1.206321417242013, -1.8926285774918374, .2330718341097872, -.966346210507746, 2.5609639921678857, 2.404114357690247 }, { -.9908547748818626, 3.3684571572913202, 1.1584558708299262, 3.003623332782871, .5316731205315242, 1.8252529178397523, -3.5328143908734946, 1.5376775598049566, 4.973256442445103, -3.6311606864484958, .10716771617643418, -1.6967323797869343, .2243275028383352, -2.426699255789019, 1.7250876982168672, 4.077962168668139 }, { -1.098459809287549, 4.232191938135283, 2.930305899123246, 1.5813561617679537, .38199116037320074, .880925760948753, -2.9563716656652788, .28081522983129087, 6.131925015022774, -1.1532937918014903, -1.1429423720234853, -4.311095588877327, -.8021605160879861, -.45822821868834934, 1.6803226656652002, 3.7617923067314543 }, { -1.0970233151859807, 2.018556601188326, 3.0054272793201964, 1.3191139684663897, .8621850143767072, 1.5404119412601929, -4.38767731096331, 4.2860852476701865, 2.014743288496199, -2.3865784812874646, -.8390682015044146, -3.625371451492248, -1.5463827335119107, -1.8953462694829541, -1.3567372787812002, 6.302361456446936 }, { -4.40157973087377, 5.227282294639889, .8454670415725691, 1.5093291288935209, 1.1887172021261814, 1.0896808781676084, -1.6309902006737322, 1.0557109488297043, 2.556986230589369, -1.4682121012307399, -.8033431223436374, -2.0796219437943395, -1.4543114904019847, -.9651571567267925, .8749542364769237, 3.4689380840090123 }, { -1.0640021336289756, 3.914612338387461, 1.479148313595541, 1.2732968134033014, 3.537665075151074, .4976124437733259, -2.975931851947708, 1.6588559883086003, 1.4709970135504515, -1.47498808447779, .5872069718706657, -2.170302109240206, -.7229941696646087, -1.514113228419462, 1.2758526588590626, 2.5330492225584886 }, { -1.4785837755179805, 3.1074284062749427, 3.1187632841479025, -.038108820211865735, -.19013799600248818, 1.2219239407831461, -.775156395913499, -.03936050167811904, 1.6079591334555108, -2.6485726210202043, -.4136567059572929, -2.567455901273237, .9133283104486309, -.1442775774296235, -.29689207034818554, 3.636445392795466 }, { -.5521390064639452, 2.0012036765846832, 4.239137852108128, 2.1978743762965505, 1.1285752470399195, .707352049901383, -1.4403395326792852, 1.205178791400579, 1.031402281756254, -2.284804901817856, -.05733518844711816, -1.9794728280491398, -1.1406311201306112, -.8252878987766805, .24986616645137597, 4.206359064701148 }, { -1.2241532121210523, 3.482113073946442, 2.7216971374348837, -.0014205263065738133, 2.293954963418538, -.3187532014306701, -3.9019288050827825, .8090844156246784, 1.2800660993255872, .8201654823470497, 1.4402236482013664, -2.1525063282965013, -.8442529629281101, .2903813083183582, 4.123625801927187, 2.8493562376860138 }, { -1.7958273610234468, 2.7206768283959257, 2.980498751565919, .7568309159710885, -.16223036356656448, .5590267595343179, -1.7448323479042558, .9734173175333299, .8465207940954129, -3.39265835830138, -.093066117090845, -1.632385230486731, -3.2382275453048237, -2.1813963865565364, 2.814124819450218, 3.5392466328111376 }, { -.9245451271301875, 3.4903343302271255, 2.105141076237076, 2.580885858505233, 1.6503630184539921, -.22416173767168968, -2.0868345559865626, 2.2726182921077624, 1.8227692089631937, .7174721415451476, .10628605955891092, -2.3659877775710414, -1.2172791651693844, .08765900212955204, 2.3696477381445535, 3.0493270781025474 }, { -.3463805612263848, 4.912922008373675, 2.99863146108551, 2.3012771714596667, .9120252036204267, 1.7700481181495493, -3.7706249214276446, 2.323603726037968, .8827800630560437, -1.9366054220066753, -.3824688982174693, -1.371177915326735, -3.0311587713414663, -1.2932900392025892, 1.0778538904104997, 4.728161363214837 }, { -.06358695313841849, 2.7400105241330284, .4408129661222347, 1.9138130034754064, .8669113330841814, .7910248797687056, -2.748017450187184, 1.294638519305984, -.130120101481202, -2.140151420426802, .5152616112538682, -1.6149912941972737, -.5068001360548857, -.8312891844350433, 1.3127106512578037, 4.710050684463611 }, { -.4095610462894776, 3.7536790662502533, .22530013612782865, 1.3013104733850578, .5598760197196105, 1.9545822849333112, -2.209201732871651, 2.624299535779628, 1.2145933764394934, -2.3030069611750785, -.8883707729167998, -1.7325251620694464, -.34920346478059083, -.6794436297097581, 2.9052145300928305, 2.9747301451117134 }, { -1.4089645665410684, 4.163046524721643, 1.5725846408176773, 1.8656344952192876, .09584268661648672, -.03735860005973519, -1.0355414721666314, 2.0305968931952, 1.5920887970733943, -.6093070619897187, -.9850458516148172, -.1442617255874938, -1.1872505822612902, -1.1511399833218747, 1.639585981980146, 1.720704562409674 }, { -1.7786082321419572, 1.1565894324799286, 1.9512843283041104, -.11051023169508627, 1.5750941288171805, -1.193060763524546, -.8495321637922264, -.6083628656627361, -.5660613286247197, -2.265024700604121, .5047338931448135, -1.9680188969351897, .25786997126348976, .32446378452920666, 4.053977897205737, 1.3978236970174742 }, { -.5205943102232388, 2.3956637302157318, 2.7961454422724525, 1.0707593438056335, -.1678518739232041, 1.19560378433015, -.9635893213200317, 1.3286476905612024, 2.973104444790662, -1.271697385900474, -.4716442115139063, -1.1281305151269843, -.44918725553177474, -1.3107613934739955, 2.358666061956347, 2.7192220441133603 }, { -1.2238441209735487, 3.2981104963809385, 1.8379619044236237, 1.0932325967844718, -.57934129369772, .40346530436855443, -1.8534961306640056, 1.6206562621621583, 1.3194666669308146, -.532450864366392, 1.0572648167367922, -2.3821586686756975, -.846916445012394, -.021785431512395412, 3.6311688983734856, 2.922747804118345 }, { -.5829805150013623, 4.525235485001296, 2.3329972760742272, 1.1627858646573588, -.33128201961167075, -.7237261033486991, -1.9539726181251036, 1.8345521234694409, .7154004726005583, -2.1863462522666692, .10395827100460422, -1.0959017470303947, -2.053242382693374, -.5307549218465103, 1.5208527659146929, 5.577204239295707 }, { -1.0085100392610602, 1.737071605462998, -.1997136798805586, .446213258268624, .8187466954030751, 1.3371825627425606, -1.548545497597794, 1.7605424425077676, .174756976031362, -2.743046200923324, .3829660436251438, -1.8317473920200453, -.38686810373750474, -1.3857205118540021, 2.108084564734323, 3.5414485713711383 }, { -1.4089166798672959, .6056504812177459, 1.4117457513475096, .8920269502828537, 1.7321456282822019, 1.3589124983495617, -.8062063622866081, .6910742589476164, 1.2644039108615142, -1.555402282958935, .7159090053911549, -2.1519589878548073, -1.389670128532179, -.17057079160828387, 1.8008932383551095, 3.1606316100408662 }, { -.28710564946716466, 1.5115848832880805, .9930743429283507, .2040379682371173, .3533418182477229, -.5712251677318672, -1.7992193080132768, -.9895030669591914, .955058194643735, -.4479052234673909, -.3092742507246579, -.39992671192126206, -.5832529207712537, -.6377144946293696, 2.805619147559733, 2.0149290505030844 }, { 4.141542610360867, -1.3509453447488344, -1.8381785143158005, -1.6978035277014443, -.24806569297400077, 1.1419216326459585, 1.7965492998717119, -.4400865687481343, -.790843514750368, 1.1609822334766577, -.6737018743857813, 1.1728034062026726, 1.473310125014366, .6334148133468699, -1.5481059929220673, -2.3357932060802424 }, { 2.0221702690475505, -1.2094234509475807, -1.831496187253078, -.6030174791892502, -.6401661209713562, -.906949512559383, 1.2641444235184918, -.775122231454513, -.8591810485530618, 2.447141357416478, -.3837537669398613, 2.3401251165559507, 2.438320828609195, 1.1089863294993194, -2.0397987679112015, -2.108866297808528 }, { 2.1297827347264895, -1.3183963888086048, -1.7494307383608747, -1.149385963055163, -.8130295985036605, .6994996006812351, .32512760109375943, -1.3302205047821822, -.5604320245132591, 3.1471163189316806, .2756341963513958, 2.6479345671725816, 1.4778698108801054, .9409688507088472, -2.4808907417428103, -.33901303987566894 }, { 2.271873175208352, -1.8489882861636056, -1.9478119242587244, -1.8744114499588271, .29991728674651125, -.8527241982702872, .572263269574181, -.22425475281824267, -.6743933649722457, 2.796827455944896, .011870620652154983, 2.1811900052147495, .6269298682156844, .06330901554467258, -3.34411538333985, -2.4459680618815907 }, { 1.9897150241307406, -2.0079097235373435, -1.8399204545180194, -1.7838096328358184, -1.308837125467605, -.4799979962790823, 1.051124478384306, -.9714879505583546, -1.0463954551895553, 3.8272419482806055, -.019073189341937954, 1.8478995828319429, .73316290615634, .5047589886265972, -2.012958941099333, -2.3394913237989923 }, { 1.647290627611838, -1.894980576061901, -1.1866860526022835, -2.2357633859718815, -1.162615517361036, -.11689427933533857, 1.1747043866238385, -.6341711506196384, -1.1616160740766828, 2.028629781491166, -.8076164235183533, 3.2062354767080863, .6105882535704019, .8397636707546559, -2.6614361239208932, -.4505609553997969 }, { .9910558989213448, -.7176114046034123, -2.064354358121564, -.7654873707532284, -.9170887466853778, .002034610956051522, 1.3814208601786204, -.17233711278256386, -.7367547502627037, 1.5670435398711704, -.5588847446225504, 1.508851997782462, -.2989069357755008, .2997655960321746, -2.9415782588338844, -.7436665823023585 }, { -.802392491029985, -.21938137307688893, .15438339045689758, -.5991442830119267, .10237317790934441, -.16783052423346523, .20446644304025577, .2521600571266297, 1.0667680877342922, -.4272200548644656, .19265032935907872, .8109479051626306, .5441514846549086, .310693609898066, -1.2518934077996946, .6596701400807381 }, { 2.5592741417058313, -1.9288751495266419, -1.3371748114453215, -2.220465754295927, -1.7394804323106479, -.4288565628741232, .24212498188325618, -1.807622647914323, -1.3156280415076798, -.0805882397237161, -.5018003904318283, -.16104605795029087, 1.9210476332472661, -.4749496657305896, -2.035647731165618, -.4702553592672582 }, { 2.413689826945676, -1.7038222734055768, -1.6891978035178774, -2.47173551070077, -.17991943101287383, -1.2278381004603467, 1.4770693914631274, 1.1298043922735421, -2.704473682760902, 1.6280429386246014, -.21838237610631422, .9751712955965816, .9495554249969288, 1.310399021679335, -2.569910422197194, -1.5921760007219175 }, { 3.1404729064323154, -3.052510575833092, -1.475422947841173, -2.711653689176294, -.9497973184682187, -1.1525390870244854, .011873140821684969, -.44924291319311843, -2.1129644490337465, 2.6990073651463753, -.3633543512823564, 2.8799940475938537, .7431861753523153, 1.1938066164368704, -2.054949865671688, -2.0162120642517496 }, { 4.977946427467015, -3.5045819109964564, -1.7979440202188957, -1.664599095336249, -.7969612191045142, -.3457169694929668, 1.6379832744238274, -1.0969269001394248, -2.340100382826698, 1.0790930719384135, 1.1218114992140529, 2.105529878314841, 2.8925859667892286, .18771719652979924, -2.357524752062279, -1.8588596261807877 }, { -.6828020554448907, -4.221661656428144, -3.0168441215120865, -2.6778540002178173, -.6564568951093517, -.2358828129969307, 2.139457131415788, -.11983914157690477, -.9931323188079517, 3.399707634574597, -.17601708912603123, 4.0304339114197525, 1.4333114560699223, .878717918585281, -3.080021237148099, -1.8888376224454193 }, { 1.5368534793693507, -3.4480591583836375, -.9255226127261549, -1.6769722245527898, .5040870927004025, -.608790645587656, 3.252168773227786, -1.3272578666204888, -1.2432460859321413, 2.3068104788341137, -.8522808043035598, 2.705208467680331, .57370862454753, 2.138345800733973, -2.8479584379282623, -3.160339245680541 }, { 1.2495277417115667, -1.8602674126263392, -1.6034696392838348, -2.074058247362628, .6314822902567047, -1.4824341044144895, .8240973906048626, -.3462510428717445, -.9018443135100687, 3.5720468329675676, -.2925648581417222, 2.907485575499594, 1.0050654893491833, .6199907045876979, -2.55976740272814, -1.3971365333228634 }, { .7469081054172538, -1.14864800222782, -.7713692366770797, -1.7096192464261908, .311294854366502, -.5109583208061919, .2568990150822146, -1.0488212155953966, -.8036758758450814, 1.176151470636518, .24435971144909943, 3.537389297801616, 1.1010475088870844, 1.943903284182854, -2.0957727193814297, -3.5222085779133003 }, { 2.8962098735898825, -1.9207247168203763, .5667540317880325, -.9908724192756209, -.04266779455166679, .13325539269835357, .5154965787553387, .014526117640933684, .4871345400487507, 2.5649397815064248, -.3395067020368903, 3.525432663330695, 1.0519129881961475, .5855900840535598, -3.2509256872253216, -.6279511741407136 }, { -.020990412809910507, -3.0938280465140933, -1.8641917714923957, -2.0322661681366636, -1.9231219669691433, .25252777581071195, 3.3978346596277302, .27453086337492333, -.5827822703202323, 1.93510166240715, .7891984407383652, 1.803772300048347, -.02964217386335728, 2.8650355851482736, -3.1500229350883786, -.8821396306538352 }, { 1.323623342936867, -2.024586054360752, -1.7242368120024603, -3.412404016074079, -1.2171978458373145, -.6436184315333737, 2.273388875357167, -2.7676674153156995, -3.0795873819642887, 1.4760181933219514, .7744613182872564, 1.8086211012919282, 2.4959268044102765, -.42893721155393777, -3.747721768437444, -1.1542481241639349 }, { 3.7802942088500924, -4.099246205367084, .16582932696833538, -3.1703063207109885, -1.4598181985547312, -.17409927630133912, 4.385455448176931, -.07440583107189709, -2.0796387806858574, .6143907351326332, -.4115502598450007, 2.6883704003065696, 1.9564467943092936, 2.3140580059577656, -3.431405867706044, -1.1627791062494572 }, { .6129511850652245, -3.0610085801354643, -1.443813972587239, -2.992712540962122, -.0014279979744522527, -.28784181048595386, 4.085109150875133, -1.5813629211599083, -2.8317830095804704, .3721795253339834, .652379322355112, 4.523089095473403, 2.0191120246656062, -.18761656560113227, -2.634939935503456, -4.5770942278732845 }, { 1.0342046970221006, -5.071280841018501, -2.400275914922046, -3.556206253567924, -1.7973428061593584, -.4840370401902457, 2.039366714917873, -3.818369187051518, -1.674556735446263, 2.05937630679733, .0337565267844211, 2.6023592404152454, 2.733892960577758, 3.936973093115828, -4.187627608696751, -.857413704816255 }, { 1.7100430643593383, -3.115656505811538, -.010394418067874268, -.803878905247237, -1.4191492400094685, -.6455784105451228, 3.068084717223135, -1.6480236152816603, -1.8555069007959148, 4.077947674138392, .3668866759235679, 4.245567425844617, -1.597113272492843, 1.1211979052695145, -2.2980027895838293, -2.899805632506989 }, { .10167956622323074, -2.1091103781756693, -1.7952693636067725, -1.935057658629891, -.3341919771351418, .9085154061521947, 1.186819915212131, -.9527760997208518, -1.0669932791186096, 1.510825046368425, -.4510329552895332, 5.033227831008138, 1.6402926603833041, 1.5838241639562909, -2.5658581350247434, -1.179340937510216 }, { 2.4309403296329575, -1.2603053780891829, -1.1615868066750528, -1.4503267214639528, -1.1672312688302735, -.15213374953867598, -.04315030608648841, 1.6812237592523185, .9950312671470107, 1.2821991039020557, .5183197324700471, .5879129188227206, 2.4952530058538342, .7876628848413553, -3.774048202596338, -1.9467319077178442 }, { 1.423196725109111, -1.7750352113679702, -2.284074657242424, -2.1252733072282575, -1.1644684863280974, .36724296929496647, -1.3041342690710287, 2.602074817840865, .7381199156722739, 1.4123009095383883, .020932806374907048, .8001740782989782, 3.317927200397956, 2.410106185630847, -4.468677879496572, -1.4789950099406297 }, { 1.0566938454975827, -1.7906542847005384, -1.8459952355082545, -1.3764829415609, -2.058251184005462, .22806917381794412, 3.036379876829953, -.652205981461663, -1.890090996988419, 1.246518113348749, -.4378100402334868, 2.9058566765648206, 1.671862154475973, 1.3489855820448957, -3.442024835717375, -2.154893584229332 }, { 1.7744118295326217, -2.6922616210522716, -.7453841951044112, -1.6390629446641032, -1.9932616129406255, -1.0952136757587159, 3.1190587131506335, -.5585754894535956, -2.424098514825134, 3.3450551370980968, -.7694315367338465, 3.191932299821634, 2.2000565523869096, 1.8227345870432305, -2.550827290330933, -.7951290774643428 }, { 2.2462207448404814, -2.278563132632313, -1.0843748971862335, -.7662169957714179, 1.6712416912250254, -1.2320261527630205, 3.738162898702879, -3.6682193218419235, -1.6661503342182966, 2.1925660313175745, -.5288113810597663, 2.34422848009327, 1.088365962401801, 1.6134920994203295, -3.2360369858143394, -3.8305101497901135 }, { .6862698615633203, -3.5220074241913917, -1.0054462282449743, -1.1937095752784175, -.5401296672916553, .9758211706608441, 3.5688444834005653, -1.7537248953261266, -.3881536189445411, .3291079031038092, -1.1075372105303565, 3.8940865858463654, 1.4089340745139036, 2.7969447363227244, -3.0339129581288007, -4.077893264238663 }, { 1.3830141332830395, -.78940952602217, -.17779175303638817, -1.225338285018755, -.6347785344235716, .11629552016201498, 1.2353437033813524, -1.010550887586174, .22699346242133112, .7703368084122375, -.5946211031539481, 4.79338137582561, 1.333449481050073, .2250496615142955, -3.0171769850723598, -4.281655934041052 }, { -1.663665593604176, -3.08057379268997, -.9710725362079812, -.8935713542599107, -.4006162087151085, .6872554491623283, 1.2916441567815884, -.588682938318455, -.207508336742894, 2.1078658588156913, -.48128970735980914, 4.241022836983918, 2.8769701289684946, 2.67299023503353, -3.542900240366924, -.7326981517039505 }, { -.36051563256826746, .2886655623417074, -1.4538504449805314, .8762498612138784, -.5859117570152002, 1.0807453320786193, .27650519109982813, .5134296352084091, 1.6419648217627674, 3.213932759028723, -.9251176437060846, -.8188093529745093, 2.3444367349715756, 3.6959888025513306, -4.198343166577471, -.7204463585391784 }, { .286989986393723, -.03232392897484149, -3.1117404409146285, .9958639329074471, -.9129398682825353, .9888636306814754, .41332364596112764, -1.3160553445237744, -2.1843344436560708, 3.9403213273707673, -.827020025386378, 1.678490733181122, 1.654534257818838, -.3055826958288823, -1.7433763923687293, -2.7529795938136976 }, { 2.399899013642306, -.6613345278157842, -.6220136055017709, -1.2655036150585528, -.05447495018160902, 1.0237416649280113, 1.7197389933044107, .6141084812704953, -2.8540032617764295, 2.371511713602462, -1.2540855367872628, 2.4277045110463287, -2.8995166611825423, 2.2560984780604914, -4.281783713476711, -3.0274474219429486 }, { .33101496751562476, -1.5740169122309753, -1.5375585572329717, .7569428449021006, -1.9509639160949428, -.08373504155371386, 2.8049061068661474, -1.3609458822406932, -2.197839439120586, 2.6699645839476682, .8330640242675093, 1.9946559879226815, 2.8075828913472822, -.7381926826674761, -3.2212798176523956, -3.4322101867038928 }, { 1.9630214913921136, -2.7604631413405083, -1.8694850044974158, -.23992701327797514, -.7807725298975341, -.5992512586107709, 1.0752191533084685, -1.684677405918512, -1.5233630009529844, 1.724767436441911, .6752549549790344, 4.215638737610169, -.8925920462241193, 2.1914775031864746, -3.222250699459005, -4.178345498074435 }, { .7773447229134914, -2.0796376313517033, 1.2008383919728818, -1.0976088678435514, -.6984326403191065, -.6960194702689836, 1.3661335491535163, -.4249749016868961, -1.3544739353770494, 2.8361092828889833, -.7320803163845748, 2.020413472392821, 5.2086542152401645, 1.226648939979515, -3.6905714743124753, -2.907407996705228 }, { 1.1912925621393877, .45129681401645677, -1.9349934186150988, -1.1804859951746687, -1.7409239603294449, -.6855436672795058, 1.525544596996664, -.21131586650156445, -.32468537463472935, 1.4906373272654678, -.37853161697263404, 4.459373459499079, .0838502656379282, 1.8554613390191426, -2.999597964706489, -3.57871342725189 }, { -1.312345913195977, .5673926768155068, .5801490973702517, .6469759280210224, .3552281793917857, -.40903253009550394, 1.6593597404671216, -.7809862703742791, -1.2338624430306708, 2.4415014821081167, .47455906882165305, 3.814725826339486, .8926737969329593, .6559036730095583, -3.1243735353869067, -4.822003075204817 }, { 2.357526016335077, 1.6347819657384781, -3.547463393272135, .6098717712472255, .19034079559958386, .46152897232437085, -.8641595692523764, 1.1780544882032045, .30384893565158655, 1.9956494306057866, .5077978534487188, .4918950230123134, -1.3989115748448835, .7312091942155813, -2.803506968305581, -1.1212831770770106 }, { 1.6032213619133249, -.32604315374109555, -1.7907265166857949, .8845309676679066, .06507797192850881, .6582793729936839, 1.1101885909127007, -1.2730797670478844, .6348760053666597, 2.6575402772761523, .7631514033369721, .1517471883824697, 1.4265345575059412, -.45504589001562407, -4.636849413439718, -2.043175739169426 }, { .8045194702259919, -1.026222715691139, -.9133593654177619, .5422137307710012, -2.4427000231873333, .3225580350736818, 1.6904213783237991, -1.835026240206683, -2.087873231031662, 3.2526215881820915, .5307608390838011, 1.3634692310370014, -.5739083965042416, .7244385101680522, -4.542303274225618, -1.3570225793162303 }, { 1.1124848166457026, -1.0250356992359164, -1.0137919637526114, -.19966555952708306, -2.0739211260849895, 1.1311830950111992, 2.493363533816829, -.3423876570904182, -2.304463183483193, 2.9997242097166135, .7924177245711166, 2.2092927903811876, .8062715801528932, -1.5820884076280317, -3.2161220992389996, -2.142429675806072 }, { 1.2078648935647454, -1.1537788888276599, -.6099551564358052, -.7385941526465446, -1.6355578420801105, 1.3513861103316356, 1.4955373533060334, .009786159384383514, -.5426477300188397, 2.1412158438005267, -.20171070812190978, .10528411389447613, 1.307804769979812, 4.090637400539227, -4.698668273054373, -2.5661595413161185 }, { -.16449307673413832, -2.582167504713394, -1.5529453171021173, .4566038657216135, -.43535851944777976, -.14884089262579997, 1.089287955197879, -2.3209415193297573, 1.007936839755629, 2.5115719851765204, .9339562695116204, .976054042844034, 1.812695705912381, .21355997728302928, -3.5731715002652686, -3.481529449053458 }, { -.7676772619051281, -2.0971288083655106, -1.9216939273257216, .7415226794271456, .27642728048585613, .7285697181769828, .9423528519842523, -1.6817426634233734, -.6489976059232827, 1.568066621337873, -.14645990550251597, 2.249845023100086, 2.2556959843075375, 1.875683294913749, -3.564680454541851, -1.573617246151711 }, { 1.5611401771045468, -.8886877997215762, 1.0868293165264655, 1.57987071718757, -.1164096552177252, -.2593548466630531, .7022892731672697, .8692263039469748, .748635213904074, 2.4512041539350102, -.7023009190489155, 3.162207932347604, -.07941445546664819, -.32893477312813074, -3.6340539584103007, -2.396429470339035 }, { .24994598553699887, 1.0270882270802406, -1.0311540065396831, .6699337148660667, -.971631133814031, -.9370241843986374, -1.117066431250161, .2006836748114044, 1.0045405517072215, 4.035208344474694, .7138401399494816, 1.1952302895178955, -.9804424915003734, .9111152628191178, -2.404528036068122, -.779847353485149 }, { 1.4927830964409021, 1.498065988657673, -2.0175848042887257, .907189482542657, -2.006997250149056, -.2395834640037823, -2.5138488548513784, .779676169950325, -.808866256280496, 2.8002326049497475, .48441029935972285, -.06106477258637587, .09552798291917985, .18876928665621837, -5.419430878927967, -.47853009804914026 }, { .1306161974167304, 1.1916290740839017, -2.163110059178324, 1.3763774657059364, -.8746542370319367, -1.4591385039016627, -.12976043447911698, -1.8601653924922328, .3048463198455602, 3.1004142661475393, -.7834027132062445, -.5501183410333691, 1.1089761782178753, -2.300271588805768, -3.869891706607883, -2.386497127366977 }, { 1.4989078647513394, -1.3859833374452564, -.9020868019142937, .6826611143053144, -1.4393316486543681, -.23101241660547991, 1.3734903768935955, -.9033004831465866, .38192453023712, 1.5454658830603696, .4521936090666407, .8468191932538652, -.1657525433366652, -.6618871241001901, -3.228887078688252, -2.9403273010691415 }, { -1.9984504123514129, -.23793262266230872, -1.9737525309433814, -.732601593575952, -.887532548146256, .33184588848742536, -.14453575093019366, -2.7414288068588646, .36724876533995865, 2.897396839185625, 1.1905522572332752, -.005827790518336291, 2.2507388451836263, .10044606921060108, -4.115844529475454, -2.8198423925400156 }, { 1.2185830441608578, -.11186661135732619, 1.4065878969072243, -.8243185483469438, -3.2133110279638917, .5268679505923847, 1.134627119983, -2.422395823034299, -1.3456157629087444, .9601043935467625, -.2778296575254268, 1.6625923247267103, .9272566600332502, .913725405664434, -4.00416059315703, -2.3375135169371375 }, { -1.1153977953605962, -.8674654848598091, -1.7129988541542325, 2.0952159084853528, .3262478344396991, .021117855326539845, -.7602886970283728, -1.2287368360500892, -.849977456467241, 1.633480824696781, -.7614934685937801, 1.4123479018123442, 3.2069461098447705, 1.0121074849924676, -3.0030603459493044, -2.114707674016181 }, { -.4088870280743818, 1.3355917175415015, -1.0703071085144222, 2.2176942633206287, -.7421584779104505, -.3650283035906043, .5423579042106327, -.29657732191316755, 1.4736205251821732, -.8134998031154408, -.13964715510151857, 3.1411459260712293, -1.6534092554069169, .6454428050248925, -4.13046737507934, -4.126291445459741 }, { .510646375456506, 1.0681909789591943, .09402878050057625, .9220202066422646, 1.5893937945012924, -.24570010111869753, -2.41878089005546, 1.8930339391574844, .746699764113367, 3.123893385434978, .5713431164481625, .9378636098710325, -.18294415103389647, -.4009702367330516, -2.3613093166614987, -.6311408865761812 }, { .471368603801241, -.17623331087051736, -.5906890273017676, 1.6526226855642498, -2.215186605527627, -.16579601645791614, -.4162738355426712, 2.4317466502307497, 1.7794275029220388, .259158604748545, -.3371238771688597, -.4311576113349335, -.0438820254346377, .8066586834656104, -4.8582810308951805, -.04584731333119039 }, { 2.235895837302677, .5025489544485106, .49990497072780793, 1.5325485238642025, -1.1144353984650892, .2927836773373623, -2.2529179743581453, -.5539198328575503, 1.0794594986516934, 3.765551419274522, .14997709443953433, .5413338767059444, 1.8884916890239545, -.5489084430231479, -2.676105079507199, -1.4165290651552682 }, { 2.317412095300247, .892630741626042, -.3209788189460039, 1.2419817189715319, -1.9403852120175509, -.41503562148547263, -2.344870505475715, -1.4083790833550673, 1.3316729010554669, 2.975092108526267, -.5209207338490335, -.14262519428199605, -.9195110102482031, 2.1714887225376422, -4.876033881765869, -1.0341995658850696 }, { .781304312553665, 1.203429375508282, -1.6257794025302408, 2.7916185268954385, -.6525708925183284, .06879246223502673, .2826119450314279, -1.9190963466072342, -.8024487032880929, 2.8771315403167743, -.7638635305847085, .6085487631154411, -.24032872774796554, -2.2222674218928873, -3.17491962257068, -1.466953805789832 }, { 2.061264172435059, .3915973760081296, -2.1271008385626393, 2.0766901992266424, -2.993767969418999, -.10004877210486264, -1.0867519131292183, -1.4467188398341277, 1.1940429462783169, 1.043366123134444, -.11655612376245092, .8530538211317013, .1035357963022, .7395087592317093, -2.729247579052414, -2.6000226668432083 }, { .7796641279874104, .09115208153927222, -.8051637993919425, 1.220283794762011, -.21180725938501546, -.48948944611572537, -1.403930499569893, -.6160258272364487, 1.7990133555812016, .8454523450043238, .7285264909338653, 1.6469914629442146, .7031636853555084, .4856416514306904, -2.708093183876959, -3.870735262035901 }, { -.2625635591904761, 1.1135227265506846, -1.542985384610962, -.47944201189200647, -.7520087553992576, -.27890214618757736, .7437892909300547, -.6377254232567265, .8797875579834821, 1.2620342534053315, -.8087470714411348, .5593643715521047, .5099628483178916, .37549549185324926, -1.3422217819213595, .07787913749137425 }, { -2.879753904274717, 2.437775998542471, .6878620408761328, 1.5128109338294147, 2.357930948726675, 1.6440628792032543, -.7291543491143732, 2.526696470046293, 1.4276194377061149, -.613099075145661, -.1150356737658201, -1.8756947796744676, .1440038778582197, -.2555865156083369, .797769399038077, 3.2689804628180705 }, { -.22873992444716, 1.768963848039519, -.3837735950172426, 1.6903025873154813, -2.455416032739205, .7743594233908415, -1.1601010817985902, .9444300365504807, .4059301281828199, -2.3796530476574698, -.6979651272680056, -1.6186261381206666, -4.343092572749868, -1.8552439379207633, 2.454908936143281, 1.4784573979771987 }, { -1.5397982037597502, 2.6214411779328213, -2.053301045050013, 1.8476070522165187, -1.0005791261086647, 1.1776393011588342, -2.486049630771413, 1.158703190689774, .5227146110809412, -3.1858293331346768, -.9419720078950148, -3.2061357423385424, .5203792062700372, -.9009229239986781, 2.86905636789052, 1.228178482737893 }, { -2.432499769489597, .35260162578769333, 1.7370478971956504, 2.842469181637264, -1.3868969234865294, .5846096518051751, -.11703205911778547, .8614955965686764, -1.345643442551025, -1.6305810419827491, -.592960526384199, -2.2081943002947284, -1.8352554283952998, -.23402832338882512, 3.655359915371614, 1.8763419604196299 }, { -1.8117633934943358, -.18578650450017004, .6523595534052946, 3.1794700904483766, 1.6312535597444096, -1.364000720141944, -.9605883386853603, 3.5288658347020982, 1.7810955503845975, -1.3049920474147696, 1.3917003378254453, -1.456221875667378, .5820476309512972, -.211456982257905, 4.044190351722312, 1.6349617861120211 }, { -2.1018449510020725, 2.341663961453305, .7356779240636259, 1.3135551674212824, 2.372772682423598, -.18518843034904098, -.480166633982288, 2.0860596824114186, .015278689679705044, -2.5217731062561426, -.01987158502815634, -2.701095271725091, -3.7490167699441668, -.6306451123017794, 3.577616413661691, -.8986221610896068 }, { -3.0722384541767216, 3.3314934571720753, 1.0743068196721124, 1.8206657593597169, .5746994254870706, .2624351309290708, -.22721284201998773, 2.348917198306344, .8331227229167668, -1.2634453680590354, -.6191193331821804, -3.0289910270146607, -2.8072643830735835, -1.6419068724937866, 1.1770112010086573, .7438645461396977 }, { -2.0332685420141714, 1.560617656411477, .1495785886860716, 1.3671815056433887, .2321423125852714, .25912903354331296, -1.3655171945036024, 3.6583940161902158, -.9163248067757911, -2.1862639484424173, .811481359183893, -1.5197937898660654, -1.5359113350189277, -.018886332655604066, 1.460179209794751, 2.0784810482206364 }, { .2985130981890415, 3.5605250731435447, 1.334350408454843, 1.0336514381121555, 3.1296089704635235, -.7032856994438188, -1.8435115593252147, 1.9627807708385787, .3604051542825674, -2.062995986791251, -.4836448875614397, -1.666956612953603, -2.418811730488133, -1.5414785084447589, 2.2977857545320983, 1.9114313804959915 }, { -3.6002838047397363, 3.644253820205465, -.7716245276387804, 1.9045187704552693, .9121340985267665, -.27729709696214255, -1.7962943603221067, 3.8233909960586323, .3608512585930141, -3.0251993037027476, .5871118787950667, -1.9659417676649336, -.14229836224673437, -.32382260700010745, 2.608499247876242, 2.142365621619854 }, { -2.145905263809107, 3.529114485114578, .08373554149370441, 2.7048349137779537, .29711096605496734, .49927095937214233, -.6992642419543538, 2.0593809767428475, -2.6262586154626613, -1.9879776551701127, .5159495603093484, -2.408367252744771, -4.772624885158751, -2.1660194415981815, 2.6637503019872204, 2.1573098147713163 }, { -2.256816097615345, 1.4488012925819056, -.25715360647043706, 2.5564343154284135, .8388251309623306, 1.8248063424589498, -1.9216238622389294, 1.051471988843165, 1.9852255475644887, -3.4396281102347683, -.19245548307390195, -3.265619086414489, -1.2245614532896758, .7102341982181687, 3.2535468491327584, 1.3412020375087312 }, { -1.7987353130531467, 1.5959146726166338, 1.071757426595973, 1.8982038436658988, .5427101323351472, .4529842673979333, -.5995356854394938, .5844137720332547, .7740094135685177, -2.4248959065127207, .26942795355173654, -3.213486229857489, -4.080402969607226, -1.408403026275897, 2.529038575690714, .7924033706526239 }, { -3.755727168575512, 2.938605782712921, .37348641640077346, 1.966641830533504, .4967708245259912, .3043063599139432, -2.4480941829041307, 2.6985799993674386, 1.6052196449587013, -1.7214492167851614, -.049839748781809086, -3.522741425312348, -1.0878210899073248, .3099832397204009, 2.690409191875362, -.8540067312622617 }, { -1.2885632071916953, 3.954125042251703, .15688048327860588, 1.8300013249408509, 1.0134016636107634, -.49723945374707784, -1.2745831140241162, 2.702197764132181, -2.216060971629139, -3.4473823983546423, .819851404631506, -3.2118986700345458, -6.060698969292189, -1.7403504111809303, 1.985840564674161, -.6079525622128213 }, { -2.7389804652980825, 3.6962805573209394, -1.2362232172361205, -.16818875731933544, 1.1770963692441985, -.6053201901811696, -2.6671993358902055, 2.1043029743347703, 3.858589489691598, -1.9867402993874637, .06291345049249923, -2.863179700022633, -2.4102774364928687, 1.3235172727601914, 2.1853387819586714, 1.4762659710950239 }, { -1.237044691860931, 4.284223993219343, .8362512487048871, .3845423318741043, 1.4543447671124257, .6392122747871033, -.41485901995073243, 3.7710812305008097, -.32124395898418195, -3.108119582627463, .03376395805461435, -1.2559309746174088, -.9167464371397432, .45609630762045134, 3.42115338468803, .6251364184292777 }, { -3.28442941427446, 4.224575617070165, -.08780622552902503, .9093502210905444, -.7592903588573944, -.38367617040945695, -.8867035044725662, 1.663784600158598, -1.5180249331834925, -1.6862070269239833, -.2203550585109631, -3.173888351148686, -4.632198143804337, -1.4638424919163135, 2.2834735336615792, 2.88758240233024 }, { -3.743194173032973, 2.6055855264953114, -1.0992267953921133, 2.397442943083873, 1.3325436033034077, .14790813032869804, -1.7651848132437258, 2.1709651834797965, 2.9202298503935253, -.05361586070041261, .8592305710350199, -4.179759263270983, .15702363578594242, -.7678269218604291, 3.2745609296349465, 1.7899941993450528 }, { -1.0424732063778732, 3.785352244347205, .24341397580682184, 1.8120758347001489, -1.2147160951253102, -1.9510509082450773, .08337122152373815, .2531479161351602, -.28753678356877, -1.9794542770488606, -.009347259388985604, -3.676587859300559, -6.722531570173476, -1.7956517733619386, 3.458227454919929, 1.9338104282295168 }, { -2.9743819716804754, 1.7718176026496424, .9415744019092764, 2.5216877708234087, 1.9265263797349235, 2.3321606095536835, -2.4647817925473814, 1.3180859186593479, 1.2713103833281856, -2.7063341670976335, -.9851306723770326, -3.9296508416552745, 1.2872277130311636, -1.6803005078094255, 2.5464044369369376, .972436535260767 }, { -.6148302561116264, 3.2808173126581024, -.07188139570728888, 1.937753117667108, .4669043309700696, -1.3652810848873504, -.7331615486360447, 1.8921035730271163, -2.163513470993292, -3.126409359558764, .6301884440436119, -2.6817719178880353, -6.362840028477133, -1.2203003826850647, 3.3039256037773437, .882093568982204 }, { -.448527241539202, 3.2042192535933975, .6450004396345004, 3.4321277503063037, 1.0901829126895155, -.07114795891135385, -.5752580226414833, 2.267644354607058, 1.6382205002000276, -.6394369216516811, -.6804305375171786, -3.6833038065451444, -1.5242758047447365, -.09726549770499958, 3.5054114197166175, 1.6827285736458577 }, { -.9882909416649848, 3.8080270582239883, 1.2071850223722658, 1.5410842530414217, 1.235412088958935, -.40010844544451857, -2.2009101294689413, 2.6890967033410806, .5802336706973937, -1.2979947382315982, .21180305517803666, -3.2828156531277974, -3.347993958942552, -.9310763767291357, 2.4481949669713297, .06644813082974657 }, { -1.9246238646317102, 2.9467779555732942, .40867939907026896, 1.0722743470086045, .9496360431488159, .6174612191107308, -1.7226042187204864, 2.095396662227575, .8027647706581709, -2.593911454945775, .46459667860523457, -1.808061662922965, -1.5627939311564485, -1.2534874739466706, 2.253499143828521, 3.759120394039223 }, { -3.1635996309637866, 2.337948722283474, 1.812874644175521, 1.3102417286648491, 1.8756547607536294, .09895558014941827, -1.2430012836368054, -.6725513609601227, .08040912020332672, -2.742421392338724, .46603913984508005, -1.8467684255375216, -.9673996135161796, 1.1715643678359833, 1.6533207883146201, 4.67790680753227 }, { -3.4204938860221272, 4.051241646673259, .8665067315427325, .623695783945246, 2.159286156935591, -.5387678364242522, -.6725245680630203, 2.9822305354643306, -.008380823890033362, -2.3653394443800972, .45309678532316827, -1.8069314978645201, -3.259985889615206, -2.0283233510987455, 2.7553882173101556, 2.4917425900204035 }, { -3.8684538005563507, 4.621123061314889, -.14817635366595722, 1.5862992694736735, -2.34724748226346, .17625032905862695, -3.397371375907697, 1.66975918133672, 3.3586316171462256, -.2318816195729584, -.04471162509923384, -5.782513758068939, -1.6291213333553092, .07924340469986325, 2.598173969813869, 2.8811668709330336 }, { -2.5994269200829043, 1.8378784037539921, .3644621140647269, 1.678597728093705, 1.6202705971914186, -.24900000798471783, .04720938391879385, -.17986278381832402, -.16774906213702032, -3.2790433188360404, .020465453818147992, -3.5917698423531923, -5.6492717573475115, -4.744408400827757, 2.0167812924154, 2.538455729267941 }, { -1.029613622907776, 2.957824980696871, 1.4771785036132674, 1.9863870841257625, 2.612074272492904, -.3191118288764287, -2.691593606598091, 2.8815924770084607, 2.1556774207643734, -3.1728371673076268, -1.2243859251343088, -3.560852472443616, -.7724897500371907, 1.264473821705814, 2.751233780625562, 1.2174724871821048 }, { -2.0408433032498463, 2.1962253210103095, .9029612245719232, 1.3915569009463051, 2.5128972926800195, 1.7899752011803194, -1.1125181125314856, -.22422178907344356, 1.0544822689178746, -2.4775811262605507, -1.2660243241373421, -1.1915860599216979, -2.6701658377948645, -2.005788719902452, .7896405507461746, 2.9512099386293884 }, { -.7950411947565277, 4.984598795944553, -.24366860464836249, 1.2223860094971322, -.23357961793419607, .6070847623682748, -1.364839731442014, 2.1414888176723332, .5768139331338712, -1.7703915062561653, .23959703043068714, -1.5639428219756304, -1.2977566570960177, -1.8185539765776642, 2.9060073941216977, 3.2076878890994114 }, { -1.7602997263263642, 1.9316498672236044, 1.791474698752758, 3.3782209340097387, -.638241810865874, .19724790988891913, -2.9461579818543995, 1.9794813193039507, -.18110385751796, -1.9756793296801323, -.8315852927285692, -2.1024651121599014, .3228088839967316, -1.7239149834467282, 3.3580938020451647, 3.0858796264960344 }, { -3.218468217227226, 4.224308228513718, 1.6331378318714758, .4084635785997253, 1.1640266888964719, 1.1465716580008745, -2.262101479435849, -.021892352309068348, 2.4577825070217543, -1.9423958907238166, .08563616744762523, -2.863690613403051, -2.649222500168912, -.41175229892975884, 2.2049104094374306, 1.9719561506876495 }, { -3.049058760311054, 3.061610136686572, 1.4164166855356273, 2.2863160788552928, 3.089722440614342, .11339721613183452, -3.0293358794392393, .3451767775066107, 1.5329421118853745, -1.498431014540866, -1.1001147731208212, -4.639752284300685, .3717526385850699, -1.1536884682509811, 1.3399526849384202, 1.9866634633479308 }, { -2.025620927216719, 3.7774590312633713, 2.3751999584211343, 1.5278011966516971, -.743456013084387, 1.8856274714395873, -2.347274808899474, 1.8557511956879025, .7979197470728004, -1.3008989027667797, -1.3281264289008263, -3.51380804116906, -2.0790983763372477, -.9054179380123588, 1.8776973661122196, 2.4800123307009585 }, { -.8426345547977822, 2.049049430885348, -.17953128423520917, 2.0827243319000135, 3.8533818993429723, -1.6063701585235612, -3.0964257548648844, 2.4509577194033203, 2.04641404812434, -2.7009486824046642, -.346522427978538, -3.0672628207439314, -2.7560794287335066, .12270264446596088, 2.4822317161651304, 1.3167112839635915 }, { -.028693274601452642, 3.9049650659329367, 3.343739820272426, .4548101267728006, .8370060820421178, 1.7103676256195914, -.7797487725024181, 1.6038880091745418, .08946517581996781, -2.326100857036057, .514235688254764, -3.2424904506443926, -2.722462756491431, -2.0814970164107787, 1.4757866069421266, 3.1566449672112227 }, { -1.789246011506055, 4.851892988104494, -1.4298878467754834, 1.84908649931061, .9747281328661596, -.2666641184670869, -1.78636470288166, 1.3090017713276196, 1.2457142223011175, -3.629672594129497, -.24375643243823994, -3.6525671570537703, -1.3268946685220577, -.7786193830525906, 2.7590330595127996, 2.0896149112002647 }, { -.18596213047763938, 2.2061917992852367, 2.8915636107331637, 1.36853405883174, .5295919157186324, -.71287864895668, -1.2048625225144423, .49852199698944705, .7581094829597557, -2.0440063966315423, .9211609988549768, -3.15670534862629, -.5640503080204523, -.6148244173393542, 3.9781771721248473, 1.424111965865913 }, { -1.2573938747787365, 1.7459120549253517, 3.6812530749515355, 2.292750863240104, 1.57758256795407, .22575919965526173, -.342573343115504, .8297462038942636, .22604998602397838, -2.107603494429935, -.9078854895779153, -2.4411408859249066, -2.6585893267394867, .21067782841967564, 2.1520686594043568, 1.4507224726245131 }, { -2.123969656683925, 1.998505003502043, 3.4976966778265535, 2.1381706403606726, 2.08732098431277, -.32524774223785863, -2.7239774340571916, 1.2155760765673713, 1.4878074726747985, -.9507926103564719, .2890057062522337, -3.0809659585486537, -.5248666317660905, .15693441492997212, 3.219184002348304, 2.118794419553025 }, { -2.1996270394079733, 3.5471526988979742, 3.143359180495563, 2.6154772901317678, .8537093433409499, -.904057614268212, -2.3885266026252996, .8030927487143423, .496679432981199, -1.8691766382251525, .26628914958152805, -2.3418292231094173, -1.582820519826721, -1.7531727968149502, 2.1204666339374665, 3.001198591630523 }, { -2.171614596584276, 3.9793132568454994, .38042455096165406, 1.9402364086960617, 1.626314599443449, -.3138851612775264, -3.8603707142122636, 2.2495078346176234, 2.6821249239507328, -1.0107416324393927, .3112257690152209, -1.300488828071742, .5610771831537564, -1.9754702905050248, 2.888670547722352, 4.686266321172096 }, { -.24307351015022363, 2.99377341310076, 2.517917831352863, 2.908493640028574, -.4312853866895754, 1.2958598156183987, -1.5262998995685806, 1.798109831166587, 1.6283785096630772, -1.0450748448945528, .02134239522754042, -5.024653667576484, -.33824671026072983, -.8863929981979025, 2.1538398784648303, 3.577318536651025 }, { -3.980969155821244, 2.730119860329837, .33985776852947797, 1.7783579695970206, 2.2107683034382637, .28128836002362523, -2.2460959673818253, 3.520313759299353, 1.5950233817302244, -1.41020116457035, -.4836268317800158, -1.9838612185200444, -1.30332453276862, -1.6157720341636126, 1.5103566565067095, 5.323258598871987 }, { -1.7815367837489988, 4.799956335635933, .7250721481438089, 1.791312625907658, .581849675700072, .7536314089127064, -1.7236893050660556, 1.0429527305853736, 2.3092402400878216, -.5028809064833306, -.7287547611878052, -3.165576302879664, -2.8676059575411843, -1.4547214138505635, 1.7881898319761425, 5.645942061798989 }, { -2.1086925819411744, 5.270210540048883, -.49356851701299626, -.3293393397470176, .9243566879549387, .2028194247333695, -4.270951416933456, 2.4276529708855206, 2.3217541789679674, -3.2433755692489665, -.5161420178692001, -.829176580191446, -1.3655423700409646, -.38887698510010377, 2.7235967656295585, 2.8642326405117933 }, { -1.9054049158227007, 1.8116560047886279, 2.5300833376817367, .7927348138741113, 1.3446917871027328, .00887797851646292, -1.0180479644906062, -.7787124806687403, .22058704586478609, -1.9728321205195325, -.8152527523674117, -3.167580857884542, -1.1381457778006956, -.03811152616666499, 3.5739829622363697, 2.7114974395734204 }, { -1.1515935664577421, 1.3312824973796067, 3.5460198275997685, 1.2366661178016538, 1.2833022896065478, -.3448201683454079, -1.2936740203897306, 1.0593346315003875, 1.7013636737583773, .06693942587195806, -.7569604790667966, -1.5771354885260356, -2.7571983777116005, -2.309424228147194, 2.643498852096903, 2.2266541770220956 }, { -.785717699975873, 2.635052412281018, 1.3635696373959985, 3.0544549956420024, 1.2342468228014416, -.2091750628468231, -2.581427109301362, -.5397193305878191, .8443164181283798, -.8434119370294729, -.5779384602494653, -2.185785094409309, -1.013617087769842, -.8323343085242779, 5.599203260100522, 1.2043640691341975 }, { -1.5150933755570524, 2.7717840425422993, 2.0306481245632844, 1.4979682847942988, .8281393337577447, -.7353433330641723, -1.975456070785097, 1.7454834148970477, -.27606896484338195, -.7377103920423084, -.04083852621702135, -1.1768047393598842, -1.3047524936436554, -1.6946330386559971, 4.072643448224103, 3.4282844826909544 }, { -2.0353542452389415, 2.887875259092419, .9344312537839486, .8783626453626134, 1.822818789986851, -.04270511123022528, -2.053427679595822, 1.1399415676112992, 2.1782768057420694, -3.2853552574207776, -.4340030083277337, -2.7639725756991544, -2.380887769647898, -1.488687085047615, 1.3339936806045898, 3.834309611553934 }, { .25379670967333107, 4.720599425493899, 1.4784672191581694, .17251307955173764, 1.9986398064130357, -.5603440243807732, -3.197125997417402, 1.000372957589336, 1.6002214787432338, -1.724781700618042, 1.0390300163533326, -3.325836318654164, -3.437756399661165, -1.2202609380694065, 2.8648953913874884, 2.20923188264011 }, { -.8992794773204783, .9151442017350391, .03832712887119121, 2.0797400387828175, 1.3788576550275564, -.5387539532085496, -2.546558425817448, 1.554748318778689, .7273270931993245, -4.782444070693762, .19723860242631347, -1.899478446451044, -.9125157283815913, -.6441814109676853, 1.5409746801241642, 2.9090678876306315 }, { -1.4854383341119468, 2.5338808463994367, 1.290551148624086, 1.0977056404608854, 1.2466850199880295, -.1922081232331379, -4.270899428776664, 2.4304061618635884, .656495341829642, -3.100411777496377, .1800229341437145, -2.2095398120464425, -4.328305385452462, -.2698906836696637, .5369879109780288, 4.934043906268148 }, { -1.691504212135197, 1.2532453413644802, 2.0358645699153732, 1.8135729303056327, 1.5862451288713968, -.13466893134625144, -1.1998223213538683, 1.7778244832165249, .12957492635986537, -2.1435791015113863, -.40079890107561955, -1.5085815191312921, -1.713300507261054, .301164922256977, 2.520932262876764, 1.822959809114017 }, { -.5435766479263243, 1.407922760223968, 2.4242432392226014, 2.02823887515558, 1.2414813898965722, -.7090638294700802, -1.1609040481568187, 1.5146734289560753, .4855834811326072, -1.4322327514263316, .6606858840782671, -1.988671242233046, -1.9510112892046478, -.5318984726584278, 3.704055282581876, 1.5426643576372991 }, { -2.2437905220408636, 1.6215872548298305, 2.2353933505141423, 2.2514801727293374, .3285535826081417, .35055694339048693, -.11579405212400289, .49406307823243495, -.15664698177647507, -1.9809111680282692, .1034805464806297, -3.1433415686562873, -1.4677971020041116, .5280230951000854, 3.487664323530437, 1.4604340727902898 }, { -1.9836900715453583, 2.1373408779658987, .4811906187787661, 1.9765973095129785, 1.866082930205209, .3860099778561401, -1.5652086303947241, .2643177869473196, 1.492402810280518, -.563543715796189, -1.0744678894592983, -2.1568753736803323, -.9052277240061588, .45091419757742746, 4.455671503278525, 1.837518236264006 }, { -1.9078646061635076, 1.9905220111411706, -1.0862425666291902, -.30786108051529826, .7204676142962115, -.6399309458882099, -1.891669835155955, 2.140976713623944, .24612260400024324, -2.347168474797209, .730642586367805, -2.5018230332686087, -.55458696122723, -1.6198927088114619, 4.675278247447324, 2.119980286770783 }, { -1.0829614414792337, 2.945988642783271, .369461665741983, 1.5407117922892382, .7708616012092231, .17974954250554342, -.2716109541511593, .3531908114074792, 1.8343579748505043, -3.460577536152509, .8090203450703513, -.6122284968221936, -.806559357135381, -1.5668367386605098, 2.8736026849910434, 3.420054109238177 }, { -1.168507323640492, 1.5914488635921804, 1.642058207126734, .2971930128847118, .9085400116237934, .49559291937892996, -1.601974279307022, 1.4821152001808087, 1.413376820686896, -2.0308297502636634, 1.0378234984103036, -2.3654998583899696, .03405875041729345, -1.4188783161311127, 3.4728797216699108, 2.2280769299050687 }, { -1.2122086398476355, 1.2759663671998305, 1.9013226682161728, .17308445371470663, .6242186791800529, -.3176657367258022, -.46934750483934984, 2.3179980867750403, -.8188978580113944, -1.8056167024080974, .7274965829301131, -2.6671324063254582, -.05640993452018416, -1.1842202973775808, 3.75203989260587, 1.7698190061859476 }, { 4.141552402638993, -1.9747927657838638, -2.4378119664460782, -1.6002444113342684, .4238188453452774, -1.1901703631960394, .6308137632396718, .38036315333382564, -1.0198309832574937, 1.4653287198542564, -.3054541295046714, 1.79601030525126, .8655936127504245, -.45665756582545514, -3.115802421250484, -1.6739333403076548 }, { 2.6488624556763187, -2.0839453543479034, -.9437712399153897, -.5424473328978763, -1.7154406355426772, .21670427875288067, .6332787002706828, -.2652828731983457, -1.5823376788403984, 3.2133292125541377, -.41455075049009826, 2.260681780809591, .8526537405236558, .6820459861950753, -2.536208822334543, -1.0427383797804899 }, { .9479574264556536, -1.0384518040297652, -1.3334713519141348, -1.713212906551133, -.7024302951772746, -.7660592251664273, .5338494384671462, .061499802394261796, -1.2346103637143089, 4.548201593098126, -.592575923068768, 2.4178199956346376, 1.1769249564641162, .1293924514131462, -2.4638015455137894, -2.319459672103708 }, { 1.4784817873831004, -2.3457502243167405, -2.1760845079998137, -2.083120383990431, -1.3460938355288237, -.6511613908484525, -.1174608646082619, -1.651202449904064, -1.7484863724519166, 1.3275030584589034, .032998214676915084, 3.408665629743853, 1.2972338184387882, 1.025386542034149, -3.7196155467213394, -2.147749637741629 }, { 1.1192887695488933, -1.8460197615816172, -1.0096718904597963, -.5139444458367575, -.021031118857828702, -1.0361486449447228, .1238577354761583, -1.3972167563761126, -.34988430767261103, 2.7446432305451833, .9087684191748701, 2.4925839263935954, .6848185040810311, -.40672279125983946, -2.3265996950325434, -4.82777949702538 }, { .944292509090248, -1.4963225866303078, -.8360414239957239, -.3578076684084036, -1.627338932293057, .43549176468375733, .902915180376448, .4976565875286831, -1.3637535677952777, 1.83732231726446, .5961277916176075, 2.107712822526195, 3.138042825164111, .8922693419525535, -3.730290870117915, -2.4596716022741467 }, { 1.6379705872583337, -1.9880231396843508, -.23054769027287472, -1.2130427441320086, -.2920112560265394, -1.1170237313305615, .5202571487445433, -.5929662506493018, -1.673490268897744, 3.3372194890271625, .22389377339190783, 2.3501637907166453, .561579303011924, -.5780960553744277, -3.110965029759685, -2.69478303290551 }, { .6835412171495645, -.8022288564982099, -1.6207380960026907, -.7240281466136603, -.6450570409309301, .3547970013141739, -.7451578751701399, -.6383740726613842, -1.372236675700636, 1.7476353785802607, -.09319500060662675, 3.1347391320400857, 1.6520483217173103, -.46272921993757593, -3.0516127318522823, -1.9610252323585342 }, { 1.390007939388017, -.17028034094032762, -.07115252153268674, .1908585193810611, -1.324877955060099, -1.2608825253364815, .21319048926570117, -1.2989819746127331, -.9626845004493921, 3.4363632196705183, -.30121170308375794, 1.4246073074481005, 1.616113392908102, .9701268415322598, -4.086905980280518, -1.6892557733895606 }, { 1.4370875263321763, -5.016611928211811, -2.0106788099063246, -2.2218885074182335, -1.428690329909513, .13808740017700308, 2.0249405397690867, -.37929889870030753, -1.1315026236371264, 1.5258962077869358, .6149187598828421, 2.670052517273531, .9924146753155475, .1615556109899887, -2.6824944590957664, -1.3632962493604819 }, { 4.332180508575256, -2.0722650462665944, -.09599996080552907, -2.028052851362709, -.5865831511891798, .25344014668508447, .47607498314923985, -1.280681876025663, -.5806696781873825, 2.8506038320015277, .8086433179391773, 3.862273331256534, 2.4952261701528053, 1.7118184392811158, -1.410443591218664, -2.699492835120986 }, { 1.82336458428123, -2.6873249324052595, -1.3402764849313535, -.8339514860927845, -1.3979501140185704, .06418208614360096, 1.6800720173911252, -2.1496427799897075, -2.330106838407293, 2.487661473463009, -1.1412097565555803, 1.6186171134564498, .1832729613992343, 1.743655082574039, -3.215884198200762, -1.1402090029127876 }, { 1.55772322994351, -3.033097816347577, -1.689178851621809, -1.8186398421723282, .15177741546855253, .2652232874156251, 1.9583986029422578, -.7182956540485095, -1.9992741427017788, 1.7817873850928763, .4564865111194654, 3.899853167407911, 1.4965830149073867, -.04770864681708944, -3.894045989580122, -2.594837073286809 }, { -1.0393798039206006, -3.7514153673725135, -.22361139138248165, -1.3933387255627823, -1.9311251444196726, -.4499668129174203, .6106468742827303, -2.8944361396740996, -.5295221018183337, .5626406137045349, .531448460688468, 5.614678783784537, .9077646239261066, .1959225165683614, -3.49955807295914, -2.1111084854736046 }, { .26007400731794555, -1.3630338062414635, -.8637795383584639, -1.4737843107275928, .2115372811939658, -.20298934766520052, .10793352163024271, -.5604672042141303, .07708271309366556, 2.3169926279165596, -.6435592577120204, 3.1106277820132706, .9965112599118061, 1.3650065225974704, -3.6784533933922656, -4.087299883601619 }, { -.5991874117175212, -2.986823177698494, -.9878663353812103, -1.7441524811574483, .27216488854450965, -.1132425916713088, 1.8226443545008648, -1.7179381271909182, -2.120990706345647, 1.7101584222239181, .6427680326374354, 2.4694935926956822, 2.303638494757437, -.6355957703081282, -3.031737779417587, -2.5637471697648557 }, { .6140357294426741, -1.870022121480207, -2.6948921399744306, -1.4855964697496924, -1.6416683674591364, -.818737796207188, 1.1699648605047728, -2.3134599267639477, -1.7555539705727303, .8559846199714293, -.6673266330556332, .40826260015155125, 1.5716005399703485, 1.188377268082613, -4.265988078982396, -1.8896916193194246 }, { 3.133285502879125, -1.7306397892406582, -2.2676521822191344, -.9870960671051009, -.9548382864512643, -.06486174538888846, 2.1090944600404082, -2.222368989050552, -.6755060022761733, 1.0066871958199886, .7917982560106984, 1.9906760561245396, 1.0408178399060837, .762975092992596, -3.408749232954669, -1.438637817621385 }, { 3.081216636853265, -5.470448904892055, -1.159669532163196, -1.8930251989134093, -1.5956547474957692, -.2723620819919228, 1.457862183137197, -1.484916761913893, -1.4800410006871967, 1.5210007689696137, -.1523003682723898, 2.6620866335604796, .005331926697998575, .7668834182475334, -2.696579749101348, -1.4692550577565935 }, { 1.9437611550903822, -3.0870160498386583, -.5453729969113038, -2.39163570330426, -.881441278869191, -.9390773332249016, 1.2340098304737792, .6715199657640091, -3.46064247641549, 1.4292323396350577, .38634705227243904, 4.401040754411133, 3.850327484911249, 1.999697979131098, -2.5478021755142977, -2.6894989176977684 }, { 1.5307733598974855, -2.4628940973844955, -1.0635679698582825, -1.5455991683180497, .23976382329996568, .07372030984618713, 2.278827057394861, -1.4789764195215644, -.3418940713809698, 1.40492103441937, 1.1735805754014375, 4.390092465381802, -.36424912003597254, .3165136710492341, -3.3735279824325657, -1.8198700370382355 }, { -.15741967441548219, -4.119380235109049, -2.9102408154643333, -2.4882949844739772, -1.064184330134975, -.2977219770484766, 2.12293333586858, -.2253228407414501, -2.37437498943357, .9711266681131601, .4037029518146864, 4.238404622922395, .7819376480557565, 2.5980195138831, -2.7946973174550553, -3.4586766009268874 }, { .7036132414673877, -5.41267970339233, -2.140325860501247, -.412971003611804, -2.048700608622341, .15271643341719016, 4.06900376016082, -2.3979763492340536, -2.2013533064445854, 1.4724641155367353, .21101199299767456, 2.499353620635694, 1.4343441763132208, -.0857848791597762, -3.2164346032833673, -2.01391598478461 }, { 1.2489615757913766, -3.003536070917, -1.966629856071092, -3.1411106310313395, -1.1808487960740734, -.4153427051119388, 1.5057994779331858, .16259723638720933, -.23937772656282247, .39694067320918, -.7557845664934186, 4.729671121330105, 2.1439075346401784, 1.2753397649259852, -3.6512889802400705, -1.281455593690808 }, { 2.7761884207269096, -1.1556155724637474, -2.5986141873306257, -1.9150407875130415, -.8163026647382117, .09906636236067026, .5562463786380865, -1.2471705111673652, .08139980002740532, 2.4432904654189738, -.5598721712348566, 3.7055421959488544, .7898146864665325, 1.3908088698565568, -1.5880921419719436, -.4052700904429402 }, { 3.028804976181795, -1.9837625309018063, -.6488672168710365, -2.094348291852562, -.46475741312892677, -.0015102980150681522, 2.3281694096848207, -1.8232049729863586, -.7276882112226059, 1.6127594081607872, -.1284644822821384, 1.5741501303434273, 1.9992808388114196, 1.3748171387553723, -3.8623732170747402, -2.191116887459642 }, { 1.0985327423955709, -2.0743610268340857, -3.336943583210078, -1.9003887798303536, -.6176859825932242, -1.116328397508134, 2.936948347287727, -.5190539237953024, .03481125268818346, 1.3399496655531002, -.8153693687343261, 2.136162110709395, 3.2445826716655355, 1.3433544811689566, -3.3464536467021344, -1.9230012152751434 }, { 2.2986506950733174, -5.245593094647767, -1.2230201700380843, -1.3875486108621429, -3.4270142089189486, -.7686421168809445, 2.9090966089456534, -2.3510413811538333, -3.2488763033347547, -.45168616763526764, .22738652447895166, 4.364754294371109, 1.2592261792305075, .6233488515296489, -4.101747504517736, -1.9616467070117163 }, { 1.2583028963591594, -3.276023073624188, -3.283381734942372, -2.078616827031505, .5535338604293032, -.9364776651952794, 2.1517502962149364, -2.582277111704994, -.9371653674171261, 1.6532116583117058, .2481550322299377, 3.1867938436772683, 2.340608522438428, 3.107937897585947, -2.247093046234678, -3.402434662787689 }, { .752700289669484, -3.3395745634513836, -2.3401627921068195, -2.336805922584568, -.4348852672980377, .07889865781183551, 2.5951025369085636, -1.072900219945531, -1.0720715470057673, .7322034475693221, .38739838962558093, 4.162222199083305, .1813386037368893, 1.1951909453922915, -3.0204526489272676, -3.3918919203285647 }, { .7742258949098174, -2.307573772975659, -2.3992842416323317, -2.1789221733471043, -1.171286230037162, -.36988995108991174, .597088964737057, -.7620778669152661, .26669137973225704, 2.0738572789603342, -.5046119956337204, 6.283705060149146, 1.3416577848775006, 1.4309975208090908, -1.7276058259762481, -2.650851163374412 }, { 1.1032145570074638, -2.8310906656291013, -.06925251025406878, -1.1346259480202745, -1.4724399293902704, -1.2314071439542438, .3701642383675318, -1.3550497885581894, .6950809638503106, 1.8735410810350415, .922028585226802, 4.239939608681581, 3.438866971620942, 1.2229622978806962, -2.1857386910180088, -2.9207536263988936 }, { 2.701551932889166, -1.1602304414082354, -1.1545534661856014, -1.475026599367391, 1.2360971778612935, .45638307326367816, 2.0126958862180064, -2.7521827570116093, 1.130669710849181, 1.3337877153401232, .32239536103382616, 4.148633266069928, 1.1660209026041548, 1.5418525499029565, -2.768671777154721, .06600165060257115 }, { 1.599118747824773, -2.129649095718125, -2.8574179559019384, -.902378524737531, -2.5255229622467814, -.2933965400248158, 1.823295342308024, .6588685728841882, .4833095999571501, .4028613703634517, .029181428357672824, 2.6106687778765743, 2.11535055614746, .9151388971759982, -3.2930780442988845, -1.530995945605748 }, { 2.4990976979139985, -2.692405672173207, -1.569963195902875, -1.2762334100716317, -2.74388707611436, -1.72222929347264, 1.7152743291135402, -2.7756372912416003, -3.6177935206401965, 2.3554633448607945, -1.1720822651199754, 4.134688472928764, -.8037056809139675, .4449264811940088, -2.9896154434223075, .44697542663791306 }, { .47366285263266583, -3.389624782981649, -1.7468137139080888, -.6462002255849422, -1.7160854885269061, -.6368308562980082, 1.432209737763548, -1.8885096615566683, -1.296878700479166, .14229716118039215, 1.0519460962425382, 2.6044628624910264, 4.425116493149227, 1.516803753438368, -4.244475038033158, -1.8000932061859092 }, { 1.6307533587590668, -4.893211870599021, -.6582099497468793, .5374978446957711, -1.7914829178360254, .2769600266126487, 1.6891387188167586, -3.8200329091454557, -2.570573042388478, 1.4588250209722096, .057516452657959065, 5.322789937528393, -.7068099802509648, .8875561908181534, -2.4399050767806174, -2.5059522160760377 }, { .771633130281001, -1.3077239105991645, -1.8528170297223627, -1.1383693922950249, -1.0231303344766625, -1.23079030944572, 2.3584659686577103, -1.1982795988179604, -.9627725377579855, 1.3193419958797303, -.6880583433023559, 3.529301409908259, 2.3866112092161287, 2.2952580317399724, -3.010716531079324, -3.0457957923468135 }, { 2.618368983139422, -.7742719389341597, -1.5035493946639527, -1.018130384964103, -.47048021874243867, -.1605436421843218, 3.2501249918222053, -1.9457558146311589, -.8047584421119028, 1.0597410656865036, .3166869714443968, 3.3979935255477725, 1.8455212626382704, 1.6944556884402626, -2.339050064121558, -2.709972994204749 }, { -.5170221779131067, -1.4022545272871354, -1.0256649947942948, -1.4522630668632288, -.3585898771361609, -.6249070966222514, 2.214983616379059, .09387097799378905, -.8494461554506131, .7299442628489884, -.22995324379974777, 3.6908296918433114, 2.761817490903192, .4118631110291338, -3.6076853630138714, -3.9093257265345556 }, { 2.8247281535557205, -.6819662144845257, -2.5212163608897002, -1.7655050380735875, -.2521429307435903, .11899779243060497, 1.685852829798633, .28557851808876183, .41028624135017944, .2831879025006277, .8923826954122965, 3.0549778828664977, 3.01606288097654, .46331011219202045, -2.709091992066399, -2.503461441027967 }, { 2.0939574589293146, -4.177991926401234, -.8880527023410768, .4283939115136118, -.28640047929485446, .08973974330526176, 3.3174555236598207, -1.529188711336113, -.9186742881560991, .30866899820223026, -.08665791512583874, 4.162140304016154, .18262476473794506, .8237084108666661, -4.701805566565538, -.23663301467995884 }, { 1.253153338342186, -1.7634167921449653, -.2879287559884205, -1.1927876715667363, -2.704652651167435, -.9235805604207631, -.005365066885973335, -3.82119404595012, .6160341047943295, 1.8803096429994581, -.24775823216417853, 2.7911760035838724, 2.457553957276956, 2.362258940552557, -3.2446037348966668, -2.3825928726168604 }, { -.30435665611321444, -4.776148661473303, -1.6364306180560682, -.46051044878186537, .3834838348664273, -.9763116270371663, 1.4465695731706527, -2.51004540533113, -2.5980888258494264, .21328874071226914, -.9486458610137164, 4.267640252439637, 1.3131326486821693, -2.8367412967014203, -4.2147857009660195, -2.239003479584226 }, { 2.0398834104961283, -.8600397682183815, -2.795178903447031, -.9988975005839253, -2.2461377694265123, -.49967705593114053, 1.1130328433738446, -.921388562273914, -2.1352487091153143, .2098512399066817, -.09300337489154993, 2.0760311158837, 2.3358112456437015, 4.6386373172799145, -2.7035676235886075, -3.2538336575641904 }, { 1.0176811128523964, -3.67084515950637, -2.0008144870732427, -1.1501656590265903, -.6285420508756585, 1.5005079840230602, 2.4579128108082515, -4.178418289418542, -.30302860542501453, .33721781289638625, .1282901483060836, 1.4922047118588961, .9254779417193935, -.607661054403634, -4.7059559978870995, -3.5376987587250066 }, { 2.088779863208167, -1.8197776610195406, -.5429038738561103, -2.1574200067536413, -1.711123265436773, .07260562487563806, .8300592034088068, -2.7853700729067445, -1.6143189673258733, -1.2085159538300894, -1.0841460667977842, 2.3700392871213274, 4.407510122737308, 2.0071917498218492, -3.1256755920368797, -2.866215097697212 }, { 1.7748076642602861, -2.2685511825001288, -1.8231213868446232, -.4735717052675489, -1.8448859980117498, -.25805769260298556, 2.039041270839534, -1.326892804977401, .09069903338876242, .1445575863138086, -.23169073431854484, 1.8731098686709111, 1.1143752331473709, .2153363426698601, -3.014302298317977, -1.9684477695672873 }, { .8037979657219001, -3.60866558643411, -.7501072965986381, -1.259936523565092, -.11621540141625303, .511976188967149, 1.6515626724968226, -1.642294481259057, -.9269060837456015, .09231541177325833, .011359547729421174, 4.012914185146777, .1354004796246078, 2.1459240740239838, -3.7169437669600147, -.35736794072120004 }, { -.5715965810315703, -2.6308666993901197, -1.2272094375040639, -1.5086723423192059, -1.2268990284919186, -1.9795271946599415, -.9249567125233626, -1.707495619837159, 2.202057813075308, .10935277186886407, .8768499771563577, 1.1640259477418413, 4.322321415410686, 4.499203223671942, -3.8573016117081047, -2.4107752300809757 }, { -.3923521634988104, -2.2628739164936245, -2.6957829513063873, -.412018855966948, -.8598449732822034, 2.8735823140452936, 3.7554707690546745, -1.9031845173515898, .01801479910387112, -1.85959845386189, -.435279553761383, 4.458807354450009, 1.9358055881150544, 1.9927640443252008, -4.449217928180396, -1.0481968649959048 }, { .3887691672231204, -.886394292961718, -3.0262998697182875, .3446992390069607, .08752816460129027, -3.154355610108776, .5925746397039907, -.7139243569789631, -1.8227961275814686, 1.135348222870311, .6789933702810085, 3.787871341236793, .19606729506573475, 1.5907148825784665, -3.405158261501095, -2.350420580571964 }, { .9094066378313146, -4.455919676768647, -1.4974554271805873, .565423785588852, -3.115114236309652, 1.4966299542618342, .2754290298380193, -3.0496232174954256, .5324365394930537, .19853402822757918, -1.3804755613084392, .5706707867232319, 1.5373102259943743, 1.8992504990039427, -4.515437196940997, -2.8640813135448493 }, { 2.498161528649069, -2.8781844154274454, -2.102636460225059, -2.6586870296663507, -1.4526334182250016, -1.1666514293308479, -.05260411578156674, -1.8988166544725458, .13815240268395565, 1.5349836695112278, .1273580454552844, 1.782578948805001, 1.7385143796125533, 1.7362941068650561, -2.8289752477684043, -1.7877858614826987 }, { 2.509660697240247, -4.803038881936004, -.314253450994487, -1.3220126520571513, -.021614567656794377, 1.700043138251769, 3.4591922084483495, -3.854359073140342, -.40772842650385105, -1.4356615805999817, -.632026655143807, 1.8204974706652242, 2.5197994366965064, -1.4569073444844944, -3.4614335487119834, -2.602065798124469 }, { 3.140950000642761, -1.9346180022073491, .35431617208748467, -2.491841177537586, -.8195709757342069, .7191449285355125, 2.66240645726433, .6074958067292503, -2.051371774626056, .7569814402040045, -.061343743695516297, 1.1547583238545367, 4.255900860342498, 1.8000130768023899, -3.890607235197152, -.8452484135907861 }, { 2.6579116633899806, -.5391806774536068, -1.1557009967668181, -.18361597212649383, -.4433132827049176, -.9837102307827457, 1.0700360887784381, -.5349760095123286, .4552380875150293, 1.6498149215107323, .6933639771684673, 1.385893683012026, 3.9230579600735878, 1.0074794268608238, -3.215169443662186, -.2805150357220617 }, { -.8026436701905302, -2.5350536199689926, -.5451565187480975, -1.1847089395067292, .021236514449725775, .9149668316700684, 1.9293882227069388, -1.608215880540104, -.1893418288217375, .7666124649773826, .9479480999057919, 4.092470245531016, 2.286349672887535, .5706161261193097, -3.2592844639776937, -1.4240359013625268 }, { .29266746426505286, -.03408021377045629, -2.772460985199531, -.3653642088588175, .17703583283573404, -2.562154363941691, 1.1233395212282709, .22211466593777204, .43424437577611186, -.930655213336293, .45688024402214383, 3.2109302524688794, .28594561084347303, 2.1248043421015868, -3.32863114966531, -2.4510443872701924 }, { 1.1507264674895639, -2.4302253098691815, -2.456036823928304, 1.4962184047798204, .5684900605803993, .598143972664722, .9382636830766828, -2.6253904080888417, .09525513702618607, .7639890065544743, -.3278933142824916, 1.3847718512137597, 2.2437426126030444, -.37041694522298835, -3.24097176776605, -2.051406806755653 }, { 1.7848628828701008, .3659366389584016, -2.361000280536643, -.2670222939321169, -.7898126297207134, -1.2948407767874932, -.011106380368815911, -1.9152599497511724, -.7138669914975064, -.218193161295593, 1.5195735650469662, .6560601453809362, .24658004826871355, 1.3395892933215152, -4.669138618240563, -1.7747957627749082 }, { 2.4595113758156373, -2.314960631477261, -1.5540949501653778, -.14851476450937895, -1.007670832041355, 1.355176410376863, .4340614060057606, -2.4339016848176493, 2.0389944366404182, -.9159221445569172, -.3066601042615482, .4867289038682114, 2.3320457969964203, 2.4418087641561987, -3.61136116685891, -2.704037931403204 }, { 1.8473463856461723, -2.1326074909786916, -2.397235205748089, -.43437913691814684, -1.2545921158050615, .1944210382410818, -.04755859753864109, -2.56399880315303, .031078775213825172, .9121055884163143, .3838218798230358, 2.38434157613113, 2.556530805376413, 1.204137651536951, -3.005775566495537, -1.6221435415155432 }, { 2.8365225400608978, -1.0162513586131583, -1.5960296174480084, -.5366525169007328, -2.044529522951807, .6200355371896697, .6627381650427728, -2.031201004556891, -.6147410666582853, -.8279152572829187, .20807814021879933, 2.8125984541391778, 1.199282631824852, .8662257633946855, -3.633308237633905, -1.927911562612791 }, { -5.153203936500172, 3.185912660753851, 3.2576240902583735, .6433119096904846, -.4232692091589801, -.29591236841341534, -1.575266572548131, .5057703717965351, 1.2082855333962346, -6.089766442154884, -1.5627809715999845, -1.788523476262566, -1.1475436941906887, .7740420091687927, 4.532830881964003, 1.17889367361806 }, { -5.233294570370505, 3.0739742930314025, 4.104318924137309, 1.2971501716863711, .5644354636881144, .36626637654563754, 1.135554209471324, 1.6076572377026066, 3.576790299129707, -4.420160522009036, -.1876309781847625, -2.410265727223606, -.5412576322222787, -.3745123508793712, 5.276100682014002, .3122986749676443 }, { -5.535894778199243, 3.7107196368178332, 2.770851893166338, 2.0504417152798404, 1.5158004325953414, .01248546361222172, -.4773730374849248, 2.160712480981271, .33243795225880635, -4.658743367476852, .5418562557891189, -3.904354535891125, -2.174029808281203, .9000356312608623, 3.7909783785014266, .8725935278535915 }, { -4.194773698463474, 2.3735189066521305, 3.19402179742445, 2.5401006116023943, 2.7980107145664697, .5159031824145369, -.1362814257246383, 4.020177997376981, -1.0534444858592895, -4.005487485468515, .41434283254978616, -3.813455649546613, -2.0531321916906826, -.6936335247397044, 4.0294911896802335, 1.2311625912892363 }, { -3.5332061829437693, 3.307169571196825, 2.5228700315938624, 2.2087251980586884, .46702557417677526, -.5923373434079057, -.7401917860239584, 4.210992314165257, .8671244656587044, -4.591425523073778, .6806962057434391, -4.0048391870797095, -3.0365535134243125, 2.2731101507612537, 4.968596654041637, 1.170191811385984 }, { -5.4833373592388375, 3.150438332011904, .9787492966823282, 1.2521993290849243, .7952377799882991, 1.950285415671944, .32017429521247437, 2.6273269644067656, 1.7450353350181178, -3.645057310841098, -1.5077018000209106, -5.058427505939734, -1.0318668564074982, 1.3408501588460497, 3.960433686428378, 2.8391624533229143 }, { -.42856690164324157, 3.2777393763113567, -.4308613479623698, .9106437754479644, -2.21424085007632, -.9376712343401848, 1.3743286265689616, 1.0362200048908168, 1.8986130930015557, -4.8107705495874455, -.6954655117770205, -5.779667045018551, -1.7029431799567316, -.0846278721381955, 4.067179578380989, 5.771377092159215 }, { -.8824470519240578, .11792035534802374, .4697422497335389, .9032489106628628, -.6010562442530705, 2.7841517583407893, -2.658842892400028, -.9096338904289306, -.1472073137119457, -6.863093382432924, .12745366462513585, -4.063973524392284, -1.240027544788031, -.7268054123397267, 3.3170800942389214, 5.3414479638917935 }, { -4.28414723230953, 2.2515908254889263, 2.9748953289372726, .4677438591272741, .6205314495043335, .6619295658168238, 1.2660120112027238, 1.8111725164560528, 2.2516995487639866, -5.0873638748476875, .7968595748309707, -2.275339532073914, -1.7564341370797971, .9887584833706543, 5.683221654527246, .19365898909074694 }, { -4.889486316141901, .8468776330586874, 1.949420264753302, .30196650617786674, 1.5380540729142973, -.09877636496734923, -.08441808805253591, 2.952943677233473, 1.7540749636826862, -4.400803451818373, -.9085057945174988, -1.7860559129748992, -2.4575368691204322, .7918392096445206, 5.298729480541078, 1.3771591370135567 }, { -5.911719827462743, 1.5472534305278733, 4.187386717234974, 2.5314156429489474, 2.0059666260858133, -.3260130457032316, 1.5886897143249272, .7647237998829723, 1.2417470693112787, -3.6388259564624335, .012287380837208855, -3.009637946511854, -2.253381432824023, 1.477603734159435, 5.180219988745786, 1.4131543402804108 }, { -5.217828030891771, -.046299277252365004, 3.5622862743470214, 2.939332872192766, 3.130291160673079, .06280561287693816, .7708674988197001, 3.3681491033141264, -.5293754694399534, -3.199108697638331, -.05767141317706577, -3.3348208295690287, -1.3883043795572965, -.48251674858203736, 4.0822529216846, 2.0948951398000313 }, { -2.4452154130419763, 1.2804482417387506, 3.0307147884529684, 2.1072402927372607, 1.2730566924903046, .2733877144317685, .8487655732773854, 3.086442141920754, 1.4636115948059036, -3.601202394215214, .8134129955171497, -3.452574690799383, -4.133888235221866, 2.5711298528139825, 4.796111299404473, 2.7807729719617265 }, { -4.286188195124074, 1.942136514963574, 2.6324687313331174, .7071311397158193, 1.150871786988208, 1.2064063435342647, .4808440543848807, 2.609718234383264, 2.034303104541343, -2.1837789470332782, .5399398769086577, -4.342655621943131, -1.3398149368021517, .9399575394983857, 4.341213105808468, 2.7312707709878605 }, { -1.2630662914099897, 2.825683417578349, 2.1796573833763446, -.6479625968773659, 1.0112741113585861, 2.0577599671416738, -1.2941749533613942, 4.269126094639033, .7028051366687019, -2.3875402945357793, .8531743828131385, -4.865645156677153, -.7934722603259604, -1.3686511728965358, 4.598049074596058, 4.314091054664353 }, { -2.3429370747727813, 2.3023902309278856, -.559669257361286, -1.013937750632194, 1.4407149958181833, 2.1279768683218263, -.279945518602711, 1.2776923316739217, 1.2148907995669334, -4.947945884946327, .1440599121395752, -4.406227221878177, -1.9031896627899325, -1.1676440374960257, 3.5027217433090234, 4.442103221587548 }, { -4.874579373641823, 2.798310112025192, 3.927956966272006, 1.1747968480704365, 1.8744909572416242, -.1993535801763359, -.6542256688319226, -1.1834099968382374, 1.258377556111111, -3.3785985740523072, -.09063554564741617, -4.2660099868752654, -1.8147412905923075, 1.022033794444765, 3.896555741539935, 1.0857196002577714 }, { -3.5825397955091804, 2.189643686678453, 2.162760546419163, .3342708798909527, 2.1088313357289783, .8442780335563356, .13247159899888247, 1.4007546480982782, 2.5859741351894048, -4.337082050280955, .2934832679431557, -3.611507733471574, -1.1537643129061625, -.9038220717521473, 5.694033126690115, 2.467471614418443 }, { -3.231623709025102, 2.887308811366477, 3.6238949998028978, 2.1310261834350497, 1.70213283807343, -.2622564552329034, -.7732618300565801, -.617941536922925, -.4240941785531915, -3.006278916609048, -.3472324211330696, -4.2447925120320535, -2.475542641902694, 1.8163753497371147, 5.434260913969139, 2.128366666416271 }, { -3.8612459109140747, .5832273676931906, 3.5118389789459488, 1.0852364422203136, 3.1898479852155357, .916639221639913, .9985828945822938, 1.9539710302311382, .13552158312737891, -4.261189947867479, -.43350858513068324, -3.113972819853298, -2.029720419489837, -2.91862662623523, 4.540799851644841, 1.720967422956421 }, { -2.424753665967674, 1.0653154451719926, 2.3139459941839022, .6682021171761934, .5041655008517201, .9481794147129956, -.6515833418971495, 4.2320140858438, 1.9136820918865243, -5.109913989604843, -1.1567094350707445, -3.4523879679222547, -3.905933944109828, 1.6798242338845397, 4.9718944201496855, 1.6256915085399362 }, { -1.312312964890641, 2.4211585959078374, 2.106043305292898, .5971164430279796, 3.5176310271236995, 3.822198302187241, .8418116651755176, 1.4260732474176139, 2.6083043759706452, -3.173649415539613, .11695552434519411, -2.6131762877292246, -.7394906840269999, .8213190733574761, 6.033793803325508, 3.1432607403995076 }, { -2.5875521737004044, 3.2059603450906, 1.5330867338186212, .4604361490088659, 1.7043418561953292, -.6227356364047184, -.3026393194550061, 6.3457593600211935, 2.4968412851483888, -2.6490599410075704, -.859329980831378, -2.432062845168148, -2.2844380463348304, .19980508310122116, 5.2285673507360375, 3.3474833158381343 }, { -3.410108338588222, 2.969443736461365, .08569971516214774, 1.1801407660218541, .7180086365484875, 1.1513401870393385, -1.2531375856288085, 5.253618069972761, 1.0981017056186544, -4.975787793342795, 1.0156610538138013, -2.43867256583678, -.02809375245015645, -.30763467806735334, 3.3119649553122024, 4.6625290686415015 }, { -3.030936771961331, 1.7316784495311448, 3.9760904764252825, -.6396835806191062, 2.610741298998552, -.3460348405361022, -1.0468388248034286, .00771450447694222, .1590942041484114, -4.962033548685599, .19710651775933918, -4.604005044841107, -3.234861607300503, -1.0367283876312985, 3.6326590496419597, 1.5622486607009176 }, { -6.146741258030448, -.34641499281108995, 3.4816410975027257, 1.9593616489392771, 2.2245565798658093, -.6014772379098129, .02890624450913674, 3.10255214370386, -.05282335408530397, -1.4512895284140626, -.3918360933472321, -3.413585856189098, -2.377789815880395, -.07084137529381453, 5.638088187503078, 2.8056766148284153 }, { -4.955901617318364, 1.5490419184444177, 2.507232480183437, 2.775081308140356, 1.7599833096737554, -.06353879073212651, -.41688606194254735, -.28539282423912116, 1.9466393669498017, -2.7007148193053485, -.5556759146141355, -4.4209898186153795, -2.172498314751318, -.6390018417605313, 4.568370837372564, 2.9984115269690697 }, { -3.6779780764296337, 2.1561857565315115, 3.1696030143616776, 2.7772787810385395, .2660427801168283, -.7901650441690817, 1.0751135937897451, 2.8067800429364596, -1.688174243011175, -4.696775550641301, -.5794318969631876, -3.8743230978343433, -2.831244725249646, -.9229342242300628, 4.436013130962287, 2.299665885944281 }, { -2.0767447688099323, 1.8092747795150237, 4.504648148718178, 2.053602183289927, 1.120472301795258, -.79169627051303, 1.1982268797377398, 3.107798203404768, -.32578712293805434, -4.150407438953734, .3768654898633903, -5.424256512037949, -1.6144094274491159, 1.0580658912388186, 4.47333540864489, 2.02693330536653 }, { -2.0787887709325963, .9102718391382387, 3.3392548286237087, .09817803338754708, -.2065155866041394, -.7286216673234482, 1.613845645584753, 2.5648789468473354, 2.812361288399032, -4.531203593228521, .26715220269598977, -3.3018169030278943, -3.070569919136276, -1.0184128039140121, 4.0354747892400376, 4.147421010367035 }, { -1.1325182244730005, 4.006861122264247, 2.1873625056362136, -1.0662765351774648, 1.9778532200883667, .187808940839827, -.38223247019660567, 1.7953087229730527, .6499606423996845, -4.066927578390326, -.516854374699383, -5.06780379466261, -1.5530997483987588, -2.2115611946504914, 5.416407585670617, 3.5649548938301616 }, { -2.381277911454991, 1.8160345706020775, 2.3609616497809665, .1152019851571218, -.35463079284921234, .5799859929745534, -.714358544966445, 3.441381723723841, -.9653933152885655, -4.223032325500965, -.22722375341761214, -4.320661630582955, -.5618192244065898, .494998728178793, 4.703325498784445, 4.33249653712186 }, { -4.565246284253064, 2.5927835713073173, 4.036429438814307, 2.4962632704977805, 1.5610454316442495, -.5366947489103515, -1.224328516428146, 2.0799751394450077, -.7242350952860342, -4.151691898367316, -.6999957307837278, -2.8919905830994597, -2.9430125613891978, -1.4546898102576389, 4.84707448706923, 2.840435848759184 }, { -3.012336528261235, 1.299924895285291, 4.994307784290084, 2.3781140064103936, .47353740117441784, -1.1382588529122326, -1.9276493795710534, .7110219219623526, .925971892116195, -3.4348477127920263, -.21141234352940855, -3.075448579644623, -2.2435217032677253, -.2609389929535506, 6.254031637914901, 3.16031866637 }, { -4.631935928263631, 2.612617124169445, 3.337443326844801, 1.8811487427123332, 1.8502519219817584, .1627491400734423, -1.8496987818704385, 1.741662854192211, 1.1399424777373675, -3.868151064777222, -1.0244538847296336, -3.4876612375573646, -2.8493373712101566, -1.2781130026840364, 4.561300602088532, 3.332318760917037 }, { -3.6231732117847644, 3.0285870726461224, 4.2813575455300725, 1.6303987960936694, -.008536909003482198, -.7071296843297672, -1.7057672373771677, -.0008428919859499941, .09619093713893712, -4.672828105647301, .43348796824806857, -4.523221948328728, -1.9312831665585004, -2.615043321354843, 4.722916274453021, 3.938897028130586 }, { -3.0536190040282687, 3.559283611123259, 3.9847182366610494, .7468832275924011, -.8652307817124059, -1.1722196692226563, -.8984349087150705, 2.144689850799018, 2.206294659399938, -2.490648801700257, 1.3199009738467338, -5.2088955814887665, -3.6020928865488346, -.8613643974489333, 6.185319599477225, 3.664352753604048 }, { -1.2226944650869305, 3.107873434267481, 2.0877011863261288, 1.3853673956512988, 2.3377894743665104, .8407502273769664, -2.9636941217719603, 2.4876681151844338, 2.0726462907898893, -3.964414183442442, -.47238017085403233, -4.916542168242011, -.22914603826359112, -1.3365769187300702, 5.525319364893161, 3.3699853543903244 }, { .02136803638410251, .3338376002089515, 3.7252249609236183, 1.8340796816604774, 1.5115535341584962, .46344785933651317, -.3382995095351293, 3.0278046403126853, 2.553004334658178, -6.481794879644341, -.6419484182029709, -3.5995613818334835, -.7793227919785837, -.5785373301833728, 5.114695465411493, 4.602559222343769 }, { -2.06254191725426, 1.185709441696481, 1.842947463905024, 1.891222510749477, 1.0690927065890354, .5015129387967866, -1.240657875730065, 1.3847595387332092, .37149997248110483, -2.087129434736821, .6329454707479227, -5.158019911563205, -1.1480607795526212, -1.3454980917157084, 5.40399074860629, 6.485216389510379 }, { -5.122695722131009, 3.5056804841010485, 3.871334154316823, 2.832106434635835, 2.402745645971167, -1.8074081026820232, -.9123315572353041, .8810442008924586, 2.3332858726635277, -4.21090079278223, -1.3453199544380465, -2.4818093620253383, -1.8253109777651781, .12900494533809226, 4.750616901451674, 3.163930419176157 }, { -2.585828618899265, 2.7222324288968958, 4.681210321437199, 4.198629963342654, 3.0989616271866414, -.004035433419239368, -4.268217199512538, 1.5261540537646041, 2.9086703104329654, -3.2382634441368667, -.7944967427604857, -3.9829715195259863, -1.4532376525873132, -.5517975748480444, 4.4726212575856, 3.5412094967946137 }, { -3.7686193464112026, 3.0042270148916255, 3.047707099891164, 4.514203330067549, 2.4892943824521634, .9563305405619673, -3.8591961616084385, 1.277540797504495, 2.3492672205645495, -2.229427850054869, -.21041824492894992, -4.085568374472549, -2.3097472387914, -.9378195659368909, 4.967565790067093, 4.677113676208236 }, { -3.5544131279915394, 2.3410142039801016, 2.569691141310325, 4.240140056242294, 2.3223977497363566, -.3069174475444775, -3.2428065480778, 2.5706602807134376, 1.4218190634678973, -3.8624825944317336, -.9697094450053486, -2.796074929427364, -.6706003933953723, -.2020809300395074, 4.678133069660837, 4.847945505850381 }, { -1.739268896258858, 3.4945413079866796, 2.960742519224682, 1.4380430526759933, 2.4117660716009484, 1.6657982727444485, -1.193822051214478, 1.022146740915195, 4.056777783089691, -4.031271294858669, .7083460973597782, -4.450819960219361, -2.081614849077495, .12937859009651845, 5.54355579397723, 3.380605540278797 }, { -3.605475446423926, 3.1007142774994954, 1.5893916422157999, .8423018744899085, 2.4103281379265855, .11842899160379247, -4.715337382450468, 2.2080871138317275, 3.1763350196401445, -2.6697616239127724, -.7055648768058643, -5.294274501346635, -3.489448430491854, .015741551953973653, 3.193462144409548, 6.310129387789565 }, { -.910205094468239, 2.1810953320599977, 5.214482181219606, 2.9724917293923014, .821236921443103, 1.0642426034882688, -.5699981530377745, .03112879869867951, 2.194040663661505, -2.7041622856236422, .7281704744917, -4.500346719998572, -1.1093217389328067, -.6404683130141002, 5.635171828227805, 4.031575918922145 }, { -.35534510827825694, 3.06952449455676, 2.2774684411606363, .45987249709265515, 2.519481126633552, .6647307135279232, -3.3613371891741557, 1.681060365123239, 4.051791006711962, -6.396236941992592, .2912253512459631, -3.701958072277565, -.8060630270094165, -1.3860427417453216, 3.886023099438037, 5.366713924278352 }, { -2.9258773448665822, 3.8251012355968554, 1.6536557221460864, 3.742475215526685, 2.3104112921810867, .6875811351179695, -4.053523233483688, .5793438669269103, 3.9515133843869488, -3.3959358200571543, .06292576992422137, -3.443899479913927, -2.0326581059590247, -.9721751656555406, 4.96232530689886, 4.417929844171338 }, { -5.3332238481413405, 4.201395626256423, 3.5819389953173513, 1.57024333901838, 1.6039135258013186, 1.2079736552061455, -1.8071731071894355, .8514706900952366, 4.743910927990304, -2.994542807681384, .3245775683090895, -2.3450977483223236, -2.0316192211861446, -.3284299871814034, 5.211827490859335, 2.8221905068950166 }, { -2.5703641145620915, 4.742175893684849, 3.631435367926752, 3.6097898691318187, 2.844603382191675, -.021959739056513383, -3.65216954082416, 2.7360911933403425, 1.799168527657485, -2.577181528134803, .4901258116790211, -1.8048294440062946, -2.0421661222113214, -1.8695187995123592, 5.212964080852772, 6.603573787846817 }, { -4.550028205519582, 4.77315841822639, 3.3044578155467597, 2.665158192385852, 2.983769620804284, -.287174670399149, -2.1498508267360052, 2.767115007426175, 3.592873189078049, -3.1031066550062807, 1.0014335344235663, -4.127591287349932, -1.011251962307611, -1.85395448830079, 3.7829341694390535, 4.377874628709837 }, { -3.744795207934596, 5.282369202935514, 3.1679720789271317, 1.1739230808604828, 1.137478029213406, .05307028774657117, -2.310188546003933, 3.2732568398996156, 5.31174668978341, -3.0662131474183063, 1.336653159492401, -4.391226064340822, -1.7957993648957562, -.7601075491731538, 4.427675685476113, 5.4885631395821335 }, { -2.707556217265628, 3.0200588081430104, 1.9165235420536362, 3.265355735131549, .4961587641566586, -.2171170009292098, -4.32060909385108, 2.627078061734072, 2.619700842612715, -3.764212217740103, .671342445277438, -4.168339769481697, -1.4892693212680337, .25530203635357224, 5.500373030372331, 5.657287582560154 }, { -2.6000378807407576, 2.923477475476314, 2.537152483146709, .5940888196301346, 1.9762651144706485, 1.3074559094041962, -3.3828641612002164, 2.773583691792709, 1.9382869139421788, -3.9448598338490632, 1.7204626826688618, -2.6962447069603623, -4.160958853713246, -.8188760056101767, 4.737275146535483, 6.325022636725851 }, { -2.0035812418738463, 4.061545580876595, 1.24187198602353, 2.3035096037492924, 2.7271900580407387, .1906657945313469, -3.133146419073947, 3.2149290675440882, 3.3041801311601837, -4.460586224090846, -.9248773091961953, -3.8075735486588864, -1.7142108092387103, -1.3840030714971296, 3.2951906234992454, 5.968626093735517 }, { -2.605711280642799, 2.255805774097395, 3.9827934075235127, .9985418641041338, 2.0947333990661905, -.06359530529235076, -1.72945279585026, 1.0669061404946787, 2.783711108798522, -3.9000057363443714, -.5476044041041097, -3.3634534889834047, -1.4180277020336227, -.8952850691252177, 5.41290631558004, 3.70117933473383 }, { -4.273737392853799, 1.4133110401431614, 3.8831008182281255, 3.215332516440183, 2.3674105913679755, -.23152248484025695, -2.691829025540718, .36080514596642405, 3.6018467127444143, -4.117918955746494, .6646862153873573, -2.104309126076092, -3.6973753560617513, -.957188528089479, 4.198275423081217, 3.697092111897682 }, { -3.3701231999680923, 2.150566446104076, 2.07083170046821, 2.611144623681597, .18158113073697957, .639595057264907, -2.4763377420956547, -.7982777147194033, 3.057267098044598, -4.262067649723284, .5174549703354948, -2.7539477452246293, -2.830686194917947, -.27476538031904274, 5.507231111908217, 5.5731097849337 }, { -6.8174533603387895, .462024594561984, 1.0505715390844397, 1.9702980708217783, 2.399128458818789, -.5482478779599512, -1.5272270403573516, 1.2321830787407062, 3.0162632348491565, -5.147572544268351, 1.542978746438857, -3.3967947172928588, -2.7305265946095107, -.2428427990835266, 4.95707812569145, 2.9849468873129825 }, { -4.2700602529066325, 1.479940486563437, 3.8177736011628305, 1.4805200855244807, 3.415020413646218, 2.0050604868795245, .7544438475029526, 2.101893254629703, .362347412995518, -3.2370558712421778, -.2555586668270365, -4.666037950588559, -2.897121427888329, .0044797759057011825, 5.139836096387436, 3.395194760290149 }, { -2.806401941585445, .2824568711420129, 2.1559206753963065, 2.7476789667795365, 1.563743412170566, -.6882845619461219, .15767905671451893, -.7262963028793179, .3463344573108373, -6.681498880797936, 1.1158986165328084, -5.679186223700907, -1.9304135117333792, -1.2036193923031993, 5.558965505431523, 3.3410411275324003 }, { -2.8274745316406475, 5.407975812009834, 3.3391371537578554, 3.6453245996978465, 1.4727770462281, -.3988671605364078, -1.0519873451867423, .6462912651599914, 1.2358490221266172, -5.704823086737312, .8534601351728678, -3.9350121583033513, -1.0525236412774144, .042591067383880105, 4.055762042046006, 2.8852861249494857 }, { -4.275434893719886, 3.759713304667222, 1.9021634138618886, 2.582132905938972, 1.7334760780158223, .7911470222251722, -.40203686658294696, .32128175820913923, 2.588372951736543, -4.223268784260351, -.2732755707466957, -3.658675152382067, -2.2800527119045877, -2.1370914436432145, 4.594033291969514, 4.181048343165773 }, { 2.73422779607768, -2.527030327822718, -4.338703919912414, -2.427914204910719, -2.0679714851533966, -1.566963099293167, .8545931302284492, -1.1106357388396326, -.8650713410779146, 2.8370271798998763, -.250640599560744, 6.679739813816027, 3.0327000634410215, .5598984991614468, -5.047022394501305, -1.0647157221415164 }, { 3.569893277076516, -2.81504707832725, -4.454237793550087, -2.9740978378751484, -.424073528078633, .4296536775560361, .6651637222625328, -.8578575541338458, -2.704224950990514, 3.6694537988262583, -.27289265846086896, 4.497747689156849, 1.5766034788811094, .4958920249134446, -5.266685012009393, -1.898379296248041 }, { 1.706291694236684, -1.8639905544833468, -3.519291960372016, -1.3151232742189316, -.808467539607913, -.5748408380315744, 2.9177238253660236, -2.2690862732819013, -2.1335407967733135, 3.1708915203993198, -.7495552460537712, 4.790105290115917, .5663406415236314, .3496262921103063, -7.314880413928923, -2.3703618335174363 }, { .8459722041254842, .4150486803722794, -3.753491007102521, -1.3138078721295774, -3.249965084413264, .8640663187454792, 2.330741440180474, -.6605534920960068, -1.2307031549608078, 2.0931960773863403, -.3173856202704475, 4.3100090432191855, 2.898858968565356, .3505436105174014, -7.952695774845307, -2.359061493060463 }, { 2.3476147623037766, .7375115609427909, -5.7532946364578645, -.48998523274083805, -.5739342908460137, .3045835758766472, .43867908914810133, -1.8749212258680286, -1.7992668674999521, 2.4673282785642314, .8847467615148981, 4.896956168846663, 2.166901880637154, -1.1504472695814634, -7.46275310550526, -2.7019850418324385 }, { 2.772330416938479, -1.5069519361315824, -3.573647359436847, -1.6551695484935898, -1.880212841528941, .26524753895267444, .9598640645039697, -2.264964752404178, -1.0006847512637225, 2.810941139045337, -.5103083779958567, 3.072333752749105, 1.797989611359591, -.15898696192498246, -6.1166425834968665, -4.204296472338696 }, { .752629872756772, -1.5238908735884673, -4.6093008398316435, -3.3344286276395545, -2.283557956123665, -.2241769301015671, 2.7249943340244553, -.1882172007440147, -.7214991941606065, 4.269205798281317, .5536586733458988, 4.857607769034922, 1.7481701332203483, .9513122358806182, -5.206841596108789, -3.2088393876288666 }, { 2.074139841845288, -1.35389822287486, -2.6021955963190937, -2.3744897894102146, -.22424404676623877, 1.0808996186136186, .6397228094739481, -1.520119024577819, -2.683934685648877, 5.393559743600626, -.15499648761261234, 3.126543653328494, 3.6517328354525054, .8009161078093544, -4.196468822103935, -4.09512419973727 }, { 5.48133351216923, -1.77578050892025, -6.652225659721944, -3.2377317947178863, -1.5362081409240844, 1.171815213163787, 1.86637377351715, -1.5137935448557212, -3.9222979684473924, 3.0643818272813794, .39802734471453877, 6.470505842426171, .4077022585018561, .1730745474258825, -3.195051460467206, -1.8773176748697993 }, { 3.464408748045937, -2.5929287288772658, -4.652030044416874, -2.7529171567831936, -1.3956825821306653, -.42747009458180474, 2.524893750903425, -1.501846019812939, -3.551181303079885, 5.674494479501973, -.0033402715269551107, 4.15582995487338, 2.25657901602189, .4134521248537591, -5.392168854324505, -.08727188448812721 }, { 2.905907006715654, -2.235788976112306, -4.224941652306615, -3.382731967072618, -1.8565620719433047, .8799534519616937, 3.1930596837342238, -1.2534472635277436, -1.7454392945140855, 5.877204351810801, .9598142786193338, 4.932488844391028, 2.4453226754926125, 2.616074367472203, -4.850435419902895, -1.8261071396579156 }, { 2.370332702279584, -4.062977129789129, -5.484322498092336, -4.453708672377291, .3980724412306895, .7065674855276255, 3.961233190009129, -1.3145673416041432, -3.0619824977318544, 2.957587550025261, 1.0778767631567514, 6.778650424564777, -.12429340375780978, .5448984109630135, -4.215665867381151, -3.3531220161881685 }, { 1.7643590659215171, -3.1383423900603913, -4.032143712254531, -3.0499409031005764, -.4006081956009864, -.9466929211490599, 1.591296747471218, -3.694712676744832, -2.9920241651852644, 2.7379545636241485, -1.3402449732436008, 4.693660587677912, 4.274295014781786, -1.0261941160680457, -5.954438030951853, -3.3461921271640382 }, { 4.964661165314576, -3.1328489999295175, -1.023214674107244, -2.4527169511801947, -.9162319099090348, -.8130530643102549, 3.1469157674767487, -.4617869113877616, -2.6134725316113854, .43171846526415075, 1.0012802888052248, 8.508296877159331, 3.0725529088357186, 1.0639325454682247, -5.763856348694114, -2.4213368057985796 }, { 2.325112347557176, -4.385935495935142, -3.3891492696701877, -2.4824876630700308, -1.1127569253687333, -1.2024182901792457, 1.5698271287653771, -1.4521275154257698, -1.206598781843904, 4.419197667106836, .5030813158548435, 4.5107453045846375, 1.7992338269751178, 2.258289370131838, -4.022917498384026, -4.250774872755504 }, { 2.453882889490855, -1.4144586538773876, -2.3324625543808097, -2.2132489532395807, -.6330512814436215, -.5672712476850816, 3.275266971157446, -1.488331008758725, -1.6417630873290863, 5.828734270041199, -.4109757794165173, 6.107708722262135, 1.5992250441950226, 2.1705083966905354, -3.284482987548416, -4.620878009146187 }, { 3.0123784659075996, -2.3553708257120762, -5.604231842402751, -1.3415015850367058, -2.8567630516599594, .6186264794038884, 2.625418291650492, -.4580512287641394, -2.7112594291343206, 4.772988776634705, -.09521326253991033, 4.319711130797308, .32429064596419577, 1.1984728794323385, -5.147365234174565, -2.06386263382041 }, { 3.853697392252976, -1.678196418192061, -3.967923615900572, -1.9336638142427311, -.489483983144834, -1.0033392049739638, 2.74553857796256, -1.2702217054593445, -2.9680035169233734, 3.100676288153662, -.9115582380192699, 5.381377213028566, 1.1667682870057061, .1670151780560569, -5.416383810179352, -3.9033168091888606 }, { 2.987361023339173, -2.388728502253868, -5.684533113156231, -4.779082567191403, -.7883571415942283, -.5216470718775689, 1.357986945154547, .4108865965621594, -2.405187055180543, 4.5712838531002635, -.06502229884328246, 3.986077734157498, 1.755421204482975, 1.6071421406799553, -4.940234109570359, -2.705611806488814 }, { 3.276009029983288, -3.325160145231846, -2.904036183356204, -2.7637056278025165, -1.6042410400779907, -.053072773316249315, 1.93832839601736, -.2120585954519899, -3.4276126098625057, 4.32623599636601, -.4254522360454819, 4.208993672650432, 2.2677824732770184, 1.984270504560505, -5.565965957707423, -4.285993082636045 }, { 2.526024213017658, -2.3980783504882823, -1.2388304737246423, -1.533210540706807, -.17461966420864186, -1.469137630570679, 3.9288045385842683, -2.1942939470456517, -3.8003765797939253, 2.6712828773932076, .3780772283617324, 4.921314311549287, 3.053118572288695, 1.576619647580053, -6.0388811062649825, -5.265923926956674 }, { .19719254130725988, -2.9308376093206467, -2.0867233424882654, -3.042129082205768, -2.471693931501727, -.19429467450399027, 3.045867146958787, -1.615514452131394, -2.6597561853932645, 1.745169750774308, .26089111470934456, 7.520549985794133, 1.591659669956604, 1.3390762458800198, -5.950079146974297, -2.7457120258765744 }, { 1.6466451836610725, -2.209597947848433, -2.7501253884078336, -2.3287082591157326, -2.8623100288217604, .4269668629025905, 1.64824882384057, -.8235595026477167, -2.845752656727768, 4.077403619839776, .20277026409802995, 4.88624408391809, .760884715719895, -.8909638017689376, -4.895700871097599, -5.596319413260569 }, { 2.6360533793611847, -.3422362041556622, -1.5722385072836584, -2.3600574736269073, -1.137964106567861, -1.1887799190699053, 3.042256702720102, .7876560014395562, -.18530390934765717, 5.413802480907082, -.6571998026604695, 4.67854733258466, 2.515258498043556, 1.9568374890368847, -4.113437688351331, -5.855254756356339 }, { 3.9108038080510568, -1.8929957247832323, -4.880748741604205, -2.491422003797897, -2.2819642750384084, .5887766291426181, -1.2820945927343905, .045525375985256224, -3.5114115660142446, 4.447642636824457, -.9889434016456478, 3.6122403071045324, 1.9864722100241303, .1306960157313068, -5.233110222023579, -2.8373993042754266 }, { 2.322626984748464, -1.072847526054549, -4.550221738316501, -1.3829499122417013, -.886634539654226, -1.2621010320980217, 2.662659682813763, .9612476152918105, -2.5243902667183082, 4.923127525244919, .1926069918779992, 3.062430146157985, 3.2687773129279982, .09046837041087012, -5.726136101306303, -2.684931488831378 }, { 3.145529984061869, -1.971115429074689, -5.4140323675660715, -2.217007808612897, -.6069179533880358, .8472539112451005, -.050867878744563, -1.5907511951378643, -2.474426143689789, 3.762729490832525, -.34084850209494927, 1.6281079394052589, 2.6852399780687044, .8677788988481954, -6.018995684130896, -2.03839794158758 }, { 2.970179540953599, -4.1359803613465544, -2.2829614102878564, -1.813165879895788, -2.356132174514692, -.6469653784563886, 1.9609587326584528, -1.270264672244288, -2.772968026549302, 5.170824901112395, -1.1834643216818759, 1.7024850450660745, 3.676623357522751, 1.1024767716259918, -5.246357157748025, -4.695584312743265 }, { 1.6419847128365757, -3.20807084117974, -2.9787449924356535, -1.0359282587347112, -.4860366406121374, -.4793783934189846, 3.0855640210872686, -2.7086515294410374, -1.5350687785381356, 3.735881196454847, -.8626186076236462, 4.612878596228392, 2.7107349226492747, -.675346569614239, -5.302008560995513, -3.7688543550940263 }, { 4.598317528042318, -3.5443663290407468, -2.4975888834746076, -1.8962614527414179, -2.0100476231411766, -1.5844139051697124, 2.744673505140847, -.09861632270222141, -2.9507187501221446, 2.253256949937292, .884007342581268, 2.525908210053049, 1.1140150605252879, .2807243991543199, -5.679589382095935, -4.388282141276147 }, { 1.51266016618767, -.38478570598399525, -2.4656113828735426, -2.1371747685977676, -.8403740857962592, -.4160172501332775, 1.5293370954383214, -1.2022356356473543, -1.7050232359322277, 5.474700608589065, -.6457651366927389, 5.526072354953281, 1.7196873733237699, .18343581704377288, -5.170661317404332, -3.741389228749012 }, { 2.156078873983596, -3.2139748691436347, -2.2117272685692564, -1.2473810050652947, -2.4454844206286617, -1.1295302197283883, 1.8254762418038148, -.23711897156233042, -1.3831986064485808, 5.249048696248451, -.6244873872112582, 5.264176187973405, 1.577836866018411, 1.0112427859960922, -4.61814837745065, -5.366804864346626 }, { 2.6771981251490056, -2.1231805719839034, -6.081284607143013, .08059650008166955, -.9666148171265786, -.21112981493110713, -1.497042601496049, .31571862862582617, -.9764692618291745, 3.7685203319352114, -.546276017135272, 2.4943185400729493, 2.3504946761371928, 1.7892802938479289, -5.404625997936818, -2.47562655335717 }, { 4.2192079069083395, -2.0154244466188955, -4.215885844625004, -.657953804332427, -.9594395681077794, .7049172972156159, -1.2097732116123725, -.6041029219882847, -.8509413451757047, 4.1672925531072424, -.39263981869095266, 2.064865163318465, 1.0340002169026954, .4106558379372524, -6.799726593659433, -2.140838469343092 }, { 3.521165273614679, -1.9808430407929132, -3.9667407093570124, .44558182356506526, -2.6448996237456828, -.6624168130277486, -1.1359351588560502, -1.1559591534826645, -1.6542158839229535, 4.364005972576334, -.7927142495251068, .9268853186254207, 2.123084344271009, .33468918869827546, -5.837520796510101, -3.261677239397284 }, { 2.9995633079321258, -1.849314456650286, -3.7992525379532416, .44321008811527984, -2.524276240892813, -.6754604247559814, .8491915216480335, -2.138736691038725, .9666643409432113, 5.299717284116354, -.08026451906762096, 1.8327357673165476, 3.6736330956434817, -.7252159822632571, -4.782598022952848, -4.428459045711155 }, { 2.547078966996709, -1.8454340773306515, -3.3882086855217484, -.6509072750978315, -1.4769966228150475, -.7151130273991307, 1.2082952458688319, -1.6607663691087287, -1.0817129186272227, 3.3979183894440492, .472207464952084, 3.2193446313479783, 1.792951052213768, 1.881487925802983, -5.6895933656165205, -4.790265455497749 }, { 4.65856542494156, -.24148676531179797, -1.1407470328193268, -1.2774815509460378, -2.93681454893462, -.6190345968516974, .8598024545486421, -.9511384188744694, -1.322835245786732, 4.585533462129296, -1.4683188030845233, 3.5163647520240033, 3.166756779711169, .7660586026848648, -4.2574402973275545, -4.39925354032561 }, { 1.484705145853596, -2.615565457777709, -3.1966166322301066, .3226672861318083, -1.2091886935950824, -.6910867012089931, 1.384580696268031, .2860484197818764, -2.825972738020801, 1.9918583799090124, -.7105426502693072, 2.3523216916904466, 1.7506609546633287, 1.6913825942159284, -6.011672930226928, -6.174346185597727 }, { 2.2973001879406763, -1.0923939815207402, -1.250259656863044, -.9954699282329768, -1.0148082334852833, -.5066335341591423, -.07691710331446296, -1.9459972264982155, .4724926608653339, 3.7197872865525734, .5718636972623856, 4.090342711076729, 3.1757659179539393, .25843497644676544, -6.053247104644116, -4.60879018681021 }, { 3.6237366720811113, -.4398113505312984, -4.727259463897038, 1.6888873422089214, -.9178357691880492, -.7397858412538492, .4511464121753171, -1.0788943078052355, -.22561706360080092, 1.6569331011804758, -.24982533651774907, 2.0510803643748345, 2.639571546647215, -1.3417277194789456, -6.774346049586981, -2.6535097794897577 }, { 4.066979561901155, -1.0236828290707505, -3.40789117924213, -.2564065443639093, -1.9530451592700293, .5179393603111447, -.617910504457731, -1.3346556580195288, -.5535190645744167, 3.713970597250788, -.38274771173701816, 1.2654679933694701, 2.521253712215011, -.7749348506899097, -6.396355468239384, -3.0949519370142466 }, { 2.385109847063047, -1.0303901164228737, -3.0803759460936915, .4178155109377389, -2.071593476563569, 1.3340387692089126, 1.2395195453411085, -1.7764958269658548, -.10674442242328705, 4.145981403100476, .1309503033652655, 1.7564393661920177, 3.1184470427816793, -.26937363778258266, -6.384199253310451, -2.4453255037433506 }, { 4.2372793553612755, -1.3330835581411544, -3.2777123236893, -.6245628784850947, -3.132811385261137, .15234843474080909, .7812557979926226, -2.190267808910918, -1.2537829711735824, 2.5282911438618805, -.5807945450297293, 1.4886309408385199, 1.2212443933932726, .9954093562721632, -6.9454415360291515, -1.8896481729908468 }, { 2.57960422397028, -1.992865107884613, -3.3435333310131656, .3734931321796485, -1.3657079657630384, 1.7084112519876116, .685511719969532, -3.024371773172727, -.21264842409025408, 3.0027725172667545, .1166343944961696, 3.071205957464772, 3.7225332239481737, .24619750957943293, -5.773278477333636, -3.261454445081557 }, { .8474149178419201, -.23577844392572586, -3.6689005078120545, .833373371460659, -3.8099926214735786, -1.3900309728056617, .5232228804057989, -.25814303643881226, 1.2144196769572642, 2.7675288211542926, -.2316361207124986, 4.200604942488937, 2.0675116561433016, 2.540703709747703, -5.727754115801341, -3.4089494531811306 }, { 2.6045760748920532, -2.7168318657509944, -1.8680654663905158, -1.1227371021601926, -.290256776569107, -.6505850336597082, .9504655116826484, -2.082536960446595, -1.6218908569421597, 1.4780756082850242, 1.0762833371878773, 4.847200076907399, 3.3569312127984863, -.7005342210058352, -4.813046389069315, -4.490540525071217 }, { .2463598890893679, -1.204895687465379, .02359569677639462, -1.2596476626059243, -1.8337374765169832, -2.2739695891460676, -.4329972787377069, -1.4953332913168464, -1.2569503990081081, 3.65994068548486, -.1504294881753459, 5.72254468666163, 3.5703534902555893, .3113240990348663, -5.282702844548525, -3.909834656769119 }, { 3.085504516950954, .8262504231220409, -6.348815904828907, 1.1143931983636186, -.12840177931852992, -1.218082367120973, .7504006362711768, -1.6308642097555552, .12769037037822595, 2.7094748890254947, .8402422072031981, 1.0158353859943903, 2.340606213281406, 1.0147183395917958, -6.524977806548444, -1.6329916005084366 }, { 3.674980910181134, .041874577252458, -4.516492820251928, 2.100866166044805, -.7291236051901735, -.9456786266111114, .6733841362648909, -1.9579338227826784, -.17987658708499987, 3.167069802599167, .6546645058150219, 1.8731094637591554, 3.979335698955448, 1.397207605219725, -6.042859374888802, -1.5421371856628245 }, { 2.5307300675190674, -1.2916235102669855, -5.1005716382461985, -.9767059189218399, -1.6405652242940516, .04337250039187526, .3122277749202544, -1.6860968302134463, 1.6600259230434573, 4.607956782217195, .7116977760269633, .9120263819094414, 1.955421816913072, .4003059456153626, -5.945447342047505, -1.1835042209156732 }, { 2.0549299417560696, -.4514772913799701, -2.7553240175149756, -1.7888001170929007, -3.5416403021334775, 1.0109739613797013, -.005247803201770194, -3.7491267288594523, .8234477315247287, 4.5158893444273955, 1.2357458788697049, 1.3153755040260646, 3.577430623461358, 2.5212954032807047, -5.9124820972871115, -2.39269970315278 }, { 1.5916203601653722, -1.4784319975246232, -5.3039341228892845, .018164140767906897, -.39387931646686697, -.6059199708295526, .16538807898355923, -2.4666861499000947, .46319272713615806, 3.192160692578135, -.9267758920784676, 4.636305981984046, 2.580092877910391, -1.2711420916426288, -5.073691327800913, -1.7274797776839423 }, { 2.4971467094953845, -1.8214462258537076, -2.3281983048598685, .8171327467803345, -2.116871544160211, -.7414666979057931, 2.0941201871608413, -1.0928117475712598, -.6019555949488321, 2.7013239441624757, .5098330095953453, 3.979140987723794, .9550398382746218, 1.589532983985472, -6.254641860192009, -3.305122703206002 }, { 1.3363947702616428, -2.0974034887012447, -1.4857649936377104, -1.5672587075245343, -2.793255918624278, -.24819342416728732, -.11795857070133867, -2.6258989493065212, -2.49351412269835, 1.7074770848447132, .693910911732942, 5.382289795121008, 2.6296784696575153, 1.199603741445648, -4.469769903582875, -3.3871488297750596 }, { .7071077808321817, .5886914042827149, -2.5493610745476496, -1.9706299219821088, -.7824795097045149, -1.5043012581948072, -1.0050770329234475, -.5585274316979157, -1.4099433715567555, 3.3484872231197076, -.4064599810945806, 5.876385966841507, .16780257942698437, 1.2760119574952786, -4.132837156491091, -4.341131006991997 }, { 2.7593042728025745, -.3484626231032666, -6.737795625844895, -.47432705141004267, -1.5639443269872249, -1.3441578511952084, .3274876837728622, -.1821861412366392, -.2622997063887957, 2.628171561063993, .8373190108901313, 2.1828669991888576, 1.838593441459417, .1441636628002849, -6.757833015762414, -1.9683262792092566 }, { 3.3612612146594456, -.5823299042185693, -5.980065880596557, -1.026893139526965, -1.1939678823369944, -1.2107519067372408, .4450296116523693, -.19104929298523352, -1.5036804737932574, 1.4484030887760262, 1.2969749144758003, 2.1026788452287843, 2.2596940216255232, -.5159335372692363, -5.612676805385866, -2.0174522225107125 }, { 2.8685742792062268, -2.1661204318358442, -5.17333685860178, -.39213145002024846, -1.1725640031247369, -.3203105561734047, .4483616301258969, -.554474557457285, .3789815718400948, 3.149401912747615, .05146782529261083, 1.1947246928025113, 3.050605821755862, 1.6090625602960216, -5.928363693168651, -1.605860480687075 }, { 2.199242125408623, -1.3473079366382517, -5.023246663397071, -.8514498910220339, -2.9897187437858395, 1.4118751472661095, .39992569688855684, -2.1344602684751703, 1.4552760466451224, 2.62077503007014, .0746930049531677, 2.058319482877578, 3.973687905306126, 2.697055604031971, -6.0174237325032465, -2.1448799617804832 }, { 1.595964362228074, -1.0518953425108988, -5.632436828963771, -1.1867966053768289, -.5275060878863601, -.06522840814940156, 1.8860228586239163, -3.320653709247774, -.7288564266635957, 1.799333256374494, -.21100411551909964, 3.6842346248581124, 2.980666415574808, -.13482695384020182, -6.380484715214365, -2.262867380686098 }, { 1.4900726936066375, -1.1914056578104368, -4.285218145654854, -.1496045662821141, -3.234381765838593, -2.8928669873062023, .6497971745824255, -2.4998676310867998, -.4378946317684195, .4898141349260041, .7246985083623997, 4.066836515583351, 2.0296320961476013, 1.636491381979808, -6.188026140023605, -2.673013793066508 }, { -1.7520036832773562, -1.620620077570971, -2.6923264031422005, -1.0480815672682844, .11478233437125428, 1.5415658138913721, -.6071678570747145, -1.7954096654830278, -.3338715974254862, 2.5065575919955267, -1.0548932175038348, 7.330240254935943, 1.0877343996072295, 2.6537185908330456, -4.062563927822357, -4.850601872924341 }, { -3.6694750450149836, .07714196063416495, -3.569639952041663, -1.8599049313666791, -1.302964501710844, -.9816291059949951, 2.525526117281998, -.03832444039493157, 1.4000674936280084, 2.4057200271842936, -.11657195320638303, 4.784297450464171, -1.5175792683072182, 1.6753969770868766, -5.411955781639187, -6.74669281676282 }, { -5.4736472573213435, 3.883921821890313, 4.193888616388582, .2825044996405288, .25236622834124917, 1.292960186265647, -2.6459405449699585, 3.717681995550745, 3.293626719971504, -5.9079949969586565, .7011604210048797, -6.112339205454047, -1.8587947553367588, 1.1514589251703804, 8.525112857384556, 3.751362764340136 }, { -4.553514262527354, 3.1684214707956313, 4.791933785363649, 1.0766481457752985, 1.4153284887282573, 1.118545298197987, -.8289810333180403, 3.982430175301064, 2.6604170333859667, -6.439185476423446, -.6608125766441317, -5.156772649674641, -.9299933738138586, .0269576291225359, 8.855944754531533, 1.8751387373707988 }, { -5.5639544694922884, 3.5122122913138787, 5.635621240256831, 1.3295036668843891, 2.2237131918971356, 2.1888359982530363, -.6776861025978084, 5.516924208010144, 1.7211472220153907, -7.993820103606396, .5127623245452465, -3.492811123178016, -.5895463044967739, -.5878305136753931, 7.915037705948133, 1.9499190829918656 }, { -7.616994110558376, 3.2021919792023135, 4.180469940835656, 1.7072112259459258, 2.4158538553468984, 1.0900827197808707, -.9119691873112259, 3.963235610713495, 1.416352478239672, -5.756307662584217, -1.2915655155941146, -5.330175893934959, -2.6788641855012516, -1.029402327388066, 8.459674432816014, 3.2035630027858435 }, { -4.505858577871454, 4.76128487055827, 4.387871145353463, .8865922007657997, 4.010377970873067, .7638353666841817, -2.3514896557147087, 4.771468462125573, 2.18930738793449, -7.418331508783834, .9265832557834847, -4.932327043157348, -2.438548577760991, .7062305665932501, 8.235911772119147, 2.0128320134773574 }, { -5.936582400752787, 1.8464268326195767, 4.758040517296615, 1.0908638145866132, 4.031495381784838, .2810201212961123, -1.1800541435768361, 1.6855733771697912, 2.2947157159105904, -4.618894127749544, -.2768303470566051, -5.9149478699914075, -.0829083571400665, -.8373167799136609, 9.987801624994226, 4.440628826259131 }, { -4.783362166834156, 1.5429486078670707, 3.7541447765837237, 3.227471786136086, 1.2564975638876597, 1.4782354672862652, -.48523754622977183, 1.0214669494596218, 1.9110370284294802, -4.763480771756208, -.44090472121589747, -7.775740882660748, -2.726387458415344, -1.0168262962604209, 4.411487529088, 5.587664562109796 }, { -3.9226587494639924, 3.9347513540609067, 3.204562288061745, .9077169987888705, 2.766980497265047, .2517817610739927, -1.8218492905312869, 2.0053211514683964, 2.036533151416169, -5.171127336900448, -.25267196827053573, -4.7302239734898, -3.439235171832343, -1.608424491386567, 7.525821440831678, 5.95666976410146 }, { -4.300257892390196, 2.8547079594332736, 6.09699571277446, .5596326341251617, 1.9401843807584394, .5319724925743905, -2.169090849083181, 4.473907804059538, 1.480432249064417, -5.202757281883254, .5859812623503384, -5.102071439373446, -1.1194831327481245, -.4468007634025177, 8.600791547159229, 2.791426901409411 }, { -5.686011572178837, 2.353644006070226, 4.87305166203696, 1.1046543271563452, 2.424861799209759, .16550474017600644, .046831138138119885, 4.546149737576977, 3.5370754270359077, -6.336893552059156, .05529426619907646, -6.6599582149926, -1.2312420909925597, -.21947420959916922, 8.659870888580912, 2.94218057751926 }, { -6.380972716273403, 3.3662019846112994, 4.691089823098366, 1.2817507243753743, 3.545796043031039, 1.165543075308641, -2.0020230128419607, 4.2284665395498955, 2.1255217509933626, -6.79586110881056, 1.508805149841647, -5.5893512253025746, -3.0399197097559023, .8362580856897268, 8.812444682949792, 4.014425527113462 }, { -6.874536713262507, 5.256692018045252, 4.343691998865303, 1.140922546269457, 2.2641086565904196, 2.6590522804184893, -.18446800651172848, 5.304415749534903, 2.008555628334519, -7.88566628698484, -.22869545924878468, -6.698632216757334, -1.7460768889171956, .8795856193080352, 8.051510364142004, 4.786963089643332 }, { -6.412762681411814, 2.804406498873134, 4.1497592462729544, 1.6310120302154207, 2.801876765565608, 1.9645472827601678, -1.5167366268374518, 3.9368090874792645, 2.0088216431929515, -6.956333267534401, -1.0443729780622055, -5.302969561191924, -4.325050366082538, -.28308498823047346, 8.374076959113479, 5.633508495292931 }, { -4.382608514697826, 5.412361643594855, 4.839685602829709, .2714979304585059, 1.8648308308005912, 1.6356840504347157, -4.180732937930732, 5.6117534546686905, .8418404017881805, -6.126254128644989, .4035862668687796, -4.489191730690453, -2.2195972614291857, -2.102219185624473, 9.822029136443378, 4.867888971187729 }, { -5.2463946658687215, 4.115843945114631, .785222810764481, .46600878785346284, .22241262754419894, 1.8497062092061372, -1.0184253137159236, 3.958463446805195, .5338913400896272, -3.9846339551089045, -.32642546867689, -5.655634140114817, -5.126167486927235, -1.208427650947636, 8.69414080105875, 7.497002262593388 }, { -3.915273310109253, 3.3647162804023822, 1.0948516246644915, 2.8338590436981286, 3.600268721164921, 2.1044016515993307, -2.3428451738766762, -.44701341187120675, -.5752910366774817, -4.669689244492093, -.8049875102840299, -4.6774973663709165, -3.008554785500086, -.10808729437247777, 3.5062249292196204, 8.033290012696455 }, { -3.746130523980956, 4.220277491453817, 4.919739015857724, 2.091175670177511, 2.7277025410156766, -.8508852783562987, -.38420681075455604, 3.1860830812665264, 2.858798323226791, -6.49855773445846, .7212356011006346, -4.328511908722237, -3.749303024735442, -1.2222795959373705, 8.928492155926737, 1.3554666580311665 }, { -5.898339710927298, 2.667515781562732, 4.79821551902289, -.07722578008120377, 3.4382490297694335, 2.3836822866422387, -2.3976948861844636, 5.5015253916617395, 2.080817164169039, -6.22697674782506, -.05934865247610853, -4.028154220692722, -3.1123810973984467, -.8104012442856667, 8.913629089894258, 3.204671369450158 }, { -5.49086576771001, 4.079719764700953, 6.470117850834277, 1.135808613008374, 2.307548817758664, -.13852840920474674, .22373480622594194, 4.5741888493073315, 3.6019735435048568, -6.172466696621088, 1.399998954912978, -6.911453847392206, -2.1366545855896484, -.006380676042045331, 9.083557071232095, 4.346583741896616 }, { -5.96070435174902, 3.268681292655867, 4.970753108168736, .5199968674883495, 2.581254735899943, 1.3878812734049084, -.033693277983601215, 4.477477934054805, 1.429515703547598, -6.16022044786945, .2892096648583884, -5.424266372562701, -3.079427941352938, -1.4596316562874077, 9.122068594054388, 5.475887666453492 }, { -6.376568679266274, 4.086135972489511, 4.166218742236529, -.589111218540951, 3.833261788817547, .44304584180740847, .11508136229652881, 5.228642257838332, 1.5941289351149517, -7.368099539388152, .5666507187632962, -5.76644418042983, -2.130789218002172, -1.0549252119682049, 8.959936020493336, 4.7467951102852215 }, { -4.753700109469147, 3.380375688071858, 4.681612748555443, 1.089393726583805, 2.9963567482340325, 1.408340813904223, -1.053868288434247, 4.211787095217862, 1.8700973152824352, -7.535900852365488, -.7003880036067993, -5.186288941747293, -4.902563856182707, -1.0541898071633216, 8.341272359524138, 5.430022072627929 }, { -4.0774644052671185, 5.484679546660459, 3.049382540961112, -.6672081811343912, 3.2613054082206197, .31900019324981255, -.5026844252449043, 6.030339351836511, 1.8086128361798863, -5.3930074475927166, .9731482714558233, -5.696957246960059, -3.7216324565961805, -.039428374805430405, 8.947628093595103, 6.824147848020085 }, { -3.948796911134583, 3.885207935497808, -.6413912652680788, -.303861788068452, 1.2187352963202474, 2.466438931174178, -1.5184831073151956, 2.1988001742347505, 2.2429579552660472, -5.028202513430444, -.12016845101834733, -6.327784437055824, -2.901057482117719, -1.250708008921215, 9.773653329157872, 9.008061593252378 }, { -4.832775057599217, 3.6055231061887163, 6.550053114941706, .6002662903559624, 2.557253831550052, .6063482473995933, -.6758722233129386, 3.11214751249162, 3.4461578366573193, -8.42981465907739, -1.388399892558577, -4.941086262538634, -3.423801937792672, -.47841075369651687, 8.366388664997435, 2.9106494322727388 }, { -4.5261097373267205, 3.2975224158460645, 6.181625942810291, 2.2185611813966175, 1.153947532104816, -.327008932453037, -1.674134557422074, 1.2840382931599736, .4272028029164084, -7.767160886360582, 1.4702268476523437, -4.1752039753857595, -2.5953017385505217, -.2337777358835623, 9.987966117051105, 1.878566771483096 }, { -5.702005267557041, 1.9467842935485626, 4.837641194311172, .2905438931554371, 4.209642701602458, 1.7165154134103955, -.8056011261272092, 4.334632318417068, .09182023343943325, -7.613933059735718, -.6386567115135003, -4.59523324784814, -3.1873517783214766, -1.095042822730181, 9.538715673617693, 5.760493391337163 }, { -4.9998639052617095, 3.4975498425134157, 5.5569603643103616, 1.019965531538259, .6056943693442166, -.7868864189612146, -2.370608611936083, 3.9832960949074794, 3.7869056120064, -7.076214705657176, -.6307335445052198, -6.3759250205194595, -3.264179718847654, -.12329287283857762, 10.013762729055827, 5.115789432677232 }, { -5.40420242075279, 2.680770002641031, 6.382475137336244, .7640916322824375, 2.923706374399937, -.5334492983681837, -.8122360268443668, 3.8073176509284816, 1.028911175952436, -7.283101987033039, -.62924284123153, -7.035971250661363, -3.6242120956683532, -1.826629193926749, 7.96077500562818, 6.5887371836421975 }, { -5.164492141658247, 3.932223694287965, 5.464334194432215, .5124733593299062, 2.052165352216021, -.23293559916879883, .0061688092519478445, 5.905287472803444, 3.6663227048572256, -7.943828016133627, -.7084629550350565, -4.849580166703749, -1.5020158920683833, -1.0735451419193527, 9.99032506053086, 7.250279859362939 }, { -4.828734497235375, 3.485319437056726, 2.768775021270294, 1.0334471946393962, 2.1139743802251996, .5819427106401291, -1.0691098124344736, 4.182662174024404, .9276545211188285, -6.958100830603527, 1.3003754014413837, -6.3763282583109895, -2.303746427954596, -1.083348859641867, 8.707911220605899, 8.518144062474061 }, { -5.160445578414301, 6.512436220142502, 3.351564263909147, -.3480392443119282, 2.7104112482480986, -.8458868988098869, -1.813225256376331, 1.5855133631378966, 1.582954996549917, -9.285708361420184, .07809157353492385, -4.034566641220812, -2.497388246199425, -.299327092426438, 6.5291186486841015, 10.890346057350795 }, { -5.852693212581265, 2.664766221786257, 5.180628741796094, 1.5232822030410704, 5.836803820563132, 1.9226221421995995, -1.328364820169801, 3.2231455236435442, 1.6980908565698538, -6.880312259909495, -.0890612140579122, -5.655755295473693, -3.8842201006627777, -.7449957575768389, 7.225398938924927, 2.5919431031444797 }, { -4.4966757780414595, 2.4410304415149398, 5.604126502409177, 1.4005195826115648, 2.19762338122081, 1.4832726028145402, -1.953461295955676, 1.9972866617871363, 1.725160463320538, -6.168965777003793, .5698874747030644, -4.937121150706864, -3.7178939472945642, -.60204110284244, 10.7713683524328, 3.847872161449829 }, { -3.1890449923468136, 4.440039182151126, 6.922051715460546, .172957603306474, 3.0925727435242183, 1.2664462268317984, -.7158205872575749, 3.6131131437936386, 1.8798150987145377, -8.975160665302187, .8059346303884504, -4.4957023146898685, -.7885728527359354, -.6048980640949682, 10.863878592619162, 4.560845481260116 }, { -3.2512592690087243, 2.7056711110923635, 4.351517758413352, 2.3023298517885533, 3.311815276966888, 1.7734499788245022, -.69170613791497, 4.373764711467115, 2.8958895631184878, -5.145976169249961, .3117960153059538, -6.799742219790612, -4.084240342033484, -.8314344337047671, 11.481924579558475, 6.685236718168514 }, { -3.3894540412889986, 3.836713596582361, 4.244687598927531, 2.3580336965493722, 4.4665796534748345, 1.9299032320383667, -1.5535714211898646, 3.9364491051384136, 2.8540476171866644, -6.4490889029540845, .055588285899452076, -6.4656911529330925, -1.6322550919419374, -1.5370761969437592, 10.425519089387702, 7.913274619248207 }, { -6.735809656514078, 3.5413269093400324, 4.521138261800485, 1.451217515900132, 2.3922972445052406, 1.9914710221637257, -1.48375160944949, 3.6202983475858694, 1.616902412608586, -6.269288884536586, .4722614916559773, -7.428452329939749, -1.7948905503676926, -1.0451751139377563, 8.693706319696837, 9.358767534644238 }, { -4.557053972444375, 3.1021175434815187, 2.347583565937219, -.10279486326113489, 4.10963472494306, -.533510242306219, .03968471674367405, 3.2759571295468572, 6.223715584282843, -8.225189993839654, 1.6462472620892898, -6.672261585862664, -2.706524987267311, .11463970778821547, 8.3721630528889, 10.21600046193503 }, { -4.11314388681977, 4.200494739878019, 4.036596469220724, -.4011435943451185, 1.1123938310113635, 1.4474412512902088, -3.8348864277480907, 2.7501593098898165, 3.2289535188216547, -7.259481918238865, -.4797002133150635, -4.341914085699503, -2.196706064807636, -.4901817242961404, 9.284113472660664, 9.228351495361093 }, { -4.78058460546069, 3.4673341208527826, 5.2454886699041605, 3.7547171832157065, 3.1947497248830787, .9000451795079937, -2.2458518326310246, 1.8795810401480868, 2.095190846141369, -7.811545454615537, .34657611121426335, -4.632146477823391, -2.5076712059734287, .279486079061476, 9.527208149269118, 4.066648228027421 }, { -5.846514919060206, 4.662997312975826, 5.000117746416082, .7860187533608729, 4.099895063353016, .87263969005965, -1.6420283797635105, 1.1975743610753538, 1.7242444179782959, -7.266365888409553, 1.152993647102123, -4.7428190991384245, -2.1376578053733835, -1.9352231231785868, 8.069898301641462, 5.086401656187879 }, { -4.3557370476325925, 3.9179402496019518, 6.499669648593165, .5552625445613059, 4.351871943437111, .446483809870865, -1.6531899020815577, 2.497715548901085, 2.7038415657681227, -8.373426325647804, .7903448128010802, -5.481572883433233, -3.480985006383639, .8228256594395482, 9.609385755978897, 4.408124266991159 }, { -5.073480844201697, 6.956789322143535, 2.327332768686694, 1.1136041734710582, .549691795049758, .8584587203819559, -1.8897105996646528, 2.9706947460137947, .4531359473669138, -10.885181846169873, .5960961503325253, -5.1606416573987905, -2.043057642486651, -1.170497576798782, 11.04483562820474, 6.941124952172592 }, { -4.435292047265426, 4.780235423108303, 3.3380397376787867, .4509851093354872, 2.6677576051981347, .7096621241149662, -3.0092181107343676, 4.024657977269111, 1.2203424189515697, -9.601275850290852, -.07545101167433037, -5.755323472174193, -3.6484726992381447, -.3881461678023971, 10.024118538166439, 6.71906029782186 }, { -5.67158641273635, 3.832826471641919, 4.51446509660316, 1.269987182947568, 4.292255010671127, -.32752679780225086, -4.637604954681821, 5.793298711871566, 4.173003476348278, -8.244563995648244, -.3853853611302604, -7.255157766253083, -2.7812542266711833, -1.8179650572698378, 7.049635852036033, 8.040479539147292 }, { -7.6467214380941435, 4.895868407132634, 3.951678256360104, 1.2662642572597875, 5.8697608972293445, 1.3663029113956466, -1.7873446802794186, 4.306626687834507, .7628705463618716, -5.934287569750168, .4237574532498118, -7.110991058657168, -3.535137947521595, -2.267980793456155, 5.010348859494387, 9.370209585806226 }, { -5.075674184261173, 5.250041611428357, 2.4378723075702196, 2.669260992544517, 2.6750368786412646, 1.3515821646027546, -2.803679408291877, 2.0835137413298406, 1.9479065697716809, -6.830262820577115, .14235573616744357, -5.987851790040872, -3.208895278482874, -2.198261021691915, 7.758147626127194, 10.499933822743092 }, { -5.01080479047122, 3.5945863515188696, 5.8608311666311375, 1.7210654546832618, 1.5922919504661204, 2.331056526396683, -1.6440239668117145, 2.6614188752377483, 1.711096497304266, -7.7451546984721515, -.05711302183222874, -4.747535388105561, -.7663949425821589, -1.030530674307302, 9.129627640079956, 5.292888770185262 }, { -5.003471973123687, 3.1176805631271867, 6.777971221975362, 1.6865788725152613, 2.67153303585245, 1.1493662966574996, -2.0041159836294393, 1.2948924998226334, 2.217501024717066, -7.857664703064815, 1.1688026278435175, -4.362856467498643, -3.9674553776822212, -1.8206403304433902, 8.759250393837855, 4.6315378086387815 }, { -4.555740611234325, 2.6827590564426727, 4.889044141265985, 1.7413511441607032, 5.56510404381694, 2.7642292721257027, -2.860454706309858, 2.1142089360821203, 2.7701323046858475, -8.751404718037064, .31505527217234464, -5.015975005266604, -3.111839989917507, -1.9999091025009685, 9.458933239255192, 5.442743451950303 }, { -4.857063626490233, 2.7931057037360185, 3.598435002771137, .048326654124952816, 4.772258349815482, -.39431105363441166, -3.0247725851052834, 4.810952241037085, 3.764343568525501, -7.71905194024559, 1.5071869494094727, -6.1905641244251335, -2.8976602696342546, -.364514901609092, 9.9207356673355, 6.575129595348948 }, { -3.7541878103489315, 6.498015607937805, 2.331304393450335, -.15546266562671823, 7.147051942428876, 1.1627400572969675, -2.490847166025299, 2.3561044543571072, 2.4525080400947243, -9.754504319288884, .06340841043076662, -4.047112086861242, -4.554919736087199, -.9907881615080806, 11.509486452107577, 3.95160812059714 }, { -4.852392652359912, 5.018402197554664, 4.640051890314109, .560855641677679, 4.4582352477761535, 1.2627963828719608, -3.5108420168445784, 5.515580618235446, 2.4018066179116553, -7.957769937488327, .5384097739029223, -6.75118713608348, -4.800199727448572, -.680314944755915, 7.071424458204001, 8.283950862405483 }, { -5.97318841038166, 2.9259534090307437, 3.4696545776327032, 2.5821286537728874, 4.669089721573314, -.4683914549408285, -2.4844674465313084, 5.538086743874512, .30991653580292655, -9.030084236753362, .03559952584956998, -6.372733328459099, -3.319871229737538, -1.7579681668463136, 8.5780066070074, 4.7817360602194405 }, { -4.577532043177685, 6.818242592237886, .21851244563652017, 2.543441065176441, 3.6027020643138443, .514928922226913, -3.580526687144077, 3.512199616308927, .7012097981688777, -6.735397492030273, -.6544856607754697, -7.455530419765205, -3.3768073086613426, -2.5460237620231867, 8.358464198230225, 9.027922460884009 }, { -3.1557245486888306, .4990513090124268, 7.396714057887107, 1.5558615430467742, 2.916048066967399, 2.120846721682545, -3.8815630040858586, 1.7983738310063382, 2.177371673085741, -6.720617457042049, .4461753149195656, -5.3389673500539025, -3.361767695742114, -.3501295819556703, 8.652185608384851, 7.604072016366812 }, { -4.766994548308631, 5.004228167713553, 6.813031428904185, -.16770161720102095, 2.6743483670790122, -.0975263567643245, -2.7531618804805578, 3.2062811186836764, 1.7652970263330154, -6.39601696405406, -.25952184918356763, -5.94035566703823, -2.5141727697993472, -1.7044689545836298, 9.479149348054634, 4.835657251351184 }, { -6.152799590606697, 3.4671067077457582, 7.41449081554231, 2.1071288412416207, 3.5154946326691703, 1.9456295380028166, -1.8224371202046865, 3.386594698596272, 1.883559491220572, -7.405243620508657, .22637112383636826, -4.909194047487012, -3.8778735072983057, -.5770831605423092, 7.125984104677717, 7.841263013602063 }, { -6.20245836228061, 2.6488113756308276, 1.9026620028555274, .3449261792012521, 3.2745139406276538, .4991989570296093, -2.632107304418809, 4.225130785156863, 2.1253153303251944, -8.30119994771211, -.05901447256913937, -6.490572746954085, -3.0469073933271615, -.5356191229950034, 10.322494594865244, 7.190918951623112 }, { -5.2345453891918865, 2.526633986329556, 4.838533489041977, .21006299148998425, 2.5362775074216453, 1.4595746675065508, -4.3018188905019255, 3.3119538899971763, 2.626219206794568, -7.845238390236254, -.1983902003778184, -5.8651099878566, -3.0232443927717547, -1.8301631527697984, 11.35234420208198, 4.362104734213127 }, { -6.340146581792192, 2.7741322647614144, 5.432860279935703, 1.6153688304880267, 3.405242860591398, .5331348087916291, -2.2814951279066693, 3.701374071951346, 1.6019434434769226, -7.591413889257258, -.18325637713672238, -8.033182188504503, -3.8113032401472253, .8977381188767539, 10.434773931404857, 5.657425812744306 }, { -3.8626926741094865, 3.7020322709851303, 4.697430587258299, .5594623399222293, 4.430488520585857, 1.763710989847037, -2.761048186124643, 3.8630294507547003, 2.1882963979188736, -7.9070532242210465, .6121260525906019, -7.362836810080137, -2.8672353015972636, -1.2072478748415718, 8.824874602389675, 7.252035773881815 }, { -5.2122803422316775, 4.3400712246269055, 6.097682357445951, 2.71862585257499, 3.6390665238768203, 1.2708797616947758, -2.612252401057482, 2.2818427513142603, 2.2740684645344538, -9.227034598192102, -.8898924805128174, -6.850126030999842, -2.829245788582611, -1.201580205210217, 6.740905416775114, 6.979414856507642 }, { 7.631851524360706, -3.7134563990819287, -5.787909828365484, -3.505722574538238, -2.735120287063438, -.17741252563406607, 1.030264234582025, -2.9643252338713317, -3.2306907792994517, 4.457926517682234, .5145951014299679, 5.264302887720362, 2.959292393532364, .9782842355254635, -9.258271069163829, -4.145479852353891 }, { 7.457172983123254, -1.8225633252319025, -5.448206627998727, -2.41454837068468, -3.952585992461607, -.12932962654416014, 1.4841957232692928, -1.0766826268295009, -2.1973822716540847, 5.233605768507788, .6376542230414529, 5.4013389180658935, 3.8797765840970304, .8059170652475283, -9.629941054473989, -5.450341270765666 }, { 4.237655375826632, -2.528312237102898, -4.496035918815964, -3.2487836897377367, -1.8822097712321817, 1.0786229294090086, 1.9624872916462732, -1.3812143015606515, -1.2339591878143672, 6.58124349874708, .2621752973098195, 5.281837478587103, 3.970032388778345, 1.5165449388917915, -10.79645826086409, -5.483425985518324 }, { 6.509137645647728, -2.9625635740394953, -7.499434190619812, -2.000447624283958, -3.0907821786802265, -1.376482407193667, 2.398176376811313, .14015001113534822, -1.4432113917298721, 4.907116336877177, .044970723943211506, 7.867251315740809, 3.592418198152969, .006039976055533225, -9.184365839754177, -6.952267172538029 }, { 6.402000435437573, -5.127986479677088, -6.114163123099808, -4.302959619235731, -2.9221657692340144, -.7603721649601815, .9411040811128389, -.6167209352527868, -2.4807774374747407, 5.207026201097289, -.8021350033286458, 8.871918764583256, 2.386294355216194, 1.0914066062540972, -8.778198462608692, -6.710856355137902 }, { 5.870950860049774, -2.812754383654748, -7.484296926554425, -4.145701211383925, -3.6943471523481026, .022206985511455102, 2.1947743284251047, .3511658876333991, -4.353458098112906, 4.936305706409544, -.5307071171442489, 9.169755347397357, 2.565404671153326, .7998635767984371, -9.882649244083341, -7.46531613393208 }, { 5.693316763643552, -4.061199542078259, -6.123036285217933, -2.314819409928231, -3.886800665613147, -.4450273920941174, 1.113955179364308, -3.1978036248871136, -3.023643262883087, 4.949655842001286, -.3077214030580362, 7.383871845185986, 3.5092546449487454, 2.5409749111367446, -9.972367864319667, -7.10976098858106 }, { 2.540794101926118, -3.264840484568971, -7.383016219170885, -3.313459050867852, -3.1271652857131382, -.9584686678761462, .39287049621167197, -3.127865580786056, -2.80536620998407, 6.274055483909851, .5906203980920328, 8.571148496161314, 4.269571770900792, 1.4290583554223513, -9.199382967373701, -7.405715183529823 }, { 6.964901733081354, -1.3918798387564162, -3.7475435691431733, -4.100498123668707, -2.6672881533863166, -.8590112599886374, .4828728658761155, .25541719437456606, -2.36761898977214, 1.758265208419126, .7869805972043032, 5.495299329784432, 5.654987647626209, 1.9460696343803898, -10.732235308596186, -5.524995141006303 }, { 6.701153662693385, -.4054226856631336, -6.851634939989388, -3.89425901769882, -1.6199851213113285, 1.0387341725759216, 1.3378577345881024, -1.984328296244713, -1.462958268412205, 7.207588381009333, -.45764238343300306, 6.270925348529435, 3.8181460633097353, .7094877234044662, -8.685314877803998, -4.812620620850137 }, { 7.497325492152477, -2.0787361295912645, -6.92863133520976, -2.5164086695033174, -3.899591672721827, .8970276180808389, .2833144850751757, -.992375814043454, -.9770018572055957, 3.631544902016506, -.92006913660877, 7.809778115534375, 3.464353257622613, 2.2875066417434025, -9.62572570981622, -6.060557201347328 }, { 6.45054713179013, -3.0392966108193487, -3.799784518329963, -2.5381973107361944, -3.1238476778766215, -.909860174218865, .3648086520690427, -2.6644759457493374, -3.3928618766710406, 7.218514668545021, -.798982713420666, 7.352558519190297, 1.7226595122535984, .4466495701351635, -9.050007423113701, -8.070301840102676 }, { 5.633949697126335, -3.843376632112876, -5.44876910323804, -2.733327499288307, -3.7307473447411224, -.40723380216767785, 1.0231290966735045, -1.5728071869055062, -1.3345700806012255, 6.478995438553031, -.10364640938283161, 9.312930627385391, 3.1876006034467963, -.25642226324673895, -9.44301555722623, -5.492432050834862 }, { 3.442939065812423, -2.882360090524011, -5.288950173182862, -4.117925299908749, -2.91876708464121, -.15429565908710513, .5369534833798016, -.9036610861897463, -1.7106668555917686, 7.392486803587715, -1.0188444454820582, 9.567730968233471, 3.549963140904717, -.3869990524271853, -10.685021835058652, -7.803221885373577 }, { 5.519141769863746, -2.4687444482092453, -4.527075792917321, -3.0587774851769716, -3.032034406548399, -.29854268909760934, 2.82624629337018, -1.86812687912778, -2.407517395219838, 7.2806638244344075, -.38707237305630826, 7.117376820971618, 2.715106036773286, 1.882832329702864, -9.148383907066643, -7.384156808559382 }, { 4.32954372688721, -3.4723395961011754, -5.472765753829191, -4.230434164029096, -2.0418246791346357, -.15209873187945191, 2.6644211871616887, -2.693192534858878, -2.1907383536535017, 6.833903547321982, -.4726551227190131, 10.483750864088186, 2.7883123536975316, .6878495969688955, -9.138207282002256, -4.015891959177244 }, { 6.1136409994159635, -1.6979306899931024, -4.528715882492263, -1.4760935695180677, -3.338867629505195, -.8517302330090586, 2.7463332261421862, -.3782174799747705, -2.359848902631384, 4.926995828211428, -.5543059775469233, 6.148566224969242, 4.263927404270247, 1.263289790299201, -8.724339443726047, -5.487372037768092 }, { 7.048429486266846, -3.0971847308847247, -5.7315229826999925, -1.3497794073226814, -3.579964804153934, -.480953738839128, 2.2090438519014386, -.5478510329843008, -1.8832231543976532, 3.5986188242162176, -.6774698350403673, 6.047894134657205, 4.287508897309159, 1.1592682920317463, -9.24656018430507, -6.64398003520567 }, { 6.291014295121962, -1.4233509397520783, -4.186122298308422, -1.2368055816773857, -3.3796625040759416, -.767540904381147, .33176630301748233, -2.255132364212959, -3.783422624373881, 5.844039457945461, .7613705353160022, 5.411258138017747, 4.398245129818321, 3.869121254972596, -11.089048521991547, -5.115514818590804 }, { 7.102132594311975, -2.630039434788609, -4.413241138813232, -3.6599028482232114, -1.868269364616847, -.41808043848629767, .4675898382053685, .07707443024135364, -2.4035646901684244, 6.657588530211404, .6181675349386208, 8.24145071803288, 2.3304941001097776, 2.868528339767085, -10.169021236216393, -7.4383431007978125 }, { 4.993449510111145, -4.206883771309196, -6.379484830481839, -3.5260290469795863, -3.6153254372125967, -.6040852790242238, .9200631463813889, -2.061095715561307, -2.224356239973787, 5.7392901901061775, .502832243820377, 8.74292719626191, 2.231992012367083, .06767613895715288, -9.815226441297574, -6.589274307230983 }, { 3.9796181210191857, -3.476909856600714, -5.726318176120975, -3.0280549022062213, -3.3823968854003494, -.49069282738323106, 3.9357784872939896, -2.4960475089441, -2.211160091939579, 4.522583206430646, -.21872253116779883, 9.00012180131703, 4.8206202109947895, 1.4932477675485196, -10.0596713159739, -8.193008189988088 }, { 1.8358501401452432, -5.065767261560117, -6.866786010086752, -3.5874202313493875, -1.0584474408980489, -1.5857139672299743, 2.780064364814279, -3.2701249972171, -4.241344551352385, 6.038977209508208, -.09174507550212446, 8.913916214542203, 2.9712392748760905, 2.5814346870860385, -10.841365405481183, -6.157092955135394 }, { 1.1787484921065052, -3.4595756933257746, -5.577570199856909, -.7236326335268747, -.06031333626452984, -1.0099871435111312, 2.05283500351397, -1.1496570014871068, -4.317542584401203, 3.859913570634236, -.8598896208037917, 9.867067256528708, 3.3699084802010626, 3.553787900247644, -11.408411373236802, -7.377050822340249 }, { 7.934095149159437, -.24834744789589566, -5.48751436187575, -2.041864233565266, -3.0977086226522577, -.9801517842127865, 2.089648361607736, -1.9533931535197733, -1.5099446942363495, 3.0743785176795435, -.291567714514957, 3.326403374529676, 4.460885138109521, 1.8597528584616976, -9.396913840391855, -3.1280275473066568 }, { 6.574737230371627, -1.8067066845499922, -4.799552765145802, -3.1929205209819465, -2.5654643282334737, -.911086961154535, .6126227184425297, -1.2553602377801831, -.7061838589755048, 6.008539862697279, .18213331827784565, 5.498129266232925, 4.442850556259366, -1.2232976005106004, -9.742494915213923, -4.080392728941904 }, { 5.578654347264565, -2.9354123356620856, -4.903149952404758, -1.0682893705793206, -3.646146742687255, -1.795221687372967, -1.0631276681834982, -.7096521011802792, -2.0310999162625687, 5.913854550027173, .04972523060246895, 6.638871279134226, 4.659863543396743, 1.3311318377443369, -9.556595720471107, -5.988993854301109 }, { 7.08164746601264, -2.0272692669041303, -5.125326832064374, -1.9275754764082729, -3.246739865599245, -1.4078353386866977, 1.4270027206897398, -.7429074374915744, -3.8219128556633044, 7.968338639009359, .013465407268923367, 6.47682640914717, 4.266588724901972, 1.9022357743198917, -9.168899185755357, -6.8984426833548795 }, { 4.988307677149953, -3.0758957598335206, -5.749279341201565, -1.2824688884809774, -3.9217399166273843, -1.2035165158600811, .7351002106400537, -.9697620873577121, -2.3346102682787317, 4.8673182669473976, .700177494720247, 10.439793898907727, 3.5860325326135434, .8139635363977781, -9.060403172479925, -7.543829651415103 }, { 7.167281450546146, -1.3320338094054327, -5.0203565832961745, -1.7193540878533295, -2.0282761241749045, -1.1844233136359406, .049762771092937974, -.8385646726725347, -2.1956904797729906, 6.259896209409361, -.4340992059500998, 8.620773807024346, 4.731839377796049, -.6094992187482755, -9.254992443300834, -7.70221660646501 }, { 4.891286566501683, -1.1033100179001587, -4.905756488064787, -1.2425512929883529, -.4588825863555458, -.017239650485260317, -1.0040913325560905, -.17835899353528087, -.9227122465293479, 3.6919083583227614, .9606459657167924, 9.61927060385977, 4.193935978060035, .7478739945103517, -10.565964214663868, -7.804435110496504 }, { 4.718151391166206, -3.4281681266310935, -4.332926809287205, -2.5624968159957024, -3.302588664064212, -1.3006963670293175, .4788721319241604, .43099990885914286, -.053714757958973805, 4.965190362461478, .03543584406186012, 9.745055412847602, 2.1367331825671276, 2.6553483321330833, -8.879190118603447, -6.1319500258495445 }, { 7.796236295295, -1.7529153660459862, -5.625955508326838, -2.6066277306653243, -2.3166606000628867, -.0939766714726321, 2.1712049344483844, -1.1745273301512014, 1.0192986786986027, 3.087599032677075, .6702779834697111, 4.415503621839722, 3.1712031419995665, 1.3031191323217137, -9.981424130990666, -4.016746619253903 }, { 5.076108586387968, -1.2457505673737121, -5.908216663381108, -2.7965039797861255, -2.594733329653946, 1.344539519218283, 1.0971535675035833, -1.165226073615999, -2.1397965875038043, 5.899900291905315, .5296135994210163, 6.563578503230482, 4.143511450269892, 1.955976907025349, -9.679994550751639, -4.002474747376172 }, { 6.235981381396307, -.9929604561585816, -6.52586724455809, -2.395583341573983, -3.304410575347218, .25938708557713275, .30775496258708085, -1.4977272301795563, -2.1110963520224635, 5.66934112445888, .2903478187864726, 6.345618012858115, 2.9620929958092295, -.4309152915739301, -9.994520459122544, -4.67652503659502 }, { 3.85538914172586, -.7954062099322002, -6.474061171700101, -2.4610184391815526, -5.030124557385321, .0822046639384746, 1.3579784575931635, -2.852983810101811, -2.039491631138812, 4.600203288042001, .119399062621792, 7.3150204803283705, 4.598288975966866, .7164075134562664, -9.645424986007729, -6.756890465011889 }, { 6.5524518417673745, -.26060185431388455, -5.972026775953878, -2.452850466340568, -3.3416009801309947, -.4349255157714478, .30167435099987416, -2.328087254979333, -3.010650974254368, 6.138503446144685, 1.552597687402729, 7.7167228936831895, 2.889288335106435, .431191799002293, -9.469153072383516, -7.525312626355411 }, { 3.6346774157011685, -.8486415633154335, -4.83959180352253, -1.5224238718064653, -4.365412492165897, -.15162480715452784, -.2576154736810469, -2.31143475996986, -.8925997624466633, 7.033295107056012, -.7907910366281614, 7.315090583913168, 5.945230044383036, 2.1246481364748235, -8.12163430156962, -6.842794078039797 }, { 5.065136936424223, -1.9342639766467262, -3.995978006599397, -1.8591833003161238, -2.697394912273241, -1.1166227654802796, 1.9565469431042595, -1.9183621601272838, -2.6246271620056647, 6.895864601960585, .4713415759122206, 7.802890882557591, 3.5889068556200474, 1.5306129978814447, -8.419869982948809, -4.883506982253077 }, { 4.422408555015181, -2.7998521874804707, -3.043682861362041, -1.2109263156224463, -3.7512194530946847, -.885206509128084, -.5363479062699483, -1.6952865306263245, -.4636657494471531, 5.285555481544102, -.006083259475689034, 9.21201102559176, 4.218599605145462, 1.8573892932610756, -8.59597488301796, -4.855670695229674 }, { 6.396765767006468, -.7258939593504713, -4.796028062580756, -2.8005489694252503, -1.690377879882669, -.4972976323489131, 3.874930686562737, -1.0984745615508127, -1.7099145470648276, .32620450489881747, -.00898406856509976, 4.938096858045911, 4.972705238441204, .5084257613068294, -9.054952468525626, -3.5171290947698686 }, { 6.566726945542609, .09750013350387582, -5.101484947503962, -1.756701997034227, -1.8828554871059044, 1.3281951054245666, 4.772677144588938, -2.1101235296955214, 1.184680411502248, 4.340622071430795, 1.5286069539279432, 4.779853048823372, 4.497843765241453, 1.7476067646610434, -9.892616683436293, -4.677966091652349 }, { 5.792424215442705, .1388917362421013, -3.8257066780889373, -2.0552521162922215, -4.39073398358768, .7535288817856612, -1.2999461234770018, -2.1123211073415424, -1.8956119276588128, 3.916428763918391, 1.2591154178661155, 5.0418995511952325, 6.1939211192598, 2.7380528131375903, -10.660144358753568, -6.2291388214408325 }, { 4.0289409840809505, -1.0001978347525722, -3.7891494534193164, -1.0732089505211146, -2.8664976188455875, .36855005586221, 2.1919351441342223, -3.3202017706290827, -1.0272740869844383, 7.364297930147777, -1.0382550319802168, 6.737990626370726, 3.9922766732481305, -1.090900951168273, -10.431201941657063, -5.273371082425306 }, { 5.221854209814804, -.8221539873448824, -5.4627069804372015, -2.1359610328069083, -3.117986072955969, .058815540629365295, .9188949013603801, -1.3735922157451572, -2.548893453807128, 3.858090029792055, -.026789846851297393, 6.0162884188171155, 5.465478976966219, 1.1661860727052498, -9.577145900935179, -7.656973124504354 }, { 5.851949306577079, -1.3994252347829619, -4.739992922116161, -2.9264169303070955, -1.6179040870341894, 1.4491247821491098, 1.045779973649251, -3.952998388338084, .1784606459767043, 4.602222162146346, -.4721817677277645, 7.137275984390753, 2.2315730455442417, 1.053945355040397, -8.875468096691714, -7.098627519130716 }, { 4.150897148636082, -2.273090795644095, -4.335345629290315, -.9373150383425383, -2.305478912076206, .2515417965384195, 2.098173238933058, -2.6811211985777423, -1.3534206476953776, 5.435030076015577, -.40810111264526794, 8.528913220915035, 5.011414581454937, 2.607378404472234, -8.334056363473655, -7.550603584321457 }, { 2.229783941149873, -1.804529652674782, -3.2673325018144777, -3.1197914311312793, -1.1861837067920418, .06013205537197736, 1.5161654088904557, -2.0790364230651117, -3.256779784127698, 4.696148363093452, .8261881945477906, 9.03201553293362, 1.650047931069295, .9874069333184009, -8.581878845167013, -5.580042405516154 }, { 5.470961693663234, -.6495178480165342, -4.893653009510447, -2.0007772633474907, -2.1177930033648447, -.43120633515631573, 1.3300646461375112, .6484144226976198, -.25950955402488685, 2.2666530829932254, -.17728924734770085, 7.360180381766676, 4.10447470631897, .10183635154079286, -9.509493668682026, -4.839543623580464 }, { 5.669045695152848, -.5326629339865454, -4.0150037742380915, -1.2592504056543727, -3.59237940631575, .3364702278481864, 2.8092723162339395, -1.4033273839075884, .6804544741934356, 2.3902219689462685, .4038320709050145, 2.640584454077894, 7.172799060453412, 2.840726359446195, -10.29506823660905, -5.282938308093869 }, { 4.939392255633453, -1.1566283624866573, -5.247820992030828, -2.273355614990087, -3.5565026484364934, .41614896677182434, 2.1537458289142726, -1.40223760913254, .815405303036051, 3.6801084140059257, .8379007454409441, 3.7807974736552667, 4.353126252156236, 3.009239022893423, -11.149552829368218, -4.395445253927872 }, { 5.138179296318455, -.14341149705861908, -5.272381626258006, -1.3455877855615488, -3.1510826621995527, -.6446144887709129, 1.9653424449969166, -.5197221800122382, .1508611881251664, 5.162892631039076, -.22315432742177646, 4.7107461653822265, 5.918797809831941, 1.2650641653430073, -10.515920501541792, -5.583771555528738 }, { 6.159052783433846, -.925127342974917, -5.0756125660284575, -.9854747559923771, -3.30142039263018, 1.151058746913337, 3.7121255065859535, -2.47846723072724, -1.0191301536552935, 3.866914462515026, -.6960573737576343, 6.4336154980070255, 2.1396510167735703, -.6424639427453464, -9.308119627299993, -5.864768125589269 }, { 4.577223744940434, -3.3701333506477233, -3.3632412620750243, -1.8609376766789527, -2.3772948832704595, .3055310893397456, .692789991248778, -2.145364026835868, 1.124685882578909, 5.016818628999491, .6479042085084266, 6.808396995767951, 5.528686553847957, 1.3531520091217901, -9.004603673931026, -6.50711953666332 }, { 2.6647697189688664, -1.9186983789312064, -4.336219328482681, -1.630235590047108, -2.085128403238894, -.3216896890819066, 1.688847368323755, -3.2759748935446926, .7924662452632897, 3.780664811935802, -.4190417755472744, 8.346785026212322, 1.8261718759255605, 1.184923856347149, -9.043536199008868, -4.402010293318898 }, { -1.5266017821486828, -1.7874772324065589, -3.182562475979737, -1.584973717779088, -2.1492238340915177, -.21311636606993398, 1.89655059491839, -2.2880797071194836, -.557150378242315, 4.335966596408453, -1.0187480977219385, 6.805098473346118, 1.1823110318152814, 2.0026646898522498, -7.901871421142059, -5.977597852091029 }, { 4.368174221320672, .5885421906308378, -5.3197888238519395, -1.2124814108958337, -3.456741634824103, -.62094036501369, 1.758016816963777, -1.4091473834498858, -.8930978318615312, 3.652510883980092, -.09343108764034692, 5.458760472239761, 4.694049222473947, 2.9177921137815592, -9.850968382872507, -2.9896276148631236 }, { 5.187839594260144, -.9971409306132064, -5.032106977613964, -1.2883629656402174, -3.7922442100437643, .9623804975501647, .6370732352882569, -.8226693614968794, -.4917973862124109, 1.550259746345026, .7545165486992763, 6.231191464250699, 4.987376928198655, .5621422600745261, -9.455815979024567, -3.492911718987602 }, { 6.005706396518643, .14244654957647046, -5.419157063335954, -1.3610639776837588, -2.5873875335919263, -.4815689757740782, 1.8385399802403526, .33734869675912854, -.29855983518060614, 2.1017039437158243, .11510324389555623, 4.69272030199699, 4.341453451704379, .6882220622054992, -9.8934625244641, -4.6818107271630085 }, { 3.6466471165159464, -1.4209203804170074, -5.547072517395892, -1.8820765790091118, -3.233274564270441, 1.6007651562297853, 3.0383051819755913, -2.0375467440358084, -.8529318988110357, 2.1867376812240553, .4186652587459237, 5.592088991976154, 3.3365540535604614, .06593162066697207, -10.725543076372306, -4.8748601318110065 }, { 5.967004120554394, .1766517197235249, -5.7309583593893825, -1.506767083189615, -2.0076719954111497, -.2423857124230494, 2.5209688251506117, -1.0512447556166782, .48353729427316733, 2.702730916044447, -.44190428081840505, 7.116858277432681, 3.5741190760460944, 1.8150683872095275, -8.738496689010264, -3.8273246068210027 }, { 5.927074951749331, -1.613931125078127, -3.6445953955914048, .09326008923992891, -2.6437557203042323, -.7983071366412126, .700364161714794, -3.246456877229002, -1.5942295503964927, 4.472133265621317, -.9867340971382064, 6.33870089052794, 2.8962783368002523, .9496317578649486, -8.74686974147788, -3.5701400950161015 }, { 1.6501825818994178, -1.8529026465081797, -3.9277144890141824, -.8287366263428804, -1.4644035902957324, -1.1357477435072507, 1.7068665547427122, -2.1610057888736884, -2.714381952580056, 2.446016001723615, -.19005573554644423, 4.7283168333272725, 2.085862941508618, 2.0532960810722516, -6.966310242518727, -5.381083461798037 }, { 2.644064730905729, -2.651844494427281, -4.487065150257183, -.6486554039968476, -1.4292869812394648, -.9376379188913586, .9037084885948459, -.5595308004294425, -2.783295462414021, 4.340764369543441, -.776602835057668, 5.380688315572347, .9975924964331526, 2.2913967881280115, -7.599414023244442, -5.075762362199379 }, { 2.2327030682080515, 1.908559470931671, -1.8863793617558215, -1.5247310187566898, -2.157078539550982, .6156559727660257, -.08976631425673927, -.9115046822334237, -1.4450614248393243, .5072279168965196, .6571146677588325, -2.690768476956226, -1.2117058594591619, -1.6153509678721627, -.3644515892908029, .16701663878900105 }, { 1.6333272419481286, 1.0910526463755275, -1.6177754560300635, 1.844985362907457, -.3183517391639798, .24476665418970872, -.7796898930999332, 1.4765889081425994, -1.5473966365293446, .22642856511297596, -.4016955717481066, -3.284237567928384, .3494689392814321, -2.3552926492604294, -2.3994249566360977, -.23048911807207506 }, { 3.7326103513491997, .9112956658001258, -1.832779609234238, .43604460501602466, .49895087418135536, -.18394650955078487, -1.9098109875845195, 1.5432287001767384, -2.13211623087997, .18345611412499774, -1.1498691925709101, -1.0689231656341995, -.04616102860270953, -1.2570302226631813, -1.1695777541964192, -1.2572197198456254 }, { .6986286197165369, -2.6910338819836097, -.4289623156340999, -1.266084575707071, -.9538862029387304, -1.056094711556681, 1.6688913699047434, .7526964736030447, .40387180564867214, .7308467665994844, -.454133084594476, .9218441696409726, 1.4023046792504668, 1.47740468331552, .674422662525256, -.2755983977454935 }, { -.3309768698276746, 1.5589801582444394, -1.1348400907672314, -.3451946444892696, -2.7086934795462465, .48476160413178215, .3094811784379647, .8126023407479323, -1.3444221656687803, .6319182642556843, -.7275564233471845, .8837410074375952, .9421560988137899, .1441203358992289, -.15480418857188716, -2.016264784057735 }, { 1.2419112158941092, -.9616555976709952, .4045187008948006, -.07386123197188223, .5486720809148821, -.9535418352155008, -.4660570693898473, 2.8160475507872254, -1.0550543066060063, .47708555353735616, -.45248441730584626, 2.530291358198917, -.004931555999567187, 1.0925926441786327, .20833085396025505, -3.1132805170881457 }, { -.23296651655776238, 2.9889009319742055, -.8985169003399581, -.2501707540227347, .4701941410971341, .3790591341823922, -1.0047133293696449, 2.386935679283729, -1.054636333591471, 2.200898658855322, -.285243817085793, .7992891830117685, -2.471197110165769, -1.086695067279266, -.6965113807755766, -2.4956786074438324 }, { -1.2643551691049173, 2.3644636509087458, -1.3877884260989766, -2.881692401894442, -.2060161583485233, 1.7490150389571588, -1.519808626043052, 2.8327341314837406, -.17042298614854193, 2.8348675894742503, -.8243487695313879, -.011358553839356878, -2.4095605852922715, -.2252114267624917, -1.258058665602041, -2.0879365072275555 }, { 1.4080607708295354, .854706370473981, -1.0282886236197053, 1.5446429763612735, -1.3070061580879992, .5251181459639539, -.4417927689735112, -1.7448091967701893, 1.023278843885888, .1352046054103645, .7509974031566177, -.4060062713606954, -2.4520230783367025, -1.167078330083136, -.5308282484387273, -.5054214499376218 }, { 1.9125262213551377, .8439500905487295, -.7376200389653944, -.19627920411104668, .5277256891526907, .3116244925133143, -.7265637437466937, -1.366509571654671, -1.514670028243901, -.20742750324893708, 1.3023215115187177, .8711198507936228, .46182398643659905, -.513266562795824, -.6627443117355186, .1355686363472359 }, { -1.317576250401878, -1.154588609269658, -.6395005535782499, -.6624396894260732, .8488774271998534, -.9347097835898396, .22073790735696416, .23742270259196144, -1.7480690758990929, .7592549071734205, -.766301272170452, .24205924983678995, -.717622882304252, -1.8157159053148162, -.4918623567653423, -.2749865267509699 }, { .7634437049182119, -.07741887679635638, 1.111033837432077, -1.0847798647320466, -.25853585474034557, -.4831879453519076, 1.5742152083451173, -1.1327011511672584, .4746840708815605, .21727501053744422, -.7786334658589598, .7473448701566284, .5281500148737006, -.6751079557237567, -.42416462675669925, -1.1224404865683657 }, { .4245818199747073, -2.5383925404912593, 1.1983108388049692, .2623262362640953, .4433448388497979, -.49791282080654514, .647743616512083, .15943803008276222, -2.1930556974668223, .4130358626741861, -.31136518038839034, 1.4280675604771678, .06667056332066906, .6493033156639968, .08255426911731971, .41475332865239756 }, { -.9058403647933773, -.2781377411475033, 1.3643017978153744, .23933003103594158, -.9276044897850498, -.7524904091395548, .6445458112285899, 1.7833203176680155, -.8617911099098927, .7271451056618007, -.9628341643331383, 2.157797701036109, .06933739590908462, 1.4708413957603645, .2990339827397995, -1.1787220085527055 }, { -1.749841694248381, .21726018166620117, -.7550642575455268, -.16688744382787266, .4031002757067012, 2.6430643367611792, -2.189463035256304, 2.2921844747731837, -1.1688233339540026, 1.2875272191670526, -.8785627871026198, .9097964897167156, -.9786130036380118, .9641589433470854, -1.1057892256485609, -1.3111749970392248 }, { -1.7500201080747197, 1.527969732648221, -.40842380140206624, -2.5393197233683935, -.5603885367583591, .4850691385140184, -1.9074950190401452, 4.184445605373236, -.5169100882713255, 2.13357332418274, -.024541651236208117, 1.3399520355082613, -.7724057976437536, -.771197648460451, -1.2330860748306471, -1.7684258126921013 }, { .01068433818010069, -.8844119040706013, -1.2821260847660658, -.8214395269851203, -.01403251969056218, .4489302616632337, -.33992089796445235, .9290392176797911, .8945920637646796, 1.4640848031682054, .15466994746455426, 1.1458252446616173, 1.033432522554727, .8707907911792159, .8727260177311118, .20317699854271282 }, { -.5364307148585902, -.6414236517616545, .3009963375800827, .024079988842688197, -.9032787179058104, -.27268361331556307, -.528677457746114, -1.404198883876788, 1.0164577096599716, -.030234655255858486, -.7098088006442476, -.09914903210639855, -.8852390552737053, -.36147630589957547, -.4489846396266902, -.3365731188315861 }, { -.6148081434031715, -1.2351352395868584, -.43586890510884685, .3047196545352777, -.013595440743849729, -1.2448724806761633, 1.8504167088639032, -2.062858298395209, .9727046108093886, .03565003916465745, -.55039485947108, .5446408356334719, .6577804408918413, -.5554550730578314, .23063062372574736, -.057924120180301866 }, { -.2360531549774855, -2.832420922350272, .7824082263775969, .6497559768119768, -.2458811498537372, -1.2227117906074805, .9211885090904701, -.03876854376657244, -.008361884179197107, .0027808233174127367, -.6344438149205821, 2.693531057870481, -.6336888703021776, .5582961693518279, .6737778129716956, .19660199453154384 }, { .480358156542574, -.7008424246042785, .6399756028098228, 1.2091402359370982, .11177726614578366, -.797382846834235, 2.0702959181825142, -.30998023274731096, -1.6517467049361996, 1.421099889181155, -.9249473156526131, .5248498878851263, 1.270325916756023, -.19223339424703934, .8786400768222319, .8958783172396882 }, { .33238704183141954, -2.2892162024037197, -.017591897958556743, .5378147084718833, -.28367122723026067, .4870079538797279, 2.6932899506440435, .9890041084653269, -.9928018790596305, .4326348259768538, -.8406976545741675, .8241624034122487, -1.4199701617487246, .09801095193532869, .07512728313601147, .24846989487773516 }, { 1.6591514962200509, -1.1008860067101987, .7592304890425241, -2.060024513891203, .7919768673312823, -.22643628570049182, 2.2851232938587565, 1.2768994117982808, -1.6532208870703875, 1.1580044866648835, .6895564254219196, -.8741185773681452, .45788908715329313, 1.7481061934612145, .03865945097042224, .4003003111938527 }, { -1.5611539931623133, -2.1242520086605556, -.5362329170116823, -3.147188306099177, .33560691159065265, 1.8337621369796608, 1.0612781693268063, .5722475708112165, -1.0683893640966324, -.4206883479832398, -1.0832632364152501, 2.7852193204839684, 1.7865106646191922, .7251005948213053, -.5321163811211582, 1.962972553605929 }, { -.7920749727193566, -.5179463856611933, .44312932527051957, -.4080621713199904, -1.0834153247094238, -.9837339804072129, -.6374761332924423, -1.2614955172549132, -.07727481338390356, 1.1316234699369556, .024000384284129126, .2170694471445609, .6560813512241267, 1.5498043929550327, .4513192276622136, .25273165043133416 }, { .7624540155283845, 1.3201308809451697, -.025832640400998596, .08786743698985233, -.48295727487361034, -.8388884314986916, -.9664557055869932, .005702166432369038, 1.395340512507996, -.39118755274862055, .0824862314405533, -.2882941962901116, -.07680958292048982, .6435324951258731, -.6574043452124352, -.6117817419560934 }, { -.6030059649404539, -1.413019859620425, 1.1460399921681166, -.6402876798438806, -.11643295611807797, .4569103181161223, -2.844325376939251, -3.620680665620488, -.828794401644504, .03502352362514655, .07499903195572807, 1.7594000017180687, .6223716169085459, .2838776003928857, .33672688342295326, 1.0057245423226993 }, { .6394907703033438, -1.8342017612252126, .6417910126232879, .37910477014932153, -.4675235819678347, .06110321428643049, -2.253926361959771, -.4333678714206424, .053330319474764715, 1.2533966054276575, .8465279143634687, .08650013516595204, -.7212026321626965, .357294966111558, .02260441203382031, .5959219263319894 }, { 1.2587640457759035, -1.7098844240895044, 1.8125461231805173, .11238445004333183, .4237162032725744, -.6906369059786616, 1.3612985265445956, .0034521660393713547, .2609926977917228, -.4204128758952708, -.2579628662239957, .5423279104132893, 1.3433207207843108, -.7463769607101012, -.4634886378781878, 1.2257947324052276 }, { 2.9267396065173923, .17087349946709005, 2.824765939693855, -.149977468440734, -.2385862780057155, -.17215358245262144, 1.040460280424945, .08054281129448805, -.8544081569988854, -.10003347521360943, .8919806068214915, -.2625837612675881, .10384958604426443, 1.4360917434441618, -.9709876260823769, 1.380896378455624 }, { 1.4066038491885902, .13771703625306286, -.23331935427814998, -2.6423556440275178, 2.4131781628215827, .6520446329825715, 1.360617856185769, 1.52751012656303, -.6056186121102921, -.0081241906295837, .9964059787916603, -.1770199065198314, 2.6449114052767135, .2998088653393301, -.8026829117742832, .7880607992648566 }, { 2.7195815428222754, -.1830971638413123, -1.2544229730730974, -1.0275781943627122, .1763469518969003, -.04321199655168723, 2.516513693458052, 2.3215255554164425, -.6977331616328301, -1.3232991656118565, .43631086564525484, 1.4570730327889148, -2.053303188665023, -.8042717953363433, .07135493754416687, -1.0034159107407725 }, { .46848639988388074, .46584452814486693, 1.4099372279124307, .27778067796902434, -2.1510962148833053, -.7698711911690055, -.4281682279933012, -.32581167932108895, -.0354959453449267, .1876942153655635, .41832928639185435, 1.0308611657058673, .07940267772888959, -.28388440246225294, .2438835266166472, -.09111899382643164 }, { -.37190397379297385, .19340728926344386, .06907276233917872, .23366732469252766, -.9344456501977024, .5408381314703463, -.17468626717272484, 1.1759644318970375, .8345816794406087, -.8626924774977157, .04295278340658478, .19966256690123363, 2.3978367020481337, .426251431679736, 1.0745075767222045, -.4436311532821822 }, { -1.9868764237821965, .07176491846940708, -.2514180169084044, 1.8305015877766426, -1.0226495980792882, .03391128339975582, .01207839458180568, -2.5518608502558604, -.46115400312918964, -1.110936763809013, -.1768670689205976, 1.544100176603702, -1.162004170370295, -1.014723772815208, .04655545889945782, .17186617380071423 }, { -1.0965678527018552, -.41121166956526783, -1.4961726584194877, .8880799209464939, .37961758650625627, .3957811544494395, -1.0963258267469567, -.6891891643234808, 2.5224322398977734, -.11217656718938665, 1.5203516943552775, .9435731261806459, .7115231041331154, -.6231751655372262, 1.0230070545450756, -.22977887908163624 }, { 1.0399898433350148, -.5222816717830554, 1.4710422793107383, .8538659014521115, -.27639847396029366, -.4756955587768941, -.8975529271400899, -1.1188465266798044, .9530042847401902, -.9798550088028832, 1.0238289934529545, -.2405750971630791, 1.032179064402253, -.21930609908532672, .7896309896354451, .5148599654711555 }, { -1.500402694049623, .910675311507898, -.3316468046326613, .5409544902426514, .7279371293897088, 1.415982061880946, -.9473235075630357, -1.9083763821209656, 1.0682006841813703, -.06262675423113587, -.12908083951049623, .10950470914094136, 2.3747183763968036, .6880405207592736, .980460749013385, .6706033169505684 }, { -.11248067041842272, 1.02545791505207, 1.1538017376090708, -.903250364253939, 1.208735845846997, 1.564669253909392, .25067706023554276, -.8909359032628199, -1.300646008952412, -.43333858451854046, 1.1496267274709795, .6868558007538034, .2387474725537232, .043433504395797615, -.1413645057830267, .6296644122264494 }, { .3487296545380192, -1.2511117529184963, -.35200916217384315, .4958940139533573, 1.891241441039553, .012504706868377137, .01403026581092094, .029483084833941686, .1470858913490616, .24525389438434925, .8994866241321083, -.0031089452135718808, -.27929372764904287, -.33971388910952, -.27896316352655914, .3838882785091187 }, { .6510533387258908, -.6841118148826655, .46019982498643114, .20419743433477439, .27945015087175396, .7872091059400333, .5789618509937742, -.24512999020240214, 1.4743646051142945, -.4041638233334334, 1.3416148333636384, -.20395622979064743, -.12183649261286195, -.8458910380282482, -.3539673557114418, 1.5469167985737915 }, { .6648436655195282, -.30778017052292495, 1.6445107837203041, .5129743832796032, .8507735066430187, -.09088427764943656, .4424693048489646, -2.266237271299025, .8729705663580741, -1.2215307403368365, 1.7720082967076365, -.016530921111973657, .33095232532054947, -.4364911812339807, -.1736853853490004, .7313851103117558 }, { -.2760493948168419, -.05371528146890942, .8403546978770375, -.06084547337155619, .1409558295690339, -.15266687059120299, .06325321043674209, -.7625602859520587, .8679847194014082, -1.8046514726351677, .7571481342222267, -.5559193417670584, 1.3338829745624687, -1.287989991610176, 1.1432910120814708, -.5863037093274355 }, { .45543689607780335, -.20400400286871542, -.5398444923374769, -1.3621991421138886, -.08181553156562271, -.6704879317634979, -2.0801189702761147, -1.2348413276113133, .33763036771679367, -1.01583325674039, .3386716712114475, -.9102028334137183, .8312536122456752, -.47966961139178427, 1.052699815725014, .5406434008735655 }, { 1.1212131782538683, .42409286767625154, -.0633883659361234, 1.6122823275505096, -.9586336356727783, -.9947417277247492, -1.223940317147761, -1.6671468817842692, .7323697139465728, -.5077147593670459, .8924979644025764, -.7662989084385964, -1.615111037211602, -.45222584591186743, .9819812332149144, .548940030123863 }, { -2.0928960781359414, .23892149685298802, .19785185191210475, .44454646114240925, .8939821802768471, -.25138970550303136, -2.354488722064447, -1.4366919142293166, -1.053907888014502, .1284262834385942, .351140465689444, -.30192310032766895, -.5668764544159686, .0545325466475614, .6943950122134362, -.06580827517304708 }, { .4644290151437244, 1.5002767150831393, .23294287528392457, -1.1466013997986895, .12456217434610309, -.4592950574972938, -1.22025676354257, .5498543127656814, -.6104482003169659, .23538354378657206, -.013407717876218386, .23809756250294986, -.28079078405793434, .70089293474314, 1.3985038431798276, .8252268264135095 }, { .35333174387828004, .6676202282802203, -.14904722332626524, .14177131980538923, .6578554298481373, -1.0488497279232827, -.3174586770840359, 1.967615806204946, -1.239061566753917, .08753194304235985, .4782141037565129, -.455427435803799, -1.5993258322512105, .1890673953783147, -.5486195629786838, 1.0821157662261383 }, { .9121458645477101, .05325555554739795, -.22098801821370564, -.055124761991132636, .2785206102353645, .5705541856612375, .46457168585133196, -.18362206816018597, -1.3548796054590415, 1.2415091010691908, -.881372062243686, -.22183173909563744, -.9813395384305864, -.4261718641465193, -.9347389123246517, -.5038872582886745 }, { -.22998112562329523, .04561056861605047, .6747496114590914, .06932702363661772, .3908731890791332, -.10315682660451847, -.14074273361519574, -.8233362622310362, 1.4846982099476036, -1.0443090141492826, .011761746980872593, .07006916218910925, .28990883051341254, -.5171328367697574, .2392797938528627, 1.1706327080211443 }, { -.8076771109007476, 1.349391022388627, 1.0344288449443766, .13681788091233088, -.7723953151852571, -.7805139074649884, -1.5650039263278877, -.9818511518794898, .5889740299606187, -.3300084128790671, .8070491674062175, -.5900459879989557, .18142052215948026, 1.2086007991486352, 1.8874113792185427, .13044570730646288 }, { -.5880572437445039, 1.441322373070674, -.06568874990034618, -.45670280092821847, .32571735304068916, -.06894159139178577, -.6065242864594053, .48956157749482, 2.244240521225379, -.9987142350850747, .056954887384218, -.35653163778489844, -.17895552214895336, .3983176660750694, 1.5102756284805394, .9129772078153678 }, { -.6215803560926609, 1.6596725543238096, 1.0310527995767091, .19529657444977652, .6311793672948977, .7485179940577339, -.5173213750514228, -.9458464583824535, -.3759799486157276, .276062776185955, -.6975465518124984, .6679085261618518, -.02217143729363198, .21183017253997377, 1.230173868885024, .4531008190075726 }, { -.3113341982483231, -.11071366692782081, 1.1776317227635986, -.6499141831477986, .9273035046475017, -.8147217936396801, .3586028809924816, .06516675009959218, -.20903480157677237, -.06103861593139148, .4233554569589496, .37996308903906023, .8927920996359974, .4007757705918082, 1.8542588505162154, .3583724673863865 }, { .407742811712929, 1.2468812249601005, -.8894768510572203, .7377092096965787, -.46336290704968675, .28259779010246167, .6095685020707177, .17208465263639472, .5076077538729232, -1.1240984060158834, -.5167544976911196, -.6205330420894347, -.4805629974979422, .17341251594054397, 1.5058159928414419, .16097656047146192 }, { -.04564731468331483, .7416686956460367, -.11590776963970476, -.10412208910601482, .41744437163790005, -.6666342351477069, 1.066262451968357, .4631792018408429, .7674921248754202, .6971833821171195, -.41772214099402566, -.40307817675081575, -.8390115752026727, -.19175662093734588, -.05303099641849158, -.21860766700254214 }, { .45625302427327563, .7871298846531128, .3866767462879796, -.5947539563339967, -.8748577533654662, -.25007821994093027, 1.0739884418382075, .7100832820436376, -.8144937011352684, -.7549755323454568, .4096762807422487, .11968912921467881, -.6537582732424947, -.7222667204394863, -.147883636485846, -.049914871186414356 }, { -.7867248249732444, -.5799787801453159, .599788031866958, .5631087210713535, .4012853850364196, -.4076322345752429, -.29523707158593565, -.5972529980885223, -.6994710441550004, -.21917265094172345, -.14957328208239443, -.6243929159206626, .26977910809522965, .6905330016056577, -.03194342185387825, .7352447094783647 }, { .38180853841227735, -.02105214695528787, 1.0261629429719417, .6940024572170984, .3299093991990523, -.3379765705083964, -.5205934251736668, -.06637676798586871, -.4157758856226785, -1.0556061855032441, .20532918459529015, -1.6424411041062694, -.34045079594076594, .3004266687639365, .0021068962212119693, .12228501002683549 }, { -1.4804454491601096, -.018959869165967584, .31312853010894826, .9080509098752858, .1696157557753984, .4281425949292886, .03632971177286338, -.9468511738667789, -.4415762967706819, -2.022155037785816, -.858225205196522, -.9345501471242342, .6377781018390125, -.6359778250217535, .9378217877963619, .6663043331948414 }, { .44767933307221414, -.050674054581332395, -.5091590271469872, 1.0526678944280412, .2802439521714753, .08146596600091979, .3319756020976622, 1.1766291877585842, -.8821147442448212, -.6559027000175401, .19043475689812894, -1.4909448728194665, -.6705292635746597, -.2844623747913788, -.28278013671648883, 1.3779851305099309 }, { -1.5207312825438704, .5752114631552159, -.9398136471811339, .14526372574608784, -.6435634299443612, .8579494641416364, .7419075817283304, 1.0455852297012986, .7615449837580901, -.7336394218540925, .6435853760007002, .22739977832656455, -.851481697034454, 1.0781979212826216, -.698685203454691, .8513966626460369 }, { -.2358709815138434, .9941057272015827, .8805947326513223, .2790146740592077, -.5153059375732664, .37612605615227435, -.596009171535343, -.23732931474867416, -1.064024199956334, -.6527598630680058, .8551366190750026, -1.77681167778318, -.5052478123228066, -.18185712907159624, -.07776618035150405, .3105732579434761 }, { .2047994320223333, .15132742724215578, -.5197017206181885, -.4814855885716826, -.4335600736578262, .7174028932186809, .851508106051873, -.544497233201522, -.054101292781365586, .32327081677341674, .21392210651377538, -.3918987526652551, .07529117318004881, -.5819403586598209, .2603762736117711, .08348806576680624 }, { .4468437628717159, -1.2832171645572286, -.03999504128783135, .3882808066963339, .6030359319851956, -.7402476112895515, .16799503716946992, -.5023692682509691, -.2385951369338821, -.7320009023361612, .5036490737091552, -.006995548077246658, .7957236368054671, .5662973396857249, -.3806065005546919, .7693641827962298 }, { 1.101653200045578, -.166380104253183, .5742137173776592, -.3048804622633842, -1.0107952675442304, -.5540525886926168, -.15078819690045628, .052226055157938094, -.0589591651204808, -.01808320626592076, .46049066214471035, .7706662349753245, .9870779594176753, .9960015110903981, -.34037244701843444, .4911311762787002 }, { .08064272732394429, .5529397300142658, .07322017477069602, -.5945079735129403, .7071310519813644, -.9640758039950557, .9841242248241016, .5828670414439995, .2636513492417182, -.23067897282899963, -.07100085273955724, .21621315283805514, -.5241537792240194, .722449110761334, -.3873941719141485, -.05500069020128304 }, { .3726890175466067, -1.0714116727341279, .6268477125057236, -.33981527491532076, -.533140109146914, .7035985361546323, -.9199166613766186, .32009594201448693, -.17837861597673677, .5386561471635258, .5258950858442828, .6942067679435047, .263015552141546, -.689317581902088, -.9370073904270173, -1.0747126540823113 }, { .5355199187577395, -.9384639414800395, .014349092516037885, -.8379086911971837, .14251899103295795, -.26091713430920144, .67509747923568, -.077339291946998, .33114818954545605, .2106409588696792, -.4741710271042261, .4531713554876391, .5579344680239706, .9047131343022801, -.8519111683658069, -.9086937171381703 }, { -.4905580716582536, .9418668548271412, .33698446773830437, -.2954721994693667, -.94571450076752, .32675428229879594, .5287163599737326, -1.049343586365127, -1.240332312660672, .7280459763093723, .274229747518057, 1.149350842649753, -1.2514474918302942, -.953729536019189, -1.0574899266644777, -.4208991593010023 }, { .19785095772549055, -.06185167172158962, .30592122342407435, .5436442526026218, .6207762146673972, .7150478161972761, -.6972705518758563, -1.1323963778126758, -.18135793440801146, .9312531498638799, -.589466942742486, -.2796320809272961, -.3329320770594516, -.3973916281137508, -.36144846646878787, -.560700451435825 }, { .5891867026378298, -.15582423016720776, -.13773596397641355, -.14224222717116464, 1.1732700296059888, -.6406679957187453, .44396920171824117, 1.7139091919837428, -.1526875890564004, -.11719969651079723, -.8764103804806047, .05174312511171995, -.6325139710378115, .8533716119253673, -.17849097183806373, 1.2434955033465382 }, { .4552230018920367, .30342743378999965, .9976769117196841, .5182832368491275, 1.1179754095687986, .8983079052900877, -.2033174135942869, -.09669368285851912, .559530605222424, -.6323119631362398, -.6533202795407727, .5906695583924945, .3043458096210942, -.31603572528300206, .5220843200356384, .35625686512282995 }, { .04043263793578687, 1.1105882085468384, 1.1719658574415468, -.5558667768688972, .6756043868863199, .461321719751052, 1.2214997241209822, -.2952936119804722, .04179537622811355, -.12112978413096674, .9851392309448624, .6891518695816297, -.8211846460914417, 1.7231202767589844, -.9820723067439149, -.960966570966322 }, { -.5632936957286379, -.14612727931984174, .7279197856599146, -1.2552100411739286, -.8579137978519146, -.8260727355166732, 1.6474042940551934, -.37298820741756655, .24962589051007722, .8684377632717549, .6015435162744364, -.610317489770916, .06060208205138096, -.31524829242933344, -1.1039383688015423, .11087131974561391 }, { .33444365128315706, -.6914303039478987, -.2988968285319838, -.9027063144207333, .3676763974113306, -.1525575402083481, 1.1289539593275928, 1.4457897826200239, -1.3493906942559846, .7192279545635766, -.39232113525552853, 1.1295380820597483, -.7877179964988775, -.6027997174478487, -.26688311025971284, -1.1962742766564844 }, { -.47737162284845663, .03942273832249133, -.7084813393448471, -1.2243301011412948, -.9406688354567592, .032029895907061456, 1.054604626991971, 1.0710925225365606, .19048607244362775, 1.2487596510310695, .05304493940375249, .7136236187332936, -.10372825677018238, -.5405975785761343, -.9985628414855998, -1.0467372256279304 }, { 1.7588388258984469, -1.6104356730399885, -1.7052915161736073, -.49357604704271074, .8105371148545161, 1.7490952573882481, -.7324666969251692, -.40547977656866013, -.5175748666822048, -.22848975259510224, .10063915536795644, .30022713025519737, -1.5270234909841574, .2982868125635282, -1.979844563614303, -.32424692076802025 }, { 1.4297948473402031, -.24681321367828463, -.9448390909247535, .325356024187782, .06512894576221072, .9717125792297492, -.2655033879288646, .44476292718640653, .19596528370265104, .566377402612959, -.7878405458668918, .08768513784574572, -.5434024141109578, .2731101231627361, -.9424146185363605, -.672089894317167 }, { -1.208054899842104, -.8716657768675805, -.6063673749088133, .251563936311924, .021543972903648595, -.9383567372123713, -.299940825372466, .6039875108875465, -.18842366649001688, -.3484606445807909, .6452990787439287, .0789840752612114, .41694676964314104, .45134939322486484, .5472796950808495, .5646089964284746 }, { .06907424500165793, .7851037696833112, .12984902860093578, -.11586832163128515, -.651264709472757, -.12163811260811645, 1.2825854557140683, .3902142058828865, 1.1333026109293591, -.13039193883624867, 1.0398288932472946, .47582884943334103, 1.2379686560430878, .012413401541378653, .29699255500777155, .4812691677899417 }, { 1.1496342546601912, .38366344767275373, -.14320522368692598, -2.165739413629919, -.7429701005461855, -.20847058678328428, .09277631785408037, .8363005829373877, .3009280641446232, 1.4260392864638276, .7223525031938286, 1.2752551097037454, .18513506887299971, .045917113366552326, .05732253453185715, .8113317694845852 }, { 1.3950930802621369, -.4142483946735322, .853906545707073, -1.4314131405547545, .8963391269038303, -.03664018081312308, 1.155031478914113, .5173256493505307, -.3815184382306664, 2.0584387135615296, .3803807818156911, 1.2380142900668727, .8494679620787013, -.1684202226118725, -.40011517646919076, -.8854248752400367 }, { .7486346606157298, .5295089492375601, .5806636254885698, -.21104388603813662, .19119723805134484, -.17758427347443556, 1.6905674999102016, -.16992075931280756, -.7451776550199172, .49541800765124594, .0756307769040991, 1.2862676124276122, 1.0549500090458033, -.8881187731722519, -.3096961205847783, -1.9001909104011414 }, { 1.0005610722971237, -1.3228101025645695, -1.3892851029848114, -1.100424649274073, -.6849265530880377, -.5903391118614848, .7523939531953736, -.5564762051268597, .7343607634219798, .7992756871998083, -.0529285763140332, .716860661773456, 2.603042068848593, .15204024232294014, -.005437066370970635, 1.1541296306326352 }, { .511208862517705, -.22313798850699681, -1.35134346101323, -1.986989019670988, .283288458987738, .039822109612021955, .7407702164852958, .039411866392648626, 1.3231977934871275, 1.2814943684535938, .3865951453113149, 1.5712268961249698, -.8876447731722054, -.8917516658122779, .4079433238170711, -1.336099010811789 }, { -.39729383249534644, -.045228717389216834, -.6799456112625616, 1.101384951949449, -.017536936332157138, .7478309439411422, -.6309690380425043, -.9707788329539684, -.5053686884630352, 1.884223346470065, -.6585623437990126, 1.738172991632809, -.12808606426457048, .8991355748030179, -.9478430332305311, 1.4134047441176776 }, { 1.0787430048115747, .7360461276282168, -.21349140405335315, .09660526266138214, .0638081557197455, .47824751249829583, .8334891947096121, -.9013735286213487, .9838350909335138, .5869143726813246, -.7494003469713417, .3164236036954905, .10700553394483095, -.020772770893750136, .12276477267775353, .6049660475407126 }, { -1.2017134792929853, -.1468948583751558, -.2839520487960073, -.008703869193964472, .35920559279637776, -.9041325398800967, -.02505490148668649, .3204774711209893, 1.508834048528924, -.3426641715020877, .317678613114511, -1.0968519506144352, -.33791001862831593, 1.6872156323702192, .7363374192760477, .18592874832027306 }, { 1.6290024926883682, -1.7060459328488689, .0022502836072680965, -1.1562890725110833, .3296527471940657, -.6400687056893277, .32403569507072083, .5719968903304467, .10074959597874605, .8878215841249558, -.81555652088574, .5329411896394196, -.7172361174189473, 1.78999082990381, .6396929150058477, .7168937833400847 }, { -.33095970827572013, -.5498663686811841, -.4154849798710051, -2.8529522665862928, .9347810036908076, .45220525260391764, .15461401957401955, 2.5514544648076956, -.866656178776972, 1.2929478973616049, .352056131578134, 1.4288369099737899, -.6962983200456789, .09200057396026357, -.39842790505450926, -.9279072770050092 }, { .6309598746084971, -.49042526424900895, -.5658497321418566, -2.6049816295964026, 1.0640480142863298, .7202595003246702, .29798749435351396, 1.9621717300395205, -.5379740141731753, 2.160964290183833, .6612409270186993, 1.1140680884317207, -.9212644799523186, -.7199547537175194, .19084495541798208, -.04118252310935136 }, { .8585833404007046, -1.1388409867440075, -.9347439587650346, -2.146540186745162, 1.8489099606870194, 1.1452245871625197, .08501640208820775, .6079914937776192, -.24080803618957972, 1.890501123798941, .4025345616639697, -.38741951541126063, -1.1455188313013842, -.4371040076576923, .36779303643306654, -1.5994748764471949 }, { -.8008450347453767, .35302869509810186, -.146427299258095, -.600755184019678, 1.9928953032119936, .5801234126215083, -.9877268044122187, .7851347419430729, -1.0476755012767764, 1.4456813159371007, .7461262848527409, .7657969564453363, .21427027046420016, -1.7279480879727827, .7358345625980681, -.9716703804607207 }, { .5410625844595288, .8167293544980426, -1.8147918538277108, -1.8077807359527458, .7494518771415835, -.5527806688949014, .5774754397499976, .2610072233288994, .8741454065737145, .853280796465515, .3472909671001826, 1.00257121424918, -.5907323485014121, -.396076414732051, .8834194654390957, -.5106214828870431 }, { -1.7281885604110785, 1.1733457830942566, -.13392336695610058, -.33564079403081576, .411006885295342, 1.3508521081138467, -.3996267822000626, .9443549065639036, -.4260397716212579, -.1415959497211918, -.6237476202923115, .41955222059437874, -2.0245574208447383, .46431486153187795, -1.8797673920755815, 1.0148895717988797 }, { -.274416135536059, 2.594464077123729, -.29422463371521784, .49492621909805, 1.7603380175707675, .8170953872984856, -.41307805577743195, -1.5376181258924195, -.7026479950804938, -.8785616268752336, .4819816898961589, .9808271577211445, .8206928413541918, -1.1218637726201963, -.13393898902167908, .8338912922464674 }, { .3535548545385091, .037800584915200934, 1.3692352775069698, -.37528136080688246, 1.2057452667178332, .5162207457509085, -.06400344055055746, -1.1308157274389365, -.7405862599651674, 1.436370384038121, .6515107754626531, 1.040438580422131, .29776096261478396, .288209015041634, -.2991701879589585, .09764902639450565 }, { .8201603249304045, -.2115474766785125, 1.8078217471598672, -.8463037941165982, .39299412022557245, -.9262249206557372, -.1734444809550272, 1.767918863996551, -2.192967290293184, .8011874898860947, .6247077802235407, 1.28630269325316, -.5559047595159948, 1.7247344071315893, .4390128492931392, 1.579314794476632 }, { 1.1278474845392754, -.5623905962094039, 1.0176022253985448, -2.182068148802331, .4923725633229795, .17254576491179827, .7379535611977114, 3.5338218971298048, -1.6656379125332417, .6757510551425251, 1.3796862411554762, .03239737024663863, -.8540327079596857, 1.2834733957949371, .0036835935068300317, .8618732108262449 }, { 1.0601204796317163, -.12531498753533984, 1.4738745599314806, -1.3926122120230402, .6935672103554049, -.05887464206332662, -.8472176862457952, 1.7411530590653574, -.7440084163672409, .8329302824274634, .5959937309207951, .4996374720882276, -.5235668618924733, -1.0787802676288791, -.45003164622833747, .03024866688794245 }, { -1.3093285129048693, .11456436991976096, -.1253061637090262, -.11110012899984675, -.9809250665294443, .4881555993831013, -2.0919071865091508, 1.8639113686530668, .06582987366601191, .10440211667280867, -.17252582217368637, .24084740702585353, 1.6867555335912332, -1.8154346197933162, -.13386395962513856, -.981788391920542 }, { 1.2108968927553747, .23875951893389363, 1.427193688298804, .4540621335685818, -.9633274090591916, -.09907574136447665, -2.031450920098901, .977060363829758, 1.9787749898408058, -.12163560684239211, 1.0117442518191049, -.0689200632244966, -.09064030710302787, -2.123852489709253, .6392313491467734, -1.1877767166023758 }, { .04659590557054473, .4724964864399163, -.47858758890762776, .14717709551479016, -2.4924368533221943, 1.9077830861305713, -1.288119715384812, 1.784888209517642, 2.2304170120242057, -1.4587498051472245, -.588732270702478, -1.7103127386538677, 1.9490585786384387, -1.6612419786151602, -.4741379530398188, 2.6474087614486987 }, { 1.3102751532989698, .38068597906821866, 1.3014460729095827, 1.0812082838864865, -.3753862379277815, -.920359347574183, -.7486114801442846, -1.8380434380681119, 1.1428805049028004, .47526170491842956, -.1501506015973895, .20544903097915537, -1.1513863906588155, -1.0571925524270278, .38415945274961216, .3536625771374608 }, { -.3287143587780531, -1.232857382289221, -1.8234918142678005, .3963385049885982, -2.418850617348542, -1.0513095045148915, -.5289164827747852, -.38372975546905014, .009879951398367356, -.18678616662100714, -.32005865631935737, -.7532006735761437, -.8902879301361306, .3980006061938215, .11727182766124546, 1.3057296429875798 }, { -.28651611522283915, -.019403954863932333, 2.643108079053042, -1.1807638891429695, -.6943609265278361, 1.1925851721473673, .21007389627530304, -.8743356180664771, -1.946415140741109, 1.035804400607977, -.05596546045274795, .20463572947346423, -.34748598430183314, -.5776794753255641, .4281100902781847, .6235359468082775 }, { -.1708052536811071, -.2179772964446858, -.2297787868542555, .9983225434190778, .48642070779705415, -1.693551507902255, -.8578859134782565, .3684730772397414, -2.0803112409184568, .8005760753862927, -1.3805816981854993, .5004540121528636, -1.5126824651739768, -1.4996751320885415, .2106379208621111, 1.9706089624928669 }, { 1.0220349125404116, .9097373198291276, .675965146633009, .16053859122024222, -2.319167153471629, .03368889380406353, -.44814429508643194, .35748071026995276, -2.017761110485103, 1.6235795447447507, .3161845058554379, -2.664896319481098, -1.0636507895845033, -.8832542867385603, .56995152922585, -.35298115480305536 }, { -.3038456585649485, -2.434563490808578, .48822715256737514, .6961524950272909, -.5747892071452732, -.47529108928990654, -2.3269997637983875, .8357762390724675, 1.3161330770633626, .8096555133239125, -.49459120424796493, .7615253051865027, .7833497230864023, -1.0426998664844416, .7378898300718678, .2395343113717503 }, { -1.2978741869167922, .018950720679768464, .5695680787533325, .8392846276281338, -.17972183630963431, .5647090692234763, -1.4827152972854005, .03569475352452252, -.6165084301772299, -.8797256398967973, -.290806686481575, -.19783989978601416, 2.5394245196620866, -.08598454959886015, -.5504809218690371, .5172134421486481 }, { .6873568118795834, 1.2041539065261475, .020701186056471456, 1.6780748452053795, .5753189026726789, .3260073839152139, -1.3870933852684175, -.8587211141860908, 1.5586367483572017, -.8929838284775274, .38784025006027245, 1.6427973858847542, .44143408182933025, .2443545167634104, 1.4436759919263875, .663363962276222 }, { .9221154691195484, -.022751941139288454, 1.9840228762718204, 1.5316180102394514, -.6559137958411911, -.9624326301465556, -1.3357964115458059, -2.20883338385523, -.3384724544409632, 1.0754392207297883, .4199712760713532, -.9416100545548889, -.8408361507047493, -.797793667469001, 1.903799724105622, -.36978578500698767 }, { .171823189328345, 1.547732304645322, 1.7585803383944347, .18193883942209937, .6761424622850054, .31055489472843717, -1.9704326405872743, -.6137796248737636, -.07819334943857643, 1.6390307879433827, -.3821699555206435, .038368224590959, -.19685396730576935, .8562568511692237, .16298817197578824, .9205772293344064 }, { 1.5929301877906537, .5699141362298331, 1.1153250596807245, .6022002759402202, -1.3750267408538515, -1.1784258110674513, -.7965412023914076, -.25713070390970005, -.09651021028425776, -.3286284175685191, -.09867018059531778, .39993185947187004, -1.962611691579158, .4641533601192718, 1.8142315557963669, -.06123642692722676 }, { -1.4348865852353805, 1.0875406116940636, -.4787613269600391, .4077524508637602, .03426004544716492, -1.7697185450109163, -1.7774016031013475, .7751118214110505, .6425662434914667, -.29834157832977193, .7970005595822305, -.9130949858705291, 1.1654444803255233, -.14176836908065388, -.13444768593194234, .49212271993300516 }, { 1.2259208894684432, .06709349245902665, .37946388476319054, .9733357745801088, -1.8723390816633307, -1.7084195059429943, .49252979840805916, .30335570791206645, .8740463742531894, -.38310013494184725, .580797466222786, -1.0930984191754316, -.8172370603134087, -.6063513757070846, .8029922014478499, .6957402303104429 }, { -.8398203753243918, -.018234035765931765, -.24143884593928225, .3216249264240134, -1.6177838904536772, -.3902016313069648, -1.259504739280944, -2.176187231691338, 1.231267883644558, -.595838882123597, .11708316475727924, -.30655852689235386, 1.2698889888723526, .2808521401871574, .6394851744794399, 1.168488509611289 }, { -.6570606575730582, -2.682163511974322, -.3435560945085026, 1.3579882814731175, -.8877054976525004, -1.6673948818059954, 1.4391600286566375, .9820398125836646, -.16367026130584655, -1.4423146382002765, 1.223948274888786, .31879214134221856, .16992602798069834, .6729530717181226, .004930913301076135, 1.5062703549270957 }, { -.9247096926280011, -2.8851950266230193, -.543328700191975, -.8847818805725797, -.5314346664607286, -.9350529842910137, -.32752952857234496, .5070250719419901, -.21932294614416406, -3.0234276173465777, -.7927711479122168, -.27103117792235415, 2.795690916371692, 1.1694689690916358, -.24661678695815953, 3.003396823999669 }, { -1.6136480146386116, -.4272706684990928, 3.2395861473984295, 1.4191793206027161, 1.5009349950610953, -1.0506726622344484, .5744752271580478, -2.242069665408814, -.22148170690654354, -1.8438870467933968, .01570579805824173, 2.321640681772689, -.0900084815523468, -.2770347503510439, .2666328724807781, -1.4368125530998168 }, { -3.2474604475907305, -1.4494425591031588, 2.4479652350253884, .6904526706473897, .9036397872166504, .46019359311486513, 1.3990867195163361, -2.4486676212720933, -.6907632666229694, -.3433677893972021, .11222369324311599, 3.113004117352814, -1.0409870817638511, .31819424157880866, 1.7826244806131648, -.3964282001320457 }, { -1.1921170774528485, -1.1343507876858425, 1.855437016651265, -.3671286907852153, -.022778968446332794, -1.6636869803134533, -.509354130317308, -4.565246111007281, 1.8486191996485333, -.7335009972956043, -.9278320155942071, .6555942002404006, -1.3348256928021807, 3.034984795460308, .7133513933984889, -.2442476995517764 }, { 1.9072202973090844, 3.090979166708729, 1.0356900954980688, .8469836781874454, .9710557522730852, -.6417261666274648, .14655221047655936, -.862827301098923, 1.5601005456028831, 1.1889031272359145, -.36511358671940325, -.37494518017566836, -1.9710364861862064, -2.096473239382409, 1.7968896342793024, .6037449012734377 }, { -.2685273270180898, -.3946010018597174, 1.2899639271373888, -1.0033633206497654, -.30570200209314186, -.6556027787936465, -.08645159405721253, -.18949568648966128, -.4411456701324549, -2.01903390259422, -.9195909004350709, -.9470083351677754, -.46404281098206207, .5287005318104588, -.6075110893699984, 1.2343497493677342 }, { .47359617148524213, .7331118362378816, -2.4074833488172556, .8014568629466999, .7720685330855943, .3556582613743423, -1.2334775101657292, .025957989997911005, .2990096098634048, -1.216107698753674, -1.4642049063376203, -.8002871164580301, -.07752195221285453, -1.9942731174269057, 1.528246111629325, 1.4304723822872134 }, { .04240516151530995, -1.7878153535086645, -.8367828708676617, -.5445067825302587, -.03240474134646585, -.3800296793209308, 2.0937123957318136, -.6395074815309159, -1.2975415038215856, -4.671940561697326, .9631517413588678, -.07019215903146656, 2.845577956762917, .8653192079288233, .49046444919994286, 1.1013319202667646 }, { -1.1575306634857072, -1.044923915465345, -.4113394437026934, -.24841221995602047, .40434148714814, -.5996742010707526, -.00959341528487487, -1.665240480576657, -1.4783912012250364, -5.907969523831466, -.7079323739266745, 1.24268565349595, 2.1901244591350886, 2.820809971286604, .29914419286402105, .8665734769768821 } };
        public int NNUEEM_EVALUATION()
        {
            return TexelEvaluate();
            //return NNUE_EVALUATION();
            // QQQQ RRRR BBBB NNNN PPPP 0000
            //switch (countOfPiecesHash >> 8)
            //{
            //    //case 0: return NNUE_EVALUATION();
            //    default: return TexelEvaluate();
            //}
        }

        private int NNUE_EVALUATION()
        {
            double A0 = EvNNF1(NNUE_FIRST_LAYER_VALUES[0]), A1 = EvNNF1(NNUE_FIRST_LAYER_VALUES[1]), A2 = EvNNF1(NNUE_FIRST_LAYER_VALUES[2]), A3 = EvNNF1(NNUE_FIRST_LAYER_VALUES[3]), A4 = EvNNF1(NNUE_FIRST_LAYER_VALUES[4]), A5 = EvNNF1(NNUE_FIRST_LAYER_VALUES[5]), A6 = EvNNF1(NNUE_FIRST_LAYER_VALUES[6]), A7 = EvNNF1(NNUE_FIRST_LAYER_VALUES[7]), A8 = EvNNF1(NNUE_FIRST_LAYER_VALUES[8]), A9 = EvNNF1(NNUE_FIRST_LAYER_VALUES[9]), A10 = EvNNF1(NNUE_FIRST_LAYER_VALUES[10]), A11 = EvNNF1(NNUE_FIRST_LAYER_VALUES[11]), A12 = EvNNF1(NNUE_FIRST_LAYER_VALUES[12]), A13 = EvNNF1(NNUE_FIRST_LAYER_VALUES[13]), A14 = EvNNF1(NNUE_FIRST_LAYER_VALUES[14]), A15 = EvNNF1(NNUE_FIRST_LAYER_VALUES[15]);
            double B0 = EvNNF1(-5.379196581936967 * A0 - 2.7197930020585055 * A1 - .1536370227088859 * A2 - .3991702641467055 * A3 - .0018994295599151286 * A4 - 1.0476611186488485 * A5 + .4010218965034443 * A6 + 1.3216198759478706 * A7 - .40779271684956414 * A8 - 5.454963650150679 * A9 + .9183022999838866 * A10 - 6.340606316345919 * A11 - 3.0387651722017566 * A12 - 1.151295439064527 * A13 + 6.22924879049796 * A14 + 1.0725151497552188 * A15 - 4.229081180280141);
            double B1 = EvNNF1(-1.937552377814588 * A0 + .45285634625291304 * A1 - .18729009836354846 * A2 - 1.8784395574945594 * A3 - .03837293157443252 * A4 - .30983015914935513 * A5 - 1.4558190859206395 * A6 - .9255608700684023 * A7 + .5146135865480584 * A8 - 1.8947184793928773 * A9 + .9519240710877752 * A10 - 1.9582349918489232 * A11 - 1.5295650683578024 * A12 - .5867077244650545 * A13 - 1.0982626050931914 * A14 + 2.3013852264649497 * A15 - 1.4046363188910747);
            double B2 = EvNNF1(.12777762543648205 * A0 - 1.8644012450997136 * A1 - .9647230116320269 * A2 + .8758817595755352 * A3 - .25783613864871874 * A4 + .19995339419372524 * A5 - .4910763812244523 * A6 + .9194043841458968 * A7 + 1.1355586059763478 * A8 - 2.0456921176781986 * A9 + 1.9715481947791793 * A10 - .47537700170941827 * A11 - 1.5187530371797033 * A12 - .513156375033053 * A13 - 2.3607546884867925 * A14 - .8544460449258215 * A15 - 1.4904251607674799);
            double B3 = EvNNF1(-.8742214245833698 * A0 - .3670180476681265 * A1 + .36303063189809043 * A2 - 3.235436849316848 * A3 + .2687262945815173 * A4 + .07359415939168445 * A5 - 2.09378322800097 * A6 + .6001265890221327 * A7 + .09404477970992395 * A8 - 1.492771652321367 * A9 + .5985378907589094 * A10 - .35524627827610816 * A11 - 1.498482484153564 * A12 - 1.9625961726066086 * A13 - .7135583174403635 * A14 - .27071298391713544 * A15 - 2.0010858818146016);
            double B4 = EvNNF1(-1.4506564297094036 * A0 - .12116699251505308 * A1 + .24161419648453902 * A2 - 1.1617297347857214 * A3 + .7004414604805399 * A4 - .35464719035879066 * A5 - 2.552384164226463 * A6 - .2893450737276878 * A7 - .017606156908934297 * A8 - 1.653047214595185 * A9 + 1.2794736554217043 * A10 - 1.7697273206912374 * A11 - 1.34405244488911 * A12 - 1.5175092114061972 * A13 + .8415845267026686 * A14 + .17235567320925546 * A15 - 1.0476321257815564);
            double B5 = EvNNF1(6.172132916804314 * A0 - 5.930016115103723 * A1 - 5.26364903793775 * A2 - 8.198217823822892 * A3 - 3.972272432783657 * A4 - 2.6073613412476844 * A5 + .6968424186330576 * A6 - 4.40185610680129 * A7 - 3.818564558639051 * A8 + 6.383905249209342 * A9 + 2.509883176798365 * A10 + 7.414598383063825 * A11 + 3.7445624813301754 * A12 + 3.3870863682788768 * A13 - 10.337451788693699 * A14 - 6.170344476285267 * A15 + 1.4996015618681497);
            double B6 = EvNNF1(7.966031802740372 * A0 - 6.1639124886997685 * A1 - 8.806305961208096 * A2 - 7.183648136352042 * A3 - 6.555816054975801 * A4 - 5.958315684028808 * A5 + 5.165028536531874 * A6 - 6.871223539618655 * A7 - 6.149482322840045 * A8 + 9.90107091310023 * A9 - 1.7778117133075786 * A10 + 9.34712186078229 * A11 + 5.922503368168839 * A12 + 5.588701522482703 * A13 - 15.06468331896599 * A14 - 9.295523847981736 * A15 + 4.845261014880136);
            double B7 = EvNNF1(1.4584668458988432 * A0 - 2.3617142524473613 * A1 - 5.370980480033742 * A2 - .44797148014839777 * A3 - 2.531744717894515 * A4 - 2.326558379977632 * A5 - .9567602940229447 * A6 - 4.1797557008849004 * A7 - 2.8825293362182816 * A8 + 2.0845812415326495 * A9 - 3.5471239052447507 * A10 + .3231777385130409 * A11 + .7417226753668632 * A12 + .3654447409339733 * A13 - 7.346895805175774 * A14 - 5.145436194225868 * A15 - .3900498165578311);
            double B8 = EvNNF1(-.08102909820890863 * A0 - 1.7283894355407086 * A1 + .4247374197502872 * A2 - 2.0378790433692795 * A3 + .29104938730506597 * A4 - .032566526374418314 * A5 - 2.0398364329997696 * A6 + .08237533991803511 * A7 - .49747259546145933 * A8 - .2867999041518861 * A9 + .48014984033702646 * A10 - .3772706313248798 * A11 - .5762590759808343 * A12 - 1.761369296150081 * A13 - .08819361152923581 * A14 + .07342850760096245 * A15 - 1.7951939744491758);
            double B9 = EvNNF1(-1.6348054726912122 * A0 - .35274073931085137 * A1 + .49920869855237837 * A2 - 1.9908910251408987 * A3 + .21330051231021635 * A4 - .22859169531430193 * A5 - 1.898274731219437 * A6 - .23244419514570594 * A7 + .46065007843264527 * A8 - 2.2574756103508773 * A9 + 1.3382708714474894 * A10 - 1.0448978826644306 * A11 - 1.8623269020536437 * A12 - 1.3213694022623215 * A13 - .16840642693285854 * A14 - .0014675019586593563 * A15 - .9689579071856269);
            double B10 = EvNNF1(-1.4694271798125351 * A0 + .6206249200028797 * A1 - .9439856521577712 * A2 - 2.5852132144793845 * A3 - 1.165492677521744 * A4 - .19734938763804302 * A5 - 3.452806507366695 * A6 - .3306542418741879 * A7 + .21691956827183154 * A8 - .3307247178632865 * A9 + 1.4463882575830604 * A10 - 2.9644674266233406 * A11 - .7823264004660576 * A12 - .6461389696597876 * A13 + 4.339347251941747 * A14 - 1.3697862348869376 * A15 - 1.3995341346086514);
            double B11 = EvNNF1(-.8382899884304036 * A0 - .5209721002318812 * A1 + .06260555860279807 * A2 - 4.211859004188986 * A3 + .41552239495420695 * A4 - .24172440018673025 * A5 - 2.7458953811434212 * A6 + .14009086893577394 * A7 + .5856607090635642 * A8 - 1.985061667767917 * A9 + 2.075145128973317 * A10 - .6187238313197733 * A11 - 1.3064562543847678 * A12 - 1.8047843377031008 * A13 - .7213998341913281 * A14 - .004062303328803689 * A15 - .9494612036449357);
            double B12 = EvNNF1(-.5300253263486513 * A0 + .9814899486955628 * A1 - 1.3904292660009276 * A2 - 1.996983691965933 * A3 - .7540499716413547 * A4 + .6640474026378215 * A5 - 1.063378546409107 * A6 + .4449078356723158 * A7 + .8406471572213112 * A8 - .30254432957424277 * A9 + .1955025667874136 * A10 - 1.500558813277387 * A11 - .3564139787068432 * A12 - .31692939336089737 * A13 - .4460587765801037 * A14 - .19985914015595066 * A15 - .36285251498242793);
            double B13 = EvNNF1(-.1454442404525312 * A0 - .6485498767021687 * A1 + .15645914603467223 * A2 - 3.8680222970129567 * A3 + .34112662266829064 * A4 - .12897260261734514 * A5 - 1.9210996677454384 * A6 + .17037078040576503 * A7 - .27815364002483606 * A8 - 1.1218722181074232 * A9 + .6475231173850226 * A10 - .11480270724731716 * A11 - .7468412937254765 * A12 - .6918453052830961 * A13 - .3707384736754091 * A14 + .04524767941735054 * A15 - 2.1559392796732513);
            double B14 = EvNNF1(.912767072672037 * A0 - .4737917166045487 * A1 - 1.4683576104243692 * A2 - .7018733633583432 * A3 - 1.1911617785784394 * A4 - .41436588758720844 * A5 + .497101094780198 * A6 - 1.5696462256386152 * A7 - .9994394995627885 * A8 + 1.267250480040635 * A9 - 1.5103326241385027 * A10 + .8659185806660029 * A11 + .4850610836003039 * A12 + .6764086955185907 * A13 - 3.312831221423216 * A14 - 2.080479008355596 * A15 - 1.3794356634139249);
            double B15 = EvNNF1(2.1204052641408646 * A0 - 1.7796917301894761 * A1 - 3.1190075862161253 * A2 - 1.8892759173595481 * A3 - 1.5009023504647252 * A4 - 1.3994779769523553 * A5 - .31926443424179596 * A6 - 3.1054190811008158 * A7 - 2.5247508264016307 * A8 + 2.798991768063147 * A9 - 2.5463441769587503 * A10 + 1.9269124596097114 * A11 + 1.086015704819525 * A12 + .09271255299208145 * A13 - 7.2431583038858305 * A14 - 3.837878086930099 * A15 - 2.1748875138015498);
            double C0 = EvNNF1(-5.051664761506432 * B0 - .8519229963319006 * B1 - .33380039406447826 * B2 + 1.4229104621386155 * B3 - 1.0740261426972675 * B4 - .7702902232458454 * B5 - .38761462150702913 * B6 + 1.8196829587849161 * B7 + 2.6807734145585536 * B8 + .2191992377071596 * B9 - .1156743108838606 * B10 + 1.181790684586284 * B11 - .7315676883032519 * B12 + 2.7149846631919563 * B13 + .2647524125244601 * B14 + .7058743166228121 * B15 - .35264201795346395);
            double C1 = EvNNF1(-4.823892478435949 * B0 - .99990138129881 * B1 - .8682958984683911 * B2 + .5865516255511535 * B3 + .4185471619044457 * B4 - .7052640001542575 * B5 - .35788140040376293 * B6 + 1.515094348703463 * B7 + 2.1756568533217746 * B8 - .6513191288062947 * B9 + .33212629186246373 * B10 + 1.6667196451025068 * B11 - .9186863487683141 * B12 + 1.8456125077013252 * B13 - .08369974939305605 * B14 + 1.4065736175403458 * B15 - .6119532716990788);
            double C2 = EvNNF1(-1.1554875900814012 * B0 + .213988842582182 * B1 - .6482726734554164 * B2 + .790150567078156 * B3 + .19704581879436134 * B4 + .2240832344516216 * B5 - 1.8022693284914495 * B6 - 1.39739066971894 * B7 + .4152970221537939 * B8 + .27604634807325684 * B9 + .0808080445889928 * B10 + 1.349380048767054 * B11 + .9235070852171426 * B12 + 1.3242444117128722 * B13 + .6324817631804983 * B14 - .5083164432232683 * B15 - 1.253336344402429);
            double C3 = EvNNF1(-.7767910843976045 * B0 - .44336225594445283 * B1 + .4826565084809082 * B2 + 2.659875851051162 * B3 + 2.108501084745411 * B4 - 3.9536950483492155 * B5 - 2.120455219153575 * B6 + 2.757382239984502 * B7 + 2.0137415977492163 * B8 + 2.186895172979807 * B9 - .28531364502589035 * B10 + 3.7941516247968217 * B11 - .09256994222884582 * B12 + 3.0403615832164075 * B13 + 3.0689066469318322 * B14 + 6.805231449304693 * B15 - 5.707238843934238);
            double C4 = EvNNF1(-2.600515040259766 * B0 + .5232673618897014 * B1 - .5457855036255437 * B2 + 2.372568954642492 * B3 + 1.8178346265580687 * B4 - .4385450969756347 * B5 - 2.412520642667185 * B6 - 6.4105854941311184 * B7 + 1.501997242349014 * B8 + 2.2566062737391492 * B9 + 1.820891052369734 * B10 + 2.9599969056988846 * B11 - .22625441727240944 * B12 + 3.271520273418547 * B13 - 1.2090056725515348 * B14 - 1.0184832641826003 * B15 - 2.8342488300007935);
            double C5 = EvNNF1(-1.3667525534355964 * B0 + .15114984659448077 * B1 - .5472100204383856 * B2 + .3215392068669389 * B3 - .625751886128519 * B4 - .9793580577762498 * B5 - .5176725183643217 * B6 + 3.7070483944665855 * B7 + .1025825840279636 * B8 + .11905789796263513 * B9 - .3843524404052495 * B10 + .6778689455046336 * B11 - .3140040251253766 * B12 - .03767363167585171 * B13 + 1.5722905749236893 * B14 + 2.9285860673075237 * B15 - .29845552030914585);
            double C6 = EvNNF1(-1.4086311131007878 * B0 - .07399073973855294 * B1 + .1461731538095088 * B2 - .30528776307475564 * B3 + .6276974096046005 * B4 - 1.471128959505705 * B5 - .44650786211730353 * B6 + 4.445950901435683 * B7 + .4473899836152035 * B8 + .26120719939909715 * B9 + .048235896415798216 * B10 - 1.4952587238281656 * B11 - .24086524862294348 * B12 - .5635378562613401 * B13 + 1.5471191867859964 * B14 + 3.5928109946658484 * B15 - .39090108436411913);
            double C7 = EvNNF1(-1.2832073459668607 * B0 - .14080595519102734 * B1 + .6721061230344652 * B2 + 1.4542844653559897 * B3 + .6035308908336647 * B4 - 3.377300961707254 * B5 - 3.5096399458785155 * B6 - 2.2270845697954154 * B7 + 1.2710142238385285 * B8 + .7765839154086281 * B9 + .7149016948224203 * B10 + 1.6709548682378168 * B11 - .21309996062006126 * B12 + .40943800548623754 * B13 - .9486451372827387 * B14 - 2.074974777544496 * B15 - 3.3602623677323344);
            return EvNNF2(-1.2715609967957107 * C0 - 1.0809417697470804 * C1 + 1.0238346962932767 * C2 + 4.700240853229001 * C3 + 2.571396249896218 * C4 + .10133950751015566 * C5 + .2564558327119683 * C6 + 2.5594689470243015 * C7 + .23195987092465867);
        }

        private double EvNNF1(double val)
        {
            if (val < 0d) return 0.04d * val;
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
            for (int i = 0; i < SIZE_OF_FIRST_LAYER_NNUE; i++)
            {
                NNUE_FIRST_LAYER_VALUES[i] += NNUE_FIRST_LAYER_WEIGHTS[pNNUE_Inp, i];
            }
        }

        private void UPDATE_NNUE_FIRST_HIDDEN_LAYER_OFF(int pNNUE_Inp)
        {
            for (int i = 0; i < SIZE_OF_FIRST_LAYER_NNUE; i++)
            {
                NNUE_FIRST_LAYER_VALUES[i] -= NNUE_FIRST_LAYER_WEIGHTS[pNNUE_Inp, i];
            }
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
