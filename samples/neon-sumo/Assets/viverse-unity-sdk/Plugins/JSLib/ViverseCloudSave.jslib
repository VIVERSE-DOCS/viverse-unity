var LibraryViverseCloudSave = {

  $CloudSave_Helpers: {
    baseUrl: 'https://broadcasting-gateway-gaming.vrprod.viveport.com',
    userAppPrefix: '/api/webrtcbot-service/v1/userapp',
    cloudSavePrefix: '/api/webrtcbot-service/v1/cloudsave',

    makeHeaders: function(token, includeContent) {
      var h = {};
      if (includeContent) h['Content-Type'] = 'application/json';
      var isJwt = token.split('.').length === 3;
      h[isJwt ? 'AccessToken' : 'AuthKey'] = token;
      return h;
    },

    sendResult: function(gameObjectName, callbackMethod, bridgeId, success, data, message) {
      var payload = { bridgeId: bridgeId, success: success };
      if (data !== undefined && data !== null) payload.data = data;
      if (message) payload.message = message;
      SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
    }
  },

  ViverseCloudSave_Save__deps: ['$CloudSave_Helpers'],
  ViverseCloudSave_Save: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, appIdPtr, dataJsonPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var appId = UTF8ToString(appIdPtr);
    var dataJson = UTF8ToString(dataJsonPtr);
    var token = UTF8ToString(tokenPtr);

    var url = CloudSave_Helpers.baseUrl + CloudSave_Helpers.userAppPrefix + '/save';
    var parsedData = {};
    try { parsedData = JSON.parse(dataJson || '{}'); } catch(e) { parsedData = {}; }
    var body = JSON.stringify({ app_id: appId, data: parsedData });

    fetch(url, { method: 'POST', headers: CloudSave_Helpers.makeHeaders(token, true), body: body })
      .then(function(r) {
        if (r.ok || r.status === 204) CloudSave_Helpers.sendResult(go, cb, bridgeId, true, null, null);
        else r.text().then(function(t) { CloudSave_Helpers.sendResult(go, cb, bridgeId, false, null, t || 'save failed'); });
      })
      .catch(function(e) { CloudSave_Helpers.sendResult(go, cb, bridgeId, false, null, e.message); });
  },

  ViverseCloudSave_GetAll__deps: ['$CloudSave_Helpers'],
  ViverseCloudSave_GetAll: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, appIdPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var appId = UTF8ToString(appIdPtr);
    var token = UTF8ToString(tokenPtr);

    var url = CloudSave_Helpers.baseUrl + CloudSave_Helpers.userAppPrefix + '/' + appId + '/all';
    fetch(url, { headers: CloudSave_Helpers.makeHeaders(token, false) })
      .then(function(r) {
        if (r.status === 204) return CloudSave_Helpers.sendResult(go, cb, bridgeId, true, '[]', null);
        return r.json().then(function(data) { CloudSave_Helpers.sendResult(go, cb, bridgeId, true, JSON.stringify(data), null); });
      })
      .catch(function(e) { CloudSave_Helpers.sendResult(go, cb, bridgeId, false, null, e.message); });
  },

  ViverseCloudSave_GetLatest__deps: ['$CloudSave_Helpers'],
  ViverseCloudSave_GetLatest: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, appIdPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var appId = UTF8ToString(appIdPtr);
    var token = UTF8ToString(tokenPtr);

    var url = CloudSave_Helpers.baseUrl + CloudSave_Helpers.userAppPrefix + '/' + appId + '/latest';
    fetch(url, { headers: CloudSave_Helpers.makeHeaders(token, false) })
      .then(function(r) {
        if (r.status === 204) return CloudSave_Helpers.sendResult(go, cb, bridgeId, true, null, null);
        return r.json().then(function(data) { CloudSave_Helpers.sendResult(go, cb, bridgeId, true, JSON.stringify(data), null); });
      })
      .catch(function(e) { CloudSave_Helpers.sendResult(go, cb, bridgeId, false, null, e.message); });
  },

  ViverseCloudSave_Delete__deps: ['$CloudSave_Helpers'],
  ViverseCloudSave_Delete: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, appIdPtr, versionPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var appId = UTF8ToString(appIdPtr);
    var version = UTF8ToString(versionPtr);
    var token = UTF8ToString(tokenPtr);

    var url = CloudSave_Helpers.baseUrl + CloudSave_Helpers.userAppPrefix + '/' + appId + '/version/' + version;
    fetch(url, { method: 'DELETE', headers: CloudSave_Helpers.makeHeaders(token, false) })
      .then(function(r) {
        if (r.ok || r.status === 204) CloudSave_Helpers.sendResult(go, cb, bridgeId, true, null, null);
        else r.text().then(function(t) { CloudSave_Helpers.sendResult(go, cb, bridgeId, false, null, t || 'delete failed'); });
      })
      .catch(function(e) { CloudSave_Helpers.sendResult(go, cb, bridgeId, false, null, e.message); });
  },

  ViverseCloudSave_SetPlayerData__deps: ['$CloudSave_Helpers'],
  ViverseCloudSave_SetPlayerData: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, appIdPtr, keyPtr, dataJsonPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var appId = UTF8ToString(appIdPtr);
    var key = UTF8ToString(keyPtr);
    var dataJson = UTF8ToString(dataJsonPtr);
    var token = UTF8ToString(tokenPtr);

    var url = CloudSave_Helpers.baseUrl + CloudSave_Helpers.cloudSavePrefix + '/' + appId + '/upsert/' + key;
    fetch(url, { method: 'POST', headers: CloudSave_Helpers.makeHeaders(token, true), body: dataJson || '{}' })
      .then(function(r) {
        if (r.ok || r.status === 204) CloudSave_Helpers.sendResult(go, cb, bridgeId, true, null, null);
        else r.text().then(function(t) { CloudSave_Helpers.sendResult(go, cb, bridgeId, false, null, t || 'setPlayerData failed'); });
      })
      .catch(function(e) { CloudSave_Helpers.sendResult(go, cb, bridgeId, false, null, e.message); });
  },

  ViverseCloudSave_GetPlayerData__deps: ['$CloudSave_Helpers'],
  ViverseCloudSave_GetPlayerData: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, appIdPtr, keyPtr, tokenPtr) {
    var go = UTF8ToString(gameObjectNamePtr);
    var cb = UTF8ToString(callbackMethodPtr);
    var appId = UTF8ToString(appIdPtr);
    var key = UTF8ToString(keyPtr);
    var token = UTF8ToString(tokenPtr);

    var url = CloudSave_Helpers.baseUrl + CloudSave_Helpers.cloudSavePrefix + '/' + appId;
    fetch(url, { headers: CloudSave_Helpers.makeHeaders(token, false) })
      .then(function(r) {
        if (r.status === 204) return CloudSave_Helpers.sendResult(go, cb, bridgeId, true, null, null);
        return r.json().then(function(resp) {
          if (key && resp && resp.data && resp.data[key] !== undefined) {
            var val = resp.data[key];
            CloudSave_Helpers.sendResult(go, cb, bridgeId, true, typeof val === 'string' ? val : JSON.stringify(val), null);
          } else if (key) {
            CloudSave_Helpers.sendResult(go, cb, bridgeId, true, null, null);
          } else {
            CloudSave_Helpers.sendResult(go, cb, bridgeId, true, JSON.stringify(resp), null);
          }
        });
      })
      .catch(function(e) { CloudSave_Helpers.sendResult(go, cb, bridgeId, false, null, e.message); });
  }
};

mergeInto(LibraryManager.library, LibraryViverseCloudSave);
