"""Ensure the documentation does not regress to old versions or broken installer claims."""
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[1]
for path in ROOT.rglob("*.md"):
    rel = path.relative_to(ROOT)
    if any(part in {".git", ".venv", "bin", "obj", "Library", "Temp"} for part in rel.parts):
        continue
    content = path.read_text(encoding="utf-8")
    assert "\u2014" not in content, f"Em dash in {rel}"
    assert "downloads/Meshy-Importer-Browser.zip?raw=1" not in content, f"Old link in {rel}"
    assert "WORKFLOW_1.5.1.md" not in content, f"Old workflow link in {rel}"
    assert "UPGRADE_1.5.1.md" not in content, f"Old repair link in {rel}"

version = json.loads((ROOT / "browser/manifest.json").read_text(encoding="utf-8"))["version"]
assert version == "1.5.2", version
readme = (ROOT / "README.md").read_text(encoding="utf-8")
assert "**Version 1.5.2" in readme
assert "releases/download/v1.5.2/Meshy-Importer-Browser.zip" in readme
assert "Load unpacked" in readme
assert not (ROOT / "WORKFLOW_1.5.1.md").exists()
assert not (ROOT / "blender/README.md").exists()
print("Documentation audit passed for v1.5.2.")
