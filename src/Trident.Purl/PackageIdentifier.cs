namespace Trident.Purl;

// 只是用来描述 (Label, Ns?, Pid, Vid?) 的结构
public readonly record struct PackageIdentifier(string Repository, string? Namespace, string Identity, string? Version);
