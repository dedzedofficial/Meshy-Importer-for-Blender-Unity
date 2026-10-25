# Meshy Importer for Unity, Blender, Godot and Unreal

**Version 1.5.2 | FISHHWB / Ded Zed**

[![Validation](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/actions/workflows/validate.yml/badge.svg)](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/actions/workflows/validate.yml) [![MIT](https://img.shields.io/badge/License-MIT-blue)](LICENSE)

Import and convert Meshy `.meshy` assets on your own device. Unity, Blender, Godot, Unreal and Chrome/Edge are supported. The project does not upload model files to an external conversion service.

## Browser extension

**Smart browser installation is live on the existing [FISH HWB website](https://fishhwb.github.io/install/meshy/).** The page detects the desktop browser locally and offers the tested Chrome or Edge download with the correct instructions. Unsupported browsers receive compatibility guidance; official one-click store installation can be offered once a listing exists.


[![Smart Install, detects your browser](https://img.shields.io/badge/SMART_INSTALL-Auto_Detect_Browser-087ea4?style=for-the-badge&logo=googlechrome&logoColor=white)](https://fishhwb.github.io/install/meshy/)

**[Detect my browser and install](https://fishhwb.github.io/install/meshy/)** | **[Manual v1.5.2 ZIP download](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/download/v1.5.2/Meshy-Importer-Browser.zip)** | [Installation and privacy details](browser/README.md)

1. Download and extract the ZIP.
2. Open `chrome://extensions` or `edge://extensions`.
3. Enable **Developer mode**, choose **Load unpacked**, and select the extracted folder containing `manifest.json`.

Chrome and Edge do not permit a website to install an unpublished extension with a single click. The FISH HWB installer detects your browser and guides you through the supported download. Store installation will be offered only after verified listings are available.

When an open Meshy model page loads a genuine `MESHY.AI` payload, the extension validates it, names it, offers a popup and can download it locally. It does not poll Meshy's cloud generation jobs in the background.

## Download the importers

| Platform | v1.5.2 download | Setup |
|---|---|---|
| Chrome / Edge | [Smart browser installer](https://fishhwb.github.io/install/meshy/) or [ZIP](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/download/v1.5.2/Meshy-Importer-Browser.zip) | Detect browser, then extract ZIP and use Extensions > Developer mode > Load unpacked |
| Unity 2020.3+ | [Unity package source](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/tree/v1.5.2/unity) | Add package from Git URL: `https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity.git?path=/unity#v1.5.2` |
| Blender 3.6+ | [Blender ZIP](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/download/v1.5.2/Meshy-Importer-Blender.zip) | Preferences > Extensions or Add-ons > Install from Disk |
| Godot 4.2+ | [Godot ZIP](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/download/v1.5.2/Meshy-Importer-Godot.zip) | Unzip into project and enable the addon |
| Unreal 5.3+ | [Unreal ZIP](https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/download/v1.5.2/Meshy-Importer-Unreal.zip) | Unzip into the project's Plugins folder and restart |

Normal `.glb`, `.fbx` and `.obj` files from Meshy's ordinary Download action are already supported by your 3D tools. This importer is specifically for Meshy's `.meshy` payload. See [how to obtain it](GETTING_A_MESHY_FILE.md).

## Import and conversion

- **Unity:** Drop a `.meshy` into `Assets/`. Configure scale, UV repair, materials, colliders, optimisation and editable copies in the Inspector. [Unity guide](unity/README.md).
- **Blender:** File > Import > Meshy Model (.meshy), with repair and optional GLB export settings. [Blender guide](blender/meshy_blender_importer/README.md).
- **Godot:** Add `.meshy` files to the FileSystem dock after enabling the plugin. [Godot guide](godot/README.md).
- **Unreal:** Tools > Meshy > Import .meshy Files, or use the optional auto-import inbox. [Unreal guide](unreal/README.md).
- **Command line:** Use Python 3.9+ with `cd core/python && python -m meshy_core path/to/model.meshy` to produce an ordinary GLB.

## Privacy

FISHHWB model capture, decoding and conversion run on the user's device. No model content, source asset or credential is uploaded by these tools. The Meshy website still communicates with Meshy's own servers to display your models. Engine update checks can optionally request public GitHub release metadata. Downloading/installing from GitHub also communicates with GitHub. [Security policy](SECURITY.md).

## Guides and support

[Compatibility](COMPATIBILITY.md) | [Troubleshooting](TROUBLESHOOTING.md) | [glTFast dependency repair](UNITY_GLTFast_REPAIR.md) | [Changelog](CHANGELOG.md) | [Publishing and store submission](PUBLISHING.md) | [Contributing](CONTRIBUTING.md) | [Release checklist](RELEASE_CHECKLIST.md)

[Discord](https://discord.gg/vCcsnX4HQP) | [Patreon](https://www.patreon.com/cw/DedZed)

This is an unofficial project and is not affiliated with or endorsed by Meshy. Use only models you are authorised to access.
