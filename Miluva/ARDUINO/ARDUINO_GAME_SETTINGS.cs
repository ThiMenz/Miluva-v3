namespace Miluva
{
    // X O  -  PLAYER vs ENGINE
    // X O  -  PLAYER vs PLAYER
    // O O  -  ENGINE vs ENGINE


    public static class ARDUINO_GAME_SETTINGS
    {
        public static readonly string GAME_MODE = "CLASSIC"; // { Classic }

        // [CLASSIC]
        public static readonly int WHITE_TIME_IN_SEC = 30;
        public static readonly double WHITE_INCREMENT_IN_SEC = 2.5;
        public static readonly int BLACK_TIME_IN_SEC = 30;
        public static readonly double BLACK_INCREMENT_IN_SEC = 2.5;

        // [CLASSIC]
        public static ENTITY_TYPE WHITE_ENTITY = ENTITY_TYPE.ENGINE,
                                  BLACK_ENTITY = ENTITY_TYPE.ENGINE;

        public static List<string> WHITE_DISCORD_IDS = new List<string>()
        { "719886222809628674" };
        public static List<string> BLACK_DISCORD_IDS = new List<string>()
        { };

        public enum ENTITY_TYPE { PLAYER, ENGINE, DISCORD };

        // [CLASSIC]
        public static readonly string START_FEN = @"8/8/8/5q2/8/2k1K3/8/8 w - - 8 5";

        // [CLASSIC]
        public static readonly CAMERA_BOTTOM_LINE CAM_LINE = CAMERA_BOTTOM_LINE.a8_a1;
        public enum CAMERA_BOTTOM_LINE { h1_h8, a1_h1, a8_a1, h8_a8 };



        //public static readonly bool HUMAN_PLAYS_WHITE = true;
    }
}
