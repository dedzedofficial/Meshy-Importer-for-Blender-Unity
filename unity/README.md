# FISHHWB Meshy Importer for Unity

## Installation

### 1. Install UnityGLTF first

This package requires **UnityGLTF 2.21.0**. Unity Package Manager does not accept a Git URL as the version value of a package dependency, so UnityGLTF must be added to the Unity project separately before installing this importer.

In Unity:

1. Open **Window > Package Manager**.
2. Click **+**.
3. Select **Add package from git URL**.
4. Enter:

`https://github.com/KhronosGroup/UnityGLTF.git#release/2.21.0`

5. Click **Add** and wait for UnityGLTF to finish installing.

UnityGLTF's own documentation confirms that Git installation and release tags are supported. See the official UnityGLTF documentation: https://github.com/KhronosGroup/UnityGLTF

### 2. Install FISHHWB Meshy Importer

After UnityGLTF is installed, add this package through **Window > Package Manager > + > Add package from git URL**:

`https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity.git?path=/unity`

The package declares UnityGLTF as version `2.21.0`, allowing Unity's Package Manager to validate the dependency correctly.

## Important

If Unity reports that `org.khronos.unitygltf` is missing, install UnityGLTF using the Git URL above before installing this package.

## Support / Discord

For support, updates, and community discussion, join the FISHHWB Discord:

https://discord.gg/vCcsnX4HQP

## Modification Disclaimer

FISHHWB provides support for the original, unmodified version of this package. If you modify, replace, patch, fork, redistribute, or otherwise alter any part of the system, FISHHWB is not responsible for problems caused by those changes.

Issues resulting from third-party modifications, incompatible package versions, altered scripts, manual edits, unsupported Unity versions, or other changes outside the original release are not the responsibility of FISHHWB.

If you encounter an issue, please reproduce it using the original, unmodified release before reporting it as a FISHHWB issue.
