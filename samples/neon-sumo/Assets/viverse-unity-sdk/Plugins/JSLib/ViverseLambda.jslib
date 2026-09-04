var LibraryViverseLambda = {

  ViverseLambda_CreateJob: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, gameIdPtr, eventNamePtr, dataJsonPtr, requestIdPtr, tokenPtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var callbackMethod = UTF8ToString(callbackMethodPtr);
    var gameId = UTF8ToString(gameIdPtr);
    var eventName = UTF8ToString(eventNamePtr);
    var dataJson = UTF8ToString(dataJsonPtr);
    var requestId = UTF8ToString(requestIdPtr);
    var token = UTF8ToString(tokenPtr);

    var baseUrl = 'https://broadcasting-gateway-gaming.vrprod.viveport.com/api/play-lambda-service/v1';
    var parsedData = {};
    try { parsedData = JSON.parse(dataJson || '{}'); } catch(e) { try { parsedData = JSON.parse(dataJson.replace(/\\"/g, '"') || '{}'); } catch(e2) { parsedData = {}; } }
    var body = JSON.stringify({ game_id: gameId, event_name: eventName, data: parsedData, request_id: requestId });
    var isJwt = token.split('.').length === 3;
    var headers = { 'Content-Type': 'application/json' };
    headers[isJwt ? 'AccessToken' : 'AuthKey'] = token;

    // Create job
    fetch(baseUrl + '/jobs', { method: 'POST', headers: headers, body: body })
      .then(function(r) { return r.json(); })
      .then(function(createResult) {
        var jobId = (createResult.job && createResult.job.job_id) || createResult.job_id;
        if (!jobId) {
          var msg = createResult.message || 'no job_id in response';
          var err = { bridgeId: bridgeId, success: false, message: msg };
          SendMessage(gameObjectName, callbackMethod, JSON.stringify(err));
          return;
        }
        // Poll until terminal
        var pollInterval = 1000;
        var timeout = 60000;
        var start = Date.now();
        function poll() {
          if (Date.now() - start > timeout) {
            SendMessage(gameObjectName, callbackMethod, JSON.stringify({ bridgeId: bridgeId, success: false, message: 'timeout' }));
            return;
          }
          fetch(baseUrl + '/jobs/' + jobId, { headers: headers })
            .then(function(r) { return r.json(); })
            .then(function(resp) {
              var job = resp.job || resp;
              var s = job.status || 'unknown';
              if (s === 'succeeded' || s === 'failed' || s === 'timeout') {
                var payload = { bridgeId: bridgeId, success: s === 'succeeded', status: s, result: JSON.stringify(job.result || null), error: job.error || null };
                SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
              } else {
                setTimeout(poll, pollInterval);
              }
            })
            .catch(function(e) {
              SendMessage(gameObjectName, callbackMethod, JSON.stringify({ bridgeId: bridgeId, success: false, message: e.message || 'poll error' }));
            });
        }
        setTimeout(poll, pollInterval);
      })
      .catch(function(e) {
        SendMessage(gameObjectName, callbackMethod, JSON.stringify({ bridgeId: bridgeId, success: false, message: e.message || 'create job failed' }));
      });
  },

  ViverseLambda_GetJobStatus: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, jobIdPtr, tokenPtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var callbackMethod = UTF8ToString(callbackMethodPtr);
    var jobId = UTF8ToString(jobIdPtr);
    var token = UTF8ToString(tokenPtr);

    var baseUrl = 'https://broadcasting-gateway-gaming.vrprod.viveport.com/api/play-lambda-service/v1';
    var isJwt = token.split('.').length === 3;
    var headers = {};
    headers[isJwt ? 'AccessToken' : 'AuthKey'] = token;

    fetch(baseUrl + '/jobs/' + jobId, { headers: headers })
      .then(function(r) { return r.json(); })
      .then(function(result) {
        var payload = { bridgeId: bridgeId, success: true, status: result.status, result: JSON.stringify(result.result || null), error: result.error || null };
        SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
      })
      .catch(function(e) {
        SendMessage(gameObjectName, callbackMethod, JSON.stringify({ bridgeId: bridgeId, success: false, message: e.message || 'get status failed' }));
      });
  },

};

mergeInto(LibraryManager.library, LibraryViverseLambda);
