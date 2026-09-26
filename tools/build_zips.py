"""Build the ready-to-install ZIPs deterministically.

    python tools/build_zips.py                  # write every committed ZIP
    python tools/build_zips.py --check          # fail if a committed ZIP is stale
    python tools/build_zips.py --release dist   # write the release assets into dist/ (only)

Release assets (stable names, linked from the README via releases/latest/download/):
  Meshy-Importer-Blender.zip   same content as the committed Blender ZIP
  Meshy-Importer-Unreal.zip    same content as the committed Unreal ZIP
  Meshy-Importer-Godot.zip     addons/meshy_importer/... (unzip into a Godot project)

Both ZIPs bundle the shared core/python/meshy_core package:
  blender/Meshy Importer for Blender & Unity - Blender.zip
      meshy_blender_importer/...           + meshy_blender_importer/meshy_core/
  unreal/Meshy Importer for Unreal.zip
      MeshyImporter/MeshyImporter.uplugin  + MeshyImporter/Content/Python/meshy_core/

Timestamps and ordering are fixed so rebuilding unchanged sources gives
byte-identical output.
"""

import argparse
import io
import os
import sys
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))
CORE = os.path.join(ROOT, "core", "python", "meshy_core")
FIXED_TIME = (2026, 1, 1, 0, 0, 0)
SKIP_DIRS = {"__pycache__"}


def _tree(src_dir, arc_prefix):
    entries = []
    for dirpath, dirnames, filenames in os.walk(src_dir):
        dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS)
        for name in sorted(filenames):
            if name.endswith(".pyc"):
                continue
            path = os.path.join(dirpath, name)
            rel = os.path.relpath(path, src_dir).replace(os.sep, "/")
            entries.append((arc_prefix + rel, path))
    return entries


def _core(arc_prefix):
    return [(arc_prefix + name, os.path.join(CORE, name))
            for name in sorted(os.listdir(CORE)) if name.endswith(".py")]


def _godot():
    return _tree(os.path.join(ROOT, "godot", "addons", "meshy_importer"), "addons/meshy_importer/")


PACKAGES = {
    os.path.join(ROOT, "blender", "Meshy Importer for Blender & Unity - Blender.zip"): lambda: (
        _tree(os.path.join(ROOT, "blender", "meshy_blender_importer"), "meshy_blender_importer/")
        + _core("meshy_blender_importer/meshy_core/")),
    os.path.join(ROOT, "unreal", "Meshy Importer for Unreal.zip"): lambda: (
        _tree(os.path.join(ROOT, "unreal", "MeshyImporter"), "MeshyImporter/")
        + _core("MeshyImporter/Content/Python/meshy_core/")),
}


def build(entries):
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as zf:
        for arc, path in sorted(entries):
            with open(path, "rb") as f:
                data = f.read()
            if not path.endswith((".png", ".webp", ".zip")):
                data = data.replace(b"\r\n", b"\n")
            info = zipfile.ZipInfo(arc, FIXED_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            zf.writestr(info, data, compresslevel=9)
    return buf.getvalue()


RELEASE_ASSETS = {
    "Meshy-Importer-Blender.zip": PACKAGES[os.path.join(ROOT, "blender", "Meshy Importer for Blender & Unity - Blender.zip")],
    "Meshy-Importer-Unreal.zip": PACKAGES[os.path.join(ROOT, "unreal", "Meshy Importer for Unreal.zip")],
    "Meshy-Importer-Godot.zip": _godot,
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="verify the committed ZIPs are up to date")
    ap.add_argument("--release", metavar="DIR", help="write the release assets into DIR instead")
    args = ap.parse_args()
    if args.release:
        os.makedirs(args.release, exist_ok=True)
        for name, collect in RELEASE_ASSETS.items():
            entries = collect()
            with open(os.path.join(args.release, name), "wb") as f:
                f.write(build(entries))
            print("wrote %s (%d files)" % (os.path.join(args.release, name), len(entries)))
        return 0
    stale = 0
    for out, collect in PACKAGES.items():
        entries = collect()
        data = build(entries)
        rel = os.path.relpath(out, ROOT)
        if args.check:
            try:
                with open(out, "rb") as f:
                    current = f.read()
            except OSError:
                current = b""
            if current != data:
                print("%s is out of date. Run: python tools/build_zips.py" % rel, file=sys.stderr)
                stale += 1
            else:
                print("%s is up to date (%d files)." % (rel, len(entries)))
        else:
            with open(out, "wb") as f:
                f.write(data)
            print("wrote %s (%d bytes, %d files)" % (rel, len(data), len(entries)))
    return 1 if stale else 0


if __name__ == "__main__":
    sys.exit(main())
