using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Utilities;

public static class DownloadHelper
{
    public static async Task DownloadAsync(HttpClient client, Uri url, string path, FileHash? hash, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".downloading-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var input = await client.GetStreamAsync(url, token).ConfigureAwait(false))
            await using (var output = File.Create(temporary))
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            if (!FileHelper.VerifyModified(temporary, null, hash))
                throw new InvalidDataException($"Downloaded file failed verification: {path}");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
