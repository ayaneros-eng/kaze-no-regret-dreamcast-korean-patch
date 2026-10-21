"""Build a source-dependent KZRP2 recipe from local original/translated raw tracks.

Python 3.12+, standard library only. Never distribute either input track.
Literal data is permitted only in the newly written executable extension.
All original executable bytes and original UI resources are COPY/XOR references.
"""
import argparse
import hashlib
import json
import pathlib
import struct
import zlib

MAGIC = b"KZRP2\r\n\x1a"
BASE = 45000
SECTOR = 2352
SECTORS = 504150


def sha_file(path):
    h = hashlib.sha256()
    crc = 0
    with open(path, "rb") as f:
        while block := f.read(8 << 20):
            h.update(block)
            crc = zlib.crc32(block, crc)
    return h.digest(), crc


def user_read(f, lba, size):
    b = bytearray()
    for n in range((size + 2047) // 2048):
        f.seek((lba - BASE + n) * SECTOR + 16)
        b.extend(f.read(min(2048, size - len(b))))
    assert len(b) == size
    return bytes(b)


def executable(f):
    pvd = user_read(f, BASE + 16, 2048)
    assert pvd[1:6] == b"CD001"
    root, length = struct.unpack_from("<II", pvd, 158)[0], struct.unpack_from("<I", pvd, 166)[0]
    data = user_read(f, root, length)
    at = 0
    while at < len(data):
        size = data[at]
        if not size:
            at = (at // 2048 + 1) * 2048
            continue
        name = data[at + 33:at + 33 + data[at + 32]].split(b";")[0]
        if name == b"1ST_READ.BIN":
            return struct.unpack_from("<I", data, at + 2)[0], struct.unpack_from("<I", data, at + 10)[0]
        at += size
    raise ValueError("1ST_READ.BIN missing")


def build(source, target, output, report):
    inputs = {pathlib.Path(source).resolve(), pathlib.Path(target).resolve()}
    assert pathlib.Path(output).resolve() not in inputs and pathlib.Path(report).resolve() not in inputs
    assert pathlib.Path(output).resolve() != pathlib.Path(report).resolve()
    assert pathlib.Path(source).stat().st_size == pathlib.Path(target).stat().st_size == SECTOR * SECTORS
    source_sha, source_crc = sha_file(source)
    target_sha, target_crc = sha_file(target)
    records = bytearray()
    counts = {"copy_bytes": 0, "xor_source_bytes": 0, "xor_nonzero_bytes": 0, "new_extension_literal_bytes": 0, "sectors": 0}
    with open(source, "rb") as original, open(target, "rb") as translated:
        source_lba, prefix_size = executable(original)
        target_lba, target_size = executable(translated)
        assert target_size > prefix_size
        for sector in range(SECTORS):
            original.seek(sector * SECTOR)
            src_raw = original.read(SECTOR)
            translated.seek(sector * SECTOR)
            dst_raw = translated.read(SECTOR)
            if src_raw == dst_raw:
                continue
            counts["sectors"] += 1
            user = dst_raw[16:2064]
            ops = []
            at = 0
            while at < 2048:
                relative = (BASE + sector - target_lba) * 2048 + at
                length = 2048 - at
                if 0 <= relative < prefix_size:
                    length = min(length, prefix_size - relative, 2048 - relative % 2048)
                    offset = (source_lba - BASE + relative // 2048) * SECTOR + 16 + relative % 2048
                elif prefix_size <= relative < target_size:
                    length = min(length, target_size - relative)
                    value = user[at:at + length]
                    ops.append(struct.pack("<BH", 2, length) + value)
                    counts["new_extension_literal_bytes"] += length
                    at += length
                    continue
                else:
                    # Unchanged portions of texture headers/pixels and directory
                    # records are always read from the local original track.
                    offset = sector * SECTOR + 16 + at
                    if relative < 0:
                        length = min(length, -relative)
                original.seek(offset)
                base = original.read(length)
                value = user[at:at + length]
                delta = bytes(a ^ b for a, b in zip(base, value))
                if any(delta):
                    ops.append(struct.pack("<BHQ", 1, length, offset) + delta)
                    counts["xor_source_bytes"] += length
                    counts["xor_nonzero_bytes"] += sum(x != 0 for x in delta)
                else:
                    ops.append(struct.pack("<BHQ", 0, length, offset))
                    counts["copy_bytes"] += length
                at += length
            records.extend(struct.pack("<II", sector, len(ops)) + b"".join(ops))
    body = struct.pack("<Q", SECTOR * SECTORS) + source_sha + target_sha + struct.pack("<III", source_crc, target_crc, counts["sectors"]) + records
    compress = zlib.compressobj(9, zlib.DEFLATED, -15)
    package = MAGIC + struct.pack("<I", len(body)) + hashlib.sha256(body).digest() + compress.compress(body) + compress.flush()
    pathlib.Path(output).write_bytes(package)
    result = dict(format="KZRP2", source_track_sha256=source_sha.hex(), target_track_sha256=target_sha.hex(), source_crc32=f"{source_crc:08x}", target_crc32=f"{target_crc:08x}", patch_sha256=hashlib.sha256(package).hexdigest(), patch_bytes=len(package), original_executable_lba=source_lba, target_executable_lba=target_lba, original_executable_bytes=prefix_size, target_executable_bytes=target_size, **counts)
    pathlib.Path(report).write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--source-track", required=True)
    p.add_argument("--target-track", required=True)
    p.add_argument("--output", required=True)
    p.add_argument("--report", required=True)
    a = p.parse_args()
    build(a.source_track, a.target_track, a.output, a.report)
