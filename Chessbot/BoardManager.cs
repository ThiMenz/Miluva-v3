﻿using System.Diagnostics;

#pragma warning disable CS8618
#pragma warning disable CS8602
#pragma warning disable CS8600
#pragma warning disable CS8622

namespace ChessBot
{
    public interface IBoardManager
    {
        int TEXEL_PARAMS
        {
            get;
        }

        void SetJumpState();
        void LoadJumpState();
        void GetLegalMoves(ref List<Move> pMoveList);
        void LoadFenString(string pFen);
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
        private const int KILLERMOVE_SORT_VAL = -1000;

        private readonly int[,] MVVLVA_TABLE = new int[7, 7] {
            { 0, 0, 0, 0, 0, 0, 0 },  // Nichts
            { 0, 1500, 1400, 1300, 1200, 1100, 1000 },  // Bauern
            { 0, 3500, 3400, 3300, 3200, 3100, 3000 },  // Springer
            { 0, 4500, 4400, 4300, 4200, 4100, 4000 },  // Läufer
            { 0, 5500, 5400, 5300, 5200, 5100, 5000 },  // Türme
            { 0, 9500, 9400, 9300, 9200, 9100, 9000 },  // Dame
            { 0, 0, 0, 0, 0, 0, 0 }}; // König

        #endregion

        #region | VARIABLES |

        private int BOARD_MANAGER_ID = -1;

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


        private List<int> moveHashList = new List<int>();
        private ulong[] curSearchZobristKeyLine = Array.Empty<ulong>(); //Die History muss bis zum letzten Capture, PawnMove, der letzten Rochade gehen oder dem Spielbeginn gehen

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
            //int[] ttt = new int[1152] { 0, 0, 0, 0, 0, 0, 0, 0, 1, -1, -9, -10, -7, 0, 4, 4, 1, 3, -2, 1, -5, 0, 3, 4, -1, 1, -4, 0, 0, -2, 1, 3, -4, 3, 7, 3, -4, 6, -3, -10, -3, 0, 14, 21, 3, 108, -15, -8, 11, -26, 93, 38, -24, 74, 34, 54, 0, 0, 0, 0, 0, 0, 0, 0, 3, 4, 7, -3, -4, 6, -2, 28, -6, -3, -6, 5, -2, 0, 4, 1, 0, -9, 0, -3, -8, -2, 5, -10, -5, 7, 12, -3, 7, -7, -17, -5, -4, 7, -11, 7, 6, -8, 1, -18, -8, -10, 26, 4, -1, 4, 7, 25, 82, 64, 19, -37, -11, 12, 23, 101, -8, -22, -51, 27, -111, 298, 20, 22, 3, 6, 0, -3, -2, 0, -2, -8, 3, 5, -3, 5, -4, 17, -3, 15, 2, -4, 7, -1, 2, 3, -3, 7, -8, 12, -3, 4, -1, -3, -1, -2, 8, -8, -4, -2, -1, 71, -3, 9, 11, 30, 12, 7, 53, 19, -3, 3, 45, 24, -35, 16, 48, -13, 39, -15, 30, -112, 3, 366, -233, 240, 174, 35, 2, -4, 1, 0, -2, 0, -7, 0, -3, 9, -6, 1, 2, -8, 21, -6, -13, 6, 12, -5, 5, -2, 31, -6, 1, -8, 12, -11, -8, -2, -4, 21, 3, 34, 3, -16, 16, 6, -21, 38, 1, -28, 4, -26, 18, 16, -4, -3, 24, 12, 23, -8, 28, -5, 37, 14, -2, 6, 38, -50, -11, -22, 31, -29, -5, 2, 8, 0, -3, -5, 8, 30, -6, -8, -1, -1, -4, 6, 32, -2, -9, 4, 4, -1, 12, -9, 0, 14, 1, 6, 8, 15, 4, -12, -1, 1, -16, -4, -9, -1, 28, -26, 13, 14, -25, -8, -34, 5, -16, -33, 1, 64, -166, -7, 17, -10, -15, -4, 154, -44, -71, -67, -8, -47, 33, -86, 5, -152, 0, 1, -6, 10, 2, 7, -2, 1, 15, 2, 6, -10, 0, 0, 11, -4, -6, 21, -11, -10, -6, 9, -7, 10, -120, 72, 93, 49, 53, -30, -1, 32, 100, 48, -28, 4, -53, 36, 82, -212, -14, 11, 101, 12, -50, -6, 12, 36, 8, 12, 4, 0, 112, -161, -17, 0, 0, -8, 0, 8, -4, 4, 4, 4, 0, 0, 0, 0, 0, 0, 0, 0, -6, 4, 2, -18, 8, 1, -1, 4, -10, 1, 1, 4, 1, 3, -2, 1, -2, 0, 0, 4, -2, -3, 3, 1, 10, 3, -9, 10, 1, -9, 0, 0, 46, -1, -6, -2, -20, -79, -33, -30, 19, -17, -37, 4, 0, 59, 4, 130, 0, 0, 0, 0, 0, 0, 0, 0, 5, -1, -11, -12, -2, 0, -1, -8, -14, -15, -8, -3, 4, -2, -6, -3, 10, 3, -1, -4, 11, 1, 3, 8, 10, -6, -9, 5, 0, 4, 15, 0, -12, 0, 16, 5, 0, 0, -3, -20, 9, 16, -12, -10, 0, 60, 11, 38, 5, 1, 12, -2, -24, 0, 24, 8, -24, 7, 0, 4, -118, -20, -104, 0, -5, 0, 3, -4, -4, -2, 24, 5, 3, 1, -4, 3, -2, -4, -4, -12, 10, 0, 0, -4, 3, -6, -3, -4, -6, 0, 4, 1, -10, -1, 13, -2, 6, -2, 7, -2, -4, -12, 10, 0, 0, -2, 22, -1, -8, 19, 9, 10, -18, 9, 8, 52, 35, 120, 0, -2, 62, 7, 20, 57, 12, 122, -27, 9, 1, 4, 0, -5, -2, -2, 8, 1, 0, 0, 0, -5, 3, 2, 1, 3, -7, -3, 4, -3, 7, 4, 0, 0, -10, -12, 9, -6, 0, 4, -4, 4, 4, 14, -3, 31, 1, 9, 5, -8, -17, -4, 11, -1, 2, -6, -14, -2, 6, 9, 0, 0, 10, 8, 4, -1, 17, -8, -8, 4, -6, 1, 9, -90, 0, 0, -8, -1, 0, -4, -10, -6, 5, 4, 4, 0, -2, -8, -64, 15, 2, -3, -1, 4, 0, -4, -8, 4, -5, -2, -7, 4, -2, 4, 5, 2, -3, -4, -8, 4, 1, 10, 7, 32, 4, 10, 7, -2, -11, 43, -5, -4, 36, 2, 20, 13, 1, 8, 6, -39, 3, 5, 41, -10, -34, 67, -5, -12, 7, -8, 6, -3, 0, 0, -1, 0, -9, -6, 0, 1, -3, 4, 6, -3, 20, -31, -9, 7, -2, 5, 1, -4, -28, 6, -7, 14, 16, 64, 7, -12, 12, 28, 73, 1, -14, 16, -84, -33, -201, 9, -6, 16, -79, 41, 31, 19, -16, 98, 4, -52, 163, -39, 32, 87, -673, -148, -121, -11, 282, 30, 33, 12, 0, 0, 0, 0, 0, 0, 0, 0, -1, 2, 0, 0, 0, 5, 0, 6, -16, -1, 4, 0, 1, 0, 0, 4, -12, 0, 0, 5, 1, -6, 6, 0, 34, 7, -8, 0, 3, -31, 6, 5, 14, 120, 25, -58, 18, 0, 7, -1, 76, -48, -206, -4, -17, 16, -56, -24, 0, 0, 0, 0, 0, 0, 0, 0, 35, 0, -25, -4, -4, -49, 0, -6, -6, -45, 0, -1, 2, -14, -1, 1, 0, -7, 0, 0, -16, -2, 1, 0, -2, -6, -44, 8, 0, 35, 9, 0, 32, -4, 4, 12, -1, 0, -9, -1, 0, 2, -20, 0, 6, -3, 0, 1, -6, 6, 1, -60, -2, 2, 10, -24, -1, -19, 0, 0, -36, -26, -52, 132, 0, 0, -1, -30, 3, 1, 0, 0, 3, -6, -9, 1, 8, 1, 4, -13, 1, 0, -1, -7, 3, -6, 7, -3, -22, 1, -1, 0, 9, 4, 4, 0, 0, 0, 5, 7, 0, -1, 5, 0, 11, -34, 41, 0, 0, 26, 5, 22, -4, 2, 2, 0, -1, -32, 21, 0, -14, 0, 0, -31, 54, 96, -72, 0, 5, 3, 0, 1, 4, 1, 0, 5, -3, -1, -1, 0, 14, -4, -2, -2, 6, 1, 5, 0, 1, 8, 2, 0, -36, -20, -4, -16, 4, -4, -6, -1, 0, 3, 8, 6, 5, -1, 4, -1, 20, 2, 4, 21, -13, 8, 12, 1, -2, 5, 0, -8, -33, 3, -4, -31, -31, -26, -26, -8, 1, 0, 62, 96, -1, 0, 6, 0, 3, 3, -8, -1, 2, 3, 4, 1, 4, -6, 0, 4, -4, -5, 0, 4, 1, 4, 0, 5, 4, 2, -52, -2, 2, -1, 6, -4, 6, -3, 0, 2, 0, 6, 0, -1, -2, 2, -6, 3, 4, 0, -2, 1, 0, 0, -3, 32, 0, 1, 1, 4, 30, 7, 32, 146, -22, 104, 0, 5, -52, 4, 1, 0, -1, -1, 4, -2, -2, -35, -24, 16, -7, 8, -4, -6, 0, -24, -1, 3, -8, 0, -4, 3, -68, 73, -23, -18, -8, 0, -11, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            //int[] ttt = new int[1152] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 2, -1, 1, 2, 0, 0, 1, 2, 3, -1, 0, 0, 0, 0, 1, 4, -3, -11, -2, 1, -2, 2, -8, 1, -10, 3, 4, -6, -10, -1, 15, 33, -4, -18, -16, 17, -72, 47, 8, -10, -62, 51, -111, 134, -78, 60, 0, 0, 0, 0, 0, 0, 0, 0, 14, 0, -29, -10, 2, -1, 0, -19, 1, -5, -13, -3, 2, -1, -16, -5, 1, -1, 1, 6, -1, 0, 2, 2, -4, -16, -13, -9, -11, -16, 2, -11, -32, 0, -1, -6, -14, -25, 1, 10, -5, -9, -1, 1, -7, 1, 3, -14, -10, -69, -6, 92, 22, -5, 6, 17, 43, -133, -83, -71, -20, -26, -104, -87, 4, -4, -1, -7, -8, 0, -13, -8, -7, -12, -2, -1, 0, 2, -4, -14, -3, 0, -10, -7, -11, -3, -11, -3, -16, -1, 3, 1, -6, -1, -2, 8, -3, -2, -3, 14, -17, 11, -2, -5, 0, -16, 5, -18, 21, -2, 1, 2, 1, 9, 112, 105, 14, 20, -48, 12, 114, -16, 29, -10, -66, 4, -21, -18, 0, 0, -12, 2, -19, -15, -3, 0, -1, -15, -16, -16, -14, -4, -8, 1, -12, 4, -21, -4, -16, -17, -14, -11, -16, -8, 2, -14, 1, -11, 29, -1, -13, -14, -1, 21, -4, 96, -25, 5, -10, -11, 94, -42, 59, 38, 4, -7, -14, -48, -31, -89, -38, 101, 6, 7, -75, -14, -31, -86, 18, -79, -7, -7, -26, -6, -2, -2, -16, -12, -9, -37, -7, 4, -12, -1, -4, -5, -8, -6, -3, -1, -2, -19, -3, -4, -5, -17, -2, -12, -8, -3, -12, -7, -13, -15, -10, -8, 2, -11, 0, 29, -20, -11, -53, 2, -30, 0, -22, -10, -10, 23, -5, -6, -6, -28, -72, -1, -98, 4, -32, 10, -116, 3, -1, 22, 54, -114, 6, 18, -20, -2, 0, 2, -8, -28, 104, -25, -10, -2, -16, 0, -4, -9, -118, -125, 1, -11, -8, -14, -2, -16, 24, 112, -23, -26, 5, 0, -2, -15, 132, 117, 136, -1, -104, 77, -14, 107, 128, 122, -78, 133, 169, 61, 126, 134, 31, -132, -131, 130, 155, 88, 116, 142, -128, 182, -159, 2, 151, 43, -133, -163, 0, 0, 0, 0, 0, 0, 0, 0, -16, 0, 0, -1, 1, 1, 0, 0, -16, 0, 2, -1, 0, 0, 16, 0, -16, -12, 12, -14, -1, 0, -2, -15, -29, -17, 3, 17, 3, -12, -14, 15, -84, 18, 26, 10, 15, 18, 33, 32, 11, -73, 46, 15, -115, 132, -71, 142, 0, 0, 0, 0, 0, 0, 0, 0, 1, -17, -54, -23, 4, 23, 0, -51, -13, -2, -14, -3, 0, 0, -18, 14, -15, 0, -16, 6, 0, 0, 5, 0, 32, -17, -16, -28, -11, 0, 12, 5, -118, -17, 48, -8, 1, -62, 2, 59, 6, -59, -2, -1, 6, -36, 59, -2, -18, 120, 10, 24, -58, 11, 21, 51, 20, -154, -4, 18, 96, 115, -96, 89, -1, -37, -17, 8, -24, 0, -21, -4, -117, -31, 15, -16, 0, -13, 13, -82, -17, 34, -14, -11, -31, 15, -17, 16, -39, -1, 4, 5, 8, -16, 11, 5, -15, 0, -52, 30, -17, 76, -16, 6, 81, -25, 28, -31, 90, -65, 32, 74, 53, -53, 3, 107, -70, 30, -27, 12, 65, -83, 19, -112, 55, 107, 7, -4, -16, 0, -13, 2, -4, 2, -3, 0, -17, -16, 2, 16, -14, -2, 6, 0, -32, 0, -53, 15, -16, -36, 0, -13, -14, -5, -1, -32, 14, 4, 52, 14, -14, 2, -20, 56, -29, 92, -11, 23, -60, 19, -97, -41, -9, 102, 22, -24, -46, -17, -13, -111, -19, 102, 3, 38, -106, 1, -16, -72, 18, 81, -16, 6, -27, -18, -16, -17, -16, -10, -40, -33, -38, 5, -13, -16, -4, -4, -4, -72, 14, -1, -2, -34, -3, -3, -21, -19, -18, -32, -12, -17, -29, -42, -31, -30, -27, -16, 0, -16, 1, 30, -18, 9, -98, -13, -30, 66, -81, -90, 9, 54, 21, 5, -56, -47, -57, -5, -88, 17, -91, 11, -103, 100, 3, 92, 108, -107, 54, 19, -10, 0, -16, -14, -26, -59, 35, -13, -13, -17, -16, -17, -3, -23, 84, -112, 19, 0, -27, 0, 14, 0, 103, 96, 71, -108, 37, -3, -2, 36, 67, 80, 23, -13, -27, 17, 23, 74, 80, 76, 1, 115, 152, 99, 41, 63, 14, -82, -18, 113, 143, -56, 99, 114, -112, 155, -131, 75, 135, -49, -134, -168, 0, 0, 0, 0, 0, 0, 0, 0, -16, 16, -48, -49, 16, 0, 16, -32, -16, -33, -111, -16, -97, -16, 80, 15, -17, -60, 13, -112, 16, 46, -50, -64, -64, -84, -64, 111, 0, 31, -48, 47, -85, 119, -55, 37, 40, 65, 101, 99, -70, -112, 26, -72, -110, 156, -64, 78, 0, 0, 0, 0, 0, 0, 0, 0, 3, -113, -121, -68, -76, 111, -112, -12, -40, -57, -111, -3, -50, 2, -5, -16, -31, 32, 16, -6, -78, -113, -74, -48, 116, -34, -32, -65, -109, 29, -24, -13, 1, -52, -73, -73, 81, -104, -15, 111, -106, -112, 30, -5, 83, -122, 114, -20, 6, 7, 58, 105, -112, 41, 102, 102, 91, -143, 29, 82, 103, 121, -111, 106, 10, -34, -80, 22, -60, -96, 26, -114, -111, -112, -16, -64, -48, -73, -1, 5, -64, 116, -64, -94, -47, 48, -5, -32, -48, -32, -111, 3, 116, -32, -12, 87, -80, -18, -18, 113, -99, 125, -112, -111, 86, -115, 96, -96, 120, 43, 91, 106, 57, -32, 6, -84, -125, 55, 21, -33, 114, -110, -100, -99, -56, 112, 66, -3, -64, -48, -80, -29, -6, 16, -34, -64, -48, -16, 113, 78, 19, -48, -27, -16, -66, 31, -120, 67, 16, -57, 14, -96, 0, -17, -68, -100, 80, 96, 121, 78, -14, 96, -25, 120, -16, 108, -18, 44, -106, 81, -80, -103, -105, 115, 57, 102, -16, 24, -58, -114, 0, 116, 60, 82, -105, 32, -17, -105, 94, 109, -106, -13, -113, -96, -112, -97, -112, -23, -105, -44, -101, -23, -110, -95, -97, -113, -31, -118, 0, -112, -3, -113, -18, -64, -114, -47, -113, -102, 18, -113, -96, -110, -49, -48, -120, -96, -18, -69, 16, 117, 11, -3, -119, -41, -59, 115, 33, -106, -116, 114, -54, 37, -77, -112, -91, -28, -107, -112, -4, 114, -123, 114, -8, 111, 121, -82, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            //int[] ttt = new int[1152] { 0, 0, 0, 0, 0, 0, 0, 0, -13, 14, 12, -12, -4, 22, 6, 11, -8, 21, 23, -6, 2, 25, 0, 14, -11, 32, 10, -2, 1, 23, 7, 20, 13, 34, 62, 0, 12, 50, 28, 25, 108, 199, 0, -18, 116, 164, -16, 128, 215, 99, -139, -51, 453, 584, 234, 271, 0, 0, 0, 0, 0, 0, 0, 0, 143, 22, 88, 66, 62, 13, 18, 22, 71, 39, 55, 35, 32, 42, 52, 29, 29, 35, 28, 60, 63, 22, 53, 20, 34, 43, 49, 43, 39, 31, 19, 43, 51, 29, 117, 28, 39, 45, 31, 42, 164, 98, 153, 53, 104, 127, 17, 170, 25, 29, 18, 161, 131, 32, -26, 143, 81, -47, -222, 198, -412, 236, 74, -55, 86, 60, 20, 60, 65, 17, 58, 80, 108, 32, 47, 37, 31, 70, 19, 82, 30, 34, 42, 36, 42, 33, 37, 22, 23, 39, 29, 57, 43, 34, 59, 41, 79, 24, 107, 19, 65, 75, 34, 59, 57, 49, 39, 62, 33, 274, 90, 3, 15, 129, 331, -97, 153, 130, 28, 300, 187, 74, 89, 180, -68, -42, -105, 30, 23, 27, 46, 60, 53, 44, 29, 17, 33, 44, 46, 60, 94, 48, 43, 29, 33, 40, 80, 45, 28, 39, 34, 40, 49, 67, 67, 72, 49, 61, 101, 39, 88, 54, 227, 48, 178, 1, 51, 73, 523, 43, 299, 295, 233, 15, 153, 205, 303, -4, 229, 323, 258, 124, 130, 22, 271, 22, 78, 848, 28, -309, 64, 311, 67, 61, 58, 35, 66, 54, 95, 120, 81, 104, 53, 56, 46, 67, 63, 77, 47, 43, 42, 59, 49, 41, 56, 39, 43, 54, 64, 55, 68, 69, 49, 49, 48, 56, 57, 41, 57, 59, 54, 35, 116, 99, 62, 42, 102, 64, 64, 108, 88, 85, 130, 651, 280, 35, 144, 117, 56, 110, 152, -82, 112, 268, 142, 42, 54, 66, 76, 62, 32, 66, 88, 68, 72, 39, 70, 62, 48, 64, 60, 391, -54, 115, 65, 69, 72, 50, 46, 48, 24, 160, 25, 374, 37, 208, 302, 65, 52, -539, 40, 47, -56, 317, 2, -261, -384, 378, 434, 85, 681, 701, 94, -26, 1055, 12, 173, -14, -277, 376, -364, 478, -3520, -1146, 417, 18, 503, -437, 795, 813, 0, 0, 0, 0, 0, 0, 0, 0, -4, 24, 18, 3, 16, 29, 24, 18, 6, 34, 40, 3, 34, 32, 24, 24, -6, 28, 23, 48, 25, 16, 26, 17, 29, -3, 43, 7, 23, 38, 22, 27, -80, -77, 144, 2, 16, -10, -11, -18, 6, -22, 210, -57, 1, 204, 30, 145, 0, 0, 0, 0, 0, 0, 0, 0, 85, 67, 110, 113, 84, -34, 74, 43, 111, 158, 106, 86, 72, 83, 70, 118, 79, 71, 74, 98, 102, 88, 103, 68, 110, 115, 94, 70, 111, 84, 89, 149, 116, 44, 191, 32, 115, 178, 84, 75, -105, 77, 124, 95, 52, 104, 133, 119, 28, 200, 6, -16, -77, 87, -61, 93, -24, -136, 337, 218, -52, 256, 16, 71, 127, 98, 55, 108, 84, 78, 192, 130, 75, 109, 57, 82, 97, 87, 86, 158, 67, 101, 86, 139, 105, 68, 79, 102, 25, 28, 88, 97, 99, 67, 114, 11, 19, 76, 112, -4, 131, 188, 83, 143, 76, 69, 53, 132, 39, 31, 82, 98, 5, -111, -21, 37, 8, -15, 5, 225, 185, -27, -11, -125, 282, 207, 30, -7, 60, 60, 63, 24, 48, 114, 69, 78, 78, 94, 94, 40, 16, 41, 101, 96, 62, 18, 14, 39, 20, 52, 100, 109, 64, 51, 22, 17, 36, 8, 77, 34, 33, -40, 8, 0, 3, -109, 21, 56, 14, -7, 110, -27, -87, -31, 42, 44, 47, -2, -12, 113, 105, 51, 1, -1, 55, -58, -114, 6, 39, -83, 123, 160, 131, 178, 146, 134, 213, 118, 98, 215, 52, 168, 197, 160, 156, 166, 136, 100, 116, 139, 118, 209, 125, 150, 155, 125, 139, 144, 152, 121, 179, 166, 163, 168, 117, 160, 87, 124, 123, 40, 177, 165, 77, 129, 83, 112, 55, 247, 204, 110, 75, 40, 49, 101, -60, 95, 126, 159, 221, 147, -62, -48, 46, 66, 253, 85, 153, 40, 120, 150, 132, 138, 169, 45, 24, 101, 127, 131, 168, 139, 94, 227, 76, 44, 97, 98, 109, 122, 98, 66, 43, 30, 53, 183, 20, 33, 54, 74, 61, -82, -123, 11, 0, -21, 31, -115, 109, -2, -53, 33, 278, 259, -50, -23, 30, -106, 48, 37, -35, 202, 63, 37, -593, 12, -103, 128, 103, -213, -123, -159, 0, 0, 0, 0, 0, 0, 0, 0, 137, 219, 212, 210, 290, 225, 271, 196, 128, 198, 78, 200, 131, 196, 321, 219, 115, 127, 219, 128, 116, 205, 93, 80, 82, 148, -59, -38, 89, -103, 87, -47, -665, -851, 142, -211, -97, -324, 45, -178, -582, -461, -165, -778, -278, 319, 95, -75, 0, 0, 0, 0, 0, 0, 0, 0, 1231, 606, 214, 615, 1065, 98, 611, 483, 971, 426, 616, 547, 549, 518, 598, 593, 689, 639, 702, 490, 220, 612, 1590, 624, 579, 447, 400, 429, 488, 346, 298, 743, 54, 606, 363, 377, 665, -225, 606, 243, -810, 481, 362, 341, 499, 581, 475, -274, -34, 457, -180, -407, -198, 121, -537, 806, 44, -167, -238, 190, -310, 665, -355, -245, 982, 54, 610, 892, 437, 673, 320, 373, -139, 1327, 709, 816, 548, 447, 687, 563, 592, 743, 513, 1365, 603, 684, 459, 638, 467, 320, 513, 595, 651, 450, 726, 460, -9, 410, 169, 144, 743, 1039, 449, 773, -280, 85, 175, 261, -4, 695, -236, -207, -375, 353, 168, 28, 351, 6, 259, 499, 146, -159, -260, -518, 4, 246, 132, 604, 768, 544, 400, 388, 250, 496, 508, 548, 544, 540, 561, 126, 925, 27, 733, 549, 381, 493, 8, -329, 181, 349, 493, 591, 577, -262, 12, 12, 403, -35, 61, 684, -9, -22, -289, -168, -72, -420, 326, 448, 127, -13, 44, -279, 231, -229, 197, 126, 162, -52, -117, -145, 319, -406, 239, 10, 244, 21, 208, 50, 184, -189, 310, 411, 1380, 719, 1231, 828, 1936, 602, 1541, 352, 139, 1694, 1569, 1615, 996, 992, 1280, 953, 914, 751, 1070, 1023, 1397, 1389, 621, 345, 693, 1642, 834, 897, 1727, 1973, 799, 1424, 544, 1937, 1101, 541, 749, 386, 229, 279, -683, 364, -267, 593, 1249, 424, 925, 898, -150, 133, 569, 609, -297, 340, 733, 513, 379, 1128, 269, -319, 175, 328, 801, 736, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; 
            //int[] tttt = new int[197] { 94, 321, 374, 498, 1044, 0, 0, 0, 0, 0, 0, 0, 0, -4, 16, -4, -20, 6, -3, 15, -12, -11, -11, 23, 12, -4, -5, 32, -6, -24, 51, -17, -26, 9, -33, 5, -8, -64, -22, -48, -40, -41, -92, 5, 1, 40, 200, -104, -200, -176, -40, -200, 200, 192, 200, -160, -199, 72, 200, -168, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -8, -50, -4, 10, 12, -3, 48, 0, 2, 15, 28, -32, -12, 10, 30, -8, -7, -2, -1, 28, -20, 0, -11, -8, 116, -67, -28, 88, 93, 115, -42, 13, -200, -64, 148, 106, 127, -76, 22, 44, 4, -200, 200, -200, 37, 136, 173, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 93, 148, -200, 104, 169, -97, -40, -60, 196, -88, -7, -12, 46, -144, 32, 16, 80, -196, 16, -165, 36, -91, 130, 35, 200, 200, -8, -48, 160, 199, 40, 172, 128, 200, -192, -152, -200, 200, 97, -200, -184, 200, -200, -168, 200, 200, 57, 0, 0, 0, 0, 0, 0, 0, 0 };

            //PrintDefinedTexelParams(ttt);
            //PlayGameOnConsoleAgainstHuman();

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

            //Stopwatch sw = Stopwatch.StartNew();
            //
            //for (int i = 0; i < 2_000_000; i++)
            //{
            //    TexelEvaluate();
            //}
            //
            //sw.Stop();
            //
            //Console.WriteLine(sw.ElapsedMilliseconds);




            LoadBestTexelParamsIn();
            PlayGameOnConsoleAgainstHuman("B4k2/8/4Q3/5K2/8/8/2P5/8 w ha - 5 11", true, 30_000_000L);




            //TuneWithTxtFile("SELF_PLAY_GAMES");

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
            while (BOT_MAIN.goalGameCount > BOT_MAIN.gamesPlayed)
            {

                try
                {
                    string tGameStr;
                    GetLowNoisePositionalEvaluation(globalRandom);
                    LoadFenString(tGameStr = pStartFEN);
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
                catch (Exception tE)
                {
                    Console.WriteLine(tE.ToString());
                }

            }
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
                PlayThroughZobristTree();
                Move tM = transpositionTable[zobristKey].bestMove;
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
            SetJumpState();
            Console.WriteLine("\n- - - - - - - - - - - - -");
            int tC = 1;
            Dictionary<ulong, bool> usedULs = new Dictionary<ulong, bool>();
            while (transpositionTable.TryGetValue(zobristKey, out TranspositionEntry tM))
            {
                if (usedULs.ContainsKey(zobristKey))
                {
                    Console.WriteLine(tC + ": AT LEAST ONE REPETION; REST NOT VISIBLE");
                    break;
                }
                Console.WriteLine(tC + ": " + tM.bestMove + " > ");
                usedULs.Add(zobristKey, true);
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
        private int[] jmpSt_moveHashList;

        public void LoadJumpState()
        {
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
            if (isWhiteToMove) GetLegalWhiteMoves(PreMinimaxCheckCheckWhite(), ref pMoveList);
            else GetLegalBlackMoves(PreMinimaxCheckCheckBlack(), ref pMoveList);
        }

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
            if (curSearchZobristKeyLine != null)
            {
                baseLineLen = curSearchZobristKeyLine.Length;
                tZobristKeyLine = new ulong[baseLineLen];
                Array.Copy(curSearchZobristKeyLine, tZobristKeyLine, baseLineLen);
            }

            int perftScore, tattk = isWhiteToMove ? PreMinimaxCheckCheckWhite() : PreMinimaxCheckCheckBlack(), pDepth = 1;

            (string, int) bookMoveTuple = TLMDatabase.SearchForNextBookMove(moveHashList);

            if (bookMoveTuple.Item2 != 0)
            {
                int actualMoveHash = NuCRe.GetNumber(bookMoveTuple.Item1);
                List<Move> tMoves = new List<Move>();
                if (isWhiteToMove) GetLegalWhiteMoves(tattk, ref tMoves);
                else GetLegalBlackMoves(tattk, ref tMoves);
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

                        transpositionTable.Add(zobristKey, new TranspositionEntry(tMoves[i], Array.Empty<int>()));

                        return 0;
                    }
                }
            }

            do
            {
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
            } while (globalTimer.ElapsedTicks < tTimestamp && pDepth < 179);



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
            int standPat = TexelEvaluate();
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
            int standPat = TexelEvaluate();
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
            for (int p = 0; p < 64; p++) tEval += pieceEvals[tPT = pieceTypeArray[p] + 7 * ((int)(blackPieceBitboard >> p) & 1)] + piecePositionEvals[pieceCount, tPT][p];
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

            do
            {
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
            if (BOARD_MANAGER_ID == ENGINE_VALS.PARALLEL_BOARDS - 1) Console.WriteLine();
            for (int i = 0; i < 25; i++)
            {
                //earlyGameMultipliers[i] = MultiplierFunction(i, 32d);
                //middleGameMultipliers[i] = MultiplierFunction(i, 16d);
                //lateGameMultipliers[i] = MultiplierFunction(i, 0d);

                earlyGameMultipliers[i] = EGMultiplierFunction(i);
                lateGameMultipliers[i] = LGMultiplierFunction(i);

                if (BOARD_MANAGER_ID == ENGINE_VALS.PARALLEL_BOARDS - 1)
                    Console.WriteLine("[" + i + "] " + earlyGameMultipliers[i] + " | " + middleGameMultipliers[i] + " | " + lateGameMultipliers[i]);
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
            string tPath = Path.GetFullPath(pTXTName + ".txt").Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", "");
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
                    if (isWhiteToMove) GetLegalWhiteMoves(PreMinimaxCheckCheckWhite(), ref moveOptionList);
                    else GetLegalBlackMoves(PreMinimaxCheckCheckBlack(), ref moveOptionList);

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

            //BOT_MAIN.curTEXELPARAMS = ttt;

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
            int[] ttt = new int[778] { 82, 341, 416, 470, 1106, 102, 302, 304, 518, 960, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, -46, 0, -20, 0, 64, -21, -71, 37, 29, 6, 98, 13, 44, 0, -26, -14, -36, -20, 94, 16, 60, 10, -62, -19, -38, -8, -28, 14, -26, -4, -32, -28, -52, 30, 56, -40, -72, -22, 100, 4, -98, -34, 6, -49, 32, -10, 72, 20, 84, -56, -60, -112, 128, -28, 152, 34, 26, -52, 200, -56, 76, 44, -56, -113, -156, 188, -39, 28, 200, -90, -60, -86, 100, -178, -119, -200, 200, 200, -20, -22, -78, -158, -200, 196, 200, -200, -200, -200, 104, 200, 138, -122, 200, 200, 124, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 156, -37, -68, -148, -200, 200, -70, 72, 76, -31, -200, -21, 178, -200, 200, 200, -200, 123, -188, -6, 164, -74, 24, -28, -8, 71, -50, -112, -200, -8, -44, 27, -23, -50, 19, 19, 41, 150, 152, 4, -98, -36, -34, 174, -106, -16, 17, 141, -48, -108, 162, 6, -112, -52, 76, 68, 8, -86, 68, -176, 189, 81, 162, -200, 152, -41, 128, 172, -173, -144, 56, 22, 94, -28, 32, 16, -63, 36, -200, -200, -200, -22, 56, -116, 76, -36, -30, 100, 52, -166, -114, 142, -52, -44, -200, -18, -72, 60, 200, -160, 96, 46, -200, -28, -32, -44, 132, 60, -200, -137, -200, -176, 200, -200, -200, 4, 200, -200, 72, 200, -78, 200, 160, -105, 200, 96, -114, 200, 200, -88, -24, 11, -34, 184, 26, 83, -140, 10, -16, -72, -200, 12, -200, 46, -200, 11, 82, 66, -102, 22, -184, 32, 114, 140, -26, -18, -36, 66, 100, 25, -24, 133, -96, 50, 2, 80, 184, 43, -136, 44, 124, -148, 180, 69, -126, -200, 6, 78, -96, 89, -68, 200, -116, 114, 158, 5, -154, 115, 200, -152, -171, 41, -80, -23, 137, 98, -80, 40, -42, 200, 52, 146, 200, 6, -112, 32, 200, 132, 4, 90, -200, -200, 104, 67, -56, 186, 124, 140, 97, -144, 200, -4, -160, -117, -200, 24, -80, 200, 28, -46, -24, -52, -200, -200, 72, 20, 34, 200, -16, 88, 56, 200, -146, 120, 174, 200, -200, -86, 125, 172, -70, 4, 124, -72, 0, -2, 70, -14, 48, -32, 200, 80, 24, -132, 74, -44, 184, -4, -136, 0, -16, 20, -18, -32, 96, 92, 56, 2, 174, 196, -200, -56, -116, 88, 200, -8, 168, -13, 74, -106, 200, -182, 80, 82, -128, -16, 90, -200, 66, -88, 58, -16, 80, -34, 96, -95, -96, -200, 76, 26, -72, 32, -27, -108, 164, 200, -36, -93, -10, -56, 22, 36, 74, -200, -96, 200, 88, -88, 156, -92, 96, -200, 200, 191, -8, 62, -8, -100, 136, -72, -36, 200, -64, -114, 153, -110, 174, 46, 108, 142, 121, -200, 108, -200, 74, -4, 104, -200, 116, 82, 200, -76, 176, -200, 76, -8, 200, 46, 29, -86, 12, -24, -80, -12, 200, 124, -16, 54, 104, 200, -94, 140, 36, 32, -64, 24, -178, 15, -200, 0, -200, 4, -32, -124, 148, 176, 192, 90, 164, -91, 200, 172, -200, 64, 34, 40, -200, 0, -200, 12, -124, 48, -200, -56, 184, -8, -184, 0, 200, -16, 200, -8, 180, -18, -20, 24, -200, -58, 174, -200, 40, -4, -200, -24, 200, 54, 200, -24, -32, 37, 192, 48, 44, 26, 200, -22, 200, -145, -200, 30, 86, 54, 200, -102, -34, 94, -98, 92, -200, -76, 196, -18, -200, -180, 16, 186, -92, -200, -200, 164, -200, 62, 192, 159, -112, -12, -70, 184, 104, 200, -200, -148, -4, 86, -144, 200, -100, 200, 200, -104, 114, 132, -168, 12, 56, -10, 82, 200, -200, -200, -196, -6, 52, -200, 84, 200, -192, 128, 200, -200, -200, 200, 88, 168, -32, -84, 172, 52, -172, 0, -40, 68, -186, 42, 96, -200, -198, 200, -56, -176, 88, 0, 86, 16, -124, -46, 40, -16, -4, 54, -184, 200, 58, -200, 77, -200, -44, 102, 0, -50, -10, -92, 100, -86, 8, 20, -20, -200, 38, -200, -49, -4, 2, -200, 78, 0, -40, -92, 32, 0, -164, -88, -38, -138, -118, 29, -34, -200, -200, -200, -71, -200, 200, -200, -108, -200, 0, 192, -24, 104, -200, 200, 190, -28, -200, -200, -200, 128, -96, 200, 200, 200, 200, -200, -200, 177, -24, 200, 132, 200, -190, 200, -130, 200, 148, 200, 200, -200, -100, 200, -196, -200, 90, 200, -200, 200, 200, -200, -200, -200, -200, -200, 118, -200, 85, -200, 166, 199, -200 };
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
            texelPieceEvaluationsV2EG[8] = -(texelPieceEvaluationsV2EG[1] = pParams[0]);
            texelPieceEvaluationsV2EG[9] = -(texelPieceEvaluationsV2EG[2] = pParams[1]);
            texelPieceEvaluationsV2EG[10] = -(texelPieceEvaluationsV2EG[3] = pParams[2]);
            texelPieceEvaluationsV2EG[11] = -(texelPieceEvaluationsV2EG[4] = pParams[3]);
            texelPieceEvaluationsV2EG[12] = -(texelPieceEvaluationsV2EG[5] = pParams[4]);
            texelPieceEvaluationsV2LG[8] = -(texelPieceEvaluationsV2LG[1] = pParams[5]);
            texelPieceEvaluationsV2LG[9] = -(texelPieceEvaluationsV2LG[2] = pParams[6]);
            texelPieceEvaluationsV2LG[10] = -(texelPieceEvaluationsV2LG[3] = pParams[7]);
            texelPieceEvaluationsV2LG[11] = -(texelPieceEvaluationsV2LG[4] = pParams[8]);
            texelPieceEvaluationsV2LG[12] = -(texelPieceEvaluationsV2LG[5] = pParams[9]);
            int c = 10;
            for (int p = 1; p < 7; p++)
            {
                for (int s = 0; s < 64; s++)
                {
                    texelTuningRuntimePositionalValsV2EG[p + 7][blackSidedSquares[s]] = texelTuningRuntimePositionalValsV2EG[p][s] = pParams[c++];
                    texelTuningRuntimePositionalValsV2LG[p + 7][blackSidedSquares[s]] = texelTuningRuntimePositionalValsV2LG[p][s] = pParams[c++];
                }
            }

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

            string tPath = Path.GetFullPath("TEXEL_TUNING.txt").Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", "");
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

            for (int j = start; j < end; j++)
            {
                TLM_ChessGame tGame = tDataset[j];
                double tGR = tGame.gameResult;
                LoadFenString(tGame.startFen);
                int tL = tGame.actualMoves.Count - 5;
                texelCostMovesEvaluated += tL;
                for (int i = 0; i < tL; i++)
                {
                    costSum += TexelCost(tGR - TexelTuningSigmoid(TexelEvaluate()));
                    PlainMakeMove(tGame.actualMoves[i]);
                }
            }

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
                    tGame.isMoveNonTactical.Add(tB = (Evaluate() == tQuietEval && i > 4));
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

                // Könnte sein, dass es theoretisch nicht ganz save ist; da es den special double Check Case gibt; aber der ist so selten...
                List<Move> moveOptionList = new List<Move>();
                if (isWhiteToMove) GetLegalWhiteMoves(PreMinimaxCheckCheckWhite(), ref moveOptionList);
                else GetLegalBlackMoves(PreMinimaxCheckCheckBlack(), ref moveOptionList);

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

        private int TexelEvaluate()
        {
            //pieceCount = ULONG_OPERATIONS.CountBits(allPieceBitboard)
            int tEvalEG = 0, tEvalLG = 0, tProgress = 0;
            for (int p = 0; p < 64; p++)
            {
                if (((int)(allPieceBitboard >> p) & 1) == 0) continue;
                //int tAPT = pieceTypeArray[p];

                //tEval += texelPieceEvaluations[
                //    tPT = pieceTypeArray[p] + 7 * ((int)(blackPieceBitboard >> p) & 1)
                //] + texelTuningRuntimeVals[pieceCount, tPT][p];

                //tEvalEG 

                //tPT = pieceTypeArray[p] + 7 * ((int)(blackPieceBitboard >> p) & 1)

                int tPT = pieceTypeArray[p] + 7 * ((int)(blackPieceBitboard >> p) & 1);

                tEvalEG += texelTuningRuntimePositionalValsV2EG[tPT][p] + texelPieceEvaluationsV2EG[tPT];
                tEvalLG += texelTuningRuntimePositionalValsV2LG[tPT][p] + texelPieceEvaluationsV2LG[tPT];
                tProgress += pieceTypeGameProgressImpact[tPT];

            }

            //Console.WriteLine(tProgress);

            if (tProgress > 24)
            {
                tProgress = 24;
            }

            //tProgress = 24;

            //int pieceCount = ULONG_OPERATIONS.CountBits(allPieceBitboard);

            return (int)(earlyGameMultipliers[tProgress] * tEvalEG + lateGameMultipliers[tProgress] * tEvalLG);
            //return tEval;
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
