﻿using System;
using System.Collections.Generic;

namespace Miluva
{
    public static class STATIC_RAYS
    {
        public static Rays? RAY_INSTANCE;
        public static bool PRECALCULATED = false;

        public static void SetPrecalcs(Rays pRI)
        {
            RAY_INSTANCE = pRI;
            PRECALCULATED = true;
        }

        public static Rays? GetPrecalcs()
        {
            return RAY_INSTANCE;
        }
    }

    public class Rays
    {
        public List<int>[] straightLRRayLists = new List<int>[64], straightTBRayLists = new List<int>[64], diagonalLBRayLists = new List<int>[64], diagonalRBRayLists = new List<int>[64];
        public ulong[] straightLRRays = new ulong[64], straightTBRays = new ulong[64], diagonalLBRays = new ulong[64], diagonalRBRays = new ulong[64], diagonalRays = new ulong[64], straightRays = new ulong[64];
        public int[] diagonalLBRayLengths = new int[64], diagonalRBRayLengths = new int[64];

        private ushort[] combinationCount = new ushort[8] { 0, 0b1, 0b11, 0b111, 0b1111, 0b11111, 0b111111, 0b1111111 };

        public Dictionary<ulong, RayPrecalcs>[,] rayPrecalcDictLB = new Dictionary<ulong, RayPrecalcs>[64, 64];
        public Dictionary<ulong, RayPrecalcs>[,] rayPrecalcDictRB = new Dictionary<ulong, RayPrecalcs>[64, 64];
        public Dictionary<ulong, RayPrecalcs>[,] rayPrecalcDictLR = new Dictionary<ulong, RayPrecalcs>[64, 64];
        public Dictionary<ulong, RayPrecalcs>[,] rayPrecalcDictTB = new Dictionary<ulong, RayPrecalcs>[64, 64];

        // rayPrecalcDictDiagonal[ulong]
        // rayPrecalcDictStraight[ulong]

        private List<ulong>[] diagonalOtherRays = new List<ulong>[64];
        private List<ulong>[] straightOtherRays = new List<ulong>[64];

        public Dictionary<ulong, RayPrecalcs>[] rayPrecalcsDiagonal = new Dictionary<ulong, RayPrecalcs>[64];
        public Dictionary<ulong, RayPrecalcs>[] rayPrecalcsStraight = new Dictionary<ulong, RayPrecalcs>[64];

        public Rays()
        {
            if (STATIC_RAYS.PRECALCULATED)
            {
                Rays? tR = STATIC_RAYS.GetPrecalcs();
                if (tR != null)
                {
                    straightLRRayLists = tR.straightLRRayLists;
                    straightTBRayLists = tR.straightTBRayLists;
                    diagonalLBRayLists = tR.diagonalLBRayLists;
                    diagonalRBRayLists = tR.diagonalRBRayLists;
                    straightLRRays = tR.straightLRRays;
                    straightTBRays = tR.straightTBRays;
                    diagonalLBRays = tR.diagonalLBRays;
                    diagonalRBRays = tR.diagonalRBRays;
                    rayPrecalcDictLB = tR.rayPrecalcDictLB;
                    rayPrecalcDictRB = tR.rayPrecalcDictRB;
                    rayPrecalcDictLR = tR.rayPrecalcDictLR;
                    rayPrecalcDictTB = tR.rayPrecalcDictTB;
                    diagonalLBRayLengths = tR.diagonalLBRayLengths;
                    diagonalRBRayLengths = tR.diagonalRBRayLengths;
                    rayPrecalcsDiagonal = tR.rayPrecalcsDiagonal;
                    rayPrecalcsStraight = tR.rayPrecalcsStraight;
                    diagonalRays = tR.diagonalRays;
                    straightRays = tR.straightRays;
                }
            }
            else 
            { 
                for (int sq = 0; sq < 64; sq++)
                {
                    rayPrecalcsStraight[sq] = new Dictionary<ulong, RayPrecalcs>();
                    rayPrecalcsDiagonal[sq] = new Dictionary<ulong, RayPrecalcs>();
                    diagonalOtherRays[sq] = new List<ulong>();
                    straightOtherRays[sq] = new List<ulong>();
                    for (int k = 0; k < 64; k++)
                    {
                        rayPrecalcDictLB[sq, k] = new Dictionary<ulong, RayPrecalcs>();
                        rayPrecalcDictRB[sq, k] = new Dictionary<ulong, RayPrecalcs>();
                        rayPrecalcDictLR[sq, k] = new Dictionary<ulong, RayPrecalcs>();
                        rayPrecalcDictTB[sq, k] = new Dictionary<ulong, RayPrecalcs>();
                    }
                }
                PrecalculateAllRayBitboards();
                PrecalculateAllRays();
                STATIC_RAYS.SetPrecalcs(this);
            }
            //ulong tu = 0ul;
            //tu = ULONG_OPERATIONS.SetBitsToOne(tu, 5, 7);
            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(tu));
            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(rayPrecalcDictLR[3, 5][tu].attackingBitboard));
        }

        private void PrecalculateAllRays()
        {
            for (int sq = 0; sq < 64; sq++)
            {
                PrecalculateAllDiagonalLBRaysFromSquare(sq);
                PrecalculateAllDiagonalRBRaysFromSquare(sq);
                PrecalculateAllStraightLRRaysFromSquare(sq);
                PrecalculateAllStraightTBRaysFromSquare(sq);
            }
        }
        private void SingleAttkSquareIteration(bool pIBO, bool pAOI, int pIfSet, ref ulong rpAttkBB, ref int rpOption, ref int rpPinOption, ref int rpSquare)
        {
            if (pAOI) rpAttkBB = ULONG_OPERATIONS.SetBitToOne(rpAttkBB, rpSquare);
            if (pIBO)
            {
                if (pAOI) rpOption = rpSquare;
                else
                {
                    rpPinOption = rpSquare;
                    rpSquare = pIfSet;
                }
            }
        }

        private void FillDictionaryArray(Dictionary<ulong, RayPrecalcs>[,] pArr, Dictionary<ulong, RayPrecalcs>[] pArr2, List<ulong>[] otherSidedRays, int pSquare, ulong pAttkBB, ulong pTU, int pAscendingOption, int pAscendingPinO, int pDescendingOption, int pDescendingPinO, int pAscendingAdder, ulong pFullBB)
        {
            if (pAscendingOption != -1) pAttkBB = ULONG_OPERATIONS.SetBitToOne(pAttkBB, pAscendingOption);
            if (pDescendingOption != -1) pAttkBB = ULONG_OPERATIONS.SetBitToOne(pAttkBB, pDescendingOption);
            if (pAscendingPinO != -1) pArr[pSquare, pAscendingPinO].Add(pTU, new RayPrecalcs(pAttkBB, ULONG_OPERATIONS.SetBitToOne(0ul, pAscendingOption)));
            if (pDescendingPinO != -1) pArr[pSquare, pDescendingPinO].Add(pTU, new RayPrecalcs(pAttkBB, ULONG_OPERATIONS.SetBitToOne(0ul, pDescendingOption)));
            //pAscendingOption = pDescendingOption = -1;
            //if (pSquare == 3) { Console.WriteLine(pAscendingOption + "-" + pDescendingOption); }
            if (pAscendingOption != -1) pArr[pSquare, pAscendingOption].Add(pTU, new RayPrecalcs(ULONG_OPERATIONS.SetBitToOne(pAttkBB, pAscendingOption + pAscendingAdder), 0ul));
            if (pDescendingOption != -1) pArr[pSquare, pDescendingOption].Add(pTU, new RayPrecalcs(ULONG_OPERATIONS.SetBitToOne(pAttkBB, pDescendingOption - pAscendingAdder), 0ul));

            // (pArr2 == null && otherSidedRays == straightOtherRays && (~straightRays[pSquare] & pTU) != 0ul) 
            //  Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(pTU));

            RayPrecalcs tRP = new RayPrecalcs(pAttkBB, 0ul);
            for (int kSq = 0; kSq < 64; kSq++)
            {
                if (kSq == pAscendingPinO || kSq == pDescendingPinO || kSq == pAscendingOption || kSq == pDescendingOption) continue;
                pArr[pSquare, kSq].Add(pTU, tRP);
            }

            if (pArr2 != null)
            {
                int tC = otherSidedRays[pSquare].Count;
                for (int i = 0; i < tC; i++)
                {
                    ulong u = otherSidedRays[pSquare][i];
                    if (otherSidedRays == diagonalOtherRays)
                    {
                        //if ((~diagonalRays[pSquare] & (rayPrecalcDictLB[pSquare, pSquare][u].attackingBitboard | pAttkBB)) != 0ul)
                        //{
                        //    Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(rayPrecalcDictLB[pSquare, pSquare][u].attackingBitboard));
                        //    Console.WriteLine(" <=> ");
                        //    Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(pAttkBB));
                        //}
                        rayPrecalcsDiagonal[pSquare].Add(u | pFullBB, new RayPrecalcs(rayPrecalcDictLB[pSquare, pSquare][u].attackingBitboard | pAttkBB, 0ul));
                    }
                    else
                    {

                       //if ((~straightRays[pSquare] & (rayPrecalcDictLR[pSquare, pSquare][u].attackingBitboard | pAttkBB)) != 0ul)
                       //{
                       //    Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(rayPrecalcDictLR[pSquare, pSquare][u].attackingBitboard));
                       //    Console.WriteLine(" <=> ");
                       //    Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(pAttkBB));
                       //}
                       ////if ((~straightRays[pSquare] & (rayPrecalcDictLR[pSquare, 0][u].attackingBitboard | pAttkBB)) != 0ul)
                       ////    Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(rayPrecalcDictLR[pSquare, 0][u].attackingBitboard | pAttkBB));
                        rayPrecalcsStraight[pSquare].Add(u | pFullBB, new RayPrecalcs(rayPrecalcDictLR[pSquare, pSquare][u].attackingBitboard | pAttkBB, 0ul));
                    }
                }
            }
        }

        #region | RUNTIME FUNCTIONS |

        public void DiagonalRays(ulong pAllPieceBitboard, int pSquare, int pKingSquare, ref ulong curAttkBitboard, ref ulong curPinnedPieces)
        {
            RayPrecalcs trp = rayPrecalcDictLB[pSquare, pKingSquare][pAllPieceBitboard & diagonalLBRays[pSquare]];
            RayPrecalcs trp2 = rayPrecalcDictRB[pSquare, pKingSquare][pAllPieceBitboard & diagonalRBRays[pSquare]];
            curAttkBitboard |= trp.attackingBitboard | trp2.attackingBitboard;
            curPinnedPieces |= trp.pinnedPieceBitboard | trp2.pinnedPieceBitboard;
        }

        public void StraightRays(ulong pAllPieceBitboard, int pSquare, int pKingSquare, ref ulong curAttkBitboard, ref ulong curPinnedPieces)
        {
            RayPrecalcs trp = rayPrecalcDictLR[pSquare, pKingSquare][pAllPieceBitboard & straightLRRays[pSquare]];
            RayPrecalcs trp2 = rayPrecalcDictTB[pSquare, pKingSquare][pAllPieceBitboard & straightTBRays[pSquare]];
            curAttkBitboard |= trp.attackingBitboard | trp2.attackingBitboard;
            curPinnedPieces |= trp.pinnedPieceBitboard | trp2.pinnedPieceBitboard;
        }
        public int DiagonalRaySquareCount(ulong pAllPieceBitboard, int pSquare)
        {
            return rayPrecalcsDiagonal[pSquare][pAllPieceBitboard & diagonalRays[pSquare]].attackCount;
        }
        public int DiagonalRaySquareCount(ulong pAllPieceBitboard, int pSquare, ref ulong curAttkBitboard)
        {
            RayPrecalcs trp = rayPrecalcsDiagonal[pSquare][pAllPieceBitboard & diagonalRays[pSquare]];
            curAttkBitboard |= trp.attackingBitboard;
            return trp.attackCount;
        }
        public int StraightRaySquareCount(ulong pAllPieceBitboard, int pSquare)
        {
            return rayPrecalcsStraight[pSquare][pAllPieceBitboard & straightRays[pSquare]].attackCount;
        }
        public int StraightRaySquareCount(ulong pAllPieceBitboard, int pSquare, ref ulong curAttkBitboard)
        {
            RayPrecalcs trp = rayPrecalcsStraight[pSquare][pAllPieceBitboard & straightRays[pSquare]];
            curAttkBitboard |= trp.attackingBitboard;
            return trp.attackCount;
        }

        public void DiagonalRayLB(ulong pAllPieceBitboard, int pSquare, int pKingSquare, ref ulong curAttkBitboard, ref ulong curPinnedPieces)
        {
            RayPrecalcs trp = rayPrecalcDictLB[pSquare, pKingSquare][pAllPieceBitboard & diagonalLBRays[pSquare]];
            curAttkBitboard |= trp.attackingBitboard;
            curPinnedPieces |= trp.pinnedPieceBitboard;
        }

        public void DiagonalRayRB(ulong pAllPieceBitboard, int pSquare, int pKingSquare, ref ulong curAttkBitboard, ref ulong curPinnedPieces)
        {
            RayPrecalcs trp = rayPrecalcDictRB[pSquare, pKingSquare][pAllPieceBitboard & diagonalRBRays[pSquare]];
            curAttkBitboard |= trp.attackingBitboard;
            curPinnedPieces |= trp.pinnedPieceBitboard;
        }

        public void StraightRayLR(ulong pAllPieceBitboard, int pSquare, int pKingSquare, ref ulong curAttkBitboard, ref ulong curPinnedPieces)
        {
            RayPrecalcs trp = rayPrecalcDictLR[pSquare, pKingSquare][pAllPieceBitboard & straightLRRays[pSquare]];
            curAttkBitboard |= trp.attackingBitboard;
            curPinnedPieces |= trp.pinnedPieceBitboard;
        }

        public void StraightRayTB(ulong pAllPieceBitboard, int pSquare, int pKingSquare, ref ulong curAttkBitboard, ref ulong curPinnedPieces)
        {
            RayPrecalcs trp = rayPrecalcDictTB[pSquare, pKingSquare][pAllPieceBitboard & straightTBRays[pSquare]];
            curAttkBitboard |= trp.attackingBitboard;
            curPinnedPieces |= trp.pinnedPieceBitboard;
        }

        #endregion

        #region | LINE PRECALCULATIONS |

        private void PrecalculateAllDiagonalLBRaysFromSquare(int pSquare) //
        {
            int c = diagonalLBRayLengths[pSquare];

            //Console.WriteLine("  Sq := " + pSquare);
            //Console.WriteLine(c);
            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(diagonalLBRays[pSquare]));

            ushort o = combinationCount[c];
            List<int> tl = diagonalLBRayLists[pSquare];
            do {
                ulong tu = 0ul, attkBB = 0ul;
                int ascendingOption = -1, descendingOption = -1, ascendingPinO = -1, descendingPinO = -1;
                for (int i = 0; i < c; i++)
                {
                    if (ULONG_OPERATIONS.IsBitZero(o, i)) continue;
                    tu = ULONG_OPERATIONS.SetBitToOne(tu, tl[i]);
                }
                //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(tu));
                diagonalOtherRays[pSquare].Add(tu);

                for (int t = pSquare + 7; t < 64 && t % 8 != 7; t += 7)
                    SingleAttkSquareIteration(ULONG_OPERATIONS.IsBitOne(tu, t), ascendingOption == -1, 64, ref attkBB, ref ascendingOption, ref ascendingPinO, ref t);
                for (int t = pSquare - 7; t > -1 && t % 8 != 0; t -= 7)
                    SingleAttkSquareIteration(ULONG_OPERATIONS.IsBitOne(tu, t), descendingOption == -1, -1, ref attkBB, ref descendingOption, ref descendingPinO, ref t);
                FillDictionaryArray(rayPrecalcDictLB, null, diagonalOtherRays, pSquare, attkBB, tu, ascendingOption, ascendingPinO, descendingOption, descendingPinO, 7, tu);
            } while (o-- != 0);

            //Console.WriteLine(".");
        }

        private void PrecalculateAllDiagonalRBRaysFromSquare(int pSquare)
        {
            int c = diagonalRBRayLengths[pSquare];
            ushort o = combinationCount[c];
            List<int> tl = diagonalRBRayLists[pSquare];
            do
            {
                ulong tu = 0ul, attkBB = 0ul;
                int ascendingOption = -1, descendingOption = -1, ascendingPinO = -1, descendingPinO = -1;
                for (int i = 0; i < c; i++)
                {
                    if (ULONG_OPERATIONS.IsBitZero(o, i)) continue;
                    tu = ULONG_OPERATIONS.SetBitToOne(tu, tl[i]);
                }

                //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(tu));

                for (int t = pSquare + 9; t < 64 && t % 8 != 0; t += 9)
                    SingleAttkSquareIteration(ULONG_OPERATIONS.IsBitOne(tu, t), ascendingOption == -1, 64, ref attkBB, ref ascendingOption, ref ascendingPinO, ref t);
                for (int t = pSquare - 9; t > -1 && t % 8 != 7; t -= 9)
                    SingleAttkSquareIteration(ULONG_OPERATIONS.IsBitOne(tu, t), descendingOption == -1, -1, ref attkBB, ref descendingOption, ref descendingPinO, ref t);

                FillDictionaryArray(rayPrecalcDictRB, rayPrecalcsDiagonal, diagonalOtherRays, pSquare, attkBB, tu, ascendingOption, ascendingPinO, descendingOption, descendingPinO, 9, tu);
            } while (o-- != 0);
        }

        private void PrecalculateAllStraightLRRaysFromSquare(int pSquare) //
        {
            ushort o = combinationCount[7];
            int tmin = pSquare - (pSquare % 8) - 1, tmax = tmin + 9;

            //Console.WriteLine(pSquare + ". " + tmin + " -> " + tmax);

            List<int> tl = straightLRRayLists[pSquare];
            do
            {
                ulong tu = 0ul, attkBB = 0ul;
                int ascendingOption = -1, descendingOption = -1, ascendingPinO = -1, descendingPinO = -1;
                for (int i = 0; i < 7; i++)
                {
                    if (ULONG_OPERATIONS.IsBitZero(o, i)) continue;
                    tu = ULONG_OPERATIONS.SetBitToOne(tu, tl[i]);
                }

                //if (ULONG_OPERATIONS.IsBitOne(tu, 63)) Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(tu));

                straightOtherRays[pSquare].Add(tu);

                for (int t = pSquare + 1; t < tmax; t++)
                    SingleAttkSquareIteration(ULONG_OPERATIONS.IsBitOne(tu, t), ascendingOption == -1, 64, ref attkBB, ref ascendingOption, ref ascendingPinO, ref t);
                for (int t = pSquare - 1; t > tmin; t--)
                    SingleAttkSquareIteration(ULONG_OPERATIONS.IsBitOne(tu, t), descendingOption == -1, -1, ref attkBB, ref descendingOption, ref descendingPinO, ref t);

                FillDictionaryArray(rayPrecalcDictLR, null, straightOtherRays, pSquare, attkBB, tu, ascendingOption, ascendingPinO, descendingOption, descendingPinO, 1, tu);
            } while (o-- != 0);
        }

        private void PrecalculateAllStraightTBRaysFromSquare(int pSquare)
        {
            ushort o = combinationCount[7];
            List<int> tl = straightTBRayLists[pSquare];
            do
            {
                ulong tu = 0ul, attkBB = 0ul;
                int ascendingOption = -1, descendingOption = -1, ascendingPinO = -1, descendingPinO = -1;
                for (int i = 0; i < 7; i++)
                {
                    if (ULONG_OPERATIONS.IsBitZero(o, i)) continue;
                    tu = ULONG_OPERATIONS.SetBitToOne(tu, tl[i]);
                }
                for (int t = pSquare + 8; t < 64; t += 8)
                    SingleAttkSquareIteration(ULONG_OPERATIONS.IsBitOne(tu, t), ascendingOption == -1, 64, ref attkBB, ref ascendingOption, ref ascendingPinO, ref t);
                for (int t = pSquare - 8; t > -1; t -= 8)
                    SingleAttkSquareIteration(ULONG_OPERATIONS.IsBitOne(tu, t), descendingOption == -1, -1, ref attkBB, ref descendingOption, ref descendingPinO, ref t);

                FillDictionaryArray(rayPrecalcDictTB, rayPrecalcsStraight, straightOtherRays, pSquare, attkBB, tu, ascendingOption, ascendingPinO, descendingOption, descendingPinO, 8, tu);
            } while (o-- != 0);
        }

        #endregion

        #region | RAY MASK PRECALCULATIONS |

        private void PrecalculateAllRayBitboards()
        {
            for (int sq = 0; sq < 64; sq++)
            {
                ulong u1 = 0ul, u2 = u1, u3 = u1, u4 = u1;
                List<int> l1 = new List<int>(), l2 = new List<int>(), l3 = new List<int>(), l4 = new List<int>();
                for (int t = sq + 9; t < 64 && t % 8 != 0; t += 9) 
                { u1 = ULONG_OPERATIONS.SetBitToOne(u1, t); l1.Add(t); }
                for (int t = sq - 9; t > -1 && t % 8 != 7; t -= 9)
                { u1 = ULONG_OPERATIONS.SetBitToOne(u1, t); l1.Add(t); }
                for (int t = sq + 7; t < 64 && t % 8 != 7; t += 7)
                { u2 = ULONG_OPERATIONS.SetBitToOne(u2, t); l2.Add(t); }
                for (int t = sq - 7; t > -1 && t % 8 != 0; t -= 7) 
                { u2 = ULONG_OPERATIONS.SetBitToOne(u2, t); l2.Add(t); }
                int rsv = sq - sq % 8;
                for (int t = rsv; t == rsv || t % 8 != 0; t++)
                {
                    if (t != sq)
                    { u3 = ULONG_OPERATIONS.SetBitToOne(u3, t); l3.Add(t); }
                }
                for (int t = sq + 8; t < 64; t += 8) 
                { u4 = ULONG_OPERATIONS.SetBitToOne(u4, t); l4.Add(t); }
                for (int t = sq - 8; t > -1; t -= 8)
                { u4 = ULONG_OPERATIONS.SetBitToOne(u4, t); l4.Add(t); }

                //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(u3));
                //foreach (int i in l3)
                //    Console.WriteLine(i);

                diagonalRBRayLengths[sq] = ULONG_OPERATIONS.CountBits(diagonalRBRays[sq] = u1);
                diagonalLBRayLengths[sq] = ULONG_OPERATIONS.CountBits(diagonalLBRays[sq] = u2);
                straightLRRays[sq] = u3;
                straightTBRays[sq] = u4;
                straightLRRayLists[sq] = l3;
                straightTBRayLists[sq] = l4;
                diagonalLBRayLists[sq] = l2;
                diagonalRBRayLists[sq] = l1;
                diagonalRays[sq] = u1 | u2;
                straightRays[sq] = u3 | u4;
            }
        }

        #endregion
    }

    public class RayPrecalcs
    {
        public ulong attackingBitboard { get; private set; }
        public ulong pinnedPieceBitboard { get; private set; }
        public int attackCount { get; private set; }

        public RayPrecalcs(ulong pAttkBB, ulong pPpBB)
        {
            attackingBitboard = pAttkBB;
            pinnedPieceBitboard = pPpBB;
            attackCount = ULONG_OPERATIONS.CountBits(attackingBitboard);
        }

        public void Add(ulong pAttkBB)
        {
            attackingBitboard |= pAttkBB;
            attackCount = ULONG_OPERATIONS.CountBits(attackingBitboard);
        }
    }
}
