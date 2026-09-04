using UnityEngine;
using UnityEngine.InputSystem;
using ViverseSDK;
using System.Collections;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NeonSumo
{
    [RequireComponent(typeof(Rigidbody))]
    public class NeonSumoPlayer : MonoBehaviour
    {
        [Header("Config")]
        public NeonSumoMovementConfig movementConfig = new NeonSumoMovementConfig();
        public NeonSumoPhysicsConfig physicsConfig = new NeonSumoPhysicsConfig();
        public NeonSumoRampConfig rampConfig = new NeonSumoRampConfig();
        public NeonSumoCollisionConfig collisionConfig = new NeonSumoCollisionConfig();
        public NeonSumoHitPauseConfig hitPauseConfig = new NeonSumoHitPauseConfig();
        public NeonSumoEdgeConfig edgeConfig = new NeonSumoEdgeConfig();

        [Header("Visual")]
        public Renderer bodyRenderer;

        [Header("Input")]
        [Tooltip("Input Actions asset: Player/Move")]
        public InputActionReference moveAction;
        [Tooltip("Input Actions asset: Player/Jump (used for boost)")]
        public InputActionReference boostAction;

        private string _userId;
        private bool _isLocal;
        private NeonSumoControlKind _controlKind = NeonSumoControlKind.HumanRemote;
        private Rigidbody _rb;
        private NeonSumoGameManager _gameManager;
        private NeonSumoArena _arena;
        private bool _hasAssignedColor = false;
        private Color _assignedColor = Color.white;
        private Color _currentBodyColor = Color.white;
        private Material[] _bodyMaterials;
        private Material _eliminatedBodyMaterial;
        private int _lastBodyMaterialIndex;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock _colorBlock;

        // State
        private bool _isEliminated = false;
        private bool _controlsEnabled = false;
        private float _lastBoostTime = 0f;
        private InputAction _moveInputAction;
        private InputAction _boostInputAction;
        private bool _boostQueued = false;

        // Edge forgiveness
        private float fallGraceTime = 0.75f;
        private float fallTimer = 0f;

        /// <summary>After elimination, host keeps the ball dynamic this long so it can keep falling before we freeze it.</summary>
        private const float PostEliminationPhysicsDuration = 1f;
        private Coroutine _eliminationFreezeCoroutine;

        // Network interpolation (for remote players on clients)
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private Vector3 _targetVelocity;
        private bool _isInterpolating = false;
        private Coroutine _knockbackDampingRoutine;
        private bool _isInKnockback = false;
        private float _knockbackTimer = 0f;
        private bool _isHitPaused = false;
        private float _hitPauseTimer = 0f;
        private float _originalLinearDamping = 0f;
        private float _originalAngularDamping = 0f;
        private bool _hasStoredDamping = false;
        private bool _loggedFirstRemoteUpdate = false;
        private bool _loggedControlsEnabled = false;
        private float _lastCollisionSparksTime = -999f;

        private readonly List<Material> _sharedMatsScratch = new List<Material>();
        private readonly List<Renderer> _rendererScratch = new List<Renderer>();

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            EnsureBodyRenderer();
            ApplyRigidbodyTuning();
            ApplyPlayerPhysicsMaterial();
            CacheInputActions();
        }

        /// <summary>Resolve body renderer for imported meshes (e.g. Ball FBX) when not wired in the prefab.</summary>
        void EnsureBodyRenderer()
        {
            if (bodyRenderer != null)
                return;

            TryBindBodyRendererFromGameMaterials();
            if (bodyRenderer != null)
                return;

            bodyRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (bodyRenderer == null)
                bodyRenderer = GetComponentInChildren<MeshRenderer>(true);
        }

        /// <summary>
        /// Picks the renderer that uses one of the shared <see cref="_bodyMaterials"/> references (avoids Material.name).
        /// </summary>
        void TryBindBodyRendererFromGameMaterials()
        {
            if (_bodyMaterials == null || _bodyMaterials.Length == 0)
                return;

            _rendererScratch.Clear();
            GetComponentsInChildren(true, _rendererScratch);
            int rc = _rendererScratch.Count;
            for (int ri = 0; ri < rc; ri++)
            {
                Renderer r = _rendererScratch[ri];
                if (r == null)
                    continue;

                _sharedMatsScratch.Clear();
                r.GetSharedMaterials(_sharedMatsScratch);
                int matCount = _sharedMatsScratch.Count;
                for (int mi = 0; mi < matCount; mi++)
                {
                    Material m = _sharedMatsScratch[mi];
                    if (m == null)
                        continue;

                    for (int bi = 0; bi < _bodyMaterials.Length; bi++)
                    {
                        if (ReferenceEquals(m, _bodyMaterials[bi]))
                        {
                            bodyRenderer = r;
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>Sets shared body materials from the game manager. Order must match <see cref="PlayerColorManager"/> indices (Blue, Green, Red, Yellow).</summary>
        public void SetBodyMaterials(Material[] materials, Material eliminatedMaterial)
        {
            _bodyMaterials = materials != null && materials.Length > 0 ? materials : null;
            _eliminatedBodyMaterial = eliminatedMaterial;
            // Avoid GetComponentsInChildren scan when Awake/EnsureBodyRenderer already bound bodyRenderer.
            if (bodyRenderer == null)
                TryBindBodyRendererFromGameMaterials();
        }

        void FixedUpdate()
        {
            if (_isEliminated) return;

            // Host-authoritative: only host runs physics. Movement is driven by GameManager.HostFixedUpdateMovement().
            if (IsHostSimulating)
            {
                if (IsRollingIn && _rb != null && !_rb.isKinematic)
                {
                    ApplyRampRollAlongAcceleration();
                }

                if (_controlsEnabled)
                {
                    if (TickHitPause(Time.fixedDeltaTime)) return;
                    if (TickKnockback(Time.fixedDeltaTime)) return;

                    // Input is applied by GameManager.HostFixedUpdateMovement() - we only do post-movement here
                    // Cap horizontal speed so landing/settling (vertical v) does not steal maxSpeed budget. Knockback skips this block entirely via TickKnockback.
                    ClampHorizontalSpeedPreservingVertical(movementConfig.maxSpeed);

                    ApplyEdgeSlip();
                }

                TickFallElimination(Time.fixedDeltaTime);
            }
            else if (_isInterpolating)
            {
                TickRemoteInterpolation(Time.fixedDeltaTime);
            }
        }

        void ClampHorizontalSpeedPreservingVertical(float cap)
        {
            if (_rb == null || cap <= 0f)
            {
                return;
            }

            Vector3 v = _rb.linearVelocity;
            Vector3 horizontal = Vector3.ProjectOnPlane(v, Vector3.up);
            if (horizontal.sqrMagnitude <= cap * cap)
            {
                return;
            }

            horizontal = horizontal.normalized * cap;
            _rb.linearVelocity = new Vector3(horizontal.x, v.y, horizontal.z);
        }

        /// <returns>True if we should skip further movement this frame.</returns>
        bool TickHitPause(float dt)
        {
            if (!_isHitPaused) return false;
            _hitPauseTimer -= dt;
            if (_hitPauseTimer <= 0f) _isHitPaused = false;
            return true;
        }

        /// <returns>True if we should skip further movement this frame.</returns>
        bool TickKnockback(float dt)
        {
            if (!_isInKnockback) return false;
            _knockbackTimer -= dt;
            if (_knockbackTimer <= 0f) _isInKnockback = false;
            return true;
        }

        void TickFallElimination(float dt)
        {
            if (!_controlsEnabled || _arena == null) return;
            if (!_arena.IsPositionOnArena(transform.position))
            {
                fallTimer += dt;
                if (fallTimer >= fallGraceTime)
                    _gameManager?.EliminatePlayer(_userId, true);
            }
            else
            {
                fallTimer = 0f;
            }
        }

        void TickRemoteInterpolation(float dt)
        {
            transform.position = Vector3.Lerp(transform.position, _targetPosition, dt * 10f);
            transform.rotation = Quaternion.Lerp(transform.rotation, _targetRotation, dt * 10f);
        }

        void Update()
        {
            if (!_isLocal || !_controlsEnabled || _isEliminated)
            {
                return;
            }

            if (_boostInputAction == null)
            {
                CacheInputActions();
            }

            if (_boostInputAction != null && !_boostInputAction.enabled)
            {
                _boostInputAction.Enable();
            }

            if (_boostInputAction != null && _boostInputAction.triggered)
            {
                _boostQueued = true;
            }
            else if (_boostInputAction == null && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                _boostQueued = true;
            }

        }

        public void Initialize(string userId, bool isLocal, NeonSumoGameManager gameManager,
            MultiplayerClient multiplayerClient, NeonSumoArena arena, NeonSumoControlKind controlKind)
        {
            _userId = NeonSumoUserId.Normalize(userId);
            _isLocal = isLocal;
            _controlKind = controlKind;
            _gameManager = gameManager;
            _arena = arena;

            // Authority is set by GameManager.ApplyAuthorityToAllPlayers() using IsHost
            ApplyInitialColor();
        }

        private bool IsHostSimulating => _gameManager != null && _gameManager.IsHostClient;
        private bool CanSimulatePhysics => IsHostSimulating && !_isEliminated;

        /// <summary>
        /// Called by host to apply movement. Same logic as ApplyInput but invoked by GameManager.
        /// </summary>
        public void ApplyInputFromHost(Vector2 moveInput, bool boost)
        {
            if (_rb == null || _isEliminated) return;

            // Apply movement with Acceleration mode for better feel
            if (moveInput.sqrMagnitude > 0.01f)
            {
                Vector3 moveDirection = new Vector3(moveInput.x, 0f, moveInput.y).normalized;
                float forceScale = 1f;
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up);
                if (horizontalVelocity.sqrMagnitude > 0.01f)
                {
                    float alignment = Vector3.Dot(horizontalVelocity.normalized, moveDirection);
                    float alignment01 = Mathf.Clamp01((alignment + 1f) * 0.5f);
                    forceScale = Mathf.Lerp(movementConfig.turnResistanceMin, 1f, alignment01);
                }
                _rb.AddForce(moveDirection * movementConfig.moveSpeed * forceScale, ForceMode.Acceleration);
            }

            // Boost/Dash
            if (boost)
            {
                TryBoostFromHost();
            }
        }

        /// <summary>
        /// Host-authoritative physics: only host simulates. isHost=true means this machine runs physics for all players.
        /// </summary>
        public void ApplyAuthority(bool isHost)
        {
            if (_rb == null)
            {
                return;
            }

            if (_gameManager != null && _gameManager.CurrentState != NeonSumoGameState.Playing)
            {
                _isInterpolating = false;
                return;
            }

            _isInterpolating = !isHost;
            if (isHost)
            {
                _rb.isKinematic = false;
                _rb.WakeUp();
            }
            else
            {
                _rb.isKinematic = true;
            }
        }

        void TryBoostFromHost()
        {
            if (Time.time - _lastBoostTime < movementConfig.boostCooldown) return;

            Vector3 boostDirection = transform.forward;
            if (_rb.linearVelocity.magnitude > 0.1f)
            {
                boostDirection = _rb.linearVelocity.normalized;
            }

            _rb.AddForce(boostDirection * movementConfig.boostForce, ForceMode.Impulse);

            // Cap horizontal speed after boost; keep vertical unchanged (same policy as FixedUpdate cap)
            ClampHorizontalSpeedPreservingVertical(movementConfig.maxSpeed * 1.2f);

            _lastBoostTime = Time.time;
        }

        public void TriggerBoost()
        {
            // Called for remote players - visual only since they're kinematic
            if (!_isLocal && !_isEliminated)
            {
                // Remote players don't simulate physics, but we could add a visual effect here
                // For now, just log it - the position update will show the boost result
                DebugLogger.Log($"[NeonSumoPlayer] Remote player {_userId} boosted (visual effect only)");
            }
        }

        /// <summary>
        /// Apply typed remote state. Prefer this over UpdateRemotePosition when you have parsed data.
        /// </summary>
        public void ApplyRemoteState(PlayerStateSnapshot snapshot)
        {
            if (!_loggedFirstRemoteUpdate)
            {
                _loggedFirstRemoteUpdate = true;
                DebugLogger.Log("[NetSync] First remote update received for " + _userId + " at " + Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture) + "s");
            }
            _targetPosition = snapshot.Position;
            _targetRotation = snapshot.Rotation;
            _targetVelocity = snapshot.Velocity;
            _isInterpolating = true;
        }

        /// <summary>
        /// Parse network payload and apply. Kept for backward compatibility with existing message handlers.
        /// </summary>
        public void UpdateRemotePosition(object positionData)
        {
            try
            {
                var snapshot = ParsePositionDataToSnapshot(positionData);
                if (snapshot.HasValue)
                    ApplyRemoteState(snapshot.Value);
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogError($"[NeonSumoPlayer] Error updating remote position: {ex.Message}");
            }
        }

        static PlayerStateSnapshot? ParsePositionDataToSnapshot(object positionData)
        {
            if (positionData is JObject jObj)
            {
                float yRot = jObj["rotY"]?.Value<float>() ?? 0f;
                return new PlayerStateSnapshot
                {
                    Position = new Vector3(
                        jObj["x"]?.Value<float>() ?? 0f,
                        jObj["y"]?.Value<float>() ?? 0f,
                        jObj["z"]?.Value<float>() ?? 0f),
                    Rotation = TryParseRotationFromJObject(jObj, yRot),
                    Velocity = new Vector3(
                        jObj["vx"]?.Value<float>() ?? 0f,
                        jObj["vy"]?.Value<float>() ?? 0f,
                        jObj["vz"]?.Value<float>() ?? 0f)
                };
            }
            if (positionData is Dictionary<string, object> dict)
            {
                float yRot = dict.ContainsKey("rotY") ? Convert.ToSingle(dict["rotY"]) : 0f;
                return new PlayerStateSnapshot
                {
                    Position = new Vector3(
                        dict.ContainsKey("x") ? Convert.ToSingle(dict["x"]) : 0f,
                        dict.ContainsKey("y") ? Convert.ToSingle(dict["y"]) : 0f,
                        dict.ContainsKey("z") ? Convert.ToSingle(dict["z"]) : 0f),
                    Rotation = TryParseRotationFromDict(dict, yRot),
                    Velocity = new Vector3(
                        dict.ContainsKey("vx") ? Convert.ToSingle(dict["vx"]) : 0f,
                        dict.ContainsKey("vy") ? Convert.ToSingle(dict["vy"]) : 0f,
                        dict.ContainsKey("vz") ? Convert.ToSingle(dict["vz"]) : 0f)
                };
            }
            return null;
        }

        static Quaternion TryParseRotationFromJObject(JObject jObj, float rotYFallback)
        {
            if (jObj["qx"] != null && jObj["qy"] != null && jObj["qz"] != null && jObj["qw"] != null)
            {
                var q = new Quaternion(
                    jObj["qx"].Value<float>(),
                    jObj["qy"].Value<float>(),
                    jObj["qz"].Value<float>(),
                    jObj["qw"].Value<float>());
                if (Quaternion.Dot(q, q) > 0.0001f)
                    return q.normalized;
            }

            return Quaternion.Euler(0f, rotYFallback, 0f);
        }

        static Quaternion TryParseRotationFromDict(Dictionary<string, object> dict, float rotYFallback)
        {
            if (dict.TryGetValue("qx", out var qxO) &&
                dict.TryGetValue("qy", out var qyO) &&
                dict.TryGetValue("qz", out var qzO) &&
                dict.TryGetValue("qw", out var qwO))
            {
                var q = new Quaternion(
                    Convert.ToSingle(qxO),
                    Convert.ToSingle(qyO),
                    Convert.ToSingle(qzO),
                    Convert.ToSingle(qwO));
                if (Quaternion.Dot(q, q) > 0.0001f)
                    return q.normalized;
            }

            return Quaternion.Euler(0f, rotYFallback, 0f);
        }

        public void SetEliminated()
        {
            _isEliminated = true;
            _controlsEnabled = false;
            _boostQueued = false;
            DisableInputActions();

            ApplyEliminatedBodyVisual();

            CancelPendingEliminationFreeze();
            _eliminationFreezeCoroutine = StartCoroutine(DeferredKinematicFreezeAfterElimination());
        }

        void CancelPendingEliminationFreeze()
        {
            if (_eliminationFreezeCoroutine != null)
            {
                StopCoroutine(_eliminationFreezeCoroutine);
                _eliminationFreezeCoroutine = null;
            }
        }

        IEnumerator DeferredKinematicFreezeAfterElimination()
        {
            yield return new WaitForSeconds(PostEliminationPhysicsDuration);
            _eliminationFreezeCoroutine = null;
            ApplyEliminationKinematicFreeze();
        }

        /// <summary>Zero velocity and make kinematic — call after tumble delay or immediately if no delay.</summary>
        void ApplyEliminationKinematicFreeze()
        {
            if (_rb == null || !_isEliminated)
            {
                return;
            }

            // Stop physics (avoid Unity warnings: no velocity writes while kinematic)
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }

            _rb.isKinematic = true;
        }

        public void EnableControls()
        {
            _controlsEnabled = true;
            EnableInputActions();
            if (_isLocal && !_loggedControlsEnabled)
            {
                _loggedControlsEnabled = true;
                DebugLogger.Log("[NetSync] Controls enabled for " + _userId + " at " + Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture) + "s");
            }
            // No SyncPositionToNetwork — host-authoritative transforms go out on NetworkSync
        }

        public void DisableControls()
        {
            _controlsEnabled = false;
            _boostQueued = false;
            DisableInputActions();
        }

        public void SetVisible(bool visible)
        {
            _rendererScratch.Clear();
            GetComponentsInChildren(true, _rendererScratch);
            int n = _rendererScratch.Count;
            if (n == 0)
                return;
            for (int i = 0; i < n; i++)
            {
                Renderer r = _rendererScratch[i];
                if (r != null)
                    r.enabled = visible;
            }
        }

        public void FreezeForCountdown()
        {
            if (_rb == null) return;
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.isKinematic = true;
            _isInterpolating = false;
        }

        public void UnfreezeAfterCountdown()
        {
            BeginRampRoll();
        }

        public void BeginRampRoll()
        {
            if (_rb == null) return;
            IsRollingIn = true;
            // Host-authoritative: host = dynamic, clients = kinematic
            _rb.isKinematic = _gameManager == null || !_gameManager.IsHostClient;
            _rb.useGravity = true;
            _rb.WakeUp();
            StoreRigidbodyDamping();
            ApplyRampDamping();
            ApplyRampImpulse();
            DisableControls();
        }

        public void OnRampReached()
        {
            if (!IsRollingIn)
            {
                return;
            }

            IsRollingIn = false;
            RestoreRigidbodyDamping();
            EnableControls();
            DebugLogger.Log($"[Ramp] Trigger hit -> input enabled for {_userId}");
        }

        public void MoveToSpawn(Vector3 spawnPosition, Quaternion spawnRotation)
        {
            transform.position = spawnPosition;
            transform.rotation = spawnRotation;
            ResetMotion();
            fallTimer = 0f;
        }

        public void ResetMotion()
        {
            if (_rb == null)
            {
                return;
            }
            if (_rb.isKinematic)
            {
                return;
            }
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        public void Respawn(Vector3 spawnPosition)
        {
            CancelPendingEliminationFreeze();
            transform.position = spawnPosition;
            transform.rotation = Quaternion.identity;
            ResetMotion();
            _isEliminated = false;
            fallTimer = 0f; // Reset fall timer on respawn
            
            // Host-authoritative: host = dynamic, clients = kinematic
            if (_rb != null)
                _rb.isKinematic = _gameManager == null || !_gameManager.IsHostClient;
            _isInterpolating = _gameManager == null || !_gameManager.IsHostClient;
            
            ApplyInitialColor();

            // Respawn protection: prevent immediate re-elimination
            if (_isLocal)
            {
                StartCoroutine(RespawnImmunity());
            }
        }

        public void ResetForNewRound()
        {
            CancelPendingEliminationFreeze();
            _isEliminated = false;
            fallTimer = 0f;
            IsRollingIn = false;
            _boostQueued = false;
            _isHitPaused = false;
            _hitPauseTimer = 0f;
            _isInKnockback = false;
            _knockbackTimer = 0f;
            _isInterpolating = false;

            if (_knockbackDampingRoutine != null)
            {
                StopCoroutine(_knockbackDampingRoutine);
                _knockbackDampingRoutine = null;
            }

            if (_hasStoredDamping)
            {
                RestoreRigidbodyDamping();
            }

            DisableControls();

            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.linearDamping = physicsConfig.tunedLinearDamping;
                _rb.angularDamping = physicsConfig.tunedAngularDamping;
            }
        }

        IEnumerator RespawnImmunity()
        {
            // Preserve original kinematic state to avoid desync with remote players
            bool wasKinematic = _rb.isKinematic;
            _rb.isKinematic = true;
            yield return new WaitForSeconds(0.5f);
            _rb.isKinematic = wasKinematic;
        }

        // Collision detection for pushing (host-authoritative: only host runs physics)
        void OnCollisionEnter(Collision collision)
        {
            if (_gameManager == null || !_gameManager.IsHostClient)
            {
                return;
            }

            NeonSumoPlayer otherPlayer = collision.gameObject.GetComponent<NeonSumoPlayer>();
            if (otherPlayer != null && !_isEliminated && !otherPlayer.IsEliminated)
            {
                if (collision.contactCount > 0 && Time.time - _lastCollisionSparksTime >= 0.06f)
                {
                    var contact = collision.GetContact(0);
                    _gameManager.TrySpawnCollisionSparks(contact.point, contact.normal);
                    _lastCollisionSparksTime = Time.time;
                }
                ApplyCollisionBump(collision, otherPlayer);
            }
        }

        // Properties
        public bool IsEliminated => _isEliminated;
        public string UserId => _userId;
        public bool IsLocal => _isLocal;
        public NeonSumoControlKind ControlKind => _controlKind;
        public NeonSumoArena Arena => _arena;
        public bool ControlsEnabled => _controlsEnabled;
        /// <summary>Cached Rigidbody for host state broadcast. Avoids GetComponent in hot path.</summary>
        public Rigidbody CachedRigidbody => _rb;
        public bool IsRollingIn { get; private set; }
        /// <summary>Derived phase from current state flags. Use for clearer state reasoning.</summary>
        public NeonSumoPlayerPhase Phase =>
            _isEliminated ? NeonSumoPlayerPhase.Eliminated
            : _isHitPaused || _isInKnockback ? NeonSumoPlayerPhase.KnockbackLocked
            : IsRollingIn ? NeonSumoPlayerPhase.RollingIn
            : _controlsEnabled ? NeonSumoPlayerPhase.Active
            : NeonSumoPlayerPhase.Spawning;
        public Color CurrentBodyColor => _currentBodyColor;
        /// <summary>Velocity for camera look-ahead. Uses Rigidbody when host, interpolated target when client.</summary>
        public Vector3 Velocity => _rb != null && !_isInterpolating ? _rb.linearVelocity : _targetVelocity;

        public void SetBodyColor(Color color)
        {
            _assignedColor = color;
            _hasAssignedColor = true;
            _currentBodyColor = color;
            if (_bodyMaterials != null && _bodyMaterials.Length > 0)
                return;
            SetBodyColorInternal(color);
        }

        /// <summary>Applies plasma (or other) body material by <paramref name="colorIndex"/>; updates logical color for UI.</summary>
        public void ApplyBodyMaterial(int colorIndex, Color logicalColor)
        {
            EnsureBodyRenderer();
            _assignedColor = logicalColor;
            _hasAssignedColor = true;
            _currentBodyColor = logicalColor;
            if (_bodyMaterials == null || _bodyMaterials.Length == 0 || bodyRenderer == null)
            {
                SetBodyColorInternal(logicalColor);
                return;
            }

            int len = _bodyMaterials.Length;
            _lastBodyMaterialIndex = ((colorIndex % len) + len) % len;
            Material mat = _bodyMaterials[_lastBodyMaterialIndex];
            if (mat == null)
            {
                SetBodyColorInternal(logicalColor);
                return;
            }

            bodyRenderer.SetPropertyBlock(null);
            bodyRenderer.sharedMaterial = mat;
        }

        void ApplyEliminatedBodyVisual()
        {
            EnsureBodyRenderer();
            if (bodyRenderer == null)
                return;
            if (_eliminatedBodyMaterial != null)
            {
                bodyRenderer.SetPropertyBlock(null);
                bodyRenderer.sharedMaterial = _eliminatedBodyMaterial;
                return;
            }

            SetBodyColorInternal(Color.gray);
        }

        void SetBodyColorInternal(Color color)
        {
            if (bodyRenderer == null) return;
            _currentBodyColor = color;
            if (_colorBlock == null) _colorBlock = new MaterialPropertyBlock();
            bodyRenderer.GetPropertyBlock(_colorBlock);
            _colorBlock.SetColor(BaseColorId, color);
            _colorBlock.SetColor(ColorId, color);
            bodyRenderer.SetPropertyBlock(_colorBlock);
        }

        void ApplyInitialColor()
        {
            EnsureBodyRenderer();
            if (bodyRenderer == null) return;

            if (_bodyMaterials != null && _bodyMaterials.Length > 0)
            {
                if (_hasAssignedColor)
                    ApplyBodyMaterial(_lastBodyMaterialIndex, _assignedColor);
                return;
            }

            SetBodyColorInternal(_hasAssignedColor ? _assignedColor : Color.gray);
        }

        void CacheInputActions()
        {
            _moveInputAction = moveAction != null ? moveAction.action : null;
            _boostInputAction = boostAction != null ? boostAction.action : null;
        }

        void ApplyRigidbodyTuning()
        {
            _rb.constraints = RigidbodyConstraints.None;
            _rb.linearDamping = physicsConfig.tunedLinearDamping;
            _rb.angularDamping = physicsConfig.tunedAngularDamping;
            _rb.mass = physicsConfig.tunedMass;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        void StoreRigidbodyDamping()
        {
            if (_rb == null || _hasStoredDamping)
            {
                return;
            }

            _originalLinearDamping = _rb.linearDamping;
            _originalAngularDamping = _rb.angularDamping;
            _hasStoredDamping = true;
        }

        void ApplyRampDamping()
        {
            if (_rb == null)
            {
                return;
            }

            _rb.linearDamping = rampConfig.rampLinearDamping;
            _rb.angularDamping = rampConfig.rampAngularDamping;
        }

        void RestoreRigidbodyDamping()
        {
            if (_rb == null || !_hasStoredDamping)
            {
                return;
            }

            _rb.linearDamping = _originalLinearDamping;
            _rb.angularDamping = _originalAngularDamping;
            _hasStoredDamping = false;
        }

        Vector3 GetRampRollDirection()
        {
            return (transform.forward + Vector3.down).normalized;
        }

        void ApplyRampImpulse()
        {
            if (_rb == null || rampConfig.rampImpulse <= 0f)
            {
                return;
            }

            _rb.AddForce(GetRampRollDirection() * rampConfig.rampImpulse, ForceMode.Impulse);
        }

        void ApplyRampRollAlongAcceleration()
        {
            if (_rb == null || rampConfig.rampRollAcceleration <= 0f)
            {
                return;
            }

            _rb.AddForce(GetRampRollDirection() * rampConfig.rampRollAcceleration, ForceMode.Acceleration);
        }

        void ApplyPlayerPhysicsMaterial()
        {
            if (physicsConfig.playerPhysicsMaterial == null)
            {
                return;
            }

            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                collider.sharedMaterial = physicsConfig.playerPhysicsMaterial;
            }
        }

        /// <summary>
        /// Single-reporter rule for camera shake: only the player with lexicographically smaller <see cref="UserId"/>
        /// calls <see cref="NeonSumoGameManager.NotifyPlayerPlayerCollisionCameraShake"/>; skip if either id is null/empty.
        /// </summary>
        void ApplyCollisionBump(Collision collision, NeonSumoPlayer otherPlayer)
        {
            var otherRb = collision.rigidbody;
            if (otherRb == null)
            {
                return;
            }
            Vector3 relativeVelocity = _rb.linearVelocity;
            relativeVelocity -= otherRb.linearVelocity;

            Vector3 pushDir;
            if (relativeVelocity.sqrMagnitude > 0.01f)
            {
                pushDir = relativeVelocity.normalized;
            }
            else
            {
                pushDir = otherRb.position - transform.position;
                if (pushDir.sqrMagnitude < 0.0001f)
                {
                    pushDir = -collision.GetContact(0).normal;
                }
            }

            if (collision.contactCount > 0)
            {
                pushDir = -collision.GetContact(0).normal;
            }

            pushDir = Vector3.ProjectOnPlane(pushDir, Vector3.up).normalized;
            if (pushDir.sqrMagnitude < 0.0001f)
            {
                pushDir = transform.forward;
            }

            _rb.WakeUp();

            float relativeSpeed = Mathf.Min(relativeVelocity.magnitude, collisionConfig.maxRelativeSpeed);
            float speedFactor = Mathf.InverseLerp(collisionConfig.minSpeedForBonus, collisionConfig.maxSpeedForBonus, _rb.linearVelocity.magnitude);
            float speedBonus = collisionConfig.baseImpactForce + (speedFactor * collisionConfig.bonusImpactForce);
            float impulse = (relativeSpeed * collisionConfig.bumpMultiplier) + speedBonus;
            impulse = Mathf.Min(impulse, collisionConfig.maxBumpForce);


            // Local player gets the reactive shove.
            _rb.AddForce(-pushDir * impulse, ForceMode.Impulse);
            _rb.AddForce(Vector3.up * collisionConfig.verticalBumpForce, ForceMode.Impulse);

            if (_knockbackDampingRoutine != null)
            {
                StopCoroutine(_knockbackDampingRoutine);
            }

            _knockbackDampingRoutine = StartCoroutine(TemporarilyReduceDamping(_rb));

            _isInKnockback = true;
            _knockbackTimer = collisionConfig.knockbackLockoutDuration;

            if (otherPlayer != null &&
                !string.IsNullOrEmpty(_userId) && !string.IsNullOrEmpty(otherPlayer.UserId) &&
                string.Compare(_userId, otherPlayer.UserId, StringComparison.Ordinal) < 0)
            {
                _gameManager?.NotifyPlayerPlayerCollisionCameraShake(this, otherPlayer, impulse);
            }
        }

        public void TriggerHitPause()
        {
            if (_isHitPaused || hitPauseConfig.hitPauseDuration <= 0f)
            {
                return;
            }

            _isHitPaused = true;
            _hitPauseTimer = hitPauseConfig.hitPauseDuration;
        }

        public void ReceiveKnockback(Vector3 direction, float force, float verticalForce)
        {
            if ((_gameManager == null || !_gameManager.IsHostClient) || _isEliminated)
            {
                return;
            }

            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (direction.sqrMagnitude > 0.001f)
            {
                direction.Normalize();
            }
            else
            {
                direction = transform.forward;
            }

            _rb.WakeUp();
            _rb.AddForce(direction * force + Vector3.up * verticalForce, ForceMode.Impulse);

            if (_knockbackDampingRoutine != null)
            {
                StopCoroutine(_knockbackDampingRoutine);
            }

            _knockbackDampingRoutine = StartCoroutine(TemporarilyReduceDamping(_rb));

            _isInKnockback = true;
            _knockbackTimer = collisionConfig.knockbackLockoutDuration;
        }


        IEnumerator TemporarilyReduceDamping(Rigidbody target)
        {
            if (target == null)
            {
                yield break;
            }

            float originalDamping = target.linearDamping;
            target.linearDamping = collisionConfig.knockbackDamping;

            yield return new WaitForSeconds(collisionConfig.knockbackDampingDuration);

            target.linearDamping = originalDamping;
        }

        void ApplyEdgeSlip()
        {
            if (_gameManager == null || !_gameManager.IsHostClient || !_controlsEnabled || _arena == null || edgeConfig.edgeSlipForce <= 0f)
            {
                return;
            }

            float radius = _arena.CurrentRadius;
            float startRadius = Mathf.Max(0f, radius - edgeConfig.edgeSlipStartOffset);
            Vector3 toPlayer = Vector3.ProjectOnPlane(transform.position - _arena.transform.position, Vector3.up);
            float distance = toPlayer.magnitude;
            if (distance <= startRadius || distance <= 0.001f)
            {
                return;
            }

            float edgeFactor = Mathf.InverseLerp(startRadius, radius, distance);
            Vector3 outwardDir = toPlayer.normalized;
            _rb.AddForce(outwardDir * edgeConfig.edgeSlipForce * edgeFactor, ForceMode.Force);
        }

        Vector2 ReadKeyboardMoveInput()
        {
            if (Keyboard.current == null)
            {
                return Vector2.zero;
            }

            float x = 0f;
            float y = 0f;

            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
            {
                x -= 1f;
            }
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
            {
                x += 1f;
            }
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
            {
                y += 1f;
            }
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
            {
                y -= 1f;
            }

            return new Vector2(x, y);
        }

        void EnableInputActions()
        {
            if (!_isLocal)
            {
                return;
            }

            if (_moveInputAction == null || _boostInputAction == null)
            {
                CacheInputActions();
            }

            if (_moveInputAction == null || _boostInputAction == null)
            {
                DebugLogger.LogWarning("[NeonSumoPlayer] Input actions not assigned. Set Move and Boost actions on the player.");
                return;
            }

            if (_moveInputAction != null)
            {
                _moveInputAction.Enable();
            }

            if (_boostInputAction != null)
            {
                _boostInputAction.Enable();
            }
        }

        void DisableInputActions()
        {
            if (_moveInputAction != null)
            {
                _moveInputAction.Disable();
            }

            if (_boostInputAction != null)
            {
                _boostInputAction.Disable();
            }
        }

        /// <summary>
        /// Host calls this for the local player to get input. Consumes boost queue.
        /// </summary>
        public (Vector2 move, bool boost) GetInputForHost()
        {
            return CaptureInput();
        }

        (Vector2 move, bool boost) CaptureInput()
        {
            if (_moveInputAction == null)
            {
                CacheInputActions();
            }

            Vector2 move = Vector2.zero;
            if (_moveInputAction != null)
            {
                if (!_moveInputAction.enabled)
                {
                    _moveInputAction.Enable();
                }

                move = _moveInputAction.ReadValue<Vector2>();
            }

            if (move.sqrMagnitude < 0.01f)
            {
                move = ReadKeyboardMoveInput();
            }

            bool boost = _boostQueued;
            _boostQueued = false;

            return (move, boost);
        }

    }
}


