namespace PhotoPlatform.Application.Exceptions;

// 資料庫無法完成核對不等於資料已通過；API 層之後再依正式契約轉成安全的 503。
public sealed class ProcessingPrerequisiteUnavailableException() : Exception("Processing prerequisites could not be checked.");
