using System.Text.RegularExpressions;

namespace LightRAGNet.Core.Utils;

public static class TextUtils
{
    /// <summary>
    /// Sanitize and normalize extracted text
    /// </summary>
    public static string SanitizeAndNormalizeText(string text, bool removeInnerQuotes = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        
        // Remove surrogate pair characters
        text = Regex.Replace(text, @"[\uD800-\uDFFF\uFFFE\uFFFF]", "");
        
        // Normalize whitespace characters
        text = Regex.Replace(text, @"\s+", " ");
        
        // Remove quotes (if specified)
        if (removeInnerQuotes)
        {
            text = text.Replace("\"", "").Replace("'", "");
        }
        
        return text.Trim();
    }
    
    /// <summary>
    /// Sanitize text for encoding
    /// </summary>
    public static string SanitizeTextForEncoding(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        
        // Remove surrogate pair characters
        return Regex.Replace(text, @"[\uD800-\uDFFF\uFFFE\uFFFF]", "");
    }
    
    /// <summary>
    /// Get content summary (first 100 characters)
    /// </summary>
    public static string GetContentSummary(string content, int maxLength = 100)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;
        
        return content.Length <= maxLength 
            ? content 
            : content[..maxLength] + "...";
    }
}

