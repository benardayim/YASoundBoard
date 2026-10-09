namespace YASoundBoard.Configuration;

public static class Paths
{
    public static string Resolve(string basePath, string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return basePath;
        }

        return Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(basePath, configured));
    }

    public static string ToPortable(string path) => path.Replace('\\', '/');
}
