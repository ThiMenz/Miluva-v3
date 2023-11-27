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
            _ = new BoardManager("4k2r/1P4P1/1N6/3Pp3/1BR5/8/3P4/4K1R1 w k e6 0 1");
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

        public BoardManager(string fen)
        {
            #region | SETUP |

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
            PrecalculateEnPassantMoves();
            Console.WriteLine(" (" + setupStopwatch.ElapsedTicks / 10_000_000d + "s)");
            Console.WriteLine("[DONE]\n\n");

            LoadFenString(fen);
            setupStopwatch.Stop();

            #endregion

            Console.WriteLine(Evaluate(-1));
            Console.WriteLine(zobristKey);

            Stopwatch sw = Stopwatch.StartNew();
            int tPerft = 0;
            tPerft += MinimaxRoot(1);
            sw.Stop();

            Console.WriteLine(transpositionTable[zobristKey]);

            Console.WriteLine(GetThreeDigitSeperatedInteger(tPerft) + " Moves");
            Console.WriteLine(sw.ElapsedMilliseconds + "ms");
            Console.WriteLine(GetThreeDigitSeperatedInteger((int)((10_000_000d / (double)sw.ElapsedTicks) * tPerft)) + " NpS");
        }

        #region | CHECK RECOGNITION |

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

        private int LecacyLeafCheckingPieceCheckWhite(int pStartPos, int pEndPos, int pPieceType)
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

        private int LecacyLeafCheckingPieceCheckBlack(int pStartPos, int pEndPos, int pPieceType)
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

        #endregion

        #region | LEGAL MOVE GENERATION |

        private void GetLegalWhiteMoves(int pCheckingPieceSquare, ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppDiagonalSliderVision = 0ul, oppStraightSliderVision = 0ul, oppStaticPieceVision = 0ul, oppAttkBitboard, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if (((int)(blackPieceBitboard >> p) & 1) == 0) continue;
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
                    if (((int)(whitePieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 0)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(blackPieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(blackPieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 += 2;
                    if (((int)(whitePieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 7)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(blackPieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(blackPieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (((int)(whitePieceBitboard >> p) & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (((int)(pinnedPieces >> p) & 1) == 0) whitePawnMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, blackPieceBitboard);
                            else whitePawnMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, whitePieceBitboard, blackPieceBitboard);
                            break;
                        case 2:
                            if (((int)(pinnedPieces >> p) & 1) == 0) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, blackPieceBitboard);
                            break;
                        case 3:
                            if (((int)(pinnedPieces >> p) & 1) == 0) bishopMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (((int)(pinnedPieces >> p) & 1) == 0) rookMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (((int)(pinnedPieces >> p) & 1) == 0) queenMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
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
                if (enPassantSquare != 65 && (whitePieceBitboard & blackPawnAttackSquareBitboards[enPassantSquare]) != 0ul && (((int)(tCheckingPieceLine >> enPassantSquare) & 1) == 1 || (pCheckingPieceSquare + 8 == enPassantSquare && pieceTypeArray[pCheckingPieceSquare] == 1)))
                {
                    int shiftedKS = whiteKingSquare << 6, epM9 = enPassantSquare - 9, epM8 = epM9 + 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (((int)(whitePieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(blackPieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(blackPieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 += 2;
                    if (((int)(whitePieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(blackPieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(blackPieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (((int)(whitePieceBitboard >> p) & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (((int)(pinnedPieces >> p) & 1) == 0) whitePawnMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, blackPieceBitboard);
                            else whitePawnMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, whitePieceBitboard, blackPieceBitboard);
                            break;
                        case 2:
                            if (((int)(pinnedPieces >> p) & 1) == 0) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, blackPieceBitboard);
                            break;
                        case 3:
                            if (((int)(pinnedPieces >> p) & 1) == 0) bishopMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (((int)(pinnedPieces >> p) & 1) == 0) rookMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (((int)(pinnedPieces >> p) & 1) == 0) queenMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
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
                    else if (((int)(tCheckingPieceLine >> pMoveList[m].endPos) & 1) == 1 || mm.isEnPassant) tMoves.Add(pMoveList[m]);
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
                if (((int)(whitePieceBitboard >> p) & 1) == 0) continue;
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
                    if (((int)(blackPieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 7)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(whitePieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(whitePieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 -= 2;
                    if (((int)(blackPieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 0)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(whitePieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(whitePieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (((int)(blackPieceBitboard >> p) & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (((int)(pinnedPieces >> p) & 1) == 0) blackPawnMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, whitePieceBitboard);
                            else blackPawnMovement.AddMoveOptionsToMoveList(p, blackKingSquare, blackPieceBitboard, whitePieceBitboard);
                            break;
                        case 2:
                            if (((int)(pinnedPieces >> p) & 1) == 0) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, whitePieceBitboard);
                            break;
                        case 3:
                            if (((int)(pinnedPieces >> p) & 1) == 0) bishopMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (((int)(pinnedPieces >> p) & 1) == 0) rookMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (((int)(pinnedPieces >> p) & 1) == 0) queenMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
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
                if (enPassantSquare != 65 && (blackPieceBitboard & whitePawnAttackSquareBitboards[enPassantSquare]) != 0ul && (((int)(tCheckingPieceLine >> enPassantSquare) & 1) == 1 || (pCheckingPieceSquare - 8 == enPassantSquare && pieceTypeArray[pCheckingPieceSquare] == 1)))
                {
                    int shiftedKS = blackKingSquare << 6, epM9 = enPassantSquare + 9, epM8 = epM9 - 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if (((int)(blackPieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(whitePieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(whitePieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 -= 2;
                    if (((int)(blackPieceBitboard >> epM9) & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(((int)(whitePieceBitboard >> possibleAttacker1) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ((int)(whitePieceBitboard >> possibleAttacker2) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if (((int)(blackPieceBitboard >> p) & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if (((int)(pinnedPieces >> p) & 1) == 0) blackPawnMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, whitePieceBitboard);
                            else blackPawnMovement.AddMoveOptionsToMoveList(p, blackKingSquare, blackPieceBitboard, whitePieceBitboard);
                            break;
                        case 2:
                            if (((int)(pinnedPieces >> p) & 1) == 0) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, whitePieceBitboard);
                            break;
                        case 3:
                            if (((int)(pinnedPieces >> p) & 1) == 0) bishopMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if (((int)(pinnedPieces >> p) & 1) == 0) rookMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if (((int)(pinnedPieces >> p) & 1) == 0) queenMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
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
                    else if (((int)(tCheckingPieceLine >> pMoveList[m].endPos) & 1) == 1 || mm.isEnPassant) tMoves.Add(pMoveList[m]);
                }
                pMoveList = tMoves;
            }
            else kingMovement.AddMoveOptionsToMoveList(blackKingSquare, oppAttkBitboard | blackPieceBitboard, ~oppAttkBitboard & whitePieceBitboard);
        }

        private void GetLegalWhiteMoves2(int pCheckingPieceSquare, ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppDiagonalSliderVision = 0ul, oppStraightSliderVision = 0ul, oppStaticPieceVision = 0ul, oppAttkBitboard, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if ((blackPieceBitboard >> p & 1) == 0) continue;
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
                    if ((whitePieceBitboard >> epM9 & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 0)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!((blackPieceBitboard >> possibleAttacker1 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            (blackPieceBitboard >> possibleAttacker2 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 += 2;
                    if ((whitePieceBitboard >> epM9 & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 7)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!((blackPieceBitboard >> possibleAttacker1 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            (blackPieceBitboard >> possibleAttacker2 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if ((whitePieceBitboard >> p & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if ((pinnedPieces >> p & 1) == 0) whitePawnMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, blackPieceBitboard);
                            else whitePawnMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, whitePieceBitboard, blackPieceBitboard);
                            break;
                        case 2:
                            if ((pinnedPieces >> p & 1) == 0) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, blackPieceBitboard);
                            break;
                        case 3:
                            if ((pinnedPieces >> p & 1) == 0) bishopMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if ((pinnedPieces >> p & 1) == 0) rookMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if ((pinnedPieces >> p & 1) == 0) queenMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
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
                if (enPassantSquare != 65 && (whitePieceBitboard & blackPawnAttackSquareBitboards[enPassantSquare]) != 0ul && ((tCheckingPieceLine >> enPassantSquare & 1) == 1 || (pCheckingPieceSquare + 8 == enPassantSquare && pieceTypeArray[pCheckingPieceSquare] == 1)))
                {
                    int shiftedKS = whiteKingSquare << 6, epM9 = enPassantSquare - 9, epM8 = epM9 + 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if ((whitePieceBitboard >> epM9 & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!((blackPieceBitboard >> possibleAttacker1 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            (blackPieceBitboard >> possibleAttacker2 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 += 2;
                    if ((whitePieceBitboard >> epM9 & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!((blackPieceBitboard >> possibleAttacker1 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            (blackPieceBitboard >> possibleAttacker2 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if ((whitePieceBitboard >> p & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if ((pinnedPieces >> p & 1) == 0) whitePawnMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, blackPieceBitboard);
                            else whitePawnMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, whitePieceBitboard, blackPieceBitboard);
                            break;
                        case 2:
                            if ((pinnedPieces >> p & 1) == 0) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, blackPieceBitboard);
                            break;
                        case 3:
                            if ((pinnedPieces >> p & 1) == 0) bishopMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if ((pinnedPieces >> p & 1) == 0) rookMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, whiteKingSquare, blackPieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if ((pinnedPieces >> p & 1) == 0) queenMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, allPieceBitboard);
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
                    else if ((tCheckingPieceLine >> pMoveList[m].endPos & 1) == 1 || mm.isEnPassant) tMoves.Add(pMoveList[m]);
                }
                pMoveList = tMoves;
            }
            else kingMovement.AddMoveOptionsToMoveList(whiteKingSquare, oppAttkBitboard | whitePieceBitboard, ~oppAttkBitboard & blackPieceBitboard);
        }

        private void GetLegalBlackMoves2(int pCheckingPieceSquare, ref List<Move> pMoveList)
        {
            moveOptionList = pMoveList;
            ulong oppDiagonalSliderVision = 0ul, oppStraightSliderVision = 0ul, oppStaticPieceVision = 0ul, oppAttkBitboard, pinnedPieces = 0ul;
            for (int p = 0; p < 64; p++)
            {
                if ((whitePieceBitboard >> p & 1) == 0) continue;
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
                    if ((blackPieceBitboard >> epM9 & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 7)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!((whitePieceBitboard >> possibleAttacker1 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            (whitePieceBitboard >> possibleAttacker2 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 -= 2;
                    if ((blackPieceBitboard >> epM9 & 1) == 1 && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 0)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!((whitePieceBitboard >> possibleAttacker1 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            (whitePieceBitboard >> possibleAttacker2 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if ((blackPieceBitboard >> p & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if ((pinnedPieces >> p & 1) == 0) blackPawnMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, whitePieceBitboard);
                            else blackPawnMovement.AddMoveOptionsToMoveList(p, blackKingSquare, blackPieceBitboard, whitePieceBitboard);
                            break;
                        case 2:
                            if ((pinnedPieces >> p & 1) == 0) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, whitePieceBitboard);
                            break;
                        case 3:
                            if ((pinnedPieces >> p & 1) == 0) bishopMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if ((pinnedPieces >> p & 1) == 0) rookMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if ((pinnedPieces >> p & 1) == 0) queenMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
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
                if (enPassantSquare != 65 && (blackPieceBitboard & whitePawnAttackSquareBitboards[enPassantSquare]) != 0ul && ((tCheckingPieceLine >> enPassantSquare & 1) == 1 || (pCheckingPieceSquare - 8 == enPassantSquare && pieceTypeArray[pCheckingPieceSquare] == 1)))
                {
                    int shiftedKS = blackKingSquare << 6, epM9 = enPassantSquare + 9, epM8 = epM9 - 1;
                    ulong tu = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToZero(allPieceBitboard, epM8), enPassantSquare);
                    int possibleAttacker2 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM8] & tu];
                    if ((blackPieceBitboard >> epM9 & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!((whitePieceBitboard >> possibleAttacker1 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            (whitePieceBitboard >> possibleAttacker2 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 -= 2;
                    if ((blackPieceBitboard >> epM9 & 1) == 1 && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!((whitePieceBitboard >> possibleAttacker1 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            (whitePieceBitboard >> possibleAttacker2 & 1) == 1 && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                }
                for (int p = 0; p < 64; p++)
                {
                    if ((blackPieceBitboard >> p & 1) == 0) continue;
                    switch (pieceTypeArray[p])
                    {
                        case 1:
                            if ((pinnedPieces >> p & 1) == 0) blackPawnMovement.AddMoveOptionsToMoveList(p, blackPieceBitboard, whitePieceBitboard);
                            else blackPawnMovement.AddMoveOptionsToMoveList(p, blackKingSquare, blackPieceBitboard, whitePieceBitboard);
                            break;
                        case 2:
                            if ((pinnedPieces >> p & 1) == 0) knightMovement.AddMovesToMoveOptionList(p, allPieceBitboard, whitePieceBitboard);
                            break;
                        case 3:
                            if ((pinnedPieces >> p & 1) == 0) bishopMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else bishopMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 4:
                            if ((pinnedPieces >> p & 1) == 0) rookMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
                            else rookMovement.AddMoveOptionsToMoveList(p, blackKingSquare, whitePieceBitboard, allPieceBitboard);
                            break;
                        case 5:
                            if ((pinnedPieces >> p & 1) == 0) queenMovement.AddMoveOptionsToMoveList(p, whitePieceBitboard, allPieceBitboard);
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
                    else if ((tCheckingPieceLine >> pMoveList[m].endPos & 1) == 1 || mm.isEnPassant) tMoves.Add(pMoveList[m]);
                }
                pMoveList = tMoves;
            }
            else kingMovement.AddMoveOptionsToMoveList(blackKingSquare, oppAttkBitboard | blackPieceBitboard, ~oppAttkBitboard & whitePieceBitboard);
        }

        private void GetLegalWhiteMoves3(int pCheckingPieceSquare, ref List<Move> pMoveList)
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
                                pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 += 2;
                    if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, epM9) && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 7)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker1) && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker2) && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                                pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
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
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 += 2;
                    if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, epM9) && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker1) && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, possibleAttacker2) && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(whiteEnPassantMoves[epM9, enPassantSquare]);
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

        private void GetLegalBlackMoves3(int pCheckingPieceSquare, ref List<Move> pMoveList)
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
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 -= 2;
                    if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, epM9) && pieceTypeArray[epM9] == 1 && enPassantSquare % 8 != 0)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, possibleAttacker1) && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, possibleAttacker2) && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
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
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
                    }
                    epM9 -= 2;
                    if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, epM9) && pieceTypeArray[epM9] == 1)
                    {
                        int possibleAttacker1 = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[shiftedKS | epM9] & ULONG_OPERATIONS.SetBitToZero(tu, epM9)];
                        if (!(ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, possibleAttacker1) && pieceTypeAbilities[pieceTypeArray[possibleAttacker1], squareConnectivesPrecalculationArray[shiftedKS | epM9]] ||
                            ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, possibleAttacker2) && pieceTypeAbilities[pieceTypeArray[possibleAttacker2], squareConnectivesPrecalculationArray[shiftedKS | epM8]]))
                            pMoveList.Add(blackEnPassantMoves[epM9, enPassantSquare]);
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

        #endregion

        #region | MINIMAX FUNCTIONS |

        private const int WHITE_CHECKMATE_VAL = 100000, BLACK_CHECKMATE_VAL = -100000, CHECK_EXTENSION_LENGTH = -3;
        private readonly Move NULL_MOVE = new Move(0, 0, 0);

        public int MinimaxRoot(int pDepth)
        {
            int baseLineLen = 0;
            if (curSearchZobristKeyLine != null) baseLineLen = curSearchZobristKeyLine.Length;
            ulong[] completeZobristHistory = new ulong[baseLineLen + pDepth - CHECK_EXTENSION_LENGTH + 1];
            for (int i = 0; i < baseLineLen; i++) completeZobristHistory[i] = curSearchZobristKeyLine[i];
            curSearchZobristKeyLine = completeZobristHistory;

            int perftScore = 0, tattk;

            if (isWhiteToMove)
            {
                tattk = PreMinimaxCheckCheckWhite();
                Console.WriteLine(tattk);
                if (tattk == -1) perftScore = MinimaxWhite(pDepth, baseLineLen, tattk);
                else perftScore = MinimaxWhite(pDepth, baseLineLen, tattk);
            }
            else
            {
                tattk = PreMinimaxCheckCheckBlack();
                Console.WriteLine(tattk);
                if (tattk == -1) perftScore = MinimaxBlack(pDepth, baseLineLen, tattk);
                else perftScore = MinimaxBlack(pDepth, baseLineLen, tattk);
            }

            return perftScore;
        }

        private int MinimaxWhite(int pDepth, int pRepetitionHistoryPly, int pCheckingSquare)
        {
            if ((pDepth <= 0 && pCheckingSquare == 0) || pDepth < CHECK_EXTENSION_LENGTH) return Evaluate(pRepetitionHistoryPly - 4);

            List<Move> moveOptionList = new List<Move>();
            Move bestMove = NULL_MOVE;
            GetLegalWhiteMoves(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tWhiteKingSquare = whiteKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1, curEval = BLACK_CHECKMATE_VAL + pDepth;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;

            enPassantSquare = 65;
            isWhiteToMove = true;

            for (int m = 0; m < molc; m++)
            {
                Move curMove = moveOptionList[m];
                int tPieceType = curMove.pieceType, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPTI = pieceTypeArray[tEndPos], tCheckPos = -1, tI, tPossibleAttackPiece;

                #region MakeMove()

                fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
                whitePieceBitboard = tWPB ^ curMove.ownPieceBitboardXOR;
                pieceTypeArray[tEndPos] = tPieceType;
                pieceTypeArray[tStartPos] = 0;
                zobristKey = tZobristKey ^ pieceHashesWhite[tStartPos, tPieceType] ^ pieceHashesWhite[tEndPos, tPieceType];
                switch (curMove.moveTypeID)
                {
                    case 0: // Standard-Standard-Move
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 1: // Standard-Pawn-Move
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePawnAttackSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 2: // Standard-Knight-Move
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(knightSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 3: // Standard-King-Move
                        whiteKingSquare = tEndPos;
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 4: // Standard-Rook-Move
                        blackPieceBitboard = tBPB;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 5: // Standard-Pawn-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePawnAttackSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 6: // Standard-Knight-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(knightSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 7: // Standard-King-Capture
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[whiteKingSquare = tEndPos, tPTI];
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 8: // Standard-Rook-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 9: // Standard-Standard-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 10: // Double-Pawn-Move
                        zobristKey ^= enPassantSquareHashes[enPassantSquare = curMove.enPassantOption];
                        blackPieceBitboard = tBPB;
                        fiftyMoveRuleCounter = 0;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePawnAttackSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 11: // Rochade
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        whiteKingSquare = tEndPos;
                        blackPieceBitboard = tBPB;
                        pieceTypeArray[curMove.rochadeEndPos] = 4;
                        pieceTypeArray[curMove.rochadeStartPos] = 0;
                        zobristKey ^= pieceHashesWhite[curMove.rochadeStartPos, 4] ^ pieceHashesWhite[curMove.rochadeEndPos, 4];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | curMove.rochadeEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << curMove.rochadeEndPos)) tCheckPos = curMove.rochadeEndPos;
                        break;
                    case 12: // En-Passant
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[curMove.enPassantOption] = 0;
                        zobristKey ^= pieceHashesBlack[curMove.enPassantOption, 1];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(whitePawnAttackSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | curMove.enPassantOption] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 13: // Standard-Promotion
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        zobristKey ^= pieceHashesWhite[tEndPos, 1] ^ pieceHashesWhite[tEndPos, curMove.promotionType];
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && ((int)(knightSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        break;
                    case 14: // Capture-Promotion
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI] ^ pieceHashesWhite[tEndPos, 1] ^ pieceHashesWhite[tEndPos, curMove.promotionType];
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && ((int)(knightSquareBitboards[tEndPos] >> (blackKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        break;
                }
                curSearchZobristKeyLine[pRepetitionHistoryPly] = zobristKey;

                #endregion

                int tEval = MinimaxBlack(pDepth - 1, pRepetitionHistoryPly + 1, tCheckPos);
                if (tEval > curEval)
                {
                    bestMove = curMove;
                    curEval = tEval;
                }

                #region UndoMove()

                pieceTypeArray[tEndPos] = tPTI;
                pieceTypeArray[tStartPos] = tPieceType;
                whiteCastleRightKingSide = tWKSCR;
                whiteCastleRightQueenSide = tWQSCR;
                blackCastleRightKingSide = tBKSCR;
                blackCastleRightQueenSide = tBQSCR;
                switch (curMove.moveTypeID)
                {
                    case 3: // Standard-King-Move
                        whiteKingSquare = tWhiteKingSquare;
                        break;
                    case 7: // Standard-King-Capture
                        whiteKingSquare = tWhiteKingSquare;
                        break;
                    case 10: // Double-Pawn-Move
                        enPassantSquare = 65;
                        break;
                    case 11: // Rochade
                        whiteKingSquare = tWhiteKingSquare;
                        pieceTypeArray[curMove.rochadeStartPos] = 4;
                        pieceTypeArray[curMove.rochadeEndPos] = 0;
                        break;
                    case 12: // En-Passant
                        pieceTypeArray[curMove.enPassantOption] = 1;
                        break;
                    case 13: // Standard-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                    case 14: // Capture-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                }

                #endregion
            }

            isWhiteToMove = true;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            if (molc == 0 && pCheckingSquare == 0) curEval = 0;
            if (!transpositionTable.ContainsKey(zobristKey)) transpositionTable.Add(zobristKey, bestMove);

            return curEval;
        }

        private int MinimaxBlack(int pDepth, int pRepetitionHistoryPly, int pCheckingSquare)
        {
            if ((pDepth <= 0 && pCheckingSquare == 0) || pDepth < CHECK_EXTENSION_LENGTH) return Evaluate(pRepetitionHistoryPly - 4);
            List<Move> moveOptionList = new List<Move>();
            Move bestMove = NULL_MOVE;
            GetLegalBlackMoves(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tBlackKingSquare = blackKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1, curEval = WHITE_CHECKMATE_VAL - pDepth;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;

            enPassantSquare = 65;
            isWhiteToMove = false;

            for (int m = 0; m < molc; m++)
            {
                Move curMove = moveOptionList[m];
                int tPieceType = curMove.pieceType, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPTI = pieceTypeArray[tEndPos], tCheckPos = -1, tI, tPossibleAttackPiece;

                #region MakeMove()

                fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
                blackPieceBitboard = tBPB ^ curMove.ownPieceBitboardXOR;
                pieceTypeArray[tEndPos] = tPieceType;
                pieceTypeArray[tStartPos] = 0;
                zobristKey = tZobristKey ^ pieceHashesBlack[tStartPos, tPieceType] ^ pieceHashesBlack[tEndPos, tPieceType];
                switch (curMove.moveTypeID)
                {
                    case 0: // Standard-Standard-Move
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 1: // Standard-Pawn-Move
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPawnAttackSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 2: // Standard-Knight-Move
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(knightSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 3: // Standard-King-Move
                        blackKingSquare = tEndPos;
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 4: // Standard-Rook-Move
                        whitePieceBitboard = tWPB;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 5: // Standard-Pawn-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPawnAttackSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 6: // Standard-Knight-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(knightSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 7: // Standard-King-Capture
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[blackKingSquare = tEndPos, tPTI];
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 8: // Standard-Rook-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 9: // Standard-Standard-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 10: // Double-Pawn-Move
                        zobristKey ^= enPassantSquareHashes[enPassantSquare = curMove.enPassantOption];
                        whitePieceBitboard = tWPB;
                        fiftyMoveRuleCounter = 0;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPawnAttackSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 11: // Rochade
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        blackKingSquare = tEndPos;
                        whitePieceBitboard = tWPB;
                        pieceTypeArray[curMove.rochadeEndPos] = 4;
                        pieceTypeArray[curMove.rochadeStartPos] = 0;
                        zobristKey ^= pieceHashesBlack[curMove.rochadeStartPos, 4] ^ pieceHashesBlack[curMove.rochadeEndPos, 4];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | curMove.rochadeEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << curMove.rochadeEndPos)) tCheckPos = curMove.rochadeEndPos;
                        break;
                    case 12: // En-Passant
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[curMove.enPassantOption] = 0;
                        zobristKey ^= pieceHashesWhite[curMove.enPassantOption, 1];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (((int)(blackPawnAttackSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | curMove.enPassantOption] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 13: // Standard-Promotion
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        zobristKey ^= pieceHashesBlack[tEndPos, 1] ^ pieceHashesBlack[tEndPos, curMove.promotionType];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && ((int)(knightSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        break;
                    case 14: // Capture-Promotion
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
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
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI] ^ pieceHashesBlack[tEndPos, 1] ^ pieceHashesBlack[tEndPos, curMove.promotionType];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (((int)(blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard])) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && ((int)(knightSquareBitboards[tEndPos] >> (whiteKingSquare)) & 1) == 1) tCheckPos = tEndPos;
                        break;
                }
                curSearchZobristKeyLine[pRepetitionHistoryPly] = zobristKey;

                #endregion

                int tEval = MinimaxWhite(pDepth - 1, pRepetitionHistoryPly + 1, tCheckPos);
                if (tEval < curEval)
                {
                    bestMove = curMove;
                    curEval = tEval;
                }

                #region UndoMove()

                pieceTypeArray[tEndPos] = tPTI;
                pieceTypeArray[tStartPos] = tPieceType;
                whiteCastleRightKingSide = tWKSCR;
                whiteCastleRightQueenSide = tWQSCR;
                blackCastleRightKingSide = tBKSCR;
                blackCastleRightQueenSide = tBQSCR;
                switch (curMove.moveTypeID)
                {
                    case 3: // Standard-King-Move
                        blackKingSquare = tBlackKingSquare;
                        break;
                    case 7: // Standard-King-Capture
                        blackKingSquare = tBlackKingSquare;
                        break;
                    case 10: // Double-Pawn-Move
                        enPassantSquare = 65;
                        break;
                    case 11: // Rochade
                        blackKingSquare = tBlackKingSquare;
                        pieceTypeArray[curMove.rochadeStartPos] = 4;
                        pieceTypeArray[curMove.rochadeEndPos] = 0;
                        break;
                    case 12: // En-Passant
                        pieceTypeArray[curMove.enPassantOption] = 1;
                        break;
                    case 13: // Standard-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                    case 14: // Capture-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                }

                #endregion
            }

            isWhiteToMove = false;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            if (molc == 0 && pCheckingSquare == 0) curEval = 0;
            if (!transpositionTable.ContainsKey(zobristKey)) transpositionTable.Add(zobristKey, bestMove);

            return curEval;
        }

        private int MinimaxWhite2(int pDepth, int pRepetitionHistoryPly, int pCheckingSquare)
        {
            if (pDepth == 0) return 1;

            List<Move> moveOptionList = new List<Move>();
            GetLegalWhiteMoves(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tWhiteKingSquare = whiteKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;

            enPassantSquare = 65;
            isWhiteToMove = true;

            int tC = 0;

            for (int m = 0; m < molc; m++)
            {
                Move curMove = moveOptionList[m];
                int tPieceType = curMove.pieceType, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPTI = pieceTypeArray[tEndPos], tCheckPos = -1, tI, tPossibleAttackPiece;

                #region MakeMove()

                fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
                whitePieceBitboard = tWPB ^ curMove.ownPieceBitboardXOR;
                pieceTypeArray[tEndPos] = tPieceType;
                pieceTypeArray[tStartPos] = 0;
                zobristKey = tZobristKey ^ pieceHashesWhite[tStartPos, tPieceType] ^ pieceHashesWhite[tEndPos, tPieceType];
                switch (curMove.moveTypeID)
                {
                    case 0: // Standard-Standard-Move
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 1: // Standard-Pawn-Move
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((whitePawnAttackSquareBitboards[tEndPos] >> blackKingSquare & 1) == 1) tCheckPos = tEndPos;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 2: // Standard-Knight-Move
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((knightSquareBitboards[tEndPos] >> blackKingSquare & 1) == 1) tCheckPos = tEndPos;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 3: // Standard-King-Move
                        whiteKingSquare = tEndPos;
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 4: // Standard-Rook-Move
                        blackPieceBitboard = tBPB;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 5: // Standard-Pawn-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((whitePawnAttackSquareBitboards[tEndPos] >> blackKingSquare & 1) == 1) tCheckPos = tEndPos;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 6: // Standard-Knight-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((knightSquareBitboards[tEndPos] >> blackKingSquare & 1) == 1) tCheckPos = tEndPos;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 7: // Standard-King-Capture
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[whiteKingSquare = tEndPos, tPTI];
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 8: // Standard-Rook-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 9: // Standard-Standard-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 10: // Double-Pawn-Move
                        zobristKey ^= enPassantSquareHashes[enPassantSquare = curMove.enPassantOption];
                        blackPieceBitboard = tBPB;
                        fiftyMoveRuleCounter = 0;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((whitePawnAttackSquareBitboards[tEndPos] >> blackKingSquare & 1) == 1) tCheckPos = tEndPos;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 11: // Rochade
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        whiteKingSquare = tEndPos;
                        blackPieceBitboard = tBPB;
                        pieceTypeArray[curMove.rochadeEndPos] = 4;
                        pieceTypeArray[curMove.rochadeStartPos] = 0;
                        zobristKey ^= pieceHashesWhite[curMove.rochadeStartPos, 4] ^ pieceHashesWhite[curMove.rochadeEndPos, 4];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | curMove.rochadeEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << curMove.rochadeEndPos)) tCheckPos = curMove.rochadeEndPos;
                        break;
                    case 12: // En-Passant
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[curMove.enPassantOption] = 0;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((whitePawnAttackSquareBitboards[tEndPos] >> blackKingSquare & 1) == 1) tCheckPos = tEndPos;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | curMove.enPassantOption] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 13: // Standard-Promotion
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && (knightSquareBitboards[tEndPos] >> blackKingSquare & 1) == 1) tCheckPos = tEndPos;
                        break;
                    case 14: // Capture-Promotion
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if ((whitePieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && (knightSquareBitboards[tEndPos] >> blackKingSquare & 1) == 1) tCheckPos = tEndPos;
                        break;
                }
                curSearchZobristKeyLine[pRepetitionHistoryPly] = zobristKey;

                #endregion

                tC += MinimaxBlack(pDepth - 1, pRepetitionHistoryPly + 1, tCheckPos);

                #region UndoMove()

                pieceTypeArray[tEndPos] = tPTI;
                pieceTypeArray[tStartPos] = tPieceType;
                whiteCastleRightKingSide = tWKSCR;
                whiteCastleRightQueenSide = tWQSCR;
                blackCastleRightKingSide = tBKSCR;
                blackCastleRightQueenSide = tBQSCR;
                switch (curMove.moveTypeID)
                {
                    case 3: // Standard-King-Move
                        whiteKingSquare = tWhiteKingSquare;
                        break;
                    case 7: // Standard-King-Capture
                        whiteKingSquare = tWhiteKingSquare;
                        break;
                    case 10: // Double-Pawn-Move
                        enPassantSquare = 65;
                        break;
                    case 11: // Rochade
                        whiteKingSquare = tWhiteKingSquare;
                        pieceTypeArray[curMove.rochadeStartPos] = 4;
                        pieceTypeArray[curMove.rochadeEndPos] = 0;
                        break;
                    case 12: // En-Passant
                        pieceTypeArray[curMove.enPassantOption] = 1;
                        break;
                    case 13: // Standard-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                    case 14: // Capture-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                }

                #endregion
            }

            isWhiteToMove = true;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            return tC;
        }

        private int MinimaxBlack2(int pDepth, int pRepetitionHistoryPly, int pCheckingSquare)
        {
            if (pDepth == 0) return 1;
            List<Move> moveOptionList = new List<Move>();
            GetLegalBlackMoves(pCheckingSquare, ref moveOptionList);
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
                int tPieceType = curMove.pieceType, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPTI = pieceTypeArray[tEndPos], tCheckPos = -1, tI, tPossibleAttackPiece;

                #region MakeMove()

                fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
                blackPieceBitboard = tBPB ^ curMove.ownPieceBitboardXOR;
                pieceTypeArray[tEndPos] = tPieceType;
                pieceTypeArray[tStartPos] = 0;
                zobristKey = tZobristKey ^ pieceHashesBlack[tStartPos, tPieceType] ^ pieceHashesBlack[tEndPos, tPieceType];
                switch (curMove.moveTypeID)
                {
                    case 0: // Standard-Standard-Move
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 1: // Standard-Pawn-Move
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((blackPawnAttackSquareBitboards[tEndPos] >> whiteKingSquare & 1) == 1) tCheckPos = tEndPos;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 2: // Standard-Knight-Move
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((knightSquareBitboards[tEndPos] >> whiteKingSquare & 1) == 1) tCheckPos = tEndPos;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 3: // Standard-King-Move
                        blackKingSquare = tEndPos;
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 4: // Standard-Rook-Move
                        whitePieceBitboard = tWPB;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 5: // Standard-Pawn-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((blackPawnAttackSquareBitboards[tEndPos] >> whiteKingSquare & 1) == 1) tCheckPos = tEndPos;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 6: // Standard-Knight-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((knightSquareBitboards[tEndPos] >> whiteKingSquare & 1) == 1) tCheckPos = tEndPos;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 7: // Standard-King-Capture
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[blackKingSquare = tEndPos, tPTI];
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 8: // Standard-Rook-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 9: // Standard-Standard-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 10: // Double-Pawn-Move
                        zobristKey ^= enPassantSquareHashes[enPassantSquare = curMove.enPassantOption];
                        whitePieceBitboard = tWPB;
                        fiftyMoveRuleCounter = 0;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((blackPawnAttackSquareBitboards[tEndPos] >> whiteKingSquare & 1) == 1) tCheckPos = tEndPos;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 11: // Rochade
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        blackKingSquare = tEndPos;
                        whitePieceBitboard = tWPB;
                        pieceTypeArray[curMove.rochadeEndPos] = 4;
                        pieceTypeArray[curMove.rochadeStartPos] = 0;
                        zobristKey ^= pieceHashesBlack[curMove.rochadeStartPos, 4] ^ pieceHashesBlack[curMove.rochadeEndPos, 4];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | curMove.rochadeEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << curMove.rochadeEndPos)) tCheckPos = curMove.rochadeEndPos;
                        break;
                    case 12: // En-Passant
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[curMove.enPassantOption] = 0;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if ((blackPawnAttackSquareBitboards[tEndPos] >> whiteKingSquare & 1) == 1) tCheckPos = tEndPos;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | curMove.enPassantOption] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 13: // Standard-Promotion
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && (knightSquareBitboards[tEndPos] >> whiteKingSquare & 1) == 1) tCheckPos = tEndPos;
                        break;
                    case 14: // Capture-Promotion
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if ((blackPieceBitboard >> (tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) & 1) == 1 && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && (knightSquareBitboards[tEndPos] >> whiteKingSquare & 1) == 1) tCheckPos = tEndPos;
                        break;
                }
                curSearchZobristKeyLine[pRepetitionHistoryPly] = zobristKey;

                #endregion

                tC += MinimaxWhite(pDepth - 1, pRepetitionHistoryPly + 1, tCheckPos);

                #region UndoMove()

                pieceTypeArray[tEndPos] = tPTI;
                pieceTypeArray[tStartPos] = tPieceType;
                whiteCastleRightKingSide = tWKSCR;
                whiteCastleRightQueenSide = tWQSCR;
                blackCastleRightKingSide = tBKSCR;
                blackCastleRightQueenSide = tBQSCR;
                switch (curMove.moveTypeID)
                {
                    case 3: // Standard-King-Move
                        blackKingSquare = tBlackKingSquare;
                        break;
                    case 7: // Standard-King-Capture
                        blackKingSquare = tBlackKingSquare;
                        break;
                    case 10: // Double-Pawn-Move
                        enPassantSquare = 65;
                        break;
                    case 11: // Rochade
                        blackKingSquare = tBlackKingSquare;
                        pieceTypeArray[curMove.rochadeStartPos] = 4;
                        pieceTypeArray[curMove.rochadeEndPos] = 0;
                        break;
                    case 12: // En-Passant
                        pieceTypeArray[curMove.enPassantOption] = 1;
                        break;
                    case 13: // Standard-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                    case 14: // Capture-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                }

                #endregion
            }

            isWhiteToMove = false;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            return tC;
        }

        private int MinimaxWhite3(int pDepth, int pRepetitionHistoryPly, int pCheckingSquare)
        {
            if (pDepth == 0) return 1;

            List<Move> moveOptionList = new List<Move>();
            GetLegalWhiteMoves(pCheckingSquare, ref moveOptionList);
            int molc = moveOptionList.Count, tWhiteKingSquare = whiteKingSquare, tEPSquare = enPassantSquare, tFiftyMoveRuleCounter = fiftyMoveRuleCounter + 1;
            ulong tZobristKey = zobristKey ^ blackTurnHash ^ enPassantSquareHashes[tEPSquare];
            bool tWKSCR = whiteCastleRightKingSide, tWQSCR = whiteCastleRightQueenSide, tBKSCR = blackCastleRightKingSide, tBQSCR = blackCastleRightQueenSide;
            ulong tAPB = allPieceBitboard, tBPB = blackPieceBitboard, tWPB = whitePieceBitboard;

            enPassantSquare = 65;
            isWhiteToMove = true;

            int tC = 0;

            for (int m = 0; m < molc; m++)
            {
                Move curMove = moveOptionList[m];
                int tPieceType = curMove.pieceType, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPTI = pieceTypeArray[tEndPos], tCheckPos = -1, tI, tPossibleAttackPiece;

                #region MakeMove()

                fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
                whitePieceBitboard = tWPB ^ curMove.ownPieceBitboardXOR;
                pieceTypeArray[tEndPos] = tPieceType;
                pieceTypeArray[tStartPos] = 0;
                zobristKey = tZobristKey ^ pieceHashesWhite[tStartPos, tPieceType] ^ pieceHashesWhite[tEndPos, tPieceType];
                switch (curMove.moveTypeID)
                {
                    case 0: // Standard-Standard-Move
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 1: // Standard-Pawn-Move
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(whitePawnAttackSquareBitboards[tEndPos], blackKingSquare)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 2: // Standard-Knight-Move
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[tEndPos], blackKingSquare)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 3: // Standard-King-Move
                        whiteKingSquare = tEndPos;
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        blackPieceBitboard = tBPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 4: // Standard-Rook-Move
                        blackPieceBitboard = tBPB;
                        if (whiteCastleRightQueenSide && tStartPos == 0) {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        } else if (whiteCastleRightKingSide && tStartPos == 7) {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 5: // Standard-Pawn-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56) {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        } else if (blackCastleRightKingSide && tEndPos == 63) {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(whitePawnAttackSquareBitboards[tEndPos], blackKingSquare)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 6: // Standard-Knight-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56) {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        } else if (blackCastleRightKingSide && tEndPos == 63) {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[tEndPos], blackKingSquare)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 7: // Standard-King-Capture
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[whiteKingSquare = tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56) {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        } else if (blackCastleRightKingSide && tEndPos == 63) {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 8: // Standard-Rook-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (whiteCastleRightQueenSide && tStartPos == 0) {
                            zobristKey ^= whiteQueenSideRochadeRightHash;
                            whiteCastleRightQueenSide = false;
                        }
                        else if (whiteCastleRightKingSide && tStartPos == 7) {
                            zobristKey ^= whiteKingSideRochadeRightHash;
                            whiteCastleRightKingSide = false;
                        }
                        if (blackCastleRightQueenSide && tEndPos == 56) {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        } else if (blackCastleRightKingSide && tEndPos == 63) {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 9: // Standard-Standard-Capture
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56) {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        } else if (blackCastleRightKingSide && tEndPos == 63) {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 10: // Double-Pawn-Move
                        zobristKey ^= enPassantSquareHashes[enPassantSquare = curMove.enPassantOption];
                        blackPieceBitboard = tBPB;
                        fiftyMoveRuleCounter = 0;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(whitePawnAttackSquareBitboards[tEndPos], blackKingSquare)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 11: // Rochade
                        if (whiteCastleRightKingSide) zobristKey ^= whiteKingSideRochadeRightHash;
                        if (whiteCastleRightQueenSide) zobristKey ^= whiteQueenSideRochadeRightHash;
                        whiteCastleRightQueenSide = whiteCastleRightKingSide = false;
                        whiteKingSquare = tEndPos;
                        blackPieceBitboard = tBPB;
                        pieceTypeArray[curMove.rochadeEndPos] = 4;
                        pieceTypeArray[curMove.rochadeStartPos] = 0;
                        zobristKey ^= pieceHashesWhite[curMove.rochadeStartPos, 4] ^ pieceHashesWhite[curMove.rochadeEndPos, 4];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | curMove.rochadeEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << curMove.rochadeEndPos)) tCheckPos = curMove.rochadeEndPos;
                        break;
                    case 12: // En-Passant
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[curMove.enPassantOption] = 0;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(whitePawnAttackSquareBitboards[tEndPos], blackKingSquare)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | curMove.enPassantOption] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 13: // Standard-Promotion
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[tEndPos], blackKingSquare)) tCheckPos = tEndPos;
                        break;
                    case 14: // Capture-Promotion
                        fiftyMoveRuleCounter = 0;
                        blackPieceBitboard = tBPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        zobristKey ^= pieceHashesBlack[tEndPos, tPTI];
                        if (blackCastleRightQueenSide && tEndPos == 56) {
                            zobristKey ^= blackQueenSideRochadeRightHash;
                            blackCastleRightQueenSide = false;
                        } else if (blackCastleRightKingSide && tEndPos == 63) {
                            zobristKey ^= blackKingSideRochadeRightHash;
                            blackCastleRightKingSide = false;
                        }
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = blackKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(whitePieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[blackKingSquare][squareConnectivesPrecalculationRayArray[tI = blackKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[tEndPos], blackKingSquare)) tCheckPos = tEndPos;
                        break;
                }
                curSearchZobristKeyLine[pRepetitionHistoryPly] = zobristKey;

                #endregion

                tC += MinimaxBlack(pDepth - 1, pRepetitionHistoryPly + 1, tCheckPos);

                #region UndoMove()

                pieceTypeArray[tEndPos] = tPTI;
                pieceTypeArray[tStartPos] = tPieceType;
                whiteCastleRightKingSide = tWKSCR;
                whiteCastleRightQueenSide = tWQSCR;
                blackCastleRightKingSide = tBKSCR;
                blackCastleRightQueenSide = tBQSCR;
                switch (curMove.moveTypeID)
                {
                    case 3: // Standard-King-Move
                        whiteKingSquare = tWhiteKingSquare;
                        break;
                    case 7: // Standard-King-Capture
                        whiteKingSquare = tWhiteKingSquare;
                        break;
                    case 10: // Double-Pawn-Move
                        enPassantSquare = 65;
                        break;
                    case 11: // Rochade
                        whiteKingSquare = tWhiteKingSquare;
                        pieceTypeArray[curMove.rochadeStartPos] = 4;
                        pieceTypeArray[curMove.rochadeEndPos] = 0;
                        break;
                    case 12: // En-Passant
                        pieceTypeArray[curMove.enPassantOption] = 1;
                        break;
                    case 13: // Standard-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                    case 14: // Capture-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                }

                #endregion
            }

            isWhiteToMove = true;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            return tC;
        }

        private int MinimaxBlack3(int pDepth, int pRepetitionHistoryPly, int pCheckingSquare)
        {
            if (pDepth == 0) return 1;
            List<Move> moveOptionList = new List<Move>();
            GetLegalBlackMoves(pCheckingSquare, ref moveOptionList);
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
                int tPieceType = curMove.pieceType, tStartPos = curMove.startPos, tEndPos = curMove.endPos, tPTI = pieceTypeArray[tEndPos], tCheckPos = -1, tI, tPossibleAttackPiece;

                #region MakeMove()

                fiftyMoveRuleCounter = tFiftyMoveRuleCounter;
                blackPieceBitboard = tBPB ^ curMove.ownPieceBitboardXOR;
                pieceTypeArray[tEndPos] = tPieceType;
                pieceTypeArray[tStartPos] = 0;
                zobristKey = tZobristKey ^ pieceHashesBlack[tStartPos, tPieceType] ^ pieceHashesBlack[tEndPos, tPieceType];
                switch (curMove.moveTypeID)
                {
                    case 0: // Standard-Standard-Move
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 1: // Standard-Pawn-Move
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(blackPawnAttackSquareBitboards[tEndPos], whiteKingSquare)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 2: // Standard-Knight-Move
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[tEndPos], whiteKingSquare)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 3: // Standard-King-Move
                        blackKingSquare = tEndPos;
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        whitePieceBitboard = tWPB;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 4: // Standard-Rook-Move
                        whitePieceBitboard = tWPB;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 5: // Standard-Pawn-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(blackPawnAttackSquareBitboards[tEndPos], whiteKingSquare)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 6: // Standard-Knight-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[tEndPos], whiteKingSquare)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 7: // Standard-King-Capture
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[blackKingSquare = tEndPos, tPTI];
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 8: // Standard-Rook-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 9: // Standard-Standard-Capture
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 10: // Double-Pawn-Move
                        zobristKey ^= enPassantSquareHashes[enPassantSquare = curMove.enPassantOption];
                        whitePieceBitboard = tWPB;
                        fiftyMoveRuleCounter = 0;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (ULONG_OPERATIONS.IsBitOne(blackPawnAttackSquareBitboards[tEndPos], whiteKingSquare)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 11: // Rochade
                        if (blackCastleRightKingSide) zobristKey ^= blackKingSideRochadeRightHash;
                        if (blackCastleRightQueenSide) zobristKey ^= blackQueenSideRochadeRightHash;
                        blackCastleRightKingSide = blackCastleRightQueenSide = false;
                        blackKingSquare = tEndPos;
                        whitePieceBitboard = tWPB;
                        pieceTypeArray[curMove.rochadeEndPos] = 4;
                        pieceTypeArray[curMove.rochadeStartPos] = 0;
                        zobristKey ^= pieceHashesBlack[curMove.rochadeStartPos, 4] ^ pieceHashesBlack[curMove.rochadeEndPos, 4];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | curMove.rochadeEndPos] == 1 && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << curMove.rochadeEndPos)) tCheckPos = curMove.rochadeEndPos;
                        break;
                    case 12: // En-Passant
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[curMove.enPassantOption] = 0;
                        zobristKey ^= pieceHashesWhite[tEndPos, tPTI];
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if(ULONG_OPERATIONS.IsBitOne(blackPawnAttackSquareBitboards[tEndPos], whiteKingSquare)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | curMove.enPassantOption] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        break;
                    case 13: // Standard-Promotion
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[tEndPos], whiteKingSquare)) tCheckPos = tEndPos;
                        break;
                    case 14: // Capture-Promotion
                        fiftyMoveRuleCounter = 0;
                        whitePieceBitboard = tWPB ^ curMove.oppPieceBitboardXOR;
                        pieceTypeArray[tEndPos] = tPieceType = curMove.promotionType;
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
                        allPieceBitboard = blackPieceBitboard | whitePieceBitboard;
                        if (pieceTypeAbilities[tPieceType, squareConnectivesPrecalculationArray[tI = whiteKingSquare << 6 | tEndPos]] && (squareConnectivesPrecalculationLineArray[tI] & allPieceBitboard) == (1ul << tEndPos)) tCheckPos = tEndPos;
                        else if (ULONG_OPERATIONS.IsBitOne(blackPieceBitboard, tPossibleAttackPiece = rayCollidingSquareCalculations[whiteKingSquare][squareConnectivesPrecalculationRayArray[tI = whiteKingSquare << 6 | tStartPos] & allPieceBitboard]) && pieceTypeAbilities[pieceTypeArray[tPossibleAttackPiece], squareConnectivesPrecalculationArray[tI]]) tCheckPos = tPossibleAttackPiece;
                        else if (tPieceType == 2 && ULONG_OPERATIONS.IsBitOne(knightSquareBitboards[tEndPos], whiteKingSquare)) tCheckPos = tEndPos;
                        break;
                }
                curSearchZobristKeyLine[pRepetitionHistoryPly] = zobristKey;

                #endregion

                tC += MinimaxWhite(pDepth - 1, pRepetitionHistoryPly + 1, tCheckPos);

                #region UndoMove()

                pieceTypeArray[tEndPos] = tPTI;
                pieceTypeArray[tStartPos] = tPieceType;
                whiteCastleRightKingSide = tWKSCR;
                whiteCastleRightQueenSide = tWQSCR;
                blackCastleRightKingSide = tBKSCR;
                blackCastleRightQueenSide = tBQSCR;
                switch (curMove.moveTypeID)
                {
                    case 3: // Standard-King-Move
                        blackKingSquare = tBlackKingSquare;
                        break;
                    case 7: // Standard-King-Capture
                        blackKingSquare = tBlackKingSquare;
                        break;
                    case 10: // Double-Pawn-Move
                        enPassantSquare = 65;
                        break;
                    case 11: // Rochade
                        blackKingSquare = tBlackKingSquare;
                        pieceTypeArray[curMove.rochadeStartPos] = 4;
                        pieceTypeArray[curMove.rochadeEndPos] = 0;
                        break;
                    case 12: // En-Passant
                        pieceTypeArray[curMove.enPassantOption] = 1;
                        break;
                    case 13: // Standard-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                    case 14: // Capture-Promotion
                        pieceTypeArray[tStartPos] = 1;
                        break;
                }

                #endregion
            }

            isWhiteToMove = false;
            zobristKey = tZobristKey ^ blackTurnHash ^ enPassantSquareHashes[enPassantSquare = tEPSquare];
            allPieceBitboard = tAPB;
            whitePieceBitboard = tWPB;
            blackPieceBitboard = tBPB;
            fiftyMoveRuleCounter = tFiftyMoveRuleCounter - 1;

            return tC;
        }

        #endregion

        #region | EVALUATION |

        private Dictionary<ulong, Move> transpositionTable = new Dictionary<ulong, Move>();
        private int[] pieceEvals = new int[14] { 0, 100, 300, 320, 500, 900, 0, 0, -100, -300, -320, -500, -900, 0 };

        private int Evaluate(int pPlyOfFirstPossibleRepeatedPosition)
        {
            if (fiftyMoveRuleCounter > 99 || IsDrawByRepetition(pPlyOfFirstPossibleRepeatedPosition)) return 0;

            int tEval = 0;

            for (int p = 0; p < 64; p++)
            {
                tEval += pieceEvals[pieceTypeArray[p] + 7 * ((int)(blackPieceBitboard >> p) & 1)];
            }

            return tEval;
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

        #endregion

        #region | FEN MANAGEMENT |

        private char[] fenPieces = new char[7] { 'z', 'p', 'n', 'b', 'r', 'q', 'k' };
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

        #endregion

        #region | PRECALCULATIONS |

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

        private Move[,] whiteEnPassantMoves = new Move[64, 64];
        private Move[,] blackEnPassantMoves = new Move[64, 64];

        private bool[] epValidationArray = new bool[64]
        { false, false, false, false, false, false, false, false,
          false, false, false, false, false, false, false, false,
          true , true , true , true , true , true , true , true,
          true , true , true , true , true , true , true , true,
          true , true , true , true , true , true , true , true,
          true , true , true , true , true , true , true , true,
          false, false, false, false, false, false, false, false,
          false, false, false, false, false, false, false, false
        };

        private void PrecalculateEnPassantMoves()
        {
            for (int i = 0; i < 64; i++)
            {
                if (!epValidationArray[i]) continue;
                for (int j = 0; j < 64; j++)
                {
                    if (i == j) continue;
                    if (Math.Abs(i - j) > 9) continue; 
                    if (!epValidationArray[j]) continue;

                    whiteEnPassantMoves[i, j] = new Move(false, i, j, j - 8);
                    blackEnPassantMoves[i, j] = new Move(false, i, j, j + 8);
                }
            }
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

        #region | UTILITY |

        private string ReplaceAllULONGIsBitOne(string pCode)
        {
            const string METHOD_NAME = "ULONG_OPERATIONS.IsBitOne";
            int METHOD_NAME_LEN = METHOD_NAME.Length, iterations = 0;

            int pIndex = pCode.IndexOf(METHOD_NAME);
            while (pIndex != -1 && ++iterations < 10_000) {
                List<int> paramIndexes = new List<int>();
                int openedBracketCount = 1, closedBracketCount = 0, a = METHOD_NAME_LEN + pIndex;
                string tpCode = pCode.Substring(0, pIndex);
                paramIndexes.Add(a + 1);
                while (++a < pCode.Length)
                {
                    switch(pCode[a])
                    {
                        case '(':
                            openedBracketCount++;
                            break;
                        case ')':
                            if (++closedBracketCount == openedBracketCount) { pIndex = a; a = int.MaxValue - 5;}
                            break;
                        case ',':
                            if (openedBracketCount - closedBracketCount == 1) paramIndexes.Add(a + 1);
                            break;
                    }
                }

                if (openedBracketCount == closedBracketCount && paramIndexes.Count == 2)
                {
                    tpCode += "((int)(" + pCode.Substring(paramIndexes[0], paramIndexes[1] - paramIndexes[0] - 1) +
                    (pCode[paramIndexes[1]] == ' ' ? " >> (" : " >> (") + pCode.Substring(paramIndexes[1], pIndex - paramIndexes[1])
                    + ")) & 1) == 1" + pCode.Substring(pIndex + 1, pCode.Length - pIndex - 1);
                }
                pIndex = (pCode = tpCode).IndexOf(METHOD_NAME);
            }
            Console.WriteLine(iterations + " ITERATIONS");
            return pCode;
        }

        private string ReplaceAllULONGIsBitZero(string pCode)
        {
            const string METHOD_NAME = "ULONG_OPERATIONS.IsBitZero";
            int METHOD_NAME_LEN = METHOD_NAME.Length, iterations = 0;

            int pIndex = pCode.IndexOf(METHOD_NAME);
            while (pIndex != -1 && ++iterations < 10_000)
            {
                List<int> paramIndexes = new List<int>();
                int openedBracketCount = 1, closedBracketCount = 0, a = METHOD_NAME_LEN + pIndex;
                string tpCode = pCode.Substring(0, pIndex);
                paramIndexes.Add(a + 1);
                while (++a < pCode.Length)
                {
                    switch (pCode[a])
                    {
                        case '(':
                            openedBracketCount++;
                            break;
                        case ')':
                            if (++closedBracketCount == openedBracketCount) { pIndex = a; a = int.MaxValue - 5; }
                            break;
                        case ',':
                            if (openedBracketCount - closedBracketCount == 1) paramIndexes.Add(a + 1);
                            break;
                    }
                }
                if (openedBracketCount == closedBracketCount && paramIndexes.Count == 2)
                {
                    tpCode += "((int)(" + pCode.Substring(paramIndexes[0], paramIndexes[1] - paramIndexes[0] - 1) +
                    (pCode[paramIndexes[1]] == ' ' ? " >>" : " >> ") + pCode.Substring(paramIndexes[1], pIndex - paramIndexes[1])
                    + ") & 1) == 0" + pCode.Substring(pIndex + 1, pCode.Length - pIndex - 1);
                }
                pIndex = (pCode = tpCode).IndexOf(METHOD_NAME);
            }
            Console.WriteLine(iterations + " ITERATIONS");
            return pCode;
        }

        private string GetThreeDigitSeperatedInteger(int pInt)
        {
            string s = pInt.ToString(), r = s[0].ToString();
            int t = s.Length % 3;

            for (int i = 1; i < s.Length; i++)
            {
                if (i % 3 == t) r += ".";
                r += s[i];
            }

            s = "";
            for (int i = 0; i < r.Length; i++) s += r[i];

            return s;
        }

        #endregion
    }

    #region | DATA CLASSES |

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
        public int moveTypeID { get; private set; } = 0;
        public bool isCapture { get; private set; } = false;
        public bool isSliderMove { get; private set; }
        public bool isEnPassant { get; private set; } = false;
        public bool isPromotion { get; private set; } = false;
        public bool isRochade { get; private set; } = false;
        public bool isStandard { get; private set; } = false;
        public ulong ownPieceBitboardXOR { get; private set; } = 0ul;
        public ulong oppPieceBitboardXOR { get; private set; } = 0ul;
        //public ulong zobristHashXOR { get; private set; } = 0ul;

        /* [0] Standard-Standard Move
           [1] Standard-Pawn Move
           [2] Standard-Knight Move
           [3] Standard-King Move
           [4] Standard-Rook Move
           
           [5] Standard-Pawn Capture
           [6] Standard-Knight Capture
           [7] Standard-King Capture
           [8] Standard-Rook Capture
           [9] Standard-Standard Capture
           
           [10] Double-Pawn-Move
           [11] Rochade
           [12] En-Passant
           [13] Standard-Promotion
           [14] Capture-Promotion                          */

        public void PrecalculateMove()
        {
            if (isEnPassant) oppPieceBitboardXOR = ULONG_OPERATIONS.SetBitToOne(0ul, enPassantOption);
            else if (isCapture) oppPieceBitboardXOR = ULONG_OPERATIONS.SetBitToOne(0ul, endPos);

            ownPieceBitboardXOR = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToOne(0ul, startPos), endPos);
            if (isRochade) ownPieceBitboardXOR = ULONG_OPERATIONS.SetBitToOne(ULONG_OPERATIONS.SetBitToOne(ownPieceBitboardXOR, rochadeEndPos), rochadeStartPos);

            switch(pieceType) {
                case 1:
                    if (isPromotion) moveTypeID = isCapture ? 14 : 13;
                    else if (isEnPassant) moveTypeID = 12;
                    else if (isCapture) moveTypeID = 5;
                    else if (enPassantOption == 65) moveTypeID = 1;
                    else moveTypeID = 10; break;
                case 2:
                    if (isCapture) moveTypeID = 6;
                    else moveTypeID = 2; break;
                case 4:
                    bool b = startPos == 0 || startPos == 7 || startPos == 56 || startPos == 63 || endPos == 0 || endPos == 7 || endPos == 56 || endPos == 63;
                    if (isCapture) moveTypeID = b ? 8 : 9;
                    else moveTypeID = b ? 4 : 0; break;
                case 6:
                    if (isRochade) moveTypeID = 11;
                    else if (isCapture) moveTypeID = 7;
                    else moveTypeID = 3; break;
                default:
                    if (isCapture) moveTypeID = 9;
                    else moveTypeID = 0; break;
            }
        }

        public Move(int pSP, int pEP, int pPT) //Standard Move
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            isStandard = true;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
            PrecalculateMove();
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
            PrecalculateMove();
        }
        public Move(int pSP, int pEP, int pPT, bool pIC) // Capture
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = pPT;
            isCapture = pIC;
            if (!isCapture) isStandard = true;
            if (pPT == 3 || pPT == 4 || pPT == 5) isSliderMove = true;
            PrecalculateMove();
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
            PrecalculateMove();
        }
        public Move(bool b, int pSP, int pEP, int pEPS) // En Passant (bool param einfach nur damit ich noch einen Konstruktur haben kann)
        {
            startPos = pSP;
            endPos = pEP;
            pieceType = 1;
            isCapture = isEnPassant = true;
            enPassantOption = pEPS;
            isSliderMove = false;
            PrecalculateMove();
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
            PrecalculateMove();
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

    #endregion
}