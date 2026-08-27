# Meshy .meshy Importer for Blender - v1.1.0

Adds:

**File → Import → Meshy Model (.meshy)**

The add-on reads the Meshy `.meshy` container, decrypts its encrypted GLB prefix locally, reconstructs the GLB, fixes the GLB total-length field, and hands it to Blender's native glTF importer.

## Blender compatibility
Blender 5.2+ is recommended. Blender 5.2 added glTF meshopt support, which is important because Meshy's decrypted GLB uses `EXT_meshopt_compression`.

## Install
1. Blender → Edit → Preferences → Add-ons.
2. Choose **Install from Disk**.
3. Select `meshy_blender_importer_v1.1.0.zip`.
4. Enable **Meshy .meshy Importer**.

## Use
File → Import → Meshy Model (.meshy), then select your `.meshy` file.

## What was fixed in v1.1.0
The previous build accidentally omitted the leading `JSON` from the fixed 32-byte AES key. That caused the internal AES key expansion to throw:

`list index out of range`

The key is now the full literal documented by the reverse-engineered format:

`JSON{"accessors":[{"bufferView":`

The reconstructed GLB's total-length field is also corrected before Blender imports it.

## Format
The current reverse-engineered format describes:
- `MESHY.AI` magic at bytes 0–7
- 12-byte nonce at bytes 10–21
- 8192-byte AES-CTR encrypted prefix
- 16-byte authentication tag
- plaintext remainder
- `EXT_meshopt_compression` in the resulting GLB

Reference implementation:
https://github.com/Amal-David/meshy2glb

Everything is processed locally by the add-on; the model is not uploaded.


## Support & Disclaimer

Official FISHHWB support/community Discord: https://discord.gg/vCcsnX4HQP

**Important:** FISHHWB is responsible only for the original, unmodified release. Any modifications, patches, forks, replacements, or other changes made by anyone other than FISHHWB are the responsibility of the person making those changes. FISHHWB is not responsible for bugs, errors, crashes, compatibility problems, data loss, or other issues caused by third-party changes. Please use the official unmodified release when requesting support. See the repository `DISCLAIMER.md` for the full notice.
