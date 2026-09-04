using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace ViverseSDK
{
    /// <summary>
    /// Leaderboard client — score submission and ranking retrieval via REST.
    /// Supports:
    ///   - GetLeaderboard: authenticated ranking query
    ///   - GetGuestLeaderboard: unauthenticated ranking query
    ///   - SubmitScore: encrypted score submission (RSA + AES)
    /// </summary>
    public class LeaderboardClient
    {
        private const string BaseUrl = "https://www.viveport.com/";
        private const string RankingPrefix = "api/vrleaderboard/v1/apps";
        private const string IronhidePrefix = "api/ironhide/v1/token";

        private readonly string _appId;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ViverseLeaderboard_GetRanking(int bridgeId, string gameObjectName, string callbackMethod,
            string appId, string queryParams, string token);

        [DllImport("__Internal")]
        private static extern void ViverseLeaderboard_GetGuestRanking(int bridgeId, string gameObjectName, string callbackMethod,
            string appId, string queryParams);

        [DllImport("__Internal")]
        private static extern void ViverseLeaderboard_SubmitScore(int bridgeId, string gameObjectName, string callbackMethod,
            string appId, string scoresJson, string token);
#endif

        public LeaderboardClient(string appId)
        {
            _appId = appId;
        }

        // --- Public API ---

        /// <summary>Get leaderboard ranking (requires authentication).</summary>
        public async Task<LeaderboardResult> GetLeaderboard(string metaName, string token,
            int rangeStart = 0, int rangeEnd = 100, string region = "global",
            string timeRange = "alltime", bool aroundUser = false, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(token))
                return new LeaderboardResult { success = false, error = "token is required" };
            if (string.IsNullOrEmpty(metaName))
                return new LeaderboardResult { success = false, error = "metaName is required" };

            string queryParams = $"name={Uri.EscapeDataString(metaName)}&range_start={rangeStart}&range_end={rangeEnd}&region={region}&time_range={timeRange}&around_user={aroundUser.ToString().ToLower()}";

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("GetRanking", (bridgeId, go, cb) =>
                ViverseLeaderboard_GetRanking(bridgeId, go, cb, _appId, queryParams, token));
#else
            string url = $"{BaseUrl}{RankingPrefix}/{_appId}/metas/ranking?{queryParams}";
            try
            {
                string response = await Get(url, token, ct);
                return new LeaderboardResult { success = true, data = response };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new LeaderboardResult { success = false, error = ex.Message };
            }
#endif
        }

        /// <summary>Get guest leaderboard ranking (no authentication required).</summary>
        public async Task<LeaderboardResult> GetGuestLeaderboard(string metaName,
            int rangeStart = 0, int rangeEnd = 100, string region = "global",
            string timeRange = "alltime", string countryCode = "US", CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(metaName))
                return new LeaderboardResult { success = false, error = "metaName is required" };

            string queryParams = $"name={Uri.EscapeDataString(metaName)}&range_start={rangeStart}&range_end={rangeEnd}&region={region}&time_range={timeRange}&country_code={Uri.EscapeDataString(countryCode)}";

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("GetGuestRanking", (bridgeId, go, cb) =>
                ViverseLeaderboard_GetGuestRanking(bridgeId, go, cb, _appId, queryParams));
#else
            string url = $"{BaseUrl}{RankingPrefix}/{_appId}/metas/guest_ranking?{queryParams}";
            try
            {
                string response = await GetNoAuth(url, ct);
                return new LeaderboardResult { success = true, data = response };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new LeaderboardResult { success = false, error = ex.Message };
            }
#endif
        }

        /// <summary>Submit score with encryption (RSA + AES via Ironhide).</summary>
        public async Task<LeaderboardResult> SubmitScore(string metaName, string value, string token, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(token))
                return new LeaderboardResult { success = false, error = "token is required" };
            if (string.IsNullOrEmpty(metaName))
                return new LeaderboardResult { success = false, error = "metaName is required" };

            string scoresJson = $"{{\"scores\":[{{\"name\":\"{metaName}\",\"value\":\"{value}\"}}]}}";

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("SubmitScore", (bridgeId, go, cb) =>
                ViverseLeaderboard_SubmitScore(bridgeId, go, cb, _appId, scoresJson, token));
#else
            try
            {
                // Step 1: Generate RSA key pair
                using (var rsa = new RSACryptoServiceProvider(2048))
                {
                    rsa.PersistKeyInCsp = false;

                    // Export public key in X509/DER format (Base64, no PEM headers)
                    string publicKeyBase64 = ExportPublicKeyX509(rsa);

                    // Step 2: Get session token from Ironhide
                    string ironhideUrl = $"{BaseUrl}{IronhidePrefix}?app_id={_appId}&skip_ua=true";
                    string sessionResponse = await GetWithPublicKey(ironhideUrl, token, publicKeyBase64, ct);

                    var sessionDict = MiniJson.Deserialize(sessionResponse) as Dictionary<string, object>;
                    if (sessionDict == null)
                        return new LeaderboardResult { success = false, error = "Failed to parse session token response" };

                    string sessionToken = sessionDict.ContainsKey("token") ? sessionDict["token"].ToString() : null;
                    string encryptedKey = sessionDict.ContainsKey("key") ? sessionDict["key"].ToString() : null;

                    if (string.IsNullOrEmpty(sessionToken) || string.IsNullOrEmpty(encryptedKey))
                        return new LeaderboardResult { success = false, error = "Session token or key missing from response" };

                    // Step 3: Decrypt symmetric key with RSA private key
                    byte[] encryptedKeyBytes = Convert.FromBase64String(encryptedKey);
                    byte[] symmetricKeyBytes = rsa.Decrypt(encryptedKeyBytes, false); // PKCS1 padding
                    string symmetricKey = Encoding.UTF8.GetString(symmetricKeyBytes);

                    // Step 4: Encrypt scores JSON with AES
                    string encryptedScores = EncryptWithAes(symmetricKey, scoresJson);

                    // Step 5: POST encrypted data
                    string postUrl = $"{BaseUrl}{RankingPrefix}/{_appId}";
                    string body = $"{{\"scores\":\"{encryptedScores}\"}}";
                    await PostWithSessionToken(postUrl, body, token, sessionToken, ct);

                    return new LeaderboardResult { success = true };
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new LeaderboardResult { success = false, error = ex.Message };
            }
#endif
        }

        // --- Encryption helpers (Editor only) ---

#if !UNITY_WEBGL || UNITY_EDITOR
        /// <summary>Export RSA public key as X509 DER Base64 (no PEM headers).</summary>
        private static string ExportPublicKeyX509(RSACryptoServiceProvider rsa)
        {
            // RSACryptoServiceProvider doesn't directly export X509/DER.
            // We need to manually construct the ASN.1 SubjectPublicKeyInfo structure.
            var parameters = rsa.ExportParameters(false);
            byte[] modulus = parameters.Modulus;
            byte[] exponent = parameters.Exponent;

            // Build RSAPublicKey: SEQUENCE { INTEGER modulus, INTEGER exponent }
            byte[] rsaPublicKey = BuildDerSequence(
                BuildDerInteger(modulus),
                BuildDerInteger(exponent)
            );

            // Build SubjectPublicKeyInfo: SEQUENCE { SEQUENCE { OID, NULL }, BIT STRING }
            byte[] algorithmIdentifier = BuildDerSequence(
                new byte[] { 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01 }, // OID: rsaEncryption
                new byte[] { 0x05, 0x00 } // NULL
            );

            byte[] bitString = new byte[rsaPublicKey.Length + 1];
            bitString[0] = 0x00; // no unused bits
            Array.Copy(rsaPublicKey, 0, bitString, 1, rsaPublicKey.Length);

            byte[] subjectPublicKeyInfo = BuildDerSequence(
                algorithmIdentifier,
                BuildDerTagged(0x03, bitString) // BIT STRING
            );

            return Convert.ToBase64String(subjectPublicKeyInfo);
        }

        private static byte[] BuildDerSequence(params byte[][] items)
        {
            int totalLen = items.Sum(i => i.Length);
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x30); // SEQUENCE tag
                WriteDerLength(ms, totalLen);
                foreach (var item in items)
                    ms.Write(item, 0, item.Length);
                return ms.ToArray();
            }
        }

        private static byte[] BuildDerInteger(byte[] value)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x02); // INTEGER tag
                // Prepend 0x00 if high bit is set (to keep positive)
                if (value[0] >= 0x80)
                {
                    WriteDerLength(ms, value.Length + 1);
                    ms.WriteByte(0x00);
                }
                else
                {
                    WriteDerLength(ms, value.Length);
                }
                ms.Write(value, 0, value.Length);
                return ms.ToArray();
            }
        }

        private static byte[] BuildDerTagged(byte tag, byte[] value)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(tag);
                WriteDerLength(ms, value.Length);
                ms.Write(value, 0, value.Length);
                return ms.ToArray();
            }
        }

        private static void WriteDerLength(MemoryStream ms, int length)
        {
            if (length < 0x80)
            {
                ms.WriteByte((byte)length);
            }
            else if (length <= 0xFF)
            {
                ms.WriteByte(0x81);
                ms.WriteByte((byte)length);
            }
            else
            {
                ms.WriteByte(0x82);
                ms.WriteByte((byte)(length >> 8));
                ms.WriteByte((byte)(length & 0xFF));
            }
        }

        /// <summary>AES-CBC encrypt with PKCS7 padding. IV = first 16 chars of symmetricKey as UTF8.</summary>
        private static string EncryptWithAes(string symmetricKey, string plaintext)
        {
            byte[] keyBytes = Convert.FromBase64String(symmetricKey);
            byte[] ivBytes = Encoding.UTF8.GetBytes(symmetricKey.Substring(0, 16));
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            using (var aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.IV = ivBytes;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor())
                using (var ms = new MemoryStream())
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    cs.Write(plaintextBytes, 0, plaintextBytes.Length);
                    cs.FlushFinalBlock();
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }
#endif

        // --- WebGL bridge helper ---

#if UNITY_WEBGL && !UNITY_EDITOR
        private async Task<LeaderboardResult> CallWebGL(string method, Action<int, string, string> invoke)
        {
            var tcs = new TaskCompletionSource<string>();
            int bridgeId = LeaderboardCallbackRegistry.Register(tcs);
            string receiverName = AuthManager.Instance.gameObject.name;
            invoke(bridgeId, receiverName, "OnLeaderboardCallback");
            string resultJson = await tcs.Task;
            var parsed = MiniJson.Deserialize(resultJson) as Dictionary<string, object>;
            if (parsed != null && parsed.ContainsKey("success") && parsed["success"] is bool s && s)
            {
                string data = parsed.ContainsKey("data") ? (parsed["data"] is string ds ? ds : MiniJson.Serialize(parsed["data"])) : null;
                return new LeaderboardResult { success = true, data = data };
            }
            string err = parsed?.ContainsKey("message") == true ? parsed["message"].ToString() : "operation failed";
            return new LeaderboardResult { success = false, error = err };
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

        private static Task<string> GetWithPublicKey(string url, string token, string publicKeyBase64, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>();
            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-htc-public-key", publicKeyBase64);
            req.SetRequestHeader("x-htc-public-key-format", "x509");
            req.SetRequestHeader("AccessToken", token);
            var op = req.SendWebRequest();
            ct.Register(() => { req.Abort(); tcs.TrySetCanceled(); });
            op.completed += _ =>
            {
                if (ct.IsCancellationRequested) return;
                if (req.result == UnityWebRequest.Result.Success || req.responseCode == 200)
                    tcs.TrySetResult(req.downloadHandler?.text ?? "");
                else
                    tcs.TrySetException(new Exception($"Ironhide GET failed: {req.error} ({req.responseCode}) {req.downloadHandler?.text}"));
                req.Dispose();
            };
            return tcs.Task;
        }

        private static Task<string> PostWithSessionToken(string url, string body, string token, string sessionToken, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>();
            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Token", sessionToken);
            req.SetRequestHeader("AccessToken", token);
            var op = req.SendWebRequest();
            ct.Register(() => { req.Abort(); tcs.TrySetCanceled(); });
            op.completed += _ =>
            {
                if (ct.IsCancellationRequested) return;
                if (req.result == UnityWebRequest.Result.Success || req.responseCode == 200 || req.responseCode == 201)
                    tcs.TrySetResult(req.downloadHandler?.text ?? "");
                else
                    tcs.TrySetException(new Exception($"POST failed: {req.error} ({req.responseCode}) {req.downloadHandler?.text}"));
                req.Dispose();
            };
            return tcs.Task;
        }


#endif
    }

    [Serializable]
    public class LeaderboardResult
    {
        public bool success;
        public string data;   // JSON string of response data (null for write operations)
        public string error;  // Error message if failed
    }

    /// <summary>
    /// Static callback registry for WebGL Leaderboard bridge.
    /// </summary>
    public static class LeaderboardCallbackRegistry
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
