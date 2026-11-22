using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AramStats
{
    public partial class Form1
    {
        // Development-only cloud test: credentials are read from the current process
        // environment, never persisted in the application or shipped in the binary.
        private async Task ExportOrUploadCloudMatchesAsync(Button button)
        {
            if (string.IsNullOrWhiteSpace(activePuuid))
            {
                MessageBox.Show("먼저 롤 계정을 연결해 주세요.", "AramStats");
                return;
            }

            var candidates = savedMatches.Values
                .Where(m => m.QueueId == 2400 && Regex.IsMatch(m.GameId ?? "", @"^\d{1,20}$"))
                .GroupBy(m => m.GameId)
                .Select(g => g.First())
                .Where(m => m.Timestamp >= 1500000000000L &&
                            m.Timestamp <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 86400000L &&
                            savedDetails.TryGetValue(m.GameId, out var d) &&
                            IsValidCloudDetail(d))
                .OrderBy(m => m.Timestamp)
                .ToList();

            if (candidates.Count == 0)
            {
                MessageBox.Show("업로드 가능한 10명 상세 경기 기록이 없습니다.", "AramStats");
                return;
            }

            var endpoint = Environment.GetEnvironmentVariable("ARAMSTATS_SYNC_URL")?.Trim();
            var token = Environment.GetEnvironmentVariable("ARAMSTATS_UPLOAD_TOKEN")?.Trim();
            var hasCredentials = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
                                 uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(token);

            if (!hasCredentials)
            {
                using var save = new SaveFileDialog
                {
                    Title = "클라우드 업로드용 경기 데이터 내보내기",
                    Filter = "JSON 파일 (*.json)|*.json",
                    FileName = "aramstats_cloud_matches.json"
                };
                if (save.ShowDialog(this) != DialogResult.OK) return;
                var payloads = candidates.Select(m => new
                {
                    m.GameId, m.QueueId, m.Timestamp,
                    Participants = savedDetails[m.GameId].Participants
                }).ToList();
                File.WriteAllText(save.FileName, JsonSerializer.Serialize(payloads,
                    new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                MessageBox.Show($"{payloads.Count}경기를 내보냈습니다.\r\n\r\n이 파일에는 플레이어 PUUID와 게임 기록이 포함됩니다. 공유에 주의해 주세요.\r\n\r\n실제 서버 업로드는 환경 변수 ARAMSTATS_SYNC_URL, ARAMSTATS_UPLOAD_TOKEN이 모두 설정된 개발 PC에서만 가능합니다.", "AramStats");
                return;
            }

            // Developer safety: upload exactly one newest complete match per test.
            var uploadCandidates = candidates.OrderByDescending(m => m.Timestamp).Take(1).ToList();
            if (MessageBox.Show($"개발용 테스트: 최신 상세 경기 {uploadCandidates.Count}판만 서버에 업로드할까요?\r\n중복 경기 ID는 서버에서 무시됩니다.",
                "클라우드 업로드 (개발용)", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            button.Enabled = false;
            button.Text = "클라우드 업로드 중...";
            int uploaded = 0;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                foreach (var m in uploadCandidates)
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        m.GameId, m.QueueId, m.Timestamp,
                        Participants = savedDetails[m.GameId].Participants
                    });
                    using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                    request.Headers.TryAddWithoutValidation("x-upload-token", token);
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                    using var response = await client.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        throw new InvalidOperationException($"경기 {m.GameId}: HTTP {(int)response.StatusCode} / {body[..Math.Min(body.Length, 250)]}");
                    }
                    // An HTTP 200 response is not sufficient: the server must acknowledge success.
                    var responseText = await response.Content.ReadAsStringAsync();
                    using (var responseJson = JsonDocument.Parse(responseText))
                    {
                        if (!responseJson.RootElement.TryGetProperty("success", out var ok) ||
                            ok.ValueKind != JsonValueKind.True ||
                            !responseJson.RootElement.TryGetProperty("gameId", out var returnedId) ||
                            returnedId.GetString() != m.GameId)
                            throw new InvalidOperationException($"경기 {m.GameId}: 서버 응답 검증 실패");
                    }
                    uploaded++;
                    lblStatus.Text = $"상태: 클라우드 업로드 {uploaded}/{uploadCandidates.Count}";
                }
                MessageBox.Show($"서버가 {uploaded}경기 요청을 정상 수락했습니다.\r\n이미 저장된 경기의 중복 여부는 서버가 판단합니다.", "AramStats");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{uploaded}경기 요청 후 중단됐습니다.\r\n{ex.Message}", "클라우드 업로드 오류");
            }
            finally
            {
                button.Text = "클라우드 내보내기/업로드";
                button.Enabled = true;
            }
        }

        private static bool IsValidCloudDetail(MatchDetailRecord detail)
        {
            var p = detail?.Participants;
            if (p == null || p.Count != 10 ||
                p.Any(x => x == null) ||
                p.Select(x => x.ParticipantId).Distinct().Count() != 10 ||
                p.Select(x => x.Puuid).Distinct(StringComparer.Ordinal).Count() != 10 ||
                p.Count(x => x.TeamId == 100) != 5 || p.Count(x => x.TeamId == 200) != 5 ||
                p.Any(x => x.ParticipantId < 1 || x.ParticipantId > 10 ||
                           string.IsNullOrWhiteSpace(x.Puuid) || x.Puuid.Length < 10 ||
                           x.Puuid.Length > 200 || x.ChampionId <= 0 ||
                           x.Kills < 0 || x.Kills > 1000 ||
                           x.Deaths < 0 || x.Deaths > 1000 ||
                           x.Assists < 0 || x.Assists > 1000)) return false;
            var a = p.Where(x => x.TeamId == 100).Select(x => x.Win).Distinct().ToArray();
            var b = p.Where(x => x.TeamId == 200).Select(x => x.Win).Distinct().ToArray();
            return a.Length == 1 && b.Length == 1 && a[0] != b[0];
        }
    }
}
