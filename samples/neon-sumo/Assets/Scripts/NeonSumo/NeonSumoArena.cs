using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using ViverseSDK;

namespace NeonSumo
{
    public class NeonSumoArena : MonoBehaviour
    {
        [Header("Arena Settings")]
        [SerializeField] private float arenaRadius = 10f;

        [Header("Fall / elimination")]
        [Tooltip("Absolute arena-local Y: positions below this height are off the arena (void floor). Independent of ball drop distance.")]
        [SerializeField] private float killZoneHeight = -5f;
        [Tooltip("How far below the platform bottom (Platform Transform Renderer or Collider bounds) in arena-local Y the ball may go before off-arena. Requires Platform Transform with bounds. Independent of Kill Zone Height.")]
        [SerializeField] private float ballDropKillDistance = 5f;

        [SerializeField] private bool enableShrink = false;
        [Tooltip("Assign the platform mesh/transform in the inspector.")]
        [SerializeField] private Transform platformTransform;
        [Tooltip("Units per second the danger ring shrinks.")]
        [SerializeField] private float shrinkSpeed = 2f;
        [Tooltip("Pause between ring drops.")]
        [SerializeField] private float shrinkStagePause = 1.0f;
        [Tooltip("Automatically begin shrink when enabled.")]
        [SerializeField] private bool autoStartShrink = true;

        [Header("Ring Drop (Legacy — no NeonSumoRingDropController)")]
        [Tooltip("Used only when a ring has no NeonSumoRingDropController: legacy path unparents the ring and animates it here.")]
        [SerializeField] private float ringDropDistance = 20f;
        [Tooltip("Seconds to complete the drop animation.")]
        [SerializeField] private float ringDropDuration = 1.2f;
        [Tooltip("Degrees per second to spin during drop.")]
        [SerializeField] private float ringDropSpinSpeed = 90f;
        [Tooltip("Disable colliders after a ring drops.")]
        [SerializeField] private bool disableColliderOnDrop = true;

        [Header("Warning Ring")]
        [Tooltip("One LineRenderer per ring (0–4). Assign WarningRing_0 through WarningRing_4 in Inspector for overlapping arcs.")]
        [SerializeField] private LineRenderer[] warningRingRenderers;
        [Tooltip("Legacy: single renderer. Used only if warningRingRenderers is null or empty.")]
        [SerializeField] private LineRenderer warningRingRenderer;
        [Range(16, 256)]
        [SerializeField] private int warningRingSegments = 128;
        [Tooltip("Seconds to complete the warning ring draw.")]
        [SerializeField] private float warningRingDuration = 3f;
        [Tooltip("Line width for the warning ring. Used when warningRingWidthCurve is not assigned.")]
        [SerializeField] private float warningRingWidth = 0.15f;
        [Tooltip("Local height offset for the warning ring.")]
        [SerializeField] private float warningRingHeightOffset = 0.08f;
        [Tooltip("Animate clockwise instead of counter-clockwise (default when not specified).")]
        [SerializeField] private bool warningRingClockwise = true;

        [Header("Warning Ring Assets (Assign in Inspector)")]
        [Tooltip("Material for the warning ring. Create a URP Unlit with emission for neon glow. Assign in Inspector.")]
        [SerializeField] private Material warningRingMaterial;
        [Tooltip("Optional width curve (e.g. thick start, tapered end). Leave empty for flat width.")]
        [SerializeField] private AnimationCurve warningRingWidthCurve;
        [Tooltip("Scroll texture along the line for energy-flow effect. Material must have a texture.")]
        [SerializeField] private bool useTextureScroll = false;
        [Tooltip("Texture scroll speed when useTextureScroll is enabled.")]
        [SerializeField] private float textureScrollSpeed = 2f;

        [Header("Warning Alarm")]
        [Tooltip("Optional. Plays 3 seconds before each ring drop.")]
        [SerializeField] private AudioSource warningAlarmSource;
        [Tooltip("Audio clip for the warning alarm. Assign in Inspector.")]
        [SerializeField] private AudioClip warningAlarmClip;

        [Header("Ring Setup (outer to inner)")]
        [SerializeField] private bool autoCollectRingsFromChildren = true;
        [SerializeField] private Transform coreTransform;
        [Tooltip("Logical Ring_* roots (outer→inner). Add NeonSumoRingDropController on each for intact + pre-placed falling; without it, legacy drop unparents the ring.")]
        [SerializeField] private List<Transform> ringTransforms = new List<Transform>();

        [Header("Danger Ring Visual")]
        [SerializeField] private Transform dangerRingVisual;
        [SerializeField] private float dangerRingBaseRadius = 1f;
        [SerializeField] private float dangerRingHeightOffset = 0.05f;

        [Header("Spawn Settings")]
        [SerializeField] private float spawnRadius = 8f;
        [SerializeField] private int spawnAngleOffset = 0;

        [Header("Ramp Spawns")]
        [Tooltip("Assign ramp-top spawn points in the inspector.")]
        [SerializeField] private Transform[] rampSpawnPoints;
        [Tooltip("Vertical offset applied to ramp spawn positions.")]
        [SerializeField] private float rampSpawnHeightOffset = 0.35f;
        [Tooltip("Draw gizmos for ramp spawns.")]
        [SerializeField] private bool drawRampSpawnGizmos = true;

        [Header("Physics Tuning")]
        [SerializeField] private PhysicsMaterial arenaPhysicsMaterial;

        // Properties for external access (e.g. NeonSumoOfflineBootstrap, NeonSumoArenaEditor)
        public bool EnableShrink { get => enableShrink; set => enableShrink = value; }
        public bool AutoStartShrink { get => autoStartShrink; set => autoStartShrink = value; }
        public float WarningRingDuration => warningRingDuration;
        public List<Transform> RingTransforms => ringTransforms;
        public Transform CoreTransform => coreTransform;

        private float _currentRadius;
        private int _maxPlayers = 8; // Will be set from game manager
        private float _dangerRadius;
        private float _stageTargetRadius;
        private float _stagePauseTimer;
        private int _nextRingIndex = 0;
        private bool _isShrinking = false;
        private Coroutine[] _warningRingRoutines;
        private Coroutine _warningShrinkRoutine;
        private bool _useWarningShrink = false;

        private MultiplayerClient _multiplayerClient;
        private bool _isMasterClient = false;

        private readonly HashSet<Transform> _droppedRings = new HashSet<Transform>();
        private readonly List<RingState> _ringStates = new List<RingState>();

        // Read-only property for UI warnings, camera zoom, FX near collapse
        public float CurrentRadius => _currentRadius;

        void Start()
        {
            _currentRadius = arenaRadius;
            if (platformTransform == null)
            {
                DebugLogger.LogWarning("[NeonSumoArena] PlatformTransform not assigned in inspector.");
            }

            CollectRingsIfNeeded();
            CacheRingStates();
            InitializeRingPhysics();
            ApplyArenaPhysicsMaterial();
            InitializeDangerRing();
            EnsureWarningRingRenderer();
            InitializeWarningRingRoutines();
            StopAllWarningRings();

            if (enableShrink && autoStartShrink && !_useWarningShrink)
            {
                BeginShrink();
            }
        }

        void Update()
        {
            TickShrink();
        }

        private void TickShrink()
        {
            if (_useWarningShrink) return;
            if (!enableShrink || !_isShrinking || ringTransforms.Count == 0) return;
            if (_nextRingIndex >= ringTransforms.Count)
            {
                _isShrinking = false;
                return;
            }
            if (_stagePauseTimer > 0f)
            {
                _stagePauseTimer -= Time.deltaTime;
                return;
            }

            _dangerRadius = Mathf.MoveTowards(_dangerRadius, _stageTargetRadius, shrinkSpeed * Time.deltaTime);
            SetDangerRingRadius(_dangerRadius);
            if (Mathf.Approximately(_dangerRadius, _stageTargetRadius))
            {
                DropRing(_nextRingIndex, isRemote: false);
                PrepareNextStage();
            }
        }

        public void SetMaxPlayers(int maxPlayers)
        {
            _maxPlayers = maxPlayers;
        }

        public void ConfigureNetwork(MultiplayerClient multiplayerClient, bool isMasterClient)
        {
            _multiplayerClient = multiplayerClient;
            _isMasterClient = isMasterClient;
            DebugLogger.Log("[NeonSumoArena] ConfigureNetwork client=" + (multiplayerClient != null ? "SET" : "NULL") + " isMaster=" + isMasterClient.ToString());
        }

        public void BeginShrink()
        {
            if (ringTransforms.Count == 0)
            {
                DebugLogger.LogWarning("[NeonSumoArena] Shrink enabled but no rings configured.");
                return;
            }

            _isShrinking = true;
            _nextRingIndex = Mathf.Clamp(_nextRingIndex, 0, ringTransforms.Count);
            _dangerRadius = _currentRadius;
            PrepareNextStage();
        }

        public void ApplyRemoteRingDrop(int ringIndex)
        {
            DropRing(ringIndex, isRemote: true);
        }

        public void ResetArena()
        {
            _droppedRings.Clear();
            StopAllWarningRings();
            StopWarningShrink();
            foreach (var state in _ringStates)
            {
                if (state == null || state.ring == null) continue;
                if (state.ringDropController != null)
                    state.ringDropController.ResetForArena();

                state.ring.gameObject.SetActive(true);
                state.ring.SetParent(transform, false);
                state.ring.localPosition = state.localPosition;
                state.ring.localRotation = state.localRotation;
                state.ring.localScale = state.localScale;
                if (state.rb != null)
                {
                    state.rb.isKinematic = true;
                    state.rb.useGravity = false;
                }
                foreach (var collider in state.ring.GetComponentsInChildren<Collider>())
                {
                    collider.enabled = true;
                }
            }

            _nextRingIndex = 0;
            _currentRadius = arenaRadius;
            _dangerRadius = _currentRadius;
            _stagePauseTimer = 0f;
            _isShrinking = false;
            _useWarningShrink = false;
            InitializeDangerRing();
            SetAllWarningRingsVisible(false);
        }

        public void StopShrink()
        {
            _isShrinking = false;
            _useWarningShrink = false;
            StopAllWarningRings();
            StopWarningShrink();
        }

        public void BeginShrinkWithWarnings(float warningDuration, bool shouldDropOnComplete)
        {
            if (ringTransforms.Count == 0)
            {
                DebugLogger.LogWarning("[NeonSumoArena] Shrink enabled but no rings configured.");
                return;
            }

            StopWarningShrink();
            _useWarningShrink = true;
            _isShrinking = false;
            _nextRingIndex = Mathf.Clamp(_nextRingIndex, 0, ringTransforms.Count);
            _warningShrinkRoutine = StartCoroutine(WarningShrinkRoutine(warningDuration, shouldDropOnComplete));
        }

        public void PlayRingWarning(int ringIndex, float duration, bool shouldDropOnComplete = true)
        {
            PlayRingWarning(ringIndex, duration, warningRingClockwise, shouldDropOnComplete);
        }

        /// <summary>Draw the warning line for a ring without dropping it. Used for time-based shrink.</summary>
        public void PlayWarningLine(int ringIndex, float duration, bool clockwise)
        {
            if (ringTransforms.Count == 0) return;

            var lineRenderer = GetLineRendererForRing(ringIndex);
            if (lineRenderer == null) return;

            float radius = GetWarningRadius(ringIndex);
            if (radius <= 0f) return;

            StopWarningRingForIndex(ringIndex);
            if (_warningRingRoutines != null && ringIndex < _warningRingRoutines.Length)
            {
                _warningRingRoutines[ringIndex] = StartCoroutine(AnimateWarningRing(ringIndex, lineRenderer, radius, duration, clockwise, shouldDropOnComplete: false));
            }
        }

        /// <summary>Play the warning alarm sound (3 seconds before each ring drop).</summary>
        public void PlayWarningAlarm()
        {
            if (warningAlarmClip == null) return;

            var source = warningAlarmSource != null ? warningAlarmSource : GetComponent<AudioSource>();
            if (source != null && source.enabled && source.gameObject.activeInHierarchy)
            {
                source.PlayOneShot(warningAlarmClip);
            }
            else
            {
                // Fallback: play at arena position (works even without AudioSource)
                AudioSource.PlayClipAtPoint(warningAlarmClip, transform.position);
            }
        }

        /// <summary>Trigger a ring drop (host-only; clients receive via ActionSync ring_drop).</summary>
        public void TriggerRingDrop(int ringIndex)
        {
            DropRing(ringIndex, isRemote: false);
        }

        /// <summary>Same as <see cref="TriggerRingDrop(int)"/> but resolves the ring index from the controller's transform.</summary>
        public void TriggerRingDrop(NeonSumoRingDropController ringDropController)
        {
            if (ringDropController == null)
                return;
            Transform t = ringDropController.transform;
            for (int i = 0; i < ringTransforms.Count; i++)
            {
                if (ringTransforms[i] == t)
                {
                    TriggerRingDrop(i);
                    return;
                }
            }
            DebugLogger.LogWarning("[NeonSumoArena] TriggerRingDrop(controller): transform not found in ringTransforms.");
        }

        private void PlayRingWarning(int ringIndex, float duration, bool clockwise, bool shouldDropOnComplete)
        {
            if (ringTransforms.Count == 0) return;

            var lineRenderer = GetLineRendererForRing(ringIndex);
            if (lineRenderer == null)
            {
                if (shouldDropOnComplete) DropRing(ringIndex, isRemote: false);
                return;
            }

            float radius = GetWarningRadius(ringIndex);
            if (radius <= 0f) return;

            StopWarningRingForIndex(ringIndex);
            if (_warningRingRoutines != null && ringIndex < _warningRingRoutines.Length)
            {
                _warningRingRoutines[ringIndex] = StartCoroutine(AnimateWarningRing(ringIndex, lineRenderer, radius, duration, clockwise, shouldDropOnComplete));
            }
        }

        // FIX #3: Make spawn logic player-count aware
        public Vector3 GetSpawnPosition(int playerIndex, int totalPlayerCount)
        {
            // Use actual player count, not hardcoded 8
            float angleStep = 360f / Mathf.Max(totalPlayerCount, 2); // Minimum 2 players
            float angle = angleStep * playerIndex + spawnAngleOffset;
            float rad = angle * Mathf.Deg2Rad;
            
            float spawnDist = Mathf.Lerp(spawnRadius * 0.5f, spawnRadius, UnityEngine.Random.Range(0f, 1f));
            Vector3 pos = new Vector3(
                Mathf.Cos(rad) * spawnDist,
                1f, // Slightly above platform
                Mathf.Sin(rad) * spawnDist
            );
            
            return transform.TransformPoint(pos);
        }

        public bool HasRampSpawns()
        {
            return rampSpawnPoints != null && rampSpawnPoints.Length > 0;
        }

        public Vector3 GetRampSpawnPosition(int spawnIndex)
        {
            if (!HasRampSpawns())
            {
                return GetSpawnPosition(spawnIndex, _maxPlayers);
            }

            int index = Mathf.Abs(spawnIndex) % rampSpawnPoints.Length;
            Transform point = rampSpawnPoints[index];
            if (point == null)
            {
                return GetSpawnPosition(spawnIndex, _maxPlayers);
            }

            return point.position + Vector3.up * rampSpawnHeightOffset;
        }

        public Quaternion GetRampSpawnRotation(int spawnIndex)
        {
            if (!HasRampSpawns())
            {
                return Quaternion.identity;
            }

            int index = Mathf.Abs(spawnIndex) % rampSpawnPoints.Length;
            Transform point = rampSpawnPoints[index];
            return point != null ? point.rotation : Quaternion.identity;
        }

        public bool IsPositionOnArena(Vector3 position)
        {
            Vector3 localPos = transform.InverseTransformPoint(position);
            if (localPos.y < killZoneHeight)
                return false;

            if (TryGetPlatformFloorLocalY(out float floorLocalY))
            {
                float dropPlaneLocalY = floorLocalY - Mathf.Max(0f, ballDropKillDistance);
                if (localPos.y < dropPlaneLocalY)
                    return false;
            }

            return true;
        }

        /// <summary>Lowest platform point in arena local Y, from platform transform Renderer or Collider bounds.</summary>
        bool TryGetPlatformFloorLocalY(out float floorLocalY)
        {
            floorLocalY = 0f;
            if (platformTransform == null)
                return false;

            Bounds b;
            var renderer = platformTransform.GetComponent<Renderer>();
            if (renderer != null)
                b = renderer.bounds;
            else
            {
                var col = platformTransform.GetComponent<Collider>();
                if (col == null)
                    return false;
                b = col.bounds;
            }

            Vector3 worldOnBottom = new Vector3(b.center.x, b.min.y, b.center.z);
            floorLocalY = transform.InverseTransformPoint(worldOnBottom).y;
            return true;
        }

        void OnDrawGizmosSelected()
        {
            // Draw arena bounds
            Gizmos.color = Color.yellow;
            float radius = Application.isPlaying ? _currentRadius : arenaRadius;
            Gizmos.DrawWireSphere(transform.position, radius);
            
            // Draw spawn positions (using maxPlayers for visualization)
            Gizmos.color = Color.green;
            for (int i = 0; i < _maxPlayers; i++)
            {
                Vector3 spawnPos = GetSpawnPosition(i, _maxPlayers);
                Gizmos.DrawSphere(spawnPos, 0.5f);
            }

            if (drawRampSpawnGizmos && rampSpawnPoints != null)
            {
                Gizmos.color = Color.cyan;
                foreach (var point in rampSpawnPoints)
                {
                    if (point == null) continue;
                    Gizmos.DrawSphere(point.position + Vector3.up * rampSpawnHeightOffset, 0.35f);
                }
            }
            
            // Absolute kill zone (arena-local Y)
            Gizmos.color = Color.red;
            Vector3 killStart = transform.TransformPoint(new Vector3(0f, killZoneHeight, 0f));
            Vector3 killEnd = transform.TransformPoint(new Vector3(0f, killZoneHeight, radius));
            Gizmos.DrawLine(killStart, killEnd);

            // Platform-relative drop plane (when bounds available)
            if (TryGetPlatformFloorLocalY(out float floorLocalY))
            {
                float dropLocalY = floorLocalY - Mathf.Max(0f, ballDropKillDistance);
                Gizmos.color = new Color(1f, 0.5f, 0f);
                Vector3 dropStart = transform.TransformPoint(new Vector3(0f, dropLocalY, 0f));
                Vector3 dropEnd = transform.TransformPoint(new Vector3(0f, dropLocalY, radius));
                Gizmos.DrawLine(dropStart, dropEnd);
            }
        }

        private void CollectRingsIfNeeded()
        {
            if (!autoCollectRingsFromChildren || ringTransforms.Count > 0)
            {
                CacheRadiiAndArenaRadius();
                return;
            }

            ringTransforms.Clear();
            foreach (Transform child in transform)
            {
                if (child == null) continue;
                if ((child.name.Equals("Core", StringComparison.OrdinalIgnoreCase)
                    || child.name.Equals("CoreDisk", StringComparison.OrdinalIgnoreCase))
                    && coreTransform == null)
                {
                    coreTransform = child;
                    continue;
                }

                if (child.name.StartsWith("Ring_", StringComparison.OrdinalIgnoreCase))
                {
                    ringTransforms.Add(child);
                }
            }

            ringTransforms.Sort((a, b) => ArenaMeasurementUtility.GetHorizontalRadius(b).CompareTo(ArenaMeasurementUtility.GetHorizontalRadius(a)));
            CacheRadiiAndArenaRadius();
        }

        private void CacheRadiiAndArenaRadius()
        {
            if (ringTransforms.Count > 0)
            {
                arenaRadius = ArenaMeasurementUtility.GetHorizontalRadius(ringTransforms[0]);
                _currentRadius = arenaRadius;
                _dangerRadius = _currentRadius;
            }
            else if (coreTransform != null)
            {
                arenaRadius = ArenaMeasurementUtility.GetHorizontalRadius(coreTransform);
                _currentRadius = arenaRadius;
                _dangerRadius = _currentRadius;
            }
        }

        private void CacheRingStates()
        {
            _ringStates.Clear();
            foreach (var ring in ringTransforms)
            {
                if (ring == null) continue;
                var state = new RingState
                {
                    ring = ring,
                    localPosition = ring.localPosition,
                    localRotation = ring.localRotation,
                    localScale = ring.localScale,
                    rb = ring.GetComponent<Rigidbody>(),
                    ringDropController = ring.GetComponent<NeonSumoRingDropController>()
                };
                _ringStates.Add(state);
            }
        }

        private void InitializeRingPhysics()
        {
            foreach (var ring in ringTransforms)
            {
                if (ring == null) continue;
                if (ring.GetComponent<NeonSumoRingDropController>() != null)
                    continue;

                var rb = ring.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    rb = ring.gameObject.AddComponent<Rigidbody>();
                }
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        private void ApplyArenaPhysicsMaterial()
        {
            if (arenaPhysicsMaterial == null)
            {
                return;
            }

            Transform root = platformTransform != null ? platformTransform : transform;
            foreach (var collider in root.GetComponentsInChildren<Collider>())
            {
                collider.sharedMaterial = arenaPhysicsMaterial;
            }
        }

        private void InitializeDangerRing()
        {
            if (dangerRingVisual == null) return;
            var localPos = dangerRingVisual.localPosition;
            localPos.y = dangerRingHeightOffset;
            dangerRingVisual.localPosition = localPos;
            SetDangerRingRadius(_dangerRadius);
        }

        private void SetDangerRingRadius(float radius)
        {
            if (dangerRingVisual == null || dangerRingBaseRadius <= 0f) return;
            float scale = radius / dangerRingBaseRadius;
            var localScale = dangerRingVisual.localScale;
            dangerRingVisual.localScale = new Vector3(scale, localScale.y, scale);
        }

        private void PrepareNextStage()
        {
            if (_nextRingIndex >= ringTransforms.Count)
            {
                _isShrinking = false;
                return;
            }

            _stageTargetRadius = GetTargetRadiusForStage(_nextRingIndex);
            _stagePauseTimer = shrinkStagePause;
            _dangerRadius = _currentRadius;
            SetDangerRingRadius(_dangerRadius);
        }

        private float GetTargetRadiusForStage(int ringIndex)
        {
            int nextIndex = ringIndex + 1;
            if (nextIndex < ringTransforms.Count)
            {
                return ArenaMeasurementUtility.GetHorizontalRadius(ringTransforms[nextIndex]);
            }

            if (coreTransform != null)
            {
                return ArenaMeasurementUtility.GetHorizontalRadius(coreTransform);
            }

            return _currentRadius;
        }

        private void DropRing(int ringIndex, bool isRemote)
        {
            if (!TryDropRingState(ringIndex, isRemote, out Transform ring))
                return;

            ApplyDroppedRingVisuals(ring);
            _currentRadius = GetTargetRadiusForStage(ringIndex);
            _dangerRadius = _currentRadius;
            SetDangerRingRadius(_dangerRadius);
            _nextRingIndex = Mathf.Max(_nextRingIndex, ringIndex + 1);

            BroadcastRingDrop(ringIndex, isRemote);
        }

        private bool TryDropRingState(int ringIndex, bool isRemote, out Transform ring)
        {
            ring = null;
            if (ringIndex < 0 || ringIndex >= ringTransforms.Count)
            {
                DebugLogger.LogWarning("[NeonSumoArena] DropRing ignored: ringIndex=" + ringIndex.ToString() + " ringCount=" + ringTransforms.Count.ToString());
                return false;
            }

            ring = ringTransforms[ringIndex];
            if (ring == null)
            {
                DebugLogger.LogWarning("[NeonSumoArena] DropRing ignored: ringIndex=" + ringIndex.ToString() + " ring is NULL");
                return false;
            }

            if (_droppedRings.Contains(ring))
            {
                DebugLogger.LogWarning("[NeonSumoArena] DropRing ignored: ringIndex=" + ringIndex.ToString() + " already dropped");
                return false;
            }

            DebugLogger.Log("[NeonSumoArena] DropRing ringIndex=" + ringIndex.ToString() + " isRemote=" + isRemote.ToString() + " hasClient=" + (_multiplayerClient != null).ToString() + " isMaster=" + _isMasterClient.ToString());
            _droppedRings.Add(ring);
            return true;
        }

        /// <summary>Host-only visual drop. Rings with <see cref="NeonSumoRingDropController"/> stay parented to the arena; legacy rings unparent.</summary>
        private void ApplyDroppedRingVisuals(Transform ring)
        {
            if (ring.TryGetComponent<NeonSumoRingDropController>(out var dropController))
            {
                dropController.BeginDrop();
                return;
            }

            // Legacy: animate the ring transform itself (unparents; breaks static-friendly setup).
            ring.SetParent(null, true);
            var rb = ring.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            StartCoroutine(AnimateRingDrop(ring));
        }

        private void BroadcastRingDrop(int ringIndex, bool isRemote)
        {
            if (isRemote) return;
            if (_multiplayerClient != null && _isMasterClient)
            {
                DebugLogger.Log("[NeonSumoArena] Sending ActionSync ring_drop ringIndex=" + ringIndex.ToString());
                NeonSumoActionSyncSend.Competition(_multiplayerClient, NeonSumoNetEvents.RingDrop, ringIndex.ToString(), Guid.NewGuid().ToString());
            }
            else
            {
                DebugLogger.Log("[NeonSumoArena] Skip ActionSync ring_drop: hasClient=" + (_multiplayerClient != null).ToString() + " isMaster=" + _isMasterClient.ToString());
            }
        }

        private IEnumerator AnimateWarningRing(int ringIndex, LineRenderer lineRenderer, float radius, float duration, bool clockwise, bool shouldDropOnComplete)
        {
            if (lineRenderer == null)
            {
                if (shouldDropOnComplete)
                {
                    DropRing(ringIndex, isRemote: false);
                }
                yield break;
            }

            SetWarningRingVisible(lineRenderer, true);
            float totalTime = Mathf.Max(0.05f, duration);
            float elapsed = 0f;
            Material animMaterial = useTextureScroll && warningRingMaterial != null ? lineRenderer.material : null;
            bool hasTex = animMaterial != null && (animMaterial.HasProperty("_MainTex") || animMaterial.HasProperty("_BaseMap"));

            while (elapsed < totalTime)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / totalTime);
                DrawWarningArc(lineRenderer, radius, progress, clockwise);
                if (hasTex)
                {
                    var offset = animMaterial.mainTextureOffset;
                    offset.x += textureScrollSpeed * Time.deltaTime;
                    animMaterial.mainTextureOffset = offset;
                }
                yield return null;
            }

            DrawWarningArc(lineRenderer, radius, 1f, clockwise);
            yield return null; // Ensure a full ring renders for one frame.
            SetWarningRingVisible(lineRenderer, false);

            if (_warningRingRoutines != null && ringIndex < _warningRingRoutines.Length)
            {
                _warningRingRoutines[ringIndex] = null;
            }

            if (shouldDropOnComplete)
            {
                DropRing(ringIndex, isRemote: false);
            }
        }

        private IEnumerator WarningShrinkRoutine(float warningDuration, bool shouldDropOnComplete)
        {
            float stepDuration = Mathf.Max(0.05f, warningDuration);
            float pauseDuration = Mathf.Max(0f, shrinkStagePause);

            while (_nextRingIndex < ringTransforms.Count)
            {
                int ringIndex = _nextRingIndex;
                PlayRingWarning(ringIndex, stepDuration, shouldDropOnComplete);
                yield return new WaitForSeconds(stepDuration + pauseDuration);
                _nextRingIndex = ringIndex + 1;
            }

            _useWarningShrink = false;
        }

        private IEnumerator AnimateRingDrop(Transform ring)
        {
            if (ring == null) yield break;

            float duration = Mathf.Max(0.05f, ringDropDuration);
            Vector3 start = ring.position;
            Vector3 end = start + Vector3.down * Mathf.Max(0f, ringDropDistance);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                ring.position = Vector3.Lerp(start, end, t);
                if (!Mathf.Approximately(ringDropSpinSpeed, 0f))
                {
                    ring.Rotate(Vector3.up, ringDropSpinSpeed * Time.deltaTime, Space.World);
                }
                yield return null;
            }

            ring.position = end;

            if (disableColliderOnDrop)
            {
                foreach (var collider in ring.GetComponentsInChildren<Collider>())
                {
                    collider.enabled = false;
                }
            }
        }

        private void StopWarningRingForIndex(int ringIndex)
        {
            if (_warningRingRoutines == null || ringIndex < 0 || ringIndex >= _warningRingRoutines.Length)
                return;
            if (_warningRingRoutines[ringIndex] != null)
            {
                StopCoroutine(_warningRingRoutines[ringIndex]);
                _warningRingRoutines[ringIndex] = null;
            }
        }

        private void StopAllWarningRings()
        {
            if (_warningRingRoutines != null)
            {
                for (int i = 0; i < _warningRingRoutines.Length; i++)
                {
                    if (_warningRingRoutines[i] != null)
                    {
                        StopCoroutine(_warningRingRoutines[i]);
                        _warningRingRoutines[i] = null;
                    }
                }
            }
            SetAllWarningRingsVisible(false);
        }

        private void StopWarningShrink()
        {
            if (_warningShrinkRoutine != null)
            {
                StopCoroutine(_warningShrinkRoutine);
                _warningShrinkRoutine = null;
            }
        }

        private void SetWarningRingVisible(LineRenderer lineRenderer, bool visible)
        {
            if (lineRenderer == null) return;

            lineRenderer.enabled = visible;
            if (!visible)
            {
                lineRenderer.positionCount = 0;
            }
        }

        private void SetAllWarningRingsVisible(bool visible)
        {
            if (warningRingRenderers != null)
            {
                foreach (var line in warningRingRenderers)
                {
                    if (line != null)
                        SetWarningRingVisible(line, visible);
                }
            }
            if (warningRingRenderer != null)
            {
                SetWarningRingVisible(warningRingRenderer, visible);
            }
        }

        private void ApplyWarningRingSettings(LineRenderer line)
        {
            if (line == null) return;

            line.useWorldSpace = true;
            line.loop = false;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 8;
            line.positionCount = 0;

            // Material: use assigned asset only (no runtime creation)
            if (warningRingMaterial != null)
            {
                line.sharedMaterial = warningRingMaterial;
            }
            else
            {
                DebugLogger.LogWarning("[NeonSumoArena] warningRingMaterial not assigned. Create a URP Unlit material with emission and assign in Inspector.");
            }

            // Color source of truth: material tint only. Keep vertex color neutral.
            line.colorGradient = CreateDefaultWarningGradient();

            // Width: curve or flat
            if (warningRingWidthCurve != null)
            {
                line.widthCurve = warningRingWidthCurve;
                line.widthMultiplier = warningRingWidth;
            }
            else
            {
                line.widthMultiplier = warningRingWidth;
                line.startWidth = warningRingWidth;
                line.endWidth = warningRingWidth;
            }
        }

        private void EnsureWarningRingRenderer()
        {
            if (warningRingRenderers != null && warningRingRenderers.Length > 0)
            {
                foreach (var line in warningRingRenderers)
                {
                    if (line != null)
                        ApplyWarningRingSettings(line);
                }
                return;
            }

            // Try to auto-collect from WarningRing_0..4 for migration
            var list = new List<LineRenderer>();
            for (int i = 0; i < 5; i++)
            {
                var child = transform.Find("WarningRing_" + i.ToString());
                if (child != null)
                {
                    var line = child.GetComponent<LineRenderer>();
                    if (line != null)
                    {
                        list.Add(line);
                        ApplyWarningRingSettings(line);
                    }
                }
            }
            if (list.Count > 0)
            {
                warningRingRenderers = list.ToArray();
                DebugLogger.Log("[NeonSumoArena] Auto-collected " + warningRingRenderers.Length.ToString() + " warning ring renderers from children.");
                return;
            }

            // Fallback: single WarningRing (legacy)
            if (warningRingRenderer == null)
            {
                var existing = transform.Find("WarningRing");
                if (existing != null)
                    warningRingRenderer = existing.GetComponent<LineRenderer>();
            }
            if (warningRingRenderer != null)
            {
                ApplyWarningRingSettings(warningRingRenderer);
                warningRingRenderers = new[] { warningRingRenderer };
                DebugLogger.Log("[NeonSumoArena] Using single WarningRing (legacy). Assign warningRingRenderers in Inspector for overlapping arcs.");
            }
            else
            {
                DebugLogger.LogWarning("[NeonSumoArena] No warning ring renderers. Assign warningRingRenderers (WarningRing_0..4) or create WarningRing child.");
            }
        }

        private void InitializeWarningRingRoutines()
        {
            int count = warningRingRenderers != null ? warningRingRenderers.Length : 0;
            if (count == 0 && warningRingRenderer != null)
                count = 1;
            _warningRingRoutines = new Coroutine[count];
        }

        private LineRenderer GetLineRendererForRing(int ringIndex)
        {
            if (warningRingRenderers != null && warningRingRenderers.Length > 0)
            {
                if (ringIndex >= 0 && ringIndex < warningRingRenderers.Length)
                    return warningRingRenderers[ringIndex];
                return null;
            }
            if (warningRingRenderer != null && ringIndex == 0)
                return warningRingRenderer;
            return null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (warningRingRenderers != null && warningRingRenderers.Length > 0) return;
            if (warningRingRenderer != null) return;

            var list = new List<LineRenderer>();
            for (int i = 0; i < 5; i++)
            {
                var existing = transform.Find("WarningRing_" + i.ToString());
                if (existing != null)
                {
                    var line = existing.GetComponent<LineRenderer>();
                    if (line != null) list.Add(line);
                }
                else
                {
                    var go = new GameObject("WarningRing_" + i.ToString());
                    go.transform.SetParent(transform, false);
                    var line = go.AddComponent<LineRenderer>();
                    ApplyWarningRingSettings(line);
                    list.Add(line);
                }
            }
            if (list.Count > 0)
            {
                warningRingRenderers = list.ToArray();
                return;
            }

            var legacy = transform.Find("WarningRing");
            if (legacy != null)
            {
                warningRingRenderer = legacy.GetComponent<LineRenderer>();
                return;
            }

            var warningObject = new GameObject("WarningRing");
            warningObject.transform.SetParent(transform, false);
            var lineRenderer = warningObject.AddComponent<LineRenderer>();
            ApplyWarningRingSettings(lineRenderer);
            warningRingRenderer = lineRenderer;
        }
#endif

        private static Gradient CreateDefaultWarningGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                });
            return gradient;
        }

        private void DrawWarningArc(LineRenderer lineRenderer, float radius, float progress, bool clockwise)
        {
            if (lineRenderer == null) return;

            int segments = Mathf.Clamp(warningRingSegments, 16, 512);
            int visibleSegments = Mathf.CeilToInt(segments * Mathf.Clamp01(progress));
            if (visibleSegments <= 0)
            {
                lineRenderer.positionCount = 0;
                return;
            }

            bool useWorldSpace = lineRenderer.useWorldSpace;
            lineRenderer.positionCount = visibleSegments + 1;
            float direction = clockwise ? -1f : 1f;
            float baseHeight = GetWarningRingWorldHeight();

            for (int i = 0; i <= visibleSegments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f * direction;
                Vector3 localPos = new Vector3(
                    Mathf.Cos(angle) * radius,
                    warningRingHeightOffset,
                    Mathf.Sin(angle) * radius
                );
                if (useWorldSpace)
                {
                    var worldPos = transform.TransformPoint(localPos);
                    worldPos.y = baseHeight;
                    lineRenderer.SetPosition(i, worldPos);
                }
                else
                {
                    localPos.y = transform.InverseTransformPoint(new Vector3(0f, baseHeight, 0f)).y;
                    lineRenderer.SetPosition(i, localPos);
                }
            }
        }

        private float GetWarningRadius(int ringIndex)
        {
            if (ringIndex < 0 || ringIndex >= ringTransforms.Count) return 0f;

            float outerRadius = ArenaMeasurementUtility.GetHorizontalRadius(ringTransforms[ringIndex]);
            float innerRadius;

            int nextIndex = ringIndex + 1;
            if (nextIndex < ringTransforms.Count)
            {
                innerRadius = ArenaMeasurementUtility.GetHorizontalRadius(ringTransforms[nextIndex]);
            }
            else if (coreTransform != null)
            {
                innerRadius = ArenaMeasurementUtility.GetHorizontalRadius(coreTransform);
            }
            else
            {
                innerRadius = outerRadius;
            }

            return innerRadius;
        }

        private float GetWarningRingWorldHeight()
        {
            if (platformTransform != null)
            {
                var renderer = platformTransform.GetComponent<Renderer>();
                if (renderer != null)
                {
                    return renderer.bounds.max.y + warningRingHeightOffset;
                }
            }

            return transform.position.y + warningRingHeightOffset;
        }

        [Serializable]
        private class RingState
        {
            public Transform ring;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public Rigidbody rb;
            public NeonSumoRingDropController ringDropController;
        }
    }
}


