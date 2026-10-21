// 적용 엔진. 드림캐스트: 원본 → (정리) → 3번 트랙 확인 → KZRP2 원본 참조 재구성 → [KR] CHD (+GDI 폴더)
//          새턴:      원본 → 1번 트랙 분리·(정리)·확인 → BPS 제자리 적용 → [KR] cue/bin
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KazeKR
{
    class Disc
    {
        public string Sys = "dc"; public int No; public string Res; public uint SrcCrc, DstCrc, T1Crc, T2Crc; public string KrSha;
        public long Sectors; public string Media; public bool Catalog;      // 새턴 전용
        public string Name
        {
            get
            {
                return Sys == "ss" ? string.Format("Real Sound - Kaze no Regret (Japan) (Disc {0}) ({1})", No, Media)
                                   : string.Format("Real Sound - Kaze no Regret (Japan) (Disc {0}) (Rev A)", No);
            }
        }
        public string Label { get { return (Sys == "ss" ? "새턴 디스크 " : "드림캐스트 디스크 ") + No; } }
        public string Key { get { return Sys + No; } }
    }

    class UserError : Exception { public UserError(string m) : base(m) { } }

    class Patcher
    {
        public const long Track3Sectors = 504150;      // 1,185,760,800 바이트 / 2352
        public const int Track3Lba = 45000;
        public static readonly Disc[] Discs = {
            new Disc { No = 1, Res = "disc1.kzrp", SrcCrc = 0x1e5ec99f, DstCrc = 0xf9264f56, T1Crc = 0x57747fa8, T2Crc = 0xfc8372d0,
                       KrSha = "fa0fc10519b75423cc84b65ba7e6d0097c29f7f2afb278f0abb838acdc2df4a8" },
            new Disc { No = 2, Res = "disc2.kzrp", SrcCrc = 0x99dacab4, DstCrc = 0x00d870d3, T1Crc = 0x459af754, T2Crc = 0xfc8372d0,
                       KrSha = "db9aa8bd1bd66c2fdcf970a076cfca59ddfc91f2941152a02b31eabd5e732e4d" },
        };
        // 새턴판: Redump "(Disc N) (2M…) (Track 1).bin"에 적용, Track 2(오디오)는 그대로
        public static readonly Disc[] SsDiscs = new Disc[0];

        public Action<string> Log = delegate { };
        public Action<string, double> Progress = delegate { };
        public volatile bool Cancel;
        string work, chdman, outRoot;
        public readonly List<string> Done = new List<string>();     // 이번에 만든 디스크 ("dc1", "ss3" …)
        public readonly List<string> Outputs = new List<string>();

        // ---------------------------------------------------------------- 준비 / 정리
        public static byte[] Res(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetShortPathName(string longPath, StringBuilder shortPath, int size);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern bool CreateHardLink(string newName, string existing, IntPtr sec);

        static bool Ascii(string s) { foreach (char c in s) if (c > 126) return false; return true; }

        // chdman은 CUE/GDI 안에 적힌 트랙 파일을 한글 경로에서 못 연다 → 작업 폴더는 반드시 영문 경로
        static string WorkRoot(string outDir)
        {
            string t = Path.GetTempPath();
            if (Ascii(t)) return t;
            var sb = new StringBuilder(1024);
            if (GetShortPathName(t, sb, sb.Capacity) > 0 && Ascii(sb.ToString())) return sb.ToString();
            foreach (var root in new[] { Path.GetPathRoot(outDir), Path.GetPathRoot(Environment.SystemDirectory) })
            {
                try { string d = Path.Combine(root, "KazeKR_tmp"); Directory.CreateDirectory(d); return d; } catch { }
            }
            throw new UserError("영문 경로의 임시 폴더를 만들 수 없습니다.");
        }

        void Prepare()
        {
            if (work != null && Directory.Exists(work)) return;
            work = Path.Combine(WorkRoot(outRoot), "KazeKR_" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(work);
            // 적용기 옆에 같은 chdman.exe가 있으면 그것을 쓰고(백신 친화), 없으면 내장본을 임시 폴더에 꺼낸다
            byte[] emb = Res("chdman.exe");
            string side = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chdman.exe");
            if (File.Exists(side) && FileSha(side, "변환 도구 확인") == BitConverter.ToString(SHA256.Create().ComputeHash(emb)).Replace("-", "").ToLowerInvariant()) chdman = side;
            else { chdman = Path.Combine(work, "chdman.exe"); File.WriteAllBytes(chdman, emb); }
        }

        public void Cleanup()
        {
            try { if (work != null && Directory.Exists(work)) Directory.Delete(work, true); } catch { }
            try
            {
                string parent = work == null ? null : Path.GetDirectoryName(work);
                if (parent != null && Path.GetFileName(parent) == "KazeKR_tmp" && Directory.GetFileSystemEntries(parent).Length == 0) Directory.Delete(parent);
            }
            catch { }
            work = null;
        }

        static long Free(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace; } catch { return long.MaxValue; }
        }

        void NeedSpace(string path, long bytes, string what)
        {
            long f = Free(path);
            if (f < bytes)
                throw new UserError(string.Format("{0} 드라이브({1})에 빈 공간이 부족합니다. 필요 {2:0.0}GB / 남은 {3:0.0}GB",
                    what, Path.GetPathRoot(Path.GetFullPath(path)), bytes / 1e9, f / 1e9));
        }

        // ---------------------------------------------------------------- chdman
        void Chdman(string stage, params string[] args)
        {
            // Use short relative output names for extraction. Older chdman
            // builds can emit incorrect GDI track names from long full paths.
            string commandDir = work;
            if (args.Length > 0 && args[0] == "extractcd")
            {
                int output = Array.IndexOf(args, "-o");
                if (output >= 0)
                {
                    commandDir = Path.GetDirectoryName(Path.GetFullPath(args[output + 1]));
                    args[output + 1] = Path.GetFileName(args[output + 1]);
                    int binary = Array.IndexOf(args, "-ob");
                    if (binary >= 0) args[binary + 1] = Path.GetFileName(args[binary + 1]);
                }
            }
            var sb = new StringBuilder();
            foreach (var a in args) sb.Append(a.StartsWith("-") ? a : "\"" + a.Replace('\\', '/') + "\"").Append(' ');
            var psi = new ProcessStartInfo(chdman, sb.ToString().Trim());
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardError = true; psi.RedirectStandardOutput = true;
            psi.WorkingDirectory = commandDir;
            var tail = new StringBuilder();
            using (var p = Process.Start(psi))
            {
                p.OutputDataReceived += delegate(object o, DataReceivedEventArgs e) { if (e.Data != null) lock (tail) tail.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                var buf = new StringBuilder(); int c;
                var rx = new Regex(@"([0-9.]+)% complete");
                while ((c = p.StandardError.Read()) >= 0)
                {
                    if (Cancel) { try { p.Kill(); } catch { } throw new OperationCanceledException(); }
                    if (c == '\r' || c == '\n')
                    {
                        var line = buf.ToString(); buf.Length = 0;
                        var m = rx.Match(line);
                        if (m.Success) Progress(stage, double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100);
                        else if (line.Trim().Length > 0) lock (tail) tail.AppendLine(line);
                    }
                    else buf.Append((char)c);
                }
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    string t = tail.ToString().Trim();
                    if (t.IndexOf("Error opening", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("parsing", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new UserError("디스크 이미지를 열 수 없습니다. 파일이 손상됐거나 CUE/GDI에 적힌 트랙 파일이 빠졌을 수 있습니다.\r\n(chdman: " + LastLine(t) + ")");
                    throw new Exception("chdman 오류 (" + p.ExitCode + "): " + t);
                }
            }
        }

        string ChdmanInfo(string chd)
        {
            var psi = new ProcessStartInfo(chdman, "info -i \"" + chd + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.WorkingDirectory = work;
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit();
                if (p.ExitCode != 0) throw new UserError("CHD 파일을 열 수 없습니다. 파일이 손상됐을 수 있습니다.");
                return o;
            }
        }

        static string LastLine(string s)
        {
            var l = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return l.Length > 0 ? l[l.Length - 1] : s;
        }

        // ---------------------------------------------------------------- CRC32 / SHA-256
        static readonly uint[] T = MakeTable();
        static uint[] MakeTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; t[i] = c; }
            return t;
        }
        public static uint Crc(uint crc, byte[] b, int o, int n) { crc = ~crc; for (int i = o; i < o + n; i++) crc = T[(crc ^ b[i]) & 0xFF] ^ (crc >> 8); return ~crc; }

        uint FileCrc(string path, string stage)
        {
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                var b = new byte[1 << 22]; uint crc = 0; long done = 0, len = Math.Max(1, f.Length); int n;
                while ((n = f.Read(b, 0, b.Length)) > 0)
                {
                    if (Cancel) throw new OperationCanceledException();
                    crc = Crc(crc, b, 0, n); done += n; if (stage != null) Progress(stage, (double)done / len);
                }
                return crc;
            }
        }

        string FileSha(string path, string stage)
        {
            using (var sha = SHA256.Create())
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                var b = new byte[1 << 22]; long done = 0; int n;
                while ((n = f.Read(b, 0, b.Length)) > 0)
                {
                    if (Cancel) throw new OperationCanceledException();
                    sha.TransformBlock(b, 0, n, null, 0); done += n; Progress(stage, (double)done / f.Length);
                }
                sha.TransformFinalBlock(b, 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        // Reconstruct from the immutable, validated user track; never embed its prefix.
        void ApplyInPlace(string path, byte[] recipe)
        {
            Recipe.Apply(path, recipe,
                delegate(double value) { Progress("한글 패치 적용", value); },
                delegate { return Cancel; });
        }

        // ---------------------------------------------------------------- GDI / CUE
        public class Row { public int No, Lba, Type, SecSize; public string File; public long Offset; }

        static Row[] ReadGdi(string gdi)
        {
            var rows = new List<Row>();
            var lines = File.ReadAllLines(gdi);
            for (int i = 1; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i].Trim(), "^(\\d+)\\s+(\\d+)\\s+(\\d+)\\s+(\\d+)\\s+(\"[^\"]+\"|\\S+)\\s+(-?\\d+)$");
                if (m.Success) rows.Add(new Row { No = int.Parse(m.Groups[1].Value), Lba = int.Parse(m.Groups[2].Value), Type = int.Parse(m.Groups[3].Value),
                    SecSize = int.Parse(m.Groups[4].Value), File = m.Groups[5].Value.Trim('"'), Offset = long.Parse(m.Groups[6].Value) });
            }
            if (rows.Count == 0) throw new UserError("GDI 파일을 읽을 수 없습니다.");
            return rows.ToArray();
        }

        static void WriteGdi(string gdi, IEnumerable<Row> rows)
        {
            var r = rows.ToArray(); var sb = new StringBuilder(); sb.Append(r.Length).Append("\r\n");
            foreach (var x in r) sb.AppendFormat("{0} {1} {2} {3} {4} {5}\r\n", x.No, x.Lba, x.Type, x.SecSize, x.File, x.Offset);
            File.WriteAllText(gdi, sb.ToString(), Encoding.ASCII);
        }

        void LinkOrCopy(string src, string dst, string stage)
        {
            if (!CreateHardLink(dst, src, IntPtr.Zero)) CopyWithProgress(src, dst, stage);
        }

        void CopyWithProgress(string src, string dst, string stage)
        {
            using (var a = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (var b = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                var buf = new byte[1 << 22]; long done = 0; int n;
                while ((n = a.Read(buf, 0, buf.Length)) > 0)
                {
                    if (Cancel) throw new OperationCanceledException();
                    b.Write(buf, 0, n); done += n; Progress(stage, (double)done / Math.Max(1, a.Length));
                }
            }
        }

        // GDI와 트랙들을 작업 폴더로 (영문 이름). 3번 트랙은 고칠 것이므로 항상 진짜 복사.
        string CopyGdiToWork(string gdi, string dir)
        {
            Directory.CreateDirectory(dir);
            var rows = ReadGdi(gdi);
            foreach (var r in rows)
            {
                string src = Path.Combine(Path.GetDirectoryName(gdi), r.File);
                if (!File.Exists(src)) throw new UserError("GDI에 적힌 트랙 파일이 없습니다: " + r.File);
                string name = string.Format("track{0:00}{1}", r.No, Path.GetExtension(r.File).ToLowerInvariant());
                string dst = Path.Combine(dir, name);
                if (r.No == 3) CopyWithProgress(src, dst, "트랙 복사"); else LinkOrCopy(src, dst, "트랙 복사");
                r.File = name;
            }
            string outGdi = Path.Combine(dir, "disc.gdi");
            WriteGdi(outGdi, rows);
            return outGdi;
        }

        string CopyCueToWork(string cue, string dir)
        {
            var lines = File.ReadAllLines(cue, Encoding.UTF8); int n = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i], @"^(\s*FILE\s+)(""[^""]+""|\S+)(\s+\S+\s*)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                string fname = m.Groups[2].Value.Trim('"');
                string src = Path.Combine(Path.GetDirectoryName(cue), fname);
                if (!File.Exists(src))
                {   // 대소문자·확장자만 다른 경우 구제
                    var alt = Directory.GetFiles(Path.GetDirectoryName(cue)).FirstOrDefault(f => string.Equals(Path.GetFileName(f), fname, StringComparison.OrdinalIgnoreCase));
                    if (alt == null) throw new UserError("CUE에 적힌 파일이 없습니다: " + fname);
                    src = alt;
                }
                string name = string.Format("cue{0:00}.bin", ++n), dst = Path.Combine(dir, name);
                LinkOrCopy(src, dst, "트랙 복사");
                lines[i] = m.Groups[1].Value + "\"" + name + "\"" + m.Groups[3].Value;
            }
            string outCue = Path.Combine(dir, "in.cue");
            File.WriteAllLines(outCue, lines, Encoding.ASCII);
            return outCue;
        }

        static Row Track3(Row[] rows)
        {
            var r = rows.FirstOrDefault(x => x.No == 3);
            if (r == null) throw new UserError("3번 트랙이 없습니다. 드림캐스트 GD-ROM 이미지가 아닌 것 같습니다.");
            return r;
        }

        // ---------------------------------------------------------------- 정규화: 게임 데이터만 보고 Redump 형식 3번 트랙 재구성
        // 2352(정렬/밀림), 2048(ISO형) 어느 쪽이든 섹터 사용자 데이터를 LBA 자리에 놓고 싱크·헤더·EDC·ECC를 다시 만든다.
        // 끝의 150섹터(포스트갭)는 원본에서도 빈 섹터라, 짧은 덤프는 빈 섹터로 채운다.
        Dictionary<long, uint> Normalize(string src, int lbaStart, long total, long[] checkpoints, string dst)
        {
            var res = new Dictionary<long, uint>();
            using (var f = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (var o = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                long len = f.Length;
                var head = new byte[Math.Min(len, 2352 * 64)];
                f.Read(head, 0, head.Length); f.Position = 0;
                int mode = 0; long start = 0;
                if (len >= 2352 && Sector.IsSync(head, 0) && Sector.IsSync(head, Math.Min(2352, head.Length - 12))) mode = 2352;
                else
                {
                    for (int i = 0; i + 2352 + 12 <= head.Length; i++)
                        if (Sector.IsSync(head, i) && Sector.IsSync(head, i + 2352)) { mode = 2352; start = i; break; }
                    if (mode == 0 && len % 2048 == 0) mode = 2048;
                }
                if (mode == 0) return res;
                f.Position = start;
                var sec = new byte[2352]; var zero = new byte[2048]; var outb = new byte[2352];
                uint crc = 0; long next = 0, idx = 0;
                var cps = new HashSet<long>(checkpoints);
                Action<byte[], int> emit = (user, ofs) =>
                {
                    Sector.Build(user, ofs, (int)(lbaStart + next), outb);
                    o.Write(outb, 0, 2352); crc = Crc(crc, outb, 0, 2352); next++;
                    if (cps.Contains(next)) res[next] = crc;
                };
                int rd = mode;
                while (next < total)
                {
                    if (Cancel) throw new OperationCanceledException();
                    int n = f.Read(sec, 0, rd);
                    if (n < rd) break;
                    long target;
                    if (mode == 2352)
                    {
                        if (!Sector.IsSync(sec, 0) || sec[15] != 1) { idx++; continue; }
                        target = Sector.Lba(sec, 0) - lbaStart;
                    }
                    else target = idx;
                    idx++;
                    if (target < next || target >= total) continue;
                    while (next < target) emit(zero, 0);
                    emit(sec, mode == 2352 ? 16 : 0);
                    if ((next & 4095) == 0) Progress("원본 정리", (double)next / total);
                }
                while (next < total) emit(zero, 0);
                return res;
            }
        }

        // ---------------------------------------------------------------- 압축 파일
        List<string> ExtractZip(string zip, string dir)
        {
            var found = new List<string>();
            using (var z = ZipFile.OpenRead(zip))
            {
                long total = z.Entries.Sum(e => e.Length);
                NeedSpace(dir, total + (1L << 28), "임시 폴더");
                long done = 0;
                foreach (var e in z.Entries)
                {
                    if (e.FullName.EndsWith("/")) continue;
                    string ext = Path.GetExtension(e.Name).ToLowerInvariant();
                    if (!new[] { ".chd", ".gdi", ".cue", ".bin", ".raw", ".iso" }.Contains(ext)) continue;
                    string dst = Path.Combine(dir, e.Name);
                    using (var a = e.Open())
                    using (var b = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                    {
                        var buf = new byte[1 << 22]; int n;
                        while ((n = a.Read(buf, 0, buf.Length)) > 0)
                        {
                            if (Cancel) throw new OperationCanceledException();
                            b.Write(buf, 0, n); done += n; Progress("압축 풀기", (double)done / Math.Max(1, total));
                        }
                    }
                    if (ext == ".chd" || ext == ".gdi" || ext == ".cue") found.Add(dst);
                }
            }
            // gdi가 있으면 같은 디스크의 cue는 건너뜀
            if (found.Any(x => x.EndsWith(".gdi", StringComparison.OrdinalIgnoreCase)))
                found.RemoveAll(x => x.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
            if (found.Count == 0) throw new UserError("압축 안에 디스크 이미지(CHD/GDI/CUE)가 없습니다.");
            return found;
        }

        // ---------------------------------------------------------------- 한 파일 처리
        public void Run(string input, string outDir, bool makeGdi)
        {
            input = Path.GetFullPath(input); outDir = Path.GetFullPath(outDir);
            outRoot = outDir;
            Prepare();
            string ext = Path.GetExtension(input).ToLowerInvariant();
            Log("▶ " + Path.GetFileName(input));
            if (ext == ".zip")
            {
                string zd = Path.Combine(work, "z" + DateTime.Now.Ticks); Directory.CreateDirectory(zd);
                foreach (var f in ExtractZip(input, zd)) RunOne(f, outDir, makeGdi, Path.GetFileName(input));
                try { Directory.Delete(zd, true); } catch { }
                return;
            }
            if (ext == ".7z" || ext == ".rar")
                throw new UserError("7z/rar 압축은 먼저 풀어 주세요. (zip은 그대로 넣어도 됩니다)");
            if (ext == ".cdi" || ext == ".mds" || ext == ".nrg")
                throw new UserError("CDI/MDS 같은 CD-R(셀프부트)용 이미지는 원본과 데이터 배치가 달라 패치할 수 없습니다.\r\nRedump 원본(CHD/GDI/CUE)을 사용해 주세요.");
            RunOne(input, outDir, makeGdi, null);
        }

        void RunOne(string input, string outDir, bool makeGdi, string from)
        {
            string ext = Path.GetExtension(input).ToLowerInvariant();
            if (from != null) Log("  압축 안: " + Path.GetFileName(input));
            if (ext != ".chd" && ext != ".gdi" && ext != ".cue") throw new UserError("CHD, GDI, CUE, ZIP 파일만 넣을 수 있습니다.");
            if (Detect(input, ext) == "ss") throw new UserError("이 적용기는 드림캐스트 Rev A 전용입니다. 새턴판에는 사용할 수 없습니다.");
            int guess = Finder.Guess(from ?? input);
            if (guess > 0 && guess <= Discs.Length && !makeGdi)
            {   // 이미 만들어 둔 [KR]이 배포판과 같으면 다시 만들지 않는다
                var d0 = Discs[guess - 1]; string exist = Path.Combine(outDir, d0.Name + " [KR-VisualFix].chd");
                if (File.Exists(exist) && FileSha(exist, "기존 결과 확인") == d0.KrSha)
                {
                    Log("  " + d0.Label + " [KR]이 이미 있습니다 (배포판과 동일) — 건너뜁니다."); Outputs.Add(exist); Done.Add(d0.Key); return;
                }
            }
            string dir = Path.Combine(work, "d" + DateTime.Now.Ticks);
            Directory.CreateDirectory(dir);
            NeedSpace(dir, 3L << 30, "임시 폴더");
            NeedSpace(outDir, (makeGdi ? 2200L : 1000L) << 20, "결과 폴더");
            string gdi;

            if (ext == ".chd")
            {
                gdi = Path.Combine(dir, "disc.gdi");
                Chdman("CHD 풀기", "extractcd", "-f", "-i", input, "-o", gdi, "-ob", Path.Combine(Path.GetDirectoryName(gdi), "disc.bin"));
            }
            else if (ext == ".gdi") gdi = CopyGdiToWork(input, dir);
            else if (ext == ".cue")
            {
                string tmp = Path.Combine(dir, "in.chd");
                Chdman("CUE 읽기 (1/2)", "createcd", "-f", "-i", CopyCueToWork(input, dir), "-o", tmp);
                gdi = Path.Combine(dir, "disc.gdi");
                Chdman("CUE 읽기 (2/2)", "extractcd", "-f", "-i", tmp, "-o", gdi, "-ob", Path.Combine(Path.GetDirectoryName(gdi), "disc.bin"));
                File.Delete(tmp);
            }
            else throw new UserError("CHD, GDI, CUE, ZIP 파일만 넣을 수 있습니다.");

            var rows = ReadGdi(gdi);
            var t3row = Track3(rows);
            string t3 = Path.Combine(Path.GetDirectoryName(gdi), t3row.File);
            uint crc = FileCrc(t3, "원본 확인");
            Disc disc = Match(crc);
            if (Discs.Any(d => d.DstCrc == crc)) throw new UserError("이미 한글패치가 적용된 디스크입니다.");
            bool normalized = false;
            if (disc == null)
            {   // Redump와 형식이 다른 덤프 → 게임 데이터 기준으로 정리해서 다시 확인
                Log("  파일 형식이 Redump와 달라 게임 데이터 기준으로 정리합니다…");
                string fixedT3 = Path.Combine(Path.GetDirectoryName(gdi), "track03_fixed.bin");
                var cr = Normalize(t3, Track3Lba, Track3Sectors, new[] { Track3Sectors }, fixedT3);
                crc = cr.ContainsKey(Track3Sectors) ? cr[Track3Sectors] : 0;
                disc = Match(crc);
                if (disc != null)
                {
                    File.Delete(t3); File.Move(fixedT3, t3);
                    t3row.SecSize = 2352; t3row.Offset = 0; t3row.Lba = Track3Lba;
                    WriteGdi(gdi, rows); normalized = true;
                }
                else try { File.Delete(fixedT3); } catch { }
            }
            if (disc == null)
            {
                if (Discs.Any(d => d.DstCrc == crc)) throw new UserError("이미 한글패치가 적용된 디스크입니다.");
                throw new UserError("이 패치에 맞는 원본이 아닙니다.\r\n" +
                    "필요한 원본: Real Sound - Kaze no Regret (Japan) 드림캐스트 (Disc 1·2) (Rev A)\r\n" +
                    "Rev A가 아닌 초판, 다른 게임, 또는 수정·손상된 이미지일 수 있습니다. (게임 데이터 CRC32 " + crc.ToString("x8") + ")");
            }
            Log(string.Format("  원본 확인: {0} (Rev A){1} ✔", disc.Label, normalized ? " — 형식 정리 후 일치" : ""));
            if (Done.Contains(disc.Key)) { Log("  " + disc.Label + "는 방금 만들었으므로 건너뜁니다."); try { Directory.Delete(dir, true); } catch { } return; }

            ApplyInPlace(t3, Res(disc.Res));
            uint after = FileCrc(t3, "패치 결과 확인");
            if (after != disc.DstCrc) throw new Exception("패치 결과가 예상과 다릅니다 (CRC32 " + after.ToString("x8") + ").");
            Log("  한글 패치 적용 ✔");

            // 저밀도 영역(1·2번 트랙)이 Redump와 같으면 결과 CHD는 배포판과 완전히 같아진다
            bool lowSame = rows.Length == 3 && rows[0].No == 1 && rows[1].No == 2
                && FileCrc(Path.Combine(Path.GetDirectoryName(gdi), rows[0].File), null) == disc.T1Crc
                && FileCrc(Path.Combine(Path.GetDirectoryName(gdi), rows[1].File), null) == disc.T2Crc;

            Directory.CreateDirectory(outDir);
            string chd = Path.Combine(outDir, disc.Name + " [KR-VisualFix].chd");
            if (File.Exists(chd)) throw new UserError("같은 이름의 결과 파일이 이미 있습니다. 보존을 위해 덮어쓰지 않습니다. 다른 결과 폴더를 선택해 주세요.");
            string tmpChd = Path.Combine(dir, "out.chd");
            Chdman("CHD 만들기", "createcd", "-f", "-i", gdi, "-o", tmpChd, "-np", "6");
            Chdman("CHD 무결성 검사", "verify", "-i", tmpChd);
            string sha = FileSha(tmpChd, "결과 확인");
            MoveOver(tmpChd, chd);
            Log("  완성: " + chd); Outputs.Add(chd);
            if (sha == disc.KrSha) Log("  배포판 [KR] CHD와 완전히 같은 파일입니다 ✔");
            else if (!lowSame) Log("  게임 데이터는 배포판과 같습니다 ✔ (경고음성 트랙 등 1·2번 트랙은 넣은 원본 그대로라 파일 해시만 다름)");
            else Log("  게임 데이터는 배포판과 같습니다 ✔");

            if (makeGdi)
            {
                string g = Path.Combine(outDir, disc.Name + " [KR-VisualFix] (GDI)");
                if (Directory.Exists(g)) throw new UserError("같은 이름의 GDI 결과 폴더가 이미 있습니다. 다른 결과 폴더를 선택하세요.");
                Directory.CreateDirectory(g);
                foreach (var r in rows)
                {
                    string name = string.Format("track{0:00}{1}", r.No, r.Type == 0 ? ".raw" : ".bin");
                    MoveOver(Path.Combine(Path.GetDirectoryName(gdi), r.File), Path.Combine(g, name));
                    r.File = name;
                }
                WriteGdi(Path.Combine(g, "disc.gdi"), rows);
                Log("  GDEMU용 GDI 폴더: " + g);
            }
            try { Directory.Delete(dir, true); } catch { }
            Done.Add(disc.Key);
        }

        // ---------------------------------------------------------------- 기종 판별
        string Detect(string input, string ext)
        {
            if (ext == ".gdi") return "dc";
            if (ext == ".cue")
            {
                string t = File.ReadAllText(input);
                return t.IndexOf("HIGH-DENSITY", StringComparison.OrdinalIgnoreCase) >= 0 ? "dc" : "ss";
            }
            string info = ChdmanInfo(input);
            if (info.Contains("CHGD")) return "dc";
            if (info.Contains("CHT2") || info.Contains("CHTR") || info.Contains("CHCD")) return "ss";
            throw new UserError("CD/GD-ROM 이미지가 아닌 CHD입니다.");
        }

        // ---------------------------------------------------------------- 새턴
        class CueTrack { public int No; public string Type, File; public long Idx0 = -1, Idx1 = -1, Pregap; }

        static long Msf(string t)
        {
            var p = t.Split(':'); return (long.Parse(p[0]) * 60 + long.Parse(p[1])) * 75 + long.Parse(p[2]);
        }

        static List<CueTrack> ParseCue(string cue)
        {
            var list = new List<CueTrack>(); string file = null; CueTrack cur = null;
            foreach (var raw in File.ReadAllLines(cue, Encoding.UTF8))
            {
                var l = raw.Trim(); Match m;
                if ((m = Regex.Match(l, @"^FILE\s+(""[^""]+""|\S+)", RegexOptions.IgnoreCase)).Success) file = m.Groups[1].Value.Trim('"');
                else if ((m = Regex.Match(l, @"^TRACK\s+(\d+)\s+(\S+)", RegexOptions.IgnoreCase)).Success)
                    list.Add(cur = new CueTrack { No = int.Parse(m.Groups[1].Value), Type = m.Groups[2].Value.ToUpperInvariant(), File = file });
                else if (cur != null && (m = Regex.Match(l, @"^INDEX\s+0*(\d+)\s+(\d+:\d+:\d+)", RegexOptions.IgnoreCase)).Success)
                {
                    long v = Msf(m.Groups[2].Value);
                    if (m.Groups[1].Value == "0") cur.Idx0 = v; else if (m.Groups[1].Value == "1") cur.Idx1 = v;
                }
                else if (cur != null && (m = Regex.Match(l, @"^PREGAP\s+(\d+:\d+:\d+)", RegexOptions.IgnoreCase)).Success) cur.Pregap = Msf(m.Groups[1].Value);
            }
            return list;
        }

        string FindFile(string dir, string name)
        {
            string p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
            var alt = Directory.GetFiles(dir).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
            if (alt == null) throw new UserError("CUE에 적힌 파일이 없습니다: " + name);
            return alt;
        }

        void CopyRange(string src, long ofs, long len, string dst, bool append, string stage)
        {
            using (var a = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (var b = new FileStream(dst, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                a.Position = ofs; if (len < 0) len = a.Length - ofs;
                var buf = new byte[1 << 22]; long done = 0;
                while (done < len)
                {
                    if (Cancel) throw new OperationCanceledException();
                    int n = a.Read(buf, 0, (int)Math.Min(buf.Length, len - done)); if (n <= 0) break;
                    b.Write(buf, 0, n); done += n; Progress(stage, (double)done / Math.Max(1, len));
                }
            }
        }

        void RunSaturn(string input, string ext, string outDir, string from)
        {
            int guess = Finder.Guess(from ?? input);
            if (guess >= 1 && guess <= SsDiscs.Length)
            {   // 이미 만들어 둔 [KR]이 정상이면 다시 만들지 않는다
                var d0 = SsDiscs[guess - 1]; string b0 = Path.Combine(outDir, d0.Name + " [KR]");
                if (File.Exists(b0 + ".cue") && File.Exists(b0 + " (Track 2).bin") && File.Exists(b0 + " (Track 1).bin")
                    && FileCrc(b0 + " (Track 1).bin", "기존 결과 확인") == d0.DstCrc)
                {
                    Log("  " + d0.Label + " [KR]이 이미 있습니다 (배포판과 동일) — 건너뜁니다."); Outputs.Add(b0 + ".cue"); Done.Add(d0.Key); return;
                }
            }
            string dir = Path.Combine(work, "s" + DateTime.Now.Ticks);
            Directory.CreateDirectory(dir);
            NeedSpace(dir, 1600L << 20, "임시 폴더");
            NeedSpace(outDir, 800L << 20, "결과 폴더");

            string cue = input, cueDir = Path.GetDirectoryName(input);
            if (ext == ".chd")
            {
                cue = Path.Combine(dir, "x.cue"); cueDir = dir;
                Chdman("CHD 풀기", "extractcd", "-f", "-i", input, "-o", cue);
            }
            var tr = ParseCue(cue);
            var tk1 = tr.FirstOrDefault(x => x.No == 1); var tk2 = tr.FirstOrDefault(x => x.No == 2);
            if (tk1 == null || tk1.File == null) throw new UserError("CUE에서 1번 트랙을 찾을 수 없습니다.");
            int ss1 = tk1.Type.EndsWith("/2048") ? 2048 : 2352;
            string t1 = Path.Combine(dir, "t1.bin"), t2 = Path.Combine(dir, "t2.bin");
            string f1 = FindFile(cueDir, tk1.File);
            if (tk2 != null && tk2.File != null && tk2.File != tk1.File)
            {   // Redump처럼 트랙마다 파일
                CopyWithProgress(f1, t1, "1번 트랙 복사");
                string f2 = FindFile(cueDir, tk2.File);
                if (tk2.Idx0 < 0 && tk2.Pregap > 0) File.WriteAllBytes(t2, new byte[tk2.Pregap * 2352]);
                CopyRange(f2, 0, -1, t2, tk2.Idx0 < 0 && tk2.Pregap > 0, "2번 트랙 복사");
            }
            else if (tk2 != null)
            {   // 한 파일에 트랙이 이어 붙은 경우: 2번 트랙(프리갭 포함) 시작에서 자른다
                long start2 = tk2.Idx0 >= 0 ? tk2.Idx0 : tk2.Idx1;
                CopyRange(f1, 0, start2 * ss1, t1, false, "1번 트랙 분리");
                if (tk2.Idx0 < 0) File.WriteAllBytes(t2, new byte[Math.Max(tk2.Pregap, 150) * 2352]);
                CopyRange(f1, start2 * ss1, -1, t2, tk2.Idx0 < 0, "2번 트랙 분리");
            }
            else { CopyWithProgress(f1, t1, "1번 트랙 복사"); File.WriteAllBytes(t2, new byte[0]); }

            uint crc = FileCrc(t1, "원본 확인");
            if (SsDiscs.Any(d => d.DstCrc == crc)) throw new UserError("이미 한글패치가 적용된 새턴 디스크입니다.");
            Disc disc = SsDiscs.FirstOrDefault(d => d.SrcCrc == crc);
            bool normalized = false;
            if (disc == null)
            {
                Log("  파일 형식이 Redump와 달라 게임 데이터 기준으로 정리합니다…");
                string fx = Path.Combine(dir, "t1_fixed.bin");
                var cps = SsDiscs.Select(d => d.Sectors).ToArray();
                var cr = Normalize(t1, 0, cps.Max(), cps, fx);
                disc = SsDiscs.FirstOrDefault(d => cr.ContainsKey(d.Sectors) && cr[d.Sectors] == d.SrcCrc);
                if (disc != null)
                {
                    using (var f = new FileStream(fx, FileMode.Open, FileAccess.Write)) f.SetLength(disc.Sectors * 2352);
                    File.Delete(t1); File.Move(fx, t1); normalized = true;
                }
                else
                {
                    try { File.Delete(fx); } catch { }
                    if (cr.Values.Any(v => SsDiscs.Any(d => d.DstCrc == v))) throw new UserError("이미 한글패치가 적용된 새턴 디스크입니다.");
                }
            }
            if (disc == null)
                throw new UserError("이 패치에 맞는 원본이 아닙니다.\r\n" +
                    "필요한 원본: Real Sound - Kaze no Regret (Japan) 새턴 (Disc 1~4) — Redump 덤프\r\n" +
                    "다른 판이나 다른 게임, 또는 수정·손상된 이미지일 수 있습니다. (1번 트랙 CRC32 " + crc.ToString("x8") + ")");
            Log(string.Format("  원본 확인: {0}{1} ✔", disc.Label, normalized ? " — 형식 정리 후 일치" : ""));
            if (Done.Contains(disc.Key)) { Log("  " + disc.Label + "는 방금 만들었으므로 건너뜁니다."); try { Directory.Delete(dir, true); } catch { } return; }

            ApplyInPlace(t1, Res(disc.Res));
            uint after = FileCrc(t1, "패치 결과 확인");
            if (after != disc.DstCrc) throw new Exception("패치 결과가 예상과 다릅니다 (CRC32 " + after.ToString("x8") + ").");
            Log("  한글 패치 적용 ✔");
            bool t2Same = FileCrc(t2, null) == disc.T2Crc;

            Directory.CreateDirectory(outDir);
            string b = Path.Combine(outDir, disc.Name + " [KR]"), n = disc.Name + " [KR]";
            MoveOver(t1, b + " (Track 1).bin");
            MoveOver(t2, b + " (Track 2).bin");
            File.WriteAllText(b + ".cue", (disc.Catalog ? "CATALOG 0000000000000\n" : "") +
                "FILE \"" + n + " (Track 1).bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n" +
                "FILE \"" + n + " (Track 2).bin\" BINARY\n  TRACK 02 AUDIO\n    INDEX 00 00:00:00\n    INDEX 01 00:02:00\n", new UTF8Encoding(false));
            Log("  완성: " + b + ".cue"); Outputs.Add(b + ".cue");
            Log(t2Same ? "  배포판 [KR]과 완전히 같은 파일입니다 ✔" : "  게임 데이터는 배포판과 같습니다 ✔ (2번 오디오 트랙은 넣은 원본 그대로)");
            try { Directory.Delete(dir, true); } catch { }
            Done.Add(disc.Key);
        }

        static Disc Match(uint crc) { return Discs.FirstOrDefault(d => d.SrcCrc == crc); }
        public static Disc[] All { get { return Discs.Concat(SsDiscs).ToArray(); } }

        void MoveOver(string src, string dst)
        {
            if (File.Exists(dst)) File.Delete(dst);
            if (string.Equals(Path.GetPathRoot(Path.GetFullPath(src)), Path.GetPathRoot(Path.GetFullPath(dst)), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Move(src, dst); return; } catch (IOException) { }
            }
            CopyWithProgress(src, dst, "결과 저장"); File.Delete(src);
        }

        // 자체 시험용: 트랙 파일을 정규화해서 CRC만 출력
        public uint NormalizeOnly(string src, string dst)
        {
            outRoot = Path.GetDirectoryName(Path.GetFullPath(dst));
            var r = Normalize(src, Track3Lba, Track3Sectors, new[] { Track3Sectors }, dst); return r.ContainsKey(Track3Sectors) ? r[Track3Sectors] : 0;
        }

        public string NormalizeSaturn(string src, string dst)
        {
            outRoot = Path.GetDirectoryName(Path.GetFullPath(dst));
            var cps = SsDiscs.Select(d => d.Sectors).ToArray();
            var r = Normalize(src, 0, cps.Max(), cps, dst);
            return string.Join(" ", SsDiscs.Select(d => d.No + ":" + (r.ContainsKey(d.Sectors) ? r[d.Sectors].ToString("x8") : "-")));
        }
    }
}
