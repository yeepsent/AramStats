using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AramStats;

internal sealed class GroupSyncClient(Uri project, string publishableKey) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        BaseAddress = project,
        Timeout = TimeSpan.FromSeconds(20)
    };
    private Session? session;
    private DateTimeOffset refreshAt;
    private sealed record Session(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string RefreshToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);

    internal sealed record Member(Guid Id, string DisplayName);
    internal sealed record Roster(Guid GroupId, string GroupName, Guid OwnId, string OwnPuuid, Member[] Members);
    internal sealed record GroupParticipant(Guid MemberId, int TeamId, int ChampionId, bool Win, int Kills, int Deaths, int Assists);
    internal sealed record Game(long Cursor, string GameId, long Timestamp, GroupParticipant[] Members);
    internal sealed record UploadResult(string GameId, string Status);

    internal async Task SignInAsync(string email, string password) =>
        await AuthenticateAsync("password", new { email, password });

    internal Task<Roster> GetRosterAsync() => RpcAsync<Roster>("aramstats_roster", new { });

    internal Task<Game[]> DownloadAsync(Guid group, long after) =>
        RpcAsync<Game[]>("aramstats_download", new { p_group = group, p_after = after });

    internal Task<UploadResult[]> UploadAsync(Guid group, object matches) =>
        RpcAsync<UploadResult[]>("aramstats_upload", new { p_group = group, p_matches = matches });

    private async Task<T> RpcAsync<T>(string function, object body)
    {
        if (session is null)
            throw new InvalidOperationException("공동 통계에 로그인해 주세요.");
        if (DateTimeOffset.UtcNow >= refreshAt)
            await AuthenticateAsync("refresh_token", new { refresh_token = session.RefreshToken });
        return await SendAsync<T>("rest/v1/rpc/" + function, body, session.AccessToken);
    }

    private async Task AuthenticateAsync(string grantType, object body)
    {
        var next = await SendAsync<Session>("auth/v1/token?grant_type=" + grantType, body, null);
        if (string.IsNullOrWhiteSpace(next.AccessToken) || string.IsNullOrWhiteSpace(next.RefreshToken)
            || next.ExpiresIn <= 0)
            throw new JsonException("공동 통계 로그인 응답이 올바르지 않습니다.");
        session = next;
        refreshAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, next.ExpiresIn - 60));
    }

    private async Task<T> SendAsync<T>(string path, object body, string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.Add("apikey", publishableKey);
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            // GoTrue reports invalid credentials/refresh tokens as 400 on the token endpoint.
            var status = accessToken is null && response.StatusCode == HttpStatusCode.BadRequest
                ? HttpStatusCode.Unauthorized : response.StatusCode;
            throw new HttpRequestException($"공동 서버 응답 HTTP {(int)status}", null, status);
        }
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new JsonException("공동 서버가 빈 응답을 반환했습니다.");
    }

    public void Dispose() => http.Dispose();
}
