using System;

namespace ChessBot
{
    public class ZobristHashing
    {
        private ulong[,] pieceHashes = new ulong[64, 7];
        private ulong blackTurnHash;

        public ZobristHashing()
        {
            Random rng = new Random(2344);
            for (int sq = 0; sq < 64; sq++)
            {
                for (int it = 1; it < 7; it++)
                {
                    pieceHashes[sq, it] = GetRandomULONG(rng); 
                }
            }
            blackTurnHash = GetRandomULONG(rng);
            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(pieceHashes[44, 4]));
        }

        private ulong GetRandomULONG(Random pRNG)
        {
            ulong ru = 0ul;
            for (int i = 0; i < 64; i++) if (pRNG.NextDouble() < 0.5d) ru = ULONG_OPERATIONS.SetBitToOne(ru, i);
            return ru;
        }
    }
}
