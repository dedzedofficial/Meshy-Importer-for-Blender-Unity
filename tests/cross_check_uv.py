"""Check that the C# and GDScript UV-repair ports agree with the Python reference.

    python tests/cross_check_uv.py [--dotnet DOTNET] [--godot GODOT]

Each available port is run on the same cases; stats must match exactly and UVs
within float32 tolerance. A port whose runtime is not given/found is skipped.
Exit code is non-zero on any mismatch.
"""

import argparse
import json
import math
import os
import shutil
import struct
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(ROOT, "core", "python"))
sys.path.insert(0, HERE)

from meshy_core.uv_repair import repair_uvs  # noqa: E402
from meshy_fixtures import grid_mesh  # noqa: E402

STAT_KEYS = ("bad", "collapsed", "repaired", "projected", "regen")


def _f32(x):
    return struct.unpack("<f", struct.pack("<f", x))[0] if math.isfinite(x) else x


def cases():
    pos, uv, idx = grid_mesh()
    pos = [tuple(_f32(c) for c in p) for p in pos]
    uv = [tuple(_f32(c) for c in p) for p in uv]
    out = [(pos, uv, idx)]
    broken = list(uv)
    broken[40] = (float("nan"), 0.5)
    broken[12] = (50.0, -40.0)
    out.append((pos, broken, idx))
    out.append((pos, None, idx))
    out.append((pos, [(0.0, 0.0)] * len(pos), idx))
    seam_pos = [(0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0), (1, 1, 0), (2, 0, 0), (2, 1, 0)]
    seam_uv = [(0, 0), (0.4, 0), (0, 1), (0.4, 1), (float("nan"), float("nan")), (float("inf"), 0.0),
               (float("nan"), 0.0), (1, 1)]
    out.append((seam_pos, seam_uv, [0, 1, 2, 1, 3, 2, 4, 6, 5, 6, 7, 5]))
    strip_pos = [(0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0), (2, 0, 0), (2, 1, 0)]
    out.append((strip_pos, [(0, 0), (0.5, 0), (0, 0), (0.5, 0), (1, 0), (1, 1)],
                [0, 1, 2, 1, 3, 2, 1, 4, 3, 4, 5, 3]))
    tiny = [(u / 4096.0, v / 4096.0) for u, v in uv]
    out.append((pos, [tuple(_f32(c) for c in p) for p in tiny], idx))
    return out


def _enc(v):
    if math.isnan(v):
        return "NaN"
    if math.isinf(v):
        return "Infinity" if v > 0 else "-Infinity"
    return v


def reference(all_cases):
    res = []
    for p, u, i in all_cases:
        r, s = repair_uvs(p, u, i)
        res.append({"uv": None if r is None else [list(x) for x in r], "bad": s["bad_vertices"],
                    "collapsed": s["collapsed_triangles"], "repaired": s["repaired_vertices"],
                    "projected": s["projected_vertices"], "regen": s["regenerated"]})
    return res


def compare(name, ref, got):
    ok = True
    if len(ref) != len(got):
        print("%s: expected %d results, got %d" % (name, len(ref), len(got)))
        return False
    for k, (a, b) in enumerate(zip(ref, got)):
        stats_a = [a[x] for x in STAT_KEYS]
        stats_b = [b[x] for x in STAT_KEYS]
        if stats_a != stats_b:
            print("%s case %d: stats differ: ref %s vs %s" % (name, k, stats_a, stats_b))
            ok = False
        if (a["uv"] is None) != (b["uv"] is None):
            print("%s case %d: one side changed UVs, the other did not" % (name, k))
            ok = False
        elif a["uv"] is not None:
            diff = max(abs(x - y) for p, q in zip(a["uv"], b["uv"]) for x, y in zip(p, q))
            if diff > 1e-5:
                print("%s case %d: UVs differ by %g" % (name, k, diff))
                ok = False
    print("%s: %s (%d cases)" % (name, "OK" if ok else "MISMATCH", len(ref)))
    return ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dotnet", default=shutil.which("dotnet"))
    ap.add_argument("--godot", default=shutil.which("godot") or shutil.which("godot4"))
    args = ap.parse_args()

    all_cases = cases()
    ref = reference(all_cases)
    ok = True
    ran = 0
    with tempfile.TemporaryDirectory() as tmp:
        path = os.path.join(tmp, "cases.json")
        with open(path, "w") as f:
            json.dump([{"pos": p, "uv": None if u is None else [[_enc(a), _enc(b)] for a, b in u], "idx": i}
                       for p, u, i in all_cases], f)
        if args.dotnet:
            proj = os.path.join(HERE, "csharp", "uv-parity")
            out = subprocess.run([args.dotnet, "run", "--project", proj, "-v", "q", "--", path],
                                 capture_output=True, text=True)
            if out.returncode != 0:
                print(out.stdout, out.stderr)
                return 1
            ok &= compare("C# (unity/Editor/MeshyUvRepair.cs)", ref, json.loads(out.stdout.strip().splitlines()[-1]))
            ran += 1
        else:
            print("C#: skipped (dotnet not found)")
        if args.godot:
            script = os.path.join(HERE, "hosts", "godot_uv_parity.gd")
            out = subprocess.run([args.godot, "--headless", "--path", os.path.join(HERE, "hosts", "godot_project"),
                                  "--script", script, "--", path], capture_output=True, text=True)
            lines = [l for l in out.stdout.splitlines() if l.startswith("[")]
            if out.returncode != 0 or not lines:
                print(out.stdout, out.stderr)
                return 1
            ok &= compare("GDScript (godot/addons/meshy_importer/meshy_uv_repair.gd)", ref, json.loads(lines[-1]))
            ran += 1
        else:
            print("GDScript: skipped (godot not found)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
