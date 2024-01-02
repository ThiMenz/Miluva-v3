namespace ChessBot
{
    public static class PathManager
    {
        public static string GetTXTPath(string pPath)
        {
            return Path.GetFullPath(pPath + ".txt").Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", "");
        }
    }
}
