namespace PhotoPlatform.Application.Processing;

// 每個格式欄位先宣告型別與 null 規則，再交付值；逐 Step 欄位清單仍須另行核准。
public sealed class CanonicalField
{
    public CanonicalField(string name, CanonicalValueKind kind, bool nullable = false,
        CanonicalValueKind? elementKind = null, bool nullableElements = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CanonicalText.Validate(name);
        if (name == "schemaVersion")
            throw new ArgumentException("Schema version is supplied by the format.", nameof(name));
        if (!Enum.IsDefined(kind) || kind == CanonicalValueKind.Null)
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == CanonicalValueKind.Array)
        {
            // Phase A 只提供明確型別的 scalar 陣列，不藉通用巢狀 object 預先定義模組 Payload。
            if (elementKind is null || !Enum.IsDefined(elementKind.Value) ||
                elementKind is CanonicalValueKind.Null or CanonicalValueKind.Array)
                throw new ArgumentException("An array requires a scalar element type.", nameof(elementKind));
        }
        else if (elementKind is not null || nullableElements)
            throw new ArgumentException("Element rules only apply to arrays.", nameof(elementKind));

        Name = name;
        Kind = kind;
        Nullable = nullable;
        ElementKind = elementKind;
        NullableElements = nullableElements;
    }

    public string Name { get; }
    public CanonicalValueKind Kind { get; }
    public bool Nullable { get; }
    public CanonicalValueKind? ElementKind { get; }
    public bool NullableElements { get; }
}

public sealed record CanonicalProperty(string Name, CanonicalValue Value);
