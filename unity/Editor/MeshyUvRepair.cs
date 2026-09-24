using System;
using System.Collections.Generic;
using UnityEngine;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>
    /// Smart UV repair. C# port of core/python/meshy_core/uv_repair.py (the reference
    /// implementation, which has the tests); the GDScript port lives in
    /// godot/addons/meshy_importer/meshy_uv_repair.gd. Keep all three in step.
    ///
    /// Rule: repair only what is broken, never touch valid UVs.
    ///  1. Bad vertices: NaN/infinite UVs; outliers outside [p5 - 2R, p95 + 2R]
    ///     (R = p95 - p5, per axis); vertices whose every triangle is collapsed
    ///     (UV area <= 1e-6 * longest UV edge^2 while the 3D triangle is not a sliver).
    ///  2. Nothing bad: unchanged. More than half bad, or no UVs: box projection.
    ///  3. Otherwise: ring fill from good triangle neighbours, then copy from a good
    ///     vertex at the same position (split seams) and ring fill again, then box
    ///     projection for anything left.
    /// All checks are affine-invariant, so running after the glTF V flip is fine.
    /// </summary>
    internal static class MeshyUvRepair
    {
        public const double RegenerateFraction = 0.5;
        private const double SliverEps = 1e-6;
        private const double WeldEps = 1e-5;

        public struct Stats
        {
            public int BadVertices;
            public int CollapsedTriangles;
            public int RepairedVertices;
            public int ProjectedVertices;
            public bool Regenerated;
        }

        /// <summary>Returns repaired UVs, or null when nothing needed changing.</summary>
        public static Vector2[] Repair(Vector3[] positions, Vector2[] uvs, int[] indices, Vector3[] normals, out Stats stats)
        {
            stats = new Stats();
            int n = positions.Length;
            if (n == 0) return null;
            if (uvs == null || uvs.Length != n)
            {
                stats.Regenerated = true;
                stats.BadVertices = n;
                stats.ProjectedVertices = n;
                return BoxProject(positions, indices, normals, null);
            }

            var bad = new bool[n];
            int finiteCount = 0;
            for (int i = 0; i < n; i++)
            {
                bad[i] = !(IsFinite(uvs[i].x) && IsFinite(uvs[i].y));
                if (!bad[i]) finiteCount++;
            }

            if (finiteCount > 0)
            {
                var us = new double[finiteCount];
                var vs = new double[finiteCount];
                int k = 0;
                for (int i = 0; i < n; i++)
                    if (!bad[i]) { us[k] = uvs[i].x; vs[k] = uvs[i].y; k++; }
                Array.Sort(us);
                Array.Sort(vs);
                double u5 = Percentile(us, 0.05), u95 = Percentile(us, 0.95);
                double v5 = Percentile(vs, 0.05), v95 = Percentile(vs, 0.95);
                double ru = u95 - u5, rv = v95 - v5;
                double ulo = u5 - 2 * ru, uhi = u95 + 2 * ru, vlo = v5 - 2 * rv, vhi = v95 + 2 * rv;
                for (int i = 0; i < n; i++)
                    if (!bad[i] && (uvs[i].x < ulo || uvs[i].x > uhi || uvs[i].y < vlo || uvs[i].y > vhi))
                        bad[i] = true;
            }

            int triCount = indices.Length / 3;
            var incident = new int[n];
            var collapsedIncident = new int[n];
            for (int t = 0; t < triCount; t++)
            {
                int a = indices[3 * t], b = indices[3 * t + 1], c = indices[3 * t + 2];
                incident[a]++; incident[b]++; incident[c]++;
                if (bad[a] || bad[b] || bad[c]) continue;
                if (IsCollapsed(positions[a], positions[b], positions[c], uvs[a], uvs[b], uvs[c]))
                {
                    stats.CollapsedTriangles++;
                    collapsedIncident[a]++; collapsedIncident[b]++; collapsedIncident[c]++;
                }
            }
            for (int i = 0; i < n; i++)
                if (collapsedIncident[i] > 0 && collapsedIncident[i] == incident[i]) bad[i] = true;

            int badCount = 0;
            for (int i = 0; i < n; i++) if (bad[i]) badCount++;
            stats.BadVertices = badCount;
            if (badCount == 0) return null;
            if (badCount > RegenerateFraction * n)
            {
                stats.Regenerated = true;
                stats.ProjectedVertices = n;
                return BoxProject(positions, indices, normals, null);
            }

            var outUv = (Vector2[])uvs.Clone();
            var neighbours = new List<int>[n];
            for (int i = 0; i < n; i++) neighbours[i] = new List<int>();
            for (int t = 0; t < triCount; t++)
            {
                int a = indices[3 * t], b = indices[3 * t + 1], c = indices[3 * t + 2];
                AddUnique(neighbours[a], b); AddUnique(neighbours[a], c);
                AddUnique(neighbours[b], a); AddUnique(neighbours[b], c);
                AddUnique(neighbours[c], a); AddUnique(neighbours[c], b);
            }
            foreach (var list in neighbours) list.Sort();

            int repaired = RingFill(bad, outUv, neighbours);

            if (Array.IndexOf(bad, true) >= 0)
            {
                Bounds(positions, out var mn, out var mx);
                double diag = Math.Sqrt((double)(mx - mn).sqrMagnitude);
                double cell = diag > 0 ? diag * WeldEps : 1e-12;
                var keys = new (long, long, long)[n];
                var groups = new Dictionary<(long, long, long), List<int>>();
                for (int i = 0; i < n; i++)
                {
                    var p = positions[i];
                    var key = ((long)Math.Floor(p.x / cell + 0.5), (long)Math.Floor(p.y / cell + 0.5), (long)Math.Floor(p.z / cell + 0.5));
                    keys[i] = key;
                    if (!groups.TryGetValue(key, out var g)) groups[key] = g = new List<int>();
                    g.Add(i);
                }
                var copied = new List<KeyValuePair<int, Vector2>>();
                for (int i = 0; i < n; i++)
                {
                    if (!bad[i]) continue;
                    foreach (int j in groups[keys[i]])
                        if (!bad[j]) { copied.Add(new KeyValuePair<int, Vector2>(i, outUv[j])); break; }
                }
                foreach (var kv in copied) { outUv[kv.Key] = kv.Value; bad[kv.Key] = false; }
                repaired += copied.Count;
                if (copied.Count > 0) repaired += RingFill(bad, outUv, neighbours);
            }

            var left = new HashSet<int>();
            for (int i = 0; i < n; i++) if (bad[i]) left.Add(i);
            if (left.Count > 0)
            {
                var proj = BoxProject(positions, indices, normals, left);
                foreach (int i in left) outUv[i] = proj[i];
                stats.ProjectedVertices = left.Count;
            }
            stats.RepairedVertices = repaired;
            return outUv;
        }

        private static int RingFill(bool[] bad, Vector2[] uv, List<int>[] neighbours)
        {
            int repaired = 0;
            var updates = new List<KeyValuePair<int, Vector2>>();
            while (true)
            {
                updates.Clear();
                for (int i = 0; i < bad.Length; i++)
                {
                    if (!bad[i]) continue;
                    double su = 0, sv = 0;
                    int cnt = 0;
                    foreach (int j in neighbours[i])
                    {
                        if (bad[j]) continue;
                        su += uv[j].x; sv += uv[j].y; cnt++;
                    }
                    if (cnt > 0) updates.Add(new KeyValuePair<int, Vector2>(i, new Vector2((float)(su / cnt), (float)(sv / cnt))));
                }
                if (updates.Count == 0) return repaired;
                foreach (var kv in updates) { uv[kv.Key] = kv.Value; bad[kv.Key] = false; }
                repaired += updates.Count;
            }
        }

        private static bool IsCollapsed(Vector3 pa, Vector3 pb, Vector3 pc, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            double e1x = pb.x - pa.x, e1y = pb.y - pa.y, e1z = pb.z - pa.z;
            double e2x = pc.x - pa.x, e2y = pc.y - pa.y, e2z = pc.z - pa.z;
            double e3x = pc.x - pb.x, e3y = pc.y - pb.y, e3z = pc.z - pb.z;
            double cx = e1y * e2z - e1z * e2y, cy = e1z * e2x - e1x * e2z, cz = e1x * e2y - e1y * e2x;
            double posArea = Math.Sqrt(cx * cx + cy * cy + cz * cz);
            double posEdge2 = Math.Max(e1x * e1x + e1y * e1y + e1z * e1z,
                              Math.Max(e2x * e2x + e2y * e2y + e2z * e2z, e3x * e3x + e3y * e3y + e3z * e3z));
            if (posEdge2 <= 0 || posArea <= SliverEps * posEdge2) return false;
            double f1x = ub.x - ua.x, f1y = ub.y - ua.y;
            double f2x = uc.x - ua.x, f2y = uc.y - ua.y;
            double f3x = uc.x - ub.x, f3y = uc.y - ub.y;
            double uvArea = Math.Abs(f1x * f2y - f1y * f2x);
            double uvEdge2 = Math.Max(f1x * f1x + f1y * f1y, Math.Max(f2x * f2x + f2y * f2y, f3x * f3x + f3y * f3y));
            return uvArea <= SliverEps * uvEdge2;
        }

        /// <summary>Box projection into [0,1] from the mesh bounds (see uv_repair.box_project).</summary>
        public static Vector2[] BoxProject(Vector3[] positions, int[] indices, Vector3[] normals, HashSet<int> only)
        {
            var result = new Vector2[positions.Length];
            if (positions.Length == 0) return result;
            Bounds(positions, out var mn, out var mx);
            float ext = Mathf.Max(mx.x - mn.x, Mathf.Max(mx.y - mn.y, mx.z - mn.z));
            if (ext <= 0) ext = 1f;
            if (normals == null || normals.Length != positions.Length) normals = FaceNormals(positions, indices);
            for (int i = 0; i < positions.Length; i++)
            {
                if (only != null && !only.Contains(i)) continue;
                var p = positions[i];
                var nrm = normals[i];
                float ax = Mathf.Abs(nrm.x), ay = Mathf.Abs(nrm.y), az = Mathf.Abs(nrm.z);
                float x = (p.x - mn.x) / ext, y = (p.y - mn.y) / ext, z = (p.z - mn.z) / ext;
                if (ax >= ay && ax >= az) result[i] = new Vector2(z, y);
                else if (ay >= az) result[i] = new Vector2(x, z);
                else result[i] = new Vector2(x, y);
            }
            return result;
        }

        private static Vector3[] FaceNormals(Vector3[] positions, int[] indices)
        {
            var normals = new Vector3[positions.Length];
            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                var nrm = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                normals[a] += nrm; normals[b] += nrm; normals[c] += nrm;
            }
            return normals;
        }

        private static void Bounds(Vector3[] positions, out Vector3 mn, out Vector3 mx)
        {
            mn = positions[0]; mx = positions[0];
            for (int i = 1; i < positions.Length; i++) { mn = Vector3.Min(mn, positions[i]); mx = Vector3.Max(mx, positions[i]); }
        }

        private static double Percentile(double[] sorted, double frac) => sorted[(int)Math.Floor(frac * (sorted.Length - 1))];
        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static void AddUnique(List<int> list, int v)
        {
            if (!list.Contains(v)) list.Add(v);
        }
    }
}
