namespace Miluva
{
    public static class ARDUINO_GAME_SETTINGS
    {

        public static readonly string GAME_MODE = "CLASSIC"; // { Classic }

        // [CLASSIC]
        public static readonly int HUMAN_TIME_IN_SEC = 1200;
        public static readonly double HUMAN_INCREMENT_IN_SEC = 2.5;
        public static readonly int BOT_TIME_IN_SEC = 1200;
        public static readonly double BOT_INCREMENT_IN_SEC = 2.5;

        // [CLASSIC]
        public static readonly bool HUMAN_PLAYS_WHITE = true;

        // [CLASSIC]
        public static readonly string START_FEN = ENGINE_VALS.DEFAULT_FEN;

    }
}
