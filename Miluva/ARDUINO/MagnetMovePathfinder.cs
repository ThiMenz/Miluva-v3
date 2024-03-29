using System.Diagnostics;
using System.Drawing;
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
            public enum Direction { Up, Down, Left, Right };

            public override string ToString()
            {
                string c = "O";
                if (dir == Direction.Left) c = "L";
                else if (dir == Direction.Right) c = "R";
                else if (dir == Direction.Down) c = "U";
                if (magnet) MagnetMovePathfinder.FINAL_ACTIONS.Add(4);
                MagnetMovePathfinder.FINAL_ACTIONS.Add((int)dir);
                return (magnet ? " M " : " ") + c;
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

        public string GetDirectionMoveString()
        {
            string s = "";
            int f = 0;

            // Konvertieren der rohen Felder in Bewegungsrichtungen
            for (int i = 0; i < individualFields.Count; i++)
            {
                int dif = individualFields[i].pos - f;
                DMove.Direction tdir = DMove.Direction.Left;
                if (dif == 1) tdir = DMove.Direction.Up; //Up
                else if (dif == -1) tdir = DMove.Direction.Down; //Down
                else if (dif > 1) tdir = DMove.Direction.Right; //Right
                dirMoves.Add(new DMove() { dir = tdir, magnet = ((i == 0 ? individualFields[i].magnet : (individualFields[i].magnet && !individualFields[i - 1].magnet
                    
                    || (!individualFields[i].magnet && individualFields[i - 1].magnet)

                    ))) });
                f = individualFields[i].pos;
            }

            // Löschen jeglicher Vor und Zurück Bewegungen -> Schlussendlich finale Ausgabe
            CleanUpDirectionMoves();

            int indivFC = individualFields.Count;
            GetToFieldWithoutMagnet(0);

            for (int i = indivFC; i < individualFields.Count; i++)
            {
                int dif = individualFields[i].pos - f;
                DMove.Direction tdir = DMove.Direction.Left;
                if (dif == 1) tdir = DMove.Direction.Up; //Up
                else if (dif == -1) tdir = DMove.Direction.Down; //Down
                else if (dif > 1) tdir = DMove.Direction.Right; //Right
                dirMoves.Add(new DMove()
                {
                    dir = tdir,
                    magnet = indivFC == i
                });
                f = individualFields[i].pos;
            }

            for (int i = 0; i < dirMoves.Count; i++) s += dirMoves[i];
            return s;
        }

        private void CleanUpDirectionMoves()
        {
            for (int i = 1; i < dirMoves.Count; i++)
            {
                // Sobald ein Magnet sich hin & her mit dem gleichen Magnet-Status bewegt, wird diese Sequenz als unnötig angesehen und gelöscht
                if (IsContradictingDirection(dirMoves[i].dir, dirMoves[i - 1].dir) && dirMoves[i].magnet == dirMoves[i - 1].magnet)
                {
                    dirMoves.RemoveAt(i - 1);
                    dirMoves.RemoveAt(i - 1);
                }
            }
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
        public void FinishMoveSequence(int nonReversedAmount)
        {
            for (int i = compressedReversedMoves.Count - nonReversedAmount; i-- > 0;)
            {
                AddIndividualMovesFromPath(compressedReversedMoves[i].directpath, compressedReversedMoves[i].ulBoard, 0ul, false);
            }
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
                individualFields.Add(new RMove() { pos = path[a], magnet = true });
                a++;
            }

            // Bewegung des Magnets zum Ende des Paths
            List<int> tempPath = new List<int>();
            for (int i = a + 1; i < path.Length; i++) tempPath.Add(path[i]);
            AddIndividualMoves(tempPath.ToArray());

            // Ziehen aller Figuren die auf dem Path der zu ziehenden Figur stehen (bis hinzu der tatsächlichen Figur am Ende)
            for (int i = path.Length - 1; i >= a; i--)
            {
                individualFields.Add(new RMove() { pos = path[i - 1], magnet = false });
                individualFields.Add(new RMove() { pos = path[i], magnet = true });
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

        #endregion
    }

    public static class MagnetMovePathfinder
    {
        public static List<int> FINAL_ACTIONS = new List<int>(); 

        private static Vector2 bottomDownVec;
        private static float squareSize, maxDistance;
        private static Color[] squareColors;

        private static int pathFinderPoints = 0;
        private static ulong blockedSquares = 0ul;
        private static int[] pathFinderPointArr = new int[2] { -1, -1 };
        public static SquarePoint[] squares { get; private set; } = new SquarePoint[64];

        private static bool setupped = false;

        #region | PATHFINDER |

        public static MagnetMoveSequence CalculatePath(ulong pblockedSquares, int startSquareIndex, int endSquareIndex)
        {
            Console.WriteLine(startSquareIndex + " | " + endSquareIndex);

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

            Stopwatch sw = Stopwatch.StartNew();
            List<int[]> paths = WaterflowPathfinder(pblockedSquares, startSquareIndex, endSquareIndex);
            float pathCount = (float)paths.Count;
            MagnetMoveSequence shortestMoveSeq = null;
            int shortestMoveSeqLength = int.MaxValue;
            foreach (int[] path in paths)
            {
                MagnetMoveSequence mms = BlockerSubPathFinder(path, pblockedSquares);
                int tempField = mms.curField;
                mms.AddIndividualMovesFromPath(path, 0ul, 0ul, false);
                mms.FinishMoveSequence(0);
                if (mms.individualFields.Count < shortestMoveSeqLength)
                {
                    shortestMoveSeq = mms;
                    shortestMoveSeqLength = mms.individualFields.Count;
                }
            }
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);

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
                if (ULONG_OPERATIONS.IsBitOne(blockerUL, path[i])) blockers.Add(path[i]);
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

        private static int blockedSubPathRecordLength;
        private static int[] blockedSubPathRecord;
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
                if (ULONG_OPERATIONS.IsBitOne(completelyExcludedFields, tempIndex)) continue;

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
                else
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

        private static int waterFlowPathLength;
        private static List<ulong> bestWaterFlowPathBlockSkipPositions = new List<ulong>();
        private static Dictionary<ulong, int[]> waterFlowPathsDict = new Dictionary<ulong, int[]>();

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
