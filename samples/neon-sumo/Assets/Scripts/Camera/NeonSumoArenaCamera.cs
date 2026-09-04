using UnityEngine;
using System.Collections.Generic;

namespace NeonSumo
{
    public enum ArenaCameraMode
    {
        /// <summary>Scene / reset pose for the whole match; positional impact shake only (Fusion-style).</summary>
        FixedArena,
        /// <summary>Centroid orbit framing; gated until ramp roll-in completes.</summary>
        DynamicSpectator
    }

    /// <summary>
    /// Arena camera for NeonSumo: either fixed authored pose with shake, or dynamic spectator framing
    /// (centroid, zoom, smooth orbit). Use <see cref="ArenaCameraMode.FixedArena"/> to avoid intro-to-gameplay jumps.
    /// </summary>
    public class NeonSumoArenaCamera : MonoBehaviour
    {
        [Header("Mode")]
        [Tooltip("FixedArena: stay at reset/scene pose during play (Fusion-style). DynamicSpectator: orbit / zoom on player cluster.")]
        [SerializeField] private ArenaCameraMode cameraMode = ArenaCameraMode.FixedArena;

        [Header("References")]
        [Tooltip("Assign in Inspector. Fallback: FindFirstObjectByType if null.")]
        [SerializeField] private NeonSumoGameManager gameManager;
        [Tooltip("Assign in Inspector. Fallback: FindFirstObjectByType if null.")]
        [SerializeField] private NeonSumoArena arena;

        [Header("Zoom (DynamicSpectator only)")]
        [SerializeField] private float baseDistance = 14f;
        [SerializeField] private float zoomMultiplier = 1.65f;
        [SerializeField] private float minDistance = 12f;
        [SerializeField] private float maxDistance = 70f;
        [Tooltip("Minimum radius when only one player (avoids extreme zoom-in).")]
        [SerializeField] private float minRadiusSinglePlayer = 1.5f;
        [Tooltip("Extra meters added to player spread when fitting the view frustum (avatar size / margin).")]
        [SerializeField] private float extraFramingRadius = 1.35f;
        [Tooltip("Scales frustum-derived distance to compensate for look-down pitch vs. horizontal spread.")]
        [SerializeField] private float frustumDistanceFudge = 1.28f;

        [Header("Angle (DynamicSpectator only)")]
        [SerializeField] private float pitchDegrees = 30f;
        [SerializeField] private float yawDegrees = 45f;
        [SerializeField] private float lookAtHeightOffset = 1.5f;

        [Header("Framing (DynamicSpectator only)")]
        [Tooltip("Shifts focus from arena origin toward the living-player cluster (0 = centroid only).")]
        [SerializeField] [Range(0f, 1f)] private float clusterFramingPull = 0.22f;

        [Header("Lens")]
        [SerializeField] private bool applyFieldOfView = true;
        [SerializeField] private float fieldOfView = 42f;

        [Header("Smoothing (DynamicSpectator only)")]
        [SerializeField] private float positionSmoothTime = 0.18f;
        [Tooltip("Stronger damp for ~this many seconds after dynamic framing first becomes allowed (ramp roll finished).")]
        [SerializeField] private float postUnlockEaseDuration = 0.75f;
        [SerializeField] private float postUnlockPositionSmoothTime = 0.32f;
        [Tooltip("Smooth raw framing centroid / radius before computing camera position (reduces spikes on elimination).")]
        [SerializeField] private float targetCenterSmoothTime = 0.22f;
        [SerializeField] private float targetRadiusExpandSmoothTime = 0.1f;
        [SerializeField] private float targetRadiusContractSmoothTime = 0.32f;
        [Tooltip("Smooth average velocity used for tilt.")]
        [SerializeField] private float targetVelocitySmoothTime = 0.15f;
        [Tooltip("If true, teleports once when dynamic framing starts (can be disorienting). Keep off for smooth transition from intro camera position.")]
        [SerializeField] private bool snapWhenGameplayStarts = false;
        [Tooltip("Max world-units per second for camera position (0 = uncapped).")]
        [SerializeField] private float maxPositionSpeed = 28f;

        [Header("Rotation (DynamicSpectator only)")]
        [Tooltip("Max degrees per second when rotating toward the look target.")]
        [SerializeField] private float maxRotationDegreesPerSecond = 95f;

        [Header("Look-Ahead (DynamicSpectator only)")]
        [SerializeField] private float lookAheadFactor = 0.35f;
        [SerializeField] private float lookAheadSmoothTime = 0.2f;
        [SerializeField] private float maxLookAheadDistance = 2.5f;
        [SerializeField] private float lookAheadSuppressionRadius = 12f;
        [SerializeField] private float lookAheadRadiusPaddingFactor = 0.6f;

        [Header("Chaos Tilt (DynamicSpectator only)")]
        [SerializeField] private float tiltSpeedFactor = 0.3f;
        [SerializeField] private float maxTiltDegrees = 6f;

        [Header("Camera Priority Phases (DynamicSpectator only)")]
        [Tooltip("Slow clockwise camera orbit speed during early match framing.")]
        [SerializeField] private float introOrbitDegreesPerSecond = 3f;
        [Tooltip("Maximum duration for early orbit behavior. Capped to half rotation over this window.")]
        [SerializeField] private float introOrbitDuration = 60f;
        [Tooltip("Early/mid match phase blend. Values below 1 are ignored for strict fit so players never leave frame.")]
        [SerializeField] private float earlyPhaseZoomMultiplier = 0.92f;
        [Tooltip("Widen framing once warning rings begin.")]
        [SerializeField] private float warningPhaseZoomMultiplier = 1.2f;
        [Tooltip("Smooth blend time from early framing to warning-phase framing.")]
        [SerializeField] private float warningPhaseBlendTime = 0.65f;
        [Tooltip("Deprecated for strict fit path. Kept for inspector compatibility and ignored when it would under-fit players.")]
        [SerializeField] private float preWarningMaxFramingRadius = 10f;

        [Header("Framing Safety")]
        [Tooltip("Exclude obvious holding-spawn outliers (e.g. y=-1000) from camera composition.")]
        [SerializeField] private float invalidCandidateMinY = -200f;
        [Tooltip("Maximum allowed horizontal distance from arena center for camera candidates (very large to avoid dropping real players).")]
        [SerializeField] private float maxCandidateHorizontalDistance = 300f;
        [Tooltip("Viewport safety margin used by strict visibility fallback.")]
        [SerializeField] [Range(0.01f, 0.2f)] private float viewportSafeMargin = 0.06f;
        [SerializeField] [Range(1, 8)] private int viewportSafetyMaxIterations = 4;
        [SerializeField] private float viewportSafetyDistanceStep = 1.25f;

        [Header("Shake")]
        [SerializeField] private float shakeDuration = 0.25f;

        private readonly List<NeonSumoPlayer> _alivePlayersBuffer = new();

        private Vector3 _positionVelocity;
        private float _shakeTimer;
        private float _shakeStrength;
        private Vector3 _shakeOffset;
        private bool _hasWarnedFallback;
        private Vector3 _initialPosition;
        private Quaternion _initialRotation;
        private Camera _camera;
        private bool _wasFramingGameplay;
        private bool _hadSmoothedTarget;
        private Vector3 _smoothedCenter;
        private float _smoothedRadius;
        private Vector3 _centerSmoothVelocity;
        private float _radiusSmoothVelocity;
        private Vector3 _smoothedAvgVelocity;
        private Vector3 _avgVelocitySmoothVelocity;
        private bool _wasDynamicFramingReady;
        private float _timeDynamicFramingBecameReady = -1f;
        private bool _warningPhaseActive;
        private float _warningPhaseBlend;
        private float _warningPhaseBlendVelocity;
        private float _phaseOrbitElapsed;
        private float _phaseOrbitYawOffset;
        private Vector3 _smoothedLookAheadOffset;
        private Vector3 _lookAheadSmoothVelocity;

        private struct FrameTarget
        {
            public Vector3 Center;
            public float Radius;
            public Vector3 AverageVelocity;
            public int PlayerCount;
        }

        private void Awake()
        {
            ResolveDependencies();
            TryApplyFieldOfView();
            CacheInitialTransform();
        }

        private void OnEnable()
        {
            ResolveDependencies();
        }

        private void LateUpdate()
        {
            if (gameManager == null)
            {
                return;
            }

            if (cameraMode == ArenaCameraMode.FixedArena)
            {
                LateUpdateFixedArena();
                return;
            }

            if (!ShouldUpdateDynamicSpectator())
            {
                ClearDynamicSpectatorState();
                return;
            }

            bool framingReady = gameManager.IsArenaCameraDynamicFramingReady();
            if (framingReady && !_wasDynamicFramingReady)
            {
                _timeDynamicFramingBecameReady = Time.unscaledTime;
            }

            _wasDynamicFramingReady = framingReady;

            FrameTarget rawTarget = BuildFrameTarget();
            ApplySmoothedTarget(rawTarget, Time.deltaTime, resetOnFirstFrame: !_wasFramingGameplay);
            FrameTarget smoothTarget = BuildSmoothedFrameTarget(rawTarget);
            FrameTarget phaseTarget = ApplyCameraPhase(smoothTarget, Time.deltaTime);

            Vector3 desiredPosition = ComputeDesiredPosition(phaseTarget);
            Vector3 shakeOffset = UpdateShakeOffset();

            float smoothTime = positionSmoothTime;
            if (postUnlockEaseDuration > 0f && _timeDynamicFramingBecameReady >= 0f &&
                Time.unscaledTime - _timeDynamicFramingBecameReady < postUnlockEaseDuration)
            {
                smoothTime = postUnlockPositionSmoothTime;
            }

            // Prevent disorienting intro->gameplay teleports: first dynamic frame always blends from current position.
            if (!_wasFramingGameplay && snapWhenGameplayStarts && _timeDynamicFramingBecameReady < 0f)
            {
                _positionVelocity = Vector3.zero;
                transform.position = desiredPosition + shakeOffset;
                ApplySmoothedRotation(phaseTarget, desiredPosition + shakeOffset, Time.deltaTime, instant: true);
                _wasFramingGameplay = true;
                return;
            }

            _wasFramingGameplay = true;

            Vector3 nextPos = Vector3.SmoothDamp(
                transform.position,
                desiredPosition + shakeOffset,
                ref _positionVelocity,
                Mathf.Max(0.01f, smoothTime));

            if (maxPositionSpeed > 0f)
            {
                Vector3 step = nextPos - transform.position;
                float maxStep = maxPositionSpeed * Time.deltaTime;
                if (step.sqrMagnitude > maxStep * maxStep)
                {
                    nextPos = transform.position + step.normalized * maxStep;
                }
            }

            transform.position = nextPos;
            ApplySmoothedRotation(phaseTarget, transform.position, Time.deltaTime, instant: false);
        }

        private void LateUpdateFixedArena()
        {
            ClearDynamicSpectatorState();

            if (gameManager.CurrentState != NeonSumoGameState.Playing)
            {
                _shakeTimer = 0f;
                _shakeStrength = 0f;
                _shakeOffset = Vector3.zero;
                transform.position = _initialPosition;
                transform.rotation = _initialRotation;
                return;
            }

            if (gameManager.IsGameActive)
            {
                transform.position = _initialPosition + UpdateShakeOffset();
                transform.rotation = _initialRotation;
            }
            else
            {
                _shakeTimer = 0f;
                _shakeStrength = 0f;
                _shakeOffset = Vector3.zero;
                transform.position = _initialPosition;
                transform.rotation = _initialRotation;
            }
        }

        private void ClearDynamicSpectatorState()
        {
            _wasFramingGameplay = false;
            _wasDynamicFramingReady = false;
            _timeDynamicFramingBecameReady = -1f;
            _warningPhaseActive = false;
            _warningPhaseBlend = 0f;
            _warningPhaseBlendVelocity = 0f;
            _phaseOrbitElapsed = 0f;
            _phaseOrbitYawOffset = 0f;
            _smoothedLookAheadOffset = Vector3.zero;
            _lookAheadSmoothVelocity = Vector3.zero;
        }

        private void ApplySmoothedTarget(FrameTarget raw, float dt, bool resetOnFirstFrame)
        {
            if (resetOnFirstFrame || !_hadSmoothedTarget)
            {
                _smoothedCenter = raw.Center;
                _smoothedRadius = raw.Radius;
                _smoothedAvgVelocity = raw.AverageVelocity;
                _centerSmoothVelocity = Vector3.zero;
                _radiusSmoothVelocity = 0f;
                _avgVelocitySmoothVelocity = Vector3.zero;
                _hadSmoothedTarget = true;
                return;
            }

            _smoothedCenter = Vector3.SmoothDamp(
                _smoothedCenter,
                raw.Center,
                ref _centerSmoothVelocity,
                Mathf.Max(0.01f, targetCenterSmoothTime),
                Mathf.Infinity,
                dt);

            _smoothedRadius = Mathf.SmoothDamp(
                _smoothedRadius,
                raw.Radius,
                ref _radiusSmoothVelocity,
                Mathf.Max(0.01f, raw.Radius > _smoothedRadius ? targetRadiusExpandSmoothTime : targetRadiusContractSmoothTime),
                Mathf.Infinity,
                dt);

            _smoothedAvgVelocity = Vector3.SmoothDamp(
                _smoothedAvgVelocity,
                raw.AverageVelocity,
                ref _avgVelocitySmoothVelocity,
                Mathf.Max(0.01f, targetVelocitySmoothTime),
                Mathf.Infinity,
                dt);
        }

        private FrameTarget BuildSmoothedFrameTarget(FrameTarget raw)
        {
            return new FrameTarget
            {
                Center = _smoothedCenter,
                Radius = _smoothedRadius,
                AverageVelocity = _smoothedAvgVelocity,
                PlayerCount = raw.PlayerCount
            };
        }

        private FrameTarget ApplyCameraPhase(FrameTarget target, float dt)
        {
            float blendTarget = _warningPhaseActive ? 1f : 0f;
            _warningPhaseBlend = Mathf.SmoothDamp(
                _warningPhaseBlend,
                blendTarget,
                ref _warningPhaseBlendVelocity,
                Mathf.Max(0.01f, warningPhaseBlendTime),
                Mathf.Infinity,
                dt);

            if (!_warningPhaseActive)
            {
                _phaseOrbitElapsed = Mathf.Min(_phaseOrbitElapsed + dt, Mathf.Max(0f, introOrbitDuration));
            }

            float maxOrbitDegrees = Mathf.Min(180f, introOrbitDegreesPerSecond * Mathf.Max(0f, introOrbitDuration));
            float orbitProgress = introOrbitDuration > 0.01f
                ? Mathf.Clamp01(_phaseOrbitElapsed / introOrbitDuration)
                : 1f;
            float clockwiseOrbitDegrees = orbitProgress * maxOrbitDegrees;
            _phaseOrbitYawOffset = Mathf.Lerp(-clockwiseOrbitDegrees, 0f, _warningPhaseBlend);

            float phaseZoomMultiplier = Mathf.Lerp(earlyPhaseZoomMultiplier, warningPhaseZoomMultiplier, _warningPhaseBlend);
            FrameTarget adjusted = target;

            // Strict fit path: never shrink the fit radius below true player spread.
            float strictFitMultiplier = Mathf.Max(1f, phaseZoomMultiplier);
            adjusted.Radius = Mathf.Max(0f, adjusted.Radius * strictFitMultiplier);
            adjusted.Center = ApplySmoothedLookAhead(adjusted.Center, adjusted.AverageVelocity, adjusted.Radius, dt);
            adjusted.Radius += _smoothedLookAheadOffset.magnitude * Mathf.Max(0f, lookAheadRadiusPaddingFactor);
            return adjusted;
        }

        private Vector3 ApplySmoothedLookAhead(Vector3 center, Vector3 averageVelocity, float currentRadius, float dt)
        {
            float suppression = lookAheadSuppressionRadius > 0f
                ? 1f - Mathf.Clamp01(currentRadius / lookAheadSuppressionRadius)
                : 1f;
            float effectiveLookAhead = lookAheadFactor * suppression;

            Vector3 desiredLookAhead = averageVelocity * effectiveLookAhead;
            if (maxLookAheadDistance > 0f)
            {
                desiredLookAhead = Vector3.ClampMagnitude(desiredLookAhead, maxLookAheadDistance);
            }

            _smoothedLookAheadOffset = Vector3.SmoothDamp(
                _smoothedLookAheadOffset,
                desiredLookAhead,
                ref _lookAheadSmoothVelocity,
                Mathf.Max(0.01f, lookAheadSmoothTime),
                Mathf.Infinity,
                dt);

            return center + _smoothedLookAheadOffset;
        }

        private void ApplySmoothedRotation(FrameTarget target, Vector3 cameraPosition, float dt, bool instant)
        {
            Vector3 lookPoint = target.Center + Vector3.up * lookAtHeightOffset;
            Vector3 toTarget = lookPoint - cameraPosition;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            if (instant || maxRotationDegreesPerSecond <= 0f)
            {
                transform.rotation = targetRot;
                return;
            }

            float maxStep = maxRotationDegreesPerSecond * dt;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, maxStep);
        }

        private bool ShouldUpdateDynamicSpectator()
        {
            return gameManager != null &&
                   gameManager.CurrentState == NeonSumoGameState.Playing &&
                   gameManager.IsArenaCameraDynamicFramingReady();
        }

        private void CacheInitialTransform()
        {
            _initialPosition = transform.position;
            _initialRotation = transform.rotation;
        }

        private void ResolveDependencies()
        {
            bool usedFallback = false;

            if (gameManager == null)
            {
                gameManager = FindFirstObjectByType<NeonSumoGameManager>();
                usedFallback = true;
            }

            if (arena == null)
            {
                arena = FindFirstObjectByType<NeonSumoArena>();
                usedFallback = true;
            }

            if (usedFallback && !_hasWarnedFallback)
            {
                _hasWarnedFallback = true;
                Debug.LogWarning("[NeonSumo] NeonSumoArenaCamera: Resolved references via FindFirstObjectByType. Assign GameManager and Arena in Inspector for reliability.");
            }
        }

        private void TryApplyFieldOfView()
        {
            if (!applyFieldOfView)
                return;

            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
                if (_camera == null)
                    _camera = GetComponentInChildren<Camera>();
            }

            if (_camera != null)
                _camera.fieldOfView = fieldOfView;
        }

        private FrameTarget BuildFrameTarget()
        {
            RebuildAlivePlayersBuffer();

            if (_alivePlayersBuffer.Count == 0)
            {
                return new FrameTarget
                {
                    Center = arena != null ? arena.transform.position : Vector3.zero,
                    Radius = 0f,
                    AverageVelocity = Vector3.zero,
                    PlayerCount = 0
                };
            }

            Vector3 centerSum = Vector3.zero;
            Vector3 velocitySum = Vector3.zero;
            int count = 0;

            foreach (var player in _alivePlayersBuffer)
            {
                if (player == null) continue;
                centerSum += player.transform.position;
                velocitySum += player.Velocity;
                count++;
            }

            Vector3 center = count > 0 ? centerSum / count : Vector3.zero;
            Vector3 avgVelocity = count > 0 ? velocitySum / count : Vector3.zero;

            float radius = 0f;
            foreach (var player in _alivePlayersBuffer)
            {
                if (player == null) continue;
                float dist = Vector3.Distance(center, player.transform.position);
                if (dist > radius)
                    radius = dist;
            }

            if (count == 1)
                radius = Mathf.Max(radius, minRadiusSinglePlayer);

            if (arena != null && clusterFramingPull > 0f && count > 0)
            {
                Vector3 arenaPos = arena.transform.position;
                Vector3 towardCluster = center - arenaPos;
                center += towardCluster * clusterFramingPull;
            }

            return new FrameTarget
            {
                Center = center,
                Radius = radius,
                AverageVelocity = avgVelocity,
                PlayerCount = count
            };
        }

        private void RebuildAlivePlayersBuffer()
        {
            _alivePlayersBuffer.Clear();

            foreach (var player in gameManager.GetAlivePlayers())
            {
                if (IsValidCameraCandidate(player))
                    _alivePlayersBuffer.Add(player);
            }
        }

        private bool IsValidCameraCandidate(NeonSumoPlayer player)
        {
            if (player == null || player.IsEliminated)
                return false;

            Vector3 pos = player.transform.position;
            if (!float.IsFinite(pos.x) || !float.IsFinite(pos.y) || !float.IsFinite(pos.z))
                return false;

            // Keep camera combat-focused: once a player leaves the playable arena volume,
            // stop including them in framing immediately (independent of elimination grace timing).
            if (arena != null && !arena.IsPositionOnArena(pos))
                return false;

            // Filters holding-spawn/off-world placeholders while keeping real active/falling players.
            if (pos.y < invalidCandidateMinY)
                return false;

            if (arena != null && maxCandidateHorizontalDistance > 0f)
            {
                Vector3 center = arena.transform.position;
                Vector2 horizontal = new Vector2(pos.x - center.x, pos.z - center.z);
                if (horizontal.magnitude > maxCandidateHorizontalDistance)
                    return false;
            }

            return true;
        }

        private Vector3 ComputeDesiredPosition(FrameTarget target)
        {
            float spreadDistance = baseDistance + target.Radius * zoomMultiplier;
            float boundsRadius = Mathf.Max(0f, target.Radius) + extraFramingRadius;
            float frustumDistance = GetMinCameraDistanceForBounds(boundsRadius);
            // Fit-first: never clamp below frustum-required distance.
            float desiredDistance = Mathf.Max(minDistance, Mathf.Max(spreadDistance, frustumDistance));
            if (maxDistance > 0f)
                desiredDistance = Mathf.Min(desiredDistance, Mathf.Max(minDistance, maxDistance));

            float speed = target.AverageVelocity.magnitude;
            float tilt = Mathf.Clamp(speed * tiltSpeedFactor, 0f, maxTiltDegrees);

            Quaternion baseRotation = Quaternion.Euler(pitchDegrees, yawDegrees + _phaseOrbitYawOffset, 0f);
            Quaternion tiltRotation = Quaternion.Euler(tilt, 0f, 0f);

            Vector3 offsetDirection = tiltRotation * (baseRotation * Vector3.back);
            Vector3 desiredPosition = target.Center + offsetDirection * desiredDistance;
            EnforceViewportSafety(target, ref desiredPosition);
            return desiredPosition;
        }

        private void EnforceViewportSafety(FrameTarget target, ref Vector3 desiredPosition)
        {
            if (_alivePlayersBuffer.Count == 0)
                return;

            EnsureCameraCached();
            if (_camera == null)
                return;

            Vector3 lookPoint = target.Center + Vector3.up * lookAtHeightOffset;
            Vector3 toTarget = lookPoint - desiredPosition;
            if (toTarget.sqrMagnitude < 0.0001f)
                return;

            Quaternion desiredRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            Vector3 viewDir = (desiredPosition - target.Center).normalized;
            float currentDistance = Vector3.Distance(desiredPosition, target.Center);
            float minStep = Mathf.Max(0.25f, viewportSafetyDistanceStep);
            int maxIterations = Mathf.Max(1, viewportSafetyMaxIterations);

            for (int i = 0; i < maxIterations; i++)
            {
                if (AreAllCandidatesInsideViewport(desiredPosition, desiredRotation))
                    return;

                currentDistance += minStep;
                desiredPosition = target.Center + viewDir * currentDistance;
                Vector3 updatedToTarget = lookPoint - desiredPosition;
                if (updatedToTarget.sqrMagnitude < 0.0001f)
                    break;
                desiredRotation = Quaternion.LookRotation(updatedToTarget.normalized, Vector3.up);
            }
        }

        private bool AreAllCandidatesInsideViewport(Vector3 cameraPosition, Quaternion cameraRotation)
        {
            Matrix4x4 view = Matrix4x4.TRS(cameraPosition, cameraRotation, Vector3.one).inverse;
            Matrix4x4 projection = _camera.projectionMatrix;
            float margin = Mathf.Clamp(viewportSafeMargin, 0.01f, 0.25f);

            foreach (var player in _alivePlayersBuffer)
            {
                if (player == null)
                    continue;

                // Check center and slight head offset for safer composition.
                if (!IsViewportSafe(player.transform.position, view, projection, margin))
                    return false;
                if (!IsViewportSafe(player.transform.position + Vector3.up * 0.9f, view, projection, margin))
                    return false;
            }

            return true;
        }

        private static bool IsViewportSafe(Vector3 worldPoint, Matrix4x4 view, Matrix4x4 projection, float margin)
        {
            Vector4 clip = projection * view * new Vector4(worldPoint.x, worldPoint.y, worldPoint.z, 1f);
            if (clip.w <= 0.0001f)
                return false;

            float invW = 1f / clip.w;
            float x = (clip.x * invW + 1f) * 0.5f;
            float y = (clip.y * invW + 1f) * 0.5f;
            return x >= margin && x <= 1f - margin && y >= margin && y <= 1f - margin;
        }

        /// <summary>
        /// Minimum orbit distance so a circle of radius <paramref name="boundsRadius"/> at the look target
        /// fits inside both horizontal and vertical frustum (perspective, current FOV and aspect).
        /// </summary>
        private float GetMinCameraDistanceForBounds(float boundsRadius)
        {
            EnsureCameraCached();
            if (_camera == null)
                return minDistance;

            float r = Mathf.Max(boundsRadius, minRadiusSinglePlayer);
            float vFovRad = _camera.fieldOfView * Mathf.Deg2Rad;
            float aspect = _camera.aspect > 0.01f ? _camera.aspect : (16f / 9f);
            float tanHalfV = Mathf.Tan(vFovRad * 0.5f);
            float tanHalfH = tanHalfV * aspect;
            float dMinH = r / Mathf.Max(0.001f, tanHalfH);
            float dMinV = r / Mathf.Max(0.001f, tanHalfV);
            return Mathf.Max(dMinH, dMinV) * frustumDistanceFudge;
        }

        private void EnsureCameraCached()
        {
            if (_camera != null)
                return;
            _camera = GetComponent<Camera>();
            if (_camera == null)
                _camera = GetComponentInChildren<Camera>();
        }

        private Vector3 UpdateShakeOffset()
        {
            if (_shakeTimer <= 0f)
            {
                _shakeOffset = Vector3.zero;
                return _shakeOffset;
            }

            _shakeOffset = new Vector3(
                (Random.value - 0.5f) * 2f * _shakeStrength,
                (Random.value - 0.5f) * 2f * _shakeStrength,
                (Random.value - 0.5f) * 2f * _shakeStrength);

            _shakeTimer -= Time.deltaTime;
            return _shakeOffset;
        }

        /// <summary>
        /// Trigger camera shake. Invoked from <see cref="NeonSumo.NeonSumoGameManager"/> (local human or ActionSync <c>camera_shake</c>).
        /// </summary>
        public void Shake(float strength)
        {
            _shakeTimer = shakeDuration;
            _shakeStrength = strength;
        }

        /// <summary>
        /// Reset camera to the same view as at the beginning of the game. Call right before Get Ready phase.
        /// </summary>
        public void ResetToInitialView()
        {
            transform.position = _initialPosition;
            transform.rotation = _initialRotation;
            _positionVelocity = Vector3.zero;
            _shakeTimer = 0f;
            _shakeStrength = 0f;
            _shakeOffset = Vector3.zero;
            _wasFramingGameplay = false;
            _hadSmoothedTarget = false;
            _centerSmoothVelocity = Vector3.zero;
            _radiusSmoothVelocity = 0f;
            _avgVelocitySmoothVelocity = Vector3.zero;
            _wasDynamicFramingReady = false;
            _timeDynamicFramingBecameReady = -1f;
            _warningPhaseActive = false;
            _warningPhaseBlend = 0f;
            _warningPhaseBlendVelocity = 0f;
            _phaseOrbitElapsed = 0f;
            _phaseOrbitYawOffset = 0f;
            _smoothedLookAheadOffset = Vector3.zero;
            _lookAheadSmoothVelocity = Vector3.zero;
        }

        /// <summary>
        /// Called when the first warning ring line starts drawing so the camera can prioritize arena awareness.
        /// </summary>
        public void NotifyWarningRingStarted(int ringIndex)
        {
            if (ringIndex < 0)
            {
                return;
            }

            _warningPhaseActive = true;
        }
    }
}
