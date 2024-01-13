using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;

namespace ChessBot
{

    public class ReLeTests
    {

    }


    #region | REINFORCEMENT LEARNING |

    public static class ReLe_AI_VARS
    {
        public const double MUTATION_PROPABILITY = 0.001;

        public const int GENERATION_SIZE = 50;
        public const int GENERATION_SURVIVORS = 7; //n^2-n = spots

        public const int GENERATION_GOAL_COUNT = 150; //n^2-n = spots

        public static int[] START_ARR;
        public const int ARR_MUTATION_INCR = 15;
        public const int PARAMS = 10;
    }

    public class ReLe_AIHandler
    {
        private System.Random rng = new System.Random();
        private ReLe_AIGeneration curGen;

        private const int TGAME_COUNT = 700000;
        private const int TPARAM_COUNT = 1000;
        private const int TQUANTITY_NotIncl_MAX = 4;

        private readonly int[] TQUANTITIES = new int[50]
        {
            1,1,1,1,1,1,1,1,1,1,
            1,1,1,1,1,1,1,1,1,1,
            1,1,1,1,1,1,1,1,1,1,
            1,1,1,1,1,1,1,1,2,2,
            2,2,2,2,2,2,2,3,3,4
        };

        private const int TQUANTITY_MaxMutate = 2;

        private List<(int[], int[], int)> tgames = new List<(int[], int[], int)>();

        private Stopwatch sw = new Stopwatch();

        private void PrintSW(string pInfo)
        {
            Console.WriteLine(pInfo + " [" + sw.ElapsedMilliseconds + "ms]");
        }

        public void GenerateRandomDataset()
        {
            sw.Start();

            int mutAdd = TQUANTITY_MaxMutate + 1, mutSub = -TQUANTITY_MaxMutate;

            int[] valsSearchedFor = GetRandomIntArr(TPARAM_COUNT, 0, 200);

            PrintIntArray(valsSearchedFor);

            int[] resses = new int[3];

            for (int i = 0; i < TGAME_COUNT; i++)
            {
                int[] gameQuantityValsP1 = GetRandomIntArr(TPARAM_COUNT, 0, TQUANTITIES[rng.Next(0, 10)]);
                int[] gameQuantityValsP2 = GetRandomMutatedIntArr(TPARAM_COUNT, mutSub, mutAdd, gameQuantityValsP1);

                int p1Val = MultiplyIntArrLikeDotProduct(TPARAM_COUNT, valsSearchedFor, gameQuantityValsP1);
                int p2Val = MultiplyIntArrLikeDotProduct(TPARAM_COUNT, valsSearchedFor, gameQuantityValsP2);

                int res = HandleResult(p1Val - p2Val);

                resses[res + 1]++;

                tgames.Add((gameQuantityValsP1, gameQuantityValsP2, res));
            }

            //int[] tav = new int[TPARAM_COUNT];
            //Array.Copy(valsSearchedFor, tav, TPARAM_COUNT);

            //for (int i = 0; i < TPARAM_COUNT; i++) tav[i] += rng.Next(-10, 11);

            //Console.WriteLine(REval(tav, 0, TGAME_COUNT));

            Console.WriteLine("P1: " + resses[2]);
            Console.WriteLine("D: " + resses[1]);
            Console.WriteLine("P2: " + resses[0]);

            PrintSW("Dataset Generation");

            int[] pArr = TLM_IntuitionTuning();
            ReLe_AI_VARS.START_ARR = pArr;

            ReLe_AIEvaluator.tGames = tgames;
            ReLe_AIEvaluator.tGameCount = tgames.Count;

            //TLM_RandomTuning(pArr);
            pArr = TLM_TrailAndErrorTuning(pArr);

            TLM_RandomTuning(pArr);
        }

        private int[] TLM_RandomTuning(int[] startArr) //95.4%
        {
            int[] bArr = new int[TPARAM_COUNT];

            Array.Copy(startArr, bArr, TPARAM_COUNT);

            double b = REval(bArr, 0, TGAME_COUNT);

            while (b < 1d) 
            {
                int[] pArr2 = SpecialRandomMutatedIntArr(TPARAM_COUNT, bArr);
                double d = REval(pArr2, 0, TGAME_COUNT);
                if (d > b)
                {
                    bArr = pArr2;
                    b = d;
                    PrintIntArray(bArr);
                    Console.WriteLine(b + " (" + d + ")");
                }
            }

            return bArr;
        }

        private int[] SpecialRandomMutatedIntArr(int tL, int[] pArr)
        {
            int[] rArr = new int[tL];
            for (int i = 0; i < tL; i++)
                rArr[i] = pArr[i] + GetSpecialIncrement();
            return rArr;
        }

        private int GetSpecialIncrement()
        {
            double d = rng.NextDouble();
            if (d < 0.4)
            {
                return d < 0.2 ? 1 : -1;
            }
            return 0;
        }

        private int[] TLM_IntuitionTuning()
        {
            int[] tTuneds = new int[TPARAM_COUNT];

            for (int i = 0; i < TGAME_COUNT; i++)
            {
                int tres = tgames[i].Item3;
                if (tres == 0) continue;
                int[] tp1 = tgames[i].Item1, tp2 = tgames[i].Item2;
                for (int j = 0; j < TPARAM_COUNT; j++)
                {
                    if (tres == 1)
                    {
                        tTuneds[j] += tp1[j] - tp2[j];
                    }
                    else
                    {
                        tTuneds[j] += tp2[j] - tp1[j];
                    }
                }
            }

            double curMult = 1d;

            while (!AreAllParamsUnderLimit(200, MultiplyParamsWithMult(tTuneds, curMult)))
            {
                curMult /= 2d;
                Console.WriteLine(curMult);
            }
            
            tTuneds = MultiplyParamsWithMult(tTuneds, curMult); 
            PrintIntArray(tTuneds);
            Console.WriteLine("Accuracy: " + (TEval(tTuneds) / (double)TGAME_COUNT));

            curMult = 1d;

            double curMultIncr = 1d;
            int bestEval = TEval(tTuneds);
            int[] bestFoundParams = tTuneds;
            while (curMultIncr > 0.0001d)
            {
                curMult += curMultIncr;
                int[] tArr = MultiplyParamsWithMult(tTuneds, curMult);
                int cEval = TEval(tArr);
                if (cEval > bestEval)
                {
                    bestFoundParams = tArr;
                    bestEval = cEval;
                }
                else
                {
                    curMult -= 2 * curMultIncr;
                    tArr = MultiplyParamsWithMult(tTuneds, curMult);
                    cEval = TEval(tArr);
                    if (cEval > bestEval)
                    {
                        bestFoundParams = tArr;
                        bestEval = cEval;
                    }
                    else
                    {
                        curMultIncr /= 2d;
                    }
                }
            }

            PrintSW("Intuition Tune Process");

            PrintIntArray(bestFoundParams);
            Console.WriteLine("Accuracy: " + (TEval(bestFoundParams) / (double)TGAME_COUNT));

            return bestFoundParams;
        }

        private bool AreAllParamsUnderLimit(int pLimit, int[] pParams)
        {
            for (int i = 0; i < TPARAM_COUNT; i++) if (Math.Abs(pParams[i]) > pLimit) return false;
            return true;
        }

        private int[] TLM_TrailAndErrorTuning(int[] pParams)
        {
            double bestEval = REval(pParams, 0, TGAME_COUNT);

            bool improved;

            int a = 1;

            while (a != 0)
            {
                improved = true;
                while (improved)
                {
                    improved = false;
                    for (int p = 0; p < TPARAM_COUNT; p++)
                    {
                        pParams[p] += a;
                        double cEval = REval(pParams, 0, TGAME_COUNT);
                        if (cEval > bestEval)
                        {
                            Console.WriteLine("+");
                            improved = true;
                            bestEval = cEval;
                        }
                        else
                        {
                            pParams[p] -= a * 2;
                            cEval = REval(pParams, 0, TGAME_COUNT);
                            if (cEval > bestEval)
                            {
                                Console.WriteLine("-");
                                improved = true;
                                bestEval = cEval;
                            }
                            else
                            {
                                pParams[p] += a;
                            }
                        }
                    }
                }
                if (a == 1) break;
                a /= 2;
                Console.WriteLine("Accuracy: " + REval(pParams, 0, TGAME_COUNT));
                PrintIntArray(pParams);
                PrintSW("A = " + a);
            }


            Console.WriteLine("Accuracy: " + REval(pParams, 0, TGAME_COUNT));
            PrintIntArray(pParams);
            PrintSW("Trail And Error Pruning");

            return pParams;
        }

        private int[] MultiplyParamsWithMult(int[] pArr, double pV)
        {
            int[] rArr = new int[TPARAM_COUNT];
            for (int i = 0; i < TPARAM_COUNT; i++)
            {
                rArr[i] = (int)(pArr[i] * pV);
            }
            return rArr;
        }

        private int TEval(int[] pParamSuggestions)
        {
            int rE = 0;
            for (int i = 0; i < TGAME_COUNT; i++)
            {
                (int[], int[], int) tG = tgames[i];

                //int tres = tgames[i].Item3;
                //int[] tp1 = tgames[i].Item1, tp2 = tgames[i].Item2;
                //
                //int p1Val = MultiplyIntArrLikeDotProduct(TPARAM_COUNT, pParamSuggestions, tp1);
                //int p2Val = MultiplyIntArrLikeDotProduct(TPARAM_COUNT, pParamSuggestions, tp2);

                if (EfficientGameEval(tG.Item1, tG.Item2, tG.Item3, pParamSuggestions))
                {
                    rE++;
                }
            }
            return rE;
        }

        private double REval(int[] pParamSuggestions, int gamesS, int gamesE)
        {
            int rE = 0;
            for (int i = gamesS; i < gamesE; i++)
            {
                (int[], int[], int) tG = tgames[i];



                //int tres = tgames[i].Item3;
                //int[] tp1 = tgames[i].Item1, tp2 = tgames[i].Item2;
                //
                //int p1Val = MultiplyIntArrLikeDotProduct(TPARAM_COUNT, pParamSuggestions, tp1);
                //int p2Val = MultiplyIntArrLikeDotProduct(TPARAM_COUNT, pParamSuggestions, tp2);

                if (EfficientGameEval(tG.Item1, tG.Item2, tG.Item3, pParamSuggestions))
                {
                    rE++;
                }
            }
            return rE / (double)(gamesE - gamesS);
        }

        private bool EfficientGameEval(int[] pArr1, int[] pArr2, int pRes, int[] pParamSug)
        {
            int r1 = 0, r2 = 0;
            for (int i = 0; i < TPARAM_COUNT; i++)
            {
                r1 += pArr1[i] * pParamSug[i];
                r2 += pArr2[i] * pParamSug[i];
            }
            return HandleResult(r1 - r2) == pRes;
        }

        //private const double TexelK = 0.2;
        //private double TexelTuningSigmoid(double pVal)
        //{
        //    return 2d / (1 + Math.Pow(10, -TexelK * pVal / 400));
        //}
        //
        //private double TexelSigmoid(double pVal)
        //{
        //    return 2d / (Math.Exp(-0.04d * pVal) + 1d);
        //}
        //
        //private double TexelSigmoidDerivative(double pVal)
        //{
        //    double d = Math.Exp(-0.04d * pVal);
        //    return 2d * d / (25d * (d + 1d) * (d + 1d));
        //}
        //
        //private double TexelCost(double pVal)
        //{
        //    return pVal * pVal;
        //}
        //
        //private double TexelCostDerivative(double pVal)
        //{
        //    return 2 * pVal;
        //}

        private void PrintIntArray(int[] pArr)
        {
            int tL = pArr.Length - 1;
            Console.Write("{ ");
            for (int i = 0; i < tL; i++)
            {
                Console.Write(pArr[i] + ", ");
            }
            Console.WriteLine(pArr[tL] + " }");
        }

        private int HandleResult(int pVal)
        {
            int absd = Math.Abs(pVal);
            if (absd > 160)
            {
                return Math.Sign(pVal);
            }
            return 0;
        }

        private int MultiplyIntArrLikeDotProduct(int tL, int[] pArr1, int[] pArr2)
        {
            int r = 0;
            for (int i = 0; i < tL; i++)
                r += pArr1[i] * pArr2[i];
            return r;
        }

        private int[] GetRandomIntArr(int tL, int min, int max)
        {
            int[] rArr = new int[tL];
            for (int i = 0; i < tL; i++)
                rArr[i] = rng.Next(min, max);
            return rArr;
        }

        private int[] GetRandomMutatedIntArr(int tL, int min, int max, int[] pArr)
        {
            int[] rArr = new int[tL];
            for (int i = 0; i < tL; i++)
                rArr[i] = pArr[i] + rng.NextDouble() < 0.03 ? rng.Next(min, max) : 0;
            return rArr;
        }

        public ReLe_AIHandler()
        {
            GenerateRandomDataset();
            return;
            //ReLe_AIEvaluator.fens = File.ReadAllLines(@"C:\Users\tpmen\Desktop\4 - Programming\41 - Unity & C#\MiluvaV3\Miluva-v3\Chessbot\FENS.txt");

            //for (int i = 0; i < 3; i++)
            //    for (int j = 0; j < 6; j++)
            //        ReLe_AIEvaluator.oppAIValues[i, j] = new int[64] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            curGen = new ReLe_AIGeneration(rng);
            for (int i = 0; i < ReLe_AI_VARS.GENERATION_GOAL_COUNT; i++)
            {
                ReLe_AIInstance[] topPerformingAIs = curGen.GetTopAIInstances();
                //ClearTextFile();
                AppendToText("- - - { Generation " + (i + 1) + ": } - - -\n\n");
                foreach (ReLe_AIInstance instance in topPerformingAIs)
                {
                    AppendToText(instance.ToString() + "\n");
                    AppendToText(GetAIArrayValues(instance) + "\n");
                    //Console.WriteLine(instance.ToString());
                    //Console.WriteLine(GetAIArrayValues(instance));
                }
                //Console.WriteLine(ReLe_AIEvaluator.boardManager.GetAverageDepth());
                curGen = new ReLe_AIGeneration(rng, topPerformingAIs);
            }
        }

        private string GetAIArrayValues(ReLe_AIInstance pAI)
        {
            string r = "int[] ReLe_AI_RESULT_VALS = new int[" + ReLe_AI_VARS.PARAMS + "] {";
            int[] tVals = pAI.digitArray;

            for (int i = 0; i < ReLe_AI_VARS.PARAMS; i++)
            {
                r += tVals[i] + ",";
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
                generationInstances[i] = new ReLe_AIInstance(rng, ReLe_AI_VARS.START_ARR, -ReLe_AI_VARS.ARR_MUTATION_INCR, ReLe_AI_VARS.ARR_MUTATION_INCR + 1);
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
                generationInstances[i] = new ReLe_AIInstance(rng, ReLe_AI_VARS.START_ARR, -ReLe_AI_VARS.ARR_MUTATION_INCR, ReLe_AI_VARS.ARR_MUTATION_INCR + 1);
            }
        }

        private ReLe_AIInstance CombineTwoAIInstances(ReLe_AIInstance ai1, ReLe_AIInstance ai2, System.Random rng)
        {
            int[] tempDigitArray = new int[ReLe_AI_VARS.PARAMS];
            for (int k = 0; k < 64; k++)
            {
                ReLe_AIInstance tAI = rng.NextDouble() < 0.5f ? ai1 : ai2;

                //Mutations
                if (rng.NextDouble() < ReLe_AI_VARS.MUTATION_PROPABILITY)
                {
                    tempDigitArray[k] = Math.Clamp(tAI.digitArray[k] + rng.Next(-20, 21), 0, 2000);
                    continue;
                }

                //Combination
                //tempDigitArray[i, j][k] = (rng.NextDouble() < 0.5) ? ai1.digitArray[i, j][k] : ai2.digitArray[i, j][k];

                tempDigitArray[k] = Math.Clamp(tAI.digitArray[k] + rng.Next(-4, 5), 0, 2000);

                //tempDigitArray[i, j][k] = Math.Clamp((ai1.digitArray[i, j][k] + ai2.digitArray[i, j][k]) / 2 + rng.Next(-3, 3), 0, 150);
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
        private static Random evalRNG = new Random();

        public static List<(int[], int[], int)> tGames = new List<(int[], int[], int)>();
        public static int tGameCount = 0;

        public static double EvaluateAIInstance(ReLe_AIInstance ai)
        {
            double eval = REval(ai.digitArray, 0, tGameCount);

            //double eval = 0d;
            //
            //for (int i = 0; i < 10; i++)
            //{
            //    string gameFen = fens[evalRNG.Next(fens.Length)];
            //
            //    //double t1, t2;
            //    boardManager.LoadFenString(gameFen);
            //    eval += boardManager.ReLePlayGame(ai.digitArray, oppAIValues, 500L);
            //    boardManager.LoadFenString(gameFen);
            //    eval -= boardManager.ReLePlayGame(oppAIValues, ai.digitArray, 500L);
            //    //Console.WriteLine(eval + "|" + t1 + " & " + t2);
            //}
            ai.SetEvaluationResults(eval);
            return eval;
        }

        private static int HandleResult(int pVal)
        {
            if (Math.Abs(pVal) > 160) return Math.Sign(pVal);
            return 0;
        }

        private static int MultiplyIntArrLikeDotProduct(int tL, int[] pArr1, int[] pArr2)
        {
            int r = 0;
            for (int i = 0; i < tL; i++)
                r += pArr1[i] * pArr2[i];
            return r;
        }

        private static double REval(int[] pParamSuggestions, int gamesS, int gamesE)
        {
            int rE = 0;
            for (int i = gamesS; i < gamesE; i++)
            {
                int tres = tGames[i].Item3;
                int[] tp1 = tGames[i].Item1, tp2 = tGames[i].Item2;

                int p1Val = MultiplyIntArrLikeDotProduct(ReLe_AI_VARS.PARAMS, pParamSuggestions, tp1);
                int p2Val = MultiplyIntArrLikeDotProduct(ReLe_AI_VARS.PARAMS, pParamSuggestions, tp2);

                if (tres == HandleResult(p1Val - p2Val))
                {
                    rE++;
                }
                else
                {
                    //rE--;
                }
            }
            return rE / (double)(gamesE - gamesS);
        }
    }

    public class ReLe_AIInstance
    {
        public int[] digitArray { private set; get; } = new int[ReLe_AI_VARS.PARAMS];

        private double evalResult;

        public ReLe_AIInstance(System.Random rng, int[] baseArr, int min, int max)
        {
            digitArray = GetRandomMutatedIntArr(rng, baseArr.Length, min, max, baseArr);
        }

        private int[] GetRandomMutatedIntArr(System.Random rng, int tL, int min, int max, int[] pArr)
        {
            int[] rArr = new int[tL];
            for (int i = 0; i < tL; i++)
                rArr[i] = pArr[i] + rng.Next(min, max);
            return rArr;
        }

        //private int[] Get64IntArray(System.Random rng, int pMax)
        //{
        //    return new int[64] {
        //        rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
        //        rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
        //        rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
        //        rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
        //        rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
        //        rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
        //        rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax),
        //        rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax), rng.Next(0, pMax)
        //    };
        //}

        public ReLe_AIInstance(int[] pDigitArray)
        {
            SetCharToDigitList(pDigitArray);
        }

        public void SetCharToDigitList(int[] digitRefArr)
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
}
