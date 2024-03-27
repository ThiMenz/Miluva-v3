namespace Miluva
{
    public static class PathManager
    {
        public static string GetTXTPath(string pPath)
        {
            return Path.GetFullPath(pPath + ".txt").Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", "");
        }

        public static string GetPath(string pPath, string pPathEnding)
        {
            return Path.GetFullPath(pPath + "." + pPathEnding).Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", "");
        }
    }
}
