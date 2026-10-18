using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    /// <summary>Engine-native camera/input. No network, serialization, map scan, or browser work.</summary>
    internal static class NativeView
    {
        internal const float EyeHeight = 1.55f;
        static Pawn pawn;
        static Camera camera;
        static Vector3 oldPosition;
        static Quaternion oldRotation;
        static bool oldOrthographic, oldCursorVisible, oldRunInBackground;
        static float oldSize, oldFov, oldNear, oldFar;
        static Color oldBackground;
        static CameraClearFlags oldClearFlags;
        static CursorLockMode oldCursorLock;
        static readonly List<Camera> disabledSubcameras = new List<Camera>();
        static float yaw, pitch;
        static int consumeEscapeThroughFrame = -1;
        static bool requestRelease, waitForInitialMouseUp;
        static float nextHudRefresh;
        static float frameSeconds = 1f / 60f;
        static WeaponState weapon;
        static string healthText = "", nameText = "";

        internal static bool Active => pawn != null && camera != null;
        internal static float Yaw => yaw * Mathf.Deg2Rad;
        internal static float PitchDegrees => pitch;
        internal static Pawn ControlledPawn => pawn;
        internal static Vector3 RenderPosition => camera != null ? camera.transform.position : Vector3.zero;

        internal static void Start(Pawn controlled)
        {
            Stop();
            if (controlled == null || !controlled.Spawned || Find.Camera == null)
                throw new InvalidOperationException("A spawned pawn and map camera are required.");
            camera = Find.Camera;
            oldPosition = camera.transform.position;
            oldRotation = camera.transform.rotation;
            oldOrthographic = camera.orthographic;
            oldSize = camera.orthographicSize;
            oldFov = camera.fieldOfView;
            oldNear = camera.nearClipPlane;
            oldFar = camera.farClipPlane;
            oldBackground = camera.backgroundColor;
            oldClearFlags = camera.clearFlags;
            oldCursorLock = Cursor.lockState;
            oldCursorVisible = Cursor.visible;
            oldRunInBackground = Application.runInBackground;
            pawn = controlled;
            NativeShieldEffects.BeginViewSession();
            yaw = controlled.Rotation.AsAngle;
            pitch = 0f;
            requestRelease = false;
            waitForInitialMouseUp = true;
            nextHudRefresh = 0f;
            frameSeconds = 1f / 60f;
            weapon = null;
            NativeWindowIcon.Capture();
            try
            {
                // Ensure one update arrives after focus loss so we can release immediately,
                // even when the user's normal engine preference pauses background updates.
                Application.runInBackground = true;
                // Stock subcamera effects use orthographic coordinates; suspend them instead
                // of rendering them incorrectly over the perspective view.
                if (Current.SubcameraDriver != null)
                    foreach (SubcameraDef def in DefDatabase<SubcameraDef>.AllDefsListForReading)
                    {
                        Camera subcamera = Current.SubcameraDriver.GetSubcamera(def);
                        if (subcamera != null && subcamera.enabled)
                        { disabledSubcameras.Add(subcamera); subcamera.enabled = false; }
                    }
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                camera.orthographic = false;
                camera.fieldOfView = 75f;
                camera.nearClipPlane = 0.06f;
                // Match the scene's bounded pawn/aim visibility radius.
                camera.farClipPlane = 30f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.37f, 0.46f, 0.48f);
                ApplyPose();
            }
            catch { Stop(); throw; }
            finally { NativeWindowIcon.Restore(); }
        }

        internal static void Stop()
        {
            if (pawn == null && camera == null) return;
            // Clear activity before restoring so Harmony immediately returns camera/UI ownership.
            pawn = null;
            Camera saved = camera;
            camera = null;
            try
            {
                if (saved != null)
                {
                    saved.orthographic = oldOrthographic;
                    saved.orthographicSize = oldSize;
                    saved.fieldOfView = oldFov;
                    saved.nearClipPlane = oldNear;
                    saved.farClipPlane = oldFar;
                    saved.clearFlags = oldClearFlags;
                    saved.backgroundColor = oldBackground;
                    saved.transform.position = oldPosition;
                    saved.transform.rotation = oldRotation;
                }
            }
            finally
            {
                try
                {
                    foreach (Camera subcamera in disabledSubcameras)
                        if (subcamera != null) subcamera.enabled = true;
                }
                finally
                {
                    disabledSubcameras.Clear();
                    try
                    {
                        Application.runInBackground = oldRunInBackground;
                        Cursor.lockState = oldCursorLock;
                        Cursor.visible = oldCursorVisible;
                        requestRelease = false;
                        weapon = null;
                    }
                    finally { NativeWindowIcon.Restore(); }
                }
            }
        }

        internal static InputCommand Update()
        {
            if (!Active) return null;
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) frameSeconds = Mathf.Lerp(frameSeconds, dt, Mathf.Clamp01(dt * 3f));
            bool escape = requestRelease || Input.GetKeyDown(KeyCode.Escape);
            if (escape) consumeEscapeThroughFrame = Time.frameCount + 2;
            bool release = escape || !Application.isFocused
                || !pawn.Spawned || pawn.Dead || pawn.Downed || !pawn.Drafted || pawn.Map != Find.CurrentMap || ModalOpen();
            if (release) return new InputCommand { action = "release" };
            if (Input.GetKeyDown(KeyCode.Space)) Find.TickManager.TogglePaused();
#if RW17
            // Waylith's verified Input shim exposes raw pixel deltas rather than Unity's axes.
            const float sensitivity = 0.13f;
#else
            const float sensitivity = 2f;
#endif
            yaw = Mathf.Repeat(yaw + Input.GetAxisRaw("Mouse X") * sensitivity, 360f);
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * sensitivity, -65f, 70f);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            ApplyPose();
            if (!Input.GetMouseButton(0)) waitForInitialMouseUp = false;
            bool paused = Find.TickManager.Paused;
            return new InputCommand
            {
                action = "input", yaw = Yaw, pitch = pitch * Mathf.Deg2Rad,
                forward = paused ? 0 : (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f),
                strafe = paused ? 0 : (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                fire = !paused && !waitForInitialMouseUp && Input.GetMouseButton(0),
                reload = !paused && Input.GetKeyDown(KeyCode.R),
                cycleFireMode = !paused && Input.GetKeyDown(KeyCode.F),
                toggleBipod = !paused && Input.GetKeyDown(KeyCode.V)
            };
        }

        internal static bool FireHeld => Input.GetMouseButton(0);

        internal static void ApplyPose()
        {
            if (!Active || !pawn.Spawned) return;
            Vector3 position;
            if (AnalogMovement.Controls(pawn)) position = AnalogMovement.RenderPosition;
            else
            {
                // Native path progress without the colony renderer's slow position spring.
                position = pawn.pather.MovingNow
                    ? Vector3.Lerp(pawn.Position.ToVector3Shifted(), pawn.pather.nextCell.ToVector3Shifted(), pawn.pather.MovePercentage)
                    : pawn.Position.ToVector3Shifted();
            }
            position.y = EyeHeight;
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        internal static CellRect ViewRect()
        {
            if (pawn?.Map == null) return default(CellRect);
            CellRect rect = CellRect.CenteredOn(pawn.Position, 34);
            rect.ClipInsideMap(pawn.Map);
            return rect;
        }

        internal static bool ModalOpen()
        {
            if (Find.WindowStack != null)
                foreach (Window window in Find.WindowStack.Windows)
                    if (window.forcePause || window.absorbInputAroundWindow) return true;
            return false;
        }

        internal static void ConsumeExitEvent()
        {
            // Update sees Escape before OnGUI on Unity. Prevent that same key press from
            // opening RimWorld's main menu immediately after returning camera ownership.
            Event current = Event.current;
            if (Time.frameCount <= consumeEscapeThroughFrame && current.isKey && current.keyCode == KeyCode.Escape)
                current.Use();
        }

        internal static void OnGUI(string status)
        {
            if (!Active) return;
            Event current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape) requestRelease = true;
            if (current.type != EventType.Repaint) return;
            NativeTargetIndicators.OnGUI(pawn);
            if (Time.realtimeSinceStartup >= nextHudRefresh)
            {
                nextHudRefresh = Time.realtimeSinceStartup + 0.1f;
                weapon = CombatAdapter.Describe(pawn);
                string mounting = NativeBipods.Status(pawn);
                if (mounting != "") weapon.status += " / " + mounting;
                nameText = pawn.LabelShortCap.ToString();
                healthText = Mathf.RoundToInt(pawn.health.summaryHealth.SummaryHealthPercent * 100f) + "%";
            }
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                float width = UI.screenWidth, height = UI.screenHeight;
                Widgets.DrawBoxSolid(new Rect(16, 16, 710, 56), new Color(0.055f, 0.07f, 0.06f, 0.88f));
                GUI.color = new Color(0.85f, 0.91f, 0.79f);
                Widgets.Label(new Rect(28, 19, 510, 24), "RIMFRONT  /  " + nameText + "  /  " + healthText + "  /  " + Mathf.RoundToInt(1f / Math.Max(0.001f, frameSeconds)) + " FPS");
                GUI.color = Color.white;
                Widgets.Label(new Rect(28, 42, 690, 24), "WASD move · Mouse aim · LMB fire · R reload · F mode · Space pause · Esc exit");
                Widgets.DrawBoxSolid(new Rect(width - 340, 16, 324, 31), new Color(0.055f, 0.07f, 0.06f, 0.88f));
                Widgets.Label(new Rect(width - 328, 18, 304, 26), (AnalogMovement.Active ? "Analog" : "Native path") + " / " + NativeScene.DrawCalls + " draws / " + NativeScene.CachedChunks + " chunks");
                Widgets.DrawBoxSolid(new Rect(16, height - 86, 620, 70), new Color(0.055f, 0.07f, 0.06f, 0.88f));
                string ammo = weapon != null && weapon.ammo >= 0 ? "  " + weapon.ammo + " / " + weapon.capacity : "";
                Widgets.Label(new Rect(28, height - 82, 600, 26), (weapon?.label ?? "Unarmed") + ammo + "  /  " + (weapon?.fireMode ?? "Fixed") + "  —  " + (weapon?.status ?? ""));
                Widgets.Label(new Rect(28, height - 54, 600, 26), Find.TickManager.Paused ? "PAUSED — press Space to resume" : (status ?? ""));
                Color crosshair = new Color(0.9f, 0.95f, 0.85f, 0.85f);
                Widgets.DrawBoxSolid(new Rect(width / 2f - 7, height / 2f, 5, 1), crosshair);
                Widgets.DrawBoxSolid(new Rect(width / 2f + 3, height / 2f, 5, 1), crosshair);
                Widgets.DrawBoxSolid(new Rect(width / 2f, height / 2f - 7, 1, 5), crosshair);
                Widgets.DrawBoxSolid(new Rect(width / 2f, height / 2f + 3, 1, 5), crosshair);
            }
            finally { GUI.color = oldColor; Text.Font = oldFont; Text.Anchor = oldAnchor; }
        }
    }

    [HarmonyPatch(typeof(CameraDriver), "Update")]
    internal static class NativeCameraDriverUpdate
    {
        static bool Prefix()
        {
            if (!NativeView.Active) return true;
            NativeView.ApplyPose();
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.CameraDriverOnGUI))]
    internal static class NativeCameraDriverGui
    {
        static bool Prefix() => !NativeView.Active;
    }

    // Actual 1.7 implementation only culls/prepares/submits visual draws and silhouettes.
    // Simulation ticks, attack verbs, projectile flight and impact remain untouched.
    [HarmonyPatch(typeof(DynamicDrawManager), nameof(DynamicDrawManager.DrawDynamicThings))]
    internal static class NativeDynamicDrawOwnership
    {
        static bool Prefix() => !NativeView.Active;
    }

    [HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.CurrentViewRect), MethodType.Getter)]
    internal static class NativeCameraViewRect
    {
        static bool Prefix(ref CellRect __result)
        {
            if (!NativeView.Active) return true;
            __result = NativeView.ViewRect();
            return false;
        }
    }

    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    internal static class NativeInputOwnership
    {
        static bool Prefix()
        {
            NativeView.ConsumeExitEvent();
            if (!NativeView.Active) return true;
            if (NativeView.ModalOpen())
            {
                RimFrontGame.Active?.Release("Control released for a game dialog.");
                return true;
            }
            Text.StartOfOnGUI();
            // Preserve the normal component hook (including RimFront HUD) without executing
            // colony shortcuts, gizmos, selection, designators, or camera drag handlers.
            GameComponentUtility.GameComponentOnGUI();
            Event current = Event.current;
            if (current.isKey || current.isMouse || current.type == EventType.ScrollWheel) current.Use();
            return false;
        }
    }
}
