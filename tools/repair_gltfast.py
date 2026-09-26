#!/usr/bin/env python3
"""Inspect a Unity glTFast collision; --apply removes ONE redundant direct dependency.

Close Unity first. No third-party modules needed (Python 3.9+).
  python repair_gltfast.py "C:/Unity/MyProject"
  python repair_gltfast.py "C:/Unity/MyProject" --keep unity --apply
"""
import argparse
import datetime
import json
import os
from pathlib import Path
import shutil
import tempfile

UNITY = "com.unity.cloud.gltfast"
LEGACY = "com.atteneder.gltfast"
VARIANTS = {"unity": UNITY, "legacy": LEGACY}


def _object(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate JSON key: " + key)
            result[key] = value
        return result
    value = json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique)
    if not isinstance(value, dict):
        raise ValueError(str(path) + " must contain a JSON object")
    return value


def _deps(value, name):
    deps = value.get("dependencies")
    if not isinstance(deps, dict):
        raise ValueError(name + " has no valid dependencies object")
    return deps


def inspect(project, keep="unity"):
    project = Path(project).expanduser().resolve()
    if not (project / "Assets").is_dir() or not (project / "ProjectSettings").is_dir():
        raise ValueError("Choose the Unity project root containing Assets, Packages and ProjectSettings.")
    manifest_path = project / "Packages/manifest.json"
    original = manifest_path.read_bytes()
    manifest = _object(manifest_path)
    direct = _deps(manifest, "manifest.json")
    if any(not isinstance(v, str) for v in direct.values()):
        raise ValueError("Every manifest dependency must have a string version or URL")
    lock_path = project / "Packages/packages-lock.json"
    locked = _deps(_object(lock_path), "packages-lock.json") if lock_path.exists() else {}
    lock_bytes = lock_path.read_bytes() if lock_path.exists() else None
    graph = {}
    for name, entry in locked.items():
        if not isinstance(entry, dict):
            raise ValueError("Invalid lock entry: " + name)
        graph[name] = set(_deps(entry, name))
    embedded = set()
    package_inputs = []
    local_manifests = list((project / "Packages").glob("*/package.json"))
    for value in direct.values():
        if value.startswith("file:"):
            local = Path(value[5:])
            if not local.is_absolute():
                local = project / "Packages" / local
            if local.is_dir():
                local_manifests.append(local / "package.json")
    for path in local_manifests:
        data = _object(path)
        name = data.get("name")
        if not isinstance(name, str):
            raise ValueError("Package has no name: " + str(path))
        dependencies = data.get("dependencies", {})
        if not isinstance(dependencies, dict):
            raise ValueError("Invalid local package dependencies: " + str(path))
        graph[name] = set(dependencies)
        package_inputs.append((path, path.read_bytes()))
        if path.parent.parent == project / "Packages":
            embedded.add(name)
    present = set(direct) | set(locked) | embedded
    kept = VARIANTS[keep]
    removed = LEGACY if keep == "unity" else UNITY
    # Only packages reachable from a direct/embedded root can require removal targets.
    reachable = set()
    todo = list((set(direct) | embedded) - {removed})
    while todo:
        name = todo.pop()
        if name in reachable:
            continue
        reachable.add(name)
        todo.extend(graph.get(name, ()))
    parents = sorted(name for name in reachable if name != removed and removed in graph.get(name, ()))
    blockers = []
    if (project / "Temp/UnityLockfile").exists():
        blockers.append("Unity appears open (Temp/UnityLockfile exists). Close the Editor first.")
    missing = sorted((set(direct) | embedded) - set(graph))
    if missing:
        blockers.append("Dependency graph is incomplete for: " + ", ".join(missing) + ". Resolve packages in Unity first.")
    if lock_bytes is None:
        blockers.append("packages-lock.json is missing; dependency safety cannot be established. Resolve packages in Unity first.")
    if not {UNITY, LEGACY} <= present:
        blockers.append("Both package IDs are not present. No duplicate direct dependency to repair.")
    if removed in embedded:
        blockers.append(removed + " is embedded. Move/remove that embedded package manually after checking its dependants.")
    if removed not in direct:
        blockers.append(removed + " is not a direct manifest dependency. Update the package that brings it in.")
    if parents:
        blockers.append("Removal would break dependencies from: " + ", ".join(parents) + ". Update those packages or keep the other variant.")
    return dict(project=project, manifest=manifest, original=original, lock_bytes=lock_bytes,
                package_inputs=package_inputs, keep=kept, remove=removed,
                present=present, parents=parents, blockers=blockers)


def apply(plan):
    if plan["blockers"]:
        raise ValueError("\n".join(plan["blockers"]))
    project = plan["project"]
    manifest_path = project / "Packages/manifest.json"
    lock_path = project / "Packages/packages-lock.json"
    # Re-inspect before any write; don't apply a stale preview.
    fresh = inspect(project, "unity" if plan["keep"] == UNITY else "legacy")
    if fresh["blockers"]:
        raise ValueError("\n".join(fresh["blockers"]))
    if fresh["original"] != plan["original"] or fresh["lock_bytes"] != plan["lock_bytes"] or fresh["package_inputs"] != plan["package_inputs"]:
        raise ValueError("Package configuration changed since inspection. Run the command again.")
    backup_root = project / "MeshyPackageBackups"
    backup_root.mkdir(exist_ok=True)
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%S")
    backup = Path(tempfile.mkdtemp(prefix=stamp + "-", dir=str(backup_root)))
    (backup / "manifest.json").write_bytes(plan["original"])
    (backup / "packages-lock.json").write_bytes(plan["lock_bytes"])
    manifest = json.loads(json.dumps(plan["manifest"]))
    del manifest["dependencies"][plan["remove"]]
    newline = "\r\n" if b"\r\n" in plan["original"] else "\n"
    payload = (json.dumps(manifest, indent=2, ensure_ascii=False) + "\n").replace("\n", newline).encode("utf-8")
    handle, name = tempfile.mkstemp(prefix="meshy-manifest-", suffix=".tmp", dir=str(manifest_path.parent))
    temporary = Path(name)
    try:
        with os.fdopen(handle, "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        shutil.copymode(manifest_path, temporary)
        if manifest_path.read_bytes() != plan["original"] or lock_path.read_bytes() != plan["lock_bytes"] or (project / "Temp/UnityLockfile").exists():
            raise ValueError("Project changed during repair; no manifest change made.")
        for path, data in plan["package_inputs"]:
            if path.read_bytes() != data:
                raise ValueError("A local package changed during repair; no manifest change made.")
        os.replace(temporary, manifest_path)
    finally:
        temporary.unlink(missing_ok=True)
    return backup


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("project", help="Unity project root")
    parser.add_argument("--keep", choices=VARIANTS, default="unity")
    parser.add_argument("--apply", action="store_true", help="back up configuration and remove the unused direct dependency")
    args = parser.parse_args(argv)
    try:
        plan = inspect(args.project, args.keep)
        for name in (UNITY, LEGACY):
            print(name + ": " + ("present" if name in plan["present"] else "not found"))
        print("Keep: " + plan["keep"] + "\nRemove direct dependency: " + plan["remove"])
        if plan["blockers"]:
            print("Cannot apply:\n- " + "\n- ".join(plan["blockers"]))
            return 2
        if not args.apply:
            print("Preview only. No files changed. Add --apply to make this change with a backup.")
            return 0
        backup = apply(plan)
        print("Manifest updated. Backup: " + str(backup))
        print("Reopen Unity and wait for Package Manager to resolve. The lock file was preserved; Unity updates it.")
        print("No assets, GUIDs, caches, scoped registries or package versions were changed.")
        return 0
    except (OSError, ValueError) as exc:
        print("Repair stopped: " + str(exc))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
