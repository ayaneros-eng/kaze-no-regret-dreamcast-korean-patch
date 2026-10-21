// CD-ROM Mode 1 섹터 재생성 (싱크·헤더·EDC·ECC) — 형식이 다른 덤프(2048바이트 ISO형 트랙, 앞뒤가 밀린 bin 등)를
// 게임 데이터만 보고 Redump와 같은 2352바이트 트랙으로 되돌리는 데 쓴다.
using System;
using System.IO;

namespace KazeKR
{
    static class Sector
    {
        static readonly byte[] F = new byte[256], B = new byte[256];
        static readonly uint[] E = new uint[256];
        public static readonly byte[] Sync = { 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0 };

        static Sector()
        {
            for (int i = 0; i < 256; i++)
            {
                int j = (i << 1) ^ ((i & 0x80) != 0 ? 0x11D : 0);
                F[i] = (byte)j; B[i ^ j] = (byte)i;
                uint e = (uint)i;
                for (int k = 0; k < 8; k++) e = (e >> 1) ^ ((e & 1) != 0 ? 0xD8018001u : 0);
                E[i] = e;
            }
        }

        static byte Bcd(int v) { return (byte)(((v / 10) << 4) | (v % 10)); }

        static void Ecc(byte[] s, int majorCount, int minorCount, int majorMult, int minorInc, int dest)
        {
            int size = majorCount * minorCount;
            for (int major = 0; major < majorCount; major++)
            {
                int index = (major >> 1) * majorMult + (major & 1);
                byte a = 0, b = 0;
                for (int minor = 0; minor < minorCount; minor++)
                {
                    byte t = s[12 + index];
                    index += minorInc; if (index >= size) index -= size;
                    a ^= t; b ^= t; a = F[a];
                }
                a = B[F[a] ^ b];
                s[dest + major] = a; s[dest + major + majorCount] = (byte)(a ^ b);
            }
        }

        /// <summary>user(2048바이트, ofs부터)와 LBA로 완전한 Mode 1 섹터를 out에 만든다.</summary>
        public static void Build(byte[] user, int ofs, int lba, byte[] o)
        {
            Buffer.BlockCopy(Sync, 0, o, 0, 12);
            int a = lba + 150;
            o[12] = Bcd(a / 4500); o[13] = Bcd(a / 75 % 60); o[14] = Bcd(a % 75); o[15] = 1;
            Buffer.BlockCopy(user, ofs, o, 16, 2048);
            uint edc = 0;
            for (int i = 0; i < 2064; i++) edc = (edc >> 8) ^ E[(edc ^ o[i]) & 0xFF];
            o[2064] = (byte)edc; o[2065] = (byte)(edc >> 8); o[2066] = (byte)(edc >> 16); o[2067] = (byte)(edc >> 24);
            for (int i = 2068; i < 2076; i++) o[i] = 0;
            Ecc(o, 86, 24, 2, 86, 2076);
            Ecc(o, 52, 43, 86, 88, 2248);
        }

        public static int Lba(byte[] s, int ofs)
        {
            Func<byte, int> d = x => (x >> 4) * 10 + (x & 15);
            return (d(s[ofs + 12]) * 60 + d(s[ofs + 13])) * 75 + d(s[ofs + 14]) - 150;
        }

        public static bool IsSync(byte[] s, int ofs)
        {
            for (int i = 0; i < 12; i++) if (s[ofs + i] != Sync[i]) return false;
            return true;
        }
    }
}
