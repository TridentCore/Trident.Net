using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;
using TridentCore.Core.Services.Instances;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceInstallCommand(
    InstanceManager instanceManager,
    RepositoryAgent repositories,
    ActivityAwaiter awaiter,
    CliOutput output) : Command<InstanceInstallCommand.Arguments>
{
    protected override int Execute(CommandContext context, Arguments settings, CancellationToken cancellationToken)
    {
        var activities = InstanceOperation
                        .StartInstallAsync(instanceManager, repositories, settings.Pref, settings.Identity)
                        .GetAwaiter()
                        .GetResult();
        awaiter.AwaitInstallAsync(activities, CancellationToken.None).GetAwaiter().GetResult();

        // 终态快照已带全安装结果，无需回查活动对象。
        var final = (InstanceActivity.Installing)ActivityAwaiter
                                               .AwaitCompletionAsync(activities, CancellationToken.None)
                                               .GetAwaiter()
                                               .GetResult();

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { action = "install", key = final.Key, source = final.Reference });
        }
        else
        {
            output.WriteKeyValueTable("Modpack installed",
                                      ("Instance", final.Key),
                                      ("Source", final.Reference ?? "-"));
            output.WriteSuccess($"Instance {final.Key} installed.");
        }

        return ExitCodes.SUCCESS;
    }

    public class Arguments : CommandSettings
    {
        [CommandOption("--identity <KEY>")]
        public string? Identity { get; set; }

        [CommandArgument(0, "<PREF>")]
        public required string Pref { get; set; }
    }
}
