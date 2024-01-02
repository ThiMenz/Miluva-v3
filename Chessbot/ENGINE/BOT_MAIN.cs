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
            TLMDatabase.InitDatabase();

            //LegacyEngineManager.CreateNewBoardManagerSnapshot("SNAPSHOT_V01_00_000");
            //return;

            SetupParallelBoards();

            IBoardManager opp = new SNAPSHOT_V01_00_000(ENGINE_VALS.DEFAULT_FEN);

            LegacyEngineManager.PlayTwoGamesBetweenTwoSnapshots(boardManagers[0], opp, 500_000L, ENGINE_VALS.DEFAULT_FEN);

            //SetupParallelBoards();
            //boardManagers[ENGINE_VALS.PARALLEL_BOARDS - 1].TempStuff();

            //CGFF.InterpretateLine(File.ReadAllLines(Path.GetFullPath("SELF_PLAY_GAMES.txt").Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", ""))[0]);
            //CGFF.InterpretateLine("r1br2k1/p3qpp1/1pn1p2p/2p5/3P4/1BPQPN2/P4PPP/R4RK1 w - - 2 15;Ñ8,ù:,tq,KI,2W,[[,Èa,ĥ7,ĘG,ëW,58,·|,ÞK,ľc,n6,°v,KN,Á²,ąa,Øt,0");
            //SetupParallelBoards();
            //MEM_SelfPlay();
        }

        private static void SetupParallelBoards()
        {
            for (int i = 0; i < ENGINE_VALS.PARALLEL_BOARDS; i++)
            {
                curBoardManagerID++;
                boardManagers[i] = new BoardManager("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
                Console.Write((i + 1) + ", ");
                isFirstBoardManagerInitialized = true;
            }
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

            string tPath = PathManager.GetTXTPath("DATABASES/SELF_PLAY_GAMES");
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

        public static readonly string[] GAME_RESULT_STRINGS = new string[3]
        {
            "Result: Black Has Won",
            "Result: Draw",
            "Result: White Has Won"
        };

        public static TLM_ChessGame GetGame(string pStr)
        {
            string[] tSpl = pStr.Split(';');
            string startFen = tSpl[0];
            if (startFen.Replace(" ", "") == "") startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
            TLM_ChessGame rCG = new TLM_ChessGame(startFen);
            string[] tSpl2 = pStr.Replace(startFen + ";", "").Split(',');
            int tSpl2Len = tSpl2.Length - 1;
            for (int i = 0; i < tSpl2Len; i++) rCG.hashedMoves.Add(NuCRe.GetNumber(tSpl2[i]));
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
                tS += "Move " + (i + 1) + ": " + MOVE_HASH_EXTRACTOR.moveLookupTable[hashedMoves[i]] + "\n";
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