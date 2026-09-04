using System;
using System.Collections.Generic;
using ViverseSDK;

namespace NeonSumo
{
    /// <summary>
    /// Parses VIVERSE SDK 1.2 matchmaking JSON at the lobby boundary.
    /// Field names are taken from MatchmakingClient.SendRequest, MatchmakingDemo, and
    /// MatchmakingClient.ParseAndSetCurrentRoom — unknown shapes are logged, not guessed silently.
    /// </summary>
    internal static class NeonSumoMatchmakingJson
    {
        private const string LogPrefix = "[NeonSumoLobby][Json]";

        public static string SerializePayload(Dictionary<string, object> payload)
        {
            return MiniJson.Serialize(payload);
        }

        public static bool TryParseSuccess(string json, out bool success, out string message)
        {
            success = true;
            message = null;
            var dict = DeserializeObject(json);
            if (dict == null)
                return false;

            if (dict.TryGetValue("message", out var msgObj) && msgObj != null)
                message = msgObj.ToString();

            if (!dict.TryGetValue("success", out var successObj) || successObj == null)
                return false;

            success = ToBool(successObj);
            return true;
        }

        public static Room ParseRoomFromJoinOrCreateResponse(string json)
        {
            var dict = DeserializeObject(json);
            if (dict == null)
            {
                DebugLogger.LogWarning($"{LogPrefix} Join/Create response was not an object: {json}");
                return null;
            }

            var roomDict = ResolveRoomDictionary(dict);
            if (roomDict == null)
            {
                DebugLogger.LogWarning($"{LogPrefix} No room object in join/create JSON: {json}");
                return null;
            }

            return MapRoom(roomDict);
        }

        public static List<Room> ParseRoomList(string json)
        {
            var rooms = new List<Room>();
            var dict = DeserializeObject(json);
            if (dict == null)
            {
                var asList = MiniJson.Deserialize(json) as List<object>;
                if (asList != null)
                    AppendRoomsFromList(asList, rooms);
                else
                    DebugLogger.LogWarning($"{LogPrefix} Room list JSON was not an object or array: {json}");
                return rooms;
            }

            if (TryGetList(dict, "rooms", out var roomsList))
            {
                AppendRoomsFromList(roomsList, rooms);
                return rooms;
            }

            if (dict.TryGetValue("data", out var data))
            {
                if (data is List<object> dataList)
                {
                    AppendRoomsFromList(dataList, rooms);
                    return rooms;
                }

                if (data is Dictionary<string, object> dataObj)
                {
                    if (TryGetList(dataObj, "rooms", out var nested))
                    {
                        AppendRoomsFromList(nested, rooms);
                        return rooms;
                    }

                    var single = MapRoom(dataObj);
                    if (single != null)
                        rooms.Add(single);
                    return rooms;
                }

                if (data is string dataStr && !string.IsNullOrEmpty(dataStr))
                    return ParseRoomList(dataStr);
            }

            // GetAvailableRooms with no rooms omits the rooms key entirely:
            // { "success": true, "message": "..." }. That is an empty list, not a parse failure.
            if (!dict.ContainsKey("success") || ToBool(dict, "success"))
            {
                DebugLogger.Log($"{LogPrefix} Room-list omitted rooms; treating as empty.");
                return rooms;
            }

            DebugLogger.LogWarning($"{LogPrefix} Room-list JSON had no rooms/data array. Raw: {json}");
            return rooms;
        }

        public static Actor ParseActor(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            var dict = DeserializeObject(json);
            if (dict == null)
                return null;

            if (dict.TryGetValue("data", out var data) && data is Dictionary<string, object> nested)
                dict = nested;
            if (dict.TryGetValue("actor", out var actorObj) && actorObj is Dictionary<string, object> actorDict)
                dict = actorDict;

            var actor = new Actor();
            if (dict.TryGetValue("session_id", out var sid) && sid != null)
                actor.session_id = NeonSumoUserId.Normalize(sid.ToString());
            if (dict.TryGetValue("name", out var name) && name != null)
                actor.name = name.ToString();
            if (dict.TryGetValue("is_master_client", out var master) && master != null)
                actor.is_master_client = ToBool(master);
            return actor;
        }

        public static bool IsOwnedByLocalActor(Room room, Actor actor)
        {
            if (room == null || actor == null)
                return false;
            string masterId = NeonSumoUserId.Normalize(room.master_client_id);
            string sessionId = NeonSumoUserId.Normalize(actor.session_id);
            return !string.IsNullOrEmpty(masterId)
                   && string.Equals(masterId, sessionId, StringComparison.Ordinal);
        }

        private static Dictionary<string, object> ResolveRoomDictionary(Dictionary<string, object> dict)
        {
            if (dict.TryGetValue("room", out var roomObj) && roomObj is Dictionary<string, object> room)
                return room;

            if (dict.TryGetValue("data", out var data))
            {
                if (data is Dictionary<string, object> dataDict)
                    return dataDict;
                if (data is string dataStr && !string.IsNullOrEmpty(dataStr))
                    return DeserializeObject(dataStr);
            }

            if (dict.ContainsKey("id") || dict.ContainsKey("room_id") || dict.ContainsKey("room_name") || dict.ContainsKey("name"))
                return dict;

            return null;
        }

        private static Room MapRoom(Dictionary<string, object> roomDict)
        {
            if (roomDict == null)
                return null;

            var room = new Room();
            if (roomDict.TryGetValue("id", out var id) && id != null)
                room.id = id.ToString();
            else if (roomDict.TryGetValue("room_id", out var roomId) && roomId != null)
                room.id = roomId.ToString();

            if (roomDict.TryGetValue("name", out var name) && name != null)
                room.name = name.ToString();
            else if (roomDict.TryGetValue("room_name", out var roomName) && roomName != null)
                room.name = roomName.ToString();

            if (roomDict.TryGetValue("mode", out var mode) && mode != null)
                room.mode = mode.ToString();
            if (roomDict.TryGetValue("game_session", out var gs) && gs != null)
                room.game_session = gs.ToString();
            if (roomDict.TryGetValue("master_client_id", out var master) && master != null)
                room.master_client_id = NeonSumoUserId.Normalize(master.ToString());

            room.max_players = ToInt(roomDict, "max_players");
            room.min_players = ToInt(roomDict, "min_players");
            room.is_closed = ToBool(roomDict, "is_closed");
            room.is_game_started = ToBool(roomDict, "is_game_started");

            if (TryGetList(roomDict, "actors", out var actors))
            {
                room.actors = new List<Actor>(actors.Count);
                for (int i = 0; i < actors.Count; i++)
                {
                    if (actors[i] is Dictionary<string, object> actorDict)
                        room.actors.Add(MapActor(actorDict));
                }
            }

            return room;
        }

        private static Actor MapActor(Dictionary<string, object> dict)
        {
            var actor = new Actor();
            if (dict.TryGetValue("session_id", out var sid) && sid != null)
                actor.session_id = NeonSumoUserId.Normalize(sid.ToString());
            if (dict.TryGetValue("name", out var name) && name != null)
                actor.name = name.ToString();
            if (dict.TryGetValue("is_master_client", out var master) && master != null)
                actor.is_master_client = ToBool(master);
            return actor;
        }

        private static void AppendRoomsFromList(List<object> list, List<Room> rooms)
        {
            if (list == null)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is Dictionary<string, object> roomDict)
                {
                    var mapped = MapRoom(roomDict);
                    if (mapped != null)
                        rooms.Add(mapped);
                }
            }
        }

        private static Dictionary<string, object> DeserializeObject(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            return MiniJson.Deserialize(json) as Dictionary<string, object>;
        }

        private static bool TryGetList(Dictionary<string, object> dict, string key, out List<object> list)
        {
            list = null;
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
                return false;
            list = value as List<object>;
            return list != null;
        }

        private static int ToInt(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
                return 0;
            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private static bool ToBool(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
                return false;
            return ToBool(value);
        }

        private static bool ToBool(object value)
        {
            if (value is bool b)
                return b;
            if (value is string s)
                return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1";
            try
            {
                return Convert.ToInt32(value) != 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
