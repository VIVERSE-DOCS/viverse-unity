using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using ViverseSDK;

namespace NeonSumo
{
    /// <summary>
    /// VIVERSE profile fields for Neon Sumo display names (no VIVERSE calls from UI presenters).
    /// Populated from AuthManager account_id, AvatarClient.GetProfile (<c>name</c>/<c>displayName</c>),
    /// optional GetAvatarList scrape, then account_id fallback. No VRM.
    /// </summary>
    public static class NeonSumoPlatformProfiles
    {
        /// <summary>Fired when cached display label may have changed — gameplay should re-apply registry + ActionSync.</summary>
        public static event Action LocalDisplayProfileChanged;

        public const string PrefKeyDisplayLabel = "NeonSumo_ViverseDisplayLabel";
        public const string PrefKeyUserProfileId = "NeonSumo_ViverseUserProfileId";

        /// <remarks>Omits bare <c>name</c> — API trees often carry UI placeholders like \"You\"/slot labels scanned before real profile strings.</remarks>
        static readonly string[] DisplayKeyPriority =
        {
            "displayName", "display_name", "nickname", "userName", "username", "nickName", "nick_name", "profileName", "profile_name", "fullname", "full_name",
        };

        /// <summary>Official GetProfile payload — includes <c>name</c> (not used on avatar-list scrapes).</summary>
        static readonly string[] ProfileNameKeyPriority =
        {
            "displayName", "display_name", "name", "nickname", "userName", "username", "nickName", "nick_name", "profileName", "profile_name", "fullname", "full_name",
        };

        static readonly string[] UserIdKeyPriority =
        {
            "userID", "userId", "user_id", "userid", "viveportId", "ViveportId", "viveport_id",
        };

        /// <summary>
        /// Plan order: displayName family, then user ID string, then shortened account_id.
        /// Sanitization for registry is applied upstream; this returns a non-empty player-facing label when possible.
        /// </summary>
        public static bool TryGetLocalDisplayName(out string displayName)
        {
            displayName = TryBuildLocalDisplayLabelPreferringProfile();
            return !string.IsNullOrEmpty(displayName);
        }

        /// <summary>True when we already cached a deliberate display string (Avatar SDK / auth), not relying on shortened account fallback.</summary>
        public static bool HasUsableStoredDisplayLabelPreference()
        {
            return TryGetProfileDisplayLabel(out _);
        }

        /// <summary>VIVERSE profile label only (HUD). Does not return account_id or other fallbacks.</summary>
        public static bool TryGetProfileDisplayLabel(out string displayName)
        {
            displayName = null;
            string raw = PlayerPrefs.GetString(PrefKeyDisplayLabel, string.Empty);
            string s = NeonSumoDisplayNameRegistry.Sanitize(raw);
            if (string.IsNullOrEmpty(s) || IsGarbageOrUiPlaceholderDisplay(s))
                return false;
            displayName = s;
            return true;
        }

        public static void ClearViVerseGameplayDisplayCache()
        {
            PlayerPrefs.DeleteKey(PrefKeyDisplayLabel);
            PlayerPrefs.DeleteKey(PrefKeyUserProfileId);
            PlayerPrefs.Save();
            LocalDisplayProfileChanged?.Invoke();
        }

        static bool _avatarProbeInFlight;

        /// <summary>After AuthManager login (SDK 1.2 keys). Does not import legacy access_token.</summary>
        public static void AfterAuthManagerLogin(AuthResult result, MonoBehaviour coroutineHost)
        {
            if (result != null && !string.IsNullOrEmpty(result.account_id))
                ApplyExtracted(null, result.account_id);

            TryScheduleAvatarFallbackFromStoredPrefs(coroutineHost);
        }

        /// <summary>When token is already in PlayerPrefs (e.g. editor resume) but display label may be missing.</summary>
        public static void TryScheduleAvatarFallbackFromStoredPrefs(MonoBehaviour host)
        {
            ScheduleAvatarApiProbeIfNeeded();
        }

        public static void ApplyFromAvatarListApiJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var root = JToken.Parse(json);
                string display = PickBestStringByKeyPriority(root, DisplayKeyPriority, out _);
                string userLine = PickBestStringByKeyPriority(root, UserIdKeyPriority, out _);

                bool changed = ApplyExtracted(display, userLine);
                if (changed)
                    LocalDisplayProfileChanged?.Invoke();
            }
            catch (Exception ex)
            {
                DebugLogger.LogWarning($"[NeonSumo][ViverseProfile] Avatar list JSON parse failed: {ex.Message}");
            }
        }

        static void ScheduleAvatarApiProbeIfNeeded()
        {
            var auth = AuthManager.Instance;
            if (HasUsableStoredDisplayLabelPreference())
                return;

            if (_avatarProbeInFlight)
                return;

            if (auth == null || !auth.IsLoggedIn)
                return;

            string token = auth.AccessToken;
            if (string.IsNullOrEmpty(token))
                return;

            _avatarProbeInFlight = true;
            _ = ProbeDisplayNameWithAvatarClientAsync(token);
        }

        static async Task ProbeDisplayNameWithAvatarClientAsync(string token)
        {
            try
            {
                var client = new AvatarClient();
                try
                {
                    AvatarResult profile = await client.GetProfile(token);
                    bool applied = profile != null && profile.success && TryApplyOfficialAvatarSdkProfileJson(profile.data);
                    if (applied)
                        return;

                    if (profile != null && !profile.success)
                        DebugLogger.LogWarning($"[NeonSumo][ViverseProfile] GetProfile failed: {profile.error}");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogWarning($"[NeonSumo][ViverseProfile] GetProfile failed: {ex.Message}");
                }

                if (HasUsableStoredDisplayLabelPreference())
                    return;

                AvatarResult list = await client.GetAvatarList(token);
                if (list != null && list.success)
                {
                    ApplyFromAvatarListApiJson(list.data);
                    return;
                }

                if (list != null && !list.success)
                    DebugLogger.LogWarning($"[NeonSumo][ViverseProfile] GetAvatarList failed: {list.error}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogWarning($"[NeonSumo][ViverseProfile] AvatarClient probe failed: {ex.Message}");
            }
            finally
            {
                _avatarProbeInFlight = false;
            }
        }

        /// <summary>Parses AvatarClient.GetProfile JSON (<c>name</c> / <c>displayName</c>). Returns true when a usable display label was written.</summary>
        static bool TryApplyOfficialAvatarSdkProfileJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var jo = JObject.Parse(json);
                JObject payload = UnwrapProfileObject(jo);
                string nm = PickBestStringByKeyPriority(payload ?? jo, ProfileNameKeyPriority, out _);
                if (string.IsNullOrWhiteSpace(nm))
                    nm = PickBestStringByKeyPriority(jo, ProfileNameKeyPriority, out _);
                if (string.IsNullOrWhiteSpace(nm))
                    return false;

                bool changed = ApplyExtracted(nm.Trim(), null);
                if (changed)
                {
                    DebugLogger.Log("[NeonSumo][ViverseProfile] Stored display label from AvatarClient.GetProfile");
                    LocalDisplayProfileChanged?.Invoke();
                }

                // #region agent log
                NeonSumoDebugNdjson.Log(
                    hypothesisId: "H4_H5",
                    location: "NeonSumoPlatformProfiles.TryApplyOfficialAvatarSdkProfileJson",
                    message: "apply_official_avatar_profile",
                    data: new
                    {
                        jsonChars = json?.Length ?? 0,
                        trimmedNameChars = nm?.Trim()?.Length ?? 0,
                        prefsWritten = changed,
                    });
                // #endregion

                return changed;
            }
            catch (Exception ex)
            {
                // #region agent log
                NeonSumoDebugNdjson.Log(
                    hypothesisId: "H5",
                    location: "NeonSumoPlatformProfiles.TryApplyOfficialAvatarSdkProfileJson",
                    message: "official_profile_json_exception",
                    data: new { exType = ex.GetType().Name });
                // #endregion
                DebugLogger.LogWarning($"[NeonSumo][ViverseProfile] Official profile JSON parse failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>True when the sanitized string looks like localized UI placeholders, not an actual profile label.</summary>
        static bool IsGarbageOrUiPlaceholderDisplay(string sanitizedDisplay)
        {
            if (string.IsNullOrEmpty(sanitizedDisplay))
                return true;

            string normalized = sanitizedDisplay.Trim().ToLowerInvariant();
            return normalized is "you" or "me" or "guest" or "anonymous" or "anon" or "default" or "player"
                   || normalized == "user" || normalized == "null" || normalized == "undefined" || normalized == "n/a";
        }

        static string TryBuildLocalDisplayLabelPreferringProfile()
        {
            string fromDisplayKey = PlayerPrefs.GetString(PrefKeyDisplayLabel, string.Empty);
            string sanitizedDisplay = NeonSumoDisplayNameRegistry.Sanitize(fromDisplayKey);

            if (IsGarbageOrUiPlaceholderDisplay(sanitizedDisplay))
            {
                if (!string.IsNullOrWhiteSpace(fromDisplayKey))
                {
                    PlayerPrefs.DeleteKey(PrefKeyDisplayLabel);
                    PlayerPrefs.Save();
                    DebugLogger.Log("[NeonSumo][ViverseProfile] Cleared bogus cached display label (UI placeholder)");
                }

                sanitizedDisplay = null;
            }

            if (!string.IsNullOrEmpty(sanitizedDisplay))
                return sanitizedDisplay;

            string uidRaw = PlayerPrefs.GetString(PrefKeyUserProfileId, string.Empty);
            string sanitizedUid = NeonSumoDisplayNameRegistry.Sanitize(uidRaw);
            if (!string.IsNullOrEmpty(sanitizedUid))
                return sanitizedUid;

            string acc = PlayerPrefs.GetString("viverse_account_id", string.Empty);
            if (string.IsNullOrEmpty(acc))
                acc = PlayerPrefs.GetString("account_id", string.Empty);
            if (!string.IsNullOrEmpty(acc))
            {
                string shortAcc = acc.Length <= 8 ? acc : acc.Substring(0, 8) + "…";
                DebugLogger.Log($"[NeonSumo][ViverseProfile] Using shortened account_id fallback (len={acc.Length})");
                return shortAcc;
            }

            return null;
        }

        static bool ApplyExtracted(string displayCandidate, string userIdCandidate)
        {
            bool any = false;
            if (!string.IsNullOrEmpty(displayCandidate))
            {
                string s = NeonSumoDisplayNameRegistry.Sanitize(displayCandidate);
                if (!string.IsNullOrEmpty(s) && !IsGarbageOrUiPlaceholderDisplay(s))
                {
                    PlayerPrefs.SetString(PrefKeyDisplayLabel, s);
                    any = true;
                }
            }

            if (!string.IsNullOrEmpty(userIdCandidate))
            {
                string s = NeonSumoDisplayNameRegistry.Sanitize(userIdCandidate);
                if (!string.IsNullOrEmpty(s))
                {
                    PlayerPrefs.SetString(PrefKeyUserProfileId, s);
                    any = true;
                }
            }

            if (any)
                PlayerPrefs.Save();

            return any;
        }

        /// <summary>DFS: lowest index in priority list wins.</summary>
        static string PickBestStringByKeyPriority(JToken root, string[] priorityOrdered)
        {
            return PickBestStringByKeyPriority(root, priorityOrdered, out _);
        }

        static string PickBestStringByKeyPriority(JToken root, string[] priorityOrdered, out string pickedKey)
        {
            pickedKey = null;
            if (root == null || priorityOrdered == null || priorityOrdered.Length == 0)
                return null;

            if (!(root is JContainer container))
                return null;

            string bestVal = null;
            int bestRank = int.MaxValue;

            foreach (var prop in container.Descendants().OfType<JProperty>())
            {
                if (prop.Value?.Type != JTokenType.String)
                    continue;

                string v = prop.Value.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(v))
                    continue;

                if (IsGarbageOrUiPlaceholderDisplay(NeonSumoDisplayNameRegistry.Sanitize(v) ?? string.Empty))
                    continue;

                string propName = prop.Name;
                for (int i = 0; i < priorityOrdered.Length; i++)
                {
                    if (string.Equals(propName, priorityOrdered[i], StringComparison.OrdinalIgnoreCase))
                    {
                        if (i < bestRank)
                        {
                            bestRank = i;
                            bestVal = v.Trim();
                            pickedKey = propName;
                        }
                        break;
                    }
                }

                if (bestRank == 0)
                    break;
            }

            return bestVal;
        }

        static JObject UnwrapProfileObject(JObject jo)
        {
            if (jo == null)
                return null;

            if (jo["data"] is JObject nested)
                return nested;

            if (jo["data"] is JValue jv && jv.Type == JTokenType.String)
            {
                string inner = jv.Value<string>();
                if (!string.IsNullOrWhiteSpace(inner))
                {
                    string trimmed = inner.TrimStart();
                    if (trimmed.StartsWith("{"))
                    {
                        try { return JObject.Parse(inner); }
                        catch { /* fall through */ }
                    }
                }
            }

            if (jo["result"] is JObject resultObj)
                return resultObj;

            return jo;
        }
    }
}
