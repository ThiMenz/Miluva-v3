namespace Miluva
{
    public static class ARDUINO_GAME_SETTINGS
    {

        public const string GAME_MODE = "CLASSIC"; // { Classic }

        // [CLASSIC]
        public const int HUMAN_TIME_IN_SEC = 300;
        public const double HUMAN_INCREMENT_INT_SEC = 2.5;
        public const int BOT_TIME_IN_SEC = 300;
        public const double BOT_INCREMENT_INT_SEC = 2.5;

        // [CLASSIC]
        public const bool HUMAN_PLAYS_WHITE = true;

        // [CLASSIC]
        public const string START_FEN = ENGINE_VALS.DEFAULT_FEN;

    }
}
