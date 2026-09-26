"""Check that the C# and GDScript "wrong file" messages match the Python reference.

    python tests/cross_check_wrong_file.py [--dotnet DOTNET] [--godot GODOT]

A port whose runtime is not given/found is skipped. Exit code is non-zero on any mismatch.
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(ROOT, "core", "python"))
sys.path.insert(0, HERE)

from meshy_core.decode import describe_wrong_file, encode_meshy_bytes  # noqa: E402
from meshy_fixtures import WRONG_FILE_CASES  # noqa: E402


def _inputs():
    valid = encode_meshy_bytes(b"glTF\x02\x00\x00\x00" + b"\x00" * 9000)
    return [data for data, _ in WRONG_FILE_CASES] + [valid, b"\xef\xbb\xbf<html>"]


def _compare(name, ref, got):
    bad = [(i, a, b) for i, (a, b) in enumerate(zip(ref, got)) if a != b]
    if len(ref) != len(got) or bad:
        for i, a, b in bad:
            print("%s case %d:\n  ref: %r\n  got: %r" % (name, i, a, b))
        print("%s: MISMATCH" % name)
        return False
    print("%s: OK (%d cases)" % (name, len(ref)))
    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dotnet", default=shutil.which("dotnet"))
    ap.add_argument("--godot", default=shutil.which("godot") or shutil.which("godot4"))
    args = ap.parse_args()
    inputs = _inputs()
    ref = [describe_wrong_file(d) or "" for d in inputs]
    ok = True
    with tempfile.TemporaryDirectory() as tmp:
        paths = []
        for i, data in enumerate(inputs):
            p = os.path.join(tmp, "case%02d.bin" % i)
            with open(p, "wb") as f:
                f.write(data)
            paths.append(p)
        if args.dotnet:
            out = subprocess.run([args.dotnet, "run", "--project", os.path.join(HERE, "csharp", "wrong-file"),
                                  "-v", "q", "--"] + paths, capture_output=True, text=True)
            if out.returncode != 0:
                print(out.stdout, out.stderr)
                return 1
            ok &= _compare("C# (unity/Editor/MeshyFileCheck.cs)", ref, json.loads(out.stdout.strip().splitlines()[-1]))
        else:
            print("C#: skipped (dotnet not found)")
        if args.godot:
            project = os.path.join(tmp, "godot_project")
            shutil.copytree(os.path.join(HERE, "hosts", "godot_project"), project)
            shutil.copytree(os.path.join(ROOT, "godot", "addons"), os.path.join(project, "addons"))
            shutil.copy(os.path.join(HERE, "hosts", "godot_wrong_file.gd"), project)
            out = subprocess.run([args.godot, "--headless", "--path", project, "--script", "res://godot_wrong_file.gd",
                                  "--"] + paths, capture_output=True, text=True)
            lines = [l for l in out.stdout.splitlines() if l.startswith("[")]
            if out.returncode != 0 or not lines:
                print(out.stdout, out.stderr)
                return 1
            ok &= _compare("GDScript (godot/addons/meshy_importer/meshy_decrypt.gd)", ref, json.loads(lines[-1]))
        else:
            print("GDScript: skipped (godot not found)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
