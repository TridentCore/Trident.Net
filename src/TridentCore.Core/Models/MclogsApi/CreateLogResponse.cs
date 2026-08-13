namespace TridentCore.Core.Models.MclogsApi;

// NOTE: Token 由 mclo.gs 创建响应返回，持有它才能在导出中止/失败时调用 DeleteLogAsync 回滚已上传的日志。
public record CreateLogResponse(bool Success, string? Id, string? Url, string? Raw, string? Error, string? Token = null);
