using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Trident.Cli.Commands;

public abstract class InstanceCommandBase<T> : Command<T> where T : InstanceArgumentsBase
{
    public InstanceContext Context { get; private set; } = null!;
    public override ValidationResult Validate(CommandContext context, T settings)
    {
        if (settings.Profile != null)
        {
            // 选择特定的 profile 文件
        }
        else if (settings.Instance != null)
        {
            // 选择 trident home 里的实例
        }
        else
        {
            // 自动搜索
        }

        return ValidationResult.Success();
    }

    public class InstanceContext(string instanceHome)
    {

    }
}
