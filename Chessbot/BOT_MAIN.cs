using System;
using System.Diagnostics;
using System.Linq;

namespace ChessBot
{
    public static class BOT_MAIN
    {
        public readonly static string[] FIGURE_TO_ID_LIST = new string[] { "Nichts", "Bauer", "Springer", "Läufer", "Turm", "Dame", "König" };
        public static void Main(string[] args)
        {
            ULONG_OPERATIONS.SetUpCountingArray();
            _ = new BoardManager("rnbq1bnr/ppppkppp/8/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQha - 0 1");
        }
    }

    public class BoardManager
    {
        private RookMovement rookMovement;
        private BishopMovement bishopMovement;
        private QueenMovement queenMovement;
        private KnightMovement knightMovement;
        private KingMovement kingMovement;
        private WhitePawnMovement whitePawnMovement;
        private Rays rays;
        private ZobristHashing zobristHashing;
        public List<Move> moveOptionList = new List<Move>();

        private Piece[] pieceArray = new Piece[65];
        private List<Piece> pieceList = new List<Piece>();
        private List<Piece> whitePieceList = new List<Piece>();
        private List<Piece> blackPieceList = new List<Piece>();

        public ulong whitePieceBitboard, blackPieceBitboard, allPieceBitboard;
        private int whiteKingSquare, blackKingSquare, enPassantSquare = 65, happenedFullMoves = 0, fiftyMoveRuleCounter = 0;
        private bool whiteCastleRightKingSide, whiteCastleRightQueenSide, blackCastleRightKingSide, blackCastleRightQueenSide;
        private bool isWhiteToMove;

        private const ulong WHITE_KING_ROCHADE = 96, WHITE_QUEEN_ROCHADE = 14, BLACK_KING_ROCHADE = 6917529027641081856, BLACK_QUEEN_ROCHADE = 1008806316530991104;
        private readonly Move mWHITE_KING_ROCHADE = new Move(4, 6, 7, 5), mWHITE_QUEEN_ROCHADE = new Move(4, 2, 0, 3), 
            mBLACK_KING_ROCHADE = new Move(60, 62, 63, 61), mBLACK_QUEEN_ROCHADE = new Move(60, 58, 56, 59);

        private ulong[] knightSquareBitboards = new ulong[64];
        private ulong[] whitePawnAttackSquareBitboards = new ulong[64];
        private ulong[] blackPawnAttackSquareBitboards = new ulong[64];
        private ulong[] kingSquareBitboards = new ulong[64];

        public BoardManager(string fen)
        {
            LoadFenString(fen);

            Stopwatch setupStopwatch = Stopwatch.StartNew();

            Console.Write("[PRECALCS] Zobrist Hashing");
            zobristHashing = new ZobristHashing(); Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");
            Console.Write("[PRECALCS] Square Connectives");
            SquareConnectivesPrecalculations(); Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");
            Console.Write("[PRECALCS] Pawn Attack Bitboards");
            PawnAttackBitboards(); Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");
            Console.Write("[PRECALCS] Rays");
            rays = new Rays(); Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");
            queenMovement = new QueenMovement(this);
            Console.Write("[PRECALCS] Rook Movement");
            rookMovement = new RookMovement(this, queenMovement); Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");
            Console.Write("[PRECALCS] Bishop Movement");
            bishopMovement = new BishopMovement(this, queenMovement); Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");
            Console.Write("[PRECALCS] Knight Movement");
            knightMovement = new KnightMovement(this); Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");
            Console.Write("[PRECALCS] King Movement");
            kingMovement = new KingMovement(this); Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");
            Console.Write("[PRECALCS] Pawn Movement");
            whitePawnMovement = new WhitePawnMovement(this); Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            Console.WriteLine("[DONE]\n\n");

            Stopwatch sw = Stopwatch.StartNew();

            for (int p = 0; p < 1_000_000; p++)
            {
                Minimax();
                //LeafCheckingPieceCheck(44, 45, 5);
            }

            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds + "ms");

            Console.WriteLine((int)((10_000_000d / (double)sw.ElapsedTicks) * 31_000_000));
        }

        private int LeafCheckingPieceCheck(int pStartPos, int pEndPos, int pPieceType)
        {
            int tI, tPossibleAttackPiece;
            if (pPieceType == 1)
            {
                if (ULONG_OPERATIONS.IsBitOne(blackPawnAttackSquareBitboards[pEndPos], whiteKingSquare)) return pEndPos;
            }
            else if (pPieceType == 2)
            {
                if (ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[pEndPos], whiteKingSquare)) return pEndPos;
            }
            else if (pPieceType != 6) {
                tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | pEndPos] & allPieceBitboard];
                if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece) && pieceArray[tPossibleAttackPiece].moveAbilities[squareConnectivesPrecalculationArray[tI]]) return tPossibleAttackPiece;
            }
            tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | pStartPos] & allPieceBitboard];
            if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece) && pieceArray[tPossibleAttackPiece].moveAbilities[squareConnectivesPrecalculationArray[tI]]) return tPossibleAttackPiece;
            return -1;
        }

        private void Minimax()
        {
            int tCheckingPieceSquare = LeafCheckingPieceCheck(49, 50, 5);

            // -> Schneller Leaf Check Check für Check Extensions (hmm, vielleicht auch einfach seperater Quiescence Search Generator; wäre besser imo, wohl da müsste man das ja auch iwi haben)
            // -> Queens [DONE]
            // -> Knights [DONE]
            // -> Pawns (+En Passant, +Promotions) [DONE]
            //    - Control Bitboards BoardManager
            //    - PreCalc Dicts on Pin and without Pin (including Promotions)
            //    - Iwi En Passant Handling (Mask != 0ul >> Add En Passant zu Square)
            // -> Kings & 1 & 2+ Check Cases & Rochade ahh (Bei 1 erstmal die Lazy Variante wählen) [DONE]

            // -> Repetitions (Zobrist Keys)
            // -> Draw by 50 Move Rule
            // -> Other Sided Minimax

            moveOptionList.Clear();
            ulong oppDiagonalSliderVision = 0ul, oppStraightSliderVision = 0ul, oppStaticPieceVision = 0ul, oppAttkBitboard, pinnedPieces = 0ul;
            for (int p = blackPieceList.Count; p-- > 0;)
            {
                Piece pc = blackPieceList[p];

                switch (pc.pieceTypeID)
                {
                    case 1:
                        oppStaticPieceVision |= blackPawnAttackSquareBitboards[pc.square];
                        break;
                    case 2:
                        oppStaticPieceVision |= knightSquareBitboards[pc.square];
                        break;
                    case 3:
                        rays.DiagonalRays(allPieceBitboard, pc.square, whiteKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        break;
                    case 4:
                        rays.StraightRays(allPieceBitboard, pc.square, whiteKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 5:
                        rays.DiagonalRays(allPieceBitboard, pc.square, whiteKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        rays.StraightRays(allPieceBitboard, pc.square, whiteKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 6:
                        oppStaticPieceVision |= kingSquareBitboards[pc.square];
                        break;
                }
            }
            oppAttkBitboard = oppDiagonalSliderVision | oppStraightSliderVision | oppStaticPieceVision;
            int curCheckCount = ULONG_OPERATIONS.TrippleIsBitOne(oppDiagonalSliderVision, oppStaticPieceVision, oppStraightSliderVision, whiteKingSquare);

            // Bis hierhin ~500ms/10mil in Test 1
            if (curCheckCount == 0)
            {
                if (whiteCastleRightKingSide && ((allPieceBitboard | oppAttkBitboard) & WHITE_KING_ROCHADE) == 0ul) moveOptionList.Add(mWHITE_KING_ROCHADE);
                if (whiteCastleRightQueenSide && ((allPieceBitboard | oppAttkBitboard) & WHITE_QUEEN_ROCHADE) == 0ul) moveOptionList.Add(mWHITE_QUEEN_ROCHADE);

                // En Passant +~300ms/10mil in Test 1
                if (enPassantSquare != 65 && (whitePieceBitboard & blackPawnAttackSquareBitboards[enPassantSquare]) != 0ul)
                {
                    int shiftedKS = whiteKingSquare << 6, epM9 = enPassantSquare - 9, epM8 = epM9 + 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, epM9) && pieceArray[epM9].pieceTypeID == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker1) && pieceArray[possibleAttacker1].moveAbilities[squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker2) && pieceArray[possibleAttacker2].moveAbilities[squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                                moveOptionList.Add(new Move(false, epM9, enPassantSquare, enPassantSquare - 8));
                    }
                    epM9 += 2;
                    if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, epM9) && pieceArray[epM9].pieceTypeID == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker1) && pieceArray[possibleAttacker1].moveAbilities[squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker2) && pieceArray[possibleAttacker2].moveAbilities[squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                                moveOptionList.Add(new Move(false, epM9, enPassantSquare, enPassantSquare - 8));
                    }
                }
                for (int p = whitePieceList.Count; p-- > 0;)
                {
                    Piece pc = whitePieceList[p];
                    int pcSquare = pc.square;

                    switch (pc.pieceTypeID)
                    {
                        case 1:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, pcSquare)) whitePawnMovement.AddMoveOptionsToMoveList(pcSquare, whitePieceBitboard, blackPieceBitboard);
                            else whitePawnMovement.AddMoveOptionsToMoveList(pcSquare, whiteKingSquare, whitePieceBitboard, blackPieceBitboard);
                            break;
                        case 2:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, pcSquare)) knightMovement.AddMovesToMoveOptionList(pcSquare, allPieceBitboard, blackPieceBitboard);
                            break;
                        case 3:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, pcSquare)) bishopMovement.AddMoveOptionsToMoveList(pcSquare, blackPieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(pcSquare, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, pcSquare)) rookMovement.AddMoveOptionsToMoveList(pcSquare, blackPieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(pcSquare, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, pcSquare)) queenMovement.AddMoveOptionsToMoveList(pcSquare, blackPieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveList(pcSquare, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveList(pcSquare, oppAttkBitboard | whitePieceBitboard, ~oppAttkBitboard & blackPieceBitboard);
                            break;
                    }
                }
            }
            else if (curCheckCount == 1)
            {
                ulong tCheckingPieceLine = squareConnectivesPrecalculationLineArray[whiteKingSquare << 6 | tCheckingPieceSquare];
                if (ULONG_OPERATIONS.IsBitOne(tCheckingPieceLine, enPassantSquare) && enPassantSquare != 65 && (whitePieceBitboard & blackPawnAttackSquareBitboards[enPassantSquare]) != 0ul)
                {
                    int shiftedKS = whiteKingSquare << 6, epM9 = enPassantSquare - 9, epM8 = epM9 + 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, epM9) && pieceArray[epM9].pieceTypeID == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker1) && pieceArray[possibleAttacker1].moveAbilities[squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker2) && pieceArray[possibleAttacker2].moveAbilities[squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            moveOptionList.Add(new Move(false, epM9, enPassantSquare, enPassantSquare - 8));
                    }
                    epM9 += 2;
                    if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, epM9) && pieceArray[epM9].pieceTypeID == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker1) && pieceArray[possibleAttacker1].moveAbilities[squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker2) && pieceArray[possibleAttacker2].moveAbilities[squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            moveOptionList.Add(new Move(false, epM9, enPassantSquare, enPassantSquare - 8));
                    }
                }
                for (int p = whitePieceList.Count; p-- > 0;)
                {
                    Piece pc = whitePieceList[p];
                    int pcSquare = pc.square;
                
                    switch (pc.pieceTypeID)
                    {
                        case 1:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, pcSquare)) whitePawnMovement.AddMoveOptionsToMoveList(pcSquare, whitePieceBitboard, blackPieceBitboard);
                            else whitePawnMovement.AddMoveOptionsToMoveList(pcSquare, whiteKingSquare, whitePieceBitboard, blackPieceBitboard);
                            break;
                        case 2:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, pcSquare)) knightMovement.AddMovesToMoveOptionList(pcSquare, allPieceBitboard, blackPieceBitboard);
                            break;
                        case 3:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, pcSquare)) bishopMovement.AddMoveOptionsToMoveList(pcSquare, blackPieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(pcSquare, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, pcSquare)) rookMovement.AddMoveOptionsToMoveList(pcSquare, blackPieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(pcSquare, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, pcSquare)) queenMovement.AddMoveOptionsToMoveList(pcSquare, blackPieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveList(pcSquare, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveList(pcSquare, oppAttkBitboard | whitePieceBitboard, ~oppAttkBitboard & blackPieceBitboard);
                            break;
                    }
                }
                int s_molc = moveOptionList.Count;
                List<Move> tMoves = new List<Move>();
                for (int m = 0; m < s_molc; m++)
                {
                    Move mm = moveOptionList[m];
                    if (mm.pieceType == 6) tMoves.Add(moveOptionList[m]);
                    else if (ULONG_OPERATIONS.IsBitOne(tCheckingPieceLine, moveOptionList[m].endPos)) tMoves.Add(moveOptionList[m]);
                }
                moveOptionList = tMoves;
            }
            else kingMovement.AddMoveOptionsToMoveList(whiteKingSquare, oppAttkBitboard | whitePieceBitboard, ~oppAttkBitboard & blackPieceBitboard);
            // Bis hierhin: 1700ms/10mil in Test 1

            int molc = moveOptionList.Count;
            for (int m = 0; m < molc; m++)
            {
                Move curMove = moveOptionList[m];
                // Bis hierhin: 2150ms/10mil in Test 1

                //Console.WriteLine(curMove);
            }
        }

        public void SetKnightMasks(ulong[] uls)
        {
            knightSquareBitboards = uls;
        }

        public void SetKingMasks(ulong[] uls)
        {
            kingSquareBitboards = uls;
        }

        private char[] fenPieces = new char[7] { 'z', 'p', 'n', 'b', 'r', 'q', 'k' };

        public void LoadFenString(string fenStr)
        {
            for (int i = 0; i < 64; i++) pieceArray[i] = new Piece();

            string[] spaceSpl = fenStr.Split(' ');
            string[] rowSpl = spaceSpl[0].Split('/');

            string epstr = spaceSpl[3];
            if (epstr == "-") enPassantSquare = 65;
            else enPassantSquare = (epstr[0] - 'a') + 8 * (epstr[1] - '1');

            isWhiteToMove = true;
            if (spaceSpl[1] == "b") isWhiteToMove = false;

            string crstr = spaceSpl[2];
            whiteCastleRightKingSide = whiteCastleRightQueenSide = blackCastleRightKingSide = blackCastleRightQueenSide = false;
            int crStrL = crstr.Length;
            for (int i = 0; i < crStrL; i++) {
                switch (crstr[i])
                {
                    case 'K':
                        whiteCastleRightKingSide = true;
                        break;
                    case 'Q':
                        whiteCastleRightQueenSide = true;
                        break;
                    case 'k':
                        blackCastleRightKingSide = true;
                        break;
                    case 'q':
                        blackCastleRightQueenSide = true;
                        break;
                }
            }

            fiftyMoveRuleCounter = Convert.ToInt32(spaceSpl[4]);
            happenedFullMoves = Convert.ToInt32(spaceSpl[5]);

            int tSq = 0;
            for (int i = rowSpl.Length; i-- > 0; )
            {
                string tStr = rowSpl[i];
                int tStrLen = tStr.Length;
                for (int c = 0; c < tStrLen; c++)
                {
                    char tChar = tStr[c];
                    if (Char.IsDigit(tChar)) tSq += tChar - '0';
                    else
                    {
                        bool tCol = Char.IsLower(tChar);
                        if (tCol) blackPieceBitboard = ULONG_OPERATIONS.SetBitToOne(blackPieceBitboard, tSq);
                        else whitePieceBitboard = ULONG_OPERATIONS.SetBitToOne(whitePieceBitboard, tSq);

                        if (tChar == 'k') blackKingSquare = tSq;
                        else if (tChar == 'K') whiteKingSquare = tSq;

                        pieceArray[tSq] = new Piece(!tCol, Array.IndexOf(fenPieces, Char.ToLower(tChar)), tSq);

                        pieceList.Add(pieceArray[tSq]);
                        if (tCol) blackPieceList.Add(pieceArray[tSq]);
                        else whitePieceList.Add(pieceArray[tSq]);

                        tSq++;
                    }
                }
            }

            allPieceBitboard = whitePieceBitboard | blackPieceBitboard;
        }

        private void PawnAttackBitboards()
        {
            for (int sq = 0; sq < 64; sq++)
            {
                ulong u = 0ul;
                if (sq % 8 != 0) u = ULONG_OPERATIONS.SetBitToOne(u, sq + 7);
                if (sq % 8 != 7) u = ULONG_OPERATIONS.SetBitToOne(u, sq + 9);
                whitePawnAttackSquareBitboards[sq] = u;
                u = 0ul;
                if (sq % 8 != 0 && sq - 9 > -1) u = ULONG_OPERATIONS.SetBitToOne(u, sq - 9);
                if (sq % 8 != 7 && sq - 7 > -1) u = ULONG_OPERATIONS.SetBitToOne(u, sq - 7);
                blackPawnAttackSquareBitboards[sq] = u;
            }
        }

        //364 Rays, 1400 RayBits & 10588/10952 RayCombinations
        //TECHNICAL OPTIMIZATION: EDGE RAYS
        private int[] squareConnectivesPrecalculationArray = new int[4096];
        public int[] squareConnectivesCrossDirsPrecalculationArray = new int[4096];
        private ulong[] squareConnectivesPrecalculationRayArray = new ulong[4096];
        private ulong[] squareConnectivesPrecalculationLineArray = new ulong[4096];

        private List<Dictionary<ulong, int>> rayCollidingSquareCalculations = new List<Dictionary<ulong, int>>();

        private readonly int[] maxIntWithSpecifiedBitcount = new int[8] { 0, 0b1, 0b11, 0b111, 0b1111, 0b11111, 0b111111, 0b1111111 };

        private List<ulong> differentRays = new List<ulong>();

        private void SquareConnectivesPrecalculations()
        {
            for (int square = 0; square < 64; square++)
            {
                rayCollidingSquareCalculations.Add(new Dictionary<ulong, int>());
                rayCollidingSquareCalculations[square].Add(0ul, square);
                for (int square2 = 0; square2 < 64; square2++)
                {
                    if (square == square2) continue;

                    int t = 0, t2 = 0, difSign = Math.Sign(square2 - square), itSq = square, g = 0;
                    ulong tRay = 0ul, tExclRay = 0ul;
                    List<int> tRayInts = new List<int>();
                    if (difSign == -1) g = 7;

                    if (square % 8 == square2 % 8) // Untere oder Obere Gerade
                    {
                        t2 = t = 1;
                        while ((itSq += difSign * 8) < 64 && itSq > -1)
                        {
                            tRay = ULONG_OPERATIONS.SetBitToOne(tRay, itSq); tRayInts.Add(itSq);
                            if (itSq == square2) { tExclRay = tRay; }
                        }
                    }
                    else if ((square - square % 8) == (square2 - square2 % 8)) // Linke oder Rechte Gerade
                    {
                        t = 1;
                        t2 = -1;
                        while ((itSq += difSign) % 8 != g && itSq > -1 && itSq < 64) { tRay = ULONG_OPERATIONS.SetBitToOne(tRay, itSq); tRayInts.Add(itSq); if (itSq == square2) { tExclRay = tRay; } }
                    }
                    else
                    {
                        int dif = Math.Abs(square2 - square);
                        if (dif % 7 == 0) // Diagonalen von Rechts
                        {
                            t = 2;
                            t2 = 2;
                            g = (difSign == 1) ? 7 : 0;
                            while ((itSq += difSign * 7) % 8 != g && itSq < 64 && itSq > -1) { tRay = ULONG_OPERATIONS.SetBitToOne(tRay, itSq); tRayInts.Add(itSq); if (itSq == square2) { tExclRay = tRay; } }
                            if (ULONG_OPERATIONS.IsBitZero(tRay, square2))
                            {
                                t2 = t = 0;
                                tRay = 0ul;
                            }
                        }
                        else if (dif % 9 == 0) //Diagonalen von Links
                        {
                            t = 2;
                            t2 = -2;
                            while ((itSq += difSign * 9) % 8 != g && itSq < 64 && itSq > -1) { tRay = ULONG_OPERATIONS.SetBitToOne(tRay, itSq); tRayInts.Add(itSq); if (itSq == square2) { tExclRay = tRay; } }
                            if (ULONG_OPERATIONS.IsBitZero(tRay, square2))
                            {
                                t2 = t = 0;
                                tRay = 0ul;
                            }
                        }
                    }

                    if (tRay != 0ul && !differentRays.Contains(tRay))
                    {
                        differentRays.Add(tRay);
                        int ccount, combI = maxIntWithSpecifiedBitcount[ccount = ULONG_OPERATIONS.CountBits(tRay)];
                        do
                        {
                            ulong curAllPieceBitboard = 0ul;
                            int solution = square;
                            for (int j = 0; j < ccount; j++)
                            {
                                if (((combI >> j) & 1) == 1)
                                {
                                    curAllPieceBitboard = ULONG_OPERATIONS.SetBitToOne(curAllPieceBitboard, tRayInts[j]);
                                    if (solution == square) solution = tRayInts[j];
                                }
                            }
                            rayCollidingSquareCalculations[square].Add(curAllPieceBitboard, solution);
                        } while (--combI != 0);
                    }
                    squareConnectivesPrecalculationArray[square << 6 | square2] = t;
                    squareConnectivesPrecalculationRayArray[square << 6 | square2] = tRay;
                    squareConnectivesCrossDirsPrecalculationArray[square << 6 | square2] = t2;
                    squareConnectivesPrecalculationLineArray[square << 6 | square2] = ULONG_OPERATIONS.SetBitToOne(tExclRay, square2);
                }
            }
        }
    }

    public class Piece
    {
        public bool isNull;
        public bool isWhite;
        public bool[] moveAbilities = new bool[3];
        public int pieceTypeID;
        public int square;

        public Piece()
        {
            isNull = true;
            isWhite = true;
            moveAbilities = new bool[3] { false, false, false };
            pieceTypeID = 0;
        }
        public Piece(bool color, int pieceType, int pSquare)
        {
            isNull = false;
            isWhite = color;
            if (pieceType == 3) moveAbilities = new bool[3] { false, false, true };
            if (pieceType == 4) moveAbilities = new bool[3] { false, true, false };
            if (pieceType == 5) moveAbilities = new bool[3] { false, true, true };
            pieceTypeID = pieceType;
            square = pSquare;
        }

        public Piece(Piece copy)
        {
            isNull = copy.isNull;
            isWhite = copy.isWhite;
            moveAbilities = copy.moveAbilities;
            pieceTypeID = copy.pieceTypeID;
        }
    }

    public class Move
    {
        public int startPos { get; private set; }
        public int endPos { get; private set; }
        public int rochadeStartPos { get; private set; }
        public int rochadeEndPos { get; private set; }
        public int pieceType { get; private set; }
        public int enPassantOption { get; private set; } = 65;
        public int promotionType { get; private set; } = 0;
        public bool isCapture { get; private set; } = false;
        public bool isSliderMove { get; private set; }
        public bool isEnPassant { get; private set; } = false;
        public bool isPromotion { get; private set; } = false;
        public bool isRochade { get; private set; } = false;

        public Move(int pSP, int pEP, int pPT)
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
        }
        public Move(int pSP, int pEP, int pRSP, int pREP)
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = 6;
            isSliderMove = false;
            isRochade = true;
            rochadeStartPos = pRSP;
            rochadeEndPos = pREP;
        }
        public Move(int pSP, int pEP, int pPT, bool pIC)
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            isCapture = pIC;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
        }
        public Move(int pSP, int pEP, int pPT, bool pIC, int enPassPar)
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            isCapture = pIC;
            enPassantOption = enPassPar;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
        }
        public Move(bool b, int pSP, int pEP, int pEPS)
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = 1;
            isCapture = isEnPassant = true;
            enPassantOption = pEPS;
            isSliderMove = false;
        }
        public Move(int pSP, int pEP, int pPT, int promType, bool pIC)
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            isPromotion = true;
            isCapture = pIC;    
            promotionType = promType;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
        }
        public override string ToString()
        {
            string s = "[" + BOT_MAIN.FIGURE_TO_ID_LIST[pieceType] + "] " + startPos + " -> " + endPos;
            if (enPassantOption != 65) s += " [EP = " + enPassantOption + "] ";
            if (isCapture) s += " /CAPTURE/";
            if (isEnPassant) s += " /EN PASSANT/";
            if (isRochade) s += " /ROCHADE/";
            if (isPromotion) s += " /" + BOT_MAIN.FIGURE_TO_ID_LIST[promotionType] + "-PROMOTION/";
            return s;
        }
    }
}