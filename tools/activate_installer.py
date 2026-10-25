"""Replace the GitHub ZIP badge only after GitHub Pages has deployed successfully.

The direct GitHub ZIP remains as a manual fallback even after activation.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
readme_path = ROOT / "README.md"
readme = readme_path.read_text(encoding="utf-8")
url = "https://dedzedofficial.github.io/Meshy-Importer-for-Blender-Unity/"
zip_url = ("https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity"
           "/releases/download/v1.5.2/Meshy-Importer-Browser.zip")
old = ("[![Download Browser Extension v1.5.2]"
       "(https://img.shields.io/badge/DOWNLOAD-Browser_Extension_v1.5.2-087ea4"
       "?style=for-the-badge&logo=googlechrome&logoColor=white)]("
       + zip_url + ")")
new = ("[![Smart Install, detects your browser]"
       "(https://img.shields.io/badge/SMART_INSTALL-Auto_Detect_Browser-087ea4"
       "?style=for-the-badge&logo=googlechrome&logoColor=white)]("
       + url + ")")
if new not in readme:
    if old not in readme:
        raise SystemExit("The expected v1.5.2 badge is missing; refuse to change an unknown link.")
    readme = readme.replace(old, new, 1)
    readme = readme.replace(
        "**[Download the browser extension v1.5.2](" + zip_url + ")**",
        "**[Smart browser installation](" + url + ")** | "
        "**[Manual ZIP download](" + zip_url + ")**",
        1,
    )
    readme = readme.replace(
        "Once repository GitHub Pages hosting is enabled and deployed, it can replace the badge below, route Chrome and Edge to the correct install method, and use real browser-store listings as they become available. Until then, this button intentionally points directly to the working v1.5.2 ZIP so visitors never encounter a broken installer page.",
        "The smart installer now runs on GitHub Pages. It detects your browser and operating system locally, offers the tested Chrome or Edge ZIP, and can use official browser-store listings once they exist. The direct v1.5.2 ZIP remains available below as a fallback.",
        1,
    )
    readme_path.write_text(readme, encoding="utf-8")
    print("Activated live browser-aware installer badge; ZIP fallback preserved.")
else:
    print("Smart installer badge already active.")
