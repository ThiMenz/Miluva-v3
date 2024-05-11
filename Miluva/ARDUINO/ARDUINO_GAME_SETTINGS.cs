namespace Miluva
{
    public static class ARDUINO_GAME_SETTINGS
    {

        public static string GAME_MODE = "CLASSIC"; // { Classic }

        public static int WHITE_TIME_IN_SEC = 3;
        public static double WHITE_INCREMENT_IN_SEC = 2.5;
        public static int BLACK_TIME_IN_SEC = 3;
        public static double BLACK_INCREMENT_IN_SEC = 2.5;

        public static ENTITY_TYPE WHITE_ENTITY = ENTITY_TYPE.ENGINE,
                                  BLACK_ENTITY = ENTITY_TYPE.ENGINE;

        public static Type ENGINE_TYPE = typeof(BoardManager);

        public static List<string> WHITE_DISCORD_IDS = new List<string>()
        { "719886222809628674" };
        public static List<string> BLACK_DISCORD_IDS = new List<string>()
        { "719886222809628674" };

        public static string START_FEN = @"8/6PP/6K1/8/2k5/8/8/8 w HA - 0 1";

        public static CAMERA_BOTTOM_LINE CAM_LINE = CAMERA_BOTTOM_LINE.a8_a1;
        public static bool WHITE_TIMER_TO_THE_RIGHT = true;


        public enum ENTITY_TYPE { PLAYER, ENGINE, DISCORD };
        public enum CAMERA_BOTTOM_LINE { h1_h8, a1_h1, a8_a1, h8_a8 };

        public static void LoadIn()
        {
            string[] tStrs = File.ReadAllLines(PathManager.GetTXTPath("ARDUINO/ARDUINO_SETTINGS"));
            int tL = tStrs.Length;
            for (int i = 0; i < tL; i++)
            {
                string[] tStrSplo = tStrs[i].Split(':');
                string tStr2 = tStrSplo[1];
                string[] tStrSpl2 = tStr2.Split(',');
                int ttiL = tStrSpl2.Length;
                switch (tStrSplo[0])
                {
                    case "WHITE_TIME_IN_SEC":
                        WHITE_TIME_IN_SEC = Convert.ToInt32(tStr2);
                        break;
                    case "WHITE_INCREMENT":
                        WHITE_INCREMENT_IN_SEC = Convert.ToDouble(tStr2);
                        break;
                    case "BLACK_TIME_IN_SEC":
                        BLACK_TIME_IN_SEC = Convert.ToInt32(tStr2);
                        break;
                    case "BLACK_INCREMENT":
                        BLACK_INCREMENT_IN_SEC = Convert.ToDouble(tStr2);
                        break;
                    case "WHITE_ENTITY_TYPE":
                        WHITE_ENTITY = (ENTITY_TYPE)Convert.ToInt32(tStr2);
                        break;
                    case "BLACK_ENTITY_TYPE":
                        BLACK_ENTITY = (ENTITY_TYPE)Convert.ToInt32(tStr2);
                        break;
                    case "ENGINE_TYPE":
                        switch (Convert.ToInt32(tStr2))
                        {
                            case 1:
                                ENGINE_TYPE = typeof(SNAPSHOT_V02_05_001);
                                break;
                            default:
                                ENGINE_TYPE = typeof(BoardManager);
                                break;
                        }
                        break;
                    case "START_FEN":
                        START_FEN = tStr2;
                        break;
                    case "CAM_LINE":
                        CAM_LINE = (CAMERA_BOTTOM_LINE)Convert.ToInt32(tStr2);
                        break;
                    case "WHITE_TIMER_RIGHT":
                        WHITE_TIMER_TO_THE_RIGHT = Convert.ToBoolean(tStr2);
                        break;
                    case "WHITE_DC_IDs":
                        for (int j = 0; j < ttiL; j++)
                            WHITE_DISCORD_IDS.Add(tStrSpl2[j]);
                        break;
                    case "BLACK_DC_IDs":
                        for (int j = 0; j < ttiL; j++)
                            BLACK_DISCORD_IDS.Add(tStrSpl2[j]);
                        break;
                }
            }
        }

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