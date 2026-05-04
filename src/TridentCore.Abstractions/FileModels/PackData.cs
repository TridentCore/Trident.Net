namespace TridentCore.Abstractions.FileModels;

// 打包器配置
public class PackData
{
    public bool OfflineMode { get; set; }
    public IList<string> ExcludedTags { get; set; } = [];

    public IList<Entry> IncludedOverrides { get; set; } =
    [
        new() { Key = Profile.OVERRIDE_JAVA_MAX_MEMORY },
        new() { Key = Profile.OVERRIDE_JAVA_ADDITIONAL_ARGUMENTS },
        new() { Key = Profile.OVERRIDE_BEHAVIOR_CONNECT_SERVER }
    ];

    public bool IncludingSource { get; set; }
    public bool IncludingTags { get; set; } = true;

    #region Nested type: Entry

    public class Entry
    {
        public bool Enabled { get; set; }
        public required string Key { get; init; }
    }

    #endregion
}
