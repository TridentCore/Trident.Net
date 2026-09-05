using System.Text.RegularExpressions;
using TridentCore.Abstractions.FileModels;

namespace TridentCore.Abstractions.Utilities;

public static partial class LibraryHelper
{
    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    public static bool IsSafeIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value)
     && value is not "." and not ".."
     && IdentifierPattern().IsMatch(value);

    public static LockData.Library.Identity ParseIdentity(string fullname)
    {
        var extension = "jar";
        var index = fullname.IndexOf('@');
        if (index > 0)
        {
            extension = fullname[(index + 1)..];
            fullname = fullname[..index];
        }

        var split = fullname.Split(':');
        var identity = split.Length switch
        {
            4 => new LockData.Library.Identity(split[0], split[1], split[2], split[3], extension),
            3 => new LockData.Library.Identity(split[0], split[1], split[2], null, extension),
            _ => throw new NotSupportedException($"Not recognized package name format: {fullname}")
        };
        ValidateIdentity(identity);
        return identity;
    }

    // Maven 坐标到构件文件名的唯一规则。
    // WARNING: platform 必须进文件名，native 库的 classifier 正落在这个位置；漏掉会让每个
    //  native 库都指向不存在的路径。
    public static string FileNameOf(LockData.Library.Identity identity) =>
        identity.Platform is null
            ? $"{identity.Name}-{identity.Version}.{identity.Extension}"
            : $"{identity.Name}-{identity.Version}-{identity.Platform}.{identity.Extension}";

    // Maven 坐标到仓库内相对路径的唯一布局规则：ns 的点变斜杠，末段是构件文件名。
    public static string RelativePathOf(LockData.Library.Identity identity) =>
        $"{identity.Namespace.Replace('.', '/')}/{identity.Name}/{identity.Version}/{FileNameOf(identity)}";

    // 把 maven 仓库根解析成完整下载地址。仓库根未以 / 结尾时补上，否则 Uri 会吃掉最后一段。
    public static Uri ResolveAgainstRepository(Uri repository, LockData.Library.Identity identity)
    {
        var basedir = repository.AbsoluteUri.EndsWith('/') ? repository : new Uri(repository.AbsoluteUri + '/');
        return new(basedir, RelativePathOf(identity));
    }

    public static void ValidateIdentity(LockData.Library.Identity identity)
    {
        if (!IsSafeIdentifier(identity.Namespace)
         || !IsSafeIdentifier(identity.Name)
         || !IsSafeIdentifier(identity.Version)
         || (identity.Platform is not null && !IsSafeIdentifier(identity.Platform))
         || !IsSafeIdentifier(identity.Extension))
        {
            throw new FormatException("Launch plan contains an invalid library identity");
        }
    }

    // 库仲裁：同坐标的两个候选争同一个落点文件，必须选出唯一赢家——同一物理文件不可能下两遍。
    // 规则两条，按序：角色优先（IsPresent=true 胜 false，进 classpath 的用途压过纯下载），
    // 同角色则后来者胜（后叠加的层更具体：loader 在 vanilla 之后，用户层在来源层之后）。
    // 返回被顶替的旧库（无冲突或 incoming 落败时为 null），供调用方按需报告覆盖。
    // WARNING: vanilla+loader 天生存在大量同坐标碰撞（asm、guava 等各自带版本），必须由这两条
    //  恒定规则消化；不要改成依赖外部 plan 显式 remove-libraries 表达，那要求整合包为每次
    //  内在碰撞写一条移除，且漏写即产生同坐标双版本。
    public static LockData.Library? Merge(IList<LockData.Library> libraries, LockData.Library incoming)
    {
        ValidateIdentity(incoming.Id);
        var index = IndexOfSameSlot(libraries, incoming);
        if (index < 0)
        {
            libraries.Add(incoming);
            return null;
        }

        var resident = libraries[index];
        if (!Wins(incoming, resident))
        {
            return null;
        }

        // 就地替换而非移除后追加——落点位置由首次声明它的层决定，与后续叠加了几层无关。
        // WARNING: 库顺序即 classpath 顺序，移到队尾会静默改变同名类的加载优先级。
        libraries[index] = incoming;
        return resident;
    }

    // NOTE: Version 不在落点判定内（同坐标不同版本仍是同一个落点，需要仲裁）；IsNative 在内，
    //  因为 native 与 classpath 是同一文件的两个用途，各自成立、互不排斥。
    private static int IndexOfSameSlot(IList<LockData.Library> libraries, LockData.Library incoming)
    {
        for (var i = 0; i < libraries.Count; i++)
        {
            var candidate = libraries[i];
            if (candidate.Id.Namespace == incoming.Id.Namespace
             && candidate.Id.Name == incoming.Id.Name
             && candidate.Id.Platform == incoming.Id.Platform
             && candidate.Id.Extension == incoming.Id.Extension
             && candidate.IsNative == incoming.IsNative)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Wins(LockData.Library incoming, LockData.Library resident) =>
        incoming.IsPresent || !resident.IsPresent;
}
