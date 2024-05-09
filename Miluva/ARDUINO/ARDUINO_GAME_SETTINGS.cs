namespace Miluva
{
    public static class ARDUINO_GAME_SETTINGS
    {

        public static readonly string GAME_MODE = "CLASSIC"; // { Classic }

        public static readonly int WHITE_TIME_IN_SEC = 3;
        public static readonly double WHITE_INCREMENT_IN_SEC = 2.5;
        public static readonly int BLACK_TIME_IN_SEC = 3;
        public static readonly double BLACK_INCREMENT_IN_SEC = 2.5;

        public static ENTITY_TYPE WHITE_ENTITY = ENTITY_TYPE.ENGINE,
                                  BLACK_ENTITY = ENTITY_TYPE.ENGINE;

        public static List<string> WHITE_DISCORD_IDS = new List<string>()
        { "719886222809628674" };
        public static List<string> BLACK_DISCORD_IDS = new List<string>()
        { "719886222809628674" };

        public static readonly string START_FEN = @"8/6PP/6K1/8/2k5/8/8/8 w HA - 0 1";

        public static readonly CAMERA_BOTTOM_LINE CAM_LINE = CAMERA_BOTTOM_LINE.a8_a1;
        public static readonly bool WHITE_TIMER_TO_THE_RIGHT = true;





        public enum ENTITY_TYPE { PLAYER, ENGINE, DISCORD };
        public enum CAMERA_BOTTOM_LINE { h1_h8, a1_h1, a8_a1, h8_a8 };

    }

    // DISCORD IDs:
    // Möhrchen -> 719886222809628674


    // X X X  -  PLAYER  vs ENGINE
    // X N N  -  PLAYER  vs PLAYER
    // X X X  -  ENGINE  vs ENGINE
    // X X X  -  DISCORD vs ENGINE
    // X X X  -  DISCORD vs PLAYER
    // X X X  -  DISCORD vs DISCORD
}
