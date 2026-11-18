using System;
using HarmonyLib;
using RavenIron.Tankard.Core;
using UnityEngine;

namespace RavenIron.Tankard.Patches
{
    /// <summary>
    /// The camera leans, bobs, drifts off your aim and breathes in and out.
    ///
    /// GameCamera.UpdateCamera rebuilds the camera's rotation from the player's eye every frame (decompiled from 1.0.x:
    /// `base.transform.rotation = rot` where rot is `m_eye.transform.rotation`, and it resets `fieldOfView = m_fov` first
    /// thing). So an offset applied in a postfix is redrawn from scratch next frame and can never accumulate, and the
    /// moment you sober up the camera is exactly vanilla again. Vanilla's own camera shake works the same way, multiplying
    /// onto the rotation at the end of the same method.
    ///
    /// A decorating postfix at default priority (amended house rule 1): it adds to what survives and replaces nothing.
    /// Purely cosmetic, so it lives in its own try/catch (house rule 3) and a failure only costs the wobble.
    /// </summary>
    [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
    internal static class Patch_CameraSway
    {
        private static GameCamera _cachedFor;
        private static Camera _camera;

        [HarmonyPostfix]
        private static void Postfix(GameCamera __instance)
        {
            try
            {
                if (!Plugin.Enabled.Value || !Plugin.CameraSway.Value) return;
                Player player = Player.m_localPlayer;
                if (player == null || player.IsDead() || player.InCutscene()) return;

                float severity = Meter.SymptomSeverity;
                if (severity <= 0f) return;

                CameraSway sway = Symptoms.Camera(Time.time, severity);
                __instance.transform.rotation = __instance.transform.rotation * Quaternion.Euler(sway.Pitch, sway.Yaw, sway.Roll);

                // GameCamera.m_camera is private; it is GetComponent<Camera>() on the same object (decompiled Awake).
                if (_cachedFor != __instance || _camera == null)
                {
                    _cachedFor = __instance;
                    _camera = __instance.GetComponent<Camera>();
                }
                if (_camera != null) _camera.fieldOfView += sway.Fov;
                if (__instance.m_skyCamera != null) __instance.m_skyCamera.fieldOfView += sway.Fov; // keep the sky in step
            }
            catch (Exception ex)
            {
                Plugin.Report("camera sway", ex);
            }
        }
    }

    /// <summary>
    /// Your feet go where they like. Player.SetControls takes the movement stick in LOCAL space (x right, z forward;
    /// decompiled PlayerController.FixedUpdate builds it from the WASD and stick input), so drift is one added sideways
    /// term. Only while you are actually moving: standing still never slides you anywhere.
    ///
    /// A behaviour prefix, so Priority.Low, it only rewrites the movement argument, and it always returns true.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class Patch_Wobble
    {
        [HarmonyPriority(Priority.Low)]
        [HarmonyPrefix]
        private static bool Prefix(Player __instance, ref Vector3 movedir)
        {
            try
            {
                if (!Plugin.Enabled.Value || !Plugin.Wobble.Value) return true;
                if (__instance != Player.m_localPlayer) return true;
                if (movedir.sqrMagnitude < 0.01f) return true;

                float severity = Meter.SymptomSeverity;
                if (severity <= 0f) return true;

                float magnitude = Mathf.Min(1f, movedir.magnitude);
                movedir.x += Symptoms.MoveDrift(Time.time, severity) * magnitude;
                if (movedir.sqrMagnitude > 1f) movedir.Normalize(); // a drunk is not FASTER, only less straight
            }
            catch (Exception ex)
            {
                Plugin.Report("wobble", ex);
            }
            return true;
        }
    }
}
