using System.Security.Cryptography;
using System.Text;

namespace LightRAGNet.Core.Utils;

public static class HashUtils
{
    /// <summary>
    /// Compute MD5 hash ID
    /// </summary>
    public static string ComputeMd5Hash(string content, string prefix = "")
    {
        var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(content));
        var hash = Convert.ToHexStringLower(hashBytes);
        return string.IsNullOrEmpty(prefix) ? hash : $"{prefix}-{hash}";
    }

    /// <summary>
    /// Compute hash value for parameters (for caching)
    /// </summary>
    public static string ComputeArgsHash(params object[] args)
    {
        var combined = string.Join("|", args.Select(a => a.ToString() ?? ""));
        return ComputeMd5Hash(combined);
    }
    
    /// <summary>
    /// Generate UUID format Point ID for Qdrant (consistent with Python version)
    /// Reference: Python version compute_mdhash_id_for_qdrant function
    /// </summary>
    /// <param name="content">Original content</param>
    /// <param name="prefix">Prefix (usually workspace)</param>
    /// <param name="style">UUID format: "simple" (hex), "hyphenated" (standard format), "urn" (URN format)</param>
    /// <returns>UUID string</returns>
    public static string ComputeQdrantPointId(string content, string prefix = "", string style = "simple")
    {
        if (string.IsNullOrEmpty(content))
        {
            throw new ArgumentException("Content must not be empty.", nameof(content));
        }

        // Use SHA256 hash (consistent with Python version)
        var inputBytes = Encoding.UTF8.GetBytes(prefix + content);
        var hashBytes = SHA256.HashData(inputBytes);

        // Use first 16 bytes to create UUID (version 4)
        var uuidBytes = new byte[16];
        Array.Copy(hashBytes, 0, uuidBytes, 0, 16);

        // Set UUID version and variant bits
        uuidBytes[6] = (byte)((uuidBytes[6] & 0x0F) | 0x40); // Version 4
        uuidBytes[8] = (byte)((uuidBytes[8] & 0x3F) | 0x80); // Variant 10

        var guid = new Guid(uuidBytes);

        return style switch
        {
            "simple" => guid.ToString("N"), // Hex format, no hyphens
            "hyphenated" => guid.ToString("D"), // Standard format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
            "urn" => $"urn:uuid:{guid}", // URN format
            _ => throw new ArgumentException("Invalid style. Choose from 'simple', 'hyphenated', or 'urn'.", nameof(style))
        };
    }
}

