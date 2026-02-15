namespace OmniRecall.Api.Services;

internal static class TextSnippetHelper
{
    public static string BuildSnippet(string content, int maxLength)
    {
        var normalized = content.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (normalized.Length <= maxLength)
            return normalized;
        return normalized[..maxLength] + "...";
    }
}
