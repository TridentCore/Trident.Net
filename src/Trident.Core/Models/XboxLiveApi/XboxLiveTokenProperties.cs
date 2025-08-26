namespace Trident.Core.Models.XboxLiveApi
{
    public readonly record struct XboxLiveTokenProperties(
        string RpsTicket,
        string SiteName = "user.auth.xboxlive.com",
        string AuthMethod = "RPS");
}
