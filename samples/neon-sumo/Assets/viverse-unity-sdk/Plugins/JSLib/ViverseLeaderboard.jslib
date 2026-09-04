var LibraryViverseLeaderboard = {

  $Leaderboard_Helpers: {
    // Environment-aware base URL:
    //   localhost → relative '/' (serve_webgl.sh proxies /api/* to viveport.com)
    //   deployed  → absolute 'https://www.viveport.com/' (direct, CORS allowed from VIVERSE domains)
    baseUrl: (function() {
      var h = (typeof window !== 'undefined') ? window.location.hostname : '';
      return (h === 'localhost' || h === '127.0.0.1') ? '/' : 'https://www.viveport.com/';
    })(),
    rankingPrefix: 'api/vrleaderboard/v1/apps',
    ironhidePrefix: 'api/ironhide/v1/token',

    makeHeaders: function(token, includeContent) {
      var h = {};
      if (includeContent) h['Content-Type'] = 'application/json';
      if (token) {
        var isJwt = token.split('.').length === 3;
        h[isJwt ? 'AccessToken' : 'AuthKey'] = token;
      }
      return h;
    },

    sendResult: function(gameObjectName, callbackMethod, bridgeId, success, data, message) {
      var payload = { bridgeId: bridgeId, success: success };
      if (data !== undefined && data !== null) payload.data = data;
      if (message) payload.message = message;
      SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
    }
  },

  ViverseLeaderboard_GetRanking__deps: ['$Leaderboard_Helpers'],
  ViverseLeaderboard_GetRanking: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, appIdPtr, queryParamsPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var appId = UTF8ToString(appIdPtr);
    var queryParams = UTF8ToString(queryParamsPtr);
    var token = UTF8ToString(tokenPtr);
    var H = Leaderboard_Helpers;

    // Use viverse-sdk GameDashboard.getLeaderboard (maps community display names automatically)
    var GameDashboard = window.viverse && (window.viverse.GameDashboard || window.viverse.gameDashboard);
    if (GameDashboard) {
      try {
        var isLocal = window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1';
        var client = new GameDashboard({
          baseURL: isLocal ? window.location.origin + '/' : 'https://www.viveport.com/',
          communityBaseURL: 'https://www.viverse.com/',
          token: token
        });

        // Parse queryParams string into config object
        var params = {};
        queryParams.split('&').forEach(function(pair) {
          var kv = pair.split('=');
          if (kv.length === 2) {
            var v = decodeURIComponent(kv[1]);
            // Convert numeric strings to numbers for range_start/range_end
            if (kv[0] === 'range_start' || kv[0] === 'range_end') v = parseInt(v, 10);
            // Convert boolean strings
            else if (v === 'true') v = true;
            else if (v === 'false') v = false;
            params[kv[0]] = v;
          }
        });

        client.getLeaderboard(appId, params)
          .then(function(result) {
            if (!result) return H.sendResult(go, cb, bridgeId, true, '{"ranking":[],"total_count":0}', null);
            H.sendResult(go, cb, bridgeId, true, JSON.stringify(result), null);
          })
          .catch(function(e) { H.sendResult(go, cb, bridgeId, false, null, e.message || 'getLeaderboard failed'); });
        return;
      } catch(e) { /* fall through to direct REST */ }
    }

    // Fallback: direct REST call (no community name mapping)
    var url = H.baseUrl + H.rankingPrefix + '/' + appId + '/metas/ranking?' + queryParams;
    fetch(url, { headers: H.makeHeaders(token, true) })
      .then(function(r) {
        if (r.status === 204) return H.sendResult(go, cb, bridgeId, true, '{"ranking":[],"total_count":0}', null);
        return r.json().then(function(data) { H.sendResult(go, cb, bridgeId, true, JSON.stringify(data), null); });
      })
      .catch(function(e) { H.sendResult(go, cb, bridgeId, false, null, e.message); });
  },

  ViverseLeaderboard_GetGuestRanking__deps: ['$Leaderboard_Helpers'],
  ViverseLeaderboard_GetGuestRanking: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, appIdPtr, queryParamsPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var appId = UTF8ToString(appIdPtr);
    var queryParams = UTF8ToString(queryParamsPtr);
    var H = Leaderboard_Helpers;

    // Use viverse-sdk GameDashboard.getGuestLeaderboard (maps community display names automatically)
    var GameDashboard = window.viverse && (window.viverse.GameDashboard || window.viverse.gameDashboard);
    if (GameDashboard) {
      try {
        var isLocal = window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1';
        var client = new GameDashboard({
          baseURL: isLocal ? window.location.origin + '/' : 'https://www.viveport.com/',
          communityBaseURL: 'https://www.viverse.com/'
        });

        // Parse queryParams string into config object
        var params = {};
        queryParams.split('&').forEach(function(pair) {
          var kv = pair.split('=');
          if (kv.length === 2) {
            var v = decodeURIComponent(kv[1]);
            if (kv[0] === 'range_start' || kv[0] === 'range_end') v = parseInt(v, 10);
            else if (v === 'true') v = true;
            else if (v === 'false') v = false;
            params[kv[0]] = v;
          }
        });

        client.getGuestLeaderboard(appId, params)
          .then(function(result) {
            if (!result) return H.sendResult(go, cb, bridgeId, true, '{"ranking":[],"total_count":0}', null);
            H.sendResult(go, cb, bridgeId, true, JSON.stringify(result), null);
          })
          .catch(function(e) { H.sendResult(go, cb, bridgeId, false, null, e.message || 'getGuestLeaderboard failed'); });
        return;
      } catch(e) { /* fall through to direct REST */ }
    }

    // Fallback: direct REST call (no community name mapping)
    var url = H.baseUrl + H.rankingPrefix + '/' + appId + '/metas/guest_ranking?' + queryParams;
    fetch(url, { headers: { 'Content-Type': 'application/json' } })
      .then(function(r) {
        if (r.status === 204) return H.sendResult(go, cb, bridgeId, true, '{"ranking":[],"total_count":0}', null);
        return r.json().then(function(data) { H.sendResult(go, cb, bridgeId, true, JSON.stringify(data), null); });
      })
      .catch(function(e) { H.sendResult(go, cb, bridgeId, false, null, e.message); });
  },

  ViverseLeaderboard_SubmitScore__deps: ['$Leaderboard_Helpers'],
  ViverseLeaderboard_SubmitScore: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, appIdPtr, scoresJsonPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var appId = UTF8ToString(appIdPtr);
    var scoresJson = UTF8ToString(scoresJsonPtr);
    var token = UTF8ToString(tokenPtr);

    var H = Leaderboard_Helpers;

    // Use viverse-sdk's GameDashboard client (handles RSA/AES encryption internally)
    // Set baseURL to relative '/' so all fetches route through our local proxy (CORS workaround)
    try {
      var GameDashboard = (window.viverse && window.viverse.GameDashboard) || (window.viverse && window.viverse.gameDashboard);
      if (!GameDashboard) {
        H.sendResult(go, cb, bridgeId, false, null, 'viverse SDK not loaded - cannot submit score');
        return;
      }

      var isLocal = window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1';
      var client = new GameDashboard({
        baseURL: isLocal ? window.location.origin + '/' : 'https://www.viveport.com/',
        communityBaseURL: 'https://www.viverse.com/',
        token: token
      });

      // Parse scores from our JSON format: {"scores":[{"name":"x","value":"y"}]}
      var parsed = JSON.parse(scoresJson);
      var scores = parsed.scores || [];

      client.uploadLeaderboardScore(appId, scores, token)
        .then(function(result) {
          // SDK returns null on failure (catches internally)
          if (result === null || result === undefined) {
            H.sendResult(go, cb, bridgeId, false, null, 'uploadLeaderboardScore failed (check console for details)');
          } else {
            H.sendResult(go, cb, bridgeId, true, null, null);
          }
        })
        .catch(function(e) {
          H.sendResult(go, cb, bridgeId, false, null, e.message || 'upload failed');
        });
    } catch(e) {
      H.sendResult(go, cb, bridgeId, false, null, e.message || 'submit error');
    }
  }

};

mergeInto(LibraryManager.library, LibraryViverseLeaderboard);
