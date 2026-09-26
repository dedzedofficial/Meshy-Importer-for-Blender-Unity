# Release checklist

Use this checklist before publishing a release.

- [ ] Pick the new version and update every file listed by `python tools/check_versions.py <new-version>`, including the Unreal `.uplugin` integer `Version`.
- [ ] `python tools/check_versions.py` reports that all versions agree.
- [ ] If Unity decode/build behavior changed, bump the integer in `[ScriptedImporter(...)]`.
- [ ] If the UV-repair algorithm changed, update the Python, C# and GDScript ports together. `python tests/cross_check_uv.py` must pass.
- [ ] If the "wrong file" messages changed, update all three ports. `python tests/cross_check_wrong_file.py` must pass.
- [ ] Update `CHANGELOG.md`.
- [ ] Update compatibility/support notes if host versions changed.
- [ ] `python -m unittest discover -s tests` passes.
- [ ] `dotnet build tests/csharp/unity-compile` succeeds.
- [ ] Rebuild the ZIPs with `python tools/build_zips.py`. `--check` must pass.
- [ ] Run the repository validation workflow. It covers the checks above plus headless Blender and Godot imports.
- [ ] Test a real `.meshy` payload in the supported Unity version, including an existing cached asset after an importer-version bump.
- [ ] Test a real `.meshy` payload in Blender, Godot and Unreal.
- [ ] Merge to `main`, then push the tag (`git tag vX.Y.Z && git push origin vX.Y.Z`). The Release workflow builds the Blender, Unreal and Godot ZIPs and publishes the GitHub release with the CHANGELOG section as notes.
- [ ] Upload the new Blender ZIP to extensions.blender.org and update the Godot Asset Library entry, if listed (see `PUBLISHING.md`).
