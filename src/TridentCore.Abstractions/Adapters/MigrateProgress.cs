namespace TridentCore.Abstractions.Adapters;

public record MigrateProgress
{
    public required Phase CurrentPhase { get; init; }

    // Display name of the instance currently being transferred, null during the identify phase.
    public string? InstanceName { get; init; }

    // 1-based index of the current instance within the batch, null during the identify phase.
    public int? InstanceIndex { get; init; }
    public int? InstanceTotal { get; init; }

    // Per-instance file-transfer progress in [0,1], or null for indeterminate.
    public double? Percent { get; init; }

    public enum Phase
    {
        Identifying,
        Transferring
    }
}
