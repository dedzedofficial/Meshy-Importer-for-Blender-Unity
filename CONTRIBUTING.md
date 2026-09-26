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
- Keep `bl_info` and `blender_manifest.toml` versions synchronized (all hosts share one version; see `tools/check_versions.py`).
- The add-on must keep working on Blender 3.6 (legacy add-on) and 4.2+ (extension).

For Godot changes:
- Target Godot 4.2+. Keep scripts `@tool` and statically typed where GDScript's inference needs it.
- `python tests/hosts/godot_smoke.py <godot>` must pass.

For Unreal changes:
- Keep the plugin Editor-Python only and Python 3.9 compatible (Unreal 5.3 ships Python 3.9).
- `tests/test_unreal_plugin.py` drives it against a stub `unreal` module. Test real imports in the editor.

Shared code:
- `core/python/meshy_core` is the reference implementation of decoding, meshopt, WebP and UV repair. Unity (C#) and Godot (GDScript) carry ports of it; change them together. That includes the "wrong file" messages (`decode.py describe_wrong_file`, `MeshyFileCheck.cs`, `meshy_decrypt.gd`).
- Error messages are for players, not programmers: say what happened and what to do, in one or two plain sentences, and end with `Help: <TROUBLESHOOTING.md#anchor>` when a section covers it.
- The Blender and Unreal ZIPs bundle `meshy_core`. Rebuild them with `python tools/build_zips.py`; never edit a ZIP by hand.
- Never commit real Meshy payloads. `tests/meshy_fixtures.py` builds synthetic ones.

## Running the checks locally

```text
python -m unittest discover -s tests          # Python core + Unreal plugin (pip install pillow for the WebP comparison tests)
python tools/check_versions.py                # versions agree everywhere
python tools/build_zips.py --check            # committed ZIPs are current
dotnet build tests/csharp/unity-compile       # Unity package type-checks against UnityEngine
python tests/cross_check_uv.py                # C# / GDScript UV repair match the Python reference (needs dotnet / godot)
python tests/cross_check_wrong_file.py        # C# / GDScript "wrong file" messages match the Python reference
blender -b --factory-startup --python tests/hosts/blender_smoke.py -- blender <file.meshy>
python tests/hosts/godot_smoke.py <path-to-godot>
```

Please update `CHANGELOG.md` for user-visible changes.
