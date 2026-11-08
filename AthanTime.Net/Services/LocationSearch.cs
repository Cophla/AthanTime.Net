using System.Globalization;
using System.Text;

namespace AthanTime.Net.Services;

// Shared with the offline builder so Unicode matching never depends on OS/SQLite collations.
internal static class LocationSearch
{
    internal static string Normalize(string text)
    {
        var decomposed = text.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
