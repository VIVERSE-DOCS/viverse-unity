using System;
using System.Collections.Concurrent;
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
    /// Lambda function invocation module — async job creation and polling.
    /// </summary>
    public class LambdaModule
    {
        private readonly MultiplayerClient _sdk;
        private bool _enabled;

        private const string PublicPrefix = "/api/play-lambda-service/v1";
        public const int DefaultPollIntervalMs = 1000;
        public const int DefaultPollTimeoutMs = 60000;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ViversePlay_Lambda_CreateJob(int requestId, string gameObjectName, string callbackMethodName, string gameId, string eventName, string dataJson, string requestIdStr, string token);

        [DllImport("__Internal")]
        private static extern void ViversePlay_Lambda_GetJobStatus(int requestId, string gameObjectName, string callbackMethodName, string jobId, string token);
#endif

        public LambdaModule(MultiplayerClient sdk)
        {
            _sdk = sdk;
            _enabled = false;
        }

        public void SetEnabled(bool enabled) => _enabled = enabled;
        public bool IsEnabled() => _enabled;

        /// <summary>
        /// Invoke a Lambda function. Polls until terminal state.
        /// </summary>
        public async Task<LambdaInvokeResult> Invoke(string eventName, string dataJson, string token, CancellationToken cancellationToken = default)
        {
            if (!_enabled)
                throw new InvalidOperationException("[LambdaModule] Module is not enabled. Set options.modules.lambda.enabled = true.");

            string requestId = Guid.NewGuid().ToString();

#if UNITY_WEBGL && !UNITY_EDITOR
            var tcs = new TaskCompletionSource<string>();
            int bridgeId = LambdaBridge.RegisterPending(tcs);
            ViversePlay_Lambda_CreateJob(bridgeId, MultiplayerClient.Instance.gameObject.name, nameof(MultiplayerClient.Instance.ReceiveMessageFromJS), _sdk.AppId, eventName, dataJson ?? "{}", requestId, token ?? "");
            string resultJson = await tcs.Task;
            return new LambdaInvokeResult { success = true, status = "succeeded", result = resultJson };
#else
            // Native: create job then poll
            string baseUrl = "https://broadcasting-gateway-gaming.vrprod.viveport.com";
            string url = $"{baseUrl}{PublicPrefix}/jobs";
            string body = $"{{\"game_id\":\"{_sdk.AppId}\",\"event_name\":\"{eventName}\",\"data\":{dataJson ?? "{}"},\"request_id\":\"{requestId}\"}}";

            string createResponse = await PostRequest(url, body, token);
            var createResult = MiniJson.Deserialize(createResponse) as System.Collections.Generic.Dictionary<string, object>;
            string jobId = null;

            if (createResult != null)
            {
                if (createResult.ContainsKey("job") && createResult["job"] is System.Collections.Generic.Dictionary<string, object> job)
                    jobId = job.ContainsKey("job_id") ? job["job_id"].ToString() : null;
                else if (createResult.ContainsKey("job_id"))
                    jobId = createResult["job_id"].ToString();
            }

            if (string.IsNullOrEmpty(jobId))
                return new LambdaInvokeResult { success = false, status = "failed", error = "no_job_id" };

            // Poll
            long startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            while (DateTimeOffset.Now.ToUnixTimeMilliseconds() - startTime < DefaultPollTimeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string statusUrl = $"{baseUrl}{PublicPrefix}/jobs/{jobId}";
                string statusResponse = await GetRequest(statusUrl, token);
                var statusResult = MiniJson.Deserialize(statusResponse) as System.Collections.Generic.Dictionary<string, object>;
                string status = statusResult?.ContainsKey("status") == true ? statusResult["status"].ToString() : "failed";

                if (status == "succeeded" || status == "failed" || status == "timeout")
                {
                    return new LambdaInvokeResult
                    {
                        success = status == "succeeded",
                        status = status,
                        result = statusResult?.ContainsKey("result") == true ? MiniJson.Serialize(statusResult["result"]) : null,
                        error = statusResult?.ContainsKey("error") == true ? statusResult["error"].ToString() : null
                    };
                }
                await Task.Delay(DefaultPollIntervalMs, cancellationToken);
            }
            throw new TimeoutException($"[LambdaModule] Job polling timed out after {DefaultPollTimeoutMs}ms");
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private Task<string> PostRequest(string url, string body, string token)
        {
            var tcs = new TaskCompletionSource<string>();
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            ApplyToken(req, token);
            var op = req.SendWebRequest();
            op.completed += (_) =>
            {
                if (req.result == UnityWebRequest.Result.Success)
                    tcs.TrySetResult(req.downloadHandler.text);
                else
                    tcs.TrySetException(new Exception(req.error));
                req.Dispose();
            };
            return tcs.Task;
        }

        private Task<string> GetRequest(string url, string token)
        {
            var tcs = new TaskCompletionSource<string>();
            var req = UnityWebRequest.Get(url);
            ApplyToken(req, token);
            var op = req.SendWebRequest();
            op.completed += (_) =>
            {
                if (req.result == UnityWebRequest.Result.Success)
                    tcs.TrySetResult(req.downloadHandler.text);
                else
                    tcs.TrySetException(new Exception(req.error));
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
    public class LambdaInvokeResult
    {
        public bool success;
        public string status;
        public string result;
        public string error;
    }

    /// <summary>
    /// Static registry for Lambda bridge callbacks (WebGL path).
    /// </summary>
    public static class LambdaBridge
    {
        private static int _counter;
        private static readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pending = new ConcurrentDictionary<int, TaskCompletionSource<string>>();

        public static int RegisterPending(TaskCompletionSource<string> tcs)
        {
            int id = Interlocked.Increment(ref _counter);
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
