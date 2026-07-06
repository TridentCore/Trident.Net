using System.Text.Json;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;

namespace TridentCore.Core.Engines.Deploying.Stages;

// Loads the on-disk lock as the read-only BaseLock and seeds a fresh Lock with the current
// platform + options fingerprint. Does not judge validity — each downstream stage compares
// against BaseLock itself. A missing or legacy (FORMAT<2) file yields BaseLock = null, which
// simply means everything gets rebuilt (data is not lost: Profile is the source of truth).
public class LoadLockStage(ILogger<LoadLockStage> logger) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var path = PathDef.Default.FileOfLockData(Context.Key);
        if (!Context.Options.FullCheckMode && File.Exists(path))
        {
            try
            {
                var content = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
                var existing = JsonSerializer.Deserialize<LockData>(content, JsonSerializerOptions.Web);
                if (existing != null)
                {
                    Context.BaseLock = existing;
                    logger.LogInformation("Loaded lock: {path}", Path.GetFileName(path));
                }
                else
                {
                    logger.LogInformation("Lock deserialized to null, rebuilding");
                }
            }
            catch (JsonException e)
            {
                // Legacy FORMAT=1 (or corrupt) file — incompatible with the new structure.
                logger.LogWarning("Lock unreadable (likely legacy format), rebuilding: {message}", e.Message);
            }
            catch (Exception e)
            {
                logger.LogWarning("Load lock failed: {message}", e.Message);
            }
        }
        else
        {
            logger.LogInformation("No usable lock on disk, creating fresh");
        }

        Context.Lock = new()
        {
            Platform = new(Context.Setup.Version, Context.Setup.Loader),
            Viability = new(Context.OptionsHash)
        };
    }
}
