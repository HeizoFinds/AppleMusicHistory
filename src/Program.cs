using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AppleMusicHistory
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool background = args.Contains("--tray");
            bool first;
            using (var open = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AppleMusicHistory.Show", out first))
            {
                if (!first) { if (!background) open.Set(); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppleMusicHistory", "history.sqlite3");
                try
                {
                    using (var db = new History(path))
                    using (var window = new HistoryWindow(db))
                    {
                        var handle = window.Handle;
                        var listener = ThreadPool.RegisterWaitForSingleObject(open, (s, expired) => window.Post(window.OpenHistory), null, -1, false);
                        try
                        {
                            using (var watcher = new Watcher(db, window.Report))
                            {
                                if (!background) window.OpenHistory();
                                Application.Run();
                            }
                        }
                        finally { listener.Unregister(null); }
                    }
                }
                catch (Exception ex) { MessageBox.Show(ex.Message + Environment.NewLine + path, "Apple Music 播放历史", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
}
