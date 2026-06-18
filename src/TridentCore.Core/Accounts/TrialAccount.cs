using TridentCore.Abstractions.Accounts;

namespace TridentCore.Core.Accounts;

public class TrialAccount : IAccount
{
    #region IAccount Members

    public required string Username { get; init; }

    public required string Uuid { get; init; }

    public string AccessToken => "bird_is_the_word";

    public string UserType => "legacy";

    #endregion

    /// <summary>
    ///     本地渲染使用的内置皮肤 key（对应 <c>asset:{key}</c>），默认 <c>Steve</c>；
    ///     需要专属皮肤的角色（如愚人节限定的 Herobrine）在此指定。
    /// </summary>
    public string Skin { get; init; } = "Steve";

    public static TrialAccount CreateStewie() =>
        new() { Username = "Stewie", Uuid = Guid.NewGuid().ToString().Replace("-", string.Empty) };

    public static TrialAccount CreateBrian() =>
        new() { Username = "Brian", Uuid = Guid.NewGuid().ToString().Replace("-", string.Empty) };

    public static TrialAccount CreateChris() =>
        new() { Username = "Chris", Uuid = Guid.NewGuid().ToString().Replace("-", string.Empty) };

    public static TrialAccount CreatePeter() =>
        new() { Username = "Peter", Uuid = Guid.NewGuid().ToString().Replace("-", string.Empty) };

    public static TrialAccount CreateLois() =>
        new() { Username = "Lois", Uuid = Guid.NewGuid().ToString().Replace("-", string.Empty) };

    public static TrialAccount CreateHerobrine() =>
        new() { Username = "Herobrine", Uuid = Guid.NewGuid().ToString().Replace("-", string.Empty), Skin = "Herobrine" };
}
