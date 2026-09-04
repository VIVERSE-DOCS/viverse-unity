using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace ViverseSDK
{
    public class AvatarClient
    {
        private const string BaseUrl = "https://sdk-api.viverse.com/";
        private const string ProfileEndpoint = "api/meetingareaselector/v2/newgenavatar/sdk/me";
        private const string AvatarListEndpoint = "api/meetingareaselector/v1/newgenavatar/getavatarlist";
        private const string PublicAvatarEndpoint = "items/publicAvatar";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ViverseAvatar_GetProfile(int bridgeId, string gameObjectName, string callbackMethod, string token);

        [DllImport("__Internal")]
        private static extern void ViverseAvatar_GetAvatarList(int bridgeId, string gameObjectName, string callbackMethod, string token);

        [DllImport("__Internal")]
        private static extern void ViverseAvatar_GetPublicAvatarList(int bridgeId, string gameObjectName, string callbackMethod);

        [DllImport("__Internal")]
        private static extern void ViverseAvatar_GetPublicAvatarByID(int bridgeId, string gameObjectName, string callbackMethod, string avatarId);

        [DllImport("__Internal")]
        private static extern void ViverseAvatar_DownloadAvatarFile(int bridgeId, string gameObjectName, string callbackMethod, string vrmUrl, string token);

        [DllImport("__Internal")]
        private static extern int ViverseAvatar_CopyDownloadedBytes(int bridgeId, IntPtr dest, int destSize);
#endif

        public AvatarClient() { }

        public async Task<AvatarResult> GetProfile(string token, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(token))
                return new AvatarResult { success = false, error = "token is required" };

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("GetProfile", (bridgeId, go, cb) =>
                ViverseAvatar_GetProfile(bridgeId, go, cb, token));
#else
            try
            {
                string url = $"{BaseUrl}{ProfileEndpoint}";
                string response = await Get(url, token, ct);
                return new AvatarResult { success = true, data = response };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new AvatarResult { success = false, error = ex.Message };
            }
#endif
        }

        public async Task<AvatarResult> GetAvatarList(string token, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(token))
                return new AvatarResult { success = false, error = "token is required" };

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("GetAvatarList", (bridgeId, go, cb) =>
                ViverseAvatar_GetAvatarList(bridgeId, go, cb, token));
#else
            try
            {
                string url = $"{BaseUrl}{AvatarListEndpoint}";
                string response = await Get(url, token, ct);
                return new AvatarResult { success = true, data = response };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new AvatarResult { success = false, error = ex.Message };
            }
#endif
        }

        public async Task<AvatarResult> GetActiveAvatar(string token, CancellationToken ct = default)
        {
            var listResult = await GetAvatarList(token, ct);
            if (!listResult.success)
                return listResult;

            try
            {
                var root = MiniJson.Deserialize(listResult.data) as Dictionary<string, object>;
                if (root == null)
                    return new AvatarResult { success = false, error = "Failed to parse avatar list" };

                // Response: {"version":"...","data":{"CurrentAvatarId":N,"Avatars":[...]}}
                var data = root.ContainsKey("data") ? root["data"] as Dictionary<string, object> : root;
                if (data == null)
                    return new AvatarResult { success = false, error = "Missing data field" };

                string currentId = data.ContainsKey("CurrentAvatarId") ? data["CurrentAvatarId"]?.ToString() : null;
                if (string.IsNullOrEmpty(currentId))
                    return new AvatarResult { success = true, data = null };

                var avatarList = data.ContainsKey("Avatars") ? data["Avatars"] as List<object> : null;
                if (avatarList == null || avatarList.Count == 0)
                    return new AvatarResult { success = true, data = null };

                foreach (var item in avatarList)
                {
                    var avatar = item as Dictionary<string, object>;
                    if (avatar == null) continue;
                    string id = avatar.ContainsKey("id") ? avatar["id"]?.ToString() : null;
                    if (id == currentId)
                        return new AvatarResult { success = true, data = MiniJson.Serialize(avatar) };
                }

                return new AvatarResult { success = true, data = null };
            }
            catch (Exception ex)
            {
                return new AvatarResult { success = false, error = $"Parse error: {ex.Message}" };
            }
        }

        public async Task<AvatarResult> GetPublicAvatarList(CancellationToken ct = default)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("GetPublicAvatarList", (bridgeId, go, cb) =>
                ViverseAvatar_GetPublicAvatarList(bridgeId, go, cb));
#else
            try
            {
                string url = $"{BaseUrl}{PublicAvatarEndpoint}";
                string response = await GetNoAuth(url, ct);
                return new AvatarResult { success = true, data = response };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new AvatarResult { success = false, error = ex.Message };
            }
#endif
        }

        public async Task<AvatarResult> GetPublicAvatarByID(string avatarId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(avatarId))
                return new AvatarResult { success = false, error = "avatarId is required" };

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("GetPublicAvatarByID", (bridgeId, go, cb) =>
                ViverseAvatar_GetPublicAvatarByID(bridgeId, go, cb, avatarId));
#else
            try
            {
                string url = $"{BaseUrl}{PublicAvatarEndpoint}/{avatarId}";
                string response = await GetNoAuth(url, ct);
                return new AvatarResult { success = true, data = response };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new AvatarResult { success = false, error = ex.Message };
            }
#endif
        }

        /// <summary>
        /// Downloads avatar VRM file as raw bytes.
        /// WebGL: uses viverse Avatar SDK (handles decryption internally).
        /// Editor: direct HTTP download (works for unencrypted VRMs; encrypted VRMs need Avatar SDK).
        /// </summary>
        public async Task<AvatarDownloadResult> DownloadAvatarFile(string vrmUrl, string token = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(vrmUrl))
                return new AvatarDownloadResult { success = false, error = "vrmUrl is required" };

#if UNITY_WEBGL && !UNITY_EDITOR
            var tcs = new TaskCompletionSource<string>();
            int bridgeId = AvatarCallbackRegistry.Register(tcs);
            string receiverName = AuthManager.Instance.gameObject.name;
            ViverseAvatar_DownloadAvatarFile(bridgeId, receiverName, "OnAvatarCallback", vrmUrl, token ?? "");
            string resultJson = await tcs.Task;
            var parsed = MiniJson.Deserialize(resultJson) as Dictionary<string, object>;
            if (parsed != null && parsed.ContainsKey("success") && parsed["success"] is bool s && s)
            {
                int size = parsed.ContainsKey("size") ? Convert.ToInt32(parsed["size"]) : 0;
                if (size > 0)
                {
                    // Copy bytes from JS-side storage into managed byte array
                    byte[] buffer = new byte[size];
                    System.Runtime.InteropServices.GCHandle handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try
                    {
                        int copied = ViverseAvatar_CopyDownloadedBytes(bridgeId, handle.AddrOfPinnedObject(), size);
                        Debug.Log($"[Avatar] WebGL: copied {copied} bytes from JS buffer");
                        return new AvatarDownloadResult { success = true, size = copied, data = buffer };
                    }
                    finally
                    {
                        handle.Free();
                    }
                }
                return new AvatarDownloadResult { success = true, size = 0, data = new byte[0] };
            }
            string err = parsed?.ContainsKey("message") == true ? parsed["message"].ToString() : "download failed";
            return new AvatarDownloadResult { success = false, error = err };
#else
            try
            {
                var result = await DownloadBytes(vrmUrl, token, ct);
                return new AvatarDownloadResult { success = true, data = result, size = result.Length };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new AvatarDownloadResult { success = false, error = ex.Message };
            }
#endif
        }

        // --- WebGL bridge helper ---

#if UNITY_WEBGL && !UNITY_EDITOR
        private async Task<AvatarResult> CallWebGL(string method, Action<int, string, string> invoke)
        {
            var tcs = new TaskCompletionSource<string>();
            int bridgeId = AvatarCallbackRegistry.Register(tcs);
            string receiverName = AuthManager.Instance.gameObject.name;
            invoke(bridgeId, receiverName, "OnAvatarCallback");
            string resultJson = await tcs.Task;
            var parsed = MiniJson.Deserialize(resultJson) as Dictionary<string, object>;
            if (parsed != null && parsed.ContainsKey("success") && parsed["success"] is bool s && s)
            {
                string data = parsed.ContainsKey("data") ? (parsed["data"] is string ds ? ds : MiniJson.Serialize(parsed["data"])) : null;
                return new AvatarResult { success = true, data = data };
            }
            string err = parsed?.ContainsKey("message") == true ? parsed["message"].ToString() : "operation failed";
            return new AvatarResult { success = false, error = err };
        }
#else
        // --- Editor HTTP helpers ---

        private static Task<string> Get(string url, string token, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>();
            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("AccessToken", token);
            var op = req.SendWebRequest();
            ct.Register(() => { req.Abort(); tcs.TrySetCanceled(); });
            op.completed += _ =>
            {
                if (ct.IsCancellationRequested) return;
                if (req.result == UnityWebRequest.Result.Success || req.responseCode == 200)
                    tcs.TrySetResult(req.downloadHandler?.text ?? "");
                else
                    tcs.TrySetException(new Exception($"GET failed: {req.error} ({req.responseCode}) {req.downloadHandler?.text}"));
                req.Dispose();
            };
            return tcs.Task;
        }

        private static Task<string> GetNoAuth(string url, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>();
            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Content-Type", "application/json");
            var op = req.SendWebRequest();
            ct.Register(() => { req.Abort(); tcs.TrySetCanceled(); });
            op.completed += _ =>
            {
                if (ct.IsCancellationRequested) return;
                if (req.result == UnityWebRequest.Result.Success || req.responseCode == 200)
                    tcs.TrySetResult(req.downloadHandler?.text ?? "");
                else
                    tcs.TrySetException(new Exception($"GET failed: {req.error} ({req.responseCode}) {req.downloadHandler?.text}"));
                req.Dispose();
            };
            return tcs.Task;
        }

        private static Task<byte[]> DownloadBytes(string url, string token, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            var req = UnityWebRequest.Get(url);
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("AccessToken", token);
            var op = req.SendWebRequest();
            ct.Register(() => { req.Abort(); tcs.TrySetCanceled(); });
            op.completed += _ =>
            {
                if (ct.IsCancellationRequested) return;
                if (req.result == UnityWebRequest.Result.Success || req.responseCode == 200)
                    tcs.TrySetResult(req.downloadHandler?.data ?? new byte[0]);
                else
                    tcs.TrySetException(new Exception($"Download failed: {req.error} ({req.responseCode})"));
                req.Dispose();
            };
            return tcs.Task;
        }
#endif
    }

    [Serializable]
    public class AvatarResult
    {
        public bool success;
        public string data;
        public string error;
    }

    [Serializable]
    public class AvatarDownloadResult
    {
        public bool success;
        public byte[] data;
        public int size;
        public string error;
    }

    public static class AvatarCallbackRegistry
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
