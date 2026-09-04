using System;
using System.Collections.Generic;

namespace NeonSumo
{
    /// <summary>
    /// Canonical player ordering: room host (master_client_id) first, then ordinal user id.
    /// Use everywhere UI, spawn indices, ramp assignment, and color slots must agree.
    /// </summary>
    public static class NeonSumoPlayerOrder
    {
        static readonly StringComparer IdComparer = StringComparer.Ordinal;

        public static bool UserIdMatchesHost(string userId, string roomHostUserId)
        {
            userId = NeonSumoUserId.Normalize(userId);
            roomHostUserId = NeonSumoUserId.Normalize(roomHostUserId);
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(roomHostUserId))
                return false;
            if (string.Equals(userId, roomHostUserId, StringComparison.Ordinal))
                return true;
            return string.Equals(userId, roomHostUserId, StringComparison.OrdinalIgnoreCase);
        }

        public static int CompareHostFirst(string a, string b, string roomHostUserId)
        {
            bool aHost = UserIdMatchesHost(a, roomHostUserId);
            bool bHost = UserIdMatchesHost(b, roomHostUserId);
            if (aHost && !bHost) return -1;
            if (!aHost && bHost) return 1;
            return IdComparer.Compare(a, b);
        }

        public static void SortUserIdsHostFirst(List<string> userIds, string roomHostUserId)
        {
            if (userIds == null || userIds.Count <= 1) return;
            userIds.Sort((a, b) => CompareHostFirst(a, b, roomHostUserId));
        }

        public static List<string> OrderedUserIds(IEnumerable<string> userIds, string roomHostUserId)
        {
            var list = new List<string>();
            if (userIds == null) return list;
            foreach (var id in userIds)
            {
                if (!string.IsNullOrEmpty(id))
                    list.Add(id);
            }
            SortUserIdsHostFirst(list, roomHostUserId);
            return list;
        }

        public static int IndexInHostFirstOrder(string userId, IEnumerable<string> allUserIds, string roomHostUserId)
        {
            if (string.IsNullOrEmpty(userId) || allUserIds == null) return -1;
            var ordered = OrderedUserIds(allUserIds, roomHostUserId);
            return ordered.IndexOf(userId);
        }
    }
}
