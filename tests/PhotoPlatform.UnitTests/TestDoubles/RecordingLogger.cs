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

// 測試用 Logger，保存每次 Log 的訊息、Exception、結構化欄位、EventId 與 LogLevel。
// 讓測試可依事件識別 Log，而非依賴 Log 筆數或固定位置。
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public sealed record Entry(string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Fields,
        EventId EventId, LogLevel Level);
    public List<Entry> Entries { get; } = [];
    public bool ThrowOnLog { get; set; }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (ThrowOnLog) throw new InvalidOperationException("Logger unavailable.");
        var fields = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(x => x.Key, x => x.Value);
        Entries.Add(new(formatter(state, exception), exception, fields, eventId, logLevel));
    }
}
