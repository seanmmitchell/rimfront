using System;
namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; }
    public struct Bounds { public Vector3 size, center; }
    public static class Mathf { public static float Min(float a, float b) => Math.Min(a, b); public static float Max(float a, float b) => Math.Max(a, b); }
}

