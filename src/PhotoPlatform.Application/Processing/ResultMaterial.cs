namespace PhotoPlatform.Application.Processing;

// 區分待保存資料、待核對引用與不新增結果；任何分支都不代表 SQL 已提交。
public abstract class ResultMaterial<TPayload> where TPayload : notnull
{
    private protected ResultMaterial() { }

    public sealed class Prepared : ResultMaterial<TPayload>
    {
        public Prepared(TPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            Payload = payload;
        }

        public TPayload Payload { get; }
    }

    public sealed class ExistingCandidate : ResultMaterial<TPayload>
    {
        public ExistingCandidate(string resultReference)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resultReference);
            if (resultReference.Length > 500)
                throw new ArgumentOutOfRangeException(nameof(resultReference));
            if (resultReference.Any(character => character < '!' || character > '~'))
                throw new ArgumentException("A non-whitespace printable ASCII reference is required.", nameof(resultReference));
            // 只檢查共通文字邊界；受控 table/id/version 的解析、來源與已提交狀態仍須由具體 policy 核對。
            // 此字串不是檔案路徑，不可拿去開檔或直接當成重用結果的許可。
            ResultReference = resultReference;
        }

        public string ResultReference { get; }
    }

    // 只有具體 Step policy 允許時才能用於完成；不能藉此略過必要的結果寫入。
    public sealed class NoNewResult : ResultMaterial<TPayload>
    {
        public NoNewResult() { }
    }
}
