namespace PhotoPlatform.Application.Processing;

// 具名型別固定業務欄位；格式正確不等於來源已持久保存或取得執行權。
public interface IProcessingStepInput
{
    StepEvidenceIdentity Identity { get; }
    CanonicalInput ToCanonicalInput();
}
