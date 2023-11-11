using System;
using System.Collections.Generic;

namespace ChessBot
{
    public class BishopMovement
    {
        private const int BISHOP_TILE_ID = 3, QUEEN_TILE_ID = 5;

        private BoardManager boardManager;
        private QueenMovement queenMovement;

        private List<Dictionary<ulong, BishopPreCalcs>> precalculatedMoves = new List<Dictionary<ulong, BishopPreCalcs>>();
        private ulong[] bishopMasks = new ulong[64];

        public BishopMovement(BoardManager pBM, QueenMovement pQM)
        {
            boardManager = pBM;
            queenMovement = pQM;
            PreCalculateMoves();
        }

        public void AddMoveOptionsToMoveList(int startSquare, ulong opposingSideBitboard, ulong allPieceBitboard)
        {
            BishopPreCalcs trpc = precalculatedMoves[startSquare][allPieceBitboard & bishopMasks[startSquare]];
            boardManager.moveOptionList.AddRange(trpc.classicMoves);

            // Auch wenn etwas unschön, diese repetitive Code Struktur macht die Methode merkbar schneller
            if (((opposingSideBitboard >> trpc.possibleCapture1) & 1ul) == 1ul) boardManager.moveOptionList.Add(trpc.captureMove1);
            if (((opposingSideBitboard >> trpc.possibleCapture2) & 1ul) == 1ul) boardManager.moveOptionList.Add(trpc.captureMove2);
            if (((opposingSideBitboard >> trpc.possibleCapture3) & 1ul) == 1ul) boardManager.moveOptionList.Add(trpc.captureMove3);
            if (((opposingSideBitboard >> trpc.possibleCapture4) & 1ul) == 1ul) boardManager.moveOptionList.Add(trpc.captureMove4);
        }

        public void AddMoveOptionsToMoveList(int startSquare, int ownKingPosFilter, ulong opposingSideBitboard, ulong allPieceBitboard)
        {
            BishopPreCalcsKingPin trpc = precalculatedMoves[startSquare][allPieceBitboard & bishopMasks[startSquare]].classicMovesOnKingPin[ownKingPosFilter];
            boardManager.moveOptionList.AddRange(trpc.moves);

            if (((opposingSideBitboard >> trpc.possibleCapture1) & 1ul) == 1ul) boardManager.moveOptionList.Add(trpc.captureMove1);
            if (((opposingSideBitboard >> trpc.possibleCapture2) & 1ul) == 1ul) boardManager.moveOptionList.Add(trpc.captureMove2);
        }

        public ulong GetVisionMask(int square, ulong allPieceBitboard)
        {
            return precalculatedMoves[square][allPieceBitboard & bishopMasks[square]].visionBitboard;
        }

        public ulong GetVisionConnectivesMask(int square, ulong allPieceBitboard)
        {
            return precalculatedMoves[square][allPieceBitboard & bishopMasks[square]].captureBitbaord;
        }

        #region | PRECALCULATIONS |

        private Dictionary<int, BishopPreCalcs> tempPreCalcDict = new Dictionary<int, BishopPreCalcs>();

        private ushort[] squareDiagonalFieldOptions = new ushort[64] { 
            0b1111111, 0b1111111, 0b1111111, 0b1111111, 0b1111111, 0b1111111, 0b1111111, 0b1111111,
            0b1111111, 0b111111111, 0b111111111, 0b111111111, 0b111111111, 0b111111111, 0b111111111, 0b1111111,
            0b1111111, 0b111111111, 0b11111111111, 0b11111111111, 0b11111111111, 0b11111111111, 0b111111111, 0b1111111,
            0b1111111, 0b111111111, 0b11111111111, 0b1111111111111, 0b1111111111111, 0b11111111111, 0b111111111, 0b1111111,
            0b1111111, 0b111111111, 0b11111111111, 0b1111111111111, 0b1111111111111, 0b11111111111, 0b111111111, 0b1111111,
            0b1111111, 0b111111111, 0b11111111111, 0b11111111111, 0b11111111111, 0b11111111111, 0b111111111, 0b1111111,
            0b1111111, 0b111111111, 0b111111111, 0b111111111, 0b111111111, 0b111111111, 0b111111111, 0b1111111,
            0b1111111, 0b1111111, 0b1111111, 0b1111111, 0b1111111, 0b1111111, 0b1111111, 0b1111111
        };
        private int[] squareDiagonalFieldCount = new int[64] {
            7, 7, 7, 7, 7, 7, 7, 7,
            7, 9, 9, 9, 9, 9, 9, 7,
            7, 9, 11, 11, 11, 11, 9, 7,
            7, 9, 11, 13, 13, 11, 9, 7,
            7, 9, 11, 13, 13, 11, 9, 7,
            7, 9, 11, 11, 11, 11, 9, 7,
            7, 9, 9, 9, 9, 9, 9, 7,
            7, 7, 7, 7, 7, 7, 7, 7
        };

        private void PreCalculateMoves()
        {
            for (int i = 0; i < 64; i++)
            {
                precalculatedMoves.Add(new Dictionary<ulong, BishopPreCalcs>());
                int squareCount = squareDiagonalFieldCount[i];
                int[] necessarySquares = new int[squareCount];
                int tCount = 0;
                for (int j = i + 9; j < 64 && j % 8 != 0; j += 9) necessarySquares[tCount++] = j;
                for (int j = i - 7; j > -1 && j % 8 != 0; j -= 7) necessarySquares[tCount++] = j;
                for (int j = i + 7; j < 64 && j % 8 != 7; j += 7) necessarySquares[tCount++] = j;
                for (int j = i - 9; j > -1 && j % 8 != 7; j -= 9) necessarySquares[tCount++] = j;
                ushort availableSquares = squareDiagonalFieldOptions[i];
                do
                {
                    ulong curAllPieceBitboard = 0ul;
                    for (int j = 0; j < squareCount; j++)
                        if (ULONG_OPERATIONS.IsBitOne(availableSquares, j))
                            curAllPieceBitboard = ULONG_OPERATIONS.SetBitToOne(curAllPieceBitboard, necessarySquares[j]);
                    CalculateAndAddMovesFromSquare(i, curAllPieceBitboard);
                } while (availableSquares-- != 0);

                tempPreCalcDict.Clear();
                queenMovement.ClearTempDictDiagonal();
                ulong curMask = 0ul;
                for (int j = 0; j < squareCount; j++) curMask = ULONG_OPERATIONS.SetBitToOne(curMask, necessarySquares[j]);
                bishopMasks[i] = curMask;
            }
            queenMovement.SetDiagonalMasks(bishopMasks);
        }

        private void CalculateAndAddMovesFromSquare(int square, ulong allPieceBitboard)
        {
            List<Move> moves = new List<Move>(), movesDir1 = new List<Move>(), movesDir2 = new List<Move>();
            int[] caps = new int[4] { square, square, square, square };
            ulong visionMask = 0ul;
            Move[] capMoves = new Move[4], capQueenMoves = new Move[4];
            for (int i = square + 9; i < 64 && i % 8 != 0; i += 9)
            {
                visionMask = ULONG_OPERATIONS.SetBitToOne(visionMask, i);
                if (ULONG_OPERATIONS.IsBitOne(allPieceBitboard, i))
                {
                    caps[0] = i;
                    capMoves[0] = new Move(square, i, BISHOP_TILE_ID, true);
                    capQueenMoves[0] = new Move(square, i, QUEEN_TILE_ID, true);
                    break;
                }
                Move tM = new Move(square, i, BISHOP_TILE_ID);
                movesDir1.Add(tM);
                moves.Add(tM);
            }
            for (int i = square - 9; i > -1 && i % 8 != 7; i -= 9)
            {
                visionMask = ULONG_OPERATIONS.SetBitToOne(visionMask, i);
                if (ULONG_OPERATIONS.IsBitOne(allPieceBitboard, i))
                {
                    caps[1] = i;
                    capMoves[1] = new Move(square, i, BISHOP_TILE_ID, true);
                    capQueenMoves[1] = new Move(square, i, QUEEN_TILE_ID, true);
                    break;
                }
                Move tM = new Move(square, i, BISHOP_TILE_ID);
                movesDir1.Add(tM);
                moves.Add(tM);
            }
            for (int i = square - 7; i > -1 && i % 8 != 0; i -= 7)
            {
                visionMask = ULONG_OPERATIONS.SetBitToOne(visionMask, i);
                if (ULONG_OPERATIONS.IsBitOne(allPieceBitboard, i))
                {
                    caps[2] = i;
                    capMoves[2] = new Move(square, i, BISHOP_TILE_ID, true);
                    capQueenMoves[2] = new Move(square, i, QUEEN_TILE_ID, true);
                    break;
                }
                Move tM = new Move(square, i, BISHOP_TILE_ID);
                movesDir2.Add(tM);
                moves.Add(tM);
            }
            for (int i = square + 7; i < 64 && i % 8 != 7; i += 7)
            {
                visionMask = ULONG_OPERATIONS.SetBitToOne(visionMask, i);
                if (ULONG_OPERATIONS.IsBitOne(allPieceBitboard, i))
                {
                    caps[3] = i;
                    capMoves[3] = new Move(square, i, BISHOP_TILE_ID, true);
                    capQueenMoves[3] = new Move(square, i, QUEEN_TILE_ID, true);
                    break;
                }
                Move tM = new Move(square, i, BISHOP_TILE_ID);
                movesDir2.Add(tM);
                moves.Add(tM);
            }

            int t = caps[3] << 24 | caps[2] << 16 | caps[1] << 8 | caps[0];

            if (tempPreCalcDict.ContainsKey(t))
            {
                precalculatedMoves[square].Add(allPieceBitboard, tempPreCalcDict[t]);
                queenMovement.AddPrecalculationFromDiaDict(square, allPieceBitboard, t);
                return;
            }

            BishopPreCalcsKingPin[] pinMoves = new BishopPreCalcsKingPin[64];
            int tsv = square << 6;

            for (int i = 0; i < 64; i++)
            {
                int tCon = boardManager.squareConnectivesCrossDirsPrecalculationArray[tsv | i];
                if (tCon == -2) pinMoves[i] = new BishopPreCalcsKingPin(movesDir1, caps[0], caps[1], capMoves[0], capMoves[1]);
                else if (tCon == 2) pinMoves[i] = new BishopPreCalcsKingPin(movesDir2, caps[2], caps[3], capMoves[2], capMoves[3]);
                else pinMoves[i] = new BishopPreCalcsKingPin(new List<Move>(), i, i, capMoves[0], capMoves[0]);
            }

            BishopPreCalcs rpc = new BishopPreCalcs(moves, caps, capMoves, square, visionMask, pinMoves, capQueenMoves);

            tempPreCalcDict.Add(t, rpc);
            precalculatedMoves[square].Add(allPieceBitboard, rpc);
            queenMovement.AddPrecalculation(square, allPieceBitboard, rpc, t);
        }

        #endregion
    }

    #region | PRECALC DATA CONTENT CLASSES |

    public class BishopPreCalcsKingPin
    {
        public List<Move> moves = new List<Move>();
        public int possibleCapture1, possibleCapture2;
        public Move captureMove1, captureMove2;

        public BishopPreCalcsKingPin(List<Move> ms, int pc1, int pc2, Move cm1, Move cm2)
        {
            moves = ms;
            possibleCapture1 = pc1;
            possibleCapture2 = pc2;
            captureMove1 = cm1;
            captureMove2 = cm2;
        }
    }

    public class BishopPreCalcs
    {
        public List<Move> classicMoves { get; private set; }
        public BishopPreCalcsKingPin[] classicMovesOnKingPin { get; private set; }
        public int[] possibleCaptures { get; private set; }
        public Move[] captureMoves { get; private set; }
        public Move[] captureMovesQueen { get; private set; }

        public ulong captureBitbaord { get; private set; }
        public ulong visionBitboard { get; private set; }

        public int possibleCapture1, possibleCapture2, possibleCapture3, possibleCapture4;
        public Move captureMove1, captureMove2, captureMove3, captureMove4;

        public BishopPreCalcs(List<Move> pMoves, int[] pCapt, Move[] pCapMoves, int pSq, ulong pVisMask, BishopPreCalcsKingPin[] pMovesPin, Move[] pqCapMoves)
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
