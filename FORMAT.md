# KZRP2 source-dependent patch format

KZRP2 is a local source-reference recipe, not BPS. The input is the exact Rev A
third raw track: 504150 sectors of 2352 bytes, starting at logical LBA 45000.
All integer fields are unsigned little-endian unless stated otherwise.

The outer envelope is 8-byte `KZRP2\r\n\x1a`, a 32-bit uncompressed payload
length, a 32-byte SHA-256 of that payload, and raw DEFLATE data. The consumer
limits uncompressed data to 32 MiB, verifies its length/hash and rejects
unexpected trailing uncompressed content.

Payload: 64-bit track length, original and target track SHA-256 (32 bytes each),
original and target CRC32 (32 bits each), and a 32-bit sector-record count.
Each record contains a 32-bit relative sector number, a 32-bit operation count,
and operations filling exactly 2048 user-data bytes. Sectors must be ordered,
unique and within the input track. The 2352-byte raw sector is regenerated from
its user bytes and absolute LBA, including EDC/ECC.

Each operation starts with a one-byte kind and a 16-bit nonzero length:

| Kind | Following data | Meaning |
|---|---|---|
| 0 | 64-bit original raw-track offset | Copy local original bytes |
| 1 | 64-bit original offset, `length` XOR bytes | Read original bytes and XOR differences |
| 2 | `length` literal bytes | New executable extension authored for this patch |

The producer permits kind 2 only between the original executable prefix length
and the end of the translated executable. Existing executable bytes, relocated
code, PVR/UI resources, directory records and unused areas use kinds 0/1. A
zero XOR value means retain the original source byte; it is not a literal copy
of that byte. The remaining track is copied unchanged from the user input.
Original code cannot be reconstructed solely from the literal extension.
This is a technical property, not a legal permission for translation or
game-derived delta data.

`Recipe.cs` checks input SHA-256, reconstructs a separate temporary copy,
checks the complete output SHA-256, and replaces only the patcher's own work
track. The external CHD/GDI input is never the writable work track. Original
and target CRC32 also serve the patcher's disc selection and output checks.

Maintainer reproduction with Python 3.12+ (standard library only):

```powershell
python -X utf8 -B build_recipe.py --source-track ORIGINAL_TRACK03.bin --target-track VERIFIED_TRANSLATED_TRACK03.bin --output disc1.kzrp --report build-report.json
python -X utf8 -B verify_recipe.py --source-track ORIGINAL_TRACK03.bin --target-track VERIFIED_TRANSLATED_TRACK03.bin --patch disc1.kzrp --report independent-report.json
.\build_patcher.ps1
.\editor\build.ps1
```

Keep both track files private. The verifier independently decodes each
operation, checks that all original executable-prefix bytes use their original
source positions, prohibits original/UI literal operations, compares every
modified user sector to the verified target, and compares every untouched raw
sector. The actual Windows consumer additionally regenerates/checks all raw
sectors through the complete output hash. Publish only the explicitly reviewed
source, recipe, license and tool-package allowlist.
