from stockfish import Stockfish

START_POS = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
SEARCH_DEPTH = 3

def ANALYZE_STOCKFISH_RETURN(pRet, pMoveList, pDepth):

	if len(pRet) == 0: #stalemate
		return 0

	if str(pRet[0]["Mate"]) != "None": #mate
		return 0

	if pDepth == 0: #horizon
		return 1

	tSum = 0

	for pMove in pRet:
		
		if str(pMove["Mate"]) != "None": #mate
			continue

		if (abs(pMove["Centipawn"]) > 60): # good / bad pos found
			continue 

		tM = pMove["Move"]
		pMoveList.append(tM)

		stockfish.set_fen_position(START_POS)
		stockfish.make_moves_from_current_position(pMoveList)

		tSum += ANALYZE_STOCKFISH_RETURN(stockfish.get_top_moves(256), pMoveList, pDepth - 1)

		pMoveList = pMoveList[:-pDepth]

	if SEARCH_DEPTH == pDepth + 1:
		
		print(pMoveList[0])

		print("COMPLEXITY: " + str(tSum))

		print("==============================")

	return tSum








stockfish = Stockfish(
	path=r"C:\Users\tpmen\Downloads\stockfish-windows-x86-64-avx2\stockfish\stockfish-windows-x86-64-avx2.exe",
	depth = 5, 
	parameters={ "Threads": 10, "UCI_Elo": 4000 }
)

print(stockfish.get_parameters())

stockfish.set_fen_position(START_POS)

ANALYZE_STOCKFISH_RETURN(stockfish.get_top_moves(256), [], SEARCH_DEPTH)