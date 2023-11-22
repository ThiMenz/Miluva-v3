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
            _ = new BoardManager("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1");
            //r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1
            //8/8/8/2k5/8/8/7p/4K3 b - - 2 1
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
        private BlackPawnMovement blackPawnMovement;
        private Rays rays;
        public List<Move> moveOptionList;
        private int[] pieceTypeArray = new int[64];

        public ulong whitePieceBitboard, blackPieceBitboard, allPieceBitboard;
        private ulong zobristKey;
        private int whiteKingSquare, blackKingSquare, enPassantSquare = 65, happenedHalfMoves = 0, fiftyMoveRuleCounter = 0;
        private bool whiteCastleRightKingSide, whiteCastleRightQueenSide, blackCastleRightKingSide, blackCastleRightQueenSide;
        private bool isWhiteToMove;

        private const ulong WHITE_KING_ROCHADE = 96, WHITE_QUEEN_ROCHADE = 14, BLACK_KING_ROCHADE = 6917529027641081856, BLACK_QUEEN_ROCHADE = 1008806316530991104, WHITE_QUEEN_ATTK_ROCHADE = 12, BLACK_QUEEN_ATTK_ROCHADE = 864691128455135232;
        private readonly Move mWHITE_KING_ROCHADE = new Move(4, 6, 7, 5), mWHITE_QUEEN_ROCHADE = new Move(4, 2, 0, 3),
            mBLACK_KING_ROCHADE = new Move(60, 62, 63, 61), mBLACK_QUEEN_ROCHADE = new Move(60, 58, 56, 59);

        private ulong[] knightSquareBitboards = new ulong[64];
        private ulong[] whitePawnAttackSquareBitboards = new ulong[64];
        private ulong[] blackPawnAttackSquareBitboards = new ulong[64];
        private ulong[] kingSquareBitboards = new ulong[64];

        private bool[,] pieceTypeAbilities = new bool[7, 3] 
        {
            { false, false, false }, // Null
            { false, false, false }, // Bauer
            { false, false, false }, // Springer 
            { false, false, true }, // Läufer
            { false, true, false }, // Turm
            { false, true, true }, // Dame
            { false, false, false }  // König
        };

        private ulong[] curSearchZobristKeyLine; //Die History muss bis zum letzten Capture, PawnMove, der letzten Rochade gehen oder dem Spielbeginn gehen
        // -> Schneller Leaf Check Check für Check Extensions (hmm, vielleicht auch einfach seperater Quiescence Search Generator; wäre besser imo, wohl da müsste man das ja auch iwi haben)
        // -> Queens [DONE]
        // -> Knights [DONE]
        // -> Pawns (+En Passant, +Promotions) [DONE]
        //    - Control Bitboards BoardManager
        //    - PreCalc Dicts on Pin and without Pin (including Promotions)
        //    - Iwi En Passant Handling (Mask != 0ul >> Add En Passant zu Square)
        // -> Kings & 1 & 2+ Check Cases & Rochade ahh (Bei 1 erstmal die Lazy Variante wählen) [DONE]
        // -> Repetitions (Zobrist Keys) [ALMOST DONE]
        // -> Draw by 50 Move Rule
        // -> Other Sided Minimax
        public BoardManager(string fen)
        {
            Stopwatch setupStopwatch = Stopwatch.StartNew();

            Console.Write("[PRECALCS] Zobrist Hashing");
            InitZobrist();
            Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            Console.Write("[PRECALCS] Square Connectives");
            SquareConnectivesPrecalculations();
            Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            Console.Write("[PRECALCS] Pawn Attack Bitboards");
            PawnAttackBitboards();
            Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            Console.Write("[PRECALCS] Rays");
            rays = new Rays();
            Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            queenMovement = new QueenMovement(this);
            Console.Write("[PRECALCS] Rook Movement");
            rookMovement = new RookMovement(this, queenMovement);
            Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            Console.Write("[PRECALCS] Bishop Movement");
            bishopMovement = new BishopMovement(this, queenMovement);
            Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            Console.Write("[PRECALCS] Knight Movement");
            knightMovement = new KnightMovement(this);
            Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            Console.Write("[PRECALCS] King Movement");
            kingMovement = new KingMovement(this);
            Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");

            Console.Write("[PRECALCS] Pawn Movement");
            whitePawnMovement = new WhitePawnMovement(this);
            blackPawnMovement = new BlackPawnMovement(this);
            Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");
            Console.WriteLine("[DONE]\n\n");

            //LoadFenString("rnb1kb1r/ppppqBpp/5n2/4p3/3PP3/4QN2/PPP2PPP/RNBK3R b - - 0 6");
            //tempTest = zobristKey;
            LoadFenString(fen);

            Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(2310359833064709192));
            Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(2107775));
            Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(4769031049176344816));
            Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(11667243342067991040));
            Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(squareConnectivesPrecalculationRayArray[61 << 6 | 25]));
            Console.WriteLine(rayCollidingSquareCalculations[61][ULONG_OPERATIONS.SetBitsToOne(0ul, 16, 25)]);
            Console.WriteLine(pieceTypeAbilities[3, squareConnectivesPrecalculationArray[61 << 6 | 25]]);

            Stopwatch sw = Stopwatch.StartNew();

            curSearchZobristKeyLine = new ulong[1];

            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(squareConnectivesPrecalculationRayArray[4023]));

            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(4503599627370496ul));

            //Console.WriteLine(CreateFenString());
            int tPerft = MinimaxRoot(6);
            //for (int p = 0; p < 1; p++)
            //{
            //    int tPerft = MinimaxRoot(6);
            //    //Array.Copy(i234, i235, 64);
            //    //MinimaxWhite(0);
            //    //MinimaxBlack(0);
            //    //LeafCheckingPieceCheck(44, 45, 5);
            //}


            //Console.WriteLine(CreateFenString());


            sw.Stop();
            Console.WriteLine(GetThreeDigitSeperatedInteger(tPerft) + " Moves");
            Console.WriteLine(sw.ElapsedMilliseconds + "ms");

            Console.WriteLine(GetThreeDigitSeperatedInteger((int)((10_000_000d / (double)sw.ElapsedTicks) * tPerft)) + " NpS");
        }

        private string GetThreeDigitSeperatedInteger(int pInt)
        {
            string s = pInt.ToString(), r = s[0].ToString();
            int t = s.Length % 3;

            for (int i = 1; i < s.Length; i++) {
                if (i % 3 == t) r += ".";
                r += s[i];
            }

            s = "";

            for (int i = 0; i < r.Length; i++)
            {
                s += r[i];
            }

            return s;
        }

        private Move[] tMoveLine = new Move[10];

        private int PreMinimaxCheckCheckWhite()
        {
            ulong bitShiftedKingPos = 1ul << whiteKingSquare;
            for (int p = 0; p < 64; p++)
            {
                if (ULONG_OPERATIONS.IsBitZero(blackPieceBitboard, p)) continue;
                int tPT;
                switch (tPT = pieceTypeArray[p])
                {
                    case 1:
                        if ((bitShiftedKingPos & blackPawnAttackSquareBitboards[p]) != 0ul) return p;
                        break;
                    case 2:
                        if ((bitShiftedKingPos & knightSquareBitboards[p]) != 0ul) return p;
                        break;
                    case 6: break;
                    default:
                        if ((squareConnectivesPrecalculationLineArray[whiteKingSquare << 6 | p] & allPieceBitboard) == (1ul << p) && pieceTypeAbilities[tPT, squareConnectivesPrecalculationArray[whiteKingSquare << 6 | p]]) return p;
                        break;
                }
            }

            return -1;
        }

        private int PreMinimaxCheckCheckBlack()
        {
            ulong bitShiftedKingPos = 1ul << blackKingSquare;
            for (int p = 0; p < 64; p++)
            {
                if (ULONG_OPERATIONS.IsBitZero(whitePieceBitboard, p)) continue;
                int tPT;
                switch (tPT = pieceTypeArray[p])
                {
                    case 1:
                        if ((bitShiftedKingPos & whitePawnAttackSquareBitboards[p]) != 0ul) return p;
                        break;
                    case 2:
                        if ((bitShiftedKingPos & knightSquareBitboards[p]) != 0ul) return p;
                        break;
                    case 6: break;
                    default:
                        if ((squareConnectivesPrecalculationLineArray[blackKingSquare << 6 | p] & allPieceBitboard) == (1ul << p) && pieceTypeAbilities[tPT, squareConnectivesPrecalculationArray[blackKingSquare << 6 | p]]) return p;
                        break;
                }
            }

            return -1;
        }

        private int LeafCheckingPieceCheckWhite(int pStartPos, int pEndPos, int pPieceType)
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
                if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) return tPossibleAttackPiece;
            }
            tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | pStartPos] & allPieceBitboard];
            if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) return tPossibleAttackPiece;
            return -1;
        }

        private int LeafCheckingPieceCheckBlack(int pStartPos, int pEndPos, int pPieceType)
        {
            int tI, tPossibleAttackPiece;
            if (pPieceType == 1)
            {
                if (ULONG_OPERATIONS.IsBitOne(whitePawnAttackSquareBitboards[pEndPos], blackKingSquare)) return pEndPos;
            }
            else if (pPieceType == 2)
            {
                if (ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[pEndPos], blackKingSquare)) return pEndPos;
            }
            else if (pPieceType != 6)
            {
                tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | pEndPos] & allPieceBitboard];
                if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) return tPossibleAttackPiece;
            }
            tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | pStartPos] & allPieceBitboard];
            if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) return tPossibleAttackPiece;
            return -1;
        }

        private void GetLegalWhiteMoves(int pCheckingPieceSquare, ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppDiagonalSliderVision = 0ul, oppStraightSliderVision = 0ul, oppStaticPieceVision = 0ul, oppAttkBitboard, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if (ULONG_OPERATIONS.IsBitZero(blackPieceBitboard, p)) continue;
                switch (pieceTypeArray[p])
                {
                    case 1:
                        oppStaticPieceVision |= blackPawnAttackSquareBitboards[p];
                        break;
                    case 2:
                        oppStaticPieceVision |= knightSquareBitboards[p];
                        break;
                    case 3:
                        rays.DiagonalRays(allPieceBitboard, p, whiteKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        break;
                    case 4:
                        rays.StraightRays(allPieceBitboard, p, whiteKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 5:
                        rays.DiagonalRays(allPieceBitboard, p, whiteKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        rays.StraightRays(allPieceBitboard, p, whiteKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 6:
                        oppStaticPieceVision |= kingSquareBitboards[p];
                        break;
                }
            }

            oppAttkBitboard = oppDiagonalSliderVision | oppStraightSliderVision | oppStaticPieceVision;
            int curCheckCount = ULONG_OPERATIONS.TrippleIsBitOne(oppDiagonalSliderVision, oppStaticPieceVision, oppStraightSliderVision, whiteKingSquare);

            if (curCheckCount == 0)
            {
                if (whiteCastleRightKingSide && ((allPieceBitboard | oppAttkBitboard) & WHITE_KING_ROCHADE) == 0ul) pMoveList.Add(mWHITE_KING_ROCHADE);
                if (whiteCastleRightQueenSide && (allPieceBitboard & WHITE_QUEEN_ROCHADE | oppAttkBitboard & WHITE_QUEEN_ATTK_ROCHADE) == 0ul) pMoveList.Add(mWHITE_QUEEN_ROCHADE);

                if (enPassantSquare != 65 && (whitePieceBitboard & blackPawnAttackSquareBitboards[enPassantSquare]) != 0ul)
                {
                    int shiftedKS = whiteKingSquare << 6, epM9 = enPassantSquare - 9, epM8 = epM9 + 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, epM9) && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 0)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker1) && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker2) && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                                pMoveList.Add(new Move(false, epM9, enPassantSquare, enPassantSquare - 8));
                    }
                    epM9 += 2;
                    if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, epM9) && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 7)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker1) && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker2) && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                                pMoveList.Add(new Move(false, epM9, enPassantSquare, enPassantSquare - 8));
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (ULONG_OPERATIONS.IsBitZero(whitePieceBitboard, p)) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) whitePawnMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, blackPieceBitboard);
                            else whitePawnMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, whitePieceBitboard, blackPieceBitboard);
                            break;
                        case 2:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, blackPieceBitboard);
                            break;
                        case 3:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) bishopMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) rookMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) queenMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveList(p, oppAttkBitboard | whitePieceBitboard, ~oppAttkBitboard & blackPieceBitboard);
                            break;
                    }
                }
            }
            else if (curCheckCount == 1)
            {
                ulong tCheckingPieceLine = squareConnectivesPrecalculationLineArray[whiteKingSquare << 6 | pCheckingPieceSquare];
                if (enPassantSquare != 65 && (whitePieceBitboard & blackPawnAttackSquareBitboards[enPassantSquare]) != 0ul && (ULONG_OPERATIONS.IsBitOne(tCheckingPieceLine, enPassantSquare) || (pCheckingPieceSquare + 8 == enPassantSquare && pieceTypeArray[pCheckingPieceSquare] == 1)))
                {
                    int shiftedKS = whiteKingSquare << 6, epM9 = enPassantSquare - 9, epM8 = epM9 + 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, epM9) && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker1) && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker2) && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(new Move(false, epM9, enPassantSquare, enPassantSquare - 8));
                    }
                    epM9 += 2;
                    if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, epM9) && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker1) && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker2) && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(new Move(false, epM9, enPassantSquare, enPassantSquare - 8));
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (ULONG_OPERATIONS.IsBitZero(whitePieceBitboard, p)) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) whitePawnMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, blackPieceBitboard);
                            else whitePawnMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, whitePieceBitboard, blackPieceBitboard);
                            break;
                        case 2:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, blackPieceBitboard);
                            break;
                        case 3:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) bishopMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) rookMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) queenMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveList(p, oppAttkBitboard | whitePieceBitboard, ~oppAttkBitboard & blackPieceBitboard);
                            break;
                    }
                }
                int s_molc = pMoveList.Count;
                List<Move> tMoves = new List<Move>();
                for (int m = 0; m < s_molc; m++)
                {
                    Move mm = pMoveList[m];
                    if (mm.pieceType == 6) tMoves.Add(pMoveList[m]);
                    else if (ULONG_OPERATIONS.IsBitOne(tCheckingPieceLine, pMoveList[m].endPos) || mm.isEnPassant) tMoves.Add(pMoveList[m]);
                }
                pMoveList = tMoves;
            }
            else kingMovement.AddMoveOptionsToMoveList(whiteKingSquare, oppAttkBitboard | whitePieceBitboard, ~oppAttkBitboard & blackPieceBitboard);
        }

        private void GetLegalBlackMoves(int pCheckingPieceSquare, ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppDiagonalSliderVision = 0ul, oppStraightSliderVision = 0ul, oppStaticPieceVision = 0ul, oppAttkBitboard, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if (ULONG_OPERATIONS.IsBitZero(whitePieceBitboard, p)) continue;
                switch (pieceTypeArray[p])
                {
                    case 1:
                        oppStaticPieceVision |= whitePawnAttackSquareBitboards[p];
                        break;
                    case 2:
                        oppStaticPieceVision |= knightSquareBitboards[p];
                        break;
                    case 3:
                        rays.DiagonalRays(allPieceBitboard, p, blackKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        break;
                    case 4:
                        rays.StraightRays(allPieceBitboard, p, blackKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 5:
                        rays.DiagonalRays(allPieceBitboard, p, blackKingSquare, ref oppDiagonalSliderVision, ref pinnedPieces);
                        rays.StraightRays(allPieceBitboard, p, blackKingSquare, ref oppStraightSliderVision, ref pinnedPieces);
                        break;
                    case 6:
                        oppStaticPieceVision |= kingSquareBitboards[p];
                        break;
                }
            }

            oppAttkBitboard = oppDiagonalSliderVision | oppStraightSliderVision | oppStaticPieceVision;
            int curCheckCount = ULONG_OPERATIONS.TrippleIsBitOne(oppDiagonalSliderVision, oppStaticPieceVision, oppStraightSliderVision, blackKingSquare);
            if (curCheckCount == 0)
            {
                if (blackCastleRightKingSide && ((allPieceBitboard | oppAttkBitboard) & BLACK_KING_ROCHADE) == 0ul) pMoveList.Add(mBLACK_KING_ROCHADE);
                if (blackCastleRightQueenSide && (allPieceBitboard & BLACK_QUEEN_ROCHADE | oppAttkBitboard & BLACK_QUEEN_ATTK_ROCHADE) == 0ul) pMoveList.Add(mBLACK_QUEEN_ROCHADE);

                if (enPassantSquare != 65 && (blackPieceBitboard & whitePawnAttackSquareBitboards[enPassantSquare]) != 0ul)
                {
                    int shiftedKS = blackKingSquare << 6, epM9 = enPassantSquare + 9, epM8 = epM9 - 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, epM9) && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 7)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, possibleAttacker1) && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, possibleAttacker2) && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(new Move(false, epM9, enPassantSquare, enPassantSquare + 8));
                    }
                    epM9 -= 2;
                    if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, epM9) && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 0)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, possibleAttacker1) && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, possibleAttacker2) && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(new Move(false, epM9, enPassantSquare, enPassantSquare + 8));
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (ULONG_OPERATIONS.IsBitZero(blackPieceBitboard, p)) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) blackPawnMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, whitePieceBitboard);
                            else blackPawnMovement.AddMoveOptionsToMoveList(p, blackKingSquare, blackPieceBitboard, whitePieceBitboard);
                            break;
                        case 2:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, whitePieceBitboard);
                            break;
                        case 3:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) bishopMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) rookMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) queenMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveList(p, oppAttkBitboard | blackPieceBitboard, ~oppAttkBitboard & whitePieceBitboard);
                            break;
                    }
                }
            }
            else if (curCheckCount == 1)
            {
                //pCheckingPieceSquare +- 8 == enPassantSquare && pieceTypeList[pCheckingPieceSquare] == 1
                ulong tCheckingPieceLine = squareConnectivesPrecalculationLineArray[blackKingSquare << 6 | pCheckingPieceSquare];
                if (enPassantSquare != 65 && (blackPieceBitboard & whitePawnAttackSquareBitboards[enPassantSquare]) != 0ul && (ULONG_OPERATIONS.IsBitOne(tCheckingPieceLine, enPassantSquare) || (pCheckingPieceSquare - 8 == enPassantSquare && pieceTypeArray[pCheckingPieceSquare] == 1)))
                {
                    int shiftedKS = blackKingSquare << 6, epM9 = enPassantSquare + 9, epM8 = epM9 - 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, epM9) && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, possibleAttacker1) && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, possibleAttacker2) && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(new Move(false, epM9, enPassantSquare, enPassantSquare + 8));
                    }
                    epM9 -= 2;
                    if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, epM9) && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, possibleAttacker1) && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, possibleAttacker2) && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(new Move(false, epM9, enPassantSquare, enPassantSquare + 8));
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (ULONG_OPERATIONS.IsBitZero(blackPieceBitboard, p)) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) blackPawnMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, whitePieceBitboard);
                            else blackPawnMovement.AddMoveOptionsToMoveList(p, blackKingSquare, blackPieceBitboard, whitePieceBitboard);
                            break;
                        case 2:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, whitePieceBitboard);
                            break;
                        case 3:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) bishopMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) rookMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (ULONG_OPERATIONS.IsBitZero(pinnedPieces, p)) queenMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else queenMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 6:
                            kingMovement.AddMoveOptionsToMoveList(p, oppAttkBitboard | blackPieceBitboard, ~oppAttkBitboard & whitePieceBitboard);
                            break;
                    }
                }
                int s_molc = pMoveList.Count;
                List<Move> tMoves = new List<Move>();
                for (int m = 0; m < s_molc; m++)
                {
                    Move mm = pMoveList[m];
                    if (mm.pieceType == 6) tMoves.Add(pMoveList[m]);
                    else if (ULONG_OPERATIONS.IsBitOne(tCheckingPieceLine, pMoveList[m].endPos) || mm.isEnPassant) tMoves.Add(pMoveList[m]);
                }
                pMoveList = tMoves;
            }
            else kingMovement.AddMoveOptionsToMoveList(blackKingSquare, oppAttkBitboard | blackPieceBitboard, ~oppAttkBitboard & whitePieceBitboard);
        }

        public int MinimaxRoot(int pDepth)
        {
            int baseLineLen = curSearchZobristKeyLine.Length;
            ulong[] completeZobristHistory = new ulong[baseLineLen + pDepth];
            for (int i = 0; i < baseLineLen; i++) completeZobristHistory[i] = curSearchZobristKeyLine[i];
            curSearchZobristKeyLine = completeZobristHistory;

            int perftScore = 0, tattk;

            if (isWhiteToMove)
            {
                tattk = PreMinimaxCheckCheckWhite();
                Console.WriteLine(tattk);
                if (tattk == -1) perftScore = MinimaxWhite(pDepth, baseLineLen, whiteKingSquare, whiteKingSquare, 2);
                else perftScore = MinimaxWhite(pDepth, baseLineLen, tattk, tattk, pieceTypeArray[tattk]);
            }
            else
            {
                tattk = PreMinimaxCheckCheckBlack();
                Console.WriteLine(tattk);
                if (tattk == -1) perftScore = MinimaxBlack(pDepth, baseLineLen, whiteKingSquare, whiteKingSquare, 2);
                else perftScore = MinimaxBlack(pDepth, baseLineLen, tattk, tattk, pieceTypeArray[tattk]);
            }

            return perftScore;
        }

        private int MinimaxWhite(int pDepth, int pRepetitionHistoryPly, int pLastMoveStartPos, int pLastMoveEndPos, int pLastMovePieceType)
        {
            if (pDepth == 0) return 1;

            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(allPieceBitboard));
            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(whitePieceBitboard));

            List<Move> moveOptionList = new List<Move>();
            GetLegalWhiteMoves(LeafCheckingPieceCheckWhite(pLastMoveStartPos, pLastMoveEndPos, pLastMovePieceType), ref moveOptionList);
            int molc = moveOptionList.Count, tWhiteKingSquare = whiteKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;

            enPassantSquare = 65;
            happenedHalfMoves++;
            isWhiteToMove = true;

            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(tBPB));
            //Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(tAPB));

            int tC = 0;

            for (int m = 0; m < molc; m++)
            {
                Move curMove = moveOptionList[m];
                int tPieceType = curMove.pieceType, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPTI = pieceTypeArray[tEndPos];

                #region MakeMove()

                fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
                zobristKey = tZobristKey ^ pieceHashesWhite[tStartPos, tPieceType] ^ pieceHashesWhite[tEndPos, tPieceType];

                if (tPieceType == 1) fiftyMoveRuleCounter = 0;

                if (tPieceType == 6) // König Moves
                {
                    if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                    if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                    whiteCastleRightQueenSide = whiteCastleRightKingSide = false;

                    if (curMove.isStandard)
                    {
                        whitePieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tWPB, tStartPos), whiteKingSquare = tEndPos);
                        blackPieceBitboard = tBPB;
                    }
                    else if (curMove.isCapture)
                    {
                        whitePieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tWPB, tStartPos), whiteKingSquare = tEndPos);
                        blackPieceBitboard = ULONG_OPERATIONS.SetBitToZero(tBPB, tEndPos);
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];

                        if (blackCastleRightQueenSide && tEndPos == 56)
                        {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        }
                        else if (blackCastleRightKingSide && tEndPos == 63)
                        {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        fiftyMoveRuleCounter = 0;
                    }
                    else // Rochade
                    {
                        //Console.WriteLine(curMove);
                        whitePieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tWPB, curMove.rochadeStartPos), curMove.rochadeEndPos), tStartPos), whiteKingSquare = tEndPos);
                        blackPieceBitboard = tBPB;
                        pieceTypeArray[curMove.rochadeEndPos] = 4;
                        pieceTypeArray[curMove.rochadeStartPos] = 0;
                        zobristKey ^= pieceHashesWhite[curMove.rochadeStartPos, 4] ^ pieceHashesWhite[curMove.rochadeEndPos, 4];
                    }
                }
                else if (curMove.isStandard) 
                {
                    whitePieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tWPB, tStartPos), tEndPos);
                    blackPieceBitboard = tBPB;
                    zobristKey ^= enPassantSquareHashes[enPassantSquare = curMove.enPassantOption];

                    if (whiteCastleRightQueenSide && tStartPos == 0)
                    {
                        zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = false;
                    }
                    else if (whiteCastleRightKingSide && tStartPos == 7)
                    {
                        zobristKey ^= whiteKingSideRochadeRightHash;
                        whiteCastleRightKingSide = false;
                    }
                }
                else if (curMove.isPromotion)
                {
                    whitePieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tWPB, tStartPos), tEndPos);
                    blackPieceBitboard = ULONG_OPERATIONS.SetBitToZero(tBPB, tEndPos);
                    pieceTypeArray[tStartPos] = tPieceType = curMove.promotionType;
                    zobristKey ^= pieceHashesWhite[tEndPos, tPTI];

                    if (blackCastleRightQueenSide && tEndPos == 56)
                    {
                        zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightQueenSide = false;
                    }
                    else if (blackCastleRightKingSide && tEndPos == 63)
                    {
                        zobristKey ^= blackKingSideRochadeRightHash;
                        blackCastleRightKingSide = false;
                    }
                }
                else if (curMove.isEnPassant)
                {
                    whitePieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tWPB, tStartPos), tEndPos);
                    blackPieceBitboard = ULONG_OPERATIONS.SetBitToZero(tBPB, curMove.enPassantOption);
                    zobristKey ^= pieceHashesBlack[curMove.enPassantOption, 1];
                    pieceTypeArray[curMove.enPassantOption] = 0;
                }
                else // Captures
                {
                    whitePieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tWPB, tStartPos), tEndPos);
                    blackPieceBitboard = ULONG_OPERATIONS.SetBitToZero(tBPB, tEndPos);
                    zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                    fiftyMoveRuleCounter = 0;

                    if (whiteCastleRightQueenSide && tStartPos == 0)
                    {
                        zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = false;
                    }
                    else if (whiteCastleRightKingSide && tStartPos == 7)
                    {
                        zobristKey ^= whiteKingSideRochadeRightHash;
                        whiteCastleRightKingSide = false;
                    }

                    if (blackCastleRightQueenSide && tEndPos == 56)
                    {
                        zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightQueenSide = false;
                    }
                    else if (blackCastleRightKingSide && tEndPos == 63)
                    {
                        zobristKey ^= blackKingSideRochadeRightHash;
                        blackCastleRightKingSide = false;
                    }
                }

                pieceTypeArray[tEndPos] = pieceTypeArray[tStartPos];
                pieceTypeArray[tStartPos] = 0;

                allPieceBitboard = blackPieceBitboard | whitePieceBitboard;

                curSearchZobristKeyLine[pRepetitionHistoryPly] = zobristKey;

                #endregion
                    //Console.WriteLine(curMove);
                    //if (ULONG_OPERATIONS.IsBitOne(allPieceBitboard, 0) && pieceTypeArray[0] == 0)
                    //{
                    //    //Console.WriteLine(pLastMoveStartPos + " -> " + pLastMoveEndPos + " (" + pLastMovePieceType + ")");
                    //    //Console.WriteLine(curMove);
                    //    //Console.WriteLine(CreateFenString());
                    //}

                //Console.WriteLine(curMove);
                    //Console.WriteLine(CreateFenString());

                tMoveLine[pDepth] = curMove;
                //int t;
                tC += MinimaxBlack(pDepth - 1, pRepetitionHistoryPly + 1, tStartPos, tEndPos, tPieceType);
                tMoveLine[pDepth] = null;



                //if (pDepth == 2)
                //{
                //    Console.WriteLine(CreateFenString() + "\n" + t);
                //}

                #region UndoMove()

                whiteCastleRightKingSide = tWKSCR;
                whiteCastleRightQueenSide = tWQSCR;
                blackCastleRightKingSide = tBKSCR;
                blackCastleRightQueenSide = tBQSCR;
                whiteKingSquare = tWhiteKingSquare;
                pieceTypeArray[tStartPos] = pieceTypeArray[tEndPos]; //tPieceType
                pieceTypeArray[tEndPos] = tPTI;
                enPassantSquare = 65;
                if (curMove.isRochade)
                {
                    pieceTypeArray[curMove.rochadeStartPos] = 4;
                    pieceTypeArray[curMove.rochadeEndPos] = 0;
                }
                else if (curMove.isPromotion) pieceTypeArray[tStartPos] = 1;
                else if (curMove.isEnPassant) pieceTypeArray[curMove.enPassantOption] = 1;

                #endregion
            }

            isWhiteToMove = false;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            happenedHalfMoves--;
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;


            //if (pDepth == 1)
            //{
            //    Console.WriteLine(CreateFenString() + "\n" + tC);
            //}

            return tC;
        }

        private int MinimaxBlack(int pDepth, int pRepetitionHistoryPly, int pLastMoveStartPos, int pLastMoveEndPos, int pLastMovePieceType)
        {
            if (pDepth == 0) return 1;
            List<Move> moveOptionList = new List<Move>();
            int ttt;
            GetLegalBlackMoves(ttt = LeafCheckingPieceCheckBlack(pLastMoveStartPos, pLastMoveEndPos, pLastMovePieceType), ref moveOptionList);
            int molc = moveOptionList.Count, tBlackKingSquare = blackKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;

            enPassantSquare = 65;
            happenedHalfMoves++;
            isWhiteToMove = false;

            int tC = 0;

            for (int m = 0; m < molc; m++)
            {
                Move curMove = moveOptionList[m];
                //if (curMove == null)
                //{
                //    return 0;
                //    Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(tBPB));
                //    Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(whitePieceBitboard));
                //    Console.WriteLine(ttt);
                //    for (int m2 = 0; m2 < molc; m2++) Console.WriteLine(moveOptionList[m2]);
                //    //for (int i = 9; i >= 0; i--)
                //    //{
                //    //    Console.WriteLine(tMoveLine[i]);
                //    //}
                //    Console.WriteLine(CreateFenString());
                //    Console.WriteLine(pDepth);
                //    Console.WriteLine(pLastMoveStartPos + " -> " + pLastMoveEndPos + " (" + pLastMovePieceType + ")");
                //    return 0;
                //}
                int tPieceType = curMove.pieceType, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPTI = pieceTypeArray[tEndPos];

                #region MakeMove()

                fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
                zobristKey = tZobristKey ^ pieceHashesBlack[tStartPos, tPieceType] ^ pieceHashesBlack[tEndPos, tPieceType];

                if (tPieceType == 1) fiftyMoveRuleCounter = 0;

                if (tPieceType == 6) // König Moves
                {
                    if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                    if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                    blackCastleRightQueenSide = blackCastleRightKingSide = false;

                    if (curMove.isStandard)
                    {
                        blackPieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tBPB, tStartPos), blackKingSquare = tEndPos);
                        whitePieceBitboard = tWPB;
                    }
                    else if (curMove.isCapture)
                    {
                        blackPieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tBPB, tStartPos), blackKingSquare = tEndPos);
                        whitePieceBitboard = ULONG_OPERATIONS.SetBitToZero(tWPB, tEndPos);
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];

                        if (whiteCastleRightQueenSide && tEndPos == 0)
                        {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tEndPos == 7)
                        {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        fiftyMoveRuleCounter = 0;
                    }
                    else // Rochade
                    {
                        blackPieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tBPB, curMove.rochadeStartPos), curMove.rochadeEndPos), tStartPos), blackKingSquare = tEndPos);
                        whitePieceBitboard = tWPB;
                        pieceTypeArray[curMove.rochadeEndPos] = 4;
                        pieceTypeArray[curMove.rochadeStartPos] = 0;
                        zobristKey ^= pieceHashesBlack[curMove.rochadeStartPos, 4] ^ pieceHashesBlack[curMove.rochadeEndPos, 4];
                    }
                }
                else if (curMove.isStandard)
                {
                    blackPieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tBPB, tStartPos), tEndPos);
                    whitePieceBitboard = tWPB;
                    zobristKey ^= enPassantSquareHashes[enPassantSquare = curMove.enPassantOption];

                    if (blackCastleRightQueenSide && tStartPos == 56)
                    {
                        zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightQueenSide = false;
                    }
                    else if (blackCastleRightKingSide && tStartPos == 63)
                    {
                        zobristKey ^= blackKingSideRochadeRightHash;
                        blackCastleRightKingSide = false;
                    }
                }
                else if (curMove.isPromotion)
                {
                    blackPieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tBPB, tStartPos), tEndPos);
                    whitePieceBitboard = ULONG_OPERATIONS.SetBitToZero(tWPB, tEndPos);
                    pieceTypeArray[tStartPos] = tPieceType = curMove.promotionType;
                    zobristKey ^= pieceHashesWhite[tEndPos, tPTI];

                    if (whiteCastleRightQueenSide && tEndPos == 0)
                    {
                        zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = false;
                    }
                    else if (whiteCastleRightKingSide && tEndPos == 7)
                    {
                        zobristKey ^= whiteKingSideRochadeRightHash;
                        whiteCastleRightKingSide = false;
                    }
                }
                else if (curMove.isEnPassant)
                {
                    blackPieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tBPB, tStartPos), tEndPos);
                    whitePieceBitboard = ULONG_OPERATIONS.SetBitToZero(tWPB, curMove.enPassantOption);
                    zobristKey ^= pieceHashesWhite[curMove.enPassantOption, 1];
                    pieceTypeArray[curMove.enPassantOption] = 0;
                    fiftyMoveRuleCounter = 0;
                }
                else // Captures
                {
                    blackPieceBitboard = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(tBPB, tStartPos), tEndPos);
                    whitePieceBitboard = ULONG_OPERATIONS.SetBitToZero(tWPB, tEndPos);
                    zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                    fiftyMoveRuleCounter = 0;

                    if (whiteCastleRightQueenSide && tEndPos == 0)
                    {
                        zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = false;
                    }
                    else if (whiteCastleRightKingSide && tEndPos == 7)
                    {
                        zobristKey ^= whiteKingSideRochadeRightHash;
                        whiteCastleRightKingSide = false;
                    }

                    if (blackCastleRightQueenSide && tStartPos == 56)
                    {
                        zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightQueenSide = false;
                    }
                    else if (blackCastleRightKingSide && tStartPos == 63)
                    {
                        zobristKey ^= blackKingSideRochadeRightHash;
                        blackCastleRightKingSide = false;
                    }
                }

                allPieceBitboard = blackPieceBitboard | whitePieceBitboard;

                pieceTypeArray[tEndPos] = pieceTypeArray[tStartPos];
                pieceTypeArray[tStartPos] = 0;

                curSearchZobristKeyLine[pRepetitionHistoryPly] = zobristKey;

                //if (repetitionDictionary.ContainsKey(zobristKey)) repetitionDictionary[zobristKey] += 1;
                //else repetitionDictionary.Add(zobristKey, 1);

                #endregion


                //Console.WriteLine(curMove);
                //Console.WriteLine(CreateFenString());
                //int t = 0;
                //if (ULONG_OPERATIONS.IsBitOne(allPieceBitboard, 0) && pieceTypeArray[0] == 0) Console.WriteLine(CreateFenString());
                tMoveLine[pDepth] = curMove;
                tC += MinimaxWhite(pDepth - 1, pRepetitionHistoryPly + 1, tStartPos, tEndPos, tPieceType);
                tMoveLine[pDepth] = null;

                //if (pDepth == 2)
                //{
                //    Console.WriteLine(CreateFenString() + "\n" + t);
                //}
                //Console.WriteLine(t);

                #region UndoMove()

                whiteCastleRightKingSide = tWKSCR;
                whiteCastleRightQueenSide = tWQSCR;
                blackCastleRightKingSide = tBKSCR;
                blackCastleRightQueenSide = tBQSCR;
                blackKingSquare = tBlackKingSquare;
                pieceTypeArray[tStartPos] = pieceTypeArray[tEndPos];
                pieceTypeArray[tEndPos] = tPTI;
                enPassantSquare = 65;
                if (curMove.isRochade)
                {
                    pieceTypeArray[curMove.rochadeStartPos] = 4;
                    pieceTypeArray[curMove.rochadeEndPos] = 0;
                }
                else if (curMove.isPromotion) pieceTypeArray[tStartPos] = 1;
                else if (curMove.isEnPassant) pieceTypeArray[curMove.enPassantOption] = 1;

                #endregion
            }

            isWhiteToMove = true;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            happenedHalfMoves--;
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            return tC;
        }

        private bool IsDrawByRepetition(int pPlyOfFirstPossibleRepeatedPosition)
        {
            int tC = 0;
            for (int i = pPlyOfFirstPossibleRepeatedPosition; i >= 0; i -= 2)
            {
                if (curSearchZobristKeyLine[i] == zobristKey)
                    if (++tC == 2) return true;
            }
            return false;
        }


        private ulong[,] pieceHashesWhite = new ulong[64, 7], pieceHashesBlack = new ulong[64, 7];
        private ulong blackTurnHash, whiteKingSideRochadeRightHash, whiteQueenSideRochadeRightHash, blackKingSideRochadeRightHash, blackQueenSideRochadeRightHash;
        private ulong[] enPassantSquareHashes = new ulong[66];
        private void InitZobrist()
        {
            Random rng = new Random(2344);
            for (int sq = 0; sq < 64; sq++)
            {
                enPassantSquareHashes[sq] = ULONG_OPERATIONS.GetRandomULONG(rng);
                for (int it = 1; it < 7; it++)
                {
                    pieceHashesWhite[sq, it] = ULONG_OPERATIONS.GetRandomULONG(rng);
                    pieceHashesBlack[sq, it] = ULONG_OPERATIONS.GetRandomULONG(rng);
                }
            }
            blackTurnHash = ULONG_OPERATIONS.GetRandomULONG(rng);
            whiteKingSideRochadeRightHash = ULONG_OPERATIONS.GetRandomULONG(rng);
            whiteQueenSideRochadeRightHash = ULONG_OPERATIONS.GetRandomULONG(rng);
            blackKingSideRochadeRightHash = ULONG_OPERATIONS.GetRandomULONG(rng);
            blackQueenSideRochadeRightHash = ULONG_OPERATIONS.GetRandomULONG(rng);
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

        public string CreateFenString()
        {
            string rFEN = "";
            for (int i = 7; i >= 0; i--)
            {
                int t = 0;
                for (int j = 0; j < 8; j++)
                {
                    int sq = i * 8 + j;
                    if (pieceTypeArray[sq] != 0)
                    {
                        if (t != 0) rFEN += t;
                        t = 0;
                    }
                    if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, sq))
                    {
                        switch (pieceTypeArray[sq])
                        {
                            case 0: t++; Console.WriteLine("???"); break;
                            case 1: rFEN += 'P'; break;
                            case 2: rFEN += 'N'; break;
                            case 3: rFEN += 'B'; break;
                            case 4: rFEN += 'R'; break;
                            case 5: rFEN += 'Q'; break;
                            case 6: rFEN += 'K'; break;
                        }
                    }
                    else
                    {
                        switch (pieceTypeArray[sq])
                        {
                            case 0: t++; break;
                            case 1: rFEN += 'p'; break;
                            case 2: rFEN += 'n'; break;
                            case 3: rFEN += 'b'; break;
                            case 4: rFEN += 'r'; break;
                            case 5: rFEN += 'q'; break;
                            case 6: rFEN += 'k'; break;
                        }
                    }
                }
                if (t != 0) rFEN += t;
                if (i != 0) rFEN += "/";
            }

            rFEN += (isWhiteToMove ? " w" : " b");
            rFEN += (whiteCastleRightKingSide ? " K" : " ");
            rFEN += (whiteCastleRightQueenSide ? "Q" : "");
            rFEN += (blackCastleRightKingSide ? "k" : "");
            rFEN += (blackCastleRightQueenSide ? "q" : "");
            if (!whiteCastleRightKingSide && !whiteCastleRightQueenSide && !blackCastleRightKingSide && !blackCastleRightQueenSide) rFEN += "-";

            if (enPassantSquare < 64) rFEN += " " + squareNames[enPassantSquare] + " ";
            else rFEN += " - ";
            //= (epstr[0] - 'a') + 8 * (epstr[1] - '1');
            return rFEN + fiftyMoveRuleCounter + " " + ((happenedHalfMoves - happenedHalfMoves % 2) / 2);
        }

        private string[] squareNames = new string[64] {
            "a1", "b1", "c1", "d1", "e1", "f1", "g1", "h1",
            "a2", "b2", "c2", "d2", "e2", "f2", "g2", "h2",
            "a3", "b3", "c3", "d3", "e3", "f3", "g3", "h3",
            "a4", "b4", "c4", "d4", "e4", "f4", "g4", "h4",
            "a5", "b5", "c5", "d5", "e5", "f5", "g5", "h5",
            "a6", "b6", "c6", "d6", "e6", "f6", "g6", "h6",
            "a7", "b7", "c7", "d7", "e7", "f7", "g7", "h7",
            "a8", "b8", "c8", "d8", "e8", "f8", "g8", "h8"
        };

        public void LoadFenString(string fenStr)
        {
            zobristKey = 0ul;
            whitePieceBitboard = blackPieceBitboard = allPieceBitboard = 0ul;

            for (int i = 0; i < 64; i++) pieceTypeArray[i] = 0;

            string[] spaceSpl = fenStr.Split(' ');
            string[] rowSpl = spaceSpl[0].Split('/');

            string epstr = spaceSpl[3];
            if (epstr == "-") enPassantSquare = 65;
            else enPassantSquare = (epstr[0] - 'a') + 8 * (epstr[1] - '1');

            isWhiteToMove = true;
            if (spaceSpl[1] == "b") isWhiteToMove = false;

            if (!isWhiteToMove) zobristKey ^= blackTurnHash;

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
            happenedHalfMoves = Convert.ToInt32(spaceSpl[5]) * 2;
            if (!isWhiteToMove) happenedHalfMoves++;

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

                        pieceTypeArray[tSq] = Array.IndexOf(fenPieces, Char.ToLower(tChar));

                        if (tCol) zobristKey ^= pieceHashesBlack[tSq, pieceTypeArray[tSq]]; 
                        else zobristKey ^= pieceHashesWhite[tSq, pieceTypeArray[tSq]]; 

                        tSq++;
                    }
                }
            }

            if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
            if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
            if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
            if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;

            zobristKey ^= enPassantSquareHashes[enPassantSquare];

            allPieceBitboard = whitePieceBitboard | blackPieceBitboard;
        }

        #region | PRECALCULATIONS |

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
                    //if (square == 62 && square2 == 55)
                    //{
                    //    Console.WriteLine(tRay);
                    //    //Console.WriteLine(Console.WriteLine());
                    //}
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
                            //if (curAllPieceBitboard == 36028797018963968) Console.WriteLine(square + " -> " + square2);
                            rayCollidingSquareCalculations[square].Add(curAllPieceBitboard, solution);
                        } while (--combI != 0);
                    }
                    squareConnectivesPrecalculationArray[square << 6 | square2] = t;
                    squareConnectivesPrecalculationRayArray[square << 6 | square2] = tRay;
                    squareConnectivesCrossDirsPrecalculationArray[square << 6 | square2] = t2;
                    squareConnectivesPrecalculationLineArray[square << 6 | square2] = ULONG_OPERATIONS.SetBitToOne(tExclRay, square2);
                }
                differentRays.Clear();
            }
        }

        #endregion
    }

    public class Piece // Theoretisch ist dies Legacy; aber ich lasse es erstmals noch hier
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
        public bool isStandard { get; private set; } = false;

        public Move(int pSP, int pEP, int pPT) //Standard Move
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            isStandard = true;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
        }
        public Move(int pSP, int pEP, int pRSP, int pREP) // Rochade
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = 6;
            isSliderMove = false;
            isRochade = true;
            rochadeStartPos = pRSP;
            rochadeEndPos = pREP;
        }
        public Move(int pSP, int pEP, int pPT, bool pIC) // Capture
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            isCapture = pIC;
            if (!isCapture) isStandard = true;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
        }
        public Move(int pSP, int pEP, int pPT, bool pIC, int enPassPar) // Bauer von Base Line zwei nach vorne
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            isCapture = pIC;
            isStandard = !pIC;
            enPassantOption = enPassPar;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
        }
        public Move(bool b, int pSP, int pEP, int pEPS) // En Passant (bool param einfach nur damit ich noch einen Konstruktur haben kann)
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = 1;
            isCapture = isEnPassant = true;
            enPassantOption = pEPS;
            isSliderMove = false;
        }
        public Move(int pSP, int pEP, int pPT, int promType, bool pIC) // Promotion
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

/*
 * r4rk1/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b K - 2 2
84444
2kr3r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b K - 2 2
87068
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q2/PPPBBPpP/1R2K2R b Kkq - 0 2
94174
r3k2r/p1ppqpb1/bn2pnp1/3PN3/4P3/1pN2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
98333
r3k2r/p1ppqpb1/bn2pnp1/3PN3/4P3/2p2Q1p/PPPBBPPP/1R2K2R b Kkq - 0 2
95037
r3k2r/pbppqpb1/1n2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
92224
r1b1k2r/p1ppqpb1/1n2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
79352
r3k2r/p1ppqpb1/1n2pnp1/1b1PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
91311
r3k2r/p1ppqpb1/1n2pnp1/3PN3/1pb1P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
87817
r3k2r/p1ppqpb1/1n2pnp1/3PN3/1p2P3/2Nb1Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
86863
r3k2r/p1ppqpb1/1n2pnp1/3PN3/1p2P3/2N2Q1p/PPPBbPPP/1R2K2R b Kkq - 0 2
74818
r1n1k2r/p1ppqpb1/b3pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
78031
r3k2r/p1ppqpb1/b3pnp1/3PN3/np2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
86795
r3k2r/p1ppqpb1/b3pnp1/3PN3/1pn1P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
85466
r3k2r/p1ppqpb1/b3pnp1/3nN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 0 2
85713
r3k2r/p1ppqpb1/bn3np1/3pN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 0 2
92922
r3k2r/p1ppqpbn/bn2p1p1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
91837
r3k1nr/p1ppqpb1/bn2p1p1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
91970
r3k2r/p1ppqpb1/bn2p1p1/3PN3/1p2P1n1/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
97660
r3k2r/p1ppqpb1/bn2p1p1/3PN2n/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
95779
r3k2r/p1ppqpb1/bn2p1p1/3PN3/1p2n3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 0 2
119483
r3k2r/p1ppqpb1/bn2p1p1/3nN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 0 2
97648
r3k2r/p1ppqpb1/bn2pn2/3PN1p1/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
86635
r3k2r/p2pqpb1/bnp1pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
94025
r3k2r/p2pqpb1/bn2pnp1/2pPN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq c6 2 2
87981
r3k2r/p1p1qpb1/bn1ppnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
87007
r3kq1r/p1pp1pb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
83974
r3k2r/p1pp1pb1/bn1qpnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
92015
r3k2r/p1pp1pb1/bn2pnp1/2qPN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
104208
r2qk2r/p1pp1pb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
84275
r3k2r/p1ppqp2/bn2pnpb/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
90002
r3kb1r/p1ppqp2/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kkq - 2 2
82235
1r2k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kk - 2 2
92948
2r1k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kk - 2 2
86531
3rk2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kk - 2 2
86620
r4k1r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b K - 2 2
83189
r2k3r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b K - 2 2
84920
r3k3/p1ppqpbr/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kq - 2 2
84232
r3k3/p1ppqpb1/bn2pnpr/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kq - 2 2
84150
r3k3/p1ppqpb1/bn2pnp1/3PN2r/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kq - 2 2
90283
r3k3/p1ppqpb1/bn2pnp1/3PN3/1p2P2r/2N2Q1p/PPPBBPPP/1R2K2R b Kq - 2 2
91599
r3k1r1/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kq - 2 2
80075
r3kr2/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/1R2K2R b Kq - 2 2
75881
 */



/*
 * r3k2r/p1ppqpb1/bn2pnpB/3PN3/1p2P3/2N2Q2/PPP1BPpP/1R2K2R w Kkq - 1 2
2015
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q2/PPP1BPpP/1RB1K2R w Kkq - 1 2
2065
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q2/PPPB1PpP/1R1BK2R w Kkq - 1 2
1870
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q2/PPPB1PpP/1R2KB1R w Kkq - 1 2
2490
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NB1Q2/PPPB1PpP/1R2K2R w Kkq - 1 2
2122
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1pB1P3/2N2Q2/PPPB1PpP/1R2K2R w Kkq - 1 2
2168
r3k2r/p1ppqpb1/bn2pnp1/1B1PN3/1p2P3/2N2Q2/PPPB1PpP/1R2K2R w Kkq - 1 2
2161
r3k2r/p1ppqpb1/Bn2pnp1/3PN3/1p2P3/2N2Q2/PPPB1PpP/1R2K2R w Kkq - 0 2
2021
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1P/PPPBBPp1/1R2K2R w Kkq - 1 2
2026
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P2P/2N2Q2/PPPBBPp1/1R2K2R w Kkq h3 1 2
2071
r3k2r/p1ppqpb1/bn2pnp1/3PN3/Np2P3/5Q2/PPPBBPpP/1R2K2R w Kkq - 1 2
2339
r3k2r/p1ppqpb1/bn2pnp1/1N1PN3/1p2P3/5Q2/PPPBBPpP/1R2K2R w Kkq - 1 2
2290
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/5Q2/PPPBBPpP/1R1NK2R w Kkq - 1 2
2202
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P1Q1/2N5/PPPBBPpP/1R2K2R w Kkq - 1 2
2294
r3k2r/p1ppqpb1/bn2pnp1/3PN2Q/1p2P3/2N5/PPPBBPpP/1R2K2R w Kkq - 1 2
2213
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N5/PPPBBPQP/1R2K2R w Kkq - 0 2
2177
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2PQ2/2N5/PPPBBPpP/1R2K2R w Kkq - 1 2
2153
r3k2r/p1ppqpb1/bn2pnp1/3PNQ2/1p2P3/2N5/PPPBBPpP/1R2K2R w Kkq - 1 2
2426
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N3Q1/PPPBBPpP/1R2K2R w Kkq - 1 2
2393
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N4Q/PPPBBPpP/1R2K2R w Kkq - 1 2
2366
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N1Q3/PPPBBPpP/1R2K2R w Kkq - 1 2
2199
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R w Kkq - 1 2
2017
r3k2r/p1ppqpb1/bn2pQp1/3PN3/1p2P3/2N5/PPPBBPpP/1R2K2R w Kkq - 0 2
2175
r3k2r/p1ppqpb1/bn1Ppnp1/4N3/1p2P3/2N2Q2/PPPBBPpP/1R2K2R w Kkq - 1 2
2065
r3k2r/p1ppqpb1/bn2Pnp1/4N3/1p2P3/2N2Q2/PPPBBPpP/1R2K2R w Kkq - 0 2
2295
r3k2r/p1ppqpb1/bnN1pnp1/3P4/1p2P3/2N2Q2/PPPBBPpP/1R2K2R w Kkq - 1 2
2106
r3k2r/p1ppqpb1/bn2pnp1/3P4/1p2P3/2NN1Q2/PPPBBPpP/1R2K2R w Kkq - 1 2
1831
r3k2r/p1ppqpb1/bn2pnp1/3P4/1pN1P3/2N2Q2/PPPBBPpP/1R2K2R w Kkq - 1 2
1927
r3k2r/p1ppqpb1/bn2pnp1/3P4/1p2P1N1/2N2Q2/PPPBBPpP/1R2K2R w Kkq - 1 2
1958
r3k2r/p1ppqpb1/bn2pnN1/3P4/1p2P3/2N2Q2/PPPBBPpP/1R2K2R w Kkq - 0 2
2062
r3k2r/p1pNqpb1/bn2pnp1/3P4/1p2P3/2N2Q2/PPPBBPpP/1R2K2R w Kkq - 0 2
2177
r3k2r/p1ppqNb1/bn2pnp1/3P4/1p2P3/2N2Q2/PPPBBPpP/1R2K2R w Kkq - 0 2
2137
 * 
 */



/*
 * 
 * r4rk1/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b K - 2 3
43
2kr3r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b K - 2 3
43
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K1nR b Kkq - 2 3
43
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K1bR b Kkq - 2 3
43
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K1rR b Kkq - 2 3
2
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K1qR b Kkq - 2 3
2
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K2r b Kkq - 2 3
43
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K2r b Kkq - 2 3
43
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K2r b Kkq - 2 3
1
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K2q b Kkq - 2 3
1
r3k2r/p1ppqpb1/bn2pnp1/3PN3/4P3/1pNQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
44
r3k2r/p1ppqpb1/bn2pnp1/3PN3/4P3/2pQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
44
r3k2r/pbppqpb1/1n2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
r1b1k2r/p1ppqpb1/1n2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
r3k2r/p1ppqpb1/1n2pnp1/1b1PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
42
r3k2r/p1ppqpb1/1n2pnp1/3PN3/1pb1P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
41
r3k2r/p1ppqpb1/1n2pnp1/3PN3/1p2P3/2Nb4/PPPBBPpP/1R2K2R b Kkq - 0 3
38
r1n1k2r/p1ppqpb1/b3pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
r3k2r/p1ppqpb1/b3pnp1/3PN3/np2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
42
r3k2r/p1ppqpb1/b3pnp1/3PN3/1pn1P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
41
r3k2r/p1ppqpb1/b3pnp1/3nN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
44
r3k2r/p1ppqpb1/bn3np1/3pN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
44
r3k2r/p1ppqpbn/bn2p1p1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
r3k1nr/p1ppqpb1/bn2p1p1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
r3k2r/p1ppqpb1/bn2p1p1/3PN3/1p2P1n1/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
42
r3k2r/p1ppqpb1/bn2p1p1/3PN2n/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
r3k2r/p1ppqpb1/bn2p1p1/3PN3/1p2n3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
45
r3k2r/p1ppqpb1/bn2p1p1/3nN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
44
r3k2r/p1ppqpb1/bn2pn2/3PN1p1/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
42
r3k2r/p2pqpb1/bnp1pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
44
r3k2r/p2pqpb1/bn2pnp1/2pPN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq c6 2 3
44
r3k2r/p1p1qpb1/bn1ppnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
42
r3kq1r/p1pp1pb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
r3k2r/p1pp1pb1/bn1qpnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
42
r3k2r/p1pp1pb1/bn2pnp1/2qPN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
r2qk2r/p1pp1pb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
r3k2r/p1ppqp2/bn2pnpb/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
r3kb1r/p1ppqp2/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
1r2k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kk - 2 3
43
2r1k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kk - 2 3
43
3rk2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kk - 2 3
43
r4k1r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b K - 2 3
43
r2k3r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b K - 2 3
43
r3k3/p1ppqpbr/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
43
r3k3/p1ppqpb1/bn2pnpr/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
43
r3k3/p1ppqpb1/bn2pnp1/3PN2r/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
43
r3k3/p1ppqpb1/bn2pnp1/3PN3/1p2P2r/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
42
r3k3/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ3r/PPPBBPpP/1R2K2R b Kq - 2 3
41
r3k1r1/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
43
r3kr2/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
43
r3k3/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpr/1R2K2R b Kq - 0 3
42
 */


/*
 * 
 * 
 * [König] 60 -> 62 /ROCHADE/
r4rk1/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b K - 2 3
[König] 60 -> 58 /ROCHADE/
2kr3r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b K - 2 3
[Bauer] 14 -> 6 /Springer-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K1nR b Kkq - 2 3
[Bauer] 14 -> 6 /Läufer-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K1bR b Kkq - 2 3
[Bauer] 14 -> 6 /Turm-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K1rR b Kkq - 2 3
[Bauer] 14 -> 6 /Dame-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K1qR b Kkq - 2 3
[Bauer] 14 -> 7 /CAPTURE/ /Springer-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K2n b Kkq - 2 3
[Bauer] 14 -> 7 /CAPTURE/ /Läufer-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K2b b Kkq - 2 3
[Bauer] 14 -> 7 /CAPTURE/ /Turm-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K2r b Kkq - 2 3
[Bauer] 14 -> 7 /CAPTURE/ /Dame-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K2q b Kkq - 2 3
[Bauer] 25 -> 17
r3k2r/p1ppqpb1/bn2pnp1/3PN3/4P3/1pNQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Bauer] 25 -> 18 /CAPTURE/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/4P3/2pQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
[Läufer] 40 -> 49
r3k2r/pbppqpb1/1n2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Läufer] 40 -> 58
r1b1k2r/p1ppqpb1/1n2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Läufer] 40 -> 33
r3k2r/p1ppqpb1/1n2pnp1/1b1PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Läufer] 40 -> 26
r3k2r/p1ppqpb1/1n2pnp1/3PN3/1pb1P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Läufer] 40 -> 19 /CAPTURE/
r3k2r/p1ppqpb1/1n2pnp1/3PN3/1p2P3/2Nb4/PPPBBPpP/1R2K2R b Kkq - 0 3
[Springer] 41 -> 58
r1n1k2r/p1ppqpb1/b3pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Springer] 41 -> 24
r3k2r/p1ppqpb1/b3pnp1/3PN3/np2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Springer] 41 -> 26
r3k2r/p1ppqpb1/b3pnp1/3PN3/1pn1P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Springer] 41 -> 35 /CAPTURE/
r3k2r/p1ppqpb1/b3pnp1/3nN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
[Bauer] 44 -> 35 /CAPTURE/
r3k2r/p1ppqpb1/bn3np1/3pN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
[Springer] 45 -> 55
r3k2r/p1ppqpbn/bn2p1p1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Springer] 45 -> 62
r3k1nr/p1ppqpb1/bn2p1p1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Springer] 45 -> 30
r3k2r/p1ppqpb1/bn2p1p1/3PN3/1p2P1n1/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Springer] 45 -> 39
r3k2r/p1ppqpb1/bn2p1p1/3PN2n/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Springer] 45 -> 28 /CAPTURE/
r3k2r/p1ppqpb1/bn2p1p1/3PN3/1p2n3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
[Springer] 45 -> 35 /CAPTURE/
r3k2r/p1ppqpb1/bn2p1p1/3nN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
[Bauer] 46 -> 38
r3k2r/p1ppqpb1/bn2pn2/3PN1p1/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Bauer] 50 -> 42
r3k2r/p2pqpb1/bnp1pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Bauer] 50 -> 34 [EP = 42]
r3k2r/p2pqpb1/bn2pnp1/2pPN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq c6 2 3
[Bauer] 51 -> 43
r3k2r/p1p1qpb1/bn1ppnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Dame] 52 -> 61
r3kq1r/p1pp1pb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Dame] 52 -> 43
r3k2r/p1pp1pb1/bn1qpnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Dame] 52 -> 34
r3k2r/p1pp1pb1/bn2pnp1/2qPN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Dame] 52 -> 59
r2qk2r/p1pp1pb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Läufer] 54 -> 47
r3k2r/p1ppqp2/bn2pnpb/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Läufer] 54 -> 61
r3kb1r/p1ppqp2/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
[Turm] 56 -> 57
1r2k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kk - 2 3
[Turm] 56 -> 58
2r1k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kk - 2 3
[Turm] 56 -> 59
3rk2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kk - 2 3
[König] 60 -> 61
r4k1r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b K - 2 3
[König] 60 -> 59
r2k3r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b K - 2 3
[Turm] 63 -> 55
r3k3/p1ppqpbr/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
[Turm] 63 -> 47
r3k3/p1ppqpb1/bn2pnpr/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
[Turm] 63 -> 39
r3k3/p1ppqpb1/bn2pnp1/3PN2r/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
[Turm] 63 -> 31
r3k3/p1ppqpb1/bn2pnp1/3PN3/1p2P2r/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
[Turm] 63 -> 23
r3k3/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ3r/PPPBBPpP/1R2K2R b Kq - 2 3
[Turm] 63 -> 62
r3k1r1/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
[Turm] 63 -> 61
r3kr2/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
[Turm] 63 -> 15 /CAPTURE/
r3k3/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpr/1R2K2R b Kq - 0 3
 * 
 */


/*
 * 
 * [König] 60 -> 62 /ROCHADE/
r4rk1/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b K - 2 3
43
[König] 60 -> 58 /ROCHADE/
2kr3r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b K - 2 3
43
[Bauer] 14 -> 6 /Springer-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K1nR b Kkq - 2 3
43
[Bauer] 14 -> 6 /Läufer-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K1bR b Kkq - 2 3
43
[Bauer] 14 -> 6 /Turm-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K1rR b Kkq - 2 3
2
[Bauer] 14 -> 6 /Dame-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K1qR b Kkq - 2 3
2
[Bauer] 14 -> 7 /CAPTURE/ /Springer-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K2n b Kkq - 2 3
43
[Bauer] 14 -> 7 /CAPTURE/ /Läufer-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K2b b Kkq - 2 3
43
[Bauer] 14 -> 7 /CAPTURE/ /Turm-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K2r b Kkq - 2 3
1
[Bauer] 14 -> 7 /CAPTURE/ /Dame-PROMOTION/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBP1P/1R2K2q b Kkq - 2 3
1
[Bauer] 25 -> 17
r3k2r/p1ppqpb1/bn2pnp1/3PN3/4P3/1pNQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
44
[Bauer] 25 -> 18 /CAPTURE/
r3k2r/p1ppqpb1/bn2pnp1/3PN3/4P3/2pQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
44
[Läufer] 40 -> 49
r3k2r/pbppqpb1/1n2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
[Läufer] 40 -> 58
r1b1k2r/p1ppqpb1/1n2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
[Läufer] 40 -> 33
r3k2r/p1ppqpb1/1n2pnp1/1b1PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
42
[Läufer] 40 -> 26
r3k2r/p1ppqpb1/1n2pnp1/3PN3/1pb1P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
41
[Läufer] 40 -> 19 /CAPTURE/
r3k2r/p1ppqpb1/1n2pnp1/3PN3/1p2P3/2Nb4/PPPBBPpP/1R2K2R b Kkq - 0 3
38
[Springer] 41 -> 58
r1n1k2r/p1ppqpb1/b3pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
[Springer] 41 -> 24
r3k2r/p1ppqpb1/b3pnp1/3PN3/np2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
42
[Springer] 41 -> 26
r3k2r/p1ppqpb1/b3pnp1/3PN3/1pn1P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
41
[Springer] 41 -> 35 /CAPTURE/
r3k2r/p1ppqpb1/b3pnp1/3nN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
44
[Bauer] 44 -> 35 /CAPTURE/
r3k2r/p1ppqpb1/bn3np1/3pN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
44
[Springer] 45 -> 55
r3k2r/p1ppqpbn/bn2p1p1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
[Springer] 45 -> 62
r3k1nr/p1ppqpb1/bn2p1p1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
[Springer] 45 -> 30
r3k2r/p1ppqpb1/bn2p1p1/3PN3/1p2P1n1/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
42
[Springer] 45 -> 39
r3k2r/p1ppqpb1/bn2p1p1/3PN2n/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
[Springer] 45 -> 28 /CAPTURE/
r3k2r/p1ppqpb1/bn2p1p1/3PN3/1p2n3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
45
[Springer] 45 -> 35 /CAPTURE/
r3k2r/p1ppqpb1/bn2p1p1/3nN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 0 3
44
[Bauer] 46 -> 38
r3k2r/p1ppqpb1/bn2pn2/3PN1p1/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
42
[Bauer] 50 -> 42
r3k2r/p2pqpb1/bnp1pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
44
[Bauer] 50 -> 34 [EP = 42]
r3k2r/p2pqpb1/bn2pnp1/2pPN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq c6 2 3
44
[Bauer] 51 -> 43
r3k2r/p1p1qpb1/bn1ppnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
42
[Dame] 52 -> 61
r3kq1r/p1pp1pb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
[Dame] 52 -> 43
r3k2r/p1pp1pb1/bn1qpnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
42
[Dame] 52 -> 34
r3k2r/p1pp1pb1/bn2pnp1/2qPN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
[Dame] 52 -> 59
r2qk2r/p1pp1pb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
[Läufer] 54 -> 47
r3k2r/p1ppqp2/bn2pnpb/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
[Läufer] 54 -> 61
r3kb1r/p1ppqp2/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kkq - 2 3
43
[Turm] 56 -> 57
1r2k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kk - 2 3
43
[Turm] 56 -> 58
2r1k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kk - 2 3
43
[Turm] 56 -> 59
3rk2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kk - 2 3
43
[König] 60 -> 61
r4k1r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b K - 2 3
43
[König] 60 -> 59
r2k3r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b K - 2 3
43
[Turm] 63 -> 55
r3k3/p1ppqpbr/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
43
[Turm] 63 -> 47
r3k3/p1ppqpb1/bn2pnpr/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
43
[Turm] 63 -> 39
r3k3/p1ppqpb1/bn2pnp1/3PN2r/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
43
[Turm] 63 -> 31
r3k3/p1ppqpb1/bn2pnp1/3PN3/1p2P2r/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
42
[Turm] 63 -> 23
r3k3/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ3r/PPPBBPpP/1R2K2R b Kq - 2 3
41
[Turm] 63 -> 62
r3k1r1/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
43
[Turm] 63 -> 61
r3kr2/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpP/1R2K2R b Kq - 2 3
43
[Turm] 63 -> 15 /CAPTURE/
r3k3/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2NQ4/PPPBBPpr/1R2K2R b Kq - 0 3
42
 * 
 */



/*
 * 
 * 8/2p5/3p4/KP5r/4Rpk1/4P3/6P1/8 w - - 0 2
281
8/2p5/3p4/KP5r/4Rpk1/6P1/4P3/8 w - - 0 2
300
8/2p5/3p4/KP2R2r/5pk1/8/4P1P1/8 w - - 3 2
270
8/2p5/3pR3/KP5r/5pk1/8/4P1P1/8 w - - 3 2
321
8/2p1R3/3p4/KP5r/5pk1/8/4P1P1/8 w - - 3 2
342
4R3/2p5/3p4/KP5r/5pk1/8/4P1P1/8 w - - 3 2
378
8/2p5/3p4/KP5r/5pk1/4R3/4P1P1/8 w - - 3 2
324
8/2p5/3p4/KP5r/3R1pk1/8/4P1P1/8 w - - 3 2
320
8/2p5/3p4/KP5r/2R2pk1/8/4P1P1/8 w - - 3 2
336
8/2p5/3p4/KP5r/1R3pk1/8/4P1P1/8 w - - 3 2
267
8/2p5/3p4/KP5r/R4pk1/8/4P1P1/8 w - - 3 2
264
8/2p5/3p4/KP5r/5Rk1/8/4P1P1/8 w - - 0 2
48
8/2p5/K2p4/1P5r/4Rpk1/8/4P1P1/8 w - - 3 2
322
8/2p5/3p4/1P5r/1K2Rpk1/8/4P1P1/8 w - - 3 2
313
8/2p5/3p4/1P5r/K3Rpk1/8/4P1P1/8 w - - 3 2
316
 * 
 */


/*
 * 
 * 
 * 8/2p5/3p4/1P5r/1K2Rp1k/8/4P1P1/8 b - - 4 3
18
8/2p5/3p4/1P3k1r/1K2Rp2/8/4P1P1/8 b - - 4 3
18
8/2p5/3p4/1P4kr/1K2Rp2/8/4P1P1/8 b - - 4 3
18
8/2p5/3p4/1P5r/1K2Rp2/6k1/4P1P1/8 b - - 4 3
16
8/2p5/3p3r/1P6/1K2Rpk1/8/4P1P1/8 b - - 4 3
17
8/2p4r/3p4/1P6/1K2Rpk1/8/4P1P1/8 b - - 4 3
17
7r/2p5/3p4/1P6/1K2Rpk1/8/4P1P1/8 b - - 4 3
17
8/2p5/3p4/1P6/1K2Rpkr/8/4P1P1/8 b - - 4 3
17
8/2p5/3p4/1P6/1K2Rpk1/7r/4P1P1/8 b - - 4 3
15
8/2p5/3p4/1P6/1K2Rpk1/8/4P1Pr/8 b - - 4 3
17
8/2p5/3p4/1P6/1K2Rpk1/8/4P1P1/7r b - - 4 3
17
8/2p5/3p4/1P4r1/1K2Rpk1/8/4P1P1/8 b - - 4 3
17
8/2p5/3p4/1P3r2/1K2Rpk1/8/4P1P1/8 b - - 4 3
17
8/2p5/3p4/1P2r3/1K2Rpk1/8/4P1P1/8 b - - 4 3
14
8/2p5/3p4/1P1r4/1K2Rpk1/8/4P1P1/8 b - - 4 3
17
8/2p5/3p4/1Pr5/1K2Rpk1/8/4P1P1/8 b - - 4 3
15
8/2p5/3p4/1r6/1K2Rpk1/8/4P1P1/8 b - - 0 3
5
8/2p5/8/1P1p3r/1K2Rpk1/8/4P1P1/8 b - - 0 3
17
8/8/2pp4/1P5r/1K2Rpk1/8/4P1P1/8 b - - 0 3
18
8/8/3p4/1Pp4r/1K2Rpk1/8/4P1P1/8 b - c6 0 3
6
 * 
 */