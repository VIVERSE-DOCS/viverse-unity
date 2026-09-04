mergeInto(LibraryManager.library, {

    _clientConfig: null,
    _gameObjectName: null,

    // VIVERSE UMD (globalThis.viverse).
    _viverseSdkUrl: 'https://www.viverse.com/static-assets/viverse-sdk/1.3.3/index.umd.cjs',

    VIVERSE_LoadSDK: function (gameObjectNameStr) {
        var gameObjectName = UTF8ToString(gameObjectNameStr);
        this._gameObjectName = gameObjectName;
        console.log('[VIVERSE] VIVERSE_LoadSDK gameObject:', gameObjectName);

        if (globalThis.viverse) {
            console.log('[VIVERSE] globalThis.viverse already loaded — skipping script inject, notifying C# OnSDKLoaded.');
            if (typeof SendMessage === 'function') {
                SendMessage(gameObjectName, 'OnSDKLoaded', '');
            }
            return;
        }

        var script = document.createElement('script');
        console.log('[VIVERSE] Dynamically loading VIVERSE SDK from', this._viverseSdkUrl);

        script.onload = function() {
            console.log('[VIVERSE] VIVERSE SDK loaded successfully.');
            if (typeof SendMessage === 'function') {
                SendMessage(gameObjectName, 'OnSDKLoaded', '');
            }
        };

        script.onerror = function() {
            console.error('[VIVERSE] Failed to load VIVERSE SDK.');
            if (typeof SendMessage === 'function') {
                SendMessage(gameObjectName, 'OnSDKLoadFailed', '');
            }
        };

        script.type = 'text/javascript';
        script.defer = true;
        script.src = this._viverseSdkUrl;
        document.head.appendChild(script);
    },

    VIVERSE_InitializeClient: function (clientIdStr, domainStr, cookieDomainStr, gameObjectNameStr) {
        var clientId = UTF8ToString(clientIdStr);
        var domain = UTF8ToString(domainStr);
        var cookieDomain = cookieDomainStr && UTF8ToString(cookieDomainStr).length > 0 ? UTF8ToString(cookieDomainStr) : null;

        var goName = UTF8ToString(gameObjectNameStr);
        if (goName) this._gameObjectName = goName;

        console.log('[VIVERSE] Initializing client. Domain:', domain);

        var clientConfig = { clientId: clientId, domain: domain };
        if (cookieDomain) {
            clientConfig.cookieDomain = cookieDomain;
        }

        this._clientConfig = clientConfig;
        globalThis.viverseClient = new globalThis.viverse.client(clientConfig);
    },

    VIVERSE_LoginWithWorlds: function (stateStr) {
        if (!globalThis.viverseClient) {
            console.error('[VIVERSE] Client not initialized.');
            if (typeof SendMessage === 'function') {
                SendMessage(this._gameObjectName, 'HandleLoginFailure', "Client not initialized for login");
            }
            return;
        }

        var state = stateStr ? UTF8ToString(stateStr) : undefined;
        var options = state ? { state: state } : {};
        console.log('[VIVERSE] loginWithWorlds state:', state);
        globalThis.viverseClient.loginWithWorlds(options);
    },

    VIVERSE_CheckAuth: async function() {
        if (!globalThis.viverseClient) {
            console.error('[VIVERSE] Client not initialized.');
            if (typeof SendMessage === 'function') {
                SendMessage(this._gameObjectName, 'HandleLoginFailure', "Client not initialized");
            }
            return;
        }

        try {
            const result = await globalThis.viverseClient.checkAuth();
            if (result) {
                var resultJson = JSON.stringify(result);
                if (typeof SendMessage === 'function') {
                    SendMessage(this._gameObjectName, 'HandleLoginSuccess', resultJson);
                }
            } else {
                console.log('[VIVERSE] checkAuth: no existing token');
                if (typeof SendMessage === 'function') {
                    SendMessage(this._gameObjectName, 'HandleLoginFailure', "No existing token found");
                }
            }
        } catch (error) {
            console.error('[VIVERSE] checkAuth error', error);
            if (typeof SendMessage === 'function') {
                SendMessage(this._gameObjectName, 'HandleLoginFailure', "Auth check error: " + error.message);
            }
        }
    }

});
