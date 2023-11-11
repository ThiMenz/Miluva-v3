using System;
using System.Collections.Generic;

namespace ChessBot
{
    public class KnightMovement
    {
        private const int KNIGHT_PIECE_ID = 2;

        private BoardManager boardManager;

        private ulong[] knightSquareBitboards = new ulong[64];
        private int[][] knightSquareArrays = new int[64][];

        private List<Dictionary<ulong, List<Move>>> nonCapturePrecalcs = new List<Dictionary<ulong, List<Move>>>();
        private List<Dictionary<ulong, List<Move>>> capturePrecalcs = new List<Dictionary<ulong, List<Move>>>();

        public KnightMovement(BoardManager bM)
        {
            boardManager = bM;
            GenerateSquareBitboards();
            Precalculate();
            boardManager.SetKnightMasks(knightSquareBitboards);
        }

        public void AddMovesToMoveOptionList(int square, ulong allPieceBitboard, ulong oppPieceBitboard)
        {
            boardManager.moveOptionList.AddRange(nonCapturePrecalcs[square][knightSquareBitboards[square] & allPieceBitboard]);
            boardManager.moveOptionList.AddRange(capturePrecalcs[square][knightSquareBitboards[square] & oppPieceBitboard]);
        }

        private ushort[] maxOptions = new ushort[9] { 0, 0b1, 0b11, 0b111, 0b1111, 0b11111, 0b111111, 0b1111111, 0b11111111 };

        public void Precalculate()
        {
            for (int sq = 0; sq < 64; sq++)
            {
                int c = ULONG_OPERATIONS.CountBits(knightSquareBitboards[sq]);
                ushort o = maxOptions[c];
                int[] a = knightSquareArrays[sq];
                do
                {
                    ulong curAllPieceBitboard = 0ul;
                    for (int j = 0; j < c; j++)
                        if (ULONG_OPERATIONS.IsBitOne(o, j)) 
                            curAllPieceBitboard = ULONG_OPERATIONS.SetBitToOne(curAllPieceBitboard, a[j]);

                    List<Move> normalMoves = new List<Move>(), captureMoves = new List<Move>();

                    for (int j = 0; j < c; j++)
                    {
                        if (ULONG_OPERATIONS.IsBitOne(curAllPieceBitboard, a[j]))
                            captureMoves.Add(new Move(sq, a[j], KNIGHT_PIECE_ID, true));
                        else normalMoves.Add(new Move(sq, a[j], KNIGHT_PIECE_ID));
                    }

                    nonCapturePrecalcs[sq].Add(curAllPieceBitboard, normalMoves);
                    capturePrecalcs[sq].Add(curAllPieceBitboard, captureMoves);

                } while (o-- > 0);
            }
        }

        public void GenerateSquareBitboards()
        {
            for (int sq = 0; sq < 64; sq++)
            {
                List<int> sqrs = new List<int>();
                ulong u = 0ul;
                int t = sq + 6, sqMod8 = sq % 8;
                if (t < 64 && t % 8 + 2 == sqMod8) { u = ULONG_OPERATIONS.SetBitToOne(u, t); sqrs.Add(t); }
                if ((t += 4) < 64 && t % 8 - 2 == sqMod8) { u = ULONG_OPERATIONS.SetBitToOne(u, t); sqrs.Add(t); }
                if ((t += 5) < 64 && t % 8 + 1 == sqMod8) { u = ULONG_OPERATIONS.SetBitToOne(u, t); sqrs.Add(t); }
                if ((t += 2) < 64 && t % 8 - 1 == sqMod8) { u = ULONG_OPERATIONS.SetBitToOne(u, t); sqrs.Add(t); }
                if ((t -= 34) > -1 && t % 8 + 1 == sqMod8) { u = ULONG_OPERATIONS.SetBitToOne(u, t); sqrs.Add(t); }
                if ((t += 2) > -1 && t % 8 - 1 == sqMod8) { u = ULONG_OPERATIONS.SetBitToOne(u, t); sqrs.Add(t); }
                if ((t += 5) > -1 && t % 8 + 2 == sqMod8) { u = ULONG_OPERATIONS.SetBitToOne(u, t); sqrs.Add(t); }
                if ((t += 4) > -1 && t % 8 - 2 == sqMod8) { u = ULONG_OPERATIONS.SetBitToOne(u, t); sqrs.Add(t); }
                knightSquareBitboards[sq] = u;
                knightSquareArrays[sq] = sqrs.ToArray();
                nonCapturePrecalcs.Add(new Dictionary<ulong, List<Move>>());
                capturePrecalcs.Add(new Dictionary<ulong, List<Move>>());
            }
        }
    }
}
