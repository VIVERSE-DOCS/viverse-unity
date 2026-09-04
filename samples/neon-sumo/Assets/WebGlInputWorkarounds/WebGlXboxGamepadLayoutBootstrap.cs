#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace WebGLInputWorkarounds
{
    /// <summary>
    /// WebGL caps per-device input state size (~511 bits). Some Bluetooth Xbox controllers
    /// (e.g. Vendor 045e / Product 0b22) hit Unity's WebGL fallback path that builds a dynamic
    /// joystick layout whose synthetic controls exceed that limit.
    /// Forcing <c>WebGLGamepad</c> (standard HTML5 mapping state) avoids the broken dynamic layout and matches
    /// what the WebGL runtime writes; the generic <c>Gamepad</c>/<c>GamepadState</c> path can leave sticks/buttons dead.
    /// <para>
    /// <c>InputManager.TryFindMatchingControlLayout</c> only applies the <b>first</b> non-null layout
    /// returned from <c>onFindLayoutForDevice</c>. Unity registers its WebGL handler during
    /// <c>InitializeInPlayer</c>, so a handler registered at <c>BeforeSceneLoad</c> runs too late and
    /// never wins. We subscribe, then move this callback to the front of the internal callback list.
    /// </para>
    /// </summary>
    public static class WebGlXboxGamepadLayoutBootstrap
    {
#if ENABLE_INPUT_SYSTEM && UNITY_WEBGL && !UNITY_EDITOR
        static readonly InputDeviceFindControlLayoutDelegate s_OnFindLayout = OnFindLayoutForDevice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            _ = InputSystem.settings;
            InputSystem.onFindLayoutForDevice += s_OnFindLayout;
            TryMoveFindLayoutCallbackToFront(s_OnFindLayout);
        }

        const string ForcedLayoutName = "WebGLGamepad";

        static string OnFindLayoutForDevice(
            ref InputDeviceDescription description,
            string matchedLayout,
            InputDeviceExecuteCommandDelegate executeDeviceCommand)
        {
            if (!ShouldApplyXboxBluetoothWebGlLayoutWorkaround(ref description))
                return null;

            LogLayoutRouting(ref description, matchedLayout, ForcedLayoutName);
            return ForcedLayoutName;
        }

        static void LogLayoutRouting(ref InputDeviceDescription description, string matchedLayout, string returnedLayout)
        {
            var caps = description.capabilities ?? "";
            Debug.Log(
                "[WebGlXboxGamepadLayoutBootstrap] Routing Xbox Bluetooth WebGL gamepad: " +
                $"matchedLayout={matchedLayout ?? "null"}, " +
                $"returnedLayout={returnedLayout}, " +
                $"interfaceName={description.interfaceName ?? "null"}, " +
                $"manufacturer={description.manufacturer ?? "null"}, " +
                $"product={description.product ?? "null"}, " +
                $"capabilities={caps}");
        }

        static bool ShouldApplyXboxBluetoothWebGlLayoutWorkaround(ref InputDeviceDescription description)
        {
            var blob = ConcatDescription(ref description);
            if (string.IsNullOrEmpty(blob))
                return false;

            if (blob.IndexOf("0b22", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            var hasMicrosoftVendor =
                blob.IndexOf("045e", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("\"vendorId\":1118", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("\"vendorId\": 1118", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!hasMicrosoftVendor)
                return false;

            return blob.IndexOf("xbox", StringComparison.OrdinalIgnoreCase) >= 0
                   || blob.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string ConcatDescription(ref InputDeviceDescription description)
        {
            var m = description.manufacturer ?? "";
            var p = description.product ?? "";
            var c = description.capabilities ?? "";
            return string.Concat(m, " ", p, " ", c);
        }

        /// <summary>
        /// <c>CallbackArray</c> appends only; Unity's WebGL plugin registers first, so we reorder after subscribe.
        /// </summary>
        static void TryMoveFindLayoutCallbackToFront(InputDeviceFindControlLayoutDelegate target)
        {
            try
            {
                var inputSystemType = typeof(InputSystem);
                var sManagerField = inputSystemType.GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic);
                var manager = sManagerField?.GetValue(null);
                if (manager == null)
                {
                    _ = InputSystem.settings;
                    manager = sManagerField?.GetValue(null);
                }

                if (manager == null)
                    return;

                var callbacksField = manager.GetType().GetField(
                    "m_DeviceFindLayoutCallbacks",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (callbacksField == null)
                    return;

                var callbackArray = callbacksField.GetValue(manager);
                if (callbackArray == null)
                    return;

                var callbackArrayType = callbackArray.GetType();
                var lengthProp = callbackArrayType.GetProperty("length", BindingFlags.Public | BindingFlags.Instance);
                if (lengthProp == null)
                    return;

                var length = (int)lengthProp.GetValue(callbackArray)!;
                var itemProp = callbackArrayType.GetProperty(
                    "Item",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    typeof(InputDeviceFindControlLayoutDelegate),
                    new[] { typeof(int) },
                    null);
                if (itemProp == null)
                    return;

                var list = new List<InputDeviceFindControlLayoutDelegate>(length);
                var targetIndex = -1;
                for (var i = 0; i < length; i++)
                {
                    var dlg = (InputDeviceFindControlLayoutDelegate)itemProp.GetValue(callbackArray, new object[] { i })!;
                    list.Add(dlg);
                    if (ReferenceEquals(dlg, target))
                        targetIndex = i;
                }

                if (targetIndex <= 0)
                    return;

                list.RemoveAt(targetIndex);
                list.Insert(0, target);

                var clearMethod = callbackArrayType.GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
                var addMethod = callbackArrayType.GetMethod("AddCallback", BindingFlags.Public | BindingFlags.Instance);
                if (clearMethod == null || addMethod == null)
                    return;

                clearMethod.Invoke(callbackArray, null);
                foreach (var dlg in list)
                    addMethod.Invoke(callbackArray, new object[] { dlg! });

                callbacksField.SetValue(manager, callbackArray);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WebGlXboxGamepadLayoutBootstrap] Could not reorder onFindLayoutForDevice callbacks: {ex.Message}");
            }
        }
#endif
    }
}
