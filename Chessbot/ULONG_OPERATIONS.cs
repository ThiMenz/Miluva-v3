using System;
using System.Collections.Generic;

namespace ChessBot
{
    public static class ULONG_OPERATIONS
    {
        private static int[] onebyteBitCountingArray = new int[256];
        private static int[] twobyteBitCountingArray = new int[65_536];
        private static int[] threebyteBitCountingArray = new int[16_777_216];

        public static void SetUpCountingArray()
        {
            for (int i = 0; i < 256; i++)
            {
                int c = 0;
                for (int d = 0; d < 8; d++)
                {
                    if (((i >> d) & 1) == 0) continue;
                    c++;
                }
                onebyteBitCountingArray[i] = c;
            }

            for (int i = 0; i < 65536; i++)
                twobyteBitCountingArray[i] = onebyteBitCountingArray[(i >> 8) & 255] + onebyteBitCountingArray[i & 255];

            for (int i = 0; i < 16777216; i++)
                threebyteBitCountingArray[i] = onebyteBitCountingArray[((i >> 16) & 255)] + twobyteBitCountingArray[i & 65535];
        }

        public static ulong ManageTwoBits(ulong u, int Zindex1, int Oindex2)
        {
            return 1ul << Oindex2 | ((ulong.MaxValue ^ 1ul << Zindex1) & u);
        }

        public static ulong ReverseByteOrder(ulong u)
        {
            return (u & 0xFF00000000000000) >> 56 | 
                (u & 0xFF000000000000) >> 40 | 
                (u & 0xFF0000000000) >> 24 |
                (u & 0xFF00000000) >> 8 |
                (u & 0xFF000000) << 8 |
                (u & 0xFF0000) << 24 |
                (u & 0xFF00) << 40 |
                (u & 0xFF) << 56;
        }

        public static int CountBits(ulong u)
        {
            return twobyteBitCountingArray[(u >> 48) & 65535] + threebyteBitCountingArray[(u >> 24) & 16777215] + threebyteBitCountingArray[u & 16777215];
        }

        public static int TrippleIsBitOne(ulong u1, ulong u2, ulong u3, int index)
        {
            return (((int)(u1 >> index) & 1) + ((int)(u2 >> index) & 1) + ((int)(u3 >> index) & 1));
        }

        public static bool IsBitOne(ulong u, int index)
        {
            return ((u >> index) & 1ul) == 1ul;
        }

        public static bool IsBitZero(ulong u, int index)
        {
            return ((u >> index) & 1ul) == 0ul;
        }

        public static ulong SetBitToOne(ulong u, int index)
        {
            return (1ul << index) | u;
        }

        public static ulong SetBitsToOne(ulong u, params int[] index)
        {
            int tL = index.Length;
            for (int i = 0; i < tL; i++) u |= (1ul << index[i]);
            return u;
        }

        public static ulong LegacySetBitToZero(ulong u, int index)
        {
            return (ulong.MaxValue ^ (1ul << index)) & u;
        }

        public static ulong SetBitToZero(ulong u, int index)
        {
            return ~(1ul << index) & u;
        }

        public static string GetStringBoardVisualization(ulong u)
        {
            string r = "";

            for (int i = 7; i > -1; i--)
            {
                for (int j = 0; j < 8; j++)
                {
                    r += IsBitOne(u, j + i * 8) ? " (#) " : " ( ) ";
                }
                r += "\n\n";
            }

            return r;
        }

        public static ulong GetRandomULONG(Random pRNG)
        {
            ulong ru = 0ul;
            for (int i = 0; i < 64; i++) if (pRNG.NextDouble() < 0.5d) ru = ULONG_OPERATIONS.SetBitToOne(ru, i);
            return ru;
        }
    }
}
