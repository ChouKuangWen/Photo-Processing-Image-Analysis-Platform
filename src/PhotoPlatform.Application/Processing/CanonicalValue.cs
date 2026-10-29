using System.Text;

namespace PhotoPlatform.Application.Processing;

// 這些是 JSON 格式型別，不是 Processing 狀態，也不是任何 Step 的業務 Schema。
public enum CanonicalValueKind
{
    Null,
    String,
    Integer,
    Decimal,
    Boolean,
    UtcInstant,
    Array
}

// 不接受任意 object 或現成 JSON；呼叫端必須明確選用支援的型別。
public sealed class CanonicalValue
{
    private CanonicalValue(CanonicalValueKind kind, object? value)
    {
        Kind = kind;
        Value = value;
    }

    public CanonicalValueKind Kind { get; }
    internal object? Value { get; }
    public static CanonicalValue Null { get; } = new(CanonicalValueKind.Null, null);

    public static CanonicalValue String(string value)
    {
        CanonicalText.Validate(value);
        return new(CanonicalValueKind.String, value);
    }

    public static CanonicalValue Integer(long value) => new(CanonicalValueKind.Integer, value);
    public static CanonicalValue Decimal(decimal value) => new(CanonicalValueKind.Decimal, value);
    public static CanonicalValue Boolean(bool value) => new(CanonicalValueKind.Boolean, value);
    public static CanonicalValue UtcInstant(DateTimeOffset value) => new(CanonicalValueKind.UtcInstant, value);

    public static CanonicalValue Array(params CanonicalValue[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Any(value => value is null))
            throw new ArgumentException("Use an explicit canonical null.", nameof(values));
        // 複製陣列，避免呼叫端稍後改資料而改變同一份輸入的 Fingerprint。
        return new(CanonicalValueKind.Array, values.ToArray());
    }
}

internal static class CanonicalText
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    // Canonical v1 首次使用前核定的固定規則；未來改格式必須另行核准可區分的版本。
    // 名稱、字串值和 UTC 共用此處，不依賴 .NET Encoder 的 Unicode 清單。
    // 只保留可列印 ASCII；控制字元及非 ASCII 一律用小寫四位 UTF-16 跳脫。
    // 不接受原始 JSON，也不可將輸出直接嵌入 HTML 或 JavaScript。
    internal static void AppendJsonString(StringBuilder output, string value)
    {
        Validate(value);
        output.Append('"');
        foreach (var character in value)
        {
            if (character is '"' or '\\')
                output.Append('\\').Append(character);
            else if (character >= '\u0020' && character <= '\u007e')
                output.Append(character);
            else
            {
                // 合法代理對依原本兩個 UTF-16 單位輸出；不正規化或改變字元內容。
                const string hex = "0123456789abcdef";
                output.Append("\\u")
                    .Append(hex[(character >> 12) & 15])
                    .Append(hex[(character >> 8) & 15])
                    .Append(hex[(character >> 4) & 15])
                    .Append(hex[character & 15]);
            }
        }
        output.Append('"');
    }

    public static void Validate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // 拒絕不完整的 UTF-16，不能讓序列化器默默用替代字元把不同輸入變成相同 bytes。
        StrictUtf8.GetByteCount(value);
    }
}
