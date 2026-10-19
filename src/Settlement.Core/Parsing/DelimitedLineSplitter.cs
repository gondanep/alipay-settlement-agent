using System.Text;

namespace Settlement.Core.Parsing;

public static class DelimitedLineSplitter
{
    public static List<string> Split(string line, string delimiter, char quote)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var i = 0;
        while (i < line.Length)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == quote)
                {
                    if (i + 1 < line.Length && line[i + 1] == quote)
                    {
                        current.Append(quote);
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                current.Append(c);
                i++;
                continue;
            }

            if (c == quote && current.Length == 0)
            {
                inQuotes = true;
                i++;
                continue;
            }

            if (string.CompareOrdinal(line, i, delimiter, 0, delimiter.Length) == 0)
            {
                values.Add(current.ToString());
                current.Clear();
                i += delimiter.Length;
                continue;
            }

            current.Append(c);
            i++;
        }

        values.Add(current.ToString());
        return values;
    }
}
