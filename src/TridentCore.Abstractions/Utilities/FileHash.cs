namespace TridentCore.Abstractions.Utilities;

public record FileHash(HashAlgorithm Algorithm, string Value)
{
    public static FileHash Sha1(string value) => new(HashAlgorithm.Sha1, value);

    public static FileHash Sha256(string value) => new(HashAlgorithm.Sha256, value);

    public static FileHash Sha512(string value) => new(HashAlgorithm.Sha512, value);

    public static FileHash Md5(string value) => new(HashAlgorithm.Md5, value);

    /// <summary>
    ///     从 nullable SHA1 字符串构造 FileHash，向下兼容。
    /// </summary>
    public static FileHash? FromSha1(string? sha1) => sha1 is not null ? new(HashAlgorithm.Sha1, sha1) : null;
}
