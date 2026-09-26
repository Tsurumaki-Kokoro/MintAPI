using System.Net.Http.Headers;
using System.Text.Json;
using IdentityModel.Client;

namespace Ossapi.Auth;

/// <summary>
/// DelegatingHandler that injects a Bearer token into every request.
/// Supports Client Credentials grant only (Phase 1).
/// Authorization Code grant will be added in Phase 4.
/// </summary>
public class OssapiAuthHandler : DelegatingHandler
{
    private readonly int _clientId;
    private readonly string _clientSecret;
    private readonly string _tokenEndpoint;
    private readonly TokenStore _tokenStore;
    private readonly string _tokenKey;

    private TokenStore.StoredToken? _cachedToken;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public OssapiAuthHandler(int clientId, string clientSecret,
        string tokenEndpoint, TokenStore tokenStore)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _tokenEndpoint = tokenEndpoint;
        _tokenStore = tokenStore;
        _tokenKey = TokenStore.GenerateKey(
            OAuthGrant.ClientCredentials, clientId, clientSecret, ["public"]);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetValidTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await base.SendAsync(request, cancellationToken);

        // If 401, the token may have been revoked — refresh and retry once
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _cachedToken = null;
            _tokenStore.Delete(_tokenKey);
            token = await GetValidTokenAsync(cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            response = await base.SendAsync(request, cancellationToken);
        }

        return response;
    }

    private async Task<TokenStore.StoredToken> GetValidTokenAsync(CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is null)
                _cachedToken = _tokenStore.Load(_tokenKey);

            if (_cachedToken is not null && _cachedToken.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
                return _cachedToken;

            _cachedToken = await FetchClientCredentialsTokenAsync(ct);
            _tokenStore.Save(_tokenKey, _cachedToken);
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<TokenStore.StoredToken> FetchClientCredentialsTokenAsync(CancellationToken ct)
    {
        using var client = new HttpClient();
        var response = await client.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
        {
            Address      = _tokenEndpoint,
            ClientId     = _clientId.ToString(),
            ClientSecret = _clientSecret,
            Scope        = "public",
        }, ct);

        if (response.IsError)
            throw new InvalidOperationException(
                $"Failed to obtain access token: {response.Error} — {response.ErrorDescription}");

        return new TokenStore.StoredToken(
            AccessToken:  response.AccessToken!,
            RefreshToken: response.RefreshToken,
            ExpiresAt:    DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn - 30));
    }
}
