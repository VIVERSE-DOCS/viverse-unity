var LibraryViversePlay = {

  $ViversePlay_State: {
    client: null,
    multiplayerClient: null,
    multiplayerGameObjectName: null,
    multiplayerCallbackMethod: null,
    gameObjectName: null,
    callbackMethod: null,
    diagInterval: null,
  },

  ViversePlay_Matchmaking_Initialize__deps: ['$ViversePlay_State'],
  ViversePlay_Matchmaking_Initialize: function(appIdPtr, debugMode, gameObjectNamePtr, callbackMethodPtr) {
    var appId = UTF8ToString(appIdPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var callbackMethod = UTF8ToString(callbackMethodPtr);
    ViversePlay_State.gameObjectName = gameObjectName;
    ViversePlay_State.callbackMethod = callbackMethod;

    (async function() {
      try {
        var PlaySDK = globalThis.viverse.Play || globalThis.viverse.play;
        var playClient = new PlaySDK();
        var client = await playClient.newMatchmakingClient(appId, debugMode);
        ViversePlay_State.client = client;

        client.on("onConnect", function() {
          var args = Array.prototype.slice.call(arguments);
          var payload = JSON.stringify({ event: "onConnect", data: args.length === 1 ? args[0] : args });
          SendMessage(gameObjectName, callbackMethod, payload);
        });
        client.on("onDisconnect", function() {
          var args = Array.prototype.slice.call(arguments);
          var payload = JSON.stringify({ event: "onDisconnect", data: args.length === 1 ? args[0] : args });
          SendMessage(gameObjectName, callbackMethod, payload);
        });
        client.on("onError", function() {
          var args = Array.prototype.slice.call(arguments);
          var payload = JSON.stringify({ event: "onError", data: args.length === 1 ? args[0] : args });
          SendMessage(gameObjectName, callbackMethod, payload);
        });
        client.on("stateChange", function() {
          var args = Array.prototype.slice.call(arguments);
          var payload = JSON.stringify({ event: "stateChange", data: args.length === 1 ? args[0] : args });
          SendMessage(gameObjectName, callbackMethod, payload);
        });
        client.on("onRoomListUpdate", function() {
          var args = Array.prototype.slice.call(arguments);
          var payload = JSON.stringify({ event: "onRoomListUpdate", data: args.length === 1 ? args[0] : args });
          SendMessage(gameObjectName, callbackMethod, payload);
        });
        client.on("onRoomClosed", function() {
          var args = Array.prototype.slice.call(arguments);
          var payload = JSON.stringify({ event: "onRoomClosed", data: args.length === 1 ? args[0] : args });
          SendMessage(gameObjectName, callbackMethod, payload);
        });
        client.on("onGameStartNotify", function() {
          var args = Array.prototype.slice.call(arguments);
          var payload = JSON.stringify({ event: "onGameStartNotify", data: args.length === 1 ? args[0] : args });
          SendMessage(gameObjectName, callbackMethod, payload);
        });
        client.on("onRoomActorChange", function() {
          var args = Array.prototype.slice.call(arguments);
          var payload = JSON.stringify({ event: "onRoomActorChange", data: args.length === 1 ? args[0] : args });
          SendMessage(gameObjectName, callbackMethod, payload);
        });
        client.on("onMatchingTimeout", function() {
          var args = Array.prototype.slice.call(arguments);
          var payload = JSON.stringify({ event: "onMatchingTimeout", data: args.length === 1 ? args[0] : args });
          SendMessage(gameObjectName, callbackMethod, payload);
        });
        client.on("onJoinRoom", function() {
          var args = Array.prototype.slice.call(arguments);
          var payload = JSON.stringify({ event: "onJoinRoom", data: args.length === 1 ? args[0] : args });
          SendMessage(gameObjectName, callbackMethod, payload);
        });
        client.on("onJoinedLobby", function() {
          var args = Array.prototype.slice.call(arguments);
          var payload = JSON.stringify({ event: "onJoinedLobby", data: args.length === 1 ? args[0] : args });
          SendMessage(gameObjectName, callbackMethod, payload);
        });
      } catch (err) {
        var payload = JSON.stringify({ event: "onError", data: err.message || "Failed to initialize matchmaking" });
        SendMessage(gameObjectName, callbackMethod, payload);
      }
    })();
  },

  ViversePlay_Matchmaking_Disconnect__deps: ['$ViversePlay_State'],
  ViversePlay_Matchmaking_Disconnect: function() {
    if (ViversePlay_State.client) {
      ViversePlay_State.client.disconnect();
    }
  },

  ViversePlay_Matchmaking_SendRequest__deps: ['$ViversePlay_State'],
  ViversePlay_Matchmaking_SendRequest: function(requestJsonPtr) {
    var json = UTF8ToString(requestJsonPtr);
    var request = JSON.parse(json);
    var client = ViversePlay_State.client;
    if (!client) return;

    var requestId = request.request_id;
    var promise = null;

    switch (request.request_type) {
      case "SetActor":
        promise = client.setActor({ session_id: request.session_id, name: request.properties ? request.properties.name : "", properties: request.properties ? request.properties.properties : {} });
        break;
      case "SetActorProperties":
        promise = client.setActorProperties(request.properties);
        break;
      case "CreateRoom":
        promise = client.createRoom({ name: request.room_name, mode: request.mode, max_players: request.max_players, min_players: request.min_players, properties: request.properties });
        break;
      case "JoinRoom":
        promise = client.joinRoom(request.room_id);
        break;
      case "LeaveRoom":
        promise = client.leaveRoom();
        break;
      case "CloseRoom":
        promise = client.closeRoom();
        break;
      case "OpenRoom":
        promise = client.openRoom();
        break;
      case "StartGame":
        promise = client.startGame();
        break;
      case "SetRoomProperties":
        promise = client.setRoomProperties(request.properties);
        break;
      case "GetRoomProperties":
        promise = client.getRoomProperties();
        break;
      case "GetAvailableRooms":
        promise = client.getAvailableRooms();
        break;
      case "GetRoomActors":
        promise = client.getMyRoomActors();
        break;
      case "StartMatch":
        promise = client.startMatch(request.match_mode, request.min_players, request.max_players);
        break;
      case "CancelMatch":
        promise = client.cancelMatch();
        break;
      default:
        return;
    }

    if (promise && promise.then) {
      promise.then(function(result) {
        var response = JSON.stringify({ request_id: requestId, success: true, request_type: request.request_type, data: result });
        SendMessage(ViversePlay_State.gameObjectName, ViversePlay_State.callbackMethod, response);
      }).catch(function(err) {
        var response = JSON.stringify({ request_id: requestId, success: false, request_type: request.request_type, message: err.message || "Unknown error" });
        SendMessage(ViversePlay_State.gameObjectName, ViversePlay_State.callbackMethod, response);
      });
    }
  },

  ViversePlay_Matchmaking_GetCurrentActor__deps: ['$ViversePlay_State'],
  ViversePlay_Matchmaking_GetCurrentActor: function() {
    var actor = ViversePlay_State.client ? ViversePlay_State.client.getCurrentActor() : null;
    var json = JSON.stringify(actor);
    var bufferSize = lengthBytesUTF8(json) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(json, buffer, bufferSize);
    return buffer;
  },

  ViversePlay_Matchmaking_GetCurrentRoom__deps: ['$ViversePlay_State'],
  ViversePlay_Matchmaking_GetCurrentRoom: function() {
    var room = ViversePlay_State.client ? ViversePlay_State.client.getCurrentRoom() : null;
    var json = JSON.stringify(room);
    var bufferSize = lengthBytesUTF8(json) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(json, buffer, bufferSize);
    return buffer;
  },

  ViversePlay_Matchmaking_IsInLobby__deps: ['$ViversePlay_State'],
  ViversePlay_Matchmaking_IsInLobby: function() {
    return ViversePlay_State.client ? ViversePlay_State.client.isInLobby() : true;
  },

  ViversePlay_Matchmaking_IsJoinedToRoom__deps: ['$ViversePlay_State'],
  ViversePlay_Matchmaking_IsJoinedToRoom: function() {
    return ViversePlay_State.client ? ViversePlay_State.client.isJoinedToRoom() : false;
  },

  // ─── Multiplayer ───────────────────────────────────────────────────────────

  ViversePlay_Multiplayer_Initialize__deps: ['$ViversePlay_State'],
  ViversePlay_Multiplayer_Initialize: function(requestId, roomIdPtr, appIdPtr, userSessionIdPtr, gameObjectNamePtr, callbackMethodPtr) {
    var roomId = UTF8ToString(roomIdPtr);
    var appId = UTF8ToString(appIdPtr);
    var userSessionId = UTF8ToString(userSessionIdPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var callbackMethod = UTF8ToString(callbackMethodPtr);

    ViversePlay_State.multiplayerGameObjectName = gameObjectName;
    ViversePlay_State.multiplayerCallbackMethod = callbackMethod;

    var sendToUnity = function(payload) {
      if (typeof SendMessage === 'function') {
        SendMessage(ViversePlay_State.multiplayerGameObjectName, ViversePlay_State.multiplayerCallbackMethod, JSON.stringify(payload));
      }
    };

    var attachListeners = function(instance) {
      var sender = function(eventName, data) {
        sendToUnity({ eventName: eventName, data: data || null });
      };

      instance.onConnected(function() { sender('onConnected'); });
      instance.onDisconnected(function() { sender('onDisconnected'); });
      instance.onClientConnected(function(data) { sender('onClientConnected', data); });
      instance.onClientDisconnected(function(data) { sender('onClientDisconnected', data); });
      instance.onMessage(function(data) { sender('onMessage', data); });

      if (instance.networksync) {
        instance.networksync.onNotifyPositionUpdate(function(data) { sender('networksync/onNotifyPositionUpdate', data); });
        instance.networksync.onNotifyRemove(function(data) { sender('networksync/onNotifyRemove', data); });
      }
      if (instance.actionsync) {
        instance.actionsync.onCompetition(function(data) { sender('actionsync/onCompetition', data); });
      }
      if (instance.leaderboard) {
        instance.leaderboard.onLeaderboardUpdate(function(data) { sender('leaderboard/onLeaderboardUpdate', data); });
      }
      if (instance.game) {
        instance.game.onBotLeave(function() { sender('game/onBotLeave'); });
        instance.game.onMasterNotify(function(data) { sender('game/onMasterNotify', data); });
        instance.game.onWaitForPlayer(function(data) { sender('game/onWaitForPlayer', data); });
        instance.game.onPlayerAllReady(function(data) { sender('game/onPlayerAllReady', data); });
        instance.game.onPlayerOverLimit(function() { sender('game/onPlayerOverLimit'); });
        instance.game.onCountdownToStart(function(data) { sender('game/onCountdownToStart', data); });
        instance.game.onCountdownToEnd(function(data) { sender('game/onCountdownToEnd', data); });
        instance.game.onGameTimeUp(function() { sender('game/onGameTimeUp'); });
        instance.game.onGameEnd(function() { sender('game/onGameEnd'); });
        instance.game.onGameRestart(function() { sender('game/onGameRestart'); });
        instance.game.onErrorNotify(function(data) { sender('game/onErrorNotify', data); });
      }
    };

    (async function() {
      try {
        var PlaySDK = globalThis.viverse.Play || globalThis.viverse.play;
        var playClient = new PlaySDK();
        var client = await playClient.newMultiplayerClient(roomId, appId, userSessionId || undefined);
        ViversePlay_State.multiplayerClient = client;
        attachListeners(client);

        // Expose global diagnostic function
        window.ViversePlay_Diag = function() {
          var mc = client.mediasoupclient || client._mediasoupclient;
          if (!mc) {
            var keys = Object.keys(client);
            for (var i = 0; i < keys.length; i++) {
              var v = client[keys[i]];
              if (v && (v._sendTransport || v._protoo)) { mc = v; break; }
            }
          }
          if (!mc) { console.log('[Diag] No mediasoupclient found. Keys:', Object.keys(client)); return; }
          console.log('[Diag] === Mediasoup Diagnostic ===');
          console.log('[Diag] closed:', mc._closed);
          console.log('[Diag] protooUrl:', mc._protooUrl);
          console.log('[Diag] protoo connected:', mc._protoo ? mc._protoo.connected : 'no protoo');

          var checkTransport = function(name, t) {
            if (!t) { console.log('[Diag] ' + name + ': null'); return; }
            console.log('[Diag] ' + name + ' id=' + t.id + ' dir=' + t.direction + ' connState=' + t.connectionState);
            var handler = t._handler;
            if (!handler) { console.log('[Diag] ' + name + ' no handler'); return; }
            var pc = handler._pc;
            if (!pc) { console.log('[Diag] ' + name + ' no RTCPeerConnection'); return; }
            console.log('[Diag] ' + name + ' ICE: conn=' + pc.iceConnectionState + ' gather=' + pc.iceGatheringState);
            console.log('[Diag] ' + name + ' DTLS: ' + pc.connectionState);
            console.log('[Diag] ' + name + ' signalingState: ' + pc.signalingState);
            if (pc.remoteDescription && pc.remoteDescription.sdp) {
              var cands = pc.remoteDescription.sdp.match(/a=candidate:.*/g) || [];
              console.log('[Diag] ' + name + ' remote candidates (' + cands.length + '):');
              cands.forEach(function(c) { console.log('[Diag]   ' + c); });
            }
            if (pc.localDescription && pc.localDescription.sdp) {
              var lcands = pc.localDescription.sdp.match(/a=candidate:.*/g) || [];
              console.log('[Diag] ' + name + ' local candidates (' + lcands.length + '):');
              lcands.forEach(function(c) { console.log('[Diag]   ' + c); });
            }
            // Get stats
            pc.getStats().then(function(stats) {
              stats.forEach(function(report) {
                if (report.type === 'candidate-pair' && report.state === 'failed') {
                  console.log('[Diag] ' + name + ' FAILED candidate-pair:', report);
                }
                if (report.type === 'candidate-pair' && (report.nominated || report.state === 'succeeded')) {
                  console.log('[Diag] ' + name + ' ACTIVE candidate-pair:', report);
                }
              });
            });
          };
          checkTransport('sendTransport', mc._sendTransport);
          checkTransport('recvTransport', mc._recvTransport);
          if (mc._chatDataProducer) {
            console.log('[Diag] chatDataProducer: readyState=' + mc._chatDataProducer.readyState + ' id=' + mc._chatDataProducer.id);
          } else {
            console.log('[Diag] chatDataProducer: NOT created');
          }
        };
        console.log('[ViversePlay] Diagnostic available: call ViversePlay_Diag() in console');

        sendToUnity({ requestId: requestId, success: true, data: JSON.stringify({ session_id: userSessionId, room_id: roomId, app_id: appId }) });
      } catch (error) {
        sendToUnity({ requestId: requestId, success: false, message: error.message || 'Initialize failed' });
      }
    })();
  },

  ViversePlay_Multiplayer_Init__deps: ['$ViversePlay_State'],
  ViversePlay_Multiplayer_Init: function(requestId, jsonOptionsPtr) {
    var client = ViversePlay_State.multiplayerClient;
    if (!client) {
      var sendToUnity = function(payload) {
        if (typeof SendMessage === 'function') SendMessage(ViversePlay_State.multiplayerGameObjectName, ViversePlay_State.multiplayerCallbackMethod, JSON.stringify(payload));
      };
      sendToUnity({ requestId: requestId, success: false, message: 'MultiplayerClient not initialized.' });
      return;
    }

    var opts = null;
    if (jsonOptionsPtr) {
      try {
        var jsonStr = UTF8ToString(jsonOptionsPtr);
        if (jsonStr && jsonStr.length > 0) opts = JSON.parse(jsonStr);
      } catch (e) {}
    }

    var initPromise = opts ? client.init(opts) : client.init();
    initPromise.then(function(result) {
      // Diagnostic: monitor all RTCPeerConnections for ICE state
      try {
        var mc = client.mediasoupclient || client._mediasoupclient;
        if (!mc) {
          var keys = Object.keys(client);
          for (var i = 0; i < keys.length; i++) {
            var v = client[keys[i]];
            if (v && v._sendTransport) { mc = v; break; }
          }
        }
        if (mc) {
          console.log('[ViversePlay Diag] Found mediasoupclient, monitoring transports...');
          var logTransport = function(name, transport) {
            if (!transport) { console.log('[ViversePlay Diag] ' + name + ': null'); return; }
            console.log('[ViversePlay Diag] ' + name + ' id=' + transport.id + ' connectionState=' + transport.connectionState);
            var handler = transport._handler;
            if (handler && handler._pc) {
              var pc = handler._pc;
              console.log('[ViversePlay Diag] ' + name + ' PC iceConnectionState=' + pc.iceConnectionState + ' iceGatheringState=' + pc.iceGatheringState);
              console.log('[ViversePlay Diag] ' + name + ' PC localDescription type=' + (pc.localDescription ? pc.localDescription.type : 'null'));
              console.log('[ViversePlay Diag] ' + name + ' PC remoteDescription type=' + (pc.remoteDescription ? pc.remoteDescription.type : 'null'));
              if (pc.remoteDescription && pc.remoteDescription.sdp) {
                var candidates = pc.remoteDescription.sdp.match(/a=candidate:.*/g);
                console.log('[ViversePlay Diag] ' + name + ' remote ICE candidates: ' + (candidates ? candidates.length : 0));
                if (candidates) {
                  candidates.forEach(function(c) { console.log('[ViversePlay Diag]   ' + c); });
                }
              }
            }
          };
          logTransport('sendTransport', mc._sendTransport);
          logTransport('recvTransport', mc._recvTransport);
          if (mc._chatDataProducer) {
            console.log('[ViversePlay Diag] chatDataProducer readyState=' + mc._chatDataProducer.readyState);
          } else {
            console.log('[ViversePlay Diag] chatDataProducer: not created yet');
          }
        } else {
          console.log('[ViversePlay Diag] Could not find mediasoupclient on MultiplayerClient');
        }
      } catch (diagErr) {
        console.log('[ViversePlay Diag] Error during diagnostics: ' + diagErr.message);
      }

      var payload = { requestId: requestId, success: true, data: JSON.stringify(result || {}) };
      if (typeof SendMessage === 'function') SendMessage(ViversePlay_State.multiplayerGameObjectName, ViversePlay_State.multiplayerCallbackMethod, JSON.stringify(payload));
    }).catch(function(error) {
      var payload = { requestId: requestId, success: false, message: error && error.message ? error.message : 'Init failed' };
      if (typeof SendMessage === 'function') SendMessage(ViversePlay_State.multiplayerGameObjectName, ViversePlay_State.multiplayerCallbackMethod, JSON.stringify(payload));
    });
  },

  ViversePlay_Multiplayer_Send__deps: ['$ViversePlay_State'],
  ViversePlay_Multiplayer_Send: function(dataPtr) {
    var data = UTF8ToString(dataPtr);
    if (ViversePlay_State.multiplayerClient) {
      ViversePlay_State.multiplayerClient.send(data);
    }
  },

  ViversePlay_Multiplayer_Disconnect__deps: ['$ViversePlay_State'],
  ViversePlay_Multiplayer_Disconnect: function() {
    if (ViversePlay_State.multiplayerClient) {
      try { ViversePlay_State.multiplayerClient.disconnect(); } catch (e) {}
      ViversePlay_State.multiplayerClient = null;
    }
  },

  ViversePlay_Multiplayer_SetMaster__deps: ['$ViversePlay_State'],
  ViversePlay_Multiplayer_SetMaster: function(isMaster) {
    if (ViversePlay_State.multiplayerClient) {
      ViversePlay_State.multiplayerClient.setMaster(isMaster);
    }
  },

  ViversePlay_Multiplayer_IsMasterUser__deps: ['$ViversePlay_State'],
  ViversePlay_Multiplayer_IsMasterUser: function() {
    if (ViversePlay_State.multiplayerClient) {
      return ViversePlay_State.multiplayerClient.isMasterUser();
    }
    return false;
  },

  ViversePlay_Multiplayer_GetRoomInfo__deps: ['$ViversePlay_State'],
  ViversePlay_Multiplayer_GetRoomInfo: function(requestId) {
    var client = ViversePlay_State.multiplayerClient;
    if (!client || !client.game || typeof client.game.getRoomInfo !== 'function') {
      var payload = { requestId: requestId, success: false, message: 'getRoomInfo not available' };
      if (typeof SendMessage === 'function') SendMessage(ViversePlay_State.multiplayerGameObjectName, ViversePlay_State.multiplayerCallbackMethod, JSON.stringify(payload));
      return;
    }
    client.game.getRoomInfo().then(function(result) {
      var payload = { requestId: requestId, success: true, room_data: result || {} };
      if (typeof SendMessage === 'function') SendMessage(ViversePlay_State.multiplayerGameObjectName, ViversePlay_State.multiplayerCallbackMethod, JSON.stringify(payload));
    }).catch(function(error) {
      var payload = { requestId: requestId, success: false, message: error && error.message ? error.message : 'getRoomInfo failed' };
      if (typeof SendMessage === 'function') SendMessage(ViversePlay_State.multiplayerGameObjectName, ViversePlay_State.multiplayerCallbackMethod, JSON.stringify(payload));
    });
  },

  ViversePlay_Lambda_CreateJob__deps: ['$ViversePlay_State'],
  ViversePlay_Lambda_CreateJob: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, gameIdPtr, eventNamePtr, dataJsonPtr, requestIdStrPtr, tokenPtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var callbackMethod = UTF8ToString(callbackMethodPtr);
    var eventName = UTF8ToString(eventNamePtr);
    var dataJson = UTF8ToString(dataJsonPtr);
    var tokenStr = UTF8ToString(tokenPtr);

    var client = ViversePlay_State.multiplayerClient;
    if (!client || !client.lambda) {
      var errPayload = { requestId: bridgeId, lambdaBridgeId: bridgeId, success: false, message: 'Lambda module not available' };
      if (typeof SendMessage === 'function') SendMessage(gameObjectName, callbackMethod, JSON.stringify(errPayload));
      return;
    }

    var data = {};
    try { data = JSON.parse(dataJson); } catch (e) {}

    client.lambda.invoke(eventName, data, tokenStr || undefined).then(function(result) {
      var payload = { requestId: bridgeId, lambdaBridgeId: bridgeId, success: true, jobData: JSON.stringify(result || {}) };
      if (typeof SendMessage === 'function') SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
    }).catch(function(error) {
      var payload = { requestId: bridgeId, lambdaBridgeId: bridgeId, success: false, message: error && error.message ? error.message : 'Lambda invoke failed' };
      if (typeof SendMessage === 'function') SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
    });
  },

  ViversePlay_Lambda_GetJobStatus__deps: ['$ViversePlay_State'],
  ViversePlay_Lambda_GetJobStatus: function(bridgeId, gameObjectNamePtr, callbackMethodPtr, jobIdPtr, tokenPtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var callbackMethod = UTF8ToString(callbackMethodPtr);
    var jobId = UTF8ToString(jobIdPtr);
    var tokenStr = UTF8ToString(tokenPtr);

    var client = ViversePlay_State.multiplayerClient;
    if (!client || !client.lambda) {
      var errPayload = { requestId: bridgeId, lambdaBridgeId: bridgeId, success: false, message: 'Lambda module not available' };
      if (typeof SendMessage === 'function') SendMessage(gameObjectName, callbackMethod, JSON.stringify(errPayload));
      return;
    }

    client.lambda.getJobStatus(jobId, tokenStr || undefined).then(function(result) {
      var payload = { requestId: bridgeId, lambdaBridgeId: bridgeId, success: true, jobData: JSON.stringify(result || {}) };
      if (typeof SendMessage === 'function') SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
    }).catch(function(error) {
      var payload = { requestId: bridgeId, lambdaBridgeId: bridgeId, success: false, message: error && error.message ? error.message : 'GetJobStatus failed' };
      if (typeof SendMessage === 'function') SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
    });
  },

  ViversePlay_DownloadLog: function(filenamePtr, contentPtr) {
    var filename = UTF8ToString(filenamePtr);
    var content = UTF8ToString(contentPtr);
    var blob = new Blob([content], { type: 'text/plain' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  },

};

mergeInto(LibraryManager.library, LibraryViversePlay);
