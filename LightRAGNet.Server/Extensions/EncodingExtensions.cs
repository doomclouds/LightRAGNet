using System.Text;
using Ude;

namespace LightRAGNet.Server.Extensions;

/// <summary>
/// Encoding detection result
/// </summary>
public class EncodingDetectionResult
{
    /// <summary>
    /// Detected encoding
    /// </summary>
    public Encoding DetectedEncoding { get; set; } = null!;

    /// <summary>
    /// Decoded text content
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Detected charset name
    /// </summary>
    public string? Charset { get; set; }

    /// <summary>
    /// Detection confidence (0-1)
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Encoding-related extension methods
/// </summary>
public static class EncodingExtensions
{
    /// <summary>
    /// Use Ude.NetStandard to detect file encoding and decode content
    /// </summary>
    /// <param name="fileBytes">File byte array</param>
    /// <returns>Encoding detection result, containing detected encoding and decoded content</returns>
    public static EncodingDetectionResult DetectEncodingAndDecode(this byte[] fileBytes)
    {
        var detector = new CharsetDetector();
        detector.Feed(fileBytes, 0, fileBytes.Length);
        detector.DataEnd();

        Encoding detectedEncoding;
        string content;

        if (detector.Charset != null)
        {
            try
            {
                // Convert detected charset name to Encoding
                var charsetName = detector.Charset.ToUpperInvariant();

                // Handle common charset name mappings
                detectedEncoding = MapCharsetToEncoding(charsetName);

                // Read content using detected encoding
                content = detectedEncoding.GetString(fileBytes);
            }
            catch (Exception)
            {
                // If detected encoding read fails, try UTF-8
                detectedEncoding = Encoding.UTF8;
                content = Encoding.UTF8.GetString(fileBytes);
            }
        }
        else
        {
            // If encoding cannot be detected, use fallback strategy
            var fallbackResult = TryFallbackEncoding(fileBytes);
            detectedEncoding = fallbackResult.DetectedEncoding;
            content = fallbackResult.Content;
        }

        return new EncodingDetectionResult
        {
            DetectedEncoding = detectedEncoding,
            Content = content,
            Charset = detector.Charset,
            Confidence = detector.Charset != null ? detector.Confidence : 0
        };
    }

    /// <summary>
    /// Map charset name to .NET Encoding object
    /// </summary>
    /// <param name="charsetName">Charset name</param>
    /// <returns>Corresponding Encoding object</returns>
    private static Encoding MapCharsetToEncoding(string charsetName)
    {
        return charsetName switch
        {
            "UTF-8" => new UTF8Encoding(false), // Without BOM
            "UTF-16LE" => Encoding.Unicode,
            "UTF-16BE" => Encoding.BigEndianUnicode,
            "GB18030" or "GB2312" or "GBK" => Encoding.GetEncoding("GBK"),
            "BIG5" => Encoding.GetEncoding("Big5"),
            "WINDOWS-1252" or "ISO-8859-1" => Encoding.GetEncoding("Windows-1252"),
            _ => TryGetEncodingByName(charsetName)
        };
    }

    /// <summary>
    /// Try to get Encoding by charset name
    /// </summary>
    /// <param name="charsetName">Charset name</param>
    /// <returns>Encoding object, returns UTF-8 if failed</returns>
    private static Encoding TryGetEncodingByName(string charsetName)
    {
        try
        {
            return Encoding.GetEncoding(charsetName);
        }
        catch
        {
            // If unrecognized, use UTF-8 as default
            return Encoding.UTF8;
        }
    }

    /// <summary>
    /// Try to detect encoding using fallback strategy (when Ude cannot detect)
    /// </summary>
    /// <param name="fileBytes">File byte array</param>
    /// <returns>Encoding detection result</returns>
    private static EncodingDetectionResult TryFallbackEncoding(byte[] fileBytes)
    {
        Encoding detectedEncoding;
        string content;

        // First check if there is UTF-8 BOM
        if (fileBytes is [0xEF, 0xBB, 0xBF, ..])
        {
            // UTF-8 with BOM
            detectedEncoding = new UTF8Encoding(true);
            content = Encoding.UTF8.GetString(fileBytes, 3, fileBytes.Length - 3);
        }
        else
        {
            // Try UTF-8 (without BOM)
            try
            {
                var decoder = Encoding.UTF8.GetDecoder();
                decoder.Fallback = new DecoderExceptionFallback();
                var charCount = decoder.GetCharCount(fileBytes, 0, fileBytes.Length);
                var chars = new char[charCount];
                decoder.GetChars(fileBytes, 0, fileBytes.Length, chars, 0);
                content = new string(chars);
                detectedEncoding = Encoding.UTF8;
            }
            catch (DecoderFallbackException)
            {
                // UTF-8 decoding failed, try GBK (common Chinese encoding)
                try
                {
                    detectedEncoding = Encoding.GetEncoding("GBK");
                    content = detectedEncoding.GetString(fileBytes);
                }
                catch
                {
                    // Finally try system default encoding
                    detectedEncoding = Encoding.Default;
                    content = Encoding.Default.GetString(fileBytes);
                }
            }
        }

        return new EncodingDetectionResult
        {
            DetectedEncoding = detectedEncoding,
            Content = content,
            Charset = null,
            Confidence = 0
        };
    }
}
