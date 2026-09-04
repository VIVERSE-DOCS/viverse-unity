var LibraryViverseAchievements = {

  $Achievement_Helpers: {
    // Use relative URLs so requests go through the local server proxy (CORS workaround).
    // In production on viverse.com, these resolve to the same domain.
    baseUrl: '/',
    achievementPrefix: 'api/optimusprime/v1/achievement',

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

  ViverseAchievements_GetUserAchievements__deps: ['$Achievement_Helpers'],
  ViverseAchievements_GetUserAchievements: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, appIdPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var appId = UTF8ToString(appIdPtr);
    var token = UTF8ToString(tokenPtr);

    var url = Achievement_Helpers.baseUrl + Achievement_Helpers.achievementPrefix + '/' + appId;
    fetch(url, { headers: Achievement_Helpers.makeHeaders(token, true) })
      .then(function(r) {
        if (!r.ok) {
          return r.text().then(function(t) {
            Achievement_Helpers.sendResult(go, cb, bridgeId, false, null, 'GET failed (' + r.status + '): ' + t);
          });
        }
        return r.json().then(function(data) {
          Achievement_Helpers.sendResult(go, cb, bridgeId, true, JSON.stringify(data), null);
        });
      })
      .catch(function(e) { Achievement_Helpers.sendResult(go, cb, bridgeId, false, null, e.message); });
  },

  ViverseAchievements_UnlockAchievements__deps: ['$Achievement_Helpers'],
  ViverseAchievements_UnlockAchievements: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, appIdPtr, achievementsJsonPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var appId = UTF8ToString(appIdPtr);
    var achievementsJson = UTF8ToString(achievementsJsonPtr);
    var token = UTF8ToString(tokenPtr);

    var H = Achievement_Helpers;

    // Use viverse-sdk's GameDashboard client (handles RSA/AES encryption internally)
    try {
      var GameDashboard = (window.viverse && window.viverse.GameDashboard) || (window.viverse && window.viverse.gameDashboard);
      if (!GameDashboard) {
        H.sendResult(go, cb, bridgeId, false, null, 'viverse SDK not loaded - cannot unlock achievements');
        return;
      }

      var client = new GameDashboard({
        baseURL: window.location.origin + '/',
        communityBaseURL: 'https://www.viverse.com/',
        token: token
      });

      // Parse achievements from JSON: {"achievements": [{"api_name":"x","unlock":true}]}
      var parsed = JSON.parse(achievementsJson);
      var achievements = parsed.achievements || [];

      client.uploadUserAchievement(appId, achievements, token)
        .then(function(result) {
          if (result === null || result === undefined) {
            H.sendResult(go, cb, bridgeId, false, null, 'uploadUserAchievement failed (check console)');
          } else {
            H.sendResult(go, cb, bridgeId, true, JSON.stringify(result), null);
          }
        })
        .catch(function(e) {
          H.sendResult(go, cb, bridgeId, false, null, e.message || 'unlock failed');
        });
    } catch(e) {
      H.sendResult(go, cb, bridgeId, false, null, e.message || 'achievement error');
    }
  }

};

mergeInto(LibraryManager.library, LibraryViverseAchievements);
