var LibraryViverseAvatar = {

  $Avatar_Helpers: {
    avatarSdkUrl: 'https://avatar.viverse.com/static-misc/avatar-js-sdk/1.1.1/Avatar-SDK.js',
    avatarSdkLoaded: false,
    avatarSdkInstance: null,
    downloadedBuffers: {},

    resolveUrl: function(path) {
      if (window.location.hostname === 'localhost')
        return window.location.origin + '/api/avatar/' + path;
      return 'https://sdk-api.viverse.com/' + path;
    },

    resolveFileUrl: function(url) {
      if (window.location.hostname === 'localhost') {
        // Localhost: route through local proxy
        if (url.indexOf('https://avatar.viverse.com/') === 0)
          return window.location.origin + '/api/avatar-files/' + url.substring(27);
        if (url.indexOf('https://sdk-api.viverse.com/') === 0)
          return window.location.origin + '/api/avatar/' + url.substring(28);
        return url;
      }
      // Deployed: route avatar.viverse.com through sdk-api.viverse.com (CORS-friendly)
      if (url.indexOf('https://avatar.viverse.com/') === 0)
        return 'https://sdk-api.viverse.com/' + url.substring(27);
      return url;
    },

    makeHeaders: function(token) {
      var h = { 'Content-Type': 'application/json' };
      if (token) h['AccessToken'] = token;
      return h;
    },

    sendResult: function(gameObjectName, callbackMethod, bridgeId, success, data, message) {
      var payload = { bridgeId: bridgeId, success: success };
      if (data !== undefined && data !== null) payload.data = data;
      if (message) payload.message = message;
      SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
    },

    loadAvatarSdk: function() {
      var self = Avatar_Helpers;
      if (self.avatarSdkLoaded) return Promise.resolve(self.avatarSdkInstance);
      return new Promise(function(resolve, reject) {
        var script = document.createElement('script');
        script.src = self.avatarSdkUrl;
        script.onload = function() {
          self.avatarSdkLoaded = true;
          // The Avatar SDK exposes globalThis.newViveAvatarSdk (factory function)
          if (globalThis.newViveAvatarSdk) {
            self.avatarSdkInstance = globalThis.newViveAvatarSdk(
              { workerMinimum: 1, workerMaximum: Math.max(1, (navigator.hardwareConcurrency || 2) - 1) },
              self.avatarSdkUrl
            );
            resolve(self.avatarSdkInstance);
          } else {
            resolve(null);
          }
        };
        script.onerror = function() { reject(new Error('Failed to load Avatar SDK')); };
        document.head.appendChild(script);
      });
    },

    // Fallback download: tries Avatar SDK viaWorker, then direct fetch
    _downloadFallback: function(bridgeId, go, cb, vrmUrl, token) {
      var H = Avatar_Helpers;
      H.loadAvatarSdk()
        .then(function(sdk) {
          // Strategy 2: Use Avatar SDK's viaWorker (Web Worker bypasses CORS)
          if (sdk && sdk.viaWorker) {
            return sdk.viaWorker({ action: 'downloadAndDecrypt', params: { modelUrl: vrmUrl } })
              .then(function(result) {
                var arrayBuffer = result.arrayBuffer || result;
                H.downloadedBuffers[bridgeId] = new Uint8Array(arrayBuffer);
                var payload = { bridgeId: bridgeId, success: true, size: arrayBuffer.byteLength };
                SendMessage(go, cb, JSON.stringify(payload));
              });
          }
          // Strategy 3: Direct fetch (works on localhost with proxy, or same-origin)
          var fetchUrl = H.resolveFileUrl(vrmUrl);
          var headers = {};
          if (token) headers['AccessToken'] = token;
          return fetch(fetchUrl, { headers: headers })
            .then(function(r) {
              if (!r.ok) throw new Error('Download failed (' + r.status + ')');
              return r.arrayBuffer();
            })
            .then(function(buf) {
              H.downloadedBuffers[bridgeId] = new Uint8Array(buf);
              var payload = { bridgeId: bridgeId, success: true, size: buf.byteLength };
              SendMessage(go, cb, JSON.stringify(payload));
            });
        })
        .catch(function(e) { H.sendResult(go, cb, bridgeId, false, null, e.message || 'download failed'); });
    }
  },

  ViverseAvatar_GetProfile__deps: ['$Avatar_Helpers'],
  ViverseAvatar_GetProfile: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var token = UTF8ToString(tokenPtr);
    var H = Avatar_Helpers;

    var url = H.resolveUrl('api/meetingareaselector/v2/newgenavatar/sdk/me');
    fetch(url, { headers: H.makeHeaders(token) })
      .then(function(r) {
        if (!r.ok) return r.text().then(function(t) { H.sendResult(go, cb, bridgeId, false, null, 'GET failed (' + r.status + '): ' + t); });
        return r.json().then(function(data) { H.sendResult(go, cb, bridgeId, true, JSON.stringify(data), null); });
      })
      .catch(function(e) { H.sendResult(go, cb, bridgeId, false, null, e.message); });
  },

  ViverseAvatar_GetAvatarList__deps: ['$Avatar_Helpers'],
  ViverseAvatar_GetAvatarList: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var token = UTF8ToString(tokenPtr);
    var H = Avatar_Helpers;

    var url = H.resolveUrl('api/meetingareaselector/v1/newgenavatar/getavatarlist');
    fetch(url, { headers: H.makeHeaders(token) })
      .then(function(r) {
        if (!r.ok) return r.text().then(function(t) { H.sendResult(go, cb, bridgeId, false, null, 'GET failed (' + r.status + '): ' + t); });
        return r.json().then(function(data) { H.sendResult(go, cb, bridgeId, true, JSON.stringify(data), null); });
      })
      .catch(function(e) { H.sendResult(go, cb, bridgeId, false, null, e.message); });
  },

  ViverseAvatar_GetPublicAvatarList__deps: ['$Avatar_Helpers'],
  ViverseAvatar_GetPublicAvatarList: function(bridgeId, gameObjectNamePtr, callbackMethodPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var H = Avatar_Helpers;

    var url = H.resolveUrl('items/publicAvatar');
    fetch(url, { headers: { 'Content-Type': 'application/json' } })
      .then(function(r) {
        if (!r.ok) return r.text().then(function(t) { H.sendResult(go, cb, bridgeId, false, null, 'GET failed (' + r.status + '): ' + t); });
        return r.json().then(function(data) { H.sendResult(go, cb, bridgeId, true, JSON.stringify(data), null); });
      })
      .catch(function(e) { H.sendResult(go, cb, bridgeId, false, null, e.message); });
  },

  ViverseAvatar_GetPublicAvatarByID__deps: ['$Avatar_Helpers'],
  ViverseAvatar_GetPublicAvatarByID: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, avatarIdPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var avatarId = UTF8ToString(avatarIdPtr);
    var H = Avatar_Helpers;

    var url = H.resolveUrl('items/publicAvatar/' + avatarId);
    fetch(url, { headers: { 'Content-Type': 'application/json' } })
      .then(function(r) {
        if (!r.ok) return r.text().then(function(t) { H.sendResult(go, cb, bridgeId, false, null, 'GET failed (' + r.status + '): ' + t); });
        return r.json().then(function(data) { H.sendResult(go, cb, bridgeId, true, JSON.stringify(data), null); });
      })
      .catch(function(e) { H.sendResult(go, cb, bridgeId, false, null, e.message); });
  },

  ViverseAvatar_DownloadAvatarFile__deps: ['$Avatar_Helpers'],
  ViverseAvatar_DownloadAvatarFile: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, vrmUrlPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var vrmUrl = UTF8ToString(vrmUrlPtr);
    var token = UTF8ToString(tokenPtr);
    var H = Avatar_Helpers;

    // Strategy 1: Use viverse-sdk Avatar.getAvatarFileWithSDK (handles CORS via Web Workers + decryption)
    var AvatarClass = window.viverse && (window.viverse.Avatar || window.viverse.avatar);
    if (AvatarClass && typeof AvatarClass === 'function') {
      try {
        var avatarClient = new AvatarClass({
          baseURL: 'https://sdk-api.viverse.com/',
          token: token
        });
        avatarClient.getAvatarFileWithSDK(vrmUrl)
          .then(function(arrayBuffer) {
            if (!arrayBuffer) throw new Error('getAvatarFileWithSDK returned null');
            H.downloadedBuffers[bridgeId] = new Uint8Array(arrayBuffer);
            var payload = { bridgeId: bridgeId, success: true, size: arrayBuffer.byteLength };
            SendMessage(go, cb, JSON.stringify(payload));
          })
          .catch(function(e) {
            // Fall through to strategy 2
            H._downloadFallback(bridgeId, go, cb, vrmUrl, token);
          });
        return;
      } catch(e) { /* fall through */ }
    }

    H._downloadFallback(bridgeId, go, cb, vrmUrl, token);
  },

  // Copy stored download bytes into a C# allocated buffer (WASM heap)
  ViverseAvatar_CopyDownloadedBytes__deps: ['$Avatar_Helpers'],
  ViverseAvatar_CopyDownloadedBytes: function(bridgeId, destPtr, destSize) {
    var H = Avatar_Helpers;
    var buf = H.downloadedBuffers[bridgeId];
    if (!buf) return 0;
    var copyLen = Math.min(buf.byteLength, destSize);
    HEAPU8.set(buf.subarray(0, copyLen), destPtr);
    // Free JS-side memory
    delete H.downloadedBuffers[bridgeId];
    return copyLen;
  }

};

mergeInto(LibraryManager.library, LibraryViverseAvatar);
