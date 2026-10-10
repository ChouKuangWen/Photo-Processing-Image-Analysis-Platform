using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PhotoPlatform.IntegrationTests.TestDoubles;

// TASK-12 專用的 test-only Logger Provider。
// 不把 Log 輸出到 Console，而是把正式程式產生的 Log 收集到記憶體中，
// 讓 Acceptance Test 可以驗證 EventId、Message、Exception、structured fields、TraceId 與敏感資訊。
// 每個 fixture 使用自己的 instance，並以 thread-safe collection 保存 concurrent request 產生的 Log。
internal sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    // 代表一筆被測試捕捉到的 Log。
    // Category = 哪個 Logger 產生
    // EventId = 事件編號
    // Message = 格式化後的 Log 文字
    // Exception = 這筆 Log 帶出的 Exception
    // Fields = structured logging 欄位
    // Scope = Request scope 資料，例如 TraceId
    internal sealed record Entry(string Category, EventId EventId, string Message, Exception? Exception,
        IReadOnlyDictionary<string, object?> Fields, IReadOnlyDictionary<string, object?> Scope);
    // 保存所有捕捉到的 Log。
    // 使用 ConcurrentQueue，讓多個 async / concurrent request 同時寫入時仍保持 thread-safe。
    public ConcurrentQueue<Entry> Entries { get; } = new();
    // 保存 ASP.NET Core Logging Scope。
    // Scope 通常包含 TraceId / RequestId 等 request-level context，
    // Acceptance Test 會用它確認 HTTP response 與 Log 是否屬於同一次 Request。
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
    // 由 ASP.NET Core Logging 系統提供目前使用中的 Scope Provider，
    // 讓這個測試 Logger 能讀取正式 Request 的 Scope 資料。
    public void SetScopeProvider(IExternalScopeProvider provider) => scopes = provider;
    // 每個 Logger category 建立一個 Capture instance。
    // Capture 才是真正接收 ILogger.Log(...) 呼叫的測試 Logger。
    public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);
    // 此 Provider 沒有額外 unmanaged resource，因此不需要實際 cleanup。
    public void Dispose() { }

    // 真正實作 ILogger 的測試 Logger。
    // 通過 Logging 系統篩選並送到此 Provider 的 Log 呼叫會進到這裡，
    // 再轉成 Entry 放進 CapturingLoggerProvider.Entries。
    private sealed class Capture(CapturingLoggerProvider owner, string category) : ILogger
    {
        // 將目前 Logger Scope 放入外部 Scope Provider，
        // 讓後續 Log 可以保留 TraceId / RequestId 等 request context。
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner.scopes.Push(state);
        // 此測試 Logger 對所有 LogLevel 都啟用；外部 Logging 系統的 filter 仍照常生效。
        public bool IsEnabled(LogLevel level) => true;
        // 捕捉一筆正式 Logger 產生的 Log。
        // 同時保存 structured fields、目前 Scope、Exception 與格式化後 Message，
        // 供後續 Acceptance Test 驗證 logging contract 與敏感資訊保護。
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // 從 Logger state 取出 structured logging 欄位，
            // 例如 {Stage}、{BatchId} 等具名欄位。
            var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(x => x.Key, x => x.Value) : new Dictionary<string, object?>();
            // 收集目前所有 Logger Scope 資料，例如 TraceId / RequestId。
            var scope = new Dictionary<string, object?>();
            owner.scopes.ForEachScope((item, target) =>
            {
                if (item is IEnumerable<KeyValuePair<string, object?>> entries)
                    foreach (var entry in entries) target[entry.Key] = entry.Value;
            }, scope);
            // 將完整 Log snapshot 放入 thread-safe queue，供測試之後查詢與 Assert。
            owner.Entries.Enqueue(new(category, id, formatter(state, exception), exception, fields, scope));
        }
    }
}
