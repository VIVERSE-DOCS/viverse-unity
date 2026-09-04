using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using ViverseSDK;

namespace NeonSumo
{
    /// <summary>
    /// Editor / non-WebGL stand-in for the REST bot path that SDK 1.2 Init no longer runs.
    /// Spawns game / network_sync / action_sync / leaderboard bots so master_notify can arrive.
    /// WebGL player builds skip this; the JS SDK Init path owns bots there.
    /// </summary>
    internal static class NeonSumoEditorGameBots
    {
        private const string LogPrefix = "[NeonSumoFlow][EditorBots]";
        private const string BotServiceBaseUrl = "https://broadcasting-gateway-gaming.vrprod.viveport.com";

        public static async Task EnsureGameAndRoomAsync(MultiplayerClient client, MultiplayerInitOptions options)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            await Task.CompletedTask;
#else
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrEmpty(client.AppId) || string.IsNullOrEmpty(client.RoomId))
                throw new InvalidOperationException("MultiplayerClient AppId and RoomId are required before Editor bot setup.");

            var bots = BuildBots(options);
            if (bots.Count == 0)
            {
                DebugLogger.Log($"{LogPrefix} No modules enabled; skipping bot REST.");
                return;
            }

            if (!await GameExistsAsync(client.AppId))
                await CreateGameAsync(client.AppId, options, bots);

            await CreateGameRoomAsync(client.RoomId, client.AppId);
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private static List<BotConfig> BuildBots(MultiplayerInitOptions options)
        {
            var bots = new List<BotConfig>();
            var modules = options?.modules;
            if (modules == null)
                return bots;

            if (modules.game != null && modules.game.enabled)
            {
                var game = modules.game;
                bots.Add(new BotConfig
                {
                    type = "game",
                    desc = string.IsNullOrEmpty(game.desc) ? "game description" : game.desc,
                    ready_time = game.ready_time,
                    start_delay_time = game.start_delay_time,
                    play_time = game.play_time,
                    total_player = game.total_player,
                    change_second = game.change_second,
                    min_total_player = game.min_total_player,
                    max_total_player = game.max_total_player,
                    wait_player_timeout = game.wait_player_timeout
                });
            }

            if (modules.networkSync != null && modules.networkSync.enabled)
            {
                bots.Add(new BotConfig
                {
                    type = "network_sync",
                    desc = string.IsNullOrEmpty(modules.networkSync.desc) ? "network sync description" : modules.networkSync.desc
                });
            }

            if (modules.actionSync != null && modules.actionSync.enabled)
            {
                bots.Add(new BotConfig
                {
                    type = "action_sync",
                    desc = string.IsNullOrEmpty(modules.actionSync.desc) ? "action sync description" : modules.actionSync.desc
                });
            }

            if (modules.leaderboard != null && modules.leaderboard.enabled)
            {
                bots.Add(new BotConfig
                {
                    type = "leaderboard",
                    desc = string.IsNullOrEmpty(modules.leaderboard.desc) ? "leaderboard description" : modules.leaderboard.desc
                });
            }

            return bots;
        }

        private static async Task<bool> GameExistsAsync(string appId)
        {
            string url = $"{BotServiceBaseUrl}/api/webrtcbot-service/v1/game/{appId}";
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("accept", "application/json");
                await request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    if (request.responseCode == 204)
                        return false;
                    DebugLogger.Log($"{LogPrefix} GetGame exists status={request.responseCode}");
                    return true;
                }

                if (request.responseCode == 404)
                    return false;

                throw new Exception($"GetGame failed. Status: {request.responseCode} - {request.error} - {request.downloadHandler?.text}");
            }
        }

        private static async Task CreateGameAsync(string appId, MultiplayerInitOptions options, List<BotConfig> bots)
        {
            int minPlayers = options?.modules?.game != null ? options.modules.game.min_total_player : 2;
            int maxPlayers = options?.modules?.game != null ? options.modules.game.max_total_player : 4;
            if (minPlayers < 1) minPlayers = 2;
            if (maxPlayers < minPlayers) maxPlayers = minPlayers;

            var body = new CreateGameBody
            {
                name = $"tmpname_{appId}",
                bots = bots,
                matchmaking = new MatchmakingConfig
                {
                    enable = true,
                    min_players = minPlayers,
                    max_players = maxPlayers
                }
            };

            string url = $"{BotServiceBaseUrl}/api/webrtcbot-service/v1/game/{appId}";
            using (var request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                }));
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("accept", "application/json");
                await request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success || request.responseCode == 409)
                {
                    DebugLogger.Log($"{LogPrefix} CreateGame status={request.responseCode}");
                    return;
                }

                throw new Exception($"CreateGame failed. Status: {request.responseCode} - {request.error} - {request.downloadHandler?.text}");
            }
        }

        private static async Task CreateGameRoomAsync(string roomId, string appId)
        {
            string url = $"{BotServiceBaseUrl}/api/webrtcbot-service/v1/room/{roomId}/game/{appId}";
            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("accept", "application/json");
                await request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success || request.responseCode == 409)
                {
                    DebugLogger.Log($"{LogPrefix} CreateGameRoom roomId={roomId} status={request.responseCode}");
                    return;
                }

                throw new Exception($"CreateGameRoom failed. Status: {request.responseCode} - {request.error} - {request.downloadHandler?.text}");
            }
        }

        private sealed class BotConfig
        {
            public string type;
            public string desc;
            public int? ready_time;
            public float? start_delay_time;
            public int? play_time;
            public int? total_player;
            public int? change_second;
            public int? min_total_player;
            public int? max_total_player;
            public int? wait_player_timeout;
        }

        private sealed class MatchmakingConfig
        {
            public bool enable;
            public int min_players;
            public int max_players;
        }

        private sealed class CreateGameBody
        {
            public string name;
            public List<BotConfig> bots;
            public MatchmakingConfig matchmaking;
        }
#endif
    }
}
