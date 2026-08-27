# Changelog

## 1.1.0
- Fixed the AES key expansion crash (`list index out of range`) caused by an incorrect 32-byte key.
- Fixed Unity Package Manager dependency validation: UnityGLTF is no longer declared as an invalid in-package Git URL dependency. It's installed via **Tools → Meshy → Install UnityGLTF 2.21.0**, which adds it to the project's `Packages/manifest.json` directly.
- Added FISHHWB Discord support link.
- Added modification/support disclaimer.
- Corrected the root README's Unity install instructions to match the actual (manual) UnityGLTF install step.
- Fixed the Unity "About FISHHWB Meshy Importer" dialog printing literal `\n` characters instead of line breaks.
- Removed a dead, unused, and actually-broken key-expansion helper left over in the Blender add-on (it was never called, and would have raised a `TypeError` if it had been).
- Clarified `unity/Runtime/MeshyDecoder.cs`: it now fails immediately with a clear message pointing to the Editor converter, instead of reading and parsing the whole file before throwing from a non-functional stub.
