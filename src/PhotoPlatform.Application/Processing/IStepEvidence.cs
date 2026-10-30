namespace PhotoPlatform.Application.Processing;

// 模組提供具名且不可變的來源資料；實作此介面不等於通過資料庫或完成資格核對。
public interface IStepEvidence
{
    StepEvidenceIdentity Identity { get; }
}
