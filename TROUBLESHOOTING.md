# Troubleshooting

## Unity

### I dropped a `.meshy` file into Assets and nothing happened
1. Confirm the file is a real Meshy payload and starts with `MESHY.AI`.
2. Open **Tools > Meshy > Validate Installation**.
3. Select the `.meshy` file and use **Reimport** in its Inspector, or **Tools > Meshy > Reimport Selected .meshy**.
4. Check the Console for a precise decoder/import error.
5. If the Console shows "Native import not available for this file", the payload uses a glTF extension outside the native builder's coverage (meshopt geometry and WebP textures -- the combination real Meshy exports use -- are both handled natively, so this is rare). Run **Tools > Meshy > Install UnityGLTF (Optional Fallback)** so that specific file can still import via the `.glb` fallback path.

### I get “Missing MESHY.AI header”
The file is probably not the model response. Do not rename GLB, FBX, OBJ, HTML, or JSON files to `.meshy`. Re-capture the actual model response in Chrome DevTools Network.

### The model imports but materials/textures look off
The native importer repacks glTF's metallic/roughness texture channels for Unity and targets URP `Lit` or built-in `Standard` depending on your render pipeline. Normal maps are applied as raw tangent-space textures without Unity's "Normal map" import flag (there's no `TextureImporter` for an in-memory sub-asset), so they can look slightly different from an FBX-imported normal map -- this is a known limitation, not a bug in your file.

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
