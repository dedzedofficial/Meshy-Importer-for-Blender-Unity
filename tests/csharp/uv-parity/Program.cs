// Reads cases JSON ({pos, uv, idx}[]; non-finite UV values as "NaN"/"Infinity" strings),
// runs MeshyUvRepair on each and prints the results as JSON.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FISHHWB.MeshyImporter.Editor;
using UnityEngine;

internal static class Program
{
    private static void Main(string[] args)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(args[0]));
        var results = new List<object>();
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            var pos = c.GetProperty("pos").EnumerateArray()
                .Select(p => new Vector3(F(p[0]), F(p[1]), F(p[2]))).ToArray();
            var idx = c.GetProperty("idx").EnumerateArray().Select(i => i.GetInt32()).ToArray();
            Vector2[] uv = null;
            if (c.GetProperty("uv").ValueKind != JsonValueKind.Null)
                uv = c.GetProperty("uv").EnumerateArray().Select(p => new Vector2(F(p[0]), F(p[1]))).ToArray();
            var repaired = MeshyUvRepair.Repair(pos, uv, idx, null, out var st);
            results.Add(new
            {
                uv = repaired?.Select(v => new[] { (double)v.x, (double)v.y }).ToArray(),
                bad = st.BadVertices,
                collapsed = st.CollapsedTriangles,
                repaired = st.RepairedVertices,
                projected = st.ProjectedVertices,
                regen = st.Regenerated,
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(results));
    }

    private static float F(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? float.Parse(e.GetString(), CultureInfo.InvariantCulture) : (float)e.GetDouble();
}
