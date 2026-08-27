# FISHHWB Meshy Importer for Blender & Unity

**Created by FISHHWB**

Import Meshy `.meshy` containers into Blender and Unity.

## GitHub

Repository:

https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity

## Unity - Git Loader

Install the Unity package directly from Unity Package Manager:

```text
https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity.git?path=/unity
```

Go to **Window → Package Manager → + → Add package from git URL...**

Unity does not support Git dependencies declared inside another package, so UnityGLTF is **not** pulled in automatically. After installing this package, use **Tools → Meshy → Install UnityGLTF 2.21.0** (or add the UnityGLTF Git URL to your project manually) before importing any models.

## Unity usage

After installation:

**Tools → Meshy → Install UnityGLTF 2.21.0** (one-time setup)

**Tools → Meshy → Convert .meshy to GLB...**

or:

**Tools → Meshy → Convert All .meshy In Assets**

The converter works locally and creates a GLB for UnityGLTF to import.

## Blender usage

Install the Blender add-on from:

```text
blender/meshy_blender_importer/
```

Then use:

**File → Import → Meshy Model (.meshy)**

## How to get a `.meshy`

`.meshy` is a Meshy container format rather than a normal public Meshy export option. Do not simply rename a `.glb` to `.meshy`; the container structure is different.

## Attribution

This project and the included integration package are marked as:

**Created by FISHHWB**

Machine-readable attribution is stored in `FISHHWB_METADATA.json`.

## Community & Support

FISHHWB Discord: https://discord.gg/vCcsnX4HQP

For support, updates, discussion, and issue reports, please use the official Discord.

## Disclaimer

**Created and maintained by FISHHWB.** FISHHWB is responsible only for the original, unmodified files released by FISHHWB. If any part of this system is modified, replaced, patched, extended, forked, recompiled, or otherwise changed by anyone other than FISHHWB, those changes are not the responsibility of FISHHWB. Any bugs, errors, crashes, compatibility problems, data loss, or other issues caused by or resulting from third-party modifications are the responsibility of the person or party who made those changes.

FISHHWB cannot guarantee support for modified or unofficial versions. Please see `DISCLAIMER.md` for the full notice.

## Compatibility

The `.meshy` decoding method is based on the currently understood/reverse-engineered container structure. If Meshy changes that structure, the decoder may need an update.

## Version

**v1.1.0**
