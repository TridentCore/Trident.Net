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

    public static void Merge(IList<LockData.Library> libraries, LockData.Library library)
    {
        ValidateIdentity(library.Id);
        var found = libraries.FirstOrDefault(x => x.Id.Namespace == library.Id.Namespace
                                               && x.Id.Name == library.Id.Name
                                               && x.Id.Platform == library.Id.Platform
                                               && x.Id.Extension == library.Id.Extension
                                               && x.IsNative == library.IsNative);
        if (found is not null)
        {
            if (found.Id.Version == library.Id.Version)
            {
                if (library.IsPresent)
                {
                    libraries.Remove(found);
                }
                else
                {
                    return;
                }
            }
            else if (found.IsPresent && library.IsPresent)
            {
                libraries.Remove(found);
            }
        }

        libraries.Add(library);
    }
}
