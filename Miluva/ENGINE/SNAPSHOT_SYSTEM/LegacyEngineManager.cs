using System.Diagnostics;

namespace Miluva
{
    public static class LegacyEngineManager
    {
        public static int clashObjectsFinished = 0;
        public static int curClashResult = 0;
        public static (int, int, int) curClashResultTuple = (0, 0, 0);
        public static int curClashGames = 0;

        public static List<GlickoEntity> snapshotGlickoEntities = new List<GlickoEntity>();

        private static List<string> curClashStrings = new List<string>();

        private const int GAMES_PER_SEASON = 11;
        public static void InitSnapshots()
        {
            string[] strs = File.ReadAllLines(PathManager.GetTXTPath("ENGINE/SNAPSHOT_SYSTEM/SnapshotResults"));

            foreach (string s in strs)
            {
                string[] tMSpl = s.Split(':');
                string[] tSpl = tMSpl[1].Split('|');

                snapshotGlickoEntities.Add(new GlickoEntity(tMSpl[0], Convert.ToDouble(tSpl[0]), Convert.ToDouble(tSpl[1]), Convert.ToDouble(tSpl[2])));
            }
        }

        public static GlickoEntity GetGlickoEntity(string pTypeName)
        {
            foreach (GlickoEntity ge in snapshotGlickoEntities)
                if (ge.NAME == pTypeName)
                    return ge;

            GlickoEntity r = new GlickoEntity(pTypeName);
            snapshotGlickoEntities.Add(r);
            SaveGlickoEntities();
            return r;
        }

        public static void SaveGlickoEntities()
        {
            int tL;
            string[] gEntries = new string[tL = snapshotGlickoEntities.Count];
            for (int i = 0; i < tL; i++)
            {
                GlickoEntity ge = snapshotGlickoEntities[i];
                gEntries[i] = ge.NAME + ":" + ge.ELO + "|" + ge.RD + "|" + ge.VOL;
            }
            File.WriteAllLines(PathManager.GetTXTPath("ENGINE/SNAPSHOT_SYSTEM/SnapshotResults"), gEntries);
        }

        public static string GetTypeOfIBoardManager(IBoardManager pBM)
        {
            string[] spl = pBM.GetType().ToString().Split('.');
            return spl[spl.Length - 1];
        }

        public static string GetTypeOfIBoardManagerArray(IBoardManager[] pBM)
        {
            string[] spl = pBM.GetType().ToString().Replace("[]", "").Split('.');
            return spl[spl.Length - 1];
        }

        public static void PlayBetweenTwoSnapshots(IBoardManager[] pBM1, IBoardManager[] pBM2, TimeFormat pTime, int pFENCount)
        {
            GlickoEntity ge1 = GetGlickoEntity(GetTypeOfIBoardManagerArray(pBM1));
            GlickoEntity ge2 = GetGlickoEntity(GetTypeOfIBoardManagerArray(pBM2));

            curClashResult = clashObjectsFinished = 0;

            int parallelBoards = pBM1.Length;

            int threadCount = pFENCount / parallelBoards;

            int tC = 0;

            for (int i = 0; i < parallelBoards - 1; i++)
            {
                List<string> tFENS = new List<string>();
                for (int t = 0; t < threadCount; t++) tFENS.Add(FEN_MANAGER.GetRandomStartFEN());

                EngineClashThreadObject ecto = new EngineClashThreadObject(pBM1[i], pBM2[i], pTime, tFENS);

                ThreadPool.QueueUserWorkItem(new WaitCallback(ecto.ThreadedMethod));
                tC += threadCount;
            }

            List<string> tFENSL = new List<string>();
            for (int t = tC; t < pFENCount; t++) tFENSL.Add(FEN_MANAGER.GetRandomStartFEN());

            EngineClashThreadObject ectoL = new EngineClashThreadObject(pBM1[parallelBoards - 1], pBM2[parallelBoards - 1], pTime, tFENSL);
            ThreadPool.QueueUserWorkItem(new WaitCallback(ectoL.ThreadedMethod));

            while (clashObjectsFinished != parallelBoards) Thread.Sleep(100);

            Glicko2.CalculateAllEntities(snapshotGlickoEntities);

            string idStr = "{" + DateTime.Now.ToShortDateString() + "} " + ge1.NAME + " [" + (int)ge1.ELO + "] vs " + ge2.NAME + " [" + (int)ge2.ELO + "]  (" + (pFENCount * 2) + " Games): " + curClashResultTuple.Item1 + "\\" + curClashResultTuple.Item2 + "\\" + curClashResultTuple.Item3;

            File.AppendAllLines(PathManager.GetTXTPath("ENGINE/SNAPSHOT_SYSTEM/Clashes"), new string[5] 
            {
                idStr,
                "ELO: " + GetRoundedDoubleLogString(ge1.ELO - ge1.PREV_ELO) + "\\" + GetRoundedDoubleLogString(ge2.ELO - ge2.PREV_ELO),
                "RD: " + GetRoundedDoubleLogString(ge1.RD - ge1.PREV_RD) + "\\" + GetRoundedDoubleLogString(ge2.RD - ge2.PREV_RD),
                "VOL: " + GetNonRoundedDoubleLogString(ge1.VOL - ge1.PREV_VOL) + "\\" + GetNonRoundedDoubleLogString(ge2.VOL - ge2.PREV_VOL),
                "----------------------------------------------------"
            } );

            Glicko2.UpdateAllEntities(snapshotGlickoEntities);

            SaveGlickoEntities();

            string tCGPath = PathManager.GetTXTPath("ENGINE/SNAPSHOT_SYSTEM/ClashGames");
            File.AppendAllText(tCGPath, "\n\n\n\n\n" + idStr + "\n\n\n");
            File.AppendAllLines(tCGPath, curClashStrings);
            curClashStrings.Clear();

            Console.WriteLine(curClashResult);
            Console.WriteLine(curClashResultTuple);

            //int totalResult = 0;
            //for (int i = 0; i < pFENCount; i++) {
            //    totalResult += PlayTwoGamesBetweenTwoSnapshots(pBM1, pBM2, pTime, FEN_MANAGER.GetRandomStartFEN());
            //}
            //Console.WriteLine(totalResult);
        }

        public static string GetRoundedDoubleLogString(double d)
        {
            return (d < 0 ? "" : "+") + ((int)(d * 10) / 10).ToString();
        }

        public static string GetNonRoundedDoubleLogString(double d)
        {
            return (d < 0 ? "" : "+") + d.ToString();
        }

        public static (int, int, int) PlayTwoGamesBetweenTwoSnapshots(IBoardManager pBM1, IBoardManager pBM2, TimeFormat pTime, string startFen)
        {
            (int, int, int) tRes = PlayGameBetweenTwoSnapshots(pBM1, pBM2, pTime, startFen);
            (int, int, int) tRes2 = PlayGameBetweenTwoSnapshots(pBM2, pBM1, pTime, startFen);

            return (tRes.Item1 + tRes2.Item3, tRes.Item2 + tRes2.Item2, tRes.Item3 + tRes2.Item1);
        }

        private static (int, int, int) PlayGameBetweenTwoSnapshots(IBoardManager pWhite, IBoardManager pBlack, TimeFormat pTime, string pFEN)
        {
            pWhite.SetTimeFormat(pTime);
            pBlack.SetTimeFormat(pTime);
            pWhite.LoadFenString(pFEN);
            pBlack.LoadFenString(pFEN);
            pWhite.SetJumpState();
            pBlack.SetJumpState();

            Move? tMove = null;
            int tState;
            List<string> movehashnucreStrs = new List<string>();
            do
            {
                tMove = pWhite.ReturnNextMove(tMove, 1L);
                if (tMove != null) movehashnucreStrs.Add(NuCRe.GetNuCRe(tMove.moveHash) + ",");
                tState = pWhite.GameState(false);
                //Console.WriteLine(tMove);
                if (tState != 3) break;
                tMove = pBlack.ReturnNextMove(tMove, 1L);
                if (tMove != null) movehashnucreStrs.Add(NuCRe.GetNuCRe(tMove.moveHash) + ",");
                tState = pBlack.GameState(true);
                //Console.WriteLine(tMove);
            } while (tState == 3);

            if (tState == 3) Console.WriteLine("?!?!?!?!??!");

            GlickoEntity ge1 = GetGlickoEntity(GetTypeOfIBoardManager(pWhite));
            GlickoEntity ge2 = GetGlickoEntity(GetTypeOfIBoardManager(pBlack));

            curClashGames++;
            new GlickoGame(ge1, ge2, tState, (curClashGames - (curClashGames % GAMES_PER_SEASON)) / GAMES_PER_SEASON);

            movehashnucreStrs.Add((tState+1).ToString());
            curClashStrings.Add(pFEN + ";" + String.Concat(movehashnucreStrs));

            (int, int, int) tRes = (0, 1, 0);
            if (tState == 1) tRes = (1, 0, 0);
            else if (tState == -1) tRes = (0, 0, 1);

            return tRes;
        }

        public static void CreateSnapshotCSFile()
        {
            string tstr = File.ReadAllText(PathManager.GetTXTPath("ENGINE/SNAPSHOT_SYSTEM/RawSnapshots"));

            string tstr2 = String.Concat("//AUTO GENERATED USING LegacyEngineManager.cs and the corresponding txt file\r\n#pragma warning disable\r\nusing System.Diagnostics; namespace Miluva { ", tstr, "}");

            File.WriteAllText(PathManager.GetPath("ENGINE/SNAPSHOT_SYSTEM/Snapshots", "cs"), tstr2);

            Console.WriteLine("CREATED SNAPSHOT CS FILE :)");
        }

        public static void CreateNewBoardManagerSnapshot(string pSnapshotClassName)
        {
            string pStr = PathManager.GetPath("ENGINE/BoardManager", "cs");

            string pCode = File.ReadAllText(pStr);

            bool[] notViableChars = new bool[512];

            for (int i = 0; i < 33; i++) notViableChars[i] = true;
            notViableChars[' '] = false;

            int tL = pCode.Length;
            List<char> tChrs = new List<char>();
            bool commentStarted = false, stringStarted = false, lastCharASlash = false, lastCharASpace = false, multiLineComment = false;

            string tempStr = "public class " + pSnapshotClassName + " : IBoardManager ";
            char[] tempChars = tempStr.ToCharArray();
            tChrs.AddRange(tempChars);

            int tV = -1;

            for (int i = 0; i < tL; i++)
            {
                char tCh = pCode[i];
                if (tCh == 'a' &&
                    pCode[++i] == 'n' &&
                    pCode[++i] == 'a' &&
                    pCode[++i] == 'g' &&
                    pCode[++i] == 'e' &&
                    pCode[++i] == 'r' &&
                    pCode[++i] == ' ' &&
                    pCode[++i] == ':' &&
                    pCode[++i] == ' ' &&
                    pCode[++i] == 'I' &&
                    pCode[++i] == 'B' &&
                    pCode[++i] == 'o' &&
                    pCode[++i] == 'a' &&
                    pCode[++i] == 'r' &&
                    pCode[++i] == 'd' &&
                    pCode[++i] == 'M' &&
                    pCode[++i] == 'a' &&
                    pCode[++i] == 'n' &&
                    pCode[++i] == 'a') tV = i + 4;
            }

            for (int i = tV; i < tL; i++)
            {
                char tCh = pCode[i];

                if (multiLineComment)
                {
                    if (tCh == '*' && pCode[++i] == '/') multiLineComment = false;
                    continue;
                }
                if (stringStarted)
                {
                    if (tCh == '"' && pCode[i - 1] != '\\') stringStarted = false;
                    tChrs.Add(tCh);
                    continue;
                }

                if (commentStarted)
                {
                    if (tCh != '\n') continue;
                    else commentStarted = false;
                }
                else if (tCh == '#') commentStarted = true;
                else if (tCh == '"')
                {
                    stringStarted = true;
                    tChrs.Add(tCh);
                    continue;
                }
                else if (tCh == '*' && lastCharASlash)
                {
                    multiLineComment = true;
                    tChrs.RemoveAt(tChrs.Count - 1);
                }
                else if (tCh == '/')
                {
                    if (lastCharASlash)
                    {
                        lastCharASlash = false;
                        commentStarted = true;
                        tChrs.RemoveAt(tChrs.Count - 1);
                    }
                    else
                    {
                        lastCharASlash = true;
                        tChrs.Add(tCh);
                    }
                }
                else if (!notViableChars[tCh])
                {
                    lastCharASlash = false;
                    if (tCh == ' ')
                    {
                        if (lastCharASpace) continue;
                        lastCharASpace = true;
                    }
                    else lastCharASpace = false;
                    tChrs.Add(tCh);
                }
            }

            tL = tChrs.Count;

            while (--tL > 0)
            {
                if (tChrs[tL] == '}')
                {
                    tChrs.RemoveAt(tL);
                    break;
                }
            }

            string modCode = new string(tChrs.ToArray()).Replace("public BoardManager(string fen)", "public " + pSnapshotClassName + "(string fen)").Replace("Console.WriteLine", "SNAPSHOT_WRITLINE_REPLACEMENT").Replace("Console.Write", "SNAPSHOT_WRITLINE_REPLACEMENT");

            File.AppendAllLines(PathManager.GetTXTPath("ENGINE/SNAPSHOT_SYSTEM/RawSnapshots"), new string[1] { modCode });

            CreateSnapshotCSFile();
        }
    }

    public class EngineClashThreadObject
    {
        private List<string> fens = new List<string>();
        private IBoardManager bm1, bm2;
        private TimeFormat time;

        public EngineClashThreadObject(IBoardManager pBM1, IBoardManager pBM2, TimeFormat pTime, List<string> pFens)
        {
            time = pTime;
            bm1 = pBM1;
            bm2 = pBM2;
            fens = pFens;
        }

        public void ThreadedMethod(object obj)
        {
            (int, int, int) tRes = (0, 0, 0);
            Console.WriteLine("[THREAD] >> " + fens.Count + " Games To Play .-.");

            for (int i = 0; i < fens.Count; i++)
            {
                (int, int, int) tRes_ts = LegacyEngineManager.PlayTwoGamesBetweenTwoSnapshots(bm1, bm2, time, fens[i]);
                Console.WriteLine(tRes_ts);
                tRes = (tRes_ts.Item1 + tRes.Item1, tRes_ts.Item2 + tRes.Item2, tRes_ts.Item3 + tRes.Item3);
            }

            Console.WriteLine("[THREAD] >> " + tRes);

            LegacyEngineManager.curClashResultTuple = (tRes.Item1 + LegacyEngineManager.curClashResultTuple.Item1, tRes.Item2 + LegacyEngineManager.curClashResultTuple.Item2, tRes.Item3 + LegacyEngineManager.curClashResultTuple.Item3);
            LegacyEngineManager.curClashResult += tRes.Item1 - tRes.Item3;
            LegacyEngineManager.clashObjectsFinished++;
        }


    }
}