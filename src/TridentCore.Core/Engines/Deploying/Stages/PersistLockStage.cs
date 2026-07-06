using System.Text.Json;
using TridentCore.Abstractions;

namespace TridentCore.Core.Engines.Deploying.Stages;

// The single write-back point: serializes the assembled Lock to data.lock.json.
public class PersistLockStage : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var path = PathDef.Default.FileOfLockData(Context.Key);
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(Context.Lock, JsonSerializerOptions.Web),
                token
            )
            .ConfigureAwait(false);
    }
}
