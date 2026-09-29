# Meshy Importer 1.5.1 — install and fix the glTFast conflict

Your screenshot shows the original **com.atteneder.gltfast** and Unity's
**com.unity.cloud.gltfast** installed at the same time. They contain matching asset
GUIDs. Both must not remain installed together. Changing GUIDs or deleting only the
package cache does not resolve the dependency configuration.

Meshy 1.5.1 has **no glTFast or UnityGLTF package dependency**. Updating Meshy adds
checks and import fixes, but cannot remove packages from a Unity project that was
not supplied. Resolve the existing duplicate separately as below.

## 1. Resolve the duplicate in your Unity project

Use Package Manager's **In Project** view and inspect which other packages depend
on each glTFast variant. Keep the variant those packages require. If neither is
required by another package, keep `com.unity.cloud.gltfast` at its already installed
version and remove `com.atteneder.gltfast`. If something requires the legacy variant,
keep legacy and remove Unity's variant instead. If different packages require both,
update/migrate those dependent packages to agree on one variant first.

Unity's upstream migration guide:
https://github.com/Unity-Technologies/com.unity.cloud.gltfast/blob/main/Packages/com.unity.cloud.gltfast/Documentation~/UpgradeGuides.md

### If compiler errors prevent using the Editor

Close Unity. The included `tools/repair_gltfast.py` works outside Unity and needs
Python 3.9 or newer. From a terminal in this extracted download, preview:

```powershell
py -3 tools/repair_gltfast.py "C:\Path\To\YourUnityProject"
```

If the preview finds no dependency blockers, apply:

```powershell
py -3 tools/repair_gltfast.py "C:\Path\To\YourUnityProject" --keep unity --apply
```

Use `--keep legacy` to preserve the original variant instead. On macOS/Linux, use
`python3` instead of `py -3`. The project path is the folder containing `Assets`,
`Packages` and `ProjectSettings`, not the Meshy package source folder.

The utility:

- Checks the dependency graph and local/embedded package manifests.
- Refuses removal if another active package requires that variant.
- Refuses automatic repair for an embedded or indirect-only target, missing lock
  data, a changed configuration, or an Editor lock file.
- Backs up both original JSON files under `MeshyPackageBackups/<timestamp>/`.
- Removes only the selected direct manifest dependency. It retains the other
  variant's version, other dependencies and scoped registries.
- Leaves `packages-lock.json` for Unity to regenerate when the project reopens.
  It never deletes assets, `.meta` files, or caches.

If it reports a parent package, update that parent or choose the compatible
variant. Do not repeatedly remove a dependency that the parent will reinstall.
If an embedded variant is reported, inspect/move that package deliberately;
the utility never deletes embedded source folders. If a stale `UnityLockfile`
remains after a crash, verify no Editor process is using this project before
removing that stale lock manually.

To undo a repair, close Unity and copy both backed-up JSON files back into the
project's `Packages` folder. Reopening Unity will restore the old configuration
(including the old collision if both variants were there).

## 2. Install this local 1.5.1 build

1. Extract the download to a stable location outside your Unity project's `Assets`.
2. In Unity Package Manager, select **+ > Add package from disk** and choose
   **unity/package.json** from this download. Keep this folder in place.
3. If Meshy was manually copied into `Assets`, remove that old Meshy code copy first
   so only one copy of the importer remains. Retain your model/source assets.
4. If using an embedded copy under `Packages/com.fishhwb.meshy-importer`, replace its
   package files with the contents of this download's `unity` folder, retaining the
   supplied `.meta` files. Do not install a second copy.
5. Wait for package resolution and compilation. Use **Tools > Meshy > Help >
   Validate Installation**. Then **Tools > Meshy > Reimport All .meshy In Assets**.

For a Git install, add `https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity.git?path=/unity` in Package Manager. Use a version tag if you need to pin a published release.

Ordinary `.meshy` imports work without either external importer. Unsupported native
features generate a companion GLB, whose external import status must be checked in
Unity. Installing UnityGLTF alongside an already configured glTFast is no longer
suggested by Meshy's install action.

## 3. Verify in Unity

- Confirm only one glTFast package ID remains and the screenshot's GUID conflicts
  disappear after Package Manager resolves. Do not edit third-party GUIDs.
- Import a normal `.meshy`; check meshes, materials, textures and skinning.
- Import a multi-primitive model and one with repeated node/material names; confirm
  its child objects survive reimport and reopening the project.
- For an unsupported required extension, check the generated GLB is imported and
  the `.meshy` Inspector remains selectable. Test with no external importer too;
  it should explain that the GLB needs a fallback importer.
- Place a user-created GLB beside the source and run Convert All: it must be kept;
  the export should use a numbered `_export_` filename instead.
- Cancel Reimport All part-way through; the count should reflect files processed.

## Validation in this delivery

The repository validation workflow runs a C# type-check and executable preflight
checks, but Unity Editor imports, rendering and Inspector actions still need
testing in your project.

## New workflow features (same 1.5.1 version)

Read `WORKFLOW_1.5.1.md` for material overrides, editable asset copies, remembered
settings, preflight scanning and expanded import summaries. Install this revised
archive over the earlier 1.5.1 package. The internal cache revision is now 9, so
Unity will rebuild cached `.meshy` imports after the update.
