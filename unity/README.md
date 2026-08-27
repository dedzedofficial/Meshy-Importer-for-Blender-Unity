# FISHHWB Meshy Importer for Unity

## Installation

### 1. Install this package

In Unity, open **Window > Package Manager > + > Add package from git URL** and enter:

`https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity.git?path=/unity`

This package intentionally has **no UnityGLTF package dependency in its `package.json`**. Unity does not support Git dependencies between packages; Git dependencies must be declared by the Unity project itself.

### 2. Install UnityGLTF

After the FISHHWB importer has been added, use:

**Tools > Meshy > Install UnityGLTF 2.21.0**

The importer will add the official UnityGLTF Git dependency to your project's `Packages/manifest.json`:

`https://github.com/KhronosGroup/UnityGLTF.git#release/2.21.0`

UnityGLTF is required to turn the generated `.glb` file into a usable Unity model. The FISHHWB importer itself only decodes the `.meshy` container into GLB.

You can also add UnityGLTF manually through Package Manager using the same Git URL.

### 3. Import a model

Use **Tools > Meshy > Convert .meshy to GLB...** for an individual model, or **Tools > Meshy > Convert All .meshy In Assets** for models already inside your Unity project's `Assets` folder.

The resulting `.glb` will be imported by UnityGLTF.

### Important

If UnityGLTF is not installed, the FISHHWB importer can still convert the `.meshy` file to `.glb`, but Unity will not have a glTF importer available to turn that GLB into a Unity model. Install UnityGLTF first or use **Tools > Meshy > Install UnityGLTF 2.21.0**.

## Support / Discord

For support, updates, and community discussion, join the FISHHWB Discord:

https://discord.gg/vCcsnX4HQP

## Modification Disclaimer

FISHHWB provides support for the original, unmodified version of this package. If you modify, replace, patch, fork, redistribute, or otherwise alter any part of the system, FISHHWB is not responsible for problems caused by those changes.

Issues resulting from third-party modifications, incompatible package versions, altered scripts, manual edits, unsupported Unity versions, or other changes outside the original release are not the responsibility of FISHHWB.

If you encounter an issue, please reproduce it using the original, unmodified release before reporting it as a FISHHWB issue.
