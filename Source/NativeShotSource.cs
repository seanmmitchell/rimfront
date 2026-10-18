using System;
using UnityEngine;
using Verse;

namespace RimFront
{
    internal static class NativeShotSource
    {
        // Never move a shot beyond a near crosshair contact, outside the finite
        // viewmodel envelope, or through unchecked geometry. Fall back to the eye.
        internal static Vector3 Resolve(Vector3 eye, Vector3 muzzle, Vector3 target, float clearDistance)
        {
            if (!NativeEffectMath.IsFinite(eye) || !NativeEffectMath.IsFinite(muzzle)
                || !NativeEffectMath.IsFinite(target) || float.IsNaN(clearDistance) || float.IsInfinity(clearDistance)) return eye;
            Vector3 offset = muzzle - eye;
            float length = offset.magnitude;
            if (length < .001f || length > 2f || (target - eye).magnitude <= length + .02f) return eye;
            float fraction = Mathf.Clamp01((clearDistance - .01f) / length);
            return eye + offset * fraction;
        }
        internal static Vector3 Sample(Pawn pawn, Vector3 target)
        {
            Vector3 eye = NativeView.RenderPosition;
            if (!NativeWeapons.TryGetMuzzle(pawn, out Vector3 muzzle, out _)) return eye;
            Vector3 delta = muzzle - eye;
            float horizontal = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
            if (horizontal < .001f || delta.magnitude > 2f) return eye;
            float yaw = Mathf.Atan2(delta.x, delta.z), pitch = -Mathf.Atan2(delta.y, horizontal);
            if (!NativeAimTargeting.TrySelectFrom(pawn, eye, yaw, pitch, horizontal, false, out var clearance)) return eye;
            return Resolve(eye, muzzle, target, (clearance.Point - eye).magnitude);
        }
    }
}
