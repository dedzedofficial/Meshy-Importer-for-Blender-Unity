# 1.5.1 validation — workflow update

Executed in the delivery environment:

- 66 Python unit tests passed, including the glTFast repair regression cases.
- 22 executable C# preflight/material-key checks passed, including a synthetic
  meshopt + WebP payload, invalid container/range/reference cases, node cycles,
  unsupported-feature fallback routing and stable unique-name material keys.
- The full Unity package type-checked with the C# 8 compiler against UnityEngine
  2021.3.33 reference assemblies and the included UnityEditor API stubs, with
  warnings treated as errors (existing unused-field suppressions retained).
- Seven C# package-diagnostics checks passed for native-only, single glTFast,
  indirect/embedded collisions, indirect UnityGLTF and malformed configuration.
- All host version declarations remain 1.5.1; JSON and assembly definitions parse,
  Unity meta GUIDs are unique, and bundled ZIPs match source.

Compilation was performed using the .NET 8 SDK's Roslyn compiler directly because
this environment could not run the dotnet CLI's process-information probe. This
checks C# syntax and types; it does not execute Unity's native engine APIs.

Not executed:

- Unity Editor imports, Inspector interaction, rendering and prefab persistence.
- The three new Unity EditMode workflow tests (instructions in tests/unity/README.md).
- Blender, Godot and Unreal host applications.
- Repair of the user's actual project dependency graph (project manifest not supplied).

Manual checks still required in Unity: create/reopen an editable copy, edit copied
materials then reimport the source, restore successful settings after a failed
import, delete/recover a mapped material, and verify material choices after a
source revision. See WORKFLOW_1.5.1.md for behavior and limitations.
