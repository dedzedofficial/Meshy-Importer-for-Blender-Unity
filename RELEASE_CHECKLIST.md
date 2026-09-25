# Release checklist

Use this checklist before publishing a release.

- [ ] Pick the new version and update every file listed by `python tools/check_versions.py <new-version>`, including the Unreal `.uplugin` integer `Version`.
- [ ] `python tools/check_versions.py` reports that all versions agree.
- [ ] If Unity decode/build behavior changed, bump the integer in `[ScriptedImporter(...)]`.
- [ ] If the UV-repair algorithm changed, update the Python, C# and GDScript ports together. `python tests/cross_check_uv.py` must pass.
- [ ] Update `CHANGELOG.md`.
- [ ] Update compatibility/support notes if host versions changed.
- [ ] `python -m unittest discover -s tests` passes.
- [ ] `dotnet build tests/csharp/unity-compile` succeeds.
- [ ] Rebuild the ZIPs with `python tools/build_zips.py`. `--check` must pass.
- [ ] Run the repository validation workflow. It covers the checks above plus headless Blender and Godot imports.
- [ ] Test a real `.meshy` payload in the supported Unity version, including an existing cached asset after an importer-version bump.
- [ ] Test a real `.meshy` payload in Blender, Godot and Unreal.
- [ ] Create a GitHub release with the Blender ZIP, the Unreal ZIP and a ZIP of `godot/addons`.
