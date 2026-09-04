namespace NeonSumo
{
    /// <summary>
    /// Canonical multiplayer identity. VIVERSE callbacks and JSON ToString paths
    /// sometimes wrap UUIDs in extra quotes; those must never become dictionary keys.
    /// </summary>
    internal static class NeonSumoUserId
    {
        public static string Normalize(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return string.Empty;

            id = id.Trim();

            while (id.Length >= 2 && id[0] == '"' && id[id.Length - 1] == '"')
                id = id.Substring(1, id.Length - 2).Trim();

            return id;
        }

        public static bool TryNormalize(string id, out string normalized)
        {
            normalized = Normalize(id);
            return normalized.Length > 0;
        }
    }
}
