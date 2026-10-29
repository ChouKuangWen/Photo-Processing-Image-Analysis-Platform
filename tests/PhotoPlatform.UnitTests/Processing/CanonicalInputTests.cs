using System.Globalization;
using System.Text;
using System.Text.Json;
using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class CanonicalInputTests
{
    // 測試目的：逐 byte 鎖住已核准 ASCII-only v1 的輸出，不只確認 JSON 能讀回原字串。
    // 測試情境：依序使用非 BMP emoji、全形空白、控制字元、預組字與組合字；預期 JSON 及 Hash 由獨立 PowerShell SHA256 計算後固定。
    // 預期結果：UTF-8 bytes 與 Hash 完全相符；此處不代表已核准逐 Step Schema 或解除 F-04-01。
    [Theory]
    [InlineData("\U0001F600", "{\"schemaVersion\":1,\"text\":\"\\ud83d\\ude00\"}", "135854809241bd24e52322a34f773585098d4f214f9342ac4a870962b882384a")]
    [InlineData("\u3000", "{\"schemaVersion\":1,\"text\":\"\\u3000\"}", "a831e9f42675d16542ddea061576eeca8e56edd5ebcf57d66dfb60a6480e7144")]
    [InlineData("\u0000\b\t\n\f\r\u001f", "{\"schemaVersion\":1,\"text\":\"\\u0000\\u0008\\u0009\\u000a\\u000c\\u000d\\u001f\"}", "d004f37c37bd19e41e698bc89679c22c57cc61a5a3bef4fb21ba5e7c291c0f10")]
    [InlineData("\u00e9", "{\"schemaVersion\":1,\"text\":\"\\u00e9\"}", "76aab3c298c5ed289733d13f6a8f928b9e92a892378e82bfbf3eb87d0f483575")]
    [InlineData("e\u0301", "{\"schemaVersion\":1,\"text\":\"e\\u0301\"}", "76f284c881b6537ccafeb0e5aa004bc923df8c493c0c5b1243c7d09c612906e2")]
    public void Serialize_UnicodeEscaping_MatchesIndependentByteAndHashVectors(string text, string expectedJson, string expectedHash)
    {
        var schema = new CanonicalInputSchema(new CanonicalField("text", CanonicalValueKind.String));
        var input = schema.Serialize(new CanonicalProperty("text", CanonicalValue.String(text)));
        Assert.Equal(Encoding.UTF8.GetBytes(expectedJson), input.ToUtf8Bytes());
        Assert.Equal(expectedHash, InputFingerprint.Compute(input));
        using var document = JsonDocument.Parse(input.ToUtf8Bytes());
        Assert.Equal(text, document.RootElement.GetProperty("text").GetString());
    }

    // 這只是格式測試用的欄位，不是任何 Step 的 Input Schema。
    private const string GoldenJson = """{"amount":1.23,"at":"2026-10-29T00:00:00.0000000+00:00","enabled":true,"name":"\u7167\u7247","optional":null,"schemaVersion":1,"values":[2,1]}""";
    // 預期 Hash 由固定 JSON 的 UTF-8 bytes 以獨立工具計算後固定，不由被測序列化器產生。
    private const string GoldenHash = "bc9041b36a502313f7e20dda29193f454527b8d443de2b53e2956526a6ab94ff";

    // 測試目的：確認全部控制字元都使用六字元跳脫，名稱和值不能套用不同規則。
    // 測試情境：將 U+0000 到 U+001F 全部放進名稱和值，預期字串逐項固定，不由編碼器產生。
    // 預期結果：沒有短格式或原始控制字元；解析後仍只有預期的兩個欄位且內容完全相同。
    [Fact]
    public void Serialize_AllControlCharacters_UseFixedSixCharacterEscapes()
    {
        var text = "x" + new string(Enumerable.Range(0, 32).Select(code => (char)code).ToArray());
        const string escaped = @"x\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000a\u000b\u000c\u000d\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f";
        var schema = new CanonicalInputSchema(new CanonicalField(text, CanonicalValueKind.String));
        var bytes = schema.Serialize(new CanonicalProperty(text, CanonicalValue.String(text))).ToUtf8Bytes();
        Assert.Equal(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"" + escaped + "\":\"" + escaped + "\"}"), bytes);
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
        Assert.Equal(text, document.RootElement.GetProperty(text).GetString());
    }

    // 測試目的：確認可列印 ASCII 不會被 HTML 編碼，只有雙引號與反斜線需要跳脫。
    // 測試情境：一次涵蓋 U+0020 到 U+007E，包含引號、斜線、尖括號及其他標點。
    // 預期結果：輸出符合固定字串，讀回後每個字元都不變，也不產生額外 JSON 欄位。
    [Fact]
    public void Serialize_PrintableAscii_MatchesFixedEscaping()
    {
        var text = new string(Enumerable.Range(32, 95).Select(code => (char)code).ToArray());
        const string expected = """{"schemaVersion":1,"text":" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~"}""";
        var schema = new CanonicalInputSchema(new CanonicalField("text", CanonicalValueKind.String));
        var bytes = schema.Serialize(new CanonicalProperty("text", CanonicalValue.String(text))).ToUtf8Bytes();
        Assert.Equal(Encoding.UTF8.GetBytes(expected), bytes);
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(text, document.RootElement.GetProperty("text").GetString());
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
    }

    // 測試目的：確認名稱和值共用固定跳脫，不讓惡意字串變成 JSON 結構。
    // 測試情境：分別輸入 DEL 邊界、中文、emoji、全形空白及含引號反斜線的假欄位片段。
    // 預期結果：完整 bytes 符合固定表示；只有原本兩個欄位，名稱與值皆能原樣讀回。
    [Theory]
    [InlineData("x\u007f", @"x\u007f")]
    [InlineData("x照片", @"x\u7167\u7247")]
    [InlineData("x\U0001F600", @"x\ud83d\ude00")]
    [InlineData("x\u3000", @"x\u3000")]
    [InlineData("x\"},\"injected\":true,\\", """x\"},\"injected\":true,\\""")]
    public void Serialize_PropertyAndValue_UseIdenticalEscaping(string text, string escaped)
    {
        var schema = new CanonicalInputSchema(new CanonicalField(text, CanonicalValueKind.String));
        var bytes = schema.Serialize(new CanonicalProperty(text, CanonicalValue.String(text))).ToUtf8Bytes();
        Assert.Equal(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"" + escaped + "\":\"" + escaped + "\"}"), bytes);
        Assert.All(bytes, value => Assert.InRange((int)value, 0x20, 0x7e));
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
        Assert.Equal(text, document.RootElement.GetProperty(text).GetString());
    }

    // 測試目的：確認 Canonical bytes 與 Fingerprint 符合獨立固定的測試向量。
    // 測試情境：輸入順序刻意打亂，包含 decimal、UTC、中文、null 和有序陣列。
    // 預期結果：精確符合固定 UTF-8 bytes 與 64 字元小寫 Hash，沒有 BOM 或排版空白。
    [Fact]
    public void Serialize_AndFingerprint_MatchIndependentGoldenVector()
    {
        var input = GoldenInput();
        Assert.Equal(Encoding.UTF8.GetBytes(GoldenJson), input.ToUtf8Bytes());
        Assert.Equal(GoldenHash, InputFingerprint.Compute(input));
        Assert.Matches("^[0-9a-f]{64}$", InputFingerprint.Compute(input));
        Assert.Equal((byte)'{', input.ToUtf8Bytes()[0]);
        Assert.Throws<ArgumentNullException>(() => InputFingerprint.Compute(null!));
    }

    // 測試目的：確認不同語系不會改變小數點、日期、屬性排序或 Hash。
    // 測試情境：在繁中、英文、法文及土耳其文化下輸出同一份資料。
    // 預期結果：每種文化都與固定向量完全相同，並於測試後還原文化設定。
    [Theory]
    [InlineData("zh-TW")]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void Serialize_IsCultureIndependent(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(GoldenJson, Encoding.UTF8.GetString(GoldenInput().ToUtf8Bytes()));
            Assert.Equal(GoldenHash, InputFingerprint.Compute(GoldenInput()));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    // 測試目的：確認排序使用 ordinal，而不是語言排序或呼叫端的宣告順序。
    // 測試情境：混合大小寫、Unicode 名稱，反轉欄位宣告與值的輸入順序。
    // 預期結果：兩者都得到完全相同的固定順序，schemaVersion 也參與排序。
    [Fact]
    public void Serialize_OrdersPropertiesOrdinallyIncludingSchemaVersion()
    {
        var names = new[] { "é", "z", "a", "Z", "A" };
        var fields = names.Select(name => new CanonicalField(name, CanonicalValueKind.Integer)).ToArray();
        var values = names.Select(name => new CanonicalProperty(name, CanonicalValue.Integer(1))).ToArray();
        var first = new CanonicalInputSchema(fields).Serialize(values);
        var second = new CanonicalInputSchema(fields.Reverse().ToArray()).Serialize(values.Reverse().ToArray());
        Assert.Equal("""{"A":1,"Z":1,"a":1,"schemaVersion":1,"z":1,"\u00e9":1}""", Encoding.UTF8.GetString(first.ToUtf8Bytes()));
        Assert.Equal(first.ToUtf8Bytes(), second.ToUtf8Bytes());
    }

    // 測試目的：確認 null、空字串與 Unicode 原值有不同且正確的 JSON 表示。
    // 測試情境：包含引號、反斜線、換行、控制字元、HTML 字元與組合字。
    // 預期結果：可無損解析回原值；不做 Unicode 正規化，也不把 null 當成省略欄位。
    [Fact]
    public void Serialize_PreservesUnicodeAndEscapesJsonWithoutNormalizing()
    {
        var schema = new CanonicalInputSchema(new CanonicalField("text", CanonicalValueKind.String, true));
        var text = "照片😀\"\\\n\t\u0001<>&é";
        var input = schema.Serialize(new CanonicalProperty("text", CanonicalValue.String(text)));
        var expected = """{"schemaVersion":1,"text":"\u7167\u7247\ud83d\ude00\"\\\u000a\u0009\u0001<>&\u00e9"}""";
        Assert.Equal(Encoding.UTF8.GetBytes(expected), input.ToUtf8Bytes());
        using var document = JsonDocument.Parse(input.ToUtf8Bytes());
        Assert.Equal(text, document.RootElement.GetProperty("text").GetString());
        var composed = schema.Serialize(new CanonicalProperty("text", CanonicalValue.String("é")));
        var decomposed = schema.Serialize(new CanonicalProperty("text", CanonicalValue.String("e\u0301")));
        Assert.NotEqual(InputFingerprint.Compute(composed), InputFingerprint.Compute(decomposed));
        var explicitNull = schema.Serialize(new CanonicalProperty("text", CanonicalValue.Null));
        Assert.Equal("""{"schemaVersion":1,"text":null}""", Encoding.UTF8.GetString(explicitNull.ToUtf8Bytes()));
        Assert.NotEqual(InputFingerprint.Compute(explicitNull), InputFingerprint.Compute(schema.Serialize(new CanonicalProperty("text", CanonicalValue.String("")))));
    }

    // 測試目的：確認非法 Unicode 不會被默默替換成另一個合法字元。
    // 測試情境：提供缺少配對的 UTF-16 高、低代理字元，分別作為名稱及值。
    // 預期結果：立即拒絕，避免不同的壞資料輸出相同 Fingerprint。
    [Fact]
    public void Serialize_RejectsUnpairedSurrogates()
    {
        foreach (var text in new[] { "\ud800", "\udc00", "\ud800x", "\udc00\ud800", "\ud800\ud800", "\udc00\udc00" })
        {
            Assert.Throws<EncoderFallbackException>(() => CanonicalValue.String(text));
            Assert.Throws<EncoderFallbackException>(() => new CanonicalField(text, CanonicalValueKind.String));
        }
    }

    // 測試目的：確認同一時間與同一 decimal 數值不因表達方式不同而換 Hash。
    // 測試情境：使用 +08:00 與 UTC、1.2300 與 1.23，以及 decimal 的極小與最大值。
    // 預期結果：等值輸入相同，邊界值保持精度，永遠不使用科學記法。
    [Fact]
    public void Serialize_NormalizesUtcAndDecimalScaleWithoutLosingPrecision()
    {
        var schema = new CanonicalInputSchema(new("at", CanonicalValueKind.UtcInstant), new("value", CanonicalValueKind.Decimal));
        var local = new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.FromHours(8)).AddTicks(1);
        var first = schema.Serialize(new("at", CanonicalValue.UtcInstant(local)), new("value", CanonicalValue.Decimal(1.2300m)));
        var second = schema.Serialize(new("value", CanonicalValue.Decimal(1.23m)), new("at", CanonicalValue.UtcInstant(local.ToUniversalTime())));
        Assert.Equal(first.ToUtf8Bytes(), second.ToUtf8Bytes());
        Assert.Equal("""{"at":"2026-10-29T00:00:00.0000001+00:00","schemaVersion":1,"value":1.23}""", Encoding.UTF8.GetString(first.ToUtf8Bytes()));
        var numeric = new CanonicalInputSchema(new CanonicalField("value", CanonicalValueKind.Decimal));
        foreach (var pair in new[]
        {
            (0.0000000000000000000000000001m, "0.0000000000000000000000000001"),
            (decimal.MaxValue, "79228162514264337593543950335"),
            (decimal.MinValue, "-79228162514264337593543950335"),
            (new decimal(0, 0, 0, true, 4), "0")
        })
            Assert.Equal("{\"schemaVersion\":1,\"value\":" + pair.Item2 + "}",
                Encoding.UTF8.GetString(numeric.Serialize(new CanonicalProperty("value", CanonicalValue.Decimal(pair.Item1))).ToUtf8Bytes()));
    }

    // 測試目的：確認陣列保持業務順序，且建立後不受呼叫端修改來源陣列影響。
    // 測試情境：先保存 2、null、1，再修改原陣列，另建立相反順序的輸入。
    // 預期結果：原值不變，順序不同 Hash 不同，輸出的 bytes 副本也不能改壞原輸入。
    [Fact]
    public void Serialize_PreservesArrayOrderAndDefendsAgainstMutation()
    {
        var fields = new[] { new CanonicalField("values", CanonicalValueKind.Array, elementKind: CanonicalValueKind.Integer, nullableElements: true) };
        var schema = new CanonicalInputSchema(fields);
        fields[0] = new("other", CanonicalValueKind.String);
        var original = new[] { CanonicalValue.Integer(2), CanonicalValue.Null, CanonicalValue.Integer(1) };
        var array = CanonicalValue.Array(original);
        original[0] = CanonicalValue.Integer(99);
        var input = schema.Serialize(new CanonicalProperty("values", array));
        Assert.Equal("""{"schemaVersion":1,"values":[2,null,1]}""", Encoding.UTF8.GetString(input.ToUtf8Bytes()));
        var reversed = schema.Serialize(new CanonicalProperty("values", CanonicalValue.Array(CanonicalValue.Integer(1), CanonicalValue.Null, CanonicalValue.Integer(2))));
        Assert.NotEqual(InputFingerprint.Compute(input), InputFingerprint.Compute(reversed));
        var hash = InputFingerprint.Compute(input);
        Array.Fill(input.ToUtf8Bytes(), (byte)0);
        Assert.Equal(hash, InputFingerprint.Compute(input));
    }

    // 測試目的：確認輸入嚴格符合已宣告欄位，不能省略、增加、重複或偷偷轉型。
    // 測試情境：缺欄位、額外 Runtime 欄位、重複名稱、錯型別與未允許的 null。
    // 預期結果：全部拒絕；沒有任意 object 或 Dictionary 的業務輸入入口。
    [Fact]
    public void Serialize_RejectsUndeclaredMissingDuplicateAndMistypedValues()
    {
        var schema = new CanonicalInputSchema(new CanonicalField("imageId", CanonicalValueKind.Integer));
        Assert.Throws<ArgumentException>(() => schema.Serialize());
        Assert.Throws<ArgumentException>(() => schema.Serialize(new("imageId", CanonicalValue.Integer(1)), new("traceId", CanonicalValue.String("trace"))));
        Assert.Throws<ArgumentException>(() => schema.Serialize(new("imageId", CanonicalValue.Integer(1)), new("imageId", CanonicalValue.Integer(2))));
        Assert.Throws<ArgumentException>(() => schema.Serialize(new CanonicalProperty("imageId", CanonicalValue.String("1"))));
        Assert.Throws<ArgumentException>(() => schema.Serialize(new CanonicalProperty("imageId", CanonicalValue.Null)));
        Assert.Throws<ArgumentException>(() => schema.Serialize(new CanonicalProperty("ImageId", CanonicalValue.Integer(1))));
        Assert.Throws<ArgumentException>(() => schema.Serialize(new CanonicalProperty("imageId", null!)));
    }

    // 測試目的：確認宣告本身不能模糊化型別或覆寫固定 schemaVersion。
    // 測試情境：重複欄位、未知型別、保留名稱，以及缺少元素型別的陣列。
    // 預期結果：在建立格式宣告時拒絕，不留到寫入後才發現問題。
    [Fact]
    public void Schema_RejectsInvalidDeclarations()
    {
        Assert.Throws<ArgumentException>(() => new CanonicalInputSchema(new("same", CanonicalValueKind.String), new("same", CanonicalValueKind.Integer)));
        Assert.Throws<ArgumentException>(() => new CanonicalField("schemaVersion", CanonicalValueKind.Integer));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CanonicalField("value", (CanonicalValueKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CanonicalField("value", CanonicalValueKind.Null));
        Assert.Throws<ArgumentException>(() => new CanonicalField("values", CanonicalValueKind.Array));
        Assert.Throws<ArgumentException>(() => new CanonicalField("values", CanonicalValueKind.Array, elementKind: CanonicalValueKind.Array));
        Assert.Throws<ArgumentException>(() => new CanonicalField("value", CanonicalValueKind.String, nullableElements: true));
        Assert.Throws<ArgumentException>(() => new CanonicalInputSchema((CanonicalField)null!));
    }

    // 測試目的：確認陣列不會接受混合型別或未經允許的空元素。
    // 測試情境：整數陣列送入字串、null 或巢狀陣列，並對照合法空陣列。
    // 預期結果：非法內容拒絕，合法空陣列明確輸出 []，不是 null。
    [Fact]
    public void Array_EnforcesElementTypesAndExplicitNullRules()
    {
        var schema = new CanonicalInputSchema(new CanonicalField("values", CanonicalValueKind.Array, true, CanonicalValueKind.Integer));
        foreach (var value in new[] { CanonicalValue.String("1"), CanonicalValue.Null, CanonicalValue.Array() })
            Assert.Throws<ArgumentException>(() => schema.Serialize(new CanonicalProperty("values", CanonicalValue.Array(value))));
        Assert.Throws<ArgumentException>(() => CanonicalValue.Array((CanonicalValue)null!));
        Assert.Equal("""{"schemaVersion":1,"values":[]}""", Encoding.UTF8.GetString(schema.Serialize(new CanonicalProperty("values", CanonicalValue.Array())).ToUtf8Bytes()));
        Assert.Equal("""{"schemaVersion":1,"values":null}""", Encoding.UTF8.GetString(schema.Serialize(new CanonicalProperty("values", CanonicalValue.Null)).ToUtf8Bytes()));
    }

    // 測試目的：確認 Fingerprint 只依已宣告的業務資料，不依執行次數或追蹤資料。
    // 測試情境：改變外部 Runtime 資料但沿用相同業務值，再改 ImageId 作對照。
    // 預期結果：前者 Hash 不變、後者改變；將 Runtime 欄位塞進輸入會被拒絕。
    [Fact]
    public void Fingerprint_SeparatesDeclaredBusinessInputFromRuntimeData()
    {
        var schema = new CanonicalInputSchema(new CanonicalField("imageId", CanonicalValueKind.Integer));
        var hashes = new List<string>();
        foreach (var attempt in new[] { 1, 2 })
        {
            var business = schema.Serialize(new CanonicalProperty("imageId", CanonicalValue.Integer(7)));
            hashes.Add(InputFingerprint.Compute(business));
            Assert.Throws<ArgumentException>(() => schema.Serialize(new("imageId", CanonicalValue.Integer(7)), new("attemptNo", CanonicalValue.Integer(attempt))));
            Assert.Throws<ArgumentException>(() => schema.Serialize(new("imageId", CanonicalValue.Integer(7)), new("resumeNo", CanonicalValue.Integer(attempt - 1))));
        }
        Assert.Equal(hashes[0], hashes[1]);
        Assert.NotEqual(hashes[0], InputFingerprint.Compute(schema.Serialize(new CanonicalProperty("imageId", CanonicalValue.Integer(8)))));
    }

    // 測試目的：確認整數不會經浮點數轉換失去精度，且 false 不會被省略。
    // 測試情境：同時輸入 Int64 的最大值、最小值與 false。
    // 預期結果：固定 JSON 保留全部整數位數與布林值。
    [Fact]
    public void Serialize_PreservesIntegerBoundariesAndFalse()
    {
        var schema = new CanonicalInputSchema(new("max", CanonicalValueKind.Integer),
            new("min", CanonicalValueKind.Integer), new("enabled", CanonicalValueKind.Boolean));
        var input = schema.Serialize(new("max", CanonicalValue.Integer(long.MaxValue)),
            new("min", CanonicalValue.Integer(long.MinValue)), new("enabled", CanonicalValue.Boolean(false)));
        Assert.Equal("""{"enabled":false,"max":9223372036854775807,"min":-9223372036854775808,"schemaVersion":1}""",
            Encoding.UTF8.GetString(input.ToUtf8Bytes()));
    }

    // 測試目的：確認內容版本、規則版本與來源識別都是業務輸入，不能忽略。
    // 測試情境：在格式測試專用宣告中一次只改一個值，不建立任何真實 Step Schema。
    // 預期結果：每次改動都改變 Fingerprint；格式工具不會把這些欄位當成 Runtime 資料排除。
    [Fact]
    public void Fingerprint_ChangesWithDeclaredContentRuleAndReference()
    {
        var schema = new CanonicalInputSchema(new("contentVersion", CanonicalValueKind.String),
            new("ruleVersion", CanonicalValueKind.String), new("inputReference", CanonicalValueKind.String));
        string Hash(string content, string rule, string reference) => InputFingerprint.Compute(schema.Serialize(
            new("contentVersion", CanonicalValue.String(content)), new("ruleVersion", CanonicalValue.String(rule)),
            new("inputReference", CanonicalValue.String(reference))));
        var original = Hash(new string('a', 64), "test-rule-v1", "test-input-1");
        Assert.NotEqual(original, Hash(new string('b', 64), "test-rule-v1", "test-input-1"));
        Assert.NotEqual(original, Hash(new string('a', 64), "test-rule-v2", "test-input-1"));
        Assert.NotEqual(original, Hash(new string('a', 64), "test-rule-v1", "test-input-2"));
    }

    private static CanonicalInput GoldenInput()
    {
        var schema = new CanonicalInputSchema(
            new("values", CanonicalValueKind.Array, elementKind: CanonicalValueKind.Integer),
            new("optional", CanonicalValueKind.String, true),
            new("name", CanonicalValueKind.String), new("enabled", CanonicalValueKind.Boolean),
            new("at", CanonicalValueKind.UtcInstant), new("amount", CanonicalValueKind.Decimal));
        return schema.Serialize(new("values", CanonicalValue.Array(CanonicalValue.Integer(2), CanonicalValue.Integer(1))),
            new("optional", CanonicalValue.Null), new("name", CanonicalValue.String("照片")),
            new("enabled", CanonicalValue.Boolean(true)),
            new("at", CanonicalValue.UtcInstant(new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.FromHours(8)))),
            new("amount", CanonicalValue.Decimal(1.2300m)));
    }
}
