using System.Text.Json;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;

namespace TridentCore.Core.Engines.Deploying.Stages;

// NOTE: 加载磁盘锁作为只读 BaseLock，并以当前 platform + options 指纹种出新的 Lock。
//  不判有效性——各下游阶段自行与 BaseLock 比较。文件缺失或旧格式（FORMAT<2）时
//  BaseLock = null，即一切重建（数据不丢：Profile 才是真源）。
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
                // NOTE: 旧 FORMAT=1（或损坏）文件——与新结构不兼容。
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
            Viability = new(Context.OptionsHash, Context.PriorityHash)
        };
    }
}
