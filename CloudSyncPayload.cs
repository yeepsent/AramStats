using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AramStats;

/// <summary>
/// Builds validated queue-2400 match upload payloads without handling credentials
/// or making network requests. Safe to use from a desktop client.
/// </summary>
internal static class CloudSyncPayload
{
    internal sealed record MatchInfo(
        string GameId, int QueueId, long Timestamp);

    internal sealed record ParticipantInfo(
        int ParticipantId, string Puuid, string Name, int TeamId,
        int ChampionId, bool Win, int Kills, int Deaths, int Assists,
        int? DamageToChampions = null, int? DamageTaken = null,
        int? DamageMitigated = null, int? Healing = null,
        int? AllyHealing = null, int? AllyShielding = null,
        double? CrowdControlSeconds = null);

    internal sealed record MatchDetails(
        string GameId, IReadOnlyList<ParticipantInfo> Participants);

    internal sealed record UploadPayload(
        string GameId, int QueueId, long Timestamp,
        IReadOnlyList<ParticipantInfo> Participants);

    internal static IReadOnlyList<UploadPayload> Build(
        IEnumerable<MatchInfo> matches,
        IEnumerable<MatchDetails> details)
    {
        var detailById = details
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.GameId))
            .GroupBy(d => d.GameId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var result = new List<UploadPayload>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var match in matches)
        {
            if (match is null || match.QueueId != 2400 ||
                !seen.Add(match.GameId) ||
                !long.TryParse(match.GameId, out _) ||
                match.Timestamp < 1500000000000L ||
                !detailById.TryGetValue(match.GameId, out var detail))
                continue;

            var people = detail.Participants;
            if (people is null || people.Count != 10 ||
                people.Any(p => p is null ||
                    p.ParticipantId < 1 || p.ParticipantId > 10 ||
                    string.IsNullOrWhiteSpace(p.Puuid) ||
                    p.Puuid.Length < 10 || p.Puuid.Length > 200 ||
                    (p.TeamId != 100 && p.TeamId != 200) ||
                    p.ChampionId <= 0 ||
                    p.Kills < 0 || p.Deaths < 0 || p.Assists < 0) ||
                people.Select(p => p.ParticipantId).Distinct().Count() != 10 ||
                people.Select(p => p.Puuid).Distinct(StringComparer.Ordinal).Count() != 10 ||
                people.Count(p => p.TeamId == 100) != 5 ||
                people.Count(p => p.TeamId == 200) != 5 ||
                people.Where(p => p.TeamId == 100).Select(p => p.Win).Distinct().Count() != 1 ||
                people.Where(p => p.TeamId == 200).Select(p => p.Win).Distinct().Count() != 1 ||
                people[0].Win == people.First(p => p.TeamId != people[0].TeamId).Win)
                continue;

            result.Add(new UploadPayload(
                match.GameId, match.QueueId, match.Timestamp, people));
        }
        return result;
    }

    internal static string ToJson(UploadPayload payload) =>
        JsonSerializer.Serialize(payload);
}
