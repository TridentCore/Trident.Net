using System.Text;

namespace TridentCore.Text;

public sealed class MinecraftText
{
    public MinecraftText(IReadOnlyList<MinecraftTextRun> runs) => Runs = runs;
    public static MinecraftText Empty { get; } = new([]);

    public IReadOnlyList<MinecraftTextRun> Runs { get; }

    public override string ToString()
    {
        if (Runs.Count == 0)
        {
            return string.Empty;
        }

        if (Runs.Count == 1)
        {
            return Runs[0].Text;
        }

        var buffer = new StringBuilder();
        foreach (var run in Runs)
        {
            buffer.Append(run.Text);
        }

        return buffer.ToString();
    }
}
