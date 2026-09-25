// 測試用 Logger 替身：把程式寫出的 log 保存在記憶體，讓測試可以檢查內容。
/*
它主要用來驗證：
- 失敗時是否有記錄 log。
- FailureStage、BatchId 等結構化欄位是否正確。
- 是否意外傳入原始 exception 或敏感資訊。
- Logger 自己失敗時，是否影響補償流程。
*/
using Microsoft.Extensions.Logging;

namespace PhotoPlatform.UnitTests.TestDoubles;

// 同時保存結構化欄位、格式化輸出與 exception 參數，避免只測 rendered message 漏掉敏感資料。
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public sealed record Entry(string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Fields);
    public List<Entry> Entries { get; } = [];
    public bool ThrowOnLog { get; set; }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (ThrowOnLog) throw new InvalidOperationException("Logger unavailable.");
        var fields = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(x => x.Key, x => x.Value);
        Entries.Add(new(formatter(state, exception), exception, fields));
    }
}
