using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
namespace KazeKR {
    public static class LicenseDialog {
        public static void Show(IWin32Window owner) {
            using (var dialog = new Form { Text = "라이선스 및 권리 고지", Size = new Size(820, 650), StartPosition = FormStartPosition.CenterParent })
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LICENSES.txt"))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true)) {
                var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 9), Text = reader.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n") };
                dialog.Controls.Add(text); dialog.ShowDialog(owner);
            }
        }
    }
}
