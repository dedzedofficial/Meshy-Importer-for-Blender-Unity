// Just enough of UnityEngine for MeshyUvRepair.cs to run outside Unity.
using System;
namespace UnityEngine {
  public struct Vector2 { public float x, y; public Vector2(float x, float y){this.x=x;this.y=y;} }
  public struct Vector3 { public float x, y, z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    public static Vector3 operator -(Vector3 a, Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    public static Vector3 operator +(Vector3 a, Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
    public float sqrMagnitude => x*x+y*y+z*z;
    public static Vector3 Cross(Vector3 a, Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
    public static Vector3 Min(Vector3 a, Vector3 b)=>new Vector3(Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Min(a.z,b.z));
    public static Vector3 Max(Vector3 a, Vector3 b)=>new Vector3(Math.Max(a.x,b.x),Math.Max(a.y,b.y),Math.Max(a.z,b.z)); }
  public static class Mathf { public static float Abs(float v)=>Math.Abs(v); public static float Max(float a,float b)=>Math.Max(a,b); }
}
