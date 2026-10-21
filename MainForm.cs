// 바람의 리글렛 (드림캐스트) 한글패치 적용기 — 화면
// .NET Framework 4 (윈도우 10/11 기본 탑재) WinForms, C# 5 — Windows 기본 csc.exe로 빌드 (build.bat).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("바람의 리글렛 DC 한글패치 적용기")]
[assembly: AssemblyProduct("Kaze no Regret DC KR Patcher")]
[assembly: AssemblyVersion("1.5.0.0")]

namespace KazeKR
{
    class MainForm : Form
    {
        static readonly string[] Exts = { ".chd", ".gdi", ".cue", ".zip", ".7z", ".rar", ".cdi" };
        ListBox files = new ListBox();
        TextBox log = new TextBox();
        ProgressBar bar = new ProgressBar();
        Label stage = new Label();
        Button go = new Button(), add = new Button(), clear = new Button(), pick = new Button();
        CheckBox gdi = new CheckBox();
        TextBox outDir = new TextBox();
        RadioButton sameDir = new RadioButton(), otherDir = new RadioButton();
        Patcher patcher;
        Thread worker;
        StreamWriter logFile;
        string jobLabel = "";
        bool captureMode;
        protected override bool ShowWithoutActivation { get { return captureMode; } }

        public MainForm(string[] args)
        {
            captureMode = args.Contains("--screenshot-mode");
            Text = "바람의 리글렛 한글패치 적용기 (드림캐스트 비주얼 모드) v1.5";
            Font = new Font("맑은 고딕", 9.5f);
            ClientSize = new Size(640, 580); MinimumSize = new Size(560, 540);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            AllowDrop = true;
            DragEnter += OnDragEnter; DragDrop += OnDragDrop;

            var head = new Label { Text = "① 원본 롬을 끌어다 놓으세요 (CHD·GDI·CUE·ZIP) — 적용기와 같은 폴더에 두면 자동으로 시작", Left = 12, Top = 12, Width = 616, Height = 22, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            files.SetBounds(12, 38, 616, 96); files.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            files.AllowDrop = true; files.DragEnter += OnDragEnter; files.DragDrop += OnDragDrop;
            files.HorizontalScrollbar = true;
            add.Text = "파일 선택…"; add.SetBounds(12, 140, 110, 30);
            add.Click += delegate
            {
                using (var d = new OpenFileDialog { Multiselect = true, Filter = "드림캐스트 이미지 (*.chd;*.gdi;*.cue;*.zip)|*.chd;*.gdi;*.cue;*.zip|모든 파일|*.*" })
                    if (d.ShowDialog(this) == DialogResult.OK) AddFiles(d.FileNames);
            };
            clear.Text = "목록 비우기"; clear.SetBounds(128, 140, 110, 30);
            clear.Click += delegate { files.Items.Clear(); };

            var h2 = new Label { Text = "② 어디에 만들까요?", Left = 12, Top = 182, Width = 300, Height = 22 };
            sameDir.Text = "원본 파일과 같은 폴더"; sameDir.SetBounds(24, 206, 220, 24); sameDir.Checked = true;
            otherDir.Text = "다른 폴더:"; otherDir.SetBounds(24, 232, 90, 24);
            outDir.SetBounds(116, 232, 400, 24); outDir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            outDir.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (captureMode) outDir.Text = @"C:\Games\Kaze";
            pick.Text = "찾아보기…"; pick.SetBounds(522, 230, 106, 28); pick.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pick.Click += delegate
            {
                using (var d = new FolderBrowserDialog { SelectedPath = outDir.Text })
                    if (d.ShowDialog(this) == DialogResult.OK) { outDir.Text = d.SelectedPath; otherDir.Checked = true; }
            };
            gdi.Text = "GDI 폴더도 함께 만들기 (선택 사항 / 실기 동작은 미검증)"; gdi.SetBounds(24, 262, 600, 24);

            go.Text = "③ 한글패치 만들기"; go.SetBounds(12, 296, 616, 42); go.Font = new Font("맑은 고딕", 12f, FontStyle.Bold);
            go.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            go.Click += delegate { if (worker == null) { StopCountdown(); Start(); } else { patcher.Cancel = true; go.Enabled = false; } };

            stage.SetBounds(12, 346, 616, 20); stage.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bar.SetBounds(12, 368, 616, 20); bar.Maximum = 1000; bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.SetBounds(12, 396, 616, 172);
            log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            log.Text = "원본: Real Sound - Kaze no Regret (Japan) — 드림캐스트 Disc 1·2 (Rev A) 전용\r\n" +
                       "결과: [KR-VisualFix].chd — Flycast에서 풀 프레임 버퍼를 켜고 비주얼 모드로 실행하세요.\r\n" +
                       "원본 파일은 보존합니다. 한 장에 수 분이 걸리며 임시 공간 약 3GB가 필요합니다.\r\n";

            var licenses = new LinkLabel { Text = "라이선스·권리 고지", AutoSize = true, Left = 440, Top = 178 };
            licenses.LinkClicked += delegate { LicenseDialog.Show(this); };
            Controls.Add(licenses);
            Controls.AddRange(new Control[] { head, files, add, clear, h2, sameDir, otherDir, outDir, pick, gdi, go, stage, bar, log });

            var list = new List<string>(args);
            list.Remove("--screenshot-mode");
            bool auto = list.Remove("--autostart");             // 자동 테스트용
            stopAuto.Text = "자동 시작 멈추기"; stopAuto.SetBounds(480, 346, 148, 20); stopAuto.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            stopAuto.TextAlign = ContentAlignment.MiddleRight; stopAuto.Visible = false;
            stopAuto.LinkClicked += delegate { StopCountdown(); stage.Text = "준비되면 [한글패치 만들기]를 누르세요."; };
            Controls.Add(stopAuto); stopAuto.BringToFront();
            add.Click += delegate { StopCountdown(); }; clear.Click += delegate { StopCountdown(); };
            if (list.Count > 0) AddFiles(list.ToArray()); else if (!auto && !captureMode) Shown += delegate { AutoFind(); };
            if (auto) Shown += delegate { otherDir.Checked = true; outDir.Text = Path.Combine(Path.GetTempPath(), "KazeKR_guitest"); Start(); };
        }

        void OnDragEnter(object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop) && worker == null) e.Effect = DragDropEffects.Copy; }
        void OnDragDrop(object s, DragEventArgs e) { AddFiles((string[])e.Data.GetData(DataFormats.FileDrop)); }

        // 적용기와 같은 폴더에 원본이 있으면 목록에 올리고 5초 뒤 자동 시작.
        // 다른 곳은 사용자가 허락했을 때만(다운로드·바탕화면·문서, 파일 이름만 확인) 찾는다.
        System.Windows.Forms.Timer countdown;
        int left;
        LinkLabel stopAuto = new LinkLabel();

        void AutoFind()
        {
            List<string> found;
            try { found = Finder.FindHere(); } catch { found = new List<string>(); }
            if (found.Count > 0) { Found(found, "적용기 폴더에서"); return; }
            stage.Text = "원본이 없습니다 — 끌어다 놓거나, 적용기 폴더에 넣고 다시 실행하세요.";
            var places = Finder.UserPlaces();
            if (places.Length == 0) return;
            var ask = "적용기와 같은 폴더에서 원본 롬을 찾지 못했습니다.\r\n\r\n" +
                      "다음 폴더에서 찾아볼까요?\r\n  " + string.Join("\r\n  ", places) + "\r\n\r\n" +
                      "파일 이름만 확인하며, 아무것도 바꾸거나 밖으로 보내지 않습니다.\r\n" +
                      "[아니요]를 누르면 원본을 직접 끌어다 놓으면 됩니다.";
            if (MessageBox.Show(this, ask, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            stage.Text = "원본 롬을 찾는 중…"; go.Enabled = false;
            var t = new Thread(delegate()
            {
                List<string> more;
                try { more = Finder.FindInUserFolders(s => BeginInvoke(new Action(delegate { stage.Text = s; }))); } catch { more = new List<string>(); }
                BeginInvoke(new Action(delegate
                {
                    go.Enabled = true;
                    if (more.Count == 0) { stage.Text = "원본을 찾지 못했습니다 — 원본 파일을 이 창에 끌어다 놓으세요."; return; }
                    Found(more, "내 폴더에서");
                }));
            });
            t.IsBackground = true; t.Start();
        }

        void Found(List<string> found, string where)
        {
                {
                    AddFiles(found.ToArray());
                    Say(where + " 원본 " + found.Count + "개를 찾았습니다:"); foreach (var f in found) Say("  " + f);
                    left = 5; countdown = new System.Windows.Forms.Timer { Interval = 1000 };
                    countdown.Tick += delegate
                    {
                        if (--left <= 0) { StopCountdown(); Start(); }
                        else go.Text = string.Format("{0}초 뒤 자동으로 시작합니다  (지금 시작하려면 누르세요)", left);
                    };
                    go.Text = string.Format("{0}초 뒤 자동으로 시작합니다  (지금 시작하려면 누르세요)", left);
                    stage.Text = "찾은 원본으로 한글패치를 만듭니다.";
                    stopAuto.Visible = true;
                    countdown.Start();
                }
        }

        void StopCountdown()
        {
            if (countdown != null) { countdown.Stop(); countdown = null; }
            stopAuto.Visible = false;
            if (worker == null) go.Text = "③ 한글패치 만들기";
        }

        void AddFiles(string[] paths)
        {
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    var found = new List<string>(); Finder.Scan(p, 2, found, System.Diagnostics.Stopwatch.StartNew(), 10000); found = Finder.Choose(found);
                    if (found.Count == 0) foreach (var f in Directory.GetFiles(p)) if (Exts.Contains(Path.GetExtension(f).ToLowerInvariant())) found.Add(f);
                    AddFiles(found.ToArray()); continue;
                }
                string e = Path.GetExtension(p).ToLowerInvariant();
                if (!Exts.Contains(e)) { if (e == ".bin" || e == ".raw") Say("※ bin 파일 말고 같은 폴더의 .cue 또는 .gdi 파일을 넣어 주세요: " + Path.GetFileName(p)); continue; }
                if (e == ".cue" && File.Exists(Path.ChangeExtension(p, ".gdi"))) continue;
                if (!files.Items.Contains(p)) files.Items.Add(p);
            }
        }

        void Say(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Say), s); return; }
            log.AppendText(s + "\r\n");
            try { if (logFile != null) logFile.WriteLine(s); } catch { }
        }

        double lastShown = -1; string lastStage;
        void Prog(string st, double v)
        {
            if (st == lastStage && Math.Abs(v - lastShown) < 0.004 && v < 1) return;
            lastStage = st; lastShown = v;
            BeginInvoke(new Action(delegate
            {
                stage.Text = jobLabel + (v >= 0 ? string.Format("{0}… {1:0}%", st, v * 100) : st + "…");
                bar.Value = Math.Max(0, Math.Min(1000, (int)(v * 1000)));
            }));
        }

        void OpenLog()
        {
            foreach (var d in new[] { AppDomain.CurrentDomain.BaseDirectory, Path.GetTempPath() })
            {
                try { logFile = new StreamWriter(Path.Combine(d, "적용기_로그.txt"), true, new UTF8Encoding(true)) { AutoFlush = true }; break; } catch { }
            }
            string os = Environment.OSVersion.ToString();
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    os = k.GetValue("ProductName") + " (빌드 " + k.GetValue("CurrentBuild") + ")";
            }
            catch { }
            Say("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " / " + os + " / 적용기 v1.5 ====");
        }

        void Start()
        {
            if (files.Items.Count == 0) { MessageBox.Show(this, "먼저 원본 롬 파일을 끌어다 놓거나 [파일 선택]으로 고르세요.", Text); return; }
            var list = new List<string>(); foreach (string f in files.Items) list.Add(f);
            bool mk = gdi.Checked, same = sameDir.Checked; string other = outDir.Text;
            if (!same && !Directory.Exists(other))
            {
                try { Directory.CreateDirectory(other); } catch { MessageBox.Show(this, "결과 폴더를 만들 수 없습니다: " + other, Text); return; }
            }
            OpenLog();
            patcher = new Patcher(); patcher.Log = Say; patcher.Progress = Prog;
            go.Text = "취소"; add.Enabled = clear.Enabled = pick.Enabled = gdi.Enabled = sameDir.Enabled = otherDir.Enabled = false;
            worker = new Thread(delegate()
            {
                int ok = 0, fail = 0; bool canceled = false;
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    jobLabel = list.Count > 1 ? string.Format("[{0}/{1}] ", i + 1, list.Count) : "";
                    string od = same ? Path.GetDirectoryName(f) : other;
                    if (same && !Writable(od))
                    {
                        od = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "바람의 리글렛 한글패치");
                        Say("  원본 폴더에 저장할 수 없어 바탕화면의 \"바람의 리글렛 한글패치\" 폴더에 만듭니다.");
                    }
                    try { patcher.Run(f, od, mk); ok++; }
                    catch (OperationCanceledException) { Say("  취소했습니다."); canceled = true; break; }
                    catch (UserError ex) { Say("  ✖ " + ex.Message); fail++; }
                    catch (UnauthorizedAccessException) { Say("  ✖ 결과를 저장할 수 없는 폴더입니다. [다른 폴더]를 골라 주세요."); fail++; }
                    catch (IOException ex) { Say("  ✖ 파일 오류: " + ex.Message); fail++; }
                    catch (Exception ex) { Say("  ✖ 예상치 못한 오류: " + ex.Message); try { logFile.WriteLine(ex.ToString()); } catch { } fail++; }
                }
                patcher.Cleanup();
                var done = patcher.Done.Distinct().ToList();
                var dc = done.Where(x => x.StartsWith("dc")).Select(x => int.Parse(x.Substring(2))).OrderBy(x => x).ToList();
                var ss = done.Where(x => x.StartsWith("ss")).Select(x => int.Parse(x.Substring(2))).OrderBy(x => x).ToList();
                string summary;
                if (canceled) summary = "취소되었습니다.";
                else if (done.Count == 0) summary = "만들어진 디스크가 없습니다. 위 기록의 ✖ 내용을 확인해 주세요.";
                else
                {
                    var parts = new List<string>();
                    if (dc.Count > 0) parts.Add("드림캐스트 디스크 " + string.Join("·", dc));
                    if (ss.Count > 0) parts.Add("새턴 디스크 " + string.Join("·", ss));
                    summary = "완성: " + string.Join(" / ", parts) + "  — [KR-VisualFix] 파일을 Flycast 비주얼 모드로 실행하세요.";
                    var miss = new List<string>();
                    if (dc.Count > 0) foreach (var n in new[] { 1, 2 }) if (!dc.Contains(n)) miss.Add("드림캐스트 디스크 " + n);
                    if (ss.Count > 0) foreach (var n in new[] { 1, 2, 3, 4 }) if (!ss.Contains(n)) miss.Add("새턴 디스크 " + n);
                    if (miss.Count > 0) summary += "\r\n※ 끝까지 플레이하려면 " + string.Join(", ", miss) + "도 패치해 주세요.";
                }
                Say(""); Say(summary);
                try { logFile.Close(); } catch { } logFile = null;
                BeginInvoke(new Action(delegate
                {
                    worker = null; jobLabel = "";
                    go.Text = "③ 한글패치 만들기"; go.Enabled = add.Enabled = clear.Enabled = pick.Enabled = gdi.Enabled = sameDir.Enabled = otherDir.Enabled = true;
                    stage.Text = fail == 0 && !canceled ? "모두 끝났습니다." : string.Format("끝 — 성공 {0}개, 실패 {1}개", ok, fail);
                    if (fail == 0 && !canceled) bar.Value = 1000;
                    if (patcher.Outputs.Count > 0)
                    {
                        if (MessageBox.Show(this, summary + "\r\n\r\n만든 파일이 있는 폴더를 열까요?", Text, MessageBoxButtons.YesNo,
                                fail == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning) == DialogResult.Yes)
                            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + patcher.Outputs[patcher.Outputs.Count - 1] + "\""); } catch { }
                    }
                    else MessageBox.Show(this, summary, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }));
            });
            worker.IsBackground = true; worker.Start();
        }

        static bool Writable(string dir)
        {
            try
            {
                string t = Path.Combine(dir, ".kazekr_write_test");
                File.WriteAllText(t, ""); File.Delete(t); return true;
            }
            catch { return false; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (worker != null)
            {
                if (MessageBox.Show(this, "작업 중입니다. 취소하고 닫을까요?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) { e.Cancel = true; return; }
                patcher.Cancel = true; worker.Join(5000); patcher.Cleanup();
            }
            base.OnFormClosing(e);
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--screenshot") {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var f = new MainForm(new string[] { "--screenshot-mode" })) using (var b = new Bitmap(f.Width, f.Height)) {
                    f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-30000, -30000);
                    f.Show(); Application.DoEvents(); f.PerformLayout();
                    f.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height)); b.Save(args[1]);
                } return 0;
            }
            if (args.Length > 0 && args[0] == "--cli") return Cli(args);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
            return 0;
        }

        // 명령줄: 적용기.exe --cli [--gdi] [--out 폴더] 파일…  /  --cli --normalize 트랙 출력   (자동 테스트·고급 사용자용)
        static int Cli(string[] args)
        {
            var o = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            if (args.Length == 2 && args[1] == "--find")
            {
                foreach (var f in Finder.FindHere()) o.WriteLine("FOUND " + f); return 0;
            }
            if (args.Length == 4 && args[1] == "--normalize-ss")
            {
                var ps = new Patcher(); o.WriteLine(ps.NormalizeSaturn(args[2], args[3])); return 0;
            }
            if (args.Length == 4 && args[1] == "--normalize")
            {
                var pn = new Patcher(); o.WriteLine(pn.NormalizeOnly(args[2], args[3]).ToString("x8")); return 0;
            }
            bool mk = false; string od = null; var list = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--gdi") mk = true;
                else if (args[i] == "--out") od = args[++i];
                else list.Add(args[i]);
            }
            var p = new Patcher(); p.Log = delegate(string s) { o.WriteLine(s); };
            int rc = 0;
            foreach (var f in list)
            {
                try { p.Run(f, od ?? Path.GetDirectoryName(Path.GetFullPath(f)), mk); }
                catch (Exception ex) { o.WriteLine("ERROR " + ex.Message); rc = 1; }
            }
            p.Cleanup();
            return rc;
        }
    }
}
