using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using ViverseSDK;

namespace NeonSumo
{
    /// <summary>
    /// Handles master-notify events: resolve master, backfill, arena config, authority, color sync, ramp spawn.
    /// </summary>
    public sealed class MasterSyncHandler
    {
        private readonly MasterSyncContext _ctx;

        public MasterSyncHandler(MasterSyncContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public void Handle(string json)
        {
            var client = _ctx.MultiplayerClient;
            if (client == null)
                return;

            string masterUser = NeonSumoUserId.Normalize(TryGetMasterUser(json));
            if (!string.IsNullOrEmpty(masterUser))
                _ctx.OnCanonicalRoomHostUserId?.Invoke(masterUser);

            bool isMaster = ResolveIsMaster(client, masterUser);

            NotifyMasterResolved(isMaster, masterUser);
            RunCommonSync(isMaster, client);
            RunMasterOnlySync(isMaster);
        }

        private static string TryGetMasterUser(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                var jObject = JObject.Parse(json);
                return jObject.Value<string>("master_user") ?? jObject["master_user"]?.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool ResolveIsMaster(MultiplayerClient client, string masterUser)
        {
            if (!string.IsNullOrEmpty(masterUser))
                return masterUser == NeonSumoUserId.Normalize(client.PeerId);

            return client.IsMasterUser();
        }

        private void NotifyMasterResolved(bool isMaster, string masterUser)
        {
            _ctx.OnMasterResolved?.Invoke(isMaster);

            DebugLogger.Log(
                $"[NeonSumo] MasterNotify received. IsMasterUser={isMaster} master_user={masterUser}");

            DebugLogger.Log(
                $"[NeonSumo] MasterNotify identity: localPlayerId={_ctx.LocalPlayerId} peerId={_ctx.MultiplayerClient?.PeerId}");
        }

        private void RunCommonSync(bool isMaster, MultiplayerClient client)
        {
            _ctx.TryBackfill?.Invoke();

            _ctx.Arena?.ConfigureNetwork(client, isMaster);
            _ctx.ApplyAuthorityToAll?.Invoke(isMaster);
        }

        private void RunMasterOnlySync(bool isMaster)
        {
            if (!isMaster)
                return;

            _ctx.SyncColorsAndBroadcast?.Invoke();
            _ctx.BeginRampSpawnPhaseIfNeeded?.Invoke();
        }
    }

    /// <summary>Context for MasterSyncHandler. GameManager populates this.</summary>
    public sealed class MasterSyncContext
    {
        public MultiplayerClient MultiplayerClient { get; set; }
        public string LocalPlayerId { get; set; }
        public NeonSumoArena Arena { get; set; }
        public Action<bool> OnMasterResolved { get; set; }
        /// <summary>SDK <c>master_user</c> from MasterNotify; same string on all clients — use for P1 ordering.</summary>
        public Action<string> OnCanonicalRoomHostUserId { get; set; }
        public Action TryBackfill { get; set; }
        public Action<bool> ApplyAuthorityToAll { get; set; }
        public Action SyncColorsAndBroadcast { get; set; }
        public Action BeginRampSpawnPhaseIfNeeded { get; set; }
    }
}
