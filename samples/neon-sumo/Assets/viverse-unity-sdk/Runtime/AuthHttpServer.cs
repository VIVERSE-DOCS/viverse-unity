using System;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;

namespace ViverseSDK
{
    /// <summary>
    /// AuthHttpServer — Editor-only local HTTP server for OAuth callback.
    /// Serves a login page that loads viverse-sdk, authenticates, and POSTs token back.
    /// </summary>
    public class AuthHttpServer : MonoBehaviour
    {
        public const int PORT = 40078;
        public event Action<AuthResult> OnAuthResult;

        private HttpListener _listener;
        private string _appId;

        public void StartServerAndLogin(string appId)
        {
            _appId = appId;

            if (_listener != null && _listener.IsListening)
            {
                Debug.Log("[AuthHttpServer] Server already running, opening browser...");
                Application.OpenURL($"http://localhost:{PORT}/");
                return;
            }

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{PORT}/");
                _listener.Start();
                Debug.Log($"[AuthHttpServer] Started on port {PORT}");

                ThreadPool.QueueUserWorkItem(_ => ListenLoop());
                Application.OpenURL($"http://localhost:{PORT}/");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthHttpServer] Failed to start: {ex.Message}");
            }
        }

        private void ListenLoop()
        {
            while (_listener != null && _listener.IsListening)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    HandleRequest(ctx);
                }
                catch (HttpListenerException) { /* Server closed */ }
                catch (Exception ex)
                {
                    Debug.LogError($"[AuthHttpServer] Request error: {ex.Message}");
                }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath;
            string method = ctx.Request.HttpMethod;

            if (path == "/api/viverse/auth/callback" && method == "POST")
            {
                using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                {
                    string body = reader.ReadToEnd();
                    Debug.Log($"[AuthHttpServer] Received callback: {body}");

                    // Parse and dispatch on main thread
                    MainThreadDispatcher.Enqueue(() => ProcessCallback(body));

                    // Respond OK
                    byte[] resp = System.Text.Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = resp.Length;
                    ctx.Response.OutputStream.Write(resp, 0, resp.Length);
                    ctx.Response.OutputStream.Close();
                }
            }
            else
            {
                // Serve login HTML page
                string html = GenerateLoginHtml();
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(html);
                ctx.Response.ContentType = "text/html";
                ctx.Response.ContentLength64 = buffer.Length;
                ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
                ctx.Response.OutputStream.Close();
            }
        }

        private void ProcessCallback(string body)
        {
            try
            {
                // Body format: {"data": {"access_token": "...", "account_id": "...", "expires_in": 3600}}
                var wrapper = JsonUtility.FromJson<AuthCallbackWrapper>(body);
                if (wrapper?.data != null && !string.IsNullOrEmpty(wrapper.data.access_token))
                {
                    OnAuthResult?.Invoke(wrapper.data);
                }
                else
                {
                    Debug.LogWarning("[AuthHttpServer] Callback received but no token found");
                    OnAuthResult?.Invoke(null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthHttpServer] Parse error: {ex.Message}");
                OnAuthResult?.Invoke(null);
            }
        }

        private string GenerateLoginHtml()
        {
            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>VIVERSE Login</title>
    <script defer src=""https://www.viverse.com/static-assets/viverse-sdk/index.umd.cjs""></script>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; background: #1a1a2e; color: #eee; }}
        .container {{ text-align: center; background: #16213e; padding: 40px; border-radius: 12px; box-shadow: 0 8px 32px rgba(0,0,0,0.3); }}
        h1 {{ margin-bottom: 16px; font-size: 24px; }}
        p {{ color: #aaa; margin-bottom: 24px; }}
        .status {{ margin-top: 16px; padding: 12px; background: #0f3460; border-radius: 6px; font-family: monospace; font-size: 13px; min-height: 20px; }}
        .success {{ color: #4caf50; }}
        .error {{ color: #f44336; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>VIVERSE Login</h1>
        <p>Authenticating with App ID: <strong>{_appId}</strong></p>
        <div class=""status"" id=""status"">Initializing SDK...</div>
    </div>
    <script>
        const oauthClientId = '42ab6113-acc9-419e-93ca-e0734baf9d3d';
        const domain = 'account.htcvive.com';
        const statusEl = document.getElementById('status');

        function setStatus(msg, cls) {{
            statusEl.textContent = msg;
            statusEl.className = 'status ' + (cls || '');
        }}

        function waitForSDK() {{
            return new Promise((resolve) => {{
                if (window.viverse && window.viverse.Client) {{ resolve(); return; }}
                const check = setInterval(() => {{
                    if (window.viverse && window.viverse.Client) {{ clearInterval(check); resolve(); }}
                }}, 200);
                setTimeout(() => {{ clearInterval(check); resolve(); }}, 10000);
            }});
        }}

        async function main() {{
            await waitForSDK();

            if (!window.viverse || !window.viverse.Client) {{
                setStatus('Failed to load VIVERSE SDK', 'error');
                return;
            }}

            setStatus('SDK loaded. Initializing client...');
            const client = new window.viverse.Client({{
                clientId: oauthClientId,
                domain: domain,
                authorizationParams: {{
                    authorities: 'htc.com google.com steam.com'
                }}
            }});

            // Check if URL has auth callback params
            if (window.location.search.includes('code=') && window.location.search.includes('state=')) {{
                setStatus('Processing auth callback...');
                try {{
                    const result = await client.handleRedirectCallback();
                    setStatus('Login successful! Sending token to Unity...', 'success');

                    await fetch('/api/viverse/auth/callback', {{
                        method: 'POST',
                        headers: {{ 'Content-Type': 'application/json' }},
                        body: JSON.stringify({{ data: result }})
                    }});

                    setStatus('Done! You can close this tab.', 'success');
                }} catch (e) {{
                    setStatus('Callback error: ' + e.message, 'error');
                }}
            }} else {{
                setStatus('Redirecting to login...');
                try {{
                    await client.loginWithRedirect();
                }} catch (e) {{
                    setStatus('Login redirect failed: ' + e.message, 'error');
                }}
            }}
        }}

        window.addEventListener('load', main);
    </script>
</body>
</html>";
        }

        private void OnDestroy()
        {
            if (_listener != null)
            {
                _listener.Close();
                _listener = null;
            }
        }

        [Serializable]
        private class AuthCallbackWrapper
        {
            public AuthResult data;
        }
    }

    /// <summary>
    /// Simple main-thread dispatcher for callbacks from background threads.
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        private static readonly System.Collections.Generic.Queue<Action> _queue = new System.Collections.Generic.Queue<Action>();

        public static void Initialize()
        {
            EnsureInstance();
        }

        public static void Enqueue(Action action)
        {
            EnsureInstance();
            lock (_queue) { _queue.Enqueue(action); }
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("[MainThreadDispatcher]");
            _instance = go.AddComponent<MainThreadDispatcher>();
            DontDestroyOnLoad(go);
        }

        void Update()
        {
            lock (_queue)
            {
                while (_queue.Count > 0)
                    _queue.Dequeue()?.Invoke();
            }
        }

        void OnDestroy() { if (_instance == this) _instance = null; }
    }
}
