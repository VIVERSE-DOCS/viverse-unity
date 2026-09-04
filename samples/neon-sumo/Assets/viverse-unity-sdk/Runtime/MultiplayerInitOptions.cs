using System;

namespace ViverseSDK
{
    [Serializable]
    public class MultiplayerInitOptions
    {
        public ModulesConfig modules;
    }

    [Serializable]
    public class ModulesConfig
    {
        public ModuleOption game;
        public ModuleOption networkSync;
        public ModuleOption actionSync;
        public ModuleOption leaderboard;
        public ModuleOption lambda;
    }

    [Serializable]
    public class ModuleOption
    {
        public bool enabled;
        public string desc;

        // Game module specific
        public int ready_time = 3;
        public float start_delay_time = 0.5f;
        public int play_time = 30;
        public int total_player = 4;
        public int change_second = 10;
        public int min_total_player = 2;
        public int max_total_player = 4;
        public int wait_player_timeout = 100;
    }
}
