mergeInto(LibraryManager.library, {

    $ViverseAuth_State: {
        gameObjectName: null,
        client: null,
        appId: null,
        sdkLoaded: false,
        sdkLoading: false
    },

    // ============================================================
    // Load viverse-sdk UMD from CDN
    // ============================================================
    ViverseAuth_LoadSDK: function (gameObjectNameStr) {
        var gameObjectName = UTF8ToString(gameObjectNameStr);
        ViverseAuth_State.gameObjectName = gameObjectName;

        if (globalThis.viverse) {
            console.log("[ViverseAuth] SDK already loaded.");
            ViverseAuth_State.sdkLoaded = true;
            SendMessage(gameObjectName, 'OnAuthSDKLoaded', '');
            return;
        }

        if (ViverseAuth_State.sdkLoading) {
            console.log("[ViverseAuth] SDK already loading...");
            return;
        }

        ViverseAuth_State.sdkLoading = true;
        console.log("[ViverseAuth] Loading viverse-sdk from CDN...");

        var script = document.createElement('script');
        script.type = 'text/javascript';
        script.defer = true;
        script.src = "https://www.viverse.com/static-assets/viverse-sdk/index.umd.cjs";

        script.onload = function () {
            console.log("[ViverseAuth] viverse-sdk loaded successfully.");
            ViverseAuth_State.sdkLoaded = true;
            ViverseAuth_State.sdkLoading = false;
            SendMessage(gameObjectName, 'OnAuthSDKLoaded', '');
        };

        script.onerror = function () {
            console.error("[ViverseAuth] Failed to load viverse-sdk.");
            ViverseAuth_State.sdkLoading = false;
            SendMessage(gameObjectName, 'OnAuthSDKLoadFailed', 'Failed to load viverse-sdk from CDN');
        };

        document.head.appendChild(script);
    },
    ViverseAuth_LoadSDK__deps: ['$ViverseAuth_State'],

    // ============================================================
    // Initialize viverse client
    // ============================================================
    ViverseAuth_InitClient: function (clientIdStr, domainStr) {
        var appId = UTF8ToString(clientIdStr);
        var domain = UTF8ToString(domainStr);

        if (!globalThis.viverse) {
            console.error("[ViverseAuth] SDK not loaded. Call ViverseAuth_LoadSDK first.");
            SendMessage(ViverseAuth_State.gameObjectName, 'OnAuthError', 'SDK not loaded');
            return;
        }

        try {
            // In iframe (viverse.com): use app ID for loginWithWorlds
            // Standalone: use shared OAuth client ID for loginWithRedirect
            var inIframe = (window !== window.parent);
            var clientId = inIframe ? appId : '42ab6113-acc9-419e-93ca-e0734baf9d3d';
            var config = { clientId: clientId, domain: domain };
            if (!inIframe) {
                config.authorizationParams = { authorities: 'htc.com google.com steam.com' };
            }
            ViverseAuth_State.appId = appId;
            ViverseAuth_State.client = new globalThis.viverse.Client(config);
            console.log("[ViverseAuth] Client initialized. inIframe:", inIframe);
            SendMessage(ViverseAuth_State.gameObjectName, 'OnAuthClientInitialized', '');
        } catch (e) {
            console.error("[ViverseAuth] Init failed:", e);
            SendMessage(ViverseAuth_State.gameObjectName, 'OnAuthError', 'Init failed: ' + e.message);
        }
    },
    ViverseAuth_InitClient__deps: ['$ViverseAuth_State'],

    // ============================================================
    // Check existing auth (cookie/cache) + handle redirect callback
    // ============================================================
    ViverseAuth_CheckAuth: async function () {
        if (!ViverseAuth_State.client) {
            SendMessage(ViverseAuth_State.gameObjectName, 'OnAuthError', 'Client not initialized');
            return;
        }

        try {
            // Handle OAuth redirect callback (loginWithRedirect flow)
            if (window.location.search.includes('code=') && window.location.search.includes('state=')) {
                console.log("[ViverseAuth] Handling redirect callback...");
                var result = await ViverseAuth_State.client.handleRedirectCallback();
                if (result && result.access_token) {
                    console.log("[ViverseAuth] Redirect callback success.");
                    var json = JSON.stringify(result);
                    SendMessage(ViverseAuth_State.gameObjectName, 'OnAuthSuccess', json);
                    // Clean URL
                    window.history.replaceState({}, document.title, window.location.pathname);
                    return;
                }
            }

            // Normal checkAuth (iframe flow or cached token)
            var result = await ViverseAuth_State.client.checkAuth();
            if (result && result.access_token) {
                console.log("[ViverseAuth] Existing auth found.");
                var json = JSON.stringify(result);
                SendMessage(ViverseAuth_State.gameObjectName, 'OnAuthSuccess', json);
            } else {
                console.log("[ViverseAuth] No existing auth.");
                SendMessage(ViverseAuth_State.gameObjectName, 'OnAuthNotLoggedIn', '');
            }
        } catch (e) {
            console.log("[ViverseAuth] CheckAuth error:", e.message);
            SendMessage(ViverseAuth_State.gameObjectName, 'OnAuthNotLoggedIn', '');
        }
    },
    ViverseAuth_CheckAuth__deps: ['$ViverseAuth_State'],

    // ============================================================
    // Login with VIVERSE (iframe: loginWithWorlds, standalone: loginWithRedirect)
    // ============================================================
    ViverseAuth_LoginWithWorlds: function () {
        if (!ViverseAuth_State.client) {
            SendMessage(ViverseAuth_State.gameObjectName, 'OnAuthError', 'Client not initialized');
            return;
        }

        // Detect if running inside an iframe (viverse.com)
        var inIframe = (window !== window.parent);

        if (inIframe) {
            console.log("[ViverseAuth] In iframe, using loginWithWorlds...");
            ViverseAuth_State.client.loginWithWorlds();
            // loginWithWorlds triggers page refresh; checkAuth on reload will get the token
        } else {
            console.log("[ViverseAuth] Standalone, using loginWithRedirect...");
            ViverseAuth_State.client.loginWithRedirect().catch(function (e) {
                console.error("[ViverseAuth] loginWithRedirect failed:", e);
                SendMessage(ViverseAuth_State.gameObjectName, 'OnAuthError', 'Login failed: ' + e.message);
            });
            // loginWithRedirect navigates away; handleRedirectCallback on return will get the token
        }
    },
    ViverseAuth_LoginWithWorlds__deps: ['$ViverseAuth_State'],

    // ============================================================
    // Logout
    // ============================================================
    ViverseAuth_Logout: function () {
        if (!ViverseAuth_State.client) {
            return;
        }

        try {
            ViverseAuth_State.client.logout();
            console.log("[ViverseAuth] Logged out.");
        } catch (e) {
            console.log("[ViverseAuth] Logout error:", e.message);
        }
        SendMessage(ViverseAuth_State.gameObjectName, 'OnAuthLoggedOut', '');
    },
    ViverseAuth_Logout__deps: ['$ViverseAuth_State']
});
