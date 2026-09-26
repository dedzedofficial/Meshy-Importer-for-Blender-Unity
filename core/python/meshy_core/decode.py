"""Meshy .meshy container -> GLB bytes.

A .meshy file is a GLB whose first 8192 bytes are AES-256-CTR encrypted:

    offset 0   "MESHY.AI"                     8-byte magic
    offset 10  nonce                          12 bytes
    offset 32  encrypted GLB prefix           8192 bytes
    offset 8224 tag                           16 bytes (ignored)
    offset 8240 rest of the GLB, in the clear

The counter block is nonce || uint32be(2). After decryption the GLB header's
total-length field is rewritten to the real length. This mirrors
MeshyImporterMenu.DecodeFileForEditor in the Unity package.

Dependency-free: a small AES-256 encryptor is included (CTR only needs the
forward cipher), so this runs inside Blender, Unreal and plain Python alike.
"""

import struct

__all__ = ["MESHY_MAGIC", "MeshyFormatError", "decode_meshy_bytes", "decode_meshy_file",
           "encode_meshy_bytes", "aes256_encrypt_block", "aes_ctr", "describe_wrong_file",
           "HELP_WRONG_FILE", "HELP_FORMAT_CHANGED"]

MESHY_MAGIC = b"MESHY.AI"
# Meshy's current .meshy wrapper uses this fixed AES-256 key.
_KEY = b'JSON{"accessors":[{"bufferView":'
_HEADER_SIZE = 32
_ENCRYPTED_SIZE = 8192
_TAG_SIZE = 16
_MIN_SIZE = _HEADER_SIZE + _ENCRYPTED_SIZE + _TAG_SIZE

_DOCS = "https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity/blob/main/TROUBLESHOOTING.md"
HELP_WRONG_FILE = _DOCS + "#wrong-file-errors"
HELP_FORMAT_CHANGED = _DOCS + "#meshy-changed-its-web-format"


class MeshyFormatError(ValueError):
    pass


# ---- AES-256 (forward cipher only) ------------------------------------------

def _build_sbox():
    sbox = [0] * 256
    p = q = 1
    while True:
        # multiply p by 3
        p = p ^ ((p << 1) & 0xFF) ^ (0x1B if p & 0x80 else 0)
        # divide q by 3
        q ^= q << 1
        q ^= q << 2
        q ^= q << 4
        q &= 0xFF
        if q & 0x80:
            q ^= 0x09
        x = q ^ (((q << 1) | (q >> 7)) & 0xFF) ^ (((q << 2) | (q >> 6)) & 0xFF) \
            ^ (((q << 3) | (q >> 5)) & 0xFF) ^ (((q << 4) | (q >> 4)) & 0xFF)
        sbox[p] = x ^ 0x63
        if p == 1:
            break
    sbox[0] = 0x63
    return sbox


_SBOX = _build_sbox()
_XT = [((a << 1) ^ 0x1B) & 0xFF if a & 0x80 else (a << 1) for a in range(256)]


def _expand_key(key):
    if len(key) != 32:
        raise ValueError("AES-256 needs a 32-byte key")
    w = [list(key[i:i + 4]) for i in range(0, 32, 4)]
    rcon = 1
    for i in range(8, 60):
        t = list(w[i - 1])
        if i % 8 == 0:
            t = t[1:] + t[:1]
            t = [_SBOX[b] for b in t]
            t[0] ^= rcon
            rcon = _XT[rcon]
        elif i % 8 == 4:
            t = [_SBOX[b] for b in t]
        w.append([w[i - 8][j] ^ t[j] for j in range(4)])
    return [sum(w[4 * r:4 * r + 4], []) for r in range(15)]


def _encrypt(block, rks):
    s = [block[i] ^ rks[0][i] for i in range(16)]
    sb, xt = _SBOX, _XT
    for rnd in range(1, 15):
        s = [sb[b] for b in s]
        # ShiftRows (state is column-major: s[row + 4*col])
        s = [s[0], s[5], s[10], s[15], s[4], s[9], s[14], s[3],
             s[8], s[13], s[2], s[7], s[12], s[1], s[6], s[11]]
        if rnd != 14:
            out = []
            for c in range(0, 16, 4):
                a0, a1, a2, a3 = s[c], s[c + 1], s[c + 2], s[c + 3]
                t = a0 ^ a1 ^ a2 ^ a3
                out += [a0 ^ t ^ xt[a0 ^ a1], a1 ^ t ^ xt[a1 ^ a2],
                        a2 ^ t ^ xt[a2 ^ a3], a3 ^ t ^ xt[a3 ^ a0]]
            s = out
        rk = rks[rnd]
        s = [s[i] ^ rk[i] for i in range(16)]
    return bytes(s)


def aes256_encrypt_block(key, block):
    return _encrypt(block, _expand_key(key))


def aes_ctr(data, key, nonce, initial_counter=2):
    """AES-256-CTR; counter block is nonce(12) || uint32be(counter). Symmetric."""
    rks = _expand_key(key)
    counter = bytearray(nonce + struct.pack(">I", initial_counter))
    out = bytearray(len(data))
    for pos in range(0, len(data), 16):
        ks = _encrypt(counter, rks)
        chunk = data[pos:pos + 16]
        for i, b in enumerate(chunk):
            out[pos + i] = b ^ ks[i]
        for i in range(15, -1, -1):
            counter[i] = (counter[i] + 1) & 0xFF
            if counter[i]:
                break
    return bytes(out)


# ---- container -----------------------------------------------------------------

def describe_wrong_file(data):
    """Explain, in one plain sentence plus a fix, why `data` is not a usable .meshy file.

    Returns None when the data looks like a complete .meshy container. The Unity
    (MeshyImporterMenu.DescribeWrongFile) and Godot (meshy_decrypt.gd) ports use the
    same checks and wording; change them together.
    """
    data = bytes(data[:_MIN_SIZE])
    if not data:
        return "The file is empty. The download probably failed; save the model response again."
    if data[:8] == MESHY_MAGIC:
        if len(data) < _MIN_SIZE:
            return ("The .meshy file is cut off (only %d bytes). Save the complete model response again."
                    % len(data))
        return None
    head = data[:512].lstrip(b"\xef\xbb\xbf \t\r\n").lower()
    if data[:4] == b"glTF":
        what = ("This is a plain GLB, not a .meshy payload. Import it as a .glb instead; "
                "it does not need the Meshy Importer.")
    elif head.startswith((b"<!doctype", b"<html", b"<?xml", b"<head", b"<body")):
        what = ("This is a web page (HTML), not the model. In the Network tab, save the model "
                "request's response instead of the page.")
    elif head[:1] in (b"{", b"["):
        what = ("This is a JSON API response, not the model. Pick the request whose response "
                "starts with MESHY.AI.")
    elif data.startswith(b"Kaydara FBX Binary") or head.startswith(b"; fbx"):
        what = ("This is an FBX file (Meshy's normal Download). Import it directly as .fbx; "
                "it does not need the Meshy Importer.")
    elif data[:4] == b"PK\x03\x04":
        what = ("This is a ZIP archive. Unzip it; if it holds .glb/.fbx/.obj files, "
                "import those directly.")
    elif data[:8] == b"\x89PNG\r\n\x1a\n" or data[:3] == b"\xff\xd8\xff" or \
            (data[:4] == b"RIFF" and data[8:12] == b"WEBP"):
        what = "This is an image, not a model. Save the model request's response instead."
    elif any(head.startswith(p) for p in (b"# ", b"mtllib", b"o ", b"v ", b"g ")):
        what = ("This is an OBJ file (Meshy's normal Download). Import it directly as .obj; "
                "it does not need the Meshy Importer.")
    else:
        what = ("This is not a .meshy file: it does not start with MESHY.AI. "
                "Make sure you saved the model response, not another request.")
    return what


def decode_meshy_bytes(data):
    """Return the GLB bytes contained in a .meshy payload."""
    data = bytes(data)
    problem = describe_wrong_file(data)
    if problem:
        raise MeshyFormatError(problem + " Help: " + HELP_WRONG_FILE)
    nonce = data[10:22]
    first = aes_ctr(data[_HEADER_SIZE:_HEADER_SIZE + _ENCRYPTED_SIZE], _KEY, nonce)
    glb = bytearray(first + data[_MIN_SIZE:])
    if len(glb) < 12 or glb[:4] != b"glTF":
        raise MeshyFormatError("Meshy decryption produced an invalid GLB header. Meshy may have "
                               "changed the .meshy format; update the importer or report the file. "
                               "Help: " + HELP_FORMAT_CHANGED)
    struct.pack_into("<I", glb, 8, len(glb))
    return bytes(glb)


def decode_meshy_file(path):
    with open(path, "rb") as f:
        return decode_meshy_bytes(f.read())


def encode_meshy_bytes(glb, nonce=b"\x00" * 12):
    """Wrap a GLB in the .meshy layout. Used by the tests to build fixtures."""
    glb = bytes(glb)
    if len(glb) < _ENCRYPTED_SIZE:
        glb = glb + b"\x00" * (_ENCRYPTED_SIZE - len(glb))
    header = MESHY_MAGIC + b"\x00\x00" + bytes(nonce) + b"\x00" * 10
    enc = aes_ctr(glb[:_ENCRYPTED_SIZE], _KEY, bytes(nonce))
    return header + enc + b"\x00" * _TAG_SIZE + glb[_ENCRYPTED_SIZE:]
