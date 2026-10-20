using System.Text.RegularExpressions;
namespace EqlTracker.Core;

/// <summary>Reads Itempage values without stopping at nested SmIcon/other templates.</summary>
internal static class WikiTemplateFields
{
    public static string Value(string text, string name)
    {
        var start = Regex.Match(text, @"^[ \t]*\|[ \t]*" + Regex.Escape(name) + @"[ \t]*=[ \t]*", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!start.Success) return "";
        int from = start.Index + start.Length, depth = 0;
        for (int i = from; i < text.Length; i++)
        {
            if (i + 1 < text.Length && text[i] == '{' && text[i + 1] == '{') { depth++; i++; continue; }
            if (i + 1 < text.Length && text[i] == '}' && text[i + 1] == '}') { if (depth == 0) return text[from..i]; depth--; i++; continue; }
            if (depth == 0 && text[i] == '\n')
            {
                int next = i + 1; while (next < text.Length && text[next] is ' ' or '\t' or '\r') next++;
                if (next < text.Length && text[next] == '|' && Regex.IsMatch(text[next..].Split('\n')[0], @"^\|\s*\w+\s*=")) return text[from..i];
            }
        }
        return text[from..];
    }
}
