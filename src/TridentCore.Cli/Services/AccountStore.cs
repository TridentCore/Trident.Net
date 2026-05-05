using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TridentCore.Core.Accounts;

namespace TridentCore.Cli.Services;

public class AccountStore
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path = CliDataPaths.File("accounts.json");

    public IReadOnlyList<StoredAccount> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<StoredAccount>>(
                File.ReadAllText(_path),
                SerializerOptions
            ) ?? [];
    }

    public void Save(IEnumerable<StoredAccount> accounts)
    {
        AtomicFileWriter.WriteAllText(_path, JsonSerializer.Serialize(accounts, SerializerOptions));
    }

    public void AddOrReplace(StoredAccount account)
    {
        var accounts = Load().ToList();
        var isDefault = accounts.Count == 0 || account.IsDefault;
        accounts.RemoveAll(x =>
            string.Equals(x.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase)
        );
        if (isDefault)
        {
            accounts = [.. accounts.Select(x => x with { IsDefault = false })];
        }

        accounts.Add(account with { IsDefault = isDefault });
        Save(accounts.OrderBy(x => x.Username, StringComparer.OrdinalIgnoreCase));
    }

    public bool Remove(string uuid)
    {
        var accounts = Load().ToList();
        var removed =
            accounts.RemoveAll(x => string.Equals(x.Uuid, uuid, StringComparison.OrdinalIgnoreCase))
            > 0;
        if (!removed)
        {
            return false;
        }

        if (accounts.Count > 0 && accounts.All(x => !x.IsDefault))
        {
            accounts[0] = accounts[0] with { IsDefault = true };
        }

        Save(accounts);
        return true;
    }

    public static StoredAccount CreateOffline(string username, string? uuid)
    {
        var account = new StoredOfflineAccount(
            username,
            NormalizeUuid(uuid) ?? GenerateOfflineUuid(username),
            GenerateAccessToken()
        );
        return FromPayload("offline", account.Uuid, account.Username, account);
    }

    public static StoredAccount CreateMicrosoft(MicrosoftAccount account) =>
        FromPayload("microsoft", account.Uuid, account.Username, account);

    private static StoredAccount FromPayload<T>(
        string type,
        string uuid,
        string username,
        T payload
    ) =>
        new(
            uuid,
            username,
            type,
            DateTimeOffset.UtcNow,
            null,
            false,
            JsonSerializer.Serialize(payload, SerializerOptions)
        );

    private static string GenerateOfflineUuid(string playerName)
    {
        var raw = $"OfflinePlayer:{playerName}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash).ToString("N");
    }

    private static string? NormalizeUuid(string? uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
        {
            return null;
        }

        return Guid.TryParse(uuid, out var parsed)
            ? parsed.ToString("N")
            : throw new CliException("--uuid must be a valid UUID.", ExitCodes.Usage);
    }

    private static string GenerateAccessToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed record StoredAccount(
    string Uuid,
    string Username,
    string Type,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? LastUsedAt,
    bool IsDefault,
    string Data
);

public sealed record StoredOfflineAccount(string Username, string Uuid, string AccessToken)
{
    public string UserType => "legacy";
}
