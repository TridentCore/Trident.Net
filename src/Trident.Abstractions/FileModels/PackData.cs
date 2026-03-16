namespace Trident.Abstractions.FileModels;

// 打包器配置
public class PackData
{
    public required IList<Entry> IncludedOverrides { get; init; }
    public required bool IncludingSource { get; set; }

    public static PackData CreateDefault() =>
        new()
        {
            IncludingSource = false,
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
