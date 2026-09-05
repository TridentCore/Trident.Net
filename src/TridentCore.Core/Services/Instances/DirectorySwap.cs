namespace TridentCore.Core.Services.Instances;

// 一次目录级原子替换的三个位置：live 是当前生效的目录，Staged 是校验通过的新内容，
// Backup 是提交时 live 的暂存位（回滚时原样搬回）。
public sealed record DirectorySwap(string Live, string Staged, string Backup);
