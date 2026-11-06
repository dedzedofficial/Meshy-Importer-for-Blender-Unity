"""Ensure the documentation does not regress to old versions or broken installer claims."""
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]


def require(condition, message):
    if not condition:
        raise SystemExit(message)


for path in ROOT.rglob("*.md"):
    rel = path.relative_to(ROOT)
    if any(part in {".git", ".venv", "bin", "obj", "Library", "Temp"} for part in rel.parts):
        continue
    content = path.read_text(encoding="utf-8")
    require("\u2014" not in content, f"Em dash in {rel}")
    for obsolete in ("downloads/Meshy-Importer-Browser.zip?raw=1", "WORKFLOW_1.5.1.md", "UPGRADE_1.5.1.md"):
        require(obsolete not in content, f"Obsolete {obsolete} in {rel}")

version = json.loads((ROOT / "browser/manifest.json").read_text(encoding="utf-8"))["version"]
readme = (ROOT / "README.md").read_text(encoding="utf-8")
for needle, present in (
    (f"**Version {version}", True),
    (f"releases/download/v{version}/Meshy-Importer-Browser.zip", True),
    ("https://fishhwb.github.io/install/meshy/", True),
    ("site/README.md", False),
    ("dedzedofficial.github.io/Meshy-Importer-for-Blender-Unity/", False),
    ("Load unpacked", True),
):
    require((needle in readme) == present, f"Documentation audit failed: {needle!r} {'missing' if present else 'unexpected'} in README.md")
for obsolete in ("WORKFLOW_1.5.1.md", "blender/README.md"):
    require(not (ROOT / obsolete).exists(), f"Documentation audit failed: obsolete file {obsolete}")
print(f"Documentation audit passed for v{version}.")
