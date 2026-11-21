using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AppleMusicHistory
{
    internal static class HistoryTests
    {
        private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-03-15T08:00:00+08:00");
        private static Timeline Frame(double seconds, double position)
        {
            return new Timeline { Updated = Start.AddSeconds(seconds), Position = position, Duration = 180 };
        }
        private static void Require(bool value, string message) { if (!value) throw new Exception(message); }

        private static void CheckWindow(History db)
        {
            using (var window = new HistoryWindow(db))
            {
                window.Opacity = 0;
                window.ShowInTaskbar = false;
                var handle = window.Handle; // The recorder creates the window handle while it is still in the tray.
                window.OpenHistory();
                Application.DoEvents();
                var grid = (DataGridView)window.Controls.Find("historyGrid", true).Single();
                var search = (TextBox)window.Controls.Find("historySearch", true).Single();
                var rows = db.Read();
                Require(grid.RowCount == rows.Count && (string)grid[1, 0].Value == rows[0].Title, "first open shows newest first");
                using (var pixels = new Bitmap(grid.Width, grid.Height))
                {
                    grid.DrawToBitmap(pixels, grid.ClientRectangle);
                    int expectedTop = grid.GetCellDisplayRectangle(1, -1, true).Bottom;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var cell = grid.GetCellDisplayRectangle(1, i, true);
                        Require(cell.Top == expectedTop && cell.Height > 0, "rows sit directly below header");
                        expectedTop = cell.Bottom;
                        cell.Inflate(-3, -3);
                        bool ink = false;
                        for (int y = cell.Top; y < cell.Bottom && !ink; y++)
                            for (int x = cell.Left; x < cell.Right && !ink; x++)
                                ink = pixels.GetPixel(x, y).GetBrightness() < 0.5f;
                        Require(ink, "song title painted before selection, row " + i);
                    }
                }
                search.Text = rows[0].Title;
                Require(grid.RowCount == rows.Count(p => p.Title == rows[0].Title), "search returns matching rows");
                search.Text = "does-not-match-any-song";
                Require(grid.RowCount == 0, "empty search clears rows");
                search.Clear();
                window.Close();
                window.OpenHistory();
                Require(grid.RowCount == rows.Count && grid.FirstDisplayedScrollingRowIndex == 0, "reopen from tray");
            }
        }

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string path = Path.Combine(Path.GetTempPath(), "apple-music-test-" + Guid.NewGuid().ToString("N") + ".sqlite3");
            var a = Track.FromApple("虹 'A' 🎵", "歌手 — 第一辑", "", "");
            var b = Track.FromApple("第二首", "艺人 — 合作", "现场", "");
            Require(a.Artist == "歌手" && a.Album == "第一辑" && b.Artist == "艺人 — 合作", "Apple metadata");
            Require(Watcher.IsApple("AppleMusic.exe") && Watcher.IsApple("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App") && !Watcher.IsApple("OtherMusic.exe"), "Apple session filter");
            try
            {
                using (var db = new History(path))
                {
                    var tracker = new Playback(db);
                    Action<Track, double, double> play = (t, at, pos) => tracker.Playing(t, Frame(at, pos), Start.AddSeconds(at));
                    tracker.Paused(Frame(0, 0));
                    play(Track.FromApple("", "", "", ""), 0, 0);
                    Require(db.Read().Count == 0, "paused or empty metadata");
                    play(Track.FromApple(a.Title, "", "", ""), 0, 5);
                    play(a, 1, 6);
                    tracker.Paused(Frame(2, 7));
                    play(a, 100, 7);
                    play(a, 110, 70);
                    play(a, 111, 2);
                    play(a, 280, 171);
                    Require(db.Read().Count == 1 && db.Read()[0].Artist == "歌手", "enrichment, resume and seek");

                    play(a, 289, 0); // A stale title can arrive with B's new timeline.
                    play(b, 289.2, 0.2);
                    play(a, 290, 0);
                    play(a, 470, 0); // Some sessions only publish a timeline update on wrap.
                    play(a, 471.2, 1.2);
                    Require(db.Read().Count == 4 && db.Read()[0].HeardAt == Start.AddSeconds(470), "switch, return and repeat");

                    tracker = new Playback(db);
                    play(a, 472, 2);
                    Require(db.Read().Count == 4, "recorder restart in the middle of a play");
                    tracker.Ended();
                    play(a, 500, 0);
                    tracker.Playing(b, new Timeline(), Start.AddSeconds(501));
                    tracker.Playing(b, new Timeline(), Start.AddSeconds(503));
                }
                using (var reopened = new History(path))
                {
                    var rows = reopened.Read();
                    Require(rows.Count == 6 && rows[0].Title == b.Title && rows.Last().Title == a.Title, "durable rows and order");
                    Require(rows[0].Id != rows[1].Id && reopened.Read(true).Single().Id == rows[0].Id, "latest play");
                    CheckWindow(reopened);
                }
                Console.WriteLine("PASS: playback tracking, SQLite persistence, initial row painting, search, tray reopen.");
            }
            finally { File.Delete(path); }
        }
    }
}
