using System;
using System.Collections.Generic;

namespace ChessBot
{
    public class RookMovement
    {
        private const int ROOK_TILE_ID = 4, QUEEN_TILE_ID = 5;

        private BoardManager boardManager;
        private QueenMovement queenMovement;

        private List<Dictionary<ulong, RookPreCalcs>> precalculatedMoves = new List<Dictionary<ulong, RookPreCalcs>>();
        private ulong[] rookMasks = new ulong[64];

        public RookMovement(BoardManager pBM, QueenMovement pQM)
        {
            boardManager = pBM;
            queenMovement = pQM;
            PreCalculateMoves();
        }

        public void AddMoveOptionsToMoveList(int startSquare, ulong opposingSideBitboard, ulong allPieceBitboard)
        {
            RookPreCalcs trpc = precalculatedMoves[startSquare][allPieceBitboard & rookMasks[startSquare]];
            boardManager.moveOptionList.AddRange(trpc.classicMoves);

            // Auch wenn etwas unschön, diese repetitive Code Struktur macht die Methode merkbar schneller
            if (((opposingSideBitboard >> trpc.possibleCapture1) & 1ul) == 1ul) boardManager.moveOptionList.Add(trpc.captureMove1);
            if (((opposingSideBitboard >> trpc.possibleCapture2) & 1ul) == 1ul) boardManager.moveOptionList.Add(trpc.captureMove2);
            if (((opposingSideBitboard >> trpc.possibleCapture3) & 1ul) == 1ul) boardManager.moveOptionList.Add(trpc.captureMove3);
            if (((opposingSideBitboard >> trpc.possibleCapture4) & 1ul) == 1ul) boardManager.moveOptionList.Add(trpc.captureMove4);
        }

        public void AddMoveOptionsToMoveList(int startSquare, int ownKingPosFilter, ulong opposingSideBitboard, ulong allPieceBitboard)
        {
            RookPreCalcsKingPin trpc = precalculatedMoves[startSquare][allPieceBitboard & rookMasks[startSquare]].classicMovesOnKingPin[ownKingPosFilter];
            boardManager.moveOptionList.AddRange(trpc.moves);

            if (((opposingSideBitboard >> trpc.possibleCapture1) & 1ul) == 1ul) boardManager.moveOptionList.Add(trpc.captureMove1);
            if (((opposingSideBitboard >> trpc.possibleCapture2) & 1ul) == 1ul) boardManager.moveOptionList.Add(trpc.captureMove2);
        }

        public ulong GetVisionMask(int square, ulong allPieceBitboard)
        {
            return precalculatedMoves[square][allPieceBitboard & rookMasks[square]].visionBitboard;
        }

        public ulong GetVisionConnectivesMask(int square, ulong allPieceBitboard)
        {
            return precalculatedMoves[square][allPieceBitboard & rookMasks[square]].captureBitbaord;
        }

        #region | PRECALCULATIONS |

        private Dictionary<int, RookPreCalcs> tempPreCalcDict = new Dictionary<int, RookPreCalcs>();

        private void PreCalculateMoves()
        {
            for (int i = 0; i < 64; i++)
            {
                precalculatedMoves.Add(new Dictionary<ulong, RookPreCalcs>());
                int[] necessarySquares = new int[14];
                int tCount = 0;
                for (int j = i % 8; j < 64; j += 8)
                    if (j != i) necessarySquares[tCount++] = j;

                int tRowMax = i - (i % 8) + 8;
                for (int j = i - (i % 8); j < tRowMax; j++)
                    if (j != i) necessarySquares[tCount++] = j;

                ushort availableSquares = 0b11111111111111;
                do {
                    ulong curAllPieceBitboard = 0ul;
                    for (int j = 0; j < 14; j++)
                        if (ULONG_OPERATIONS.IsBitOne(availableSquares, j))
                            curAllPieceBitboard = ULONG_OPERATIONS.SetBitToOne(curAllPieceBitboard, necessarySquares[j]);
                    CalculateAndAddMovesFromSquare(i, curAllPieceBitboard);
                } while (availableSquares-- != 0);

                tempPreCalcDict.Clear();
                queenMovement.ClearTempDictStraight();
                ulong curMask = 0ul;
                for (int j = 0; j < 14; j++) curMask = ULONG_OPERATIONS.SetBitToOne(curMask, necessarySquares[j]);
                rookMasks[i] = curMask;
            }
            queenMovement.SetStraightMasks(rookMasks);
        }

        private void CalculateAndAddMovesFromSquare(int square, ulong allPieceBitboard)
        {
            List<Move> moves = new List<Move>(), movesDir1 = new List<Move>(), movesDir2 = new List<Move>();
            int[] caps = new int[4] { square, square, square, square };
            ulong visionMask = 0ul;
            Move[] capMoves = new Move[4], capMovesQueen = new Move[4];
            for (int i = square + 8; i < 64; i += 8) 
            {
                visionMask = ULONG_OPERATIONS.SetBitToOne(visionMask, i);
                if (ULONG_OPERATIONS.IsBitOne(allPieceBitboard, i)) 
                {
                    caps[0] = i;
                    capMoves[0] = new Move(square, i, ROOK_TILE_ID, true);
                    capMovesQueen[0] = new Move(square, i, QUEEN_TILE_ID, true);
                    break;
                }
                Move tM = new Move(square, i, ROOK_TILE_ID);
                movesDir1.Add(tM);
                moves.Add(tM);
            }
            for (int i = square - 8; i > -1; i -= 8) 
            {
                visionMask = ULONG_OPERATIONS.SetBitToOne(visionMask, i);
                if (ULONG_OPERATIONS.IsBitOne(allPieceBitboard, i))
                {
                    caps[1] = i;
                    capMoves[1] = new Move(square, i, ROOK_TILE_ID, true);
                    capMovesQueen[1] = new Move(square, i, QUEEN_TILE_ID, true);
                    break;
                }
                Move tM = new Move(square, i, ROOK_TILE_ID);
                movesDir1.Add(tM);
                moves.Add(tM);
            }
            int tempSq = square - (square % 8) - 1, tempSq2 = tempSq + 9;
            for (int i = square + 1; i < tempSq2; i++)
            {
                visionMask = ULONG_OPERATIONS.SetBitToOne(visionMask, i);
                if (ULONG_OPERATIONS.IsBitOne(allPieceBitboard, i)) 
                {
                    caps[2] = i;
                    capMoves[2] = new Move(square, i, ROOK_TILE_ID, true);
                    capMovesQueen[2] = new Move(square, i, QUEEN_TILE_ID, true);
                    break;
                }
                Move tM = new Move(square, i, ROOK_TILE_ID);
                movesDir2.Add(tM);
                moves.Add(tM);
            }
            for (int i = square - 1; i > tempSq; i--)
            {
                visionMask = ULONG_OPERATIONS.SetBitToOne(visionMask, i);
                if (ULONG_OPERATIONS.IsBitOne(allPieceBitboard, i))
                {
                    caps[3] = i;
                    capMoves[3] = new Move(square, i, ROOK_TILE_ID, true);
                    capMovesQueen[3] = new Move(square, i, QUEEN_TILE_ID, true);
                    break;
                }
                Move tM = new Move(square, i, ROOK_TILE_ID);
                movesDir2.Add(tM);
                moves.Add(tM);
            }

            int t = caps[3] << 24 | caps[2] << 16 | caps[1] << 8 | caps[0]; 

            if (tempPreCalcDict.ContainsKey(t))
            {
                precalculatedMoves[square].Add(allPieceBitboard, tempPreCalcDict[t]);
                queenMovement.AddPrecalculationFromStrDict(square, allPieceBitboard, t);
                return;
            }

            RookPreCalcsKingPin[] pinMoves = new RookPreCalcsKingPin[64];
            int tsv = square << 6;

            for (int i = 0; i < 64; i++)
            {
                int tCon = boardManager.squareConnectivesCrossDirsPrecalculationArray[tsv | i];
                if (tCon == 1) pinMoves[i] = new RookPreCalcsKingPin(movesDir1, caps[0], caps[1], capMoves[0], capMoves[1]);
                else if (tCon == -1) pinMoves[i] = new RookPreCalcsKingPin(movesDir2, caps[2], caps[3], capMoves[2], capMoves[3]);
                else pinMoves[i] = new RookPreCalcsKingPin(new List<Move>(), i, i, capMoves[0], capMoves[0]);
            }

            RookPreCalcs rpc = new RookPreCalcs(moves, caps, capMoves, square, visionMask, pinMoves, capMovesQueen);

            tempPreCalcDict.Add(t, rpc);
            precalculatedMoves[square].Add(allPieceBitboard, rpc);
            queenMovement.AddPrecalculation(square, allPieceBitboard, rpc, t);
        }

        #endregion
    }

    #region | PRECALC DATA CONTENT CLASSES |

    public class RookPreCalcsKingPin
    {
        public List<Move> moves = new List<Move>();
        public int possibleCapture1, possibleCapture2;
        public Move captureMove1, captureMove2;

        public RookPreCalcsKingPin(List<Move> ms, int pc1, int pc2, Move cm1, Move cm2)
        {
            moves = ms;
            possibleCapture1 = pc1;
            possibleCapture2 = pc2;
            captureMove1 = cm1;
            captureMove2 = cm2;
        }
    }

    public class RookPreCalcs
    {
        public List<Move> classicMoves { get; private set; }
        public RookPreCalcsKingPin[] classicMovesOnKingPin { get; private set; }
        public int[] possibleCaptures { get; private set; }
        public Move[] captureMoves { get; private set; }
        public Move[] captureMovesQueen { get; private set; }

        public ulong captureBitbaord { get; private set; }
        public ulong visionBitboard { get; private set; }

        public int possibleCapture1, possibleCapture2, possibleCapture3, possibleCapture4;
        public Move captureMove1, captureMove2, captureMove3, captureMove4;

        public RookPreCalcs(List<Move> pMoves, int[] pCapt, Move[] pCapMoves, int pSq, ulong pVisMask, RookPreCalcsKingPin[] pMovesPin, Move[] pqCapMoves)
        {
            classicMoves = pMoves;
            classicMovesOnKingPin = pMovesPin;

            possibleCaptures = pCapt;
            captureMoves = pCapMoves;
            captureMovesQueen = pqCapMoves;

            captureMove1 = pCapMoves[0];
            captureMove2 = pCapMoves[1];
            captureMove3 = pCapMoves[2];
            captureMove4 = pCapMoves[3];
            possibleCapture1 = pCapt[0];
            possibleCapture2 = pCapt[1];
            possibleCapture3 = pCapt[2];
            possibleCapture4 = pCapt[3];

            visionBitboard = pVisMask;

            captureBitbaord = ULONG_OPERATIONS.SetBitToZero(ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToOne(
                ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToOne(0ul, possibleCapture1), possibleCapture2), possibleCapture3), possibleCapture4), pSq);
        }
    }

    #endregion
}
