# Release checklist

Use this checklist before publishing a release.

- [ ] Update the Unity package version in `unity/package.json`.
- [ ] Update the Blender version in `blender/meshy_blender_importer/blender_manifest.toml`.
- [ ] Keep Blender `bl_info` in `__init__.py` synchronized with the manifest.
- [ ] If Unity decode/build behavior changed, bump the integer in `[ScriptedImporter(...)]`.
- [ ] Update `CHANGELOG.md`.
- [ ] Update compatibility/support notes if host versions changed.
- [ ] Run the repository validation workflow.
- [ ] Test a real `.meshy` payload in the supported Unity version.
- [ ] Test a real `.meshy` payload in the supported Blender version.
- [ ] Test an existing cached Unity `.meshy` asset after an importer-version bump.
- [ ] Rebuild the bundled Blender ZIP.
- [ ] Create a GitHub release with the repository ZIP and Blender ZIP when appropriate.
