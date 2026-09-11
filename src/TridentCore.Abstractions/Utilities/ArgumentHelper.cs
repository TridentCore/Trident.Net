using System.Text;

namespace TridentCore.Abstractions.Utilities;

// 启动参数以「token 组」为最小单位流转：一个组是一次完整的选项声明（`-cp` 与它的值、`-Xmx2G`
// 自成一组），patch 按组前缀匹配，只有启动 JVM 时才展平。
public static class ArgumentHelper
{
    public static IReadOnlyList<string[]> DefaultJvmArguments(bool firstThreadOnMacOS = false)
    {
        var arguments = new List<string>();
        if (firstThreadOnMacOS && OperatingSystem.IsMacOS())
        {
            arguments.Add("-XstartOnFirstThread");
        }
        if (OperatingSystem.IsWindows())
        {
            arguments.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");
        }
        arguments.AddRange([
            "-Djava.library.path=${natives_directory}",
            "-DlibraryDirectory=${library_directory}",
            "-Djna.tmpdir=${natives_directory}",
            "-Dorg.lwjgl.system.SharedLibraryExtractPath=${natives_directory}",
            "-Dio.netty.native.workdir=${natives_directory}",
            "-Dminecraft.launcher.brand=${launcher_name}",
            "-Dminecraft.launcher.version=${launcher_version}",
            "-Xmx${jvm_max_memory}", "-cp", "${classpath}"
        ]);
        return GroupArguments(arguments);
    }

    public static IReadOnlyList<string[]> GroupArguments(IReadOnlyList<string> arguments)
    {
        var groups = new List<string[]>();
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].StartsWith('-') && !arguments[i].Contains('=')
             && i + 1 < arguments.Count && !arguments[i + 1].StartsWith('-'))
            {
                groups.Add([arguments[i], arguments[++i]]);
            }
            else
            {
                groups.Add([arguments[i]]);
            }
        }
        return groups;
    }

    public static IReadOnlyList<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var value = new StringBuilder();
        char? quote = null;
        var started = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && (text[i + 1] == quote || text[i + 1] == '"'))
            {
                value.Append(text[++i]);
                started = true;
            }
            else if (c == quote)
            {
                quote = null;
            }
            else if (quote is null && c is '\'' or '"')
            {
                quote = c;
                started = true;
            }
            else if (quote is null && char.IsWhiteSpace(c))
            {
                if (started)
                {
                    tokens.Add(value.ToString());
                    value.Clear();
                    started = false;
                }
            }
            else
            {
                value.Append(c);
                started = true;
            }
        }
        if (quote is not null)
        {
            throw new InvalidDataException("Unterminated quote in arguments.");
        }
        if (started)
        {
            tokens.Add(value.ToString());
        }
        return tokens;
    }
}
