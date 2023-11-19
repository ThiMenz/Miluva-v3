using System;
using System.Collections.Generic;

namespace ChessBot
{
    public class BlackPawnMovement
    {
        private const int PAWN_PIECE_ID = 1;

        private ulong[] squareBitboards = new ulong[64], oppSquareBitboards = new ulong[64];
        private List<Dictionary<ulong, List<Move>>> movePrecalcs = new List<Dictionary<ulong, List<Move>>>();
        private List<List<Dictionary<ulong, List<Move>>>> pinMovePrecalcs = new List<List<Dictionary<ulong, List<Move>>>>();

        private BoardManager boardManager;

        public BlackPawnMovement(BoardManager bM)
        {
            boardManager = bM;

            Precalculate();
        }

        public void AddMoveOptionsToMoveList(int square, ulong whitePieceBitboard, ulong blackPieceBitboard)
        {
            boardManager.moveOptionList.AddRange(movePrecalcs[square][whitePieceBitboard & squareBitboards[square] | blackPieceBitboard & oppSquareBitboards[square]]);
        }
        public void AddMoveOptionsToMoveList(int square, int pinKingSquare, ulong whitePieceBitboard, ulong blackPieceBitboard)
        {
            boardManager.moveOptionList.AddRange(pinMovePrecalcs[square][pinKingSquare][whitePieceBitboard & squareBitboards[square] | blackPieceBitboard & oppSquareBitboards[square]]);
        }

        private void Precalculate()
        {
            for (int sq = 0; sq < 64; sq++)
            {
                pinMovePrecalcs.Add(new List<Dictionary<ulong, List<Move>>>());
                movePrecalcs.Add(new Dictionary<ulong, List<Move>>());

                for (int k = 0; k < 64; k++) pinMovePrecalcs[sq].Add(new Dictionary<ulong, List<Move>>());

                ulong u = 0ul, u2 = 0ul;
                if (sq - 16 > -1) u = ULONG_OPERATIONS.SetBitToOne(u, sq - 16);
                if (sq - 8 > -1) u = ULONG_OPERATIONS.SetBitToOne(u, sq - 8);
                if (sq - 7 > -1 && sq % 8 != 7) u2 = ULONG_OPERATIONS.SetBitToOne(u2, sq - 7);
                if (sq - 9 > -1 && sq % 8 != 0) u2 = ULONG_OPERATIONS.SetBitToOne(u2, sq - 9);

                squareBitboards[sq] = u;
                oppSquareBitboards[sq] = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToOne(u2, sq - 16), sq - 8);
            }

            for (int k = 0; k < 64; k++)
            {
                for (int sq = 8; sq < 56; sq++)
                {
                    int sqMod8 = sq % 8, kShift = k << 6, tCon = boardManager.squareConnectivesCrossDirsPrecalculationArray[kShift | sq];
                    List<int> oList = new List<int>() { sq - 8, sq - 7, sq - 9, sq - 16 }, oiList = new List<int>() { 0 };
                    List<bool> obList = new List<bool>() { false, true, true, false };
                    if (sqMod8 != 7) { oiList.Add(1); }
                    if (sqMod8 != 0) { oiList.Add(2); }
                    if (sq - sqMod8 == 48) { oiList.Add(3); }

                    ushort mOptions = 0b1111; //pawnMaxOptions[o];
                    do
                    {
                        ulong u = 0ul;
                        List<Move> tmoves = new List<Move>(), tmoves2 = new List<Move>();
                        for (int i = 0; i < 4; i++)
                        {
                            if (!oiList.Contains(i))
                            {
                                if (ULONG_OPERATIONS.IsBitOne(mOptions, i)) u = ULONG_OPERATIONS.SetBitToOne(u, oList[i]);
                                continue;
                            }
                            int oli = oList[i], t = boardManager.squareConnectivesCrossDirsPrecalculationArray[kShift | oli];
                            bool b = tCon == t || tCon == 0, b2 = i == 3, b3 = oli - oli % 8 == 0;
                            if (ULONG_OPERATIONS.IsBitOne(mOptions, i))
                            {
                                u = ULONG_OPERATIONS.SetBitToOne(u, oli);
                                if (k == 0 && obList[i])
                                {
                                    if (b3)
                                    {
                                        tmoves2.Add(new Move(sq, oli, PAWN_PIECE_ID, 2, true));
                                        tmoves2.Add(new Move(sq, oli, PAWN_PIECE_ID, 3, true));
                                        tmoves2.Add(new Move(sq, oli, PAWN_PIECE_ID, 4, true));
                                        tmoves2.Add(new Move(sq, oli, PAWN_PIECE_ID, 5, true));
                                    }
                                    else tmoves2.Add(new Move(sq, oli, PAWN_PIECE_ID, true));
                                }
                                if (obList[i] && b)
                                {
                                    if (b3)
                                    {
                                        tmoves.Add(new Move(sq, oli, PAWN_PIECE_ID, 2, true));
                                        tmoves.Add(new Move(sq, oli, PAWN_PIECE_ID, 3, true));
                                        tmoves.Add(new Move(sq, oli, PAWN_PIECE_ID, 4, true));
                                        tmoves.Add(new Move(sq, oli, PAWN_PIECE_ID, 5, true));
                                        continue;
                                    }
                                    tmoves.Add(new Move(sq, oli, PAWN_PIECE_ID, true));
                                }
                            }
                            else if (!obList[i])
                            {
                                if (ULONG_OPERATIONS.IsBitOne(mOptions, 0)) continue;
                                if (b2 && ULONG_OPERATIONS.IsBitOne(mOptions, 3)) continue;
                                if (k == 0)
                                {
                                    if (b3)
                                    {
                                        tmoves2.Add(new Move(sq, oli, PAWN_PIECE_ID, 2, false));
                                        tmoves2.Add(new Move(sq, oli, PAWN_PIECE_ID, 3, false));
                                        tmoves2.Add(new Move(sq, oli, PAWN_PIECE_ID, 4, false));
                                        tmoves2.Add(new Move(sq, oli, PAWN_PIECE_ID, 5, false));
                                    }
                                    else tmoves2.Add(new Move(sq, oli, PAWN_PIECE_ID, false, b2 ? sq - 8 : 65));
                                }
                                if (b)
                                {
                                    if (b3)
                                    {
                                        tmoves.Add(new Move(sq, oli, PAWN_PIECE_ID, 2, false));
                                        tmoves.Add(new Move(sq, oli, PAWN_PIECE_ID, 3, false));
                                        tmoves.Add(new Move(sq, oli, PAWN_PIECE_ID, 4, false));
                                        tmoves.Add(new Move(sq, oli, PAWN_PIECE_ID, 5, false));
                                        continue;
                                    }
                                    tmoves.Add(new Move(sq, oli, PAWN_PIECE_ID, false, b2 ? sq - 8 : 65));
                                }
                            }
                        }
                        pinMovePrecalcs[sq][k].Add(u, tmoves);
                        if (k == 0) movePrecalcs[sq].Add(u, tmoves2);
                    } while (mOptions-- > 0);
                }
            }
        }
    }
}
