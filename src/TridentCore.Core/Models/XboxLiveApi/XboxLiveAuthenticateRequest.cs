namespace TridentCore.Core.Models.XboxLiveApi;

// NOTE: 两个 Xbox 请求体各自独立、不做泛型化——Refit 源生成器无法为泛型请求体内联生成（RF006），
//  而客户端经 AddRefitGeneratedClient 注册、没有反射兜底，泛型化会让客户端直接构造不出来。
public record XboxLiveAuthenticateRequest(
    XboxLiveTokenProperties Properties,
    string RelyingParty,
    string TokenType = "JWT");
