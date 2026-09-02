# Meshy Importer for Unity — v1.1.1

## What it does

Drop a real Meshy `.meshy` payload into your Unity project's `Assets` folder. The package automatically detects it, decodes it locally, writes the GLB companion, and lets UnityGLTF import the model.

### One-time setup

1. Install this package from Unity Package Manager.
2. Choose **Tools → Meshy → Install UnityGLTF (Compatible Version)**. The importer selects UnityGLTF 2.9.1-rc on Unity 2020.3 and 2.21.0 on Unity 2021.3+.
3. Wait for Unity Package Manager to finish.
4. Drop `model.meshy` into `Assets/`.

After that, normal `.meshy` files can be added by drag-and-drop. The generated `.glb` is the bridge asset used by UnityGLTF.

## Getting the `.meshy` payload

Meshy's regular download options are not the same thing as the `.meshy` container used by this importer. To obtain the payload used here, open the model in Chrome, press **F12**, open **Network**, reload the model, and search/filter for **`model`**. Save the actual model response as `.meshy`.

Do not rename a GLB/FBX/OBJ/HTML/JSON response to `.meshy`. A valid payload begins with `MESHY.AI`.

## Manual tools

- **Tools → Meshy → Convert `.meshy` to GLB...**
- **Tools → Meshy → Convert All `.meshy` In Assets**
- **Tools → Meshy → Install UnityGLTF (Compatible Version)**
- **Tools → Meshy → Support / Donate on Patreon**

## Support

Patreon: https://www.patreon.com/cw/DedZed

GitHub: https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity

## Unity compatibility

- Unity 2020.3 LTS: supported with UnityGLTF 2.9.1-rc
- Unity 2021.3 LTS: supported with UnityGLTF 2.21.0
- Unity 2022.3 LTS: supported with UnityGLTF 2.21.0
- Unity 6+: supported with UnityGLTF 2.21.0

For the best supported path, use a current Unity LTS release. See `../COMPATIBILITY.md` for details.
