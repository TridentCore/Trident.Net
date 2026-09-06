using System.Collections.Immutable;
using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Services;

public sealed class LaunchCompilerService
{
    public const int FORMAT_VERSION = 1;

    public string Fingerprint(LaunchResolution resolution, LaunchTarget target) =>
        HashHelper.ComputeObjectHash(new { Compiler = FORMAT_VERSION, resolution.Fingerprint, Target = target });

    public Compilation Compile(LaunchResolution resolution, LaunchTarget target, bool ignoreJavaRequirements = false)
    {
        if (!ignoreJavaRequirements && !resolution.JavaMajors.Contains(target.JavaMajor))
        {
            throw new FormatException($"Java {target.JavaMajor} is incompatible with the selected components");
        }

        string? mainClass = null;
        LaunchAssetIndex? assets = null;
        var firstThread = target.Os == "osx";
        LaunchArtifact.Identity? clientArtifact = null;
        var artifacts = new Dictionary<LaunchArtifact.Identity, LaunchArtifact>();
        var classpath = new List<LaunchArtifact.Identity>();
        var natives = new List<CompiledLaunch.NativeExtraction>();
        var agents = new List<LaunchLibrary>();
        var javaArguments = DefaultJavaArguments().ToList();
        var gameArguments = new List<string>();
        var diagnostics = new List<LaunchDiagnostic>();

        foreach (var entry in resolution.Components)
        {
            var component = entry.Definition;
            try
            {
                firstThread = component.StartOnFirstThread ?? firstThread;
                if (component.MainClass is { } declaredMain)
                {
                    if (string.IsNullOrWhiteSpace(declaredMain))
                    {
                        throw new FormatException("Main class is empty");
                    }
                    if (mainClass is not null && mainClass != declaredMain)
                    {
                        diagnostics.Add(new(LaunchDiagnostic.Kind.Warning,
                                            $"Component '{component.Id}' selects main class '{declaredMain}' instead of '{mainClass}'",
                                            entry.Source));
                    }
                    mainClass = declaredMain;
                }
                if (component.AssetIndex is { } index)
                {
                    if (!ArtifactHelper.IsSafeSegment(index.Id))
                    {
                        throw new FormatException($"Invalid asset index identity '{index.Id}'");
                    }
                    ValidateUri(index.Url);
                    assets = index;
                }

                ApplyArguments(javaArguments, component.JavaArguments, target);
                ApplyArguments(gameArguments, component.GameArguments, target);
                foreach (var library in component.Libraries.Where(x => LaunchRuleHelper.Allows(x.Rules, target)
                    && (x.Platform is null || (x.Platform.Os == target.Os && x.Platform.Architecture == target.Architecture))))
                {
                    var artifact = library.Artifact;
                    ArtifactHelper.Validate(artifact.Id);
                    ValidateUri(artifact.Url);
                    artifacts[artifact.Id] = artifact;
                    switch (library.Use)
                    {
                        case LaunchLibrary.Usage.Client:
                            clientArtifact = artifact.Id;
                            goto case LaunchLibrary.Usage.Classpath;
                        case LaunchLibrary.Usage.Classpath:
                            var slot = classpath.FindIndex(x => SameClasspathSlot(x, artifact.Id));
                            if (slot < 0)
                            {
                                classpath.Add(artifact.Id);
                            }
                            else
                            {
                                if (classpath[slot] != artifact.Id)
                                {
                                    diagnostics.Add(new(LaunchDiagnostic.Kind.Warning,
                                                        $"Component '{component.Id}' selects '{ArtifactHelper.CoordinateOf(artifact.Id)}' instead of '{ArtifactHelper.CoordinateOf(classpath[slot])}'",
                                                        entry.Source));
                                }
                                classpath[slot] = artifact.Id;
                            }
                            break;
                        case LaunchLibrary.Usage.Native:
                            if (library.ExtractExcludes.Any(x => x.StartsWith('/') || x.Split('/').Contains("..")))
                            {
                                throw new FormatException("Invalid native extraction exclusion");
                            }
                            var nativeSlot = natives.FindIndex(x => SameClasspathSlot(x.Artifact, artifact.Id));
                            var extraction = new CompiledLaunch.NativeExtraction(artifact.Id, [.. library.ExtractExcludes]);
                            if (nativeSlot < 0) natives.Add(extraction);
                            else natives[nativeSlot] = extraction;
                            break;
                        case LaunchLibrary.Usage.Agent:
                            agents.Add(library);
                            break;
                        case LaunchLibrary.Usage.Required:
                            break;
                        default:
                            throw new FormatException($"Unsupported artifact use '{library.Use}'");
                    }
                }
            }
            catch (Exception e) when (e is FormatException or ArgumentException)
            {
                throw new FormatException($"Component '{component.Id}' from '{entry.Source}': {e.Message}", e);
            }
        }

        string Bind(string argument)
        {
            if (argument.Contains("${minecraft_jar}"))
            {
                argument = argument.Replace("${minecraft_jar}", clientArtifact is not null
                    ? ArtifactHelper.ReferenceOf(clientArtifact)
                    : throw new FormatException("Argument requires a client artifact, but none is declared"));
            }
            return ArtifactHelper.BindReferences(argument, id => artifacts.ContainsKey(id)
                ? ArtifactHelper.ReferenceOf(id)
                : throw new FormatException($"Argument references undeclared artifact '{ArtifactHelper.CoordinateOf(id)}'"));
        }

        if (firstThread && target.Os == "osx")
        {
            javaArguments.Insert(0, "-XstartOnFirstThread");
        }
        if (target.Os == "windows")
        {
            javaArguments.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");
        }

        foreach (var agent in agents)
        {
            javaArguments.Add("-javaagent:" + ArtifactHelper.ReferenceOf(agent.Artifact.Id)
                              + (agent.AgentArguments is null ? "" : "=" + agent.AgentArguments));
        }

        var result = new CompiledLaunch(Fingerprint(resolution, target), target, resolution.JavaMajors,
                                        mainClass ?? throw new FormatException("No component supplies a main class"),
                                        assets ?? throw new FormatException("No component supplies an asset index"),
                                        [.. gameArguments.Select(Bind)], [.. javaArguments.Select(Bind)],
                                        [.. artifacts.Values], [.. classpath], [.. natives]);
        return new(result, diagnostics);
    }

    private static void ApplyArguments(List<string> destination, LaunchArguments arguments, LaunchTarget target)
    {
        if (arguments.Replace is not null)
        {
            destination.Clear();
            Append(arguments.Replace);
        }
        Append(arguments.Append);
        return;

        void Append(IEnumerable<LaunchArgument> values)
        {
            foreach (var argument in values.Where(x => LaunchRuleHelper.Allows(x.Rules, target)))
            {
                if (argument.Value is null || argument.Value.Contains('\0'))
                {
                    throw new FormatException("Invalid launch argument");
                }
                destination.Add(argument.Value);
            }
        }
    }

    private static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("https" or "http" or "file"))
        {
            throw new FormatException($"Unsupported or unresolved artifact source '{uri}'");
        }
    }

    private static bool SameClasspathSlot(LaunchArtifact.Identity a, LaunchArtifact.Identity b) =>
        a.Namespace == b.Namespace && a.Name == b.Name && a.Classifier == b.Classifier && a.Extension == b.Extension;

    private static IEnumerable<string> DefaultJavaArguments() =>
    [
        "-Djava.library.path=${natives_directory}", "-DlibraryDirectory=${library_directory}",
        "-Djna.tmpdir=${natives_directory}", "-Dorg.lwjgl.system.SharedLibraryExtractPath=${natives_directory}",
        "-Dio.netty.native.workdir=${natives_directory}", "-Dminecraft.launcher.brand=${launcher_name}",
        "-Dminecraft.launcher.version=${launcher_version}", "-Xmx${jvm_max_memory}", "-cp", "${classpath}"
    ];

    public sealed record Compilation(CompiledLaunch Result, IReadOnlyList<LaunchDiagnostic> Diagnostics);
}
