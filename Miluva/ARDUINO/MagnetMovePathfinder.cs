﻿using MathNet.Numerics.Financial;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Numerics;

#pragma warning disable

namespace Miluva
{
    public class MagnetMoveSequence
    {
        public List<DMove> dirMoves { get; private set; } = new List<DMove>();
        public List<RMove> individualFields { get; private set; } = new List<RMove>();
        public List<CMove> compressedReversedMoves { get; private set; } = new List<CMove>();

        public int curField { get; private set; } = 0;
        public int backwardsMark { get; private set; } = 0;

        public override string ToString()
        {
            string s = "";
            foreach (RMove rm in individualFields) s += rm;
            return s;
        }

        #region | MOVE STRUCTS |

        public struct DMove // "Direction Move"
        {
            public Direction dir;
            public bool magnet;
            public bool captureAddOn;
            public enum Direction { Up, Down, Left, Right };

            public override string ToString()
            {
                string c = "O";
                if (dir == Direction.Left) c = "L";
                else if (dir == Direction.Right) c = "R";
                else if (dir == Direction.Down) c = "U";
                //if (magnet) MagnetMovePathfinder.FINAL_ACTIONS.Add(4);
                //MagnetMovePathfinder.FINAL_ACTIONS.Add((int)dir);
                //MagnetMovePathfinder.FINAL_ACTIONS.Add(((int)dir, magnet));
                return (magnet ? c : c.ToLower());
            }
        }
        public struct RMove // "Raw Move"
        {
            public int pos;
            public bool magnet;
            public enum Direction { Up, Down, Left, Right };

            public override string ToString()
            {
                return "[" + ((magnet) ? "m" : "x") + pos + "]";
            }
        }
        public struct CMove // "Compromized Move"
        {
            public int[] directpath;
            public ulong ulBoard;
        }

        #endregion

        #region | MOVE MANAGEMENT |

        private DMove.Direction ChangeDMovesPerspective(DMove dM)
        {
            if (dM.captureAddOn) return dM.dir;

            switch (ARDUINO_GAME_SETTINGS.MAIN_AXIS_LINE)
            {
                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.a1_h1:
                    return dM.dir;

                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.h8_a8:

                    switch (dM.dir)
                    {
                        case DMove.Direction.Left:
                            return DMove.Direction.Right;
                        case DMove.Direction.Right:
                            return DMove.Direction.Left;
                        case DMove.Direction.Up:
                            return DMove.Direction.Down;
                        case DMove.Direction.Down:
                            return DMove.Direction.Up;
                    }

                    break;

                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.a8_a1:

                    switch (dM.dir)
                    {
                        case DMove.Direction.Left:
                            return DMove.Direction.Down;
                        case DMove.Direction.Right:
                            return DMove.Direction.Up;
                        case DMove.Direction.Up:
                            return DMove.Direction.Left;
                        case DMove.Direction.Down:
                            return DMove.Direction.Right;
                    }

                    break;

                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.h1_h8:

                    switch (dM.dir)
                    {
                        case DMove.Direction.Left:
                            return DMove.Direction.Up;
                        case DMove.Direction.Right:
                            return DMove.Direction.Down;
                        case DMove.Direction.Up:
                            return DMove.Direction.Right;
                        case DMove.Direction.Down:
                            return DMove.Direction.Left;
                    }

                    break;
            }

            throw new Exception("§§§");
        }

        public void ChangePerspective()
        {
            List<DMove> newL = new List<DMove>();
            foreach (DMove move in dirMoves)
                newL.Add(new DMove() { dir = ChangeDMovesPerspective(move), magnet = move.magnet });

            dirMoves = newL;

            List<DMove> tdMoves = new List<DMove>();
            switch (ARDUINO_GAME_SETTINGS.MAIN_AXIS_LINE)
            {
                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.h8_a8:

                    for (int i = 0; i < 7; i++) tdMoves.Add(new DMove() { dir = DMove.Direction.Up, magnet = false });
                    for (int i = 0; i < 7; i++) tdMoves.Add(new DMove() { dir = DMove.Direction.Right, magnet = false });
                    for (int i = 0; i < 7; i++) dirMoves.Add(new DMove() { dir = DMove.Direction.Down, magnet = false });
                    for (int i = 0; i < 7; i++) dirMoves.Add(new DMove() { dir = DMove.Direction.Left, magnet = false });

                    break;

                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.h1_h8:

                    for (int i = 0; i < 7; i++) tdMoves.Add(new DMove() { dir = DMove.Direction.Up, magnet = false });
                    for (int i = 0; i < 7; i++) dirMoves.Add(new DMove() { dir = DMove.Direction.Down, magnet = false });

                    break;

                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.a8_a1:

                    for (int i = 0; i < 7; i++) tdMoves.Add(new DMove() { dir = DMove.Direction.Right, magnet = false });
                    for (int i = 0; i < 7; i++) dirMoves.Add(new DMove() { dir = DMove.Direction.Left, magnet = false });

                    break;
            }
            dirMoves.InsertRange(0, tdMoves);

            CleanUpDirectionMoves();

            foreach (DMove move in dirMoves)
                MagnetMovePathfinder.FINAL_ACTIONS.Add(((int)move.dir, move.magnet));
        }

        public int GetDirMoveCountWithMagnet()
        {
            int ccount = 0;
            foreach (DMove dmove in dirMoves)
            {
                if (dmove.magnet) ccount++; 
            }
            return ccount;
        }

        public string GetDirectionMoveString(bool pCapture)
        {
            string s = "";
            int f = 0;

            // Konvertieren der rohen Felder in Bewegungsrichtungen
            for (int i = 0; i < individualFields.Count; i++)
            {
                int dif = individualFields[i].pos - f;

                if (dif == 0)
                {
                    throw new Exception("§§§");
                }

                DMove.Direction tdir = DMove.Direction.Left;
                if (dif == 1 || individualFields[i].pos == 64) tdir = DMove.Direction.Up; //Up
                else if (dif == -1 || f == 64) tdir = DMove.Direction.Down; //Down
                else if (dif > 1) tdir = DMove.Direction.Right; //Right
                //DMove.Direction tdir = GetDirectionBasedOnMainAxisAlignment(dif, f, i);
                //dirMoves.Add(new DMove() { dir = tdir, magnet = individualFields[i].magnet });
                dirMoves.Add(new DMove()
                {
                    dir = tdir,
                    magnet = individualFields[i].magnet //((i == 0 ? individualFields[i].magnet : ((individualFields[i].magnet && !individualFields[i - 1].magnet) || (!individualFields[i].magnet && individualFields[i - 1].magnet))))
                });
                f = individualFields[i].pos;
            }

            if (pCapture) AppendCaptureAddition();

            CleanUpDirectionMoves();

            //int indivFC = individualFields.Count;
            //GetToFieldWithoutMagnet(0);
            //
            //for (int i = indivFC; i < individualFields.Count; i++)
            //{
            //    int dif = individualFields[i].pos - f;
            //    DMove.Direction tdir = DMove.Direction.Left;
            //    if (dif == 1) tdir = DMove.Direction.Up; //Up
            //    else if (dif == -1) tdir = DMove.Direction.Down; //Down
            //    else if (dif > 1) tdir = DMove.Direction.Right; //Right
            //    dirMoves.Add(new DMove() { dir = tdir, magnet = false });
            //    f = individualFields[i].pos;
            //}

            for (int i = 0; i < dirMoves.Count; i++) s += dirMoves[i];
            return s;
        }

        public DMove.Direction GetDirectionBasedOnMainAxisAlignment(int dif, int f, int i)
        {
            DMove.Direction tdir;
            switch (ARDUINO_GAME_SETTINGS.MAIN_AXIS_LINE) {
                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.a1_h1:
                    tdir = DMove.Direction.Left;
                    if (dif == 1 || individualFields[i].pos == 64) tdir = DMove.Direction.Up;
                    else if (dif == -1 || f == 64) tdir = DMove.Direction.Down;
                    else if (dif > 1) tdir = DMove.Direction.Right;
                    return tdir;
                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.h8_a8:
                    tdir = DMove.Direction.Right;
                    if (dif == 1 || individualFields[i].pos == 64) tdir = DMove.Direction.Down;
                    else if (dif == -1 || f == 64) tdir = DMove.Direction.Up;
                    else if (dif > 1) tdir = DMove.Direction.Left;
                    return tdir;
                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.h1_h8:
                    tdir = DMove.Direction.Up;
                    if (dif == 1 || individualFields[i].pos == 64) tdir = DMove.Direction.Right;
                    else if (dif == -1 || f == 64) tdir = DMove.Direction.Left;
                    else if (dif > 1) tdir = DMove.Direction.Down;
                    return tdir;
                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.a8_a1:
                    tdir = DMove.Direction.Down;
                    if (dif == 1 || individualFields[i].pos == 64) tdir = DMove.Direction.Left;
                    else if (dif == -1 || f == 64) tdir = DMove.Direction.Right;
                    else if (dif > 1) tdir = DMove.Direction.Up;
                    return tdir;
            }
            throw new Exception("Main Axis Aligment caused an error :( [GetDirectionBasedOnMainAxisAlignment]");
        }

        public void CleanUpDirectionMoves()
        {
            bool b = true;
            //while (b)
            //{
            //    b = false;
            //    for (int i = 1; i < dirMoves.Count; i++)
            //    {
            //        // Sobald ein Magnet sich hin & her mit dem gleichen Magnet-Status bewegt, wird diese Sequenz als unnötig angesehen und gelöscht
            //        if (IsContradictingDirection(dirMoves[i].dir, dirMoves[i - 1].dir) && dirMoves[i].magnet == dirMoves[i - 1].magnet)
            //        {
            //            dirMoves.RemoveAt(i - 1);
            //            dirMoves.RemoveAt(i - 1);
            //            b = true;
            //        }
            //    }
            //}

            List<DMove> tdmoves = new List<DMove>();

            int tX = 0, tY = 0;
            for (int i = 0; i < dirMoves.Count; i++)
            {
                DMove.Direction dir = dirMoves[i].dir;
                if (dirMoves[i].magnet || dirMoves[i].captureAddOn)
                {
                    if (tX != 0 || tY != 0)
                    {
                        DMove.Direction xdir = tX > 0 ? DMove.Direction.Right : DMove.Direction.Left;
                        DMove.Direction ydir = tY > 0 ? DMove.Direction.Up : DMove.Direction.Down;

                        for (int y = 0; y < Math.Abs(tY); y++) tdmoves.Add(new DMove() { dir = ydir, magnet = false });
                        for (int x = 0; x < Math.Abs(tX); x++) tdmoves.Add(new DMove() { dir = xdir, magnet = false });

                        tX = tY = 0;
                    }

                    tdmoves.Add(dirMoves[i]);
                }
                else
                {
                    if (dir == DMove.Direction.Up) tY++;
                    else if (dir == DMove.Direction.Down) tY--;
                    else if (dir == DMove.Direction.Left) tX--;
                    else if (dir == DMove.Direction.Right) tX++;
                }
            }

            DMove.Direction xdirE = tX > 0 ? DMove.Direction.Right : DMove.Direction.Left;
            DMove.Direction ydirE = tY > 0 ? DMove.Direction.Up : DMove.Direction.Down;

            for (int y = 0; y < Math.Abs(tY); y++) tdmoves.Add(new DMove() { dir = ydirE, magnet = false });
            for (int x = 0; x < Math.Abs(tX); x++) tdmoves.Add(new DMove() { dir = xdirE, magnet = false });
            dirMoves = tdmoves;
        }

        private DMove.Direction GetContradictingDirection(DMove.Direction dir)
        {
            if (dir == DMove.Direction.Up) return DMove.Direction.Down;
            if (dir == DMove.Direction.Down) return DMove.Direction.Up;
            if (dir == DMove.Direction.Left) return DMove.Direction.Right;
            return DMove.Direction.Left;
        }

        // Überprüft ob zwei Richtungen genau entgegengesetzt sind
        private bool IsContradictingDirection(DMove.Direction dir1, DMove.Direction dir2)
        {
            if (dir1 == DMove.Direction.Up && dir2 == DMove.Direction.Down) return true;
            if (dir1 == DMove.Direction.Down && dir2 == DMove.Direction.Up) return true;
            if (dir1 == DMove.Direction.Left && dir2 == DMove.Direction.Right) return true;
            if (dir1 == DMove.Direction.Right && dir2 == DMove.Direction.Left) return true;
            return false;
        }

        // Finalisiert die rohe Move Sequenz, indem es alle "Blocker", also die Figuren die zur Seite gemoved wurde, wieder an ihre Position zurückstellt
        public void FinishMoveSequence()
        {
            if (individualFields.Count == 1)
            {
                GetToFieldWithoutMagnet(0);
                return;
            }

            //individualFields.Add(new RMove() { pos = curField, magnet = true });

            backwardsMark = individualFields.Count - 1;

            bool tMag = false;
            for (int i = individualFields.Count; --i > 0;)
            {
                RMove tRM = individualFields[i];
                int dif = tRM.pos - individualFields[i - 1].pos;

                if (!tRM.magnet && !tMag) tMag = true;

                individualFields.Add(new RMove() { pos = curField = tRM.pos - dif, magnet = tRM.magnet && tMag });
            }
            for (int i = individualFields.Count; --i > 0;)
            {
                if (individualFields[i].magnet)
                {
                    curField = individualFields[i].pos;
                    GetToFieldWithoutMagnet(0);
                    break;
                }
                else individualFields.RemoveAt(i);
            }


            //for (int i = compressedReversedMoves.Count - nonReversedAmount; i-- > 0; )
            //{
            //    AddIndividualMovesFromPath(compressedReversedMoves[i].directpath, compressedReversedMoves[i].ulBoard, 0ul, false);
            //}
        }

        public void AddIndividualMovesFromPath(int[] path, ulong blockedTiles, ulong blockedTilesAfterwards, bool normalPath)
        {
            // Abspeichern als Compressed Move, sobald dies ein "Blocker-bewegender" Path ist
            if (normalPath)
            {
                compressedReversedMoves.Add(new CMove()
                {
                    directpath = ReverseIntArray(path),
                    ulBoard = blockedTilesAfterwards
                });
            }

            // Kalkulation der benötigten Züge zum Ausführen des gegebenen Paths in Beachtung der blockierten Felder
            GetToFieldWithoutMagnet(path[0]);
            AddIndividualMoves(path, blockedTiles);
            curField = individualFields[individualFields.Count - 1].pos;
        }

        private void GetToFieldWithoutMagnet(int field)
        {
            int pathModolu = field % 8, sign = Math.Sign(pathModolu - curField % 8), sign2 = Math.Sign(field - curField) * 8;
            List<int> tempPath = new List<int>() { curField };

            // X-Bewegung
            while (curField % 8 != pathModolu)
            {
                curField += sign;
                tempPath.Add(curField);
            }

            // Y-Bewegung
            while (curField != field)
            {
                curField += sign2;
                tempPath.Add(curField);
            }

            AddIndividualMoves(tempPath.ToArray());
        }

        private void InsertInTheBeginningToFieldWithoutMagnet(int field)
        {
            curField = 0;
            int pathModolu = field % 8, sign = Math.Sign(pathModolu - curField % 8), sign2 = Math.Sign(field - curField) * 8;
            List<RMove> tempPath = new List<RMove>() { };

            // X-Bewegung
            while (curField % 8 != pathModolu)
            {
                curField += sign;
                tempPath.Add(new RMove() { pos = curField, magnet = false });
            }

            // Y-Bewegung
            while (curField != field)
            {
                curField += sign2;
                tempPath.Add(new RMove() { pos = curField, magnet = false });
            }

            individualFields.InsertRange(0, tempPath);
        }

        private void AddIndividualMoves(int[] path)
        {
            // Fügt alle Felder außer das Erste hinzu, da dieses immer durch den vorherigen Path hinzugefügt wird
            for (int i = 1; i < path.Length; i++)
            {
                individualFields.Add(new RMove() { pos = path[i], magnet = false });
            }
        }

        private void AddIndividualMoves(int[] path, ulong blockedTiles)
        {
            // Ziehen der Figur über freie Felder
            int a = 1;
            while (a < path.Length && !ULONG_OPERATIONS.IsBitOne(blockedTiles, path[a]))
            {
                curField = path[a];
                individualFields.Add(new RMove() { pos = path[a], magnet = true });
                a++;
            }

            // Bewegung des Magnets zum Ende des Paths
            GetToFieldWithoutMagnet(path[path.Length - 1]);
            //List<int> tempPath = new List<int>();
            //for (int i = a + 1; i < path.Length; i++) tempPath.Add(path[i]);
            //AddIndividualMoves(tempPath.ToArray());


            // Ziehen aller Figuren die auf dem Path der zu ziehenden Figur stehen (bis hinzu der tatsächlichen Figur am Ende)
            for (int i = path.Length - 1; i >= a; i--)
            {
                individualFields.Add(new RMove() { pos = path[i - 1], magnet = false });
                individualFields.Add(new RMove() { pos = path[i], magnet = true });
                curField = path[i];
                if (i == a) break;
                individualFields.Add(new RMove() { pos = path[i - 1], magnet = false });
            }
        }

        private int[] ReverseIntArray(int[] pArr)
        {
            int[] r = new int[pArr.Length];
            int a = 0;
            for (int i = pArr.Length; i-- > 0;) r[a++] = pArr[i];
            return r;
        }

        public void AppendCaptureAddition()
        {
            bool isUpwardsMovement = individualFields[backwardsMark].pos == MagnetMovePathfinder.captureSquaresByMainAxisAlignment[(int)ARDUINO_GAME_SETTINGS.MAIN_AXIS_LINE, 0];

            //Console.WriteLine(individualFields[backwardsMark].pos + " | " + MagnetMovePathfinder.captureSquaresByMainAxisAlignment[(int)ARDUINO_GAME_SETTINGS.MAIN_AXIS_LINE, 0]);

            dirMoves.Insert(backwardsMark + 1, new DMove() { dir = isUpwardsMovement ? DMove.Direction.Down : DMove.Direction.Left, magnet = false, captureAddOn = true });
            dirMoves.Insert(backwardsMark + 1, new DMove() { dir = isUpwardsMovement ? DMove.Direction.Up : DMove.Direction.Right, magnet = true, captureAddOn = true });
        }

        #endregion
    }

    public static class MagnetMovePathfinder
    {
        public static List<(int, bool)> FINAL_ACTIONS = new List<(int, bool)>(); 

        private static Vector2 bottomDownVec;
        private static float squareSize, maxDistance;
        private static Color[] squareColors;

        private static int pathFinderPoints = 0;
        private static int blockedSubPathRecordLength;
        private static int[] blockedSubPathRecord;
        private static ulong blockedSquares = 0ul;
        private static int[] pathFinderPointArr = new int[2] { -1, -1 };
        public static SquarePoint[] squares { get; private set; } = new SquarePoint[64];

        private static int waterFlowPathLength;
        private static List<ulong> bestWaterFlowPathBlockSkipPositions = new List<ulong>();
        private static Dictionary<ulong, int[]> waterFlowPathsDict = new Dictionary<ulong, int[]>();


        private static bool setupped = false;

        #region | PATHFINDER |
        private static void CheckForSetup()
        {
            FINAL_ACTIONS.Clear();
            if (!setupped)
            {
                setupped = true;
                for (int i = 0; i < 8; i++)
                {
                    Vector2 v = new Vector2(squareSize * i, 0f);
                    for (int j = 0; j < 8; j++)
                    {
                        List<int> tempL = new List<int>();

                        if (i != 0) tempL.Add((i - 1) * 8 + j);
                        if (i != 7) tempL.Add((i + 1) * 8 + j);
                        if (j != 0) tempL.Add(i * 8 + j - 1);
                        if (j != 7) tempL.Add(i * 8 + j + 1);

                        squares[i * 8 + j] = new SquarePoint()
                        {
                            pos = bottomDownVec + v,
                            x = i,
                            y = j,
                            id = i * 8 + j,
                            directNeighbors = tempL
                        };
                        v.Y += squareSize;
                    }
                }
            }
        }

        public static MagnetMoveSequence CalculateRochadePath(ulong pblockedSquares, int startSquareIndexKING, int endSquareIndexKING, int startSquareIndexROOK, int endSquareIndexROOK)
        {
            MagnetMoveSequence mms1 = CalculatePath(pblockedSquares, startSquareIndexKING, endSquareIndexKING, false);
            pblockedSquares = ULONG_OPERATIONS.SetBitToZero(ULONG_OPERATIONS.SetBitToOne(pblockedSquares, endSquareIndexKING), startSquareIndexKING);
            MagnetMoveSequence mms2 = CalculatePath(pblockedSquares, startSquareIndexROOK, endSquareIndexROOK, false);

            FINAL_ACTIONS.Clear();
            MagnetMoveSequence combinedMMS = new MagnetMoveSequence();
            combinedMMS.dirMoves.AddRange(mms1.dirMoves);
            combinedMMS.dirMoves.AddRange(mms2.dirMoves);
            combinedMMS.GetDirectionMoveString(false);

            return combinedMMS;
        }

        public static int[,] captureSquaresByMainAxisAlignment = new int[4, 2] {
            { 60, 32 },
            { 32, 3 },
            { 3, 31 },
            { 31, 60 }
        };

        public static MagnetMoveSequence CalculateCapturePath(ulong pblockedSquares, int startSquareIndex, int endSquareIndex)
        {
            // { a8_a1, h8_a8, h1_h8, a1_h1 };
            // 3; 31; 32; 60

            MagnetMoveSequence mms1 = CalculatePath(pblockedSquares, endSquareIndex, captureSquaresByMainAxisAlignment[(int)ARDUINO_GAME_SETTINGS.MAIN_AXIS_LINE, 0], true);
            MagnetMoveSequence mms3 = CalculatePath(pblockedSquares, endSquareIndex, captureSquaresByMainAxisAlignment[(int)ARDUINO_GAME_SETTINGS.MAIN_AXIS_LINE, 1], true);
            int mms1C = mms1.GetDirMoveCountWithMagnet(), mms3C = mms3.GetDirMoveCountWithMagnet();

            pblockedSquares = ULONG_OPERATIONS.SetBitToZero(pblockedSquares, endSquareIndex);
            MagnetMoveSequence mms2 = CalculatePath(pblockedSquares, startSquareIndex, endSquareIndex, false);

            FINAL_ACTIONS.Clear();
            MagnetMoveSequence combinedMMS = new MagnetMoveSequence();
            combinedMMS.dirMoves.AddRange(mms1C < mms3C ? mms1.dirMoves : mms3.dirMoves);
            combinedMMS.dirMoves.AddRange(mms2.dirMoves);
            combinedMMS.GetDirectionMoveString(false);

            return combinedMMS;
        }

        public static MagnetMoveSequence CalculateEnPassantPath(ulong pblockedSquares, int startSquareIndex, int endSquareIndex, int enPassantSquareIndex)
        {
            MagnetMoveSequence mms1 = CalculatePath(pblockedSquares, enPassantSquareIndex, captureSquaresByMainAxisAlignment[(int)ARDUINO_GAME_SETTINGS.MAIN_AXIS_LINE, 0], true);
            MagnetMoveSequence mms3 = CalculatePath(pblockedSquares, enPassantSquareIndex, captureSquaresByMainAxisAlignment[(int)ARDUINO_GAME_SETTINGS.MAIN_AXIS_LINE, 1], true);
            int mms1C = mms1.GetDirMoveCountWithMagnet(), mms3C = mms3.GetDirMoveCountWithMagnet();

            pblockedSquares = ULONG_OPERATIONS.SetBitToZero(pblockedSquares, enPassantSquareIndex);
            MagnetMoveSequence mms2 = CalculatePath(pblockedSquares, startSquareIndex, endSquareIndex, false);

            FINAL_ACTIONS.Clear();
            MagnetMoveSequence combinedMMS = new MagnetMoveSequence();
            combinedMMS.dirMoves.AddRange(mms1C < mms3C ? mms1.dirMoves : mms3.dirMoves);
            combinedMMS.dirMoves.AddRange(mms2.dirMoves);
            combinedMMS.GetDirectionMoveString(false);

            return combinedMMS;
        }

        public static MagnetMoveSequence CalculatePath(ulong pblockedSquares, int startSquareIndex, int endSquareIndex, bool isCapturePath)
        {
            CheckForSetup();

            //Console.WriteLine(startSquareIndex + "|" + endSquareIndex);

            Stopwatch sw = Stopwatch.StartNew();
            List<int[]> paths = Dijkstra(startSquareIndex, endSquareIndex, pblockedSquares);
            //WaterflowPathfinder(pblockedSquares, startSquareIndex, endSquareIndex);
            //Console.WriteLine(paths.Count);

            float pathCount = (float)paths.Count;
            MagnetMoveSequence shortestMoveSeq = null;
            int shortestMoveSeqLength = int.MaxValue;
            foreach (int[] path in paths)
            {
                MagnetMoveSequence mms = BlockerSubPathFinder(path, pblockedSquares);
                int tempField = mms.curField;
                mms.AddIndividualMovesFromPath(path, 0ul, 0ul, false);

                mms.FinishMoveSequence();

                //mms.FinishMoveSequence();
                if (mms.individualFields.Count < shortestMoveSeqLength)
                {
                    shortestMoveSeq = mms;
                    shortestMoveSeqLength = mms.individualFields.Count;
                }
            }

            sw.Stop();
            //Console.WriteLine("ElapsedTicks: " + sw.ElapsedTicks);
            shortestMoveSeq.GetDirectionMoveString(isCapturePath);

            return shortestMoveSeq;
        }

        /// <summary>
        /// Diese Methode gibt alle Paths zurück, welche vorgenommen werden müssen um den übergebenen Path zu ermöglichen
        /// </summary>
        /// <param name="path"> Der Path der ermöglicht werden soll </param>
        /// <param name="blockerUL"> Die aktuell blockierten Felder in Form einer 64-bit Zahl </param>
        /// <returns></returns>
        private static MagnetMoveSequence BlockerSubPathFinder(int[] path, ulong blockerUL)
        {
            // Verarbeite den übergebenen Path in die benötigten Variablen
            ulong completelyExcludedFields = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToOne(0ul, path[0]), path[path.Length - 1]);
            ulong excludedUL = 0ul;
            List<int> blockers = new List<int>();
            for (int i = path.Length - 1; --i > 0;)
            {
                excludedUL = ULONG_OPERATIONS.SetBitToOne(excludedUL, path[i]);
                if (ULONG_OPERATIONS.IsBitOne(blockerUL, path[i]))
                {
                    blockers.Add(path[i]);
                }
            }

            if (ULONG_OPERATIONS.IsBitOne(blockerUL, path[path.Length - 1]))
            {
                blockers.Add(path[path.Length - 1]);
            }

            int[] subPath;
            int subPathScore, blocker;
            List<int[]> returnPaths = new List<int[]>();
            MagnetMoveSequence mms = new MagnetMoveSequence();

            // Schiebe solange, bis der Path vollständig durchführbar ist
            while (blockers.Count != 0)
            {
                subPathScore = 10000;
                subPath = null;
                blocker = 0;

                // Iteration durch jedes noch blockierte Feld
                for (int i = blockers.Count; --i > -1;)
                {
                    // Herausfinden des kürzesten Paths um die Figur aus dem Weg zu schieben
                    blockedSubPathRecord = null;
                    blockedSubPathRecordLength = 10000;
                    RecursiveBlockerSubPathfinder(blockers[i], 0, completelyExcludedFields, blockerUL, excludedUL, new List<int>() { blockers[i] });

                    // Falls dieser kürzer ist, als jeder bisher überprüfte Path: Ersetzen
                    if (subPathScore > blockedSubPathRecordLength)
                    {
                        subPathScore = blockedSubPathRecordLength;
                        subPath = blockedSubPathRecord;
                        blocker = i;
                    }
                }

                // Aktualisierung der Blockierungen und der Path-Liste, welche später zurück gegeben wird
                mms.AddIndividualMovesFromPath(subPath, ULONG_OPERATIONS.SetBitToZero(blockerUL, subPath[0]),
                    blockerUL = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(blockerUL, subPath[0]), subPath[subPath.Length - 1]), true);
                blockers.RemoveAt(blocker);
                returnPaths.Add(subPath);
            }
            return mms;
        }

        private static void RecursiveBlockerSubPathfinder(int curField, int curScore, ulong completelyExcludedFields, ulong blockerUL, ulong excludedPathUL, List<int> fields)
        {
            // Abbrechen dieser Tiefe der Suche sobald sich der gewählte Path als länger als ein bereits Gefundener herrausstellt
            if (curScore >= blockedSubPathRecordLength) return;

            // Aus Effizienz Gründen diese Pointer Deklarierung
            List<int> tempList = squares[curField].directNeighbors;

            // Iterieren durch jedes benachbarte Feld
            for (int i = tempList.Count; --i > -1;)
            {
                int tempIndex = tempList[i];

                // Falls dieses Feld bereits beschritten wurde oder das Start/End Feld des originalen Paths ist, bietet dieses Feld keine Möglichkeit zur Lösung etwas beizutragen
                if (ULONG_OPERATIONS.IsBitOne(completelyExcludedFields, tempIndex) && curScore != 0) continue;

                fields.Add(tempIndex);
                if (ULONG_OPERATIONS.IsBitOne(excludedPathUL, tempIndex))
                {
                    // Die Bewegung auf dem Path ist legitim und benötigt nur einen Schiebeaufwand von 1 pro Feld
                    RecursiveBlockerSubPathfinder(tempIndex, curScore + 1, ULONG_OPERATIONS.SetBitToOne(completelyExcludedFields, tempIndex), blockerUL, excludedPathUL, fields);
                }
                else if (ULONG_OPERATIONS.IsBitOne(blockerUL, tempIndex))
                {
                    // Die Bewegung auf ein blockiertes Feld benötigt pro Feld einen Schiebeaufwand von 5
                    RecursiveBlockerSubPathfinder(tempIndex, curScore + 5, ULONG_OPERATIONS.SetBitToOne(completelyExcludedFields, tempIndex), blockerUL, excludedPathUL, fields);
                }
                else if (curScore != 0 || ULONG_OPERATIONS.IsBitZero(completelyExcludedFields, tempIndex))
                {
                    // Sobald ein freies Feld erreicht wurde, wird dieser neue beste Path abgespeichert
                    blockedSubPathRecord = fields.ToArray();
                    blockedSubPathRecordLength = curScore;
                    fields.RemoveAt(fields.Count - 1);
                    break;
                }
                fields.RemoveAt(fields.Count - 1);
            }
        }

        /// <summary>
        /// Dieser Pathfinder findet, falls vorhanden, den kürzesten Weg zwischen zwei Feldern
        /// </summary>
        /// <param name="pblockedSquares"> Die aktuell blockierten Felder in Form einer 64-bit Zahl </param>
        /// <param name="startField"> Das Index des Felds an dem die Suche beginnt </param>
        /// <param name="endField"> Das Index des Felds an dem der Path enden soll </param>
        /// <returns> Alle  </returns>
        private static List<int[]> WaterflowPathfinder(ulong pblockedSquares, int startField, int endField)
        {
            // Lösche die Daten der letzten Suche (falls diese stattgefunden haben sollte)
            bestWaterFlowPathBlockSkipPositions.Clear();
            waterFlowPathsDict.Clear();
            int a = 0;

            // Versuche die Wege mit den am wenigsten blockierten Feldern zu finden
            while (a < 8 && bestWaterFlowPathBlockSkipPositions.Count == 0)
            {
                waterFlowPathLength = 128;
                RecursiveWaterflowPathfinder(0, startField, endField, startField, a,
                    ULONG_OPERATIONS.SetBitToOne(pblockedSquares, startField), ULONG_OPERATIONS.SetBitToOne(0ul, startField), 0ul, new List<int>() { startField });
                a++;
            }

            // Kreiere die Liste an relevanten Paths die sich im Anschluss ausgegeben werden sollen
            List<int[]> paths = new List<int[]>();
            for (int i = bestWaterFlowPathBlockSkipPositions.Count; --i > -1;)
            {
                paths.Add(waterFlowPathsDict[bestWaterFlowPathBlockSkipPositions[i]]);
            }
            return paths;
        }

        private static void RecursiveWaterflowPathfinder(int plyCount, int startField, int endField, int lastField, int possibleBlockSkipsLeft, ulong pblockedSquares, ulong lineUL, ulong blockSkipPositions, List<int> curFields)
        {
            // Wenn die Suche tiefer ist als der bisher kürzeste gefundene Path lang ist, soll hier die Suche beendet werden
            if (plyCount > waterFlowPathLength + 4) return;

            // Sobald das gewünschte Feld erreicht wurde, wird die Suche ebenfalls beendet
            if (lastField == endField)
            {
                // Mit der Besonderheit, dass der beste Path ersetzt werden muss, im Falle das einer gefunden wurde
                if (waterFlowPathsDict.ContainsKey(blockSkipPositions))
                {
                    if (plyCount <= waterFlowPathLength)
                    {
                        waterFlowPathLength = plyCount;
                        waterFlowPathsDict[blockSkipPositions] = curFields.ToArray();
                    }
                }
                else
                {
                    bestWaterFlowPathBlockSkipPositions.Add(blockSkipPositions);
                    waterFlowPathsDict.Add(blockSkipPositions, curFields.ToArray());
                    if (plyCount < waterFlowPathLength) waterFlowPathLength = plyCount;
                }
                return;
            }

            // Aus Effizienz Gründen diese Pointer Deklarierung
            List<int> tempList = squares[lastField].directNeighbors;

            // Iteration durch jedes benachbartes Feld
            for (int i = tempList.Count; --i > -1;)
            {
                int tempIndex = tempList[i], subtract = 0;

                // Welches nicht bereits durchquert wurde
                if (ULONG_OPERATIONS.IsBitOne(lineUL, tempIndex)) continue;

                if (ULONG_OPERATIONS.IsBitOne(pblockedSquares, tempIndex))
                {
                    if (possibleBlockSkipsLeft == 0) continue;
                    subtract = 1;
                }

                // Tiefenerweiterung der Suche
                curFields.Add(tempIndex);
                RecursiveWaterflowPathfinder(plyCount + 1, startField, endField, tempIndex, possibleBlockSkipsLeft - subtract, pblockedSquares, ULONG_OPERATIONS.SetBitToOne(lineUL, tempIndex), subtract == 0 ? blockSkipPositions : ULONG_OPERATIONS.SetBitToOne(blockSkipPositions, tempIndex), curFields);
                curFields.RemoveAt(curFields.Count - 1);
            }
        }

        private static List<int[]> Dijkstra(int startSquareIndex, int endSquareIndex, ulong pblockedSquares)
        {
            //Stopwatch sw2 = Stopwatch.StartNew();

            List<(int, int, List<int>)> dijkstraArr = new List<(int, int, List<int>)>();
            dijkstraArr.Add((startSquareIndex, 0, new List<int>() { startSquareIndex }));
            List<int[]> res = Dijkstra(dijkstraArr, pblockedSquares, ULONG_OPERATIONS.SetBitToOne(0ul, startSquareIndex), endSquareIndex);
            //sw2.Stop();

            //int original

            //foreach (int[] iarr in res)
            //{
            //    string str = "";
            //    foreach (int i in iarr)
            //        str += i + ", ";
            //    Console.WriteLine(str);
            //}

            //Console.WriteLine(sw2.ElapsedMilliseconds);

            return res;
        }

        private static List<int[]> Dijkstra(List<(int, int, List<int>)> pDists, ulong pBlocked, ulong pVisited, int pEndPos)
        {
            int optionsDist = 100000000;
            List<int[]> options = new List<int[]>();
            for (int j = 0; j < 10_000; j++) // Obscure maximum
            {
                int sq = pDists[0].Item1, dist = pDists[0].Item2;
                List<int> tPath = pDists[0].Item3, tempList = squares[sq].directNeighbors;

                if (dist > optionsDist) break;

                for (int i = tempList.Count; --i > -1;)
                {
                    int tidx = tempList[i];

                    if (tidx != pEndPos && ULONG_OPERATIONS.IsBitOne(pVisited, tidx)) continue;

                    if (ULONG_OPERATIONS.IsBitOne(pBlocked, tidx))
                    {
                        tPath.Add(tidx);
                        DijkstraInsert(ref pDists, (tidx, dist + 10000, new List<int>(tPath)));
                    }

                    else
                    {
                        tPath.Add(tidx);
                        DijkstraInsert(ref pDists, (tidx, dist + 1, new List<int>(tPath)));
                    }

                    if (tidx == pEndPos)
                    {
                        optionsDist = dist;
                        options.Add(tPath.ToArray());
                    }

                    pVisited = ULONG_OPERATIONS.SetBitToOne(pVisited, tidx);
                    tPath.RemoveAt(tPath.Count - 1);
                }

                pDists.RemoveAt(0);
            }
            return options;
        }

        private static void DijkstraInsert(ref List<(int, int, List<int>)> tList, (int, int, List<int>) tEl) // Theoretisch effizienter mit einem Binary Insert, aber sollte so schnell genug sein, da die Listengrößen nicht unfassbar groß werden können
        {
            int tV = tEl.Item2, tL = tList.Count;

            for (int i = 0; i < tList.Count; i++)
            {
                if (tList[i].Item2 > tV)
                {
                    tList.Insert(i, tEl);
                    break;
                }
            }

            tList.Add(tEl);
        }

        #endregion
    }

    public class SquarePoint
    {
        public Vector2 pos;
        public int x, y;
        public int id;
        public List<int> directNeighbors = new List<int>();
    }
}
