using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public class PackageMaterializer(ILogger<PackageMaterializer> logger, IHttpClientFactory factory)
{
    public async Task MaterializeAsync(
        IReadOnlyList<PackagePlan> plans,
        Action<PackagePlan, int, string>? callback = null,
        CancellationToken token = default)
    {
        var semaphore = new SemaphoreSlim(Math.Max(Environment.ProcessorCount - 1, 1));
        var tasks = plans
                   .Select(async (plan, index) =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        if (plan.IsSkipping)
                        {
                            return;
                        }

                        var entered = false;
                        try
                        {
                            await semaphore.WaitAsync(token).ConfigureAwait(false);
                            entered = true;
                            var cachePath = PathDef.Default.FileOfPackageObject(plan.Label,
                                                                                    plan.Namespace,
                                                                                    plan.ProjectId,
                                                                                    plan.VersionId,
                                                                                    Path.GetExtension(plan
                                                                                       .RelativeTargetPath));
                            if (!FileHelper.VerifyModified(cachePath, null, plan.Sha1))
                            {
                                logger.LogDebug("Starting download fragile file {src} from {url}", cachePath, plan.Url);
                                var dir = Path.GetDirectoryName(cachePath);
                                if (dir != null && !Directory.Exists(dir))
                                {
                                    Directory.CreateDirectory(dir);
                                }

                                using var client = factory.CreateClient();
                                await using var reader = await client
                                                              .GetStreamAsync(plan.Url, token)
                                                              .ConfigureAwait(false);
                                await using var writer = new FileStream(cachePath,
                                                                        FileMode.Create,
                                                                        FileAccess.Write,
                                                                        FileShare.Write);
                                await reader.CopyToAsync(writer, token).ConfigureAwait(false);
                                await writer.FlushAsync(token).ConfigureAwait(false);
                            }

                            callback?.Invoke(plan, index, cachePath);
                        }
                        catch (OperationCanceledException)
                        {
                            // no log
                            throw;
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to materialize {}", plan);
                            throw;
                        }
                        finally
                        {
                            if (entered)
                            {
                                semaphore.Release();
                            }
                        }
                    })
                   .ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
