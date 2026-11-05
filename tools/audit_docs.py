"""Ensure the documentation does not regress to old versions or broken installer claims."""
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
for path in ROOT.rglob("*.md"):
    rel = path.relative_to(ROOT)
    if any(part in {".git", ".venv", "bin", "obj", "Library", "Temp"} for part in rel.parts):
        continue
    content = path.read_text(encoding="utf-8")
    assert "\u2014" not in content, f"Em dash in {rel}"
    for obsolete in ("downloads/Meshy-Importer-Browser.zip?raw=1", "WORKFLOW_1.5.1.md", "UPGRADE_1.5.1.md"):
        assert obsolete not in content, f"Obsolete {obsolete} in {rel}"

version = json.loads((ROOT / "browser/manifest.json").read_text(encoding="utf-8"))["version"]
readme = (ROOT / "README.md").read_text(encoding="utf-8")
assert f"**Version {version}" in readme
assert f"releases/download/v{version}/Meshy-Importer-Browser.zip" in readme
assert "https://fishhwb.github.io/install/meshy/" in readme
assert "site/README.md" not in readme
assert "dedzedofficial.github.io/Meshy-Importer-for-Blender-Unity/" not in readme
assert "Load unpacked" in readme
assert not (ROOT / "WORKFLOW_1.5.1.md").exists()
assert not (ROOT / "blender/README.md").exists()
print(f"Documentation audit passed for v{version}.")
