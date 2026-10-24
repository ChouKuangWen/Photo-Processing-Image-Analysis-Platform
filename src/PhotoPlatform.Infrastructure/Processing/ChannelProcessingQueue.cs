using System.Threading.Channels;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Processing;

public sealed class ChannelProcessingQueue : IProcessingQueue
{
    // 僅在執行期間傳遞 Job，不執行處理或修改 Job；持久化狀態仍由資料庫保存。
    private readonly Channel<ProcessingJob> channel;

    public ChannelProcessingQueue(ProcessingQueueOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // 建立 Queue 時拒絕無效值，讓設定錯誤在開始接受工作前就被發現。
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Capacity);
        // 限制 Queue 內待取出的 Job 數量，避免 Producer 較快時緩衝區無限制成長。
        channel = Channel.CreateBounded<ProcessingJob>(new BoundedChannelOptions(options.Capacity)
        {
            // 滿載時等待 Consumer 釋放空間，以 Backpressure 放慢 Producer，不丟棄 Job。
            FullMode = BoundedChannelFullMode.Wait,
            // 不承諾只有單一讀取端或寫入端，讓 Channel 支援多 Producer / Consumer。
            SingleReader = false,
            SingleWriter = false
        });
    }

    // Producer 加入工作；完成只表示入列成功，不代表圖片處理完成。
    // Token 可取消滿載時的非同步等待，取消例外直接交由呼叫端處理。
    public Task EnqueueAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        // WriteAsync 回傳 ValueTask；AsTask 用來符合既有 IProcessingQueue 的 Task 契約。
        return channel.Writer.WriteAsync(job, cancellationToken).AsTask();
    }

    // Consumer 依入列順序取出 Job；空 Queue 由 Channel 非同步等待，不用輪詢。
    // Token 讓呼叫端可中止等待；取出後的實際處理仍由 Consumer 負責。
    public ValueTask<ProcessingJob> DequeueAsync(CancellationToken cancellationToken)
        => channel.Reader.ReadAsync(cancellationToken);
}
