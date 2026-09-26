# Compatibility

All hosts ship as version **1.5.0** and decode `.meshy` payloads locally.

## Unity

| Unity | Status | Notes |
|---|---|---|
| 2020.3 LTS | Supported | Native importer; optional UnityGLTF fallback 2.9.1-rc |
| 2021.3 LTS | Supported | Native importer; optional UnityGLTF fallback 2.21.0 |
| 2022.3 LTS | Supported | Native importer; optional UnityGLTF fallback 2.21.0 |
| Unity 6+ | Supported | Native importer; optional UnityGLTF fallback 2.21.0 |

The native importer needs no other packages. UnityGLTF is only used for a payload that requires a glTF extension the native builder does not implement.

| Render pipeline | Status |
|---|---|
| Built-in | Full (`Standard`) |
| URP | Full (`Universal Render Pipeline/Lit`) |
| HDRP | Full (`HDRP/Lit`, with metallic, occlusion and smoothness packed into the mask map) |

Non-LTS Unity versions may work but are not the recommended support target.

## Blender

| Blender | Status | Install method |
|---|---|---|
| 3.6 LTS | Supported (tested headless) | Legacy Add-on |
| 4.0–4.1 | Supported | Legacy Add-on |
| 4.2+ | Supported (tested headless) | Extension / Install from Disk |
| 5.x | Supported | Extension / Install from Disk |

The add-on decodes Meshy's `EXT_meshopt_compression` geometry itself, because Blender's glTF importer rejects it. Blender versions before 4.0 also get WebP textures converted to PNG. Drag-and-drop import needs Blender 4.1+.

## Godot

| Godot | Status |
|---|---|
| 4.2 | Supported (tested headless on 4.2.2) |
| 4.3+ | Supported (tested headless on 4.3) |
| 3.x | Not supported |

Godot's glTF importer implements neither `EXT_meshopt_compression` nor `KHR_mesh_quantization`, so the plugin converts both before handing the model over.

## Unreal Engine

| Unreal | Status |
|---|---|
| 5.3+ | Supported (Editor Python plugin; the Python Editor Script Plugin is enabled automatically; needs the Interchange/glTF importer) |
| 5.0–5.2, 4.27 | Not supported |

Meshy's meshopt geometry, quantized attributes, texture transforms and WebP textures are converted to plain glTF before Unreal's glTF (Interchange) importer runs. Install Pillow into the editor's Python for faster WebP decoding.

## Command-line converter

`python -m meshy_core` needs Python 3.9 or newer and no packages. Pillow is optional and makes WebP decoding faster.

## Recommended

For the least friction: **Unity 2021.3/2022.3 LTS or Unity 6, Blender 4.2+, Godot 4.3+ or Unreal Engine 5.4+**.

Very new Meshy payloads may use glTF features that older host applications cannot import.
