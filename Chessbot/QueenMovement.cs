using System;
using System.Collections.Generic;

namespace ChessBot
{
    public static class STATIC_QUEENMOVEMENT
    {
        public static List<Dictionary<ulong, BishopPreCalcs>> PRECALCULATED_MOVES_DIAGONAL = new List<Dictionary<ulong, BishopPreCalcs>>();
        public static List<Dictionary<ulong, RookPreCalcs>> PRECALCULATED_MOVES_STRAIGHT = new List<Dictionary<ulong, RookPreCalcs>>();
        public static ulong[] DIAGONAL_MASKS = new ulong[64];
        public static ulong[] STRAIGHT_MASKS = new ulong[64];

        public static bool PRECALCULATED = false;

        public static void SetPrecalcs(List<Dictionary<ulong, BishopPreCalcs>> pPCM, List<Dictionary<ulong, RookPreCalcs>> pPCM2, ulong[] pBM, ulong[] pRM)
        {
            PRECALCULATED_MOVES_STRAIGHT = pPCM2;
            STRAIGHT_MASKS = pRM;
            PRECALCULATED_MOVES_DIAGONAL = pPCM;
            DIAGONAL_MASKS = pBM;
            PRECALCULATED = true;
        }
    }

    public class QueenMovement
    {
        private BoardManager boardManager;

        private List<Dictionary<ulong, BishopPreCalcs>> precalculatedMovesDiagonal = new List<Dictionary<ulong, BishopPreCalcs>>();
        private List<Dictionary<ulong, RookPreCalcs>> precalculatedMovesStraigth = new List<Dictionary<ulong, RookPreCalcs>>();

        private const int QUEEN_TILE_ID = 5;

        private ulong[] diagonalMasks = new ulong[64], straightMasks = new ulong[64];

        private Dictionary<int, BishopPreCalcs> tempDiaPreCalcDict = new Dictionary<int, BishopPreCalcs>();
        private Dictionary<int, RookPreCalcs> tempStrPreCalcDict = new Dictionary<int, RookPreCalcs>();

        public QueenMovement(BoardManager bM)
        {
            boardManager = bM;
            for (int i = 0; i < 64; i++)
            {
                precalculatedMovesDiagonal.Add(new Dictionary<ulong, BishopPreCalcs>());
                precalculatedMovesStraigth.Add(new Dictionary<ulong, RookPreCalcs>());
            }
        }

        public void SetStatics()
        {
            STATIC_QUEENMOVEMENT.DIAGONAL_MASKS = diagonalMasks;
            STATIC_QUEENMOVEMENT.STRAIGHT_MASKS = straightMasks;
            STATIC_QUEENMOVEMENT.PRECALCULATED_MOVES_DIAGONAL = precalculatedMovesDiagonal; 
            STATIC_QUEENMOVEMENT.PRECALCULATED_MOVES_STRAIGHT = precalculatedMovesStraigth;
        }

        public void GetStatics()
        {
            diagonalMasks = STATIC_QUEENMOVEMENT.DIAGONAL_MASKS;
            straightMasks = STATIC_QUEENMOVEMENT.STRAIGHT_MASKS;
            precalculatedMovesDiagonal = STATIC_QUEENMOVEMENT.PRECALCULATED_MOVES_DIAGONAL;
            precalculatedMovesStraigth = STATIC_QUEENMOVEMENT.PRECALCULATED_MOVES_STRAIGHT;
        }

        public void ClearTempDictDiagonal() { tempDiaPreCalcDict.Clear(); }
        public void ClearTempDictStraight() { tempStrPreCalcDict.Clear(); }

        public void SetDiagonalMasks(ulong[] pMasks) { diagonalMasks = pMasks; }
        public void SetStraightMasks(ulong[] pMasks) { straightMasks = pMasks; }

        public void AddMoveOptionsToMoveList(int startSquare, ulong opposingSideBitboard, ulong allPieceBitboard)
        {
            BishopPreCalcs tbpc = precalculatedMovesDiagonal[startSquare][allPieceBitboard & diagonalMasks[startSquare]];
            boardManager.moveOptionList.AddRange(tbpc.classicMoves);

            if (((opposingSideBitboard >> tbpc.possibleCapture1) & 1) == 1) boardManager.moveOptionList.Add(tbpc.captureMove1);
            if (((opposingSideBitboard >> tbpc.possibleCapture2) & 1) == 1) boardManager.moveOptionList.Add(tbpc.captureMove2);
            if (((opposingSideBitboard >> tbpc.possibleCapture3) & 1) == 1) boardManager.moveOptionList.Add(tbpc.captureMove3);
            if (((opposingSideBitboard >> tbpc.possibleCapture4) & 1) == 1) boardManager.moveOptionList.Add(tbpc.captureMove4);

            RookPreCalcs trpc = precalculatedMovesStraigth[startSquare][allPieceBitboard & straightMasks[startSquare]];
            boardManager.moveOptionList.AddRange(trpc.classicMoves);

            if (((opposingSideBitboard >> trpc.possibleCapture1) & 1) == 1) boardManager.moveOptionList.Add(trpc.captureMove1);
            if (((opposingSideBitboard >> trpc.possibleCapture2) & 1) == 1) boardManager.moveOptionList.Add(trpc.captureMove2);
            if (((opposingSideBitboard >> trpc.possibleCapture3) & 1) == 1) boardManager.moveOptionList.Add(trpc.captureMove3);
            if (((opposingSideBitboard >> trpc.possibleCapture4) & 1) == 1) boardManager.moveOptionList.Add(trpc.captureMove4);
        }

        public void AddMoveOptionsToMoveList(int startSquare, int ownKingPosFilter, ulong opposingSideBitboard, ulong allPieceBitboard)
        {
            BishopPreCalcsKingPin tbpc = precalculatedMovesDiagonal[startSquare][allPieceBitboard & diagonalMasks[startSquare]].classicMovesOnKingPin[ownKingPosFilter];
            //Console.WriteLine(startSquare + ": " + (allPieceBitboard & diagonalMasks[startSquare]));
            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(allPieceBitboard & diagonalMasks[startSquare]));
            boardManager.moveOptionList.AddRange(tbpc.moves);

            if (((opposingSideBitboard >> tbpc.possibleCapture1) & 1) == 1) boardManager.moveOptionList.Add(tbpc.captureMove1);
            if (((opposingSideBitboard >> tbpc.possibleCapture2) & 1) == 1) boardManager.moveOptionList.Add(tbpc.captureMove2);

            RookPreCalcsKingPin trpc = precalculatedMovesStraigth[startSquare][allPieceBitboard & straightMasks[startSquare]].classicMovesOnKingPin[ownKingPosFilter];
            boardManager.moveOptionList.AddRange(trpc.moves);

            if (((opposingSideBitboard >> trpc.possibleCapture1) & 1) == 1) boardManager.moveOptionList.Add(trpc.captureMove1);
            if (((opposingSideBitboard >> trpc.possibleCapture2) & 1) == 1) boardManager.moveOptionList.Add(trpc.captureMove2);
        }

        public void AddMoveOptionsToMoveListOnlyCaptures(int startSquare, ulong opposingSideBitboard, ulong allPieceBitboard)
        {
            BishopPreCalcs tbpc = precalculatedMovesDiagonal[startSquare][allPieceBitboard & diagonalMasks[startSquare]];
            if (((opposingSideBitboard >> tbpc.possibleCapture1) & 1) == 1) boardManager.moveOptionList.Add(tbpc.captureMove1);
            if (((opposingSideBitboard >> tbpc.possibleCapture2) & 1) == 1) boardManager.moveOptionList.Add(tbpc.captureMove2);
            if (((opposingSideBitboard >> tbpc.possibleCapture3) & 1) == 1) boardManager.moveOptionList.Add(tbpc.captureMove3);
            if (((opposingSideBitboard >> tbpc.possibleCapture4) & 1) == 1) boardManager.moveOptionList.Add(tbpc.captureMove4);

            RookPreCalcs trpc = precalculatedMovesStraigth[startSquare][allPieceBitboard & straightMasks[startSquare]];
            if (((opposingSideBitboard >> trpc.possibleCapture1) & 1) == 1) boardManager.moveOptionList.Add(trpc.captureMove1);
            if (((opposingSideBitboard >> trpc.possibleCapture2) & 1) == 1) boardManager.moveOptionList.Add(trpc.captureMove2);
            if (((opposingSideBitboard >> trpc.possibleCapture3) & 1) == 1) boardManager.moveOptionList.Add(trpc.captureMove3);
            if (((opposingSideBitboard >> trpc.possibleCapture4) & 1) == 1) boardManager.moveOptionList.Add(trpc.captureMove4);
        }

        public void AddMoveOptionsToMoveListOnlyCaptures(int startSquare, int ownKingPosFilter, ulong opposingSideBitboard, ulong allPieceBitboard)
        {
            BishopPreCalcsKingPin tbpc = precalculatedMovesDiagonal[startSquare][allPieceBitboard & diagonalMasks[startSquare]].classicMovesOnKingPin[ownKingPosFilter];
            if (((opposingSideBitboard >> tbpc.possibleCapture1) & 1) == 1) boardManager.moveOptionList.Add(tbpc.captureMove1);
            if (((opposingSideBitboard >> tbpc.possibleCapture2) & 1) == 1) boardManager.moveOptionList.Add(tbpc.captureMove2);

            RookPreCalcsKingPin trpc = precalculatedMovesStraigth[startSquare][allPieceBitboard & straightMasks[startSquare]].classicMovesOnKingPin[ownKingPosFilter];
            if (((opposingSideBitboard >> trpc.possibleCapture1) & 1) == 1) boardManager.moveOptionList.Add(trpc.captureMove1);
            if (((opposingSideBitboard >> trpc.possibleCapture2) & 1) == 1) boardManager.moveOptionList.Add(trpc.captureMove2);
        }

        public void AddPrecalculationFromDiaDict(int square, ulong apb, int t)
        {
            precalculatedMovesDiagonal[square].Add(apb, tempDiaPreCalcDict[t]);
        }

        public void AddPrecalculationFromStrDict(int square, ulong apb, int t)
        {
            precalculatedMovesStraigth[square].Add(apb, tempStrPreCalcDict[t]);
        }

        public void AddPrecalculation(int square, ulong apb, RookPreCalcs rpc, int t)
        {
            List<Move> moves = new List<Move>();
            int ccount = rpc.classicMoves.Count;
            for (int i = 0; i < ccount; i++)
            {
                Move tM = rpc.classicMoves[i];
                moves.Add(new Move(tM.startPos, tM.endPos, QUEEN_TILE_ID));
            }
            ccount = rpc.classicMovesOnKingPin.Length;
            RookPreCalcsKingPin[] rpckp = new RookPreCalcsKingPin[ccount];
            for (int i = 0; i < ccount; i++)
            {
                RookPreCalcsKingPin tRPCKP = rpc.classicMovesOnKingPin[i];
                Move tc1 = tRPCKP.captureMove1;
                Move tc2 = tRPCKP.captureMove2;
                List<Move> tRPCKP_Moves = new List<Move>();
                int ccount2 = tRPCKP.moves.Count;
                for (int j = 0; j < ccount2; j++)
                {
                    Move tM = tRPCKP.moves[j];
                    tRPCKP_Moves.Add(new Move(tM.startPos, tM.endPos, QUEEN_TILE_ID));
                }
                rpckp[i] = new RookPreCalcsKingPin(tRPCKP_Moves, tRPCKP.possibleCapture1, tRPCKP.possibleCapture2,
                    (tc1 == null) ? new Move(square, square, QUEEN_TILE_ID) : new Move(tc1.startPos, tc1.endPos, QUEEN_TILE_ID, true),
                    (tc2 == null) ? new Move(square, square, QUEEN_TILE_ID) : new Move(tc2.startPos, tc2.endPos, QUEEN_TILE_ID, true));
            }
            RookPreCalcs modRPC = new RookPreCalcs(moves, rpc.possibleCaptures, rpc.captureMovesQueen, square, rpc.visionBitboard, rpckp, new Move[4]);
            precalculatedMovesStraigth[square].Add(apb, modRPC);
            tempStrPreCalcDict.Add(t, modRPC);
        }
        public void AddPrecalculation(int square, ulong apb, BishopPreCalcs bpc, int t)
        {
            List<Move> moves = new List<Move>();
            int ccount = bpc.classicMoves.Count;
            for (int i = 0; i < ccount; i++)
            {
                Move tM = bpc.classicMoves[i];
                moves.Add(new Move(tM.startPos, tM.endPos, QUEEN_TILE_ID));
            }
            ccount = bpc.classicMovesOnKingPin.Length;
            BishopPreCalcsKingPin[] rpckp = new BishopPreCalcsKingPin[ccount];
            for (int i = 0; i < ccount; i++)
            {
                BishopPreCalcsKingPin tRPCKP = bpc.classicMovesOnKingPin[i];
                Move tc1 = tRPCKP.captureMove1;
                Move tc2 = tRPCKP.captureMove2;
                List<Move> tRPCKP_Moves = new List<Move>();
                int ccount2 = tRPCKP.moves.Count;
                for (int j = 0; j < ccount2; j++)
                {
                    Move tM = tRPCKP.moves[j];
                    tRPCKP_Moves.Add(new Move(tM.startPos, tM.endPos, QUEEN_TILE_ID));
                }
                rpckp[i] = new BishopPreCalcsKingPin(tRPCKP_Moves, tRPCKP.possibleCapture1, tRPCKP.possibleCapture2,
                    (tc1 == null) ? new Move(square, square, QUEEN_TILE_ID) : new Move(tc1.startPos, tc1.endPos, QUEEN_TILE_ID, true),
                    (tc2 == null) ? new Move(square, square, QUEEN_TILE_ID) : new Move(tc2.startPos, tc2.endPos, QUEEN_TILE_ID, true));
            }
            BishopPreCalcs modBPC = new BishopPreCalcs(moves, bpc.possibleCaptures, bpc.captureMovesQueen, square, bpc.visionBitboard, rpckp, new Move[4]);
            tempDiaPreCalcDict.Add(t, modBPC);
            precalculatedMovesDiagonal[square].Add(apb, modBPC);
        }
    }
}
