using System.Security.Cryptography;

namespace PhotoPlatform.Application.Processing;

// 只雜湊 Canonical 業務輸入；原圖 ContentVersion 屬於 Reader 的責任，兩者不能互換。
public static class InputFingerprint
{
    public static string Compute(CanonicalInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Convert.ToHexStringLower(SHA256.HashData(input.Bytes));
    }
}
