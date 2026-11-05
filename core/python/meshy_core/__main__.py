"""Command-line converter: python -m meshy_core input.meshy [output.glb] [options]."""

import argparse
import os
import sys
import time

from . import __version__
from .decode import decode_meshy_file
from .normalize import NormalizeOptions, normalize_glb
from .webp_vp8 import has_pillow


def main(argv=None):
    ap = argparse.ArgumentParser(prog="python -m meshy_core",
                                 description="Convert Meshy .meshy payloads into plain .glb files.")
    ap.add_argument("inputs", nargs="+", help=".meshy file(s)")
    ap.add_argument("-o", "--output", help="output .glb (single input) or directory")
    ap.add_argument("--raw", action="store_true",
                    help="only decrypt; keep Meshy's compression/extensions exactly as they are")
    ap.add_argument("--keep-webp", action="store_true", help="do not convert WebP textures to PNG")
    ap.add_argument("--no-uv-repair", action="store_true", help="do not repair broken UVs")
    ap.add_argument("--scale", type=float, default=1.0, help="uniform scale for the whole model (default 1.0)")
    ap.add_argument("--version", action="version", version="meshy_core " + __version__)
    args = ap.parse_args(argv)

    if len(args.inputs) > 1 and args.output and not os.path.isdir(args.output):
        ap.error("--output must be a directory when converting several files")

    opts = NormalizeOptions.for_host("full", webp_to_png=not args.keep_webp,
                                     repair_uvs=not args.no_uv_repair, scale=args.scale,
                                     log=lambda m: print("  " + m))
    if args.scale <= 0:
        ap.error("--scale must be greater than 0")
    if not args.raw and not args.keep_webp and not has_pillow():
        print("note: Pillow not found, using the built-in WebP decoder (slower for large textures)")

    failures = 0
    for path in args.inputs:
        if args.output and os.path.isdir(args.output):
            out = os.path.join(args.output, os.path.splitext(os.path.basename(path))[0] + ".glb")
        elif args.output:
            out = args.output
        else:
            out = os.path.splitext(path)[0] + ".glb"
        t = time.time()
        try:
            glb = decode_meshy_file(path)
            if not args.raw:
                glb, report = normalize_glb(glb, opts)
                print("%s: %s" % (os.path.basename(path), report.summary()))
            with open(out, "wb") as f:
                f.write(glb)
            print("wrote %s (%.1f s)" % (out, time.time() - t))
        except Exception as exc:  # report and keep going with the rest
            failures += 1
            print("error: %s: %s" % (os.path.basename(path), exc), file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
