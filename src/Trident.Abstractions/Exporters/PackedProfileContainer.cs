namespace Trident.Abstractions.Exporters;

public class PackedProfileContainer(string key) : IDisposable
{
    public string Key => key;
    public required string OverrideDirectoryName { get; set; }
    public IDictionary<string, Stream> Attachments { get; } = new Dictionary<string, Stream>();

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
