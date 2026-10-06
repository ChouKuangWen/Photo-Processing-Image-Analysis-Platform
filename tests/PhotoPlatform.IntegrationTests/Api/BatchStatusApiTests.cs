using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Interfaces;

namespace PhotoPlatform.IntegrationTests.Api;

public sealed class BatchStatusApiTests
{
    [Theory]
    [InlineData(100, 80, 80)]
    [InlineData(8, 1, 12.5)]
    [InlineData(0, 5, 0)]
    [InlineData(10, -1, 0)]
    [InlineData(10, 11, 100)]

    // 啟動真實 ASP.NET Core Test Host，驗證 Batch 存在時的
    // 200 Response envelope、欄位內容、Progress 與 TraceId Log correlation。
    public async Task ExistingBatch_ExactEnvelopeAndProgress(int total, int processed, decimal progress)
    {
        var id = Guid.NewGuid();
        var logs = new CapturingProvider();
        await using var host = await Start((batchId, token) =>
        {
            Assert.Equal(id, batchId);
            Assert.True(token.CanBeCanceled);
            return Task.FromResult<BatchStatusSnapshot?>(new(id, total, processed, 78, 2, "Processing"));
        }, logs);
        using var response = await host.Client.GetAsync($"/api/v1/images/batches/{id}/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "success", "data" }, json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(new[] { "batchId", "totalCount", "processedCount", "successCount", "failedCount", "progressPercentage", "status" },
            data.EnumerateObject().Select(x => x.Name));
        Assert.Equal(id, data.GetProperty("batchId").GetGuid());
        Assert.Equal(total, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(processed, data.GetProperty("processedCount").GetInt32());
        Assert.Equal(78, data.GetProperty("successCount").GetInt32());
        Assert.Equal(2, data.GetProperty("failedCount").GetInt32());
        Assert.Equal(progress, data.GetProperty("progressPercentage").GetDecimal());
        Assert.Equal("Processing", data.GetProperty("status").GetString());
        AssertCorrelation(logs, id, null);
    }

    [Fact]
    // 驗證 Batch 不存在時回傳標準 404 BATCH_NOT_FOUND envelope，
    // 並確認 Response TraceId 可與查詢 Log 關聯。
    public async Task MissingBatch_ExactNotFoundEnvelopeAndTraceCorrelation()
    {
        var id = Guid.NewGuid();
        var logs = new CapturingProvider();
        await using var host = await Start((_, _) => Task.FromResult<BatchStatusSnapshot?>(null), logs);
        using var response = await host.Client.GetAsync($"/api/v1/images/batches/{id}/status");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "success", "error" }, json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        var error = json.RootElement.GetProperty("error");
        Assert.Equal(new[] { "code", "message", "traceId" }, error.EnumerateObject().Select(x => x.Name));
        Assert.Equal("BATCH_NOT_FOUND", error.GetProperty("code").GetString());
        Assert.Equal("Batch was not found.", error.GetProperty("message").GetString());
        var trace = error.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(trace));
        AssertCorrelation(logs, id, trace);
    }

    [Fact]
    // 模擬 Query 發生非預期例外，確認既有 HTTP error boundary
    // 只回傳安全的 INTERNAL_ERROR，不洩漏 SQL、路徑或其他內部資訊。
    public async Task UnexpectedQueryFailure_IsSanitizedByExistingHttpBoundary()
    {
        var logs = new CapturingProvider();
        await using var host = await Start((_, _) => throw new InvalidOperationException(
            "SQL Detail; Stack Trace; C:\\private\\secret; Password=private"), logs);
        using var response = await host.Client.GetAsync($"/api/v1/images/batches/{Guid.NewGuid()}/status");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("INTERNAL_ERROR", error.GetProperty("code").GetString());
        Assert.Equal("An internal error occurred.", error.GetProperty("message").GetString());
        Assert.DoesNotContain("private", body);
        Assert.DoesNotContain("SQL Detail", body);
        var mapping = Assert.Single(logs.Entries, x => x.Fields.ContainsKey("ErrorCode"));
        Assert.Equal(error.GetProperty("traceId").GetString(), mapping.Fields["TraceId"]);
        Assert.All(logs.Entries, entry =>
        {
            Assert.Null(entry.Exception);
            Assert.DoesNotContain("private", entry.Message);
            Assert.DoesNotContain("SQL Detail", entry.Message);
        });
    }

    // 確認同一次 HTTP Request 產生的不同 Log，
    // 都能透過同一個 TraceId 串起來，而且 BatchId 也正確。
    private static void AssertCorrelation(CapturingProvider logs, Guid id, string? trace)
    {
        var query = Assert.Single(logs.Entries, x => x.EventId.Name == "BatchStatusQueried");
        Assert.Equal(id, query.Fields["BatchId"]);
        var actualTrace = Assert.IsType<string>(query.Fields["TraceId"]);
        Assert.False(string.IsNullOrWhiteSpace(actualTrace));
        if (trace is not null) Assert.Equal(trace, actualTrace);
        Assert.Equal(actualTrace, query.Scope["TraceId"]);
        Assert.Contains(logs.Entries, x => x.Fields.TryGetValue("StatusCode", out _) &&
            x.Fields.TryGetValue("TraceId", out var value) && Equals(value, actualTrace));
    }

    // 建立測試用 ASP.NET Core Host，以 Stub 取代正式 Persistence。
    // 保留實際 Routing、DI、Application Query、Controller 與 HTTP middleware pipeline。
    private static Task<UploadApiHost> Start(Func<Guid, CancellationToken, Task<BatchStatusSnapshot?>> execute,
        CapturingProvider logs) => UploadApiHost.StartAsync(services =>
        {
            services.RemoveAll<IBatchStatusPersistence>();
            services.AddSingleton<IBatchStatusPersistence>(new Stub(execute));
            services.AddSingleton<ILoggerProvider>(logs);
        }, new()
        {
            ["ConnectionStrings:PhotoPlatform"] = "Server=unused;Database=unused;Integrated Security=True",
            ["Upload:StorageRoot"] = Path.GetTempPath(),
            ["Upload:MaxFileSizeBytes"] = "1024"
        });

    private sealed class Stub(Func<Guid, CancellationToken, Task<BatchStatusSnapshot?>> execute) : IBatchStatusPersistence
    {
        public Task<BatchStatusSnapshot?> FindAsync(Guid id, CancellationToken token) => execute(id, token);
    }

    // Integration Test 專用 Logger Provider，收集 EventId、結構化欄位及 Scope，
    // 用於驗證同一次 HTTP Request 的 TraceId correlation。
    private sealed class CapturingProvider : ILoggerProvider, ISupportExternalScope
    {
        public sealed record Entry(EventId EventId, string Message, Exception? Exception,
            Dictionary<string, object?> Fields, Dictionary<string, object?> Scope);
        public ConcurrentQueue<Entry> Entries { get; } = new();
        private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;
        public ILogger CreateLogger(string categoryName) => new Capture(this);
        public void Dispose() { }
        private sealed class Capture(CapturingProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner.scopes.Push(state);
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var fields = state is IEnumerable<KeyValuePair<string, object?>> values ? values.ToDictionary(x => x.Key, x => x.Value) : new();
                var scope = new Dictionary<string, object?>();
                owner.scopes.ForEachScope((item, target) =>
                {
                    if (item is IEnumerable<KeyValuePair<string, object?>> entries)
                        foreach (var entry in entries) target[entry.Key] = entry.Value;
                }, scope);
                owner.Entries.Enqueue(new(id, formatter(state, exception), exception, fields, scope));
            }
        }
    }
}
