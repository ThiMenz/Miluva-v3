from stockfish import Stockfish
import time
import os

START_POS = "4k3/2P1P3/5PPp/1K1p4/8/8/8/8 w - - 5 35";
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
	depth = 9, 
	parameters={ "Threads": 15, "UCI_Elo": 4000, "Hash": 512 }
)

print(stockfish.get_parameters())

#stockfish.set_fen_position(START_POS)
#print(stockfish.get_evaluation())



fenFile = open("TempFenList.txt", "r")
lns = fenFile.readlines()
fenFile.close()

fenCount = len(lns)

print(fenCount)

resultArr = [0] * fenCount;

tt = time.time()

for i in range(0, fenCount):

	if (i % 100 == 0): print(i)

	stockfish.set_position(["e2e4", "e7e6"])

	#stockfish.set_fen_position(lns[i])

	#teval = stockfish.get_evaluation()
	#
	#if (len(teval["type"]) == 4): 
	#	if (teval["value"] > 0): resultArr[i] = 9999
	#	else: resultArr[i] = -9999
	#
	#else: resultArr[i] = teval["value"]

print(time.time() - tt)

newFenFile = open("TempFenList2.txt", "w")
for i in range(0, fenCount):
	newFenFile.write(lns[i].replace("\n", "").replace("\r", "") + "|" + str(resultArr[i]) + os.linesep)
newFenFile.close()

# On Depth 9:
# 1k in 2.7s (Depth 8 <= 2s)
# 376 * 2.7 = 1015.2 [~17min]

#ANALYZE_STOCKFISH_RETURN(stockfish.get_top_moves(256), [], SEARCH_DEPTH)