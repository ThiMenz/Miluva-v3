using System.Diagnostics;

#pragma warning disable CS8618
#pragma warning disable CS8622

namespace ChessBot
{
    public static class BOT_MAIN
    {
        public readonly static string[] FIGURE_TO_ID_LIST = new string[] { "Nichts", "Bauer", "Springer", "Läufer", "Turm", "Dame", "König" };

        public static bool isFirstBoardManagerInitialized = false;
        public static IBoardManager[] boardManagers = new IBoardManager[ENGINE_VALS.PARALLEL_BOARDS];
        public static int curBoardManagerID = -1;


        public static List<string> selfPlayGameStrings = new List<string>();
        public static int gamesPlayed = 0;
        public static int[] gamesPlayedResultArray = new int[3];
        public static int movesPlayed = 0;
        public static int depthsSearched = 0;
        public static long evaluationsMade = 0;
        public static int searchesFinished = 0;
        public static int goalGameCount = 1_000;


        public static double TEXELcostSum = 0d;
        public static int TEXELcostMovesEvaluated = 0;
        public static int TEXELfinished = 0;
        public static int TEXELsortedout = 0;
        public static int TEXELadjustmentsmade = 0;
        public static ulong TEXELfinishedwithoutimpovement = 0;

        public static int[] curTEXELPARAMS;

        public static void Main(string[] args)
        {
            SQUARES.Init();
            FEN_MANAGER.Init();
            NuCRe.Init();
            ULONG_OPERATIONS.SetUpCountingArray();
            LegacyEngineManager.InitSnapshots();
            TLMDatabase.InitDatabase();

            //Console.WriteLine(TLMDatabase.SearchForNextBookMove(";-8,U:,gF,üK,fY,Q;"));
            //Console.WriteLine(TLMDatabase.SearchForNextBookMove(";-8,U:,gF,üK,fY,Q;"));
            //Console.WriteLine(TLMDatabase.SearchForNextBookMove(";-8,U:,gF,üK,fY,Q;"));
            //Console.WriteLine(TLMDatabase.SearchForNextBookMove(";-8,U:,gF,üK,fY,Q;"));
            //Console.WriteLine(TLMDatabase.SearchForNextBookMove(";-8,U:,gF,üK,fY,Q;"));
            //TLMDatabase.OptimizeSizeOfDatabase();

            MEM_TempStuff();

            //for (int i = 0; i < 100; i++)
            //Console.WriteLine(MOVE_HASH_EXTRACTOR.Get(TLMDatabase.SearchForNextBookMoveV2(new List<int>()).Item1));

            //MEM_TempStuff();
            //Console.WriteLine(MOVE_HASH_EXTRACTOR.Get("K9"));
            //Console.WriteLine(MOVE_HASH_EXTRACTOR.Get("g9"));
            //Console.WriteLine(MOVE_HASH_EXTRACTOR.Get("II"));

            //Console.WriteLine(CGFF.GetGame(";-8,U:,gF,üK,fY,Q;,BW,ÁL,Ç¤,^^,&b,´9,yU,ÿ²,Í5,K9,g9,II,6J,KJ,%j,õ;,D7,k[,Eb,lx,Ñ6,Gv,#V,ýX,Cu,Yp,AC,]l,0"));

            //FirmataArdControl.TEST();

            // MEM_CreateSnapshot("SNAPSHOT_V02_03_009"); 

            //_ = new ReLe_AIHandler();

            //SNAPSHOT_V01_00_018 sn = new SNAPSHOT_V01_00_018(ENGINE_VALS.DEFAULT_FEN);
            //sn.LoadFenString("8/6p1/p2p2k1/1p1N2b1/4P1n1/R6B/PPr5/K7 w - - 0 2");
            //Move? tm;
            //Console.WriteLine(sn.ReturnNextMove(null, 1_000_000L));

            // MEM_TempStuff();
            //
            //Console.WriteLine(MOVE_HASH_EXTRACTOR.Get(NuCRe.GetNuCRe(6947)));
            //Console.WriteLine(MOVE_HASH_EXTRACTOR.Get(NuCRe.GetNuCRe(10419)));

            //MEM_UpdateSnapshotCS();

            //MEM_SnapshotClash();
        }

        #region | MAIN METHODS |

        private static void MEM_TempStuff()
        {
            SetupParallelBoards();

            boardManagers[ENGINE_VALS.PARALLEL_BOARDS - 1].TempStuff();
        }

        private static void MEM_CreateSnapshot(string pName)
        {
            LegacyEngineManager.CreateNewBoardManagerSnapshot(pName);
        }

        private static void MEM_UpdateSnapshotCS()
        {
            LegacyEngineManager.CreateSnapshotCSFile();
        }

        /*
         *  ! V01_00_018: PeStO Piece Square Tables
         *  ! V01_01_000: Aspiration Windows & Custom Killer Heuristic
         *  ! V01_01_001: Check Ext-3 & LG For-Loop Precalcs
         *  ! V02_00_000: Time Formats & Basic Management
         *  $ V02_00_005: No CheckExt, but fixed Time Management & ApirWindows
         *  $ V02_00_009: Scaling CheckExt
         *  $ V02_00_010: QSearch InEff Checkmate Checks
         *  ! V02_01_003: Bugged Full-TT-Impl & Custom Sort Removal
         *  $ V02_01_004: TT-Improvs
         *  $ V02_01_017: Actual TT
         *  $ V02_02_000: Negamax & PVS
         *  $ V02_02_001: Proof that PVS is actually slightly helpful; Negamax without PVS
         *  $ V02_02_002: TT pPly dependend Aging
         * ++ V02_02_013: Internal Iterative Reductions
         *  $ V02_03_001: 250er Delta Pruning
         *  $ V02_03_007: Delta Pruning (excluded checks)
         */ 

        private static void MEM_SnapshotClash()
        {
            isFirstBoardManagerInitialized = true;

            IBoardManager[] oppBoards = new SNAPSHOT_V02_02_013[16];
            IBoardManager[] ownBoards = new SNAPSHOT_V02_03_009[16];
            
            for (int i = 0; i < 16; i++)
            {
                oppBoards[i] = new SNAPSHOT_V02_02_013(ENGINE_VALS.DEFAULT_FEN);
                ownBoards[i] = new SNAPSHOT_V02_03_009(ENGINE_VALS.DEFAULT_FEN);
            }

            LegacyEngineManager.PlayBetweenTwoSnapshots(ownBoards, oppBoards, new TimeFormat() { Time = 70_000_000L, Increment = 100_000L }, 64);
        }

        private static void MEM_SelfPlay()
        {
            SetupParallelBoards();

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

            string tPath = PathManager.GetTXTPath("DATABASES/SELF_PLAY_GAMES");
            selfPlayGameStrings.Add(tSave);
            File.AppendAllLines(tPath, selfPlayGameStrings.ToArray());
        }

        #endregion

        private static void SetupParallelBoards()
        {
            for (int i = 0; i < ENGINE_VALS.PARALLEL_BOARDS; i++)
            {
                curBoardManagerID++;
                boardManagers[i] = new BoardManager("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
                Console.Write((i + 1) + ", ");
                isFirstBoardManagerInitialized = true;
            }
            curBoardManagerID = 1024;
            Console.WriteLine("\n\n");
        }

        public static void ParallelTexelTuning(List<TLM_ChessGame> pChessGamesToEdit)
        {
            Stopwatch sw = Stopwatch.StartNew();

            int tCores = ENGINE_VALS.CPU_CORES - 1;
            int tGameDataSetLen = pChessGamesToEdit.Count;
            int gamesPerThread = tGameDataSetLen / ENGINE_VALS.CPU_CORES;
            int tMin = 0;
            TEXELfinished = 0;

            for (int i = 0; i < tCores; i++)
            {
                boardManagers[i].SetPlayThroughVals(pChessGamesToEdit, tMin, tMin += gamesPerThread);
            }
            boardManagers[tCores].SetPlayThroughVals(pChessGamesToEdit, tMin, tGameDataSetLen);

            ThreadPool.QueueUserWorkItem(new WaitCallback(boardManagers[tCores].PlayThroughSetOfGames));
            for (int i = 0; i < tCores; i++)
            {
                ThreadPool.QueueUserWorkItem(new WaitCallback(boardManagers[i].PlayThroughSetOfGames));
            }
            tCores += 1;

            while (TEXELfinished != tCores) Thread.Sleep(10);

            sw.Stop();

            Console.WriteLine(sw.ElapsedMilliseconds);
            Console.WriteLine("SORTED OUT: " + TEXELsortedout);

            int c = 0;

            foreach (TLM_ChessGame tlmchg in pChessGamesToEdit)
            {
                for (int i = 0; i < tlmchg.actualMoves.Count; i++)
                {
                    if (tlmchg.isMoveNonTactical[i]) continue;
                    c++;
                }
            }

            Console.WriteLine("SORTED OUT2: " + c);

            boardManagers[ENGINE_VALS.PARALLEL_BOARDS - 1].TexelTuning(pChessGamesToEdit, new int[boardManagers[ENGINE_VALS.PARALLEL_BOARDS - 1].TEXEL_PARAMS]);

            //boardManagers[ENGINE_VALS.PARALLEL_BOARDS - 1].TLMTuning(pChessGamesToEdit);
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
            string tPath = PathManager.GetTXTPath("UTILITY/FENS");
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
        public static readonly string FILE_BEGIN = "TLM_ChessGame: \nStart FEN: ";

        public static readonly string[] GAME_RESULT_STRINGS = new string[6]
        {
            "Result: Black Has Won",
            "Result: Draw",
            "Result: White Has Won",
            "Result: ?",
            "Result: ?",
            "Result: ?"
        };

        public static TLM_ChessGame GetGame(string pStr)
        {
            string[] tSpl = pStr.Split(';');
            string startFen = tSpl[0];
            if (startFen.Replace(" ", "") == "")
            {
                pStr = pStr.Substring(1, pStr.Length - 1);
                startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
                Console.WriteLine(pStr);
                Console.WriteLine(pStr.Replace(startFen + ";", ""));
            }
            TLM_ChessGame rCG = new TLM_ChessGame(startFen);
            string[] tSpl2 = pStr.Replace(startFen + ";", "").Split(',');
            int tSpl2Len = tSpl2.Length - 1;
            for (int i = 0; i < tSpl2Len; i++)
            {
                Console.WriteLine(tSpl2[i]);
                rCG.hashedMoves.Add(NuCRe.GetNumber(tSpl2[i]));
            }
            int outcome = Convert.ToInt32(tSpl2[tSpl2Len]);
            rCG.gameResult = outcome;
            return rCG;
        }
    }

    public class TLM_ChessGame
    {
        public string startFen;
        public List<int> hashedMoves = new List<int>();
        public List<Move> actualMoves = new List<Move>();
        public List<bool> isMoveNonTactical = new List<bool>();
        public int gameResult;

        public TLM_ChessGame(string pStartFen)
        {
            startFen = pStartFen;
        }

        public override string ToString()
        {
            string tS = CGFF.FILE_BEGIN + startFen + "\n";
            int tL = hashedMoves.Count;
            for (int i = 0; i < tL; i++)
            {
                Console.WriteLine(i);
                tS += "Move " + (i + 1) + ": " + MOVE_HASH_EXTRACTOR.moveLookupTable[hashedMoves[i]] + "\n";
            }
            return tS + CGFF.GAME_RESULT_STRINGS[gameResult];
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

    public class TranspositionEntryV2
    {
        public Move bestMove;
        public int bestMoveHash;
        public int[] moveGenOrderedEvals;
        public int moveGenOrderedEvalLength, depth, eval;
        public ulong allPieceBitboard;

        public TranspositionEntryV2(Move pBestMove, int[] pMoveGenOrderedEvals, int pDepth, int pEval, ulong pAllPieceBitboard)
        {
            bestMove = pBestMove;
            bestMoveHash = pBestMove.moveHash;
            moveGenOrderedEvals = pMoveGenOrderedEvals;
            moveGenOrderedEvalLength = moveGenOrderedEvals.Length;
            depth = pDepth;
            eval = pEval;
            allPieceBitboard = pAllPieceBitboard;
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
        public int situationalMoveHash { get; private set; }
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

            if (isPromotion)
            {
                int t = (endPos % 8) | ((promotionType - 2) << 3) | (startPos > endPos ? 0b100000 : 0b0);
                situationalMoveHash = t | (t << 6);
            }
            else
            {
                situationalMoveHash = startPos | (endPos << 6) | (pieceType << 12);
            }
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

    public class ChessClock
    {
        public long Increment, FullTime;
        public long curRemainingTime;

        public bool disabled;

        public void Set(long pTime, long pIncr)
        {
            Increment = pIncr;
            curRemainingTime = FullTime = pTime;
        }

        public void Set(TimeFormat pTF)
        {
            Increment = pTF.Increment;
            curRemainingTime = FullTime = pTF.Time;
            disabled = pTF.Time == -1;
        }

        public void Enable()
        {
            disabled = false;
        }
        public void Disable()
        {
            disabled = true;
        }

        public bool HasTimeLeft()
        {
            return disabled || curRemainingTime > 0L;
        }

        public void Reset()
        {
            curRemainingTime = FullTime;
        }

        public void MoveFinished(long tTimeTaken)
        {
            curRemainingTime -= tTimeTaken;
            if (HasTimeLeft()) curRemainingTime += Increment;
        }

        public override string ToString()
        {
            return (curRemainingTime / 10_000_000d) + " / " + (FullTime / 10_000_000d) + " [+" + (Increment / 10_000_000d) + "]";
        }
    }

    public class TimeFormat
    {
        public long Time, Increment;
    }

    #endregion
}