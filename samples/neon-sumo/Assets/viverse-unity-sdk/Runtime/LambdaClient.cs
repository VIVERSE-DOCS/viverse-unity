using System;
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
    /// Standalone Lambda client — pure REST, no multiplayer dependency.
    /// Thin interface: creates jobs and polls for results.
    /// </summary>
    public class LambdaClient
    {
        private const string BaseUrl = "https://broadcasting-gateway-gaming.vrprod.viveport.com";
        private const string Prefix = "/api/play-lambda-service/v1";
        public const int DefaultPollIntervalMs = 1000;
        public const int DefaultPollTimeoutMs = 60000;

        private readonly string _appId;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ViverseLambda_CreateJob(int bridgeId, string gameObjectName, string callbackMethod, string gameId, string eventName, string dataJson, string requestId, string token);

        [DllImport("__Internal")]
        private static extern void ViverseLambda_GetJobStatus(int bridgeId, string gameObjectName, string callbackMethod, string jobId, string token);
#endif

        public LambdaClient(string appId)
        {
            _appId = appId;
        }

        /// <summary>
        /// Invoke a Lambda function by event name. Creates job then polls until terminal state.
        /// </summary>
        public async Task<LambdaResult> Invoke(string eventName, string dataJson, string token, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(eventName))
                return new LambdaResult { success = false, status = "failed", error = "event_name is required" };
            if (string.IsNullOrEmpty(token))
                return new LambdaResult { success = false, status = "failed", error = "token is required" };

            string requestId = Guid.NewGuid().ToString();
            string cleanData = (dataJson ?? "{}").Replace("\\\"", "\"");

#if UNITY_WEBGL && !UNITY_EDITOR
            return await InvokeWebGL(eventName, cleanData, requestId, token);
#else
            return await InvokeEditor(eventName, cleanData, requestId, token, ct);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private async Task<LambdaResult> InvokeWebGL(string eventName, string dataJson, string requestId, string token)
        {
            var tcs = new TaskCompletionSource<string>();
            int bridgeId = LambdaCallbackRegistry.Register(tcs);
            string receiverName = AuthManager.Instance.gameObject.name;
            string callbackMethod = "OnLambdaCallback";
            ViverseLambda_CreateJob(bridgeId, receiverName, callbackMethod, _appId, eventName, dataJson, requestId, token);
            string resultJson = await tcs.Task;
            var parsed = MiniJson.Deserialize(resultJson) as System.Collections.Generic.Dictionary<string, object>;
            if (parsed != null && parsed.ContainsKey("success") && parsed["success"] is bool s && s)
                return new LambdaResult { success = true, status = "succeeded", result = resultJson };
            string err = parsed?.ContainsKey("message") == true ? parsed["message"].ToString() : "invoke failed";
            return new LambdaResult { success = false, status = "failed", error = err };
        }
#else
        private async Task<LambdaResult> InvokeEditor(string eventName, string dataJson, string requestId, string token, CancellationToken ct)
        {
            string url = $"{BaseUrl}{Prefix}/jobs";
            string body = $"{{\"game_id\":\"{_appId}\",\"event_name\":\"{eventName}\",\"data\":{dataJson},\"request_id\":\"{requestId}\"}}";
            string createResponse = await Post(url, body, token);
            var createResult = MiniJson.Deserialize(createResponse) as System.Collections.Generic.Dictionary<string, object>;
            string jobId = ExtractJobId(createResult);

            if (string.IsNullOrEmpty(jobId))
                return new LambdaResult { success = false, status = "failed", error = "no job_id in response", result = createResponse };

            // Poll until terminal state
            long startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            while (DateTimeOffset.Now.ToUnixTimeMilliseconds() - startTime < DefaultPollTimeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(DefaultPollIntervalMs, ct);

                string statusResponse = await Get($"{BaseUrl}{Prefix}/jobs/{jobId}", token);
                var statusResult = MiniJson.Deserialize(statusResponse) as System.Collections.Generic.Dictionary<string, object>;
                // Status may be at root or nested under "job"
                var jobObj = statusResult;
                if (statusResult != null && statusResult.ContainsKey("job") && statusResult["job"] is System.Collections.Generic.Dictionary<string, object> nested)
                    jobObj = nested;
                string status = jobObj?.ContainsKey("status") == true ? jobObj["status"].ToString() : "unknown";

                if (status == "succeeded" || status == "failed" || status == "timeout")
                {
                    return new LambdaResult
                    {
                        success = status == "succeeded",
                        status = status,
                        result = jobObj?.ContainsKey("result") == true ? MiniJson.Serialize(jobObj["result"]) : null,
                        error = jobObj?.ContainsKey("error") == true ? jobObj["error"].ToString() : null
                    };
                }
            }
            return new LambdaResult { success = false, status = "timeout", error = $"Polling timed out after {DefaultPollTimeoutMs}ms" };
        }

        private static string ExtractJobId(System.Collections.Generic.Dictionary<string, object> response)
        {
            if (response == null) return null;
            // Try nested: { "job": { "job_id": "..." } }
            if (response.ContainsKey("job") && response["job"] is System.Collections.Generic.Dictionary<string, object> job)
            {
                if (job.ContainsKey("job_id")) return job["job_id"].ToString();
            }
            // Try flat: { "job_id": "..." }
            if (response.ContainsKey("job_id")) return response["job_id"].ToString();
            return null;
        }

        private static Task<string> Post(string url, string body, string token)
        {
            var tcs = new TaskCompletionSource<string>();
            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("accept", "application/json");
            ApplyToken(req, token);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                if (req.result == UnityWebRequest.Result.Success)
                    tcs.TrySetResult(req.downloadHandler.text);
                else
                {
                    string respBody = req.downloadHandler?.text ?? "";
                    tcs.TrySetException(new Exception($"[Lambda] POST failed: {req.error} ({req.responseCode}) body: {respBody}"));
                }
                req.Dispose();
            };
            return tcs.Task;
        }

        private static Task<string> Get(string url, string token)
        {
            var tcs = new TaskCompletionSource<string>();
            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("accept", "application/json");
            ApplyToken(req, token);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                if (req.result == UnityWebRequest.Result.Success)
                    tcs.TrySetResult(req.downloadHandler.text);
                else
                    tcs.TrySetException(new Exception($"[Lambda] GET failed: {req.error} ({req.responseCode})"));
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
    public class LambdaResult
    {
        public bool success;
        public string status;
        public string result;
        public string error;
    }

    /// <summary>
    /// Static callback registry for WebGL Lambda bridge.
    /// </summary>
    public static class LambdaCallbackRegistry
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
