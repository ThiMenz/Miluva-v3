using System.Diagnostics;

namespace ChessBot
{
    public static class LegacyEngineManager
    {
        public static void PlayTwoGamesBetweenTwoSnapshots(IBoardManager pBM1, IBoardManager pBM2, long pTime, string startFen)
        {
            pBM1.LoadFenString(startFen);
            pBM2.LoadFenString(startFen);
            pBM1.SetJumpState();
            pBM2.SetJumpState();

            Move? tMove = null;

            do {
                tMove = pBM1.ReturnNextMove(tMove, pTime);
                Console.WriteLine(tMove);
                tMove = pBM2.ReturnNextMove(tMove, pTime);
                Console.WriteLine(tMove);
            } while (tMove != null);
        }

        public static void CreateSnapshotCSFile()
        {
            string tstr = File.ReadAllText(PathManager.GetTXTPath("ENGINE/RawSnapshots"));

            string tstr2 = String.Concat("//AUTO GENERATED USING LegacyEngineManager.cs and the corresponding txt file\r\n#pragma warning disable\r\nusing System.Diagnostics; namespace ChessBot { ", tstr, "}");

            File.WriteAllText(PathManager.GetPath("ENGINE/Snapshots", "cs"), tstr2);

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

            string modCode = new string(tChrs.ToArray()).Replace("public BoardManager(string fen)", "public " + pSnapshotClassName + "(string fen)");

            File.AppendAllLines(PathManager.GetTXTPath("ENGINE/RawSnapshots"), new string[1] { modCode });

            CreateSnapshotCSFile();
        }
    }
}