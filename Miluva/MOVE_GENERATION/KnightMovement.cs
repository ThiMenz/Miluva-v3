using System;
using System.Collections.Generic;

namespace Miluva
{
    public static class STATIC_KNIGHTMOVEMENT
    {
        public static List<Dictionary<ulong, List<Move>>> NON_CAPTURE_PRECALCULATIONS = new List<Dictionary<ulong, List<Move>>>();
        public static List<Dictionary<ulong, List<Move>>> CAPTURES_PRECALCULATIONS = new List<Dictionary<ulong, List<Move>>>();
        public static ulong[] KNIGHT_SQUARE_BITBOARDS = new ulong[64];
        public static int[][] KNIGHT_SQUARE_ARRAYS = new int[64][];

        public static bool PRECALCULATED = false;

        public static void SetPrecalcs(List<Dictionary<ulong, List<Move>>> pCPC, List<Dictionary<ulong, List<Move>>> pNCPC, ulong[] pKSB, int[][] pKSArrys)
        {
            NON_CAPTURE_PRECALCULATIONS = pNCPC;
            CAPTURES_PRECALCULATIONS = pCPC;
            KNIGHT_SQUARE_BITBOARDS = pKSB;
            KNIGHT_SQUARE_ARRAYS = pKSArrys;
            PRECALCULATED = true;
        }
    }

    public class KnightMovement
    {
        private const int KNIGHT_PIECE_ID = 2;

        private IBoardManager boardManager;

        private ulong[] knightSquareBitboards = new ulong[64];
        private int[][] knightSquareArrays = new int[64][];

        private List<Dictionary<ulong, List<Move>>> nonCapturePrecalcs = new List<Dictionary<ulong, List<Move>>>();
        private List<Dictionary<ulong, List<Move>>> capturePrecalcs = new List<Dictionary<ulong, List<Move>>>();

        public KnightMovement(IBoardManager bM)
        {
            boardManager = bM;
            if (STATIC_KNIGHTMOVEMENT.PRECALCULATED) 
            {
                capturePrecalcs = STATIC_KNIGHTMOVEMENT.CAPTURES_PRECALCULATIONS;
                nonCapturePrecalcs = STATIC_KNIGHTMOVEMENT.NON_CAPTURE_PRECALCULATIONS;
                knightSquareBitboards = STATIC_KNIGHTMOVEMENT.KNIGHT_SQUARE_BITBOARDS;
                knightSquareArrays = STATIC_KNIGHTMOVEMENT.KNIGHT_SQUARE_ARRAYS;
            }
            else 
            { 
                GenerateSquareBitboards();
                Precalculate();
                STATIC_KNIGHTMOVEMENT.SetPrecalcs(capturePrecalcs, nonCapturePrecalcs, knightSquareBitboards, knightSquareArrays);
            }
            boardManager.SetKnightMasks(knightSquareBitboards);
        }

        public void AddMovesToMoveOptionList(int square, ulong allPieceBitboard, ulong oppPieceBitboard)
        {
            boardManager.moveOptionList.AddRange(nonCapturePrecalcs[square][knightSquareBitboards[square] & allPieceBitboard]);
            boardManager.moveOptionList.AddRange(capturePrecalcs[square][knightSquareBitboards[square] & oppPieceBitboard]);
        }

        public void AddMovesToMoveOptionListOnlyCaptures(int square, ulong oppPieceBitboard)
        {
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
