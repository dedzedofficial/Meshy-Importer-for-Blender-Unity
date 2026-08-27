# FISHHWB Meshy .meshy Importer for Unity

**Created by FISHHWB — v1.1.1**

This is a Unity Package Manager package for importing Meshy `.meshy` containers.

## Install directly from GitHub

This repository is structured so the Unity package lives in `/unity`.

In Unity:

1. Open **Window → Package Manager**.
2. Click **+**.
3. Choose **Add package from git URL...**
4. Paste:

```text
https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity.git?path=/unity
```

5. Click **Add**.

Unity supports Git package URLs and repository subfolder paths when the selected subfolder contains a `package.json`. Git must be installed and available to Unity.

## UnityGLTF is installed automatically

The package declares UnityGLTF 2.21.0 as a Git dependency:

```text
https://github.com/KhronosGroup/UnityGLTF.git#release/2.21.0
```

You **do not need to install UnityGLTF manually**.

UnityGLTF 2.21.0 supports Unity 2021.3+ and is itself a UPM-compatible Git package.

## Import a `.meshy`

After the package is installed:

**Tools → Meshy → Convert .meshy to GLB...**

Select the `.meshy` file.

The importer:

1. Reads the `.meshy` container.
2. Decodes the GLB data locally.
3. Writes a `.glb` beside the source file.
4. Refreshes Unity's AssetDatabase.
5. Lets UnityGLTF import the resulting GLB.

The original `.meshy` file is not modified.

## Batch import

Put your `.meshy` files inside the Unity project's `Assets` folder and use:

**Tools → Meshy → Convert All .meshy In Assets**

## Requirements

- Unity **2021.3 LTS or newer**
- Git installed and available in PATH
- Internet access on first package installation so Unity can fetch the Git dependencies
- UnityGLTF 2.21.0 is pulled automatically by the package

## Important

The `.meshy` decoder is based on the current reverse-engineered `.meshy` container format. If Meshy changes that format, a future importer update may be required.

**Created by FISHHWB**


## Support & Disclaimer

Official FISHHWB support/community Discord: https://discord.gg/vCcsnX4HQP

**Important:** FISHHWB is responsible only for the original, unmodified release. Any modifications, patches, forks, replacements, or other changes made by anyone other than FISHHWB are the responsibility of the person making those changes. FISHHWB is not responsible for bugs, errors, crashes, compatibility problems, data loss, or other issues caused by third-party changes. Please use the official unmodified release when requesting support. See the repository `DISCLAIMER.md` for the full notice.
