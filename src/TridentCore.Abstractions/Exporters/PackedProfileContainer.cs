namespace TridentCore.Abstractions.Exporters;

public class PackedProfileContainer(string key) : IDisposable
{
    public string Key => key;

    public required string OverrideDirectoryName { get; set; }

    // NOTE: 文件流<包内相对路径，文件内容>
    public IDictionary<string, Stream> Attachments { get; } = new Dictionary<string, Stream>();

    // NOTE: 文件引用<包内相对路径，实机绝对路径>。Relative 须为最终结果（已含
    //  OverrideDirectoryName），以支持允许多个 Override Layer 的格式——如
    //  OverrideDirectoryName => "overrides" 时 Files 塞入 "overrides-clients"/"overrides-servers" 前缀。
    public IDictionary<string, string> Files { get; } = new Dictionary<string, string>();

    #region IDisposable Members

    public void Dispose()
    {
        foreach (var stream in Attachments.Values)
        {
            stream.Dispose();
        }
    }

    #endregion
}
