namespace Trident.Abstractions.FileModels;

// 打包器配置
public record PackData(IReadOnlyList<PackData.Entry> IncludedOverrides, bool IncludingSource)
{
    #region Nested type: Entry

    public record Entry(bool Enabled, string Key);

    #endregion

    public static PackData CreateDefault() =>
        new([
                new(false, Profile.OVERRIDE_JAVA_MAX_MEMORY),
                new(false, Profile.OVERRIDE_JAVA_ADDITIONAL_ARGUMENTS),
                new(false, Profile.OVERRIDE_WINDOW_TITLE),
                new(false, Profile.OVERRIDE_WINDOW_HEIGHT),
                new(false, Profile.OVERRIDE_WINDOW_WIDTH),
                new(false, Profile.OVERRIDE_BEHAVIOR_CONNECT_SERVER)
            ],
            false);
}
