"""Pads an AppImage's squashfs to a whole number of 4 KiB blocks.

vpk writes the squashfs without the padding mksquashfs adds by default. Mounted through a loop
device (firejail --appimage, the AppImage catalog's test), the device size is rounded down to whole
512-byte sectors, ends up shorter than the filesystem says it is, and the kernel refuses the mount
("bad superblock", EINVAL). Zero bytes after the filesystem change nothing else: the runtime and
squashfuse read only up to the filesystem's own size.

Usage: python -I pad-appimage.py <path/to/App.AppImage>
"""
import os
import struct
import sys

BLOCK = 4096


def squashfs_offset(path):
    # The type 2 runtime is an ELF file; the squashfs starts where its section headers end
    # (the same offset the runtime prints for --appimage-offset).
    with open(path, "rb") as f:
        h = f.read(64)
    if h[:4] != b"\x7fELF" or h[8:11] != b"AI\x02":
        raise SystemExit(f"{path}: not a type 2 AppImage")
    if h[4] == 2:
        shoff = struct.unpack_from("<Q", h, 0x28)[0]
        entsize, num = struct.unpack_from("<HH", h, 0x3A)
    else:
        shoff = struct.unpack_from("<I", h, 0x20)[0]
        entsize, num = struct.unpack_from("<HH", h, 0x2E)
    return shoff + entsize * num


def main(path):
    offset = squashfs_offset(path)
    with open(path, "rb") as f:
        f.seek(offset)
        if f.read(4) != b"hsqs":
            raise SystemExit(f"{path}: no squashfs at offset {offset}")
    size = os.path.getsize(path)
    region = size - offset
    padded = offset + -(-region // BLOCK) * BLOCK
    if padded > size:
        with open(path, "ab") as f:
            f.write(b"\0" * (padded - size))
    print(f"{path}: squashfs at {offset}, {region} bytes, padded by {padded - size} to {padded}")


if __name__ == "__main__":
    main(sys.argv[1])
