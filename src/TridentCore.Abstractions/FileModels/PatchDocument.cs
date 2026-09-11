using System.Text.Json;

namespace TridentCore.Abstractions.FileModels;

public record PatchDocument
{
    public int Format { get; init; } = 1;
    public string? Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<Operation> Operations { get; init; } = [];

    public record Operation
    {
        public required string Target { get; init; }
        public required string Action { get; init; }
        public JsonElement? Value { get; init; }
        public Selector? Match { get; init; }
        public string? Before { get; init; }
        public string? After { get; init; }
        public string? IfMissing { get; init; }
        public bool OnlyIfNewer { get; init; }
        public IReadOnlyList<PlatformRule> Rules { get; init; } = [];
    }

    public record Selector
    {
        public string? Identity { get; init; }
        public bool? Native { get; init; }
        public bool? Classpath { get; init; }
        public IReadOnlyList<string>? Arguments { get; init; }
    }

    public record PlatformRule(string Action, string? Os = null, string? Arch = null, string? Version = null);
}
