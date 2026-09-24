"""Shared, dependency-free .meshy decoding core (Meshy Importer for Blender, Unity, Godot & Unreal).

Used by the Blender add-on, the Unreal plugin and the command-line converter
(`python -m meshy_core in.meshy out.glb`). The Unity (C#) and Godot (GDScript)
importers carry their own ports of the same code.
"""

__version__ = "1.4.1"

from .decode import MeshyFormatError, decode_meshy_bytes, decode_meshy_file  # noqa: F401
from .normalize import NormalizeOptions, normalize_glb, normalize_meshy_bytes, normalize_meshy_file  # noqa: F401
from .uv_repair import repair_uvs  # noqa: F401
