using System.Globalization;
using System.Text;

namespace PhotoPlatform.Application.Processing;

// 純格式工具，不驗證來源是否已持久凍結，也不宣稱任意宣告的欄位已通過業務契約審查。
// 實際 Step 必須使用核准的欄位宣告；此處不接受 Runtime Context 或任意 object 序列化。
public sealed class CanonicalInputSchema
{
    public const int SchemaVersion = 1;
    private readonly CanonicalField[] fields;

    public CanonicalInputSchema(params CanonicalField[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Any(field => field is null))
            throw new ArgumentException("Fields cannot be null.", nameof(fields));
        if (fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length)
            throw new ArgumentException("Field names must be unique.", nameof(fields));
        this.fields = fields.OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
    }

    public CanonicalInput Serialize(params CanonicalProperty[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var values = new Dictionary<string, CanonicalValue>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (property is null || property.Name is null || property.Value is null ||
                !values.TryAdd(property.Name, property.Value))
                throw new ArgumentException("Properties must have unique names and explicit values.", nameof(properties));
        }
        if (values.Count != fields.Length || fields.Any(field => !values.ContainsKey(field.Name)))
            throw new ArgumentException("Properties must match the declared fields exactly.", nameof(properties));
        foreach (var field in fields)
            Validate(field, values[field.Name]);

        var writer = new StringBuilder("{");
        // schemaVersion 和業務欄位一起按 ordinal 排序，不能特例放在最前面。
        var first = true;
        foreach (var name in values.Keys.Append("schemaVersion").OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!first)
                writer.Append(',');
            first = false;
            CanonicalText.AppendJsonString(writer, name);
            writer.Append(':');
            if (name == "schemaVersion")
                writer.Append(SchemaVersion.ToString(CultureInfo.InvariantCulture));
            else
                WriteValue(writer, values[name]);
        }
        writer.Append('}');
        return new CanonicalInput(Encoding.UTF8.GetBytes(writer.ToString()));
    }

    private static void Validate(CanonicalField field, CanonicalValue value)
    {
        if (value.Kind == CanonicalValueKind.Null)
        {
            if (!field.Nullable)
                throw new ArgumentException("The declared field does not allow null.");
            return;
        }
        if (field.Kind != value.Kind)
            throw new ArgumentException("Value type does not match the declared field.");
        if (value.Kind == CanonicalValueKind.Array)
        {
            foreach (var item in (CanonicalValue[])value.Value!)
                if (item.Kind == CanonicalValueKind.Null ? !field.NullableElements : item.Kind != field.ElementKind)
                    throw new ArgumentException("Array element does not match the declared type.");
        }
    }

    private static void WriteValue(StringBuilder writer, CanonicalValue value)
    {
        switch (value.Kind)
        {
            case CanonicalValueKind.Null:
                writer.Append("null");
                break;
            case CanonicalValueKind.String:
                CanonicalText.AppendJsonString(writer, (string)value.Value!);
                break;
            case CanonicalValueKind.Integer:
                writer.Append(((long)value.Value!).ToString(CultureInfo.InvariantCulture));
                break;
            case CanonicalValueKind.Decimal:
                // 去掉無意義的小數尾零，不採科學記法；相同 decimal 數值不因 scale 而產生不同 Hash。
                writer.Append(((decimal)value.Value!).ToString("0.############################", CultureInfo.InvariantCulture));
                break;
            case CanonicalValueKind.Boolean:
                writer.Append((bool)value.Value! ? "true" : "false");
                break;
            case CanonicalValueKind.UtcInstant:
                // 固定七位小數與 +00:00；相同 instant 的不同來源 offset 會得到相同 bytes。
                CanonicalText.AppendJsonString(writer, ((DateTimeOffset)value.Value!).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                break;
            case CanonicalValueKind.Array:
                writer.Append('[');
                var first = true;
                foreach (var item in (CanonicalValue[])value.Value!)
                {
                    if (!first)
                        writer.Append(',');
                    first = false;
                    WriteValue(writer, item);
                }
                writer.Append(']');
                break;
            default:
                throw new InvalidOperationException("Unsupported canonical value.");
        }
    }
}

// 只保存格式化完成的 bytes；回傳副本，避免計算 Hash 前後遭外部修改。
public sealed class CanonicalInput
{
    private readonly byte[] utf8Bytes;

    internal CanonicalInput(ReadOnlySpan<byte> utf8Bytes) => this.utf8Bytes = utf8Bytes.ToArray();

    public byte[] ToUtf8Bytes() => utf8Bytes.ToArray();
    internal ReadOnlySpan<byte> Bytes => utf8Bytes;
}
