namespace TridentCore.Abstractions.FileModels;

// 打包器配置
public class PackData
{
    public required bool OfflineMode { get; set; }
    public required IList<string> ExcludedTags { get; init; } = [];
    public required IList<Entry> IncludedOverrides { get; init; }
    public required bool IncludingSource { get; set; }
    public required bool IncludingTags { get; set; }

    public static PackData CreateDefault() =>
        new()
        {
            OfflineMode = false,
            ExcludedTags = [],
            IncludingSource = false,
            IncludingTags = true,
            IncludedOverrides =
            [
                new() { Key = Profile.OVERRIDE_JAVA_MAX_MEMORY },
                new() { Key = Profile.OVERRIDE_JAVA_ADDITIONAL_ARGUMENTS },
                new() { Key = Profile.OVERRIDE_BEHAVIOR_CONNECT_SERVER },
            ],
        };

    #region Nested type: Entry

    public class Entry
    {
        public bool Enabled { get; set; }
        public required string Key { get; init; }
    }

    #endregion
}
