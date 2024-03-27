using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

#pragma warning disable CS8622

namespace ChessBot
{
    // 99.98055251% from the 22.6 GB sized PGN File
    // ~310k Games with 2100+ Elo (4.2017-File)
    public static class TLMDatabase
    {
        private static string[] DATABASE = Array.Empty<string>();
        private static int DATABASE_SIZE = 0;

        private static string[] DATABASEV2 = Array.Empty<string>();
        private static int DATABASEV2_SIZE = 0;

        public const int chunkSize = 10_000_000;
        public const int threadMargin = 10000;
        public const int minElo = 2100;
        public const int minMoves = 12;

        public const int minMovesInDatabaseForMatch = 5;
        public const double minPercentage = 0.035;

        public static int completedThreads = 0;
        public static int games = 0;

        private const string lichessDatabasePath = @"C:\Users\tpmen\Downloads\lichess_db_standard_rated_2017-04.pgn\lichess_db_standard_rated_2017-04.pgn";
        private static List<TLMDatabaseThreadObject> threadTasks = new List<TLMDatabaseThreadObject>();
        private static List<string> validPGNs = new List<string>();
        public static List<string> processedPGNs = new List<string>();
        private static IBoardManager? bM;
        private static Dictionary<char, int> pieceTypeDict = new Dictionary<char, int>();

        private static Random databaseRNG = new Random();

        private static int[] rankArr = new int[64]
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            1, 1, 1, 1, 1, 1, 1, 1,
            2, 2, 2, 2, 2, 2, 2, 2,
            3, 3, 3, 3, 3, 3, 3, 3,
            4, 4, 4, 4, 4, 4, 4, 4,
            5, 5, 5, 5, 5, 5, 5, 5,
            6, 6, 6, 6, 6, 6, 6, 6,
            7, 7, 7, 7, 7, 7, 7, 7
        };
        private static bool[] skippedChars = new bool[512];

        public static void InitDatabase()
        {
            string tPath2 = PathManager.GetTXTPath("DATABASES/TLM_DB1");
            DATABASE = File.ReadAllLines(tPath2);
            DATABASE_SIZE = DATABASE.Length;
            DATABASEV2 = File.ReadAllLines(PathManager.GetTXTPath("DATABASES/TLM_DB2"));
            DATABASEV2_SIZE = DATABASEV2.Length;
            bM = BOT_MAIN.boardManagers[ENGINE_VALS.PARALLEL_BOARDS - 1];

            Console.WriteLine("[TLM_DB] LOADED IN");
        }

        public static void LoadNN()
        {
            NeuralNetwork neuNet = new NeuralNetwork(

                NeuronFunctions.Sigmoid, NeuronFunctions.SigmoidDerivative, // STANDARD NEURON FUNCTION
                OUTPUT_SIGMOID, OUTPUT_SIGMOID_DERIVATIVE, // OUTPUT NEURON FUNCTION
                NeuronFunctions.SquaredDeviation, NeuronFunctions.SquareDeviationDerivative, // DEVIATION FUNCTION

                768, 16, 8, 7, 1);

            neuNet.LoadNNFromString(
                File.ReadAllText(PathManager.GetTXTPath("OTHER/CUR_NN"))
            );

            BoardManager tBM = new BoardManager("r1bqk1nr/ppp2ppp/2np4/2b1p3/2B1P3/1P1P1N2/P1P2PPP/RNBQK2R b KQkq - 0 5");

            tBM.LoadFenString("r1bq1rk1/ppp2ppp/2np1n2/2b1p3/8/8/5R2/R1B3K1 b Q - 0 7");

            ulong[] tULArr = new ulong[12];

            for (int i = 0; i < 64; i++)
            {
                int tPT = tBM.pieceTypeArray[i];
                if (ULONG_OPERATIONS.IsBitOne(tBM.whitePieceBitboard, i)) tULArr[tPT - 1] = ULONG_OPERATIONS.SetBitToOne(tULArr[tPT - 1], i);
                else if (ULONG_OPERATIONS.IsBitOne(tBM.blackPieceBitboard, i)) tULArr[tPT + 5] = ULONG_OPERATIONS.SetBitToOne(tULArr[tPT + 5], i);
            }

            double[] tinps = NNUE_DB_DATA.ULONGARRS_TO_NNINPUTS(tULArr);
            Console.WriteLine((neuNet.CalculateOutputs(tinps)[0] - 1) * 400);
        }

        public static void PlayThroughDatabase(BoardManager pBM)
        {
            const int MAX_GAME_AMOUNT = 7900;

            pBM.LoadFenString(ENGINE_VALS.DEFAULT_FEN);
            pBM.SetJumpState();

            List<TrainingData> tTrainingData = new List<TrainingData>();

            //ulong[] ttULArr = new ulong[12];

            //Stopwatch sw = Stopwatch.StartNew();
            //
            //for (int i = 0; i < 1_000_000; i++)
            //{
            //    double[] tArr = NNUE_DB_DATA.ULONGARRS_TO_NNINPUTS(ttULArr);
            //}
            //
            //sw.Stop();

            //Console.WriteLine(sw.ElapsedMilliseconds);


            for (int e = 0; e < DATABASE_SIZE && e < MAX_GAME_AMOUNT; e++) {

                pBM.LoadJumpState();
                string pStr = DATABASE[e];
                int tL = pStr.Length;
                List<char> tchars = new List<char>();
                List<ulong[]> gameMoveInputs = new List<ulong[]>();

                for (int cpos = 1; cpos < tL; cpos++) {

                    char ch = pStr[cpos];
                    if (ch == ',')
                    {
                        int tMoveHash = NuCRe.GetNumber(new String(tchars.ToArray()));
                        List<Move> tMoves = new List<Move>();
                        pBM.GetLegalMoves(ref tMoves);
                        int ti_tL = tMoves.Count;
                        for (int m = 0; m < ti_tL; m++)
                        {
                            Move tM = tMoves[m];
                            if (tM.moveHash == tMoveHash)
                            {
                                pBM.PlainMakeMove(tM);

                                ulong[] tULArr = new ulong[12];

                                for (int i = 0; i < 64; i++)
                                {
                                    int tPT = pBM.pieceTypeArray[i];
                                    if (ULONG_OPERATIONS.IsBitOne(pBM.whitePieceBitboard, i)) tULArr[tPT - 1] = ULONG_OPERATIONS.SetBitToOne(tULArr[tPT - 1], i);
                                    else if (ULONG_OPERATIONS.IsBitOne(pBM.blackPieceBitboard, i)) tULArr[tPT + 5] = ULONG_OPERATIONS.SetBitToOne(tULArr[tPT + 5], i);
                                }

                                gameMoveInputs.Add(tULArr);


                                //for (int j = 0; j < 12; j++)
                                //    Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(tULArr[j]));

                                break;
                            }
                        }

                        tchars.Clear();
                    }
                    else tchars.Add(ch);
                }

                NNUE_DB_DATA tNDD = new NNUE_DB_DATA(gameMoveInputs, Convert.ToInt32(new String(tchars.ToArray())));
                tTrainingData.AddRange(tNDD.TrainingData);
                if (e % 1000 == 0) Console.WriteLine(e + " Done!");
            }

            Console.WriteLine(tTrainingData.Count);



            NeuralNetwork neuNet = new NeuralNetwork(

                NeuronFunctions.ParametricReLU, NeuronFunctions.ParametricReLUDerivative, // STANDARD NEURON FUNCTION
                OUTPUT_SIGMOID, OUTPUT_SIGMOID_DERIVATIVE, // OUTPUT NEURON FUNCTION
                NeuronFunctions.SquaredDeviation, NeuronFunctions.SquareDeviationDerivative, // DEVIATION FUNCTION

                768, 16, 8, 7, 1);

            neuNet.GenerateRandomNetwork(new System.Random(), -1f, 1f, -1f, 1f);

            Console.WriteLine("Generated NN");

            TrainingData[] TrainingDataArr = RearrangeTrainingData(tTrainingData);
            TrainingData[][] TrainingDataBatches = TrainingData.CreateMiniBatches(TrainingDataArr, 40);

            int batchCount = TrainingDataBatches.Length;
            Console.WriteLine("BatchCount: " + batchCount);
            int curBatch = 0;
            int epoch = 0;

            Stopwatch sw = Stopwatch.StartNew();

            // ~((0.48/250)*8719)*4

            for (int i = 0; i < 1_500_000; i++)
            {
                neuNet.GradientDescent(TrainingDataBatches[curBatch], 1d, false);

                if (i % batchCount == 0)
                {
                    Console.WriteLine(sw.Elapsed);
                    LogManager.LogText("\n\n\n<<< EPOCH " + epoch + " >>> \n");
                    if (epoch++ % 3 == 0)
                    {
                        Console.WriteLine("EPOCH " + epoch + ": " + neuNet.CalculateDeviation(TrainingDataArr));
                    }
                    neuNet.RawLog();
                }
                if (++curBatch == batchCount) curBatch = 0; 
            }

            Console.WriteLine(neuNet.CalculateDeviation(TrainingDataArr));

            //Console.WriteLine(sw.Elapsed);

            neuNet.RawLog();

            //nnv.InitNeuralNetworkVisualizor(neuNet.layer, new Vector2(-5f, -5f), new Vector2(5f, 5f), valueColorCodeGradient, selectInfoText, new GameObject[4] { knotPrefab, linePrefab, worldSpaceTextPrefab, worldSpaceCanvas });
            //neuNet.CalculateOutputs(new double[2] { 0.5d, 0.7d }, nnv);
            //UpdateAIOutputDots();
            //selectInfoText.text = "Deviation: " + neuNet.CalculateDeviation(trainingData);
        }

        private static TrainingData[] RearrangeTrainingData(List<TrainingData> pNotRearrangedTrainingData)
        {
            Random rng = new Random();
            int tL = pNotRearrangedTrainingData.Count;
            TrainingData[] tRArr = new TrainingData[tL];
            for (int i = 0; i < tL; i++)
            {
                int tRand = rng.Next(0, tL - i);
                tRArr[i] = pNotRearrangedTrainingData[tRand];
                pNotRearrangedTrainingData.RemoveAt(tRand);
            }
            return tRArr;
        }

        private static double THI(double pVal)
        {
            if (pVal > 0) return Math.Log(pVal + 1);
            return -Math.Log(-pVal + 1);
        }

        private static double THI_DERIVATIVE(double pVal)
        {
            if (pVal < 0) return -1 / (pVal - 1);
            return 1 / (pVal + 1);
        }

        private static double OUTPUT_SIGMOID(double pVal)
        {
            return 2d / (Math.Exp(-pVal) + 1);
        }

        private static double OUTPUT_SIGMOID_DERIVATIVE(double pVal)
        {
            double d = Math.Exp(pVal);
            return 2 * d / Math.Pow(d + 1, 2);
        }

        #region | RUNTIME METHODS |

        public static (string, int) SearchForNextBookMoveV2(List<int> pMoveHashes)
        {
            int tL = pMoveHashes.Count;
            string tStr = tL == 0 ? "" : ";";
            for (int i = 0; i < tL; i++)
            {
                tStr += NuCRe.GetNuCRe(pMoveHashes[i]);
                if (i + 1 == tL) break;
                tStr += ",";
            }

            tL = tStr.Length;
            int resCount = 0;

            List<(string, int)> tResL = new List<(string, int)>();

            for (int i = 0; i < DATABASEV2_SIZE; i++)
            {
                string s = DATABASEV2[i];
                if (STRING_STARTS_WITH(s, tStr, tL))
                {
                    string sBook = GET_NEXT_BOOK_MOVE(s, tL);
                    resCount++;
                    ADD_BOOK_MOVE_TO_LIST(sBook, ref tResL);
                }
            }

            if (tResL.Count == 0) return ("", 0);

            SORT_RESULT_TUPLES(ref tResL, resCount, false);
            return GET_RANDOM_BOOK_MOVE(tResL);

            //return tResL[databaseRNG.Next(0, tResL.Count)];
        }

        public static (string, int) SearchForNextBookMove(string pNuCReMoveOpening)
        {
            int tL = pNuCReMoveOpening.Length, resCount = 0;

            List<(string, int)> tResL = new List<(string, int)>();

            for (int i = 0; i < DATABASE_SIZE; i++)
            {
                string s = DATABASE[i];
                if (STRING_STARTS_WITH(s, pNuCReMoveOpening, tL))
                {
                    string sBook = GET_NEXT_BOOK_MOVE(s, tL);
                    resCount++;
                    ADD_BOOK_MOVE_TO_LIST(sBook, ref tResL);
                }
            }

            SORT_RESULT_TUPLES(ref tResL, resCount, true);
            return GET_RANDOM_BOOK_MOVE(tResL);
        }

        public static (string, int) SearchForNextBookMove(List<int> pMoveHashes)
        {
            int tL = pMoveHashes.Count;
            string tStr = tL == 0 ? "" : ";";
            for (int i = 0; i < tL; i++)
            {
                tStr += NuCRe.GetNuCRe(pMoveHashes[i]);
                if (i + 1 == tL) break;
                tStr += ",";
            }
            return SearchForNextBookMove(tStr);
        }

        private static List<int> GetOpeningMatchesInDatabase(string pNuCReMoveOpening)
        {
            List<int> matches = new List<int>();
            int tL = pNuCReMoveOpening.Length;
            for (int i = 0; i < DATABASE_SIZE; i++)
            {
                string s = DATABASE[i];
                if (STRING_STARTS_WITH(s, pNuCReMoveOpening, tL)) matches.Add(i);
            }
            return matches;
        }

        private static (string, int) GET_RANDOM_BOOK_MOVE(List<(string, int)> pRefList)
        {
            int tL = pRefList.Count, c = 0;
            int[] tIArr = new int[tL];
            for (int i = 0; i < tL; i++)
            {
                c += pRefList[i].Item2;
                tIArr[i] = c;
            }

            int trand = databaseRNG.Next(0, c);

            for (int i = 0; i < tL; i++)
            {
                if (trand < tIArr[i])
                    return pRefList[i];
            }

            return ("", 0);
        }

        private static bool STRING_STARTS_WITH(string pStr, string pStrKey, int pKeyLen)
        {
            if (pKeyLen > pStr.Length) return false;
            for (int i = 0; i < pKeyLen; i++) if (pStr[i] != pStrKey[i]) return false;
            return true;
        }

        private static string GET_NEXT_BOOK_MOVE(string pStr, int keyLen)
        {
            int pStrLen = pStr.Length;
            string tM = "";
            while (++keyLen < pStrLen && pStr[keyLen] != ',') tM += pStr[keyLen];
            return tM;
        }

        private static void SORT_RESULT_TUPLES(ref List<(string, int)> pRefList, double pTotal, bool pSortOut)
        {
            int tL = pRefList.Count;
            for (int i = 0; i < tL; i++)
            {
                for (int j = 0; j < tL - 1; j++)
                {
                    if (pRefList[j].Item2 > pRefList[j + 1].Item2) continue;
                    (string, int) temp = pRefList[j];
                    pRefList[j] = pRefList[j + 1];
                    pRefList[j + 1] = temp;
                }
            }
            if (!pSortOut) return;
            for (int i = 0; i < tL; i++)
            {
                int c = pRefList[i].Item2;
                if (c < minMovesInDatabaseForMatch || c / pTotal < minPercentage)
                {
                    for (int j = i; j < tL; j++)
                        pRefList.RemoveAt(i);
                    break;
                }
            }
        }

        private static void ADD_BOOK_MOVE_TO_LIST(string pBookMoveStr, ref List<(string, int)> pRefList)
        {
            int tL = pRefList.Count;
            for (int i = 0; i < tL; i++)
            {
                (string, int) tEl = pRefList[i];
                if (tEl.Item1 == pBookMoveStr)
                {
                    pRefList[i] = (pBookMoveStr, tEl.Item2 + 1);
                    return;
                }
            }
            pRefList.Add((pBookMoveStr, 1));
        }

        #endregion

        #region | CREATION |

        private static int tOCount = 0;
        private static List<string> optimizedSizeDatabaseStrings = new List<string>();

        public static void OptimizeSizeOfDatabase()
        {
            RecursiveOpeningBookSearch(0, "", DATABASE.ToList(), new List<int>());
            Console.WriteLine("Optmized Line Count: " + tOCount);
            File.WriteAllLines(PathManager.GetTXTPath("DATABASES/TLM_DB2"), optimizedSizeDatabaseStrings);
        }

        private static void RecursiveOpeningBookSearch(int pDepth, string pStr, List<string> pSearchL, List<int> pPlayCounts)
        {
            int tL = pStr.Length, resCount = 0, tSLC = pSearchL.Count;

            List<(string, int)> tResL = new List<(string, int)>();
            List<string> newEntryList = new List<string>();

            for (int i = 0; i < tSLC; i++)
            {
                string s = pSearchL[i];
                if (STRING_STARTS_WITH(s, pStr, tL))
                {
                    newEntryList.Add(s);
                    string sBook = GET_NEXT_BOOK_MOVE(s, tL);
                    resCount++;
                    ADD_BOOK_MOVE_TO_LIST(sBook, ref tResL);
                }
            }

            SORT_RESULT_TUPLES(ref tResL, resCount, true);

            if (tResL.Count == 0)
            {
                //string[] tStrs = pStr.Substring(1, tL - 1).Split(',');
                //int t_ti_L = pPlayCounts.Count;
                //List<string> tStringCompL = new List<string>();
                //for (int i = 0; i < t_ti_L; i++)
                //    tStringCompL.Add(tStrs[i] + "," + NuCRe.GetNuCRe(pPlayCounts[i]) + ",");
                //optimizedSizeDatabaseStrings.Add(String.Concat(tStringCompL));
                optimizedSizeDatabaseStrings.Add(pStr);
                tOCount++;
                return;
            }

            int tResLC = tResL.Count;
            for (int j = 0; j < tResLC; j++) {
                pPlayCounts.Add(tResL[j].Item2);
                RecursiveOpeningBookSearch(pDepth + 1, pStr + (pDepth == 0 ? ";" : ",") + tResL[j].Item1, newEntryList, pPlayCounts);
                pPlayCounts.RemoveAt(pDepth);
            }
        }

        public static void ProcessCreatedDatabase()
        {
            completedThreads = 0;

            Stopwatch stopwatch = Stopwatch.StartNew();

            pieceTypeDict.Add('a', 1);
            pieceTypeDict.Add('b', 1);
            pieceTypeDict.Add('c', 1);
            pieceTypeDict.Add('d', 1);
            pieceTypeDict.Add('e', 1);
            pieceTypeDict.Add('f', 1);
            pieceTypeDict.Add('g', 1);
            pieceTypeDict.Add('h', 1);
            pieceTypeDict.Add('N', 2);
            pieceTypeDict.Add('B', 3);
            pieceTypeDict.Add('R', 4);
            pieceTypeDict.Add('Q', 5);
            pieceTypeDict.Add('K', 6);
            pieceTypeDict.Add('O', 6);

            string tPath = PathManager.GetTXTPath("DATABASES/OpeningDatabaseV1");
            string[] strs = File.ReadAllLines(tPath);
            int tL = strs.Length;
            skippedChars['!'] = skippedChars['?'] = skippedChars['+'] = skippedChars['#'] = skippedChars[' '] = true;

            for (int i = 0; i < ENGINE_VALS.CPU_CORES; i++)
            {
                bM = BOT_MAIN.boardManagers[i];
                bM.LoadFenString("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
                bM.SetJumpState();
            }

            int amountPerThread = tL / ENGINE_VALS.CPU_CORES, tempP = 0;

            for (int i = 0; i < ENGINE_VALS.CPU_CORES - 1; i++)
            {
                TLMDatabaseThreadObject2 tbto2_ts = new TLMDatabaseThreadObject2(strs, tempP, tempP += amountPerThread, BOT_MAIN.boardManagers[i], skippedChars, rankArr, pieceTypeDict);
                ThreadPool.QueueUserWorkItem(new WaitCallback(tbto2_ts.DatabaseThreadMethod));
            }

            TLMDatabaseThreadObject2 tbto2 = new TLMDatabaseThreadObject2(strs, tempP, tL, BOT_MAIN.boardManagers[ENGINE_VALS.CPU_CORES - 1], skippedChars, rankArr, pieceTypeDict);
            ThreadPool.QueueUserWorkItem(new WaitCallback(tbto2.DatabaseThreadMethod));

            while (completedThreads != ENGINE_VALS.CPU_CORES) Thread.Sleep(100);

            stopwatch.Stop();

            Console.WriteLine(stopwatch.ElapsedMilliseconds);

            string tPath2 = Path.GetFullPath("TLM_DB1.txt").Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", "");
            File.WriteAllLines(tPath2, processedPGNs);
        }

        public static void CreateDatabase()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();


            ReadChunks(lichessDatabasePath);


            stopwatch.Stop();

            int g = 0;
            foreach (TLMDatabaseThreadObject task in threadTasks)
            {
                g += task.lookedThroughGames;
                validPGNs.AddRange(task.localValidPGNs);
            }

            Console.WriteLine("Games under restrictions found: " + validPGNs.Count + " / " + g);
            Console.WriteLine(stopwatch.ElapsedMilliseconds);
            Console.WriteLine(validPGNs[validPGNs.Count - 1]);

            string tPath = Path.GetFullPath("OpeningDatabaseV1.txt").Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", "");
            File.WriteAllLines(tPath, validPGNs);
        }

        private static void ReadChunks(string path)
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            {
                long lastGameBeginByteOffset = 0;

                int tasksStarted = 0;
                long fsLen = fs.Length;

                while (lastGameBeginByteOffset < fsLen)
                {
                    fs.Position = lastGameBeginByteOffset;
                    byte[] tBytes = new byte[chunkSize];
                    int bCount = fs.Read(tBytes, 0, chunkSize);
                    TLMDatabaseThreadObject tTDTO = new TLMDatabaseThreadObject(tBytes);
                    threadTasks.Add(tTDTO);
                    ThreadPool.QueueUserWorkItem(new WaitCallback(tTDTO.DatabaseThreadMethod));
                    tasksStarted++;
                    lastGameBeginByteOffset += chunkSize + threadMargin;
                }

                while (completedThreads != tasksStarted) Thread.Sleep(100);

                Console.WriteLine("ByteCount = " + fsLen);
                lastGameBeginByteOffset = 0;

                int tttL = threadTasks.Count - 1;

                for (int i = 0; i < tttL; i++)
                {
                    int tLen = threadMargin - threadTasks[i].lastGameByteOffset + chunkSize;
                    lastGameBeginByteOffset += chunkSize + threadMargin - tLen;
                    fs.Position = lastGameBeginByteOffset;
                    byte[] tBytes = new byte[tLen];
                    int bCount = fs.Read(tBytes, 0, tLen);
                    TLMDatabaseThreadObject tTDTO = new TLMDatabaseThreadObject(tBytes);
                    threadTasks.Add(tTDTO);
                    ThreadPool.QueueUserWorkItem(new WaitCallback(tTDTO.DatabaseThreadMethod));
                    tasksStarted++;
                    lastGameBeginByteOffset += tLen;
                }

                while (completedThreads != tasksStarted) Thread.Sleep(100);
            }
        }

        #endregion
    }

    #region | THREAD OBJECTS |

    public class TLMDatabaseThreadObject
    {
        public int lastGameByteOffset = -1, lookedThroughGames = 0;

        private byte[] fileChunk;
        private int minElo, fileChunkSize;

        public List<string> localValidPGNs = new List<string>();

        public TLMDatabaseThreadObject(byte[] pArr) 
        {
            fileChunk = (byte[])pArr.Clone();
            fileChunkSize = pArr.Length;
            minElo = TLMDatabase.minElo;
        }

        private static int STRING_TO_INT(string s)
        {
            int total = 0, y = 1;
            for (int x = s.Length - 1; x > 0; x--)
            {
                total += (y * (s[x] - '0'));
                y *= 10;
            }
            if (s[0] == '-') total *= -1;
            else total += (y * (s[0] - '0'));
            return total;
        }

        public void DatabaseThreadMethod(object obj)
        {
            bool curGameValid = true;
            int tEloC = 0;
            const char nextLine = '\n';
            for (int i = 0; i < fileChunkSize; i++)
            {
                char tC = (char)fileChunk[i];
                if (nextLine == tC)
                {
                    if (i == fileChunkSize - 1) break;
                    switch ((char)fileChunk[++i])
                    {
                        case '[':
                            if (i + 9 < fileChunkSize && fileChunk[i + 5] == 'e' && fileChunk[i + 6] == 'E' && fileChunk[i + 7] == 'l' && fileChunk[i + 8] == 'o' && fileChunk[i + 9] == ' ')
                            {
                                string tempStr = "";
                                char t_ts_ch;
                                while (++i < fileChunkSize && nextLine != (t_ts_ch = (char)fileChunk[i])) tempStr += t_ts_ch;
                                if (i >= fileChunkSize) break;
                                int tcp = 9;
                                string tElo = "";
                                while ((t_ts_ch = tempStr[++tcp]) != '"') tElo += t_ts_ch;
                                tEloC++;
                                if (STRING_TO_INT(tElo) < minElo) curGameValid = false;
                                i++;

                                string tempStr3 = "";
                                char t_ts_ch3;
                                while (++i < fileChunkSize && nextLine != (t_ts_ch3 = (char)fileChunk[i])) tempStr3 += t_ts_ch3;
                                if (i >= fileChunkSize) break;
                                tcp = 9;
                                tElo = "";
                                while ((t_ts_ch3 = tempStr3[++tcp]) != '"') tElo += t_ts_ch3;
                                tEloC++;

                                if (STRING_TO_INT(tElo) < minElo) curGameValid = false;
                                i += 70;
                            }

                            break;
                        case '\n':

                            if (tEloC == 2)
                            {
                                char t_ts_ch2;
                                List<char> tchl = new List<char>();
                                while (++i < fileChunkSize && nextLine != (t_ts_ch2 = (char)fileChunk[i])) tchl.Add(t_ts_ch2);
                                if (i >= fileChunkSize) break;
                                if (curGameValid) localValidPGNs.Add(new string(tchl.ToArray()));
                                curGameValid = true;
                                lastGameByteOffset = i;
                                lookedThroughGames++;
                                tEloC = 0;
                            }
                            break;
                    }
                }
            }
            TLMDatabase.completedThreads++;
            TLMDatabase.games += lookedThroughGames;

            fileChunk = Array.Empty<byte>();
        }
    }

    public class TLMDatabaseThreadObject2
    {
        private string[] loadedUnprocessedStrs;
        private bool[] skippedChars;
        private int from, to;
        private IBoardManager bM;
        private int minMoves;
        private Dictionary<char, int> pieceTypeDict;
        private int[] rankArr;
        private List<string> tempStringListHolder = new List<string>();

        public TLMDatabaseThreadObject2(string[] pStrs, int pFrom, int pTo, IBoardManager pBM, bool[] pSkippedChars, int[] pRankArray, Dictionary<char, int> pPieceTypeDict)
        {
            loadedUnprocessedStrs = pStrs;
            from = pFrom;
            to = pTo;
            bM = pBM;
            skippedChars = pSkippedChars;
            minMoves = TLMDatabase.minMoves;
            rankArr = pRankArray;
            pieceTypeDict = pPieceTypeDict;
        }

        public void DatabaseThreadMethod(object obj)
        {
            for (int i = from; i < to; i++)
            {
                ProcessDatabaseV1Line(loadedUnprocessedStrs[i]);
            }

            TLMDatabase.processedPGNs.AddRange(tempStringListHolder);
            TLMDatabase.completedThreads++;
        }

        public void ProcessDatabaseV1Line(string pStr)
        {
            bM.LoadJumpState();

            int tL = pStr.Length, tMoveC = 0;
            List<char> tempL = new List<char>();
            bool valsOpened = false, legitGame = false;
            for (int i = 0; i < tL; i++)
            {
                char c = pStr[i];

                if (c == '.')
                {
                    legitGame = true;
                    int tI;
                    while (tempL.Count > 0 && Char.IsDigit(tempL[tI = tempL.Count - 1]))
                        tempL.RemoveAt(tI);
                    continue;
                }

                if (valsOpened && '}' == c)
                {
                    valsOpened = false;
                }
                else if ('{' == c)
                {
                    valsOpened = true;
                    tMoveC++;
                    tempL.Add(',');
                }
                else if (!valsOpened && !skippedChars[c]) tempL.Add(c);
            }

            if (!legitGame) return;

            string[] tspl = new string(tempL.ToArray()).Split(',');

            if (tMoveC < minMoves) return;

            List<char> newGameString = new List<char>() { ';' };

            for (int i = 0; i < tMoveC; i++)
            {
                List<Move> tMoves = new List<Move>();
                bM.GetLegalMoves(ref tMoves);

                string curSpl = tspl[i];
                int tPT = pieceTypeDict[curSpl[0]];

                if (tPT != 1) curSpl = curSpl.Remove(0, 1);

                int startLine = -1, startPos = -1, endPos = -1, promType = -1;
                bool isCapture = false, isRochade = false, isPromotion = false;

                if (curSpl[curSpl.Length - 2] == '=')  // Promotion (Könnte auch Promotion Capture sein)
                {
                    isPromotion = true;
                    promType = pieceTypeDict[curSpl[curSpl.Length - 1]];

                    if (curSpl.Length == 4) // Normale Promotion
                    {
                        endPos = SQUARES.NumberNotation(new string(new char[2] { curSpl[0], curSpl[1] }));
                    }
                    else // Capture Promotion
                    {
                        isCapture = true;
                        string[] t_ts_spl = curSpl.Split('x');

                        if (t_ts_spl[0].Length == 1) startLine = t_ts_spl[0][0] - 97;

                        endPos = SQUARES.NumberNotation(t_ts_spl[1]);
                    }

                }
                else if (curSpl.Contains('x'))  // Captures
                {
                    isCapture = true;
                    string[] t_ts_spl = curSpl.Split('x');

                    switch (t_ts_spl[0].Length)
                    {
                        case 1: // Line or Rank Specified
                            startLine = t_ts_spl[0][0] - 97;
                            break;

                        case 2: // Square Specified
                            startPos = SQUARES.NumberNotation(t_ts_spl[0]);
                            break;
                    }

                    endPos = SQUARES.NumberNotation(t_ts_spl[1]);
                }
                else if (curSpl[1] == 'O')
                {
                    isRochade = true;
                    if (curSpl.Length == 2) // Kingside Rochade
                        endPos = 6;
                    else // Queenside Rochade
                        endPos = 2;
                }
                else // Normaler Zug
                {
                    switch (curSpl.Length)
                    {
                        case 2: // Nothing extra specified
                            endPos = SQUARES.NumberNotation(new string(new char[2] { curSpl[0], curSpl[1] }));
                            break;
                        case 3: // Line or Rank Specified
                            startLine = curSpl[0] - 97;
                            endPos = SQUARES.NumberNotation(new string(new char[2] { curSpl[1], curSpl[2] }));
                            break;

                        case 4: // Square Specified
                            startPos = SQUARES.NumberNotation(new string(new char[2] { curSpl[0], curSpl[1] }));
                            endPos = SQUARES.NumberNotation(new string(new char[2] { curSpl[2], curSpl[3] }));
                            break;
                    }
                }

                int molc = tMoves.Count;
                Move? theMove = null;

                bool b = startPos == -1 && startLine == -1, rankAndNotFileSpecified;

                if (rankAndNotFileSpecified = startLine < -1) startLine += 48;

                for (int m = 0; m < molc; m++)
                {
                    Move curMove = tMoves[m];
                    if (curMove.pieceType == tPT && curMove.isRochade == isRochade && curMove.isPromotion == isPromotion && curMove.isCapture == isCapture)
                    {
                        if (isRochade)
                        {
                            if (endPos == curMove.endPos % 8)
                            {
                                theMove = curMove;
                                break;
                            }
                        }
                        else if (endPos == curMove.endPos)
                        {
                            if (isPromotion)
                            {
                                if (promType == curMove.promotionType)
                                {
                                    if (startLine == -1 || startLine == curMove.startPos % 8)
                                    {
                                        theMove = curMove;
                                        break;
                                    }
                                }
                            }
                            else if (b)
                            {
                                theMove = curMove;
                                break;
                            }
                            else if (rankAndNotFileSpecified)
                            {
                                if (startLine == rankArr[curMove.startPos])
                                {
                                    theMove = curMove;
                                    break;
                                }
                            }
                            else if (startPos == curMove.startPos)
                            {
                                theMove = curMove;
                                break;
                            }
                            else if (startLine == curMove.startPos % 8)
                            {
                                theMove = curMove;
                                break;
                            }
                        }
                    }
                }

                if (theMove == null)
                {
                    Console.WriteLine("NULL REFERENCE; HIER IST NOCH IWO N ERROR");
                    return;
                }

                string tS = NuCRe.GetNuCRe(theMove.moveHash);
                for (int m = 0; m < tS.Length; m++)
                    newGameString.Add(tS[m]);

                bM.PlainMakeMove(theMove);

                newGameString.Add(',');
            }

            string result = tspl[tMoveC];
            char resCH = '2';

            if (result.Length > 4) resCH = '1';
            else if (result[0] == '0') resCH = '0';

            newGameString.Add(resCH);

            tempStringListHolder.Add(new string(newGameString.ToArray()));
        }
    }

    #endregion

    public class FullyLoadedGame
    {
        public List<Move> Moves = new List<Move>();
        public int Outcome;

        public FullyLoadedGame(List<Move> pMoves, int pOutcome)
        {
            Moves = pMoves;
            Outcome = pOutcome;
        }
    }

    public class NNUE_DB_DATA
    {
        public List<ulong[]> Inputs = new List<ulong[]>();
        public int Outcome;

        public List<TrainingData> TrainingData = new List<TrainingData>();

        public NNUE_DB_DATA(List<ulong[]> pInputs, int pOutcome)
        {
            Inputs = pInputs;
            Outcome = pOutcome;

            int tL = pInputs.Count;
            for (int i = 0; i < tL; i++)
            {
                if (i < 5 || i > tL - 6) continue;
                TrainingData.Add(
                    new TrainingData(
                        ULONGARRS_TO_NNINPUTS(pInputs[i]),
                        new double[1] { Outcome }
                        //pInputs[i]
                    )
                );
            }
        }

        public static double[] ULONGARRS_TO_NNINPUTS(ulong[] pULArrs)
        {
            double[] rArr = new double[768];
            int a = 0;
            for (int j = 0; j < 12; j++)
                for (int i = 0; i < 64; i++)
                    rArr[a++] = (pULArrs[j] >> i) & 1;
            return rArr;
        }
    }
}
