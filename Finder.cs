// 원본 자동 탐색: 적용기 폴더 → 내 문서/다운로드/바탕화면 → 다른 드라이브 순으로 이 게임 이미지를 찾는다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KazeKR
{
    static class Finder
    {
        static readonly string[] Exts = { ".chd", ".gdi", ".cue", ".zip" };

        public static bool Ours(string path)
        {   // TOSEC·Redump·GDEMU 폴더 이름에 Kaze/Regret이 들어간다
            string n = (Path.GetFileName(path) + " " + Path.GetFileName(Path.GetDirectoryName(path) ?? "")).ToLowerInvariant();
            return n.Contains("kaze") || n.Contains("regret") || n.Contains("리글렛") || n.Contains("리그렛");
        }

        static bool Candidate(string f)
        {
            string e = Path.GetExtension(f).ToLowerInvariant(), name = Path.GetFileName(f);
            if (!Exts.Contains(e) || f.IndexOf("[KR", StringComparison.OrdinalIgnoreCase) >= 0 || !Ours(f)) return false;
            if (e != ".zip") return true;
            try
            {   // zip은 안에 디스크 이미지가 있을 때만
                using (var z = System.IO.Compression.ZipFile.OpenRead(f))
                    return z.Entries.Any(x => { string xe = Path.GetExtension(x.Name).ToLowerInvariant();
                        return (xe == ".chd" || xe == ".gdi" || xe == ".cue") && x.Name.IndexOf("[KR", StringComparison.OrdinalIgnoreCase) < 0; });
            }
            catch { return false; }
        }

        /// 이름으로 짐작한 디스크 번호 (모르면 0)
        public static int Guess(string f)
        {
            string n = Path.GetFileName(f) + " " + Path.GetFileName(Path.GetDirectoryName(f) ?? "");
            var m = Regex.Match(n, @"(?:disc|disk|cd|디스크)\s*[_\-]?\s*([1-4])", RegexOptions.IgnoreCase);
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        /// 이름으로 짐작한 기종: Redump 드캐판은 "(Rev A)", 새턴판은 "(2M…)"이 붙는다
        public static string Family(string f)
        {
            string n = Path.GetFileName(f) + " " + Path.GetFileName(Path.GetDirectoryName(f) ?? "");
            if (n.IndexOf("(2M", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("새턴", StringComparison.Ordinal) >= 0 || n.IndexOf("saturn", StringComparison.OrdinalIgnoreCase) >= 0) return "ss";
            if (n.IndexOf("Rev A", StringComparison.OrdinalIgnoreCase) >= 0 || Path.GetExtension(f).ToLowerInvariant() == ".gdi"
                || n.IndexOf("드림캐스트", StringComparison.Ordinal) >= 0 || n.IndexOf("dreamcast", StringComparison.OrdinalIgnoreCase) >= 0) return "dc";
            return "?";
        }

        static int Rank(string f)
        {
            switch (Path.GetExtension(f).ToLowerInvariant()) { case ".chd": return 0; case ".gdi": return 1; case ".cue": return 2; default: return 3; }
        }

        public static void Scan(string dir, int depth, List<string> found, Stopwatch sw, int budgetMs)
        {
            if (sw.ElapsedMilliseconds > budgetMs) return;
            string dn = Path.GetFileName(dir).ToLowerInvariant();
            if (dn.StartsWith("$") || dn == "windows" || dn == "program files" || dn == "program files (x86)" || dn == "programdata"
                || dn == "appdata" || dn == "node_modules" || dn.StartsWith(".") || dir.IndexOf("[KR", StringComparison.OrdinalIgnoreCase) >= 0) return;
            try { foreach (var f in Directory.GetFiles(dir)) if (Candidate(f)) found.Add(f); } catch { return; }
            if (depth <= 0) return;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return; }
            foreach (var d in subs) Scan(d, depth - 1, found, sw, budgetMs);
        }

        /// 적용기와 같은 폴더(와 그 아래 두 단계)만 본다 — 실행할 때 자동으로 보는 곳은 여기뿐.
        public static List<string> FindHere()
        {
            var found = new List<string>();
            Scan(AppDomain.CurrentDomain.BaseDirectory, 2, found, Stopwatch.StartNew(), 8000);
            return Choose(found);
        }

        public static string[] UserPlaces()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[] { Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                           Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) }
                   .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// 사용자가 [찾기]를 허락했을 때만: 다운로드·바탕화면·문서 (파일 이름만 확인)
        public static List<string> FindInUserFolders(Action<string> status)
        {
            var sw = Stopwatch.StartNew(); var found = new List<string>();
            foreach (var p in UserPlaces()) { status("찾는 중: " + p); Scan(p, 4, found, sw, 20000); }
            return Choose(found);
        }

        public static List<string> Choose(List<string> found)
        {
            found = found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // gdi와 같은 폴더의 cue/bin 묶음은 하나로
            found.RemoveAll(f => Path.GetExtension(f).ToLowerInvariant() == ".cue" && File.Exists(Path.ChangeExtension(f, ".gdi")));
            var pick = new List<string>();
            foreach (var g in found.GroupBy(f => Family(f) + Guess(f)))
            {
                var best = g.OrderBy(Rank).ToList();
                if (Guess(g.First()) == 0 || Family(g.First()) == "?") pick.AddRange(best);           // 번호를 모르면 모두 (적용기가 내용으로 판별, 같은 디스크 중복은 건너뜀)
                else pick.Add(best[0]);
            }
            return pick.OrderBy(f => Guess(f) == 0 ? 9 : Guess(f)).ToList();
        }
    }
}
