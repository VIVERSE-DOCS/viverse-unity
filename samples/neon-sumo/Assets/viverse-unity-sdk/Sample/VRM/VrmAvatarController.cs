// This file requires UniVRM (com.vrmc.vrm + com.vrmc.gltf).
// The Viverse.SDK.Sample.VRM.asmdef declares defineConstraints:["VIVERSE_VRM_INSTALLED"],
// so this whole assembly is only compiled when UniVRM is present. The #if wrapper
// below is a belt-and-suspenders safeguard in case someone bypasses the asmdef.
#if VIVERSE_VRM_INSTALLED
using System;
using System.Threading.Tasks;
using UnityEngine;
using UniGLTF;
using UniVRM10;

/// <summary>
/// Handles VRM loading, WASD movement, and facial expressions.
/// Attach to any GameObject. Call LoadVrmFromBytes() after downloading VRM data.
/// This script owns the UniVRM10 dependency — ViverseTestRunner does NOT need it.
/// </summary>
public class VrmAvatarController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;

    /// <summary>
    /// Fired when VRM is successfully loaded and spawned.
    /// </summary>
    public event Action OnVrmLoaded;

    /// <summary>
    /// Fired when VRM loading fails.
    /// </summary>
    public event Action<string> OnVrmLoadFailed;

    /// <summary>
    /// Whether a VRM model is currently loaded.
    /// </summary>
    public bool IsLoaded => _vrmInstance != null;

    /// <summary>
    /// Status text for UI display.
    /// </summary>
    public string StatusText { get; private set; } = "";

    private Vrm10Instance _vrmInstance;
    private int _activeExpression = -1;

    private static readonly ExpressionKey[] Expressions = new[]
    {
        ExpressionKey.Happy,
        ExpressionKey.Angry,
        ExpressionKey.Sad,
        ExpressionKey.Surprised,
        ExpressionKey.Relaxed
    };

    void Update()
    {
        if (_vrmInstance == null) return;

        // WASD movement
        var move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move.z += 1f;
        if (Input.GetKey(KeyCode.S)) move.z -= 1f;
        if (Input.GetKey(KeyCode.A)) move.x -= 1f;
        if (Input.GetKey(KeyCode.D)) move.x += 1f;
        if (move != Vector3.zero)
        {
            move.Normalize();
            _vrmInstance.transform.position += move * moveSpeed * Time.deltaTime;
            _vrmInstance.transform.rotation = Quaternion.LookRotation(move);
        }

        // 1-5 expressions, 0 to reset
        int newExpr = -1;
        if (Input.GetKeyDown(KeyCode.Alpha1)) newExpr = 0;
        if (Input.GetKeyDown(KeyCode.Alpha2)) newExpr = 1;
        if (Input.GetKeyDown(KeyCode.Alpha3)) newExpr = 2;
        if (Input.GetKeyDown(KeyCode.Alpha4)) newExpr = 3;
        if (Input.GetKeyDown(KeyCode.Alpha5)) newExpr = 4;
        if (Input.GetKeyDown(KeyCode.Alpha0)) newExpr = -2; // reset

        if (newExpr != -1)
        {
            var expr = _vrmInstance.Runtime.Expression;
            // Reset all
            foreach (var key in Expressions)
                expr.SetWeight(key, 0f);

            if (newExpr >= 0 && newExpr < Expressions.Length)
            {
                expr.SetWeight(Expressions[newExpr], 1f);
                _activeExpression = newExpr;
            }
            else
            {
                _activeExpression = -1;
            }
        }
    }

    /// <summary>
    /// Load a VRM model from raw bytes. Destroys any previously loaded model.
    /// </summary>
    public async Task<bool> LoadVrmFromBytes(byte[] vrmBytes)
    {
        if (vrmBytes == null || vrmBytes.Length == 0)
        {
            StatusText = "No VRM bytes provided";
            OnVrmLoadFailed?.Invoke(StatusText);
            return false;
        }

        try
        {
            if (_vrmInstance != null)
            {
                Destroy(_vrmInstance.gameObject);
                _vrmInstance = null;
            }

            Debug.Log($"[VrmAvatarController] Loading VRM ({vrmBytes.Length} bytes)...");
            Debug.Log($"[VrmAvatarController] First 4 bytes: {vrmBytes[0]:X2} {vrmBytes[1]:X2} {vrmBytes[2]:X2} {vrmBytes[3]:X2}");

            // glTF/VRM magic: first 4 bytes should be "glTF" (0x67 0x6C 0x54 0x46)
            bool isGltf = vrmBytes.Length >= 4 &&
                          vrmBytes[0] == 0x67 && vrmBytes[1] == 0x6C &&
                          vrmBytes[2] == 0x54 && vrmBytes[3] == 0x46;
            Debug.Log($"[VrmAvatarController] Valid glTF header: {isGltf}");

            if (!isGltf)
            {
                StatusText = "Invalid VRM: not a glTF file (possibly encrypted)";
                Debug.LogError($"[VrmAvatarController] {StatusText}");
                OnVrmLoadFailed?.Invoke(StatusText);
                return false;
            }

            Debug.Log("[VrmAvatarController] Calling Vrm10.LoadBytesAsync...");

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL has no threads — must use NoThread variant or LoadBytesAsync deadlocks.
            // See: UniVRM VRM10ViewerController.cs GetIAwaitCaller()
            var awaitCaller = new RuntimeOnlyNoThreadAwaitCaller();
#else
            IAwaitCaller awaitCaller = new RuntimeOnlyAwaitCaller();
#endif

            _vrmInstance = await Vrm10.LoadBytesAsync(
                vrmBytes,
                canLoadVrm0X: true,
                showMeshes: true,
                awaitCaller: awaitCaller
            );
            Debug.Log($"[VrmAvatarController] LoadBytesAsync returned: {(_vrmInstance != null ? "instance" : "null")}");

            if (_vrmInstance == null)
            {
                StatusText = "VRM load returned null";
                OnVrmLoadFailed?.Invoke(StatusText);
                return false;
            }

            _vrmInstance.transform.position = Vector3.zero;
            _vrmInstance.transform.rotation = Quaternion.identity;
            _vrmInstance.gameObject.name = "ViverseAvatar";
            _activeExpression = -1;

            // Post-load diagnostics
            var renderers = _vrmInstance.GetComponentsInChildren<Renderer>();
            Debug.Log($"[VrmAvatarController] VRM spawned: renderers={renderers.Length}, active={_vrmInstance.gameObject.activeInHierarchy}");
            foreach (var r in renderers)
            {
                Debug.Log($"[VrmAvatarController]   Renderer: {r.name}, enabled={r.enabled}, material={r.sharedMaterial?.name ?? "NULL"}, shader={r.sharedMaterial?.shader?.name ?? "NULL"}");
            }

            StatusText = "VRM loaded! WASD=move, 1-5=expressions, 0=reset";
            Debug.Log($"[VrmAvatarController] VRM spawned. Controls: WASD=move, 1=Happy 2=Angry 3=Sad 4=Surprised 5=Relaxed 0=Reset");
            OnVrmLoaded?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"VRM load failed: {ex.Message}";
            Debug.LogError($"[VrmAvatarController] Exception: {ex}");
            OnVrmLoadFailed?.Invoke(StatusText);
            return false;
        }
    }

    /// <summary>
    /// Destroy the currently loaded VRM model.
    /// </summary>
    public void DestroyVrm()
    {
        if (_vrmInstance != null)
        {
            Destroy(_vrmInstance.gameObject);
            _vrmInstance = null;
            _activeExpression = -1;
            StatusText = "";
        }
    }

    void OnDestroy()
    {
        DestroyVrm();
    }
}
#endif

