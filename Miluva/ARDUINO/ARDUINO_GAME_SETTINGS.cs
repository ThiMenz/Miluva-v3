namespace Miluva
{
    public static class ARDUINO_GAME_SETTINGS
    {
        public static readonly string GAME_MODE = "CLASSIC"; // { Classic }

        // [CLASSIC]
        public static readonly int HUMAN_TIME_IN_SEC = 300;
        public static readonly double HUMAN_INCREMENT_IN_SEC = 2.5;
        public static readonly int BOT_TIME_IN_SEC = 300;
        public static readonly double BOT_INCREMENT_IN_SEC = 2.5;

        // [CLASSIC]
        public static ENTITY_TYPE WHITE_ENTITY = ENTITY_TYPE.PLAYER       ,
                                  BLACK_ENTITY = ENTITY_TYPE.ENGINE       ;

        public static List<string> WHITE_DISCORD_IDS = new List<string>()
        { "719886222809628674" };
        public static List<string> BLACK_DISCORD_IDS = new List<string>()
        { };

        public enum ENTITY_TYPE { PLAYER, ENGINE, DISCORD };

        // [CLASSIC]
        public static readonly string START_FEN = @"8/8/8/4K3/8/k7/q7/8 w HA - 0 1";

        // [CLASSIC]
        public static readonly CAMERA_BOTTOM_LINE CAM_LINE = CAMERA_BOTTOM_LINE.a1_h1;
        public enum CAMERA_BOTTOM_LINE { h1_h8, a1_h1, a8_a1, h8_a8 };



        //public static readonly bool HUMAN_PLAYS_WHITE = true;
    }
}
