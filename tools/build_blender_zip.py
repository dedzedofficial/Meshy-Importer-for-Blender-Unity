"""Build the Blender extension / add-on ZIP deterministically.

    python tools/build_blender_zip.py            # write the ZIP
    python tools/build_blender_zip.py --check    # fail if the committed ZIP is stale

The ZIP contains blender/meshy_blender_importer plus core/python/meshy_core as the
bundled sub-package meshy_blender_importer/meshy_core. Timestamps and ordering are
fixed so rebuilding unchanged sources gives byte-identical output.
"""

import argparse
import io
import os
import sys
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))
ADDON = os.path.join(ROOT, "blender", "meshy_blender_importer")
CORE = os.path.join(ROOT, "core", "python", "meshy_core")
OUT = os.path.join(ROOT, "blender", "Meshy Importer for Blender & Unity - Blender.zip")
TOP = "meshy_blender_importer"
FIXED_TIME = (2026, 1, 1, 0, 0, 0)


def _files():
    entries = []
    for name in sorted(os.listdir(ADDON)):
        path = os.path.join(ADDON, name)
        if os.path.isfile(path) and not name.endswith(".pyc"):
            entries.append((TOP + "/" + name, path))
    for name in sorted(os.listdir(CORE)):
        path = os.path.join(CORE, name)
        if name.endswith(".py"):
            entries.append((TOP + "/meshy_core/" + name, path))
    return entries


def build():
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as zf:
        for arc, path in _files():
            with open(path, "rb") as f:
                data = f.read().replace(b"\r\n", b"\n")
            info = zipfile.ZipInfo(arc, FIXED_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            zf.writestr(info, data, compresslevel=9)
    return buf.getvalue()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="verify the committed ZIP is up to date")
    args = ap.parse_args()
    data = build()
    if args.check:
        try:
            with open(OUT, "rb") as f:
                current = f.read()
        except OSError:
            current = b""
        if current != data:
            print("Blender ZIP is out of date. Run: python tools/build_blender_zip.py", file=sys.stderr)
            return 1
        print("Blender ZIP is up to date (%d files)." % len(_files()))
        return 0
    with open(OUT, "wb") as f:
        f.write(data)
    print("wrote %s (%d bytes, %d files)" % (os.path.relpath(OUT, ROOT), len(data), len(_files())))
    return 0


if __name__ == "__main__":
    sys.exit(main())
