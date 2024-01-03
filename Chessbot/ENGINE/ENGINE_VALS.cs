using System.Diagnostics;

namespace ChessBot
{
    public static class ENGINE_VALS
    {
        public const int CPU_CORES = 16;
        public const int PARALLEL_BOARDS = 32;
        public const int SELF_PLAY_THINK_TIME = 120_000;
        public const string DEFAULT_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    }
}