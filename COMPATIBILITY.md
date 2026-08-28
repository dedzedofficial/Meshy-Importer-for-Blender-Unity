# Compatibility

## Unity

| Unity | Status | UnityGLTF path |
|---|---|---|
| 2020.3 LTS | Supported | UnityGLTF 2.9.1-rc / older compatible release |
| 2021.3 LTS | Supported | UnityGLTF 2.21.0 |
| 2022.3 LTS | Supported | UnityGLTF 2.21.0 |
| Unity 6+ | Supported | UnityGLTF 2.21.0 |

UnityGLTF's current documentation recommends LTS releases 2021.3+, 2022.3+, and Unity 6+, and identifies 2020.3 as a legacy path that should use an older UnityGLTF version.

Non-LTS Unity versions may work but are not the recommended support target.

## Blender

| Blender | Status | Install method |
|---|---|---|
| 3.6 LTS | Supported | Legacy Add-on |
| 4.0–4.1 | Supported | Legacy Add-on |
| 4.2+ | Supported | Extension / Install from Disk |
| 5.x | Supported | Extension / Install from Disk |

Blender 4.2 introduced the Extensions system; legacy add-ons remain supported for a transition period.

## Recommended

For the least friction: **Unity 2021.3/2022.3 LTS or Unity 6 + Blender 4.2+**.

Very new Meshy payloads may use glTF features that older host applications cannot import.
