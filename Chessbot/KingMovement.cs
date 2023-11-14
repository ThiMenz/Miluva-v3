using System;
using System.Collections.Generic;

namespace ChessBot
{
    public class KingMovement
    {
        private static int KING_PIECE_ID = 6;

        private BoardManager boardManager;
        private Dictionary<ulong, Dictionary<ulong, List<Move>>>[] preCalcMoves = new Dictionary<ulong, Dictionary<ulong, List<Move>>>[64];

        private ulong[] kingMasks = new ulong[64];

        private int[] adjacentSquareCount = new int[64] { 
            3, 5, 5, 5, 5, 5, 5, 3,
            5, 8, 8, 8, 8, 8, 8, 5,
            5, 8, 8, 8, 8, 8, 8, 5,
            5, 8, 8, 8, 8, 8, 8, 5,
            5, 8, 8, 8, 8, 8, 8, 5,
            5, 8, 8, 8, 8, 8, 8, 5,
            5, 8, 8, 8, 8, 8, 8, 5,
            3, 5, 5, 5, 5, 5, 5, 3
        };

        private ushort[] optionCount = new ushort[9] { 0, 0b1, 0b11, 0b111, 0b1111, 0b11111, 0b111111, 0b1111111, 0b11111111 };

        public KingMovement(BoardManager pBoardManager)
        {
            boardManager = pBoardManager;
            for (int i = 0; i < 64; i++) SingleSquarePrecalculations(i);
            boardManager.SetKingMasks(kingMasks);
        }

        public void AddMoveOptionsToMoveList(int pSquare, ulong pAttkBBandOwnColorBB, ulong pOppColorBitboardAndNotAttkBB)
        {
            ulong tu = kingMasks[pSquare];
            boardManager.moveOptionList.AddRange(preCalcMoves[pSquare][pAttkBBandOwnColorBB & tu][pOppColorBitboardAndNotAttkBB & tu]);
        }

        private void SingleSquarePrecalculations(int pSquare)
        {
            preCalcMoves[pSquare] = new Dictionary<ulong, Dictionary<ulong, List<Move>>>();
            int tC;
            ushort tO = optionCount[tC = adjacentSquareCount[pSquare]], tSaveO = tO;
            ulong tu = 0ul;
            List<int> tAdjacentSquares = new List<int>() { pSquare + 1, pSquare - 1, pSquare + 7, pSquare + 8, pSquare + 9, pSquare - 7, pSquare - 8, pSquare - 9 };
            if (tC == 8) tu = ULONG_OPERATIONS.SetBitsToOne(tu, pSquare + 1, pSquare - 1, pSquare + 7, pSquare + 8, pSquare + 9, pSquare - 7, pSquare - 8, pSquare - 9);
            else
            {
                tAdjacentSquares.Clear();
                int sqMod8 = pSquare % 8;
                bool b1 = sqMod8 != 7, b2 = sqMod8 != 0, b3 = pSquare < 56, b4 = pSquare > 7;
                if (b1) { tu = ULONG_OPERATIONS.SetBitToOne(tu, pSquare + 1); tAdjacentSquares.Add(pSquare + 1); }
                if (b2) { tu = ULONG_OPERATIONS.SetBitToOne(tu, pSquare - 1); tAdjacentSquares.Add(pSquare - 1); }
                if (b2 && b3) { tu = ULONG_OPERATIONS.SetBitToOne(tu, pSquare + 7); tAdjacentSquares.Add(pSquare + 7); }
                if (b1 && b3) { tu = ULONG_OPERATIONS.SetBitToOne(tu, pSquare + 9); tAdjacentSquares.Add(pSquare + 9); }
                if (b3) { tu = ULONG_OPERATIONS.SetBitToOne(tu, pSquare + 8); tAdjacentSquares.Add(pSquare + 8); }
                if (b2 && b4) { tu = ULONG_OPERATIONS.SetBitToOne(tu, pSquare - 7); tAdjacentSquares.Add(pSquare - 7); }
                if (b1 && b4) { tu = ULONG_OPERATIONS.SetBitToOne(tu, pSquare - 9); tAdjacentSquares.Add(pSquare - 9); }
                if (b4) { tu = ULONG_OPERATIONS.SetBitToOne(tu, pSquare - 8); tAdjacentSquares.Add(pSquare - 8); }
            }
            kingMasks[pSquare] = tu;
            do {
                List<Move> tMoves = new List<Move>();
                ulong tBlockedSquaresBitboard = GetUlongFromSquareListAndOptionUshort(tAdjacentSquares, tO, false, pSquare, ref tMoves);
                ushort t_ti_O = tSaveO;
                Dictionary<ulong, List<Move>> tDict = new Dictionary<ulong, List<Move>>();
                do
                {
                    for (int i = 0; i < tC; i++) if (ULONG_OPERATIONS.IsBitOne(tO, i) && ULONG_OPERATIONS.IsBitOne(t_ti_O, i)) goto NextIter;
                    List<Move> t_ti_Moves = new List<Move>(tMoves);
                    ulong tOppSquaresBitboard = GetUlongFromSquareListAndOptionUshort(tAdjacentSquares, t_ti_O, true, pSquare, ref t_ti_Moves);
                    tDict.Add(tOppSquaresBitboard, t_ti_Moves);
                    NextIter:;
                } while (t_ti_O-- > 0);
                preCalcMoves[pSquare].Add(tBlockedSquaresBitboard, tDict);
            } while (tO-- > 0);
        }

        private ulong GetUlongFromSquareListAndOptionUshort(List<int> pList, ushort pUS, bool pBNA, int pSq, ref List<Move> pMoves)
        {
            ulong outp = 0ul;
            int tL = pList.Count;
            for (int i = 0; i < tL; i++)
            {
                int pI = pList[i];
                if (ULONG_OPERATIONS.IsBitZero(pUS, i))
                {
                    if (!pBNA) pMoves.Add(new Move(pSq, pI, KING_PIECE_ID, false));
                    continue;
                }
                if (pBNA) {
                    pMoves.Add(new Move(pSq, pI, KING_PIECE_ID, true));
                    int t_ti_L = pMoves.Count;
                    for (int j = 0; j < t_ti_L; j++)
                    {
                        if (pMoves[j].endPos == pI)
                        {
                            pMoves.RemoveAt(j);
                            break;
                        }
                    }
                    //if (pMoves.Contains(pSq)) (new Move());
                }
                outp = ULONG_OPERATIONS.SetBitToOne(outp, pI);
            }
            return outp;
        }
    }
}
