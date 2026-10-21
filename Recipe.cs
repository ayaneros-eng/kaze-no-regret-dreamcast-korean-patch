// KZRP2: reconstruct original portions exclusively from the user's immutable track.
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace KazeKR {
    public static class Recipe {
        const int MaxPayload = 32 * 1024 * 1024;
        static byte[] Exact(BinaryReader r, int n) {
            byte[] b = r.ReadBytes(n); if (b.Length != n) throw new InvalidDataException("패치 데이터가 잘렸습니다."); return b;
        }
        static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }
        static byte[] Hash(byte[] b) { using (var s = SHA256.Create()) return s.ComputeHash(b); }
        static string FileHash(string p) { using (var s = SHA256.Create()) using (var f = File.OpenRead(p)) return Hex(s.ComputeHash(f)); }
        static void ReadAt(FileStream f, long pos, byte[] b, int offset, int count) {
            if (pos < 0 || pos > f.Length - count) throw new InvalidDataException("원본 참조 범위 오류입니다.");
            f.Position = pos; while (count > 0) { int n = f.Read(b, offset, count); if (n == 0) throw new EndOfStreamException(); offset += n; count -= n; }
        }
        public static void Apply(string source, byte[] package, Action<double> progress, Func<bool> cancel) {
            byte[] body;
            using (var m = new MemoryStream(package, false)) using (var r = new BinaryReader(m)) {
                if (Encoding.ASCII.GetString(Exact(r, 8)) != "KZRP2\r\n\u001a") throw new InvalidDataException("지원하지 않는 패치 형식입니다.");
                int size = r.ReadInt32(); if (size < 88 || size > MaxPayload) throw new InvalidDataException("패치 크기 오류입니다.");
                byte[] hash = Exact(r, 32); body = new byte[size];
                using (var z = new DeflateStream(m, CompressionMode.Decompress, true)) {
                    int p = 0; while (p < size) { int n = z.Read(body, p, size - p); if (n == 0) throw new InvalidDataException("압축 패치가 잘렸습니다."); p += n; }
                    if (z.ReadByte() != -1) throw new InvalidDataException("패치 크기 제한을 초과했습니다.");
                }
                if (Hex(Hash(body)) != Hex(hash)) throw new InvalidDataException("패치 SHA-256이 일치하지 않습니다.");
            }
            using (var r = new BinaryReader(new MemoryStream(body, false))) {
                long size = r.ReadInt64(); string original = Hex(Exact(r, 32)), target = Hex(Exact(r, 32));
                uint sourceCrc = r.ReadUInt32(), targetCrc = r.ReadUInt32();
                if (size != Patcher.Track3Sectors * 2352 || new FileInfo(source).Length != size) throw new InvalidDataException("원본 트랙 크기가 다릅니다.");
                if (FileHash(source) != original) throw new UserError("이 패치에 맞는 원본 트랙이 아닙니다 (SHA-256 불일치).");
                int records = r.ReadInt32(); if (records < 1 || records > Patcher.Track3Sectors) throw new InvalidDataException("섹터 수 오류입니다.");
                string temp = source + ".recipe-" + Guid.NewGuid().ToString("N");
                try {
                    File.Copy(source, temp, false);
                    using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var output = new FileStream(temp, FileMode.Open, FileAccess.Write, FileShare.None)) {
                        int previous = -1;
                        for (int i = 0; i < records; i++) {
                            if (cancel()) throw new OperationCanceledException();
                            int sector = r.ReadInt32(), operations = r.ReadInt32();
                            if (sector <= previous || sector >= Patcher.Track3Sectors || operations < 1 || operations > 2048) throw new InvalidDataException("섹터 명령 오류입니다.");
                            previous = sector; byte[] user = new byte[2048]; int at = 0;
                            for (int j = 0; j < operations; j++) {
                                byte kind = r.ReadByte(); int length = r.ReadUInt16();
                                if (length < 1 || length > 2048 - at || kind > 2) throw new InvalidDataException("데이터 명령 오류입니다.");
                                if (kind < 2) {
                                    ReadAt(input, r.ReadInt64(), user, at, length);
                                    if (kind == 1) { byte[] delta = Exact(r, length); for (int k = 0; k < length; k++) user[at + k] ^= delta[k]; }
                                } else { byte[] literal = Exact(r, length); Buffer.BlockCopy(literal, 0, user, at, length); }
                                at += length;
                            }
                            if (at != 2048) throw new InvalidDataException("섹터 길이 오류입니다.");
                            byte[] raw = new byte[2352]; Sector.Build(user, 0, Patcher.Track3Lba + sector, raw);
                            output.Position = (long)sector * 2352; output.Write(raw, 0, raw.Length); progress((double)(i + 1) / records);
                        }
                    }
                    if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("패치 뒤에 알 수 없는 데이터가 있습니다.");
                    if (FileHash(temp) != target) throw new InvalidDataException("재구성한 트랙의 SHA-256이 다릅니다.");
                    File.Replace(temp, source, null);
                } finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
    }
}
