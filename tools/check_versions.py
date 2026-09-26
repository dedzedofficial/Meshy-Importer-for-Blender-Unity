"""Fail if any host/package declares a different version than unity/package.json.

    python tools/check_versions.py            # check
    python tools/check_versions.py 1.4.2      # print the files to update for a new version
"""

import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))


def _read(rel):
    with open(os.path.join(ROOT, rel), encoding="utf-8") as f:
        return f.read()


def _search(rel, pattern, flags=0):
    m = re.search(pattern, _read(rel), flags)
    return ".".join(g for g in m.groups() if g is not None) if m else None


SOURCES = [
    ("unity/package.json", lambda: json.loads(_read("unity/package.json"))["version"]),
    ("FISHHWB_METADATA.json", lambda: json.loads(_read("FISHHWB_METADATA.json"))["version"]),
    ("blender/meshy_blender_importer/blender_manifest.toml",
     lambda: _search("blender/meshy_blender_importer/blender_manifest.toml", r'^version\s*=\s*"([^"]+)"', re.M)),
    ("blender/meshy_blender_importer/__init__.py (bl_info)",
     lambda: _search("blender/meshy_blender_importer/__init__.py", r'"version"\s*:\s*\((\d+),\s*(\d+),\s*(\d+)\)')),
    ("core/python/meshy_core/__init__.py",
     lambda: _search("core/python/meshy_core/__init__.py", r'__version__\s*=\s*"([^"]+)"')),
    ("godot/addons/meshy_importer/plugin.cfg",
     lambda: _search("godot/addons/meshy_importer/plugin.cfg", r'^version="([^"]+)"', re.M)),
    ("godot/addons/meshy_importer/meshy_scene_importer.gd",
     lambda: _search("godot/addons/meshy_importer/meshy_scene_importer.gd", r'"meshy_importer_version",\s*"([^"]+)"')),
    ("godot/addons/meshy_importer/meshy_support.gd",
     lambda: _search("godot/addons/meshy_importer/meshy_support.gd", r'^const VERSION := "([^"]+)"', re.M)),
    ("unreal/MeshyImporter/MeshyImporter.uplugin",
     lambda: json.loads(_read("unreal/MeshyImporter/MeshyImporter.uplugin"))["VersionName"]),
    ("unreal/MeshyImporter/Content/Python/meshy_unreal.py",
     lambda: _search("unreal/MeshyImporter/Content/Python/meshy_unreal.py", r'^VERSION\s*=\s*"([^"]+)"', re.M)),
    ("unity/Editor/MeshyImporterMenu.cs (fallback)",
     lambda: _search("unity/Editor/MeshyImporterMenu.cs", r'FallbackVersion\s*=\s*"([^"]+)"')),
    ("README.md", lambda: _search("README.md", r"\*\*Version (\d+\.\d+\.\d+)")),
    ("unity/README.md", lambda: _search("unity/README.md", r"v(\d+\.\d+\.\d+)")),
    ("blender/meshy_blender_importer/README.md",
     lambda: _search("blender/meshy_blender_importer/README.md", r"\*\*Version (\d+\.\d+\.\d+)")),
    ("godot/README.md", lambda: _search("godot/README.md", r"\*\*Version (\d+\.\d+\.\d+)")),
    ("unreal/README.md", lambda: _search("unreal/README.md", r"\*\*Version (\d+\.\d+\.\d+)")),
]


def main():
    expected = json.loads(_read("unity/package.json"))["version"]
    if len(sys.argv) > 1:
        print("Set version %s in:" % sys.argv[1])
        for name, _ in SOURCES:
            print("  " + name)
        print("and the Unreal .uplugin integer \"Version\" (e.g. 1.4.2 -> 142).")
        return 0
    bad = 0
    for name, get in SOURCES:
        try:
            found = get()
        except Exception as exc:  # missing file / field
            found = "error: %s" % exc
        status = "ok" if found == expected else "MISMATCH"
        if found != expected:
            bad += 1
        print("%-60s %-10s %s" % (name, found, status))
    uplugin = json.loads(_read("unreal/MeshyImporter/MeshyImporter.uplugin"))["Version"]
    if uplugin != int(expected.replace(".", "")):
        print("unreal/MeshyImporter/MeshyImporter.uplugin \"Version\" %s should be %s" % (uplugin, expected.replace(".", "")))
        bad += 1
    print("All versions are %s." % expected if not bad else "%d version mismatch(es)." % bad)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
