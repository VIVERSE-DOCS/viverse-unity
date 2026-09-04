using System;

namespace NeonSumo
{
    /// <summary>Typed payload for player_spawn_sync network event.</summary>
    [Serializable]
    public sealed class PlayerSpawnSyncMessage
    {
        public string user_id;
        public int spawn_index;
        public float x;
        public float y;
        public float z;
        public float rotY;
        public int color_index;
    }

    /// <summary>Typed payload for spawn_ack network event.</summary>
    [Serializable]
    public sealed class SpawnAckMessage
    {
        public string user_id;
    }
}
