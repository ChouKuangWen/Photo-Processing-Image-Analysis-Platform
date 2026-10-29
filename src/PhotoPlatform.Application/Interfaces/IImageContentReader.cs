using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.Application.Interfaces;

// 只接受系統內的 ImageId，不接受實體路徑；具體 Reader 留給 Infrastructure 後續交付。
// 成功後由呼叫端釋放 ContentHandle；讀取不保證可 Seek，ContentVersion 必須對應回傳的原圖內容。
public interface IImageContentReader
{
    Task<ContentHandle> OpenOriginalAsync(long imageId, CancellationToken cancellationToken);
}
