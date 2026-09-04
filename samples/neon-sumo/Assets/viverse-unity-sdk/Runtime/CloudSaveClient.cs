using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace ViverseSDK
{
    /// <summary>
    /// Cloud Save client — persistent player data storage via REST.
    /// Supports two storage patterns:
    ///   - UserApp: versioned full game saves (save/getAll/getLatest/delete)
    ///   - PlayerData: key-value pairs (setPlayerData/getPlayerData)
    /// </summary>
    public class CloudSaveClient
    {
        private const string BaseUrl = "https://broadcasting-gateway-gaming.vrprod.viveport.com";
        private const string UserAppPrefix = "/api/webrtcbot-service/v1/userapp";
        private const string CloudSavePrefix = "/api/webrtcbot-service/v1/cloudsave";

        private readonly string _appId;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ViverseCloudSave_Save(int bridgeId, string gameObjectName, string callbackMethod, string appId, string dataJson, string token);

        [DllImport("__Internal")]
        private static extern void ViverseCloudSave_GetAll(int bridgeId, string gameObjectName, string callbackMethod, string appId, string token);

        [DllImport("__Internal")]
        private static extern void ViverseCloudSave_GetLatest(int bridgeId, string gameObjectName, string callbackMethod, string appId, string token);

        [DllImport("__Internal")]
        private static extern void ViverseCloudSave_Delete(int bridgeId, string gameObjectName, string callbackMethod, string appId, string version, string token);

        [DllImport("__Internal")]
        private static extern void ViverseCloudSave_SetPlayerData(int bridgeId, string gameObjectName, string callbackMethod, string appId, string key, string dataJson, string token);

        [DllImport("__Internal")]
        private static extern void ViverseCloudSave_GetPlayerData(int bridgeId, string gameObjectName, string callbackMethod, string appId, string key, string token);
#endif

        public CloudSaveClient(string appId)
        {
            _appId = appId;
        }

        // --- UserApp: Versioned saves ---

        /// <summary>Save game data (creates a new version).</summary>
        public async Task<CloudSaveResult> Save(string dataJson, string token)
        {
            if (string.IsNullOrEmpty(token))
                return new CloudSaveResult { success = false, error = "token is required" };

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("ViverseCloudSave_Save", (bridgeId, go, cb) =>
                ViverseCloudSave_Save(bridgeId, go, cb, _appId, dataJson ?? "{}", token));
#else
            string url = $"{BaseUrl}{UserAppPrefix}/save";
            string body = $"{{\"app_id\":\"{_appId}\",\"data\":{dataJson ?? "{}"}}}";
            try
            {
                await Post(url, body, token);
                return new CloudSaveResult { success = true };
            }
            catch (Exception ex)
            {
                return new CloudSaveResult { success = false, error = ex.Message };
            }
#endif
        }

        /// <summary>Get all saved versions.</summary>
        public async Task<CloudSaveResult> GetAll(string token)
        {
            if (string.IsNullOrEmpty(token))
                return new CloudSaveResult { success = false, error = "token is required" };

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("ViverseCloudSave_GetAll", (bridgeId, go, cb) =>
                ViverseCloudSave_GetAll(bridgeId, go, cb, _appId, token));
#else
            string url = $"{BaseUrl}{UserAppPrefix}/{_appId}/all";
            try
            {
                string response = await Get(url, token);
                return new CloudSaveResult { success = true, data = response };
            }
            catch (Exception ex)
            {
                return new CloudSaveResult { success = false, error = ex.Message };
            }
#endif
        }

        /// <summary>Get the most recent save.</summary>
        public async Task<CloudSaveResult> GetLatest(string token)
        {
            if (string.IsNullOrEmpty(token))
                return new CloudSaveResult { success = false, error = "token is required" };

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("ViverseCloudSave_GetLatest", (bridgeId, go, cb) =>
                ViverseCloudSave_GetLatest(bridgeId, go, cb, _appId, token));
#else
            string url = $"{BaseUrl}{UserAppPrefix}/{_appId}/latest";
            try
            {
                string response = await Get(url, token);
                return new CloudSaveResult { success = true, data = response };
            }
            catch (Exception ex)
            {
                return new CloudSaveResult { success = false, error = ex.Message };
            }
#endif
        }

        /// <summary>Delete a specific version.</summary>
        public async Task<CloudSaveResult> Delete(long version, string token)
        {
            if (string.IsNullOrEmpty(token))
                return new CloudSaveResult { success = false, error = "token is required" };

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("ViverseCloudSave_Delete", (bridgeId, go, cb) =>
                ViverseCloudSave_Delete(bridgeId, go, cb, _appId, version.ToString(), token));
#else
            string url = $"{BaseUrl}{UserAppPrefix}/{_appId}/version/{version}";
            try
            {
                await DeleteRequest(url, token);
                return new CloudSaveResult { success = true };
            }
            catch (Exception ex)
            {
                return new CloudSaveResult { success = false, error = ex.Message };
            }
#endif
        }

        // --- PlayerData: Key-value storage ---

        /// <summary>Set a key-value pair in player data.</summary>
        public async Task<CloudSaveResult> SetPlayerData(string key, string dataJson, string token)
        {
            if (string.IsNullOrEmpty(token))
                return new CloudSaveResult { success = false, error = "token is required" };
            if (string.IsNullOrEmpty(key))
                return new CloudSaveResult { success = false, error = "key is required" };

            // Ensure dataJson is valid JSON object/array; wrap primitives
            // Server stores body under the key, so just wrap bare primitives as {"value": x}
            string body = dataJson ?? "{}";
            if (!body.TrimStart().StartsWith("{") && !body.TrimStart().StartsWith("["))
                body = $"{{\"value\":{body}}}";

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("ViverseCloudSave_SetPlayerData", (bridgeId, go, cb) =>
                ViverseCloudSave_SetPlayerData(bridgeId, go, cb, _appId, key, body, token));
#else
            string url = $"{BaseUrl}{CloudSavePrefix}/{_appId}/upsert/{key}";
            try
            {
                await Post(url, body, token);
                return new CloudSaveResult { success = true };
            }
            catch (Exception ex)
            {
                return new CloudSaveResult { success = false, error = ex.Message };
            }
#endif
        }

        /// <summary>Get player data (optionally filtered by key).</summary>
        public async Task<CloudSaveResult> GetPlayerData(string key, string token)
        {
            if (string.IsNullOrEmpty(token))
                return new CloudSaveResult { success = false, error = "token is required" };

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("ViverseCloudSave_GetPlayerData", (bridgeId, go, cb) =>
                ViverseCloudSave_GetPlayerData(bridgeId, go, cb, _appId, key ?? "", token));
#else
            string url = $"{BaseUrl}{CloudSavePrefix}/{_appId}";
            try
            {
                string response = await Get(url, token);
                // Extract specific key from data object
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(response))
                {
                    var root = MiniJson.Deserialize(response) as Dictionary<string, object>;
                    if (root != null && root.ContainsKey("data") && root["data"] is Dictionary<string, object> dataObj)
                    {
                        if (dataObj.ContainsKey(key))
                            return new CloudSaveResult { success = true, data = MiniJson.Serialize(dataObj[key]) };
                        else
                            return new CloudSaveResult { success = true, data = null };
                    }
                    return new CloudSaveResult { success = true, data = null };
                }
                return new CloudSaveResult { success = true, data = response };
            }
            catch (Exception ex)
            {
                return new CloudSaveResult { success = false, error = ex.Message };
            }
#endif
        }

        // --- WebGL bridge helper ---

#if UNITY_WEBGL && !UNITY_EDITOR
        private async Task<CloudSaveResult> CallWebGL(string method, Action<int, string, string> invoke)
        {
            var tcs = new TaskCompletionSource<string>();
            int bridgeId = CloudSaveCallbackRegistry.Register(tcs);
            string receiverName = AuthManager.Instance.gameObject.name;
            invoke(bridgeId, receiverName, "OnCloudSaveCallback");
            string resultJson = await tcs.Task;
            var parsed = MiniJson.Deserialize(resultJson) as Dictionary<string, object>;
            if (parsed != null && parsed.ContainsKey("success") && parsed["success"] is bool s && s)
            {
                string data = parsed.ContainsKey("data") ? (parsed["data"] is string ds ? ds : MiniJson.Serialize(parsed["data"])) : null;
                return new CloudSaveResult { success = true, data = data };
            }
            string err = parsed?.ContainsKey("message") == true ? parsed["message"].ToString() : "operation failed";
            return new CloudSaveResult { success = false, error = err };
        }
#else
        // --- Editor HTTP helpers ---

        private static Task<string> Post(string url, string body, string token)
        {
            var tcs = new TaskCompletionSource<string>();
            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            ApplyToken(req, token);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                if (req.result == UnityWebRequest.Result.Success || req.responseCode == 200 || req.responseCode == 204)
                    tcs.TrySetResult(req.downloadHandler?.text ?? "");
                else
                    tcs.TrySetException(new Exception($"POST failed: {req.error} ({req.responseCode}) {req.downloadHandler?.text}"));
                req.Dispose();
            };
            return tcs.Task;
        }

        private static Task<string> Get(string url, string token)
        {
            var tcs = new TaskCompletionSource<string>();
            var req = UnityWebRequest.Get(url);
            ApplyToken(req, token);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                if (req.result == UnityWebRequest.Result.Success || req.responseCode == 200 || req.responseCode == 204)
                    tcs.TrySetResult(req.downloadHandler?.text ?? "");
                else
                    tcs.TrySetException(new Exception($"GET failed: {req.error} ({req.responseCode}) {req.downloadHandler?.text}"));
                req.Dispose();
            };
            return tcs.Task;
        }

        private static Task<string> DeleteRequest(string url, string token)
        {
            var tcs = new TaskCompletionSource<string>();
            var req = UnityWebRequest.Delete(url);
            req.downloadHandler = new DownloadHandlerBuffer();
            ApplyToken(req, token);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                if (req.result == UnityWebRequest.Result.Success || req.responseCode == 200 || req.responseCode == 204)
                    tcs.TrySetResult(req.downloadHandler?.text ?? "");
                else
                    tcs.TrySetException(new Exception($"DELETE failed: {req.error} ({req.responseCode}) {req.downloadHandler?.text}"));
                req.Dispose();
            };
            return tcs.Task;
        }

        private static void ApplyToken(UnityWebRequest req, string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            bool isJwt = token.Split('.').Length == 3;
            req.SetRequestHeader(isJwt ? "AccessToken" : "AuthKey", token);
        }
#endif
    }

    [Serializable]
    public class CloudSaveResult
    {
        public bool success;
        public string data;   // JSON string of response data (null for write operations)
        public string error;  // Error message if failed
    }

    /// <summary>
    /// Static callback registry for WebGL Cloud Save bridge.
    /// </summary>
    public static class CloudSaveCallbackRegistry
    {
        private static int _counter;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<string>> _pending
            = new System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<string>>();

        public static int Register(TaskCompletionSource<string> tcs)
        {
            int id = System.Threading.Interlocked.Increment(ref _counter);
            _pending[id] = tcs;
            return id;
        }

        public static void Resolve(int bridgeId, string json)
        {
            if (_pending.TryRemove(bridgeId, out var tcs))
                tcs.TrySetResult(json);
        }

        public static void Reject(int bridgeId, string error)
        {
            if (_pending.TryRemove(bridgeId, out var tcs))
                tcs.TrySetException(new Exception(error));
        }
    }
}
