using System.Text.RegularExpressions;

namespace Settlement.Sfg;

public static class GlobMatcher
{
    public static bool IsMatch(string fileName, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase);
    }

    public static string Unique(string fileName) =>
        $"{Path.GetFileNameWithoutExtension(fileName)}.{DateTime.UtcNow:yyyyMMddHHmmssfff}{Path.GetExtension(fileName)}";
}
