namespace TridentCore.Abstractions.Adapters;

public record MigrateProgress
{
    public enum Phase { Identifying, Transferring }

    public required Phase CurrentPhase { get; init; }

    // NOTE: 当前迁移实例的显示名；identify 阶段为 null。
    public string? InstanceName { get; init; }

    // NOTE: 批内当前实例的 1 基索引；identify 阶段为 null。
    public int? InstanceIndex { get; init; }
    public int? InstanceTotal { get; init; }

    // NOTE: 单实例文件迁移进度 [0,1]；不可量化时为 null。
    public double? Percent { get; init; }
}
