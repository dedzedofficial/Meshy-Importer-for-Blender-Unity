# Troubleshooting

## Unity

### I dropped a `.meshy` file into Assets and nothing happened
1. Confirm the file is a real Meshy payload and starts with `MESHY.AI`.
2. Open **Tools > Meshy > Validate Installation**.
3. Install the compatible UnityGLTF version from **Tools > Meshy > Install UnityGLTF (Compatible Version)**.
4. Select the `.meshy` file and use **Reimport** in its Inspector, or **Tools > Meshy > Reimport Selected .meshy**.
5. Check the Console for a precise decoder/import error.

### I get “Missing MESHY.AI header”
The file is probably not the model response. Do not rename GLB, FBX, OBJ, HTML, or JSON files to `.meshy`. Re-capture the actual model response in Chrome DevTools Network.

### The GLB is generated but the model is not visible
UnityGLTF is the GLB importer used by this package. Confirm UnityGLTF is installed and check its importer settings and the Unity Console.

### Meshy changed its web format
The `.meshy` container is an undocumented web payload and may change without notice. If the decoder reports an invalid GLB after a Meshy site change, keep the original payload and report the failure with the Unity/Blender version and importer version.

## Blender

- Blender 4.2+: install the bundled ZIP with **Preferences > Extensions > Install from Disk**.
- Blender 3.6–4.1: use the legacy **Preferences > Add-ons > Install...** route.
- Use **File > Import > Meshy Model (.meshy)**.
- If Blender reports an invalid GLB, validate the original payload and try Blender 4.2+ or a current LTS.

## Support

- GitHub: https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity
- Patreon: https://www.patreon.com/cw/DedZed
- Discord: https://discord.gg/vCcsnX4HQP
