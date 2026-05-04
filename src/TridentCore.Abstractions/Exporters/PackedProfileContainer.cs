namespace TridentCore.Abstractions.Exporters;

public class PackedProfileContainer(string key) : IDisposable
{
    public string Key => key;

    public required string OverrideDirectoryName { get; set; }

    // 文件流<包内相对路径，文件内容>
    public IDictionary<string, Stream> Attachments { get; } = new Dictionary<string, Stream>();

    // 文件引用<包内相对路径，实机文件的绝对路径>
    // 这一步是 IProfileExporter 的职责，Relative 需要是最终的结果，也就是已经包含了 OverrideDirectoryName
    //  以此支持自定义某些格式中允许有多个 Override Layer 的情况
    //  比如 OverrideDirectoryName => "overrides" 然后 Files 里则分布塞入前缀为 "overrides-clients"/"overrides-servers"
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
