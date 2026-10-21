"""Independent read-only KZRP2 verifier. Requires local original and target tracks."""
import argparse
import hashlib
import io
import json
import pathlib
import struct
import zlib
from build_recipe import executable, BASE, SECTOR, SECTORS, sha_file


def verify(source, target, patch):
    package = pathlib.Path(patch).read_bytes()
    assert package[:8] == b'KZRP2\r\n\x1a'
    body = zlib.decompress(package[44:], -15)
    assert len(body) == struct.unpack_from('<I', package, 8)[0]
    assert hashlib.sha256(body).digest() == package[12:44]
    stream = io.BytesIO(body)
    def take(n):
        b = stream.read(n)
        assert len(b) == n
        return b
    def number(fmt):
        return struct.unpack(fmt, take(struct.calcsize(fmt)))[0]
    size = number('<Q')
    original_hash, target_hash = take(32), take(32)
    original_crc, target_crc, records = struct.unpack('<III', take(12))
    assert size == SECTOR * SECTORS
    assert sha_file(source) == (original_hash, original_crc)
    assert sha_file(target) == (target_hash, target_crc)
    touched = set()
    literal_bytes = 0
    source_prefix_bytes = 0
    with open(source, 'rb') as original, open(target, 'rb') as result:
        old_lba, old_size = executable(original)
        new_lba, new_size = executable(result)
        for _ in range(records):
            sector, count = struct.unpack('<II', take(8))
            assert 0 <= sector < SECTORS and sector not in touched
            assert not touched or sector > max(touched)
            touched.add(sector)
            user = bytearray()
            for _ in range(count):
                kind, length = struct.unpack('<BH', take(3))
                relative = (BASE + sector - new_lba) * 2048 + len(user)
                assert 0 < length <= 2048 - len(user)
                if kind == 2:
                    assert old_size <= relative < relative + length <= new_size
                    literal_bytes += length
                    value = take(length)
                else:
                    assert kind in (0, 1)
                    offset = number('<Q')
                    assert offset + length <= size
                    original.seek(offset)
                    value = original.read(length)
                    if 0 <= relative < old_size:
                        assert relative + length <= old_size
                        expected = (old_lba - BASE + relative // 2048) * SECTOR + 16 + relative % 2048
                        assert offset == expected
                        source_prefix_bytes += length
                    else:
                        assert offset == sector * SECTOR + 16 + len(user)
                    if kind == 1:
                        delta = take(length)
                        value = bytes(x ^ y for x, y in zip(value, delta))
                user.extend(value)
            assert len(user) == 2048
            result.seek(sector * SECTOR + 16)
            assert user == result.read(2048)
        assert stream.tell() == len(body)
        assert literal_bytes == new_size - old_size
        assert source_prefix_bytes == old_size
        # Verify every untouched raw sector, including original voice/music/photos.
        original.seek(0); result.seek(0)
        for sector in range(SECTORS):
            a, b = original.read(SECTOR), result.read(SECTOR)
            if sector not in touched:
                assert a == b
    return dict(verified=True, independently_decoded_sectors=records, unchanged_raw_sectors=SECTORS-records, original_program_bytes_sourced_locally=source_prefix_bytes, literal_bytes_only_in_new_extension=literal_bytes, original_program_literal_bytes=0, original_ui_literal_bytes=0, target_track_sha256=target_hash.hex())


if __name__ == '__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source-track',required=True);p.add_argument('--target-track',required=True)
    p.add_argument('--patch',required=True);p.add_argument('--report',required=True)
    a=p.parse_args()
    assert pathlib.Path(a.report).resolve() not in {pathlib.Path(x).resolve() for x in (a.source_track,a.target_track,a.patch)}
    r=verify(a.source_track,a.target_track,a.patch)
    pathlib.Path(a.report).write_text(json.dumps(r,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(r))
