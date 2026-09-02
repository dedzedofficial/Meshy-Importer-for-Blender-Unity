bl_info = {
    "name": "Meshy Importer for Blender & Unity",
    "author": "FISHHWB",
    "version": (1, 2, 0),
    "blender": (3, 6, 0),
    "location": "File > Import > Meshy Model (.meshy)",
    "description": "Imports Meshy .meshy containers locally through Blender's native GLB importer.",
    "category": "Import-Export",
}

import bpy
import os
import struct
import tempfile
from bpy_extras.io_utils import ImportHelper
from bpy.props import StringProperty
from bpy.types import Operator

# Meshy's current .meshy wrapper uses this fixed AES-256 key.
_KEY = b'JSON{"accessors":[{"bufferView":'
_MAGIC = b"MESHY.AI"
_HEADER_SIZE = 32
_ENCRYPTED_SIZE = 8192
_TAG_SIZE = 16


# ---- Small dependency-free AES-256 implementation -------------------------
# Adapted from the AES specification; used only for AES-CTR decryption so the
# add-on does not require pip packages inside Blender.

_SBOX = [
99,124,119,123,242,107,111,197,48,1,103,43,254,215,171,118,
202,130,201,125,250,89,71,240,173,212,162,175,156,164,114,192,
183,253,147,38,54,63,247,204,52,165,229,241,113,216,49,21,
4,199,35,195,24,150,5,154,7,18,128,226,235,39,178,117,
9,131,44,26,27,110,90,160,82,59,214,179,41,227,47,132,
83,209,0,237,32,252,177,91,106,203,190,57,74,76,88,207,
208,239,170,251,67,77,51,133,69,249,2,127,80,60,159,168,
81,163,64,143,146,157,56,245,188,182,218,33,16,255,243,210,
205,12,19,236,95,151,68,23,196,167,126,61,100,93,25,
115,96,129,79,220,34,42,144,136,70,238,184,20,222,94,11,
219,224,50,58,10,73,6,36,92,194,211,172,98,145,149,228,
121,231,200,55,109,141,213,78,169,108,86,244,234,101,122,
174,8,186,120,37,46,28,166,180,198,232,221,116,31,75,
189,139,138,112,62,181,102,72,3,246,14,97,53,87,185,134,
193,29,158,225,248,152,17,105,217,142,148,155,30,135,
233,206,85,40,223,140,161,137,13,191,230,66,104,65,
153,45,15,176,84,187,22
]
_RCON = [0,1,2,4,8,16,32,64,128,27,54,108,216,171,77]

def _gmul(a, b):
    r = 0
    for _ in range(8):
        if b & 1: r ^= a
        a = ((a << 1) ^ 0x11B) if a & 0x80 else (a << 1)
        b >>= 1
    return r & 255

# Return round keys as 16-byte chunks.
def _round_keys(key):
    nk, nb, nr = 8, 4, 14
    words = [list(key[i:i+4]) for i in range(0, 32, 4)]
    for i in range(nk, nb*(nr+1)):
        t = words[i-1][:]
        if i % nk == 0:
            t = t[1:] + t[:1]
            t = [_SBOX[x] for x in t]
            t[0] ^= _RCON[i//nk]
        elif i % nk == 4:
            t = [_SBOX[x] for x in t]
        words.append([words[i-nk][j] ^ t[j] for j in range(4)])
    return [bytes(sum(words[4*r:4*r+4], [])) for r in range(nr+1)]

def _aes_encrypt_block(block, rks):
    # AES state is column-major: state[row + 4*column].
    s = list(block)
    s = [s[i] ^ rks[0][i] for i in range(16)]

    def sub():
        nonlocal s
        s = [_SBOX[x] for x in s]

    def shift():
        nonlocal s
        old = s[:]
        for r in range(4):
            for c in range(4):
                s[r+4*c] = old[r+4*((c+r)%4)]

    def mix():
        nonlocal s
        out = [0]*16
        for c in range(4):
            a = s[4*c:4*c+4]
            out[4*c+0] = _gmul(a[0],2)^_gmul(a[1],3)^a[2]^a[3]
            out[4*c+1] = a[0]^_gmul(a[1],2)^_gmul(a[2],3)^a[3]
            out[4*c+2] = a[0]^a[1]^_gmul(a[2],2)^_gmul(a[3],3)
            out[4*c+3] = _gmul(a[0],3)^a[1]^a[2]^_gmul(a[3],2)
        s = [x & 255 for x in out]

    for rnd in range(1, 15):
        sub()
        shift()
        if rnd != 14: mix()
        s = [s[i] ^ rks[rnd][i] for i in range(16)]
    return bytes(s)

def _aes_ctr(data, key, nonce):
    rks = _round_keys(key)
    # Meshy uses nonce || uint32be(2) as the initial counter block.
    counter = bytearray(nonce + struct.pack(">I", 2))
    out = bytearray(len(data))
    for pos in range(0, len(data), 16):
        ks = _aes_encrypt_block(bytes(counter), rks)
        chunk = data[pos:pos+16]
        out[pos:pos+len(chunk)] = bytes(a ^ b for a, b in zip(chunk, ks))
        # Treat the counter as a big-endian 128-bit integer.
        for i in range(15, -1, -1):
            counter[i] = (counter[i] + 1) & 255
            if counter[i]:
                break
    return bytes(out)


def _decrypt_meshy(path):
    with open(path, "rb") as f:
        data = f.read()

    if len(data) < 8240 or data[:8] != _MAGIC:
        raise ValueError("Not a valid Meshy .meshy file: missing MESHY.AI header.")

    nonce = data[10:22]
    encrypted = data[32:32 + _ENCRYPTED_SIZE]
    clear_tail = data[32 + _ENCRYPTED_SIZE + _TAG_SIZE:]

    first = _aes_ctr(encrypted, _KEY, nonce)
    glb = bytearray(first + clear_tail)

    if glb[:4] != b"glTF":
        raise ValueError(
            "Meshy decryption produced an invalid GLB header. "
            "The .meshy encryption format may have changed."
        )

    if len(glb) < 12:
        raise ValueError("Decrypted GLB is unexpectedly short.")

    # GLB header: magic (4), version (4), total length (4).
    struct.pack_into("<I", glb, 8, len(glb))
    return bytes(glb)


class IMPORT_OT_meshy(bpy.types.Operator, ImportHelper):
    bl_idname = "import_scene.meshy"
    bl_label = "Import Meshy Model"
    bl_options = {'UNDO'}

    filename_ext = ".meshy"
    filter_glob: StringProperty(default="*.meshy", options={'HIDDEN'})

    def execute(self, context):
        try:
            glb = _decrypt_meshy(self.filepath)

            fd, temp_path = tempfile.mkstemp(suffix=".glb", prefix="meshy_")
            os.close(fd)
            try:
                with open(temp_path, "wb") as f:
                    f.write(glb)

                # Blender's native glTF importer handles GLB, including
                # materials/textures and any supported mesh compression.
                result = bpy.ops.import_scene.gltf(filepath=temp_path)
                if 'FINISHED' not in result:
                    raise RuntimeError("Blender's GLB importer did not finish.")
            finally:
                try:
                    os.remove(temp_path)
                except OSError:
                    pass

            self.report({'INFO'}, "Meshy model imported successfully.")
            return {'FINISHED'}

        except Exception as exc:
            self.report({'ERROR'}, f"Meshy import failed: {exc}")
            return {'CANCELLED'}


def menu_func_import(self, context):
    self.layout.operator(IMPORT_OT_meshy.bl_idname, text="Meshy Model (.meshy)")


def menu_func_help(self, context):
    self.layout.separator()
    self.layout.operator("wm.meshy_support", text="Meshy Importer Support / Patreon")
    self.layout.operator("wm.meshy_docs", text="Meshy Importer Documentation")


class WM_OT_meshy_support(bpy.types.Operator):
    bl_idname = "wm.meshy_support"
    bl_label = "Meshy Importer Support"
    def execute(self, context):
        bpy.ops.wm.url_open(url="https://www.patreon.com/cw/DedZed")
        return {'FINISHED'}


class WM_OT_meshy_docs(bpy.types.Operator):
    bl_idname = "wm.meshy_docs"
    bl_label = "Meshy Importer Documentation"
    def execute(self, context):
        bpy.ops.wm.url_open(url="https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity")
        return {'FINISHED'}


classes = (IMPORT_OT_meshy, WM_OT_meshy_support, WM_OT_meshy_docs)

def register():
    for cls in classes:
        bpy.utils.register_class(cls)
    bpy.types.TOPBAR_MT_file_import.append(menu_func_import)
    bpy.types.TOPBAR_MT_help.append(menu_func_help)

def unregister():
    bpy.types.TOPBAR_MT_file_import.remove(menu_func_import)
    bpy.types.TOPBAR_MT_help.remove(menu_func_help)
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)

if __name__ == "__main__":
    register()
