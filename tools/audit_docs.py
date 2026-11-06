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
require(f"**Version {version}" in readme, "Documentation audit failed: f'**Version {version}' in readme")
require(f"releases/download/v{version}/Meshy-Importer-Browser.zip" in readme, "Documentation audit failed: f'releases/download/v{version}/Meshy-Importer-Browser.zip' in readme")
require("https://fishhwb.github.io/install/meshy/" in readme, "Documentation audit failed: 'https://fishhwb.github.io/install/meshy/' in readme")
require("site/README.md" not in readme, "Documentation audit failed: 'site/README.md' not in readme")
require("dedzedofficial.github.io/Meshy-Importer-for-Blender-Unity/" not in readme, "Documentation audit failed: 'dedzedofficial.github.io/Meshy-Importer-for-Blender-Unity/' not in readme")
require("Load unpacked" in readme, "Documentation audit failed: 'Load unpacked' in readme")
require(not (ROOT / "WORKFLOW_1.5.1.md").exists(), "Documentation audit failed: not (ROOT / 'WORKFLOW_1.5.1.md').exists()")
require(not (ROOT / "blender/README.md").exists(), "Documentation audit failed: not (ROOT / 'blender/README.md').exists()")
print(f"Documentation audit passed for v{version}.")
