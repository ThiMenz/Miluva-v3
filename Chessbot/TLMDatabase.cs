using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChessBot
{
    //99.98055251% der 22.6 GB großen PGN File
    public static class TLMDatabase
    {
        public const int chunkSize = 10_000_000;
        public const int threadMargin = 10000;
        public const int minElo = 2100;

        public static int completedThreads = 0;
        public static int games = 0;

        private const string lichessDatabasePath = @"C:\Users\tpmen\Downloads\lichess_db_standard_rated_2017-04.pgn\lichess_db_standard_rated_2017-04.pgn";

        private static List<TLMDatabaseThreadObject> threadTasks = new List<TLMDatabaseThreadObject>();

        private static List<string> validPGNs = new List<string>();

        private static BoardManager bM;

        public static void InitDatabase()
        {
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

            string tPath = Path.GetFullPath("OpeningDatabaseV1.txt").Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", "");
            string[] strs = File.ReadAllLines(tPath);
            int tL = strs.Length;

            bM = BOT_MAIN.boardManagers[ENGINE_VALS.PARALLEL_BOARDS - 1];
            bM.LoadFenString("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
            bM.SetJumpState();

            for (int i = 0; i < 10; i++)
                ProcessDatabaseV1Line(strs[i]);
        }

        private static void ReadShortenedAlgebraicChessNotation(string pStr)
        {

        }

        private static Dictionary<char, int> pieceTypeDict = new Dictionary<char, int>();

        private static void ProcessDatabaseV1Line(string pStr)
        {
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

                if (valsOpened && '}' == c) {
                    valsOpened = false;
                } else if ('{' == c) {
                    valsOpened = true;
                    tMoveC++;
                    tempL.Add(',');
                } else if (!valsOpened && c != ' ' && c != '?' && c != '!') {
                    tempL.Add(c);
                }
            }

            if (!legitGame) return;

            string tss = "";
            Console.WriteLine(tss = new string(tempL.ToArray()));

            string[] tspl = tss.Split(',');

            List<Move> tMoves = new List<Move>();
            for (int i = 0; i < tMoveC; i++)
            {
                bM.GetLegalMoves(ref tMoves);

                string curSpl = tspl[i];
                int tPT = pieceTypeDict[curSpl[0]], tmc = tMoves.Count;

                if (curSpl.Contains('x'))
                {

                }
                else if (curSpl[0] == 'O')
                {

                }
                else
                {

                }



            }
            bM.LoadJumpState();
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
    }

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
}
