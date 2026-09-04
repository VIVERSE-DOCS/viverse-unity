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
    /// Achievements client — retrieve and unlock achievements via REST.
    /// Supports:
    ///   - GetUserAchievements: get all achievements and unlock status
    ///   - UnlockAchievements: encrypted achievement unlock (RSA + AES)
    /// </summary>
    public class AchievementsClient
    {
        private const string BaseUrl = "https://www.viveport.com/";
        private const string AchievementPrefix = "api/optimusprime/v1/achievement";
        private const string IronhidePrefix = "api/ironhide/v1/token";

        private readonly string _appId;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ViverseAchievements_GetUserAchievements(int bridgeId, string gameObjectName, string callbackMethod,
            string appId, string token);

        [DllImport("__Internal")]
        private static extern void ViverseAchievements_UnlockAchievements(int bridgeId, string gameObjectName, string callbackMethod,
            string appId, string achievementsJson, string token);
#endif

        public AchievementsClient(string appId)
        {
            _appId = appId;
        }

        // --- Public API ---

        /// <summary>Get user achievements (requires authentication).</summary>
        public async Task<AchievementResult> GetUserAchievements(string token, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(token))
                return new AchievementResult { success = false, error = "token is required" };

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("GetUserAchievements", (bridgeId, go, cb) =>
                ViverseAchievements_GetUserAchievements(bridgeId, go, cb, _appId, token));
#else
            string url = $"{BaseUrl}{AchievementPrefix}/{_appId}";
            try
            {
                string response = await Get(url, token, ct);
                return new AchievementResult { success = true, data = response };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new AchievementResult { success = false, error = ex.Message };
            }
#endif
        }

        /// <summary>Unlock achievements with encryption (RSA + AES via Ironhide).</summary>
        /// <param name="achievementsJson">JSON array: [{"api_name":"x","unlock":true}]</param>
        public async Task<AchievementResult> UnlockAchievements(string achievementsJson, string token, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(token))
                return new AchievementResult { success = false, error = "token is required" };
            if (string.IsNullOrEmpty(achievementsJson))
                return new AchievementResult { success = false, error = "achievementsJson is required" };

            // Wrap array in object if needed: {"achievements": [...]}
            string bodyToEncrypt = achievementsJson.TrimStart();
            if (bodyToEncrypt.StartsWith("["))
                bodyToEncrypt = $"{{\"achievements\":{achievementsJson}}}";

#if UNITY_WEBGL && !UNITY_EDITOR
            return await CallWebGL("UnlockAchievements", (bridgeId, go, cb) =>
                ViverseAchievements_UnlockAchievements(bridgeId, go, cb, _appId, bodyToEncrypt, token));
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
                        return new AchievementResult { success = false, error = "Failed to parse session token response" };

                    string sessionToken = sessionDict.ContainsKey("token") ? sessionDict["token"].ToString() : null;
                    string encryptedKey = sessionDict.ContainsKey("key") ? sessionDict["key"].ToString() : null;

                    if (string.IsNullOrEmpty(sessionToken) || string.IsNullOrEmpty(encryptedKey))
                        return new AchievementResult { success = false, error = "Session token or key missing from response" };

                    // Step 3: Decrypt symmetric key with RSA private key
                    byte[] encryptedKeyBytes = Convert.FromBase64String(encryptedKey);
                    byte[] symmetricKeyBytes = rsa.Decrypt(encryptedKeyBytes, false); // PKCS1 padding
                    string symmetricKey = Encoding.UTF8.GetString(symmetricKeyBytes);

                    // Step 4: Encrypt achievements JSON with AES
                    string encryptedData = EncryptWithAes(symmetricKey, bodyToEncrypt);

                    // Step 5: POST encrypted data
                    string postUrl = $"{BaseUrl}{AchievementPrefix}/{_appId}";
                    string body = $"{{\"data\":\"{encryptedData}\"}}";
                    string response = await PostWithSessionToken(postUrl, body, token, sessionToken, ct);

                    return new AchievementResult { success = true, data = response };
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new AchievementResult { success = false, error = ex.Message };
            }
#endif
        }

        // --- Encryption helpers (Editor only) ---

#if !UNITY_WEBGL || UNITY_EDITOR
        private static string ExportPublicKeyX509(RSACryptoServiceProvider rsa)
        {
            var parameters = rsa.ExportParameters(false);
            byte[] modulus = parameters.Modulus;
            byte[] exponent = parameters.Exponent;

            byte[] rsaPublicKey = BuildDerSequence(
                BuildDerInteger(modulus),
                BuildDerInteger(exponent)
            );

            byte[] algorithmIdentifier = BuildDerSequence(
                new byte[] { 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01 },
                new byte[] { 0x05, 0x00 }
            );

            byte[] bitString = new byte[rsaPublicKey.Length + 1];
            bitString[0] = 0x00;
            Array.Copy(rsaPublicKey, 0, bitString, 1, rsaPublicKey.Length);

            byte[] subjectPublicKeyInfo = BuildDerSequence(
                algorithmIdentifier,
                BuildDerTagged(0x03, bitString)
            );

            return Convert.ToBase64String(subjectPublicKeyInfo);
        }

        private static byte[] BuildDerSequence(params byte[][] items)
        {
            int totalLen = items.Sum(i => i.Length);
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x30);
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
                ms.WriteByte(0x02);
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
        private async Task<AchievementResult> CallWebGL(string method, Action<int, string, string> invoke)
        {
            var tcs = new TaskCompletionSource<string>();
            int bridgeId = AchievementCallbackRegistry.Register(tcs);
            string receiverName = AuthManager.Instance.gameObject.name;
            invoke(bridgeId, receiverName, "OnAchievementCallback");
            string resultJson = await tcs.Task;
            var parsed = MiniJson.Deserialize(resultJson) as Dictionary<string, object>;
            if (parsed != null && parsed.ContainsKey("success") && parsed["success"] is bool s && s)
            {
                string data = parsed.ContainsKey("data") ? (parsed["data"] is string ds ? ds : MiniJson.Serialize(parsed["data"])) : null;
                return new AchievementResult { success = true, data = data };
            }
            string err = parsed?.ContainsKey("message") == true ? parsed["message"].ToString() : "operation failed";
            return new AchievementResult { success = false, error = err };
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
            req.SetRequestHeader("x-htc-op-token", sessionToken);
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
    public class AchievementResult
    {
        public bool success;
        public string data;   // JSON string of response data
        public string error;  // Error message if failed
    }

    /// <summary>
    /// Static callback registry for WebGL Achievement bridge.
    /// </summary>
    public static class AchievementCallbackRegistry
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
