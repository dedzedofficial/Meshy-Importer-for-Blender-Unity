# Contributing

Thanks for helping improve Meshy Importer for Blender & Unity.

## Before opening an issue

- Make sure you are using the latest release.
- Check `TROUBLESHOOTING.md` and `COMPATIBILITY.md`.
- Keep the original `.meshy` payload unchanged when reporting an import problem.
- Include the importer version, Unity/Blender version, operating system, and a short description of what happened.

## Pull requests

Keep changes focused and avoid unrelated formatting changes.

For Unity changes:
- Keep the package dependency-free on the normal native import path.
- If the decode/build pipeline changes, bump the `ScriptedImporter` version in `MeshyScriptedImporter.cs` so cached `.meshy` assets are rebuilt.
- Avoid unnecessary `AssetDatabase.Refresh()` calls during import.

For Blender changes:
- Keep the add-on dependency-free where practical.
- Preserve existing Meshy UVs and authored material data unless a change explicitly requires otherwise.
- Keep `bl_info` and `blender_manifest.toml` versions synchronized.

Please update `CHANGELOG.md` for user-visible changes.
