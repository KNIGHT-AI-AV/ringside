"""Packs Ringside.app (built on Windows) into a tar.gz that keeps Unix file modes.

Windows file systems drop the executable bit, so every Mach-O file is marked 0755 here,
everything else 0644 and every folder 0755 (readable by all users, the ITMS-90255 lesson).
Also writes a sha256 manifest of every file for the Mac signing brief.

Usage: python -I make-mac-stage.py <path/to/Ringside.app> <out.tar.gz> <manifest.txt>
"""
import hashlib
import os
import sys
import tarfile

MACHO = {b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf", b"\xca\xfe\xba\xbe", b"\xce\xfa\xed\xfe"}


def is_macho(path):
    with open(path, "rb") as f:
        return f.read(4) in MACHO


def main(app, out, manifest):
    app = os.path.abspath(app)
    base = os.path.dirname(app)
    lines = []
    with tarfile.open(out, "w:gz", format=tarfile.PAX_FORMAT) as tar:
        for root, dirs, files in os.walk(app):
            dirs.sort()
            rel_root = os.path.relpath(root, base).replace(os.sep, "/")
            info = tarfile.TarInfo(rel_root)
            info.type = tarfile.DIRTYPE
            info.mode = 0o755
            tar.addfile(info)
            for name in sorted(files):
                path = os.path.join(root, name)
                rel = f"{rel_root}/{name}"
                macho = is_macho(path)
                info = tar.gettarinfo(path, rel)
                info.mode = 0o755 if macho else 0o644
                info.uid = info.gid = 0
                info.uname = info.gname = ""
                with open(path, "rb") as f:
                    tar.addfile(info, f)
                with open(path, "rb") as f:
                    digest = hashlib.sha256(f.read()).hexdigest()
                lines.append(f"{digest}  {'macho' if macho else 'file '}  {rel}")
    with open(manifest, "w", encoding="utf-8", newline="\n") as m:
        m.write("\n".join(lines) + "\n")
    print(f"{len(lines)} files, {sum(1 for l in lines if ' macho ' in l)} Mach-O -> {out}")


if __name__ == "__main__":
    main(*sys.argv[1:4])
