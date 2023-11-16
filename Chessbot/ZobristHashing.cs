using System;

namespace ChessBot
{
    public class ZobristHashing
    {
        private ulong[,] pieceHashesWhite = new ulong[64, 7], pieceHashesBlack = new ulong[64, 7];
        private ulong blackTurnHash, whiteKingSideRochadeRightHash, whiteQueenSideRochadeRightHash, blackKingSideRochadeRightHash, blackQueenSideRochadeRightHash;
        private ulong[] enPassantSquareHashes = new ulong[65]; 

        public ZobristHashing(BoardManager pBM)
        {
            Random rng = new Random(2344);
            for (int sq = 0; sq < 64; sq++)
            {
                enPassantSquareHashes[sq + 1] = GetRandomULONG(rng);
                for (int it = 1; it < 7; it++)
                {
                    pieceHashesWhite[sq, it] = GetRandomULONG(rng); 
                    pieceHashesBlack[sq, it] = GetRandomULONG(rng); 
                }
            }
            enPassantSquareHashes[0] = 0ul;
            pBM.SetZobristSideSwap(blackTurnHash = GetRandomULONG(rng));
            whiteKingSideRochadeRightHash = GetRandomULONG(rng);
            whiteQueenSideRochadeRightHash = GetRandomULONG(rng);
            blackKingSideRochadeRightHash = GetRandomULONG(rng);
            blackQueenSideRochadeRightHash = GetRandomULONG(rng);
        }

        public void ApplyHashingOnWhiteNormalMove(ref ulong pCurHash, int pStartPos, int pEndPos, int pPieceType, int pEnPassantSquare)
        {
            pCurHash ^= pieceHashesWhite[pStartPos, pPieceType] 
                ^ pieceHashesWhite[pEndPos, pPieceType] 
                ^ enPassantSquareHashes[pEnPassantSquare];
        }

        public void ApplyHashingOnWhiteCaptureMove(ref ulong pCurHash, int pStartPos, int pEndPos, int pPieceType, int pCapturePieceType, int pEnPassantSquare)
        {
            pCurHash ^= pieceHashesWhite[pStartPos, pPieceType] 
                ^ pieceHashesWhite[pEndPos, pPieceType] 
                ^ pieceHashesBlack[pEndPos, pCapturePieceType] 
                ^ enPassantSquareHashes[pEnPassantSquare];
        }

        private ulong GetRandomULONG(Random pRNG)
        {
            ulong ru = 0ul;
            for (int i = 0; i < 64; i++) if (pRNG.NextDouble() < 0.5d) ru = ULONG_OPERATIONS.SetBitToOne(ru, i);
            return ru;
        }
    }
}
