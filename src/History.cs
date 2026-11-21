using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace AppleMusicHistory
{
    internal sealed class Play
    {
        public long Id;
        public DateTimeOffset HeardAt;
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public DateTime LocalTime { get { return HeardAt.LocalDateTime; } }
        public long OriginTicks;
    }

    internal sealed class History : IDisposable
    {
        private const int Row = 100, Done = 101;
        private const string Columns = "id, heard_at, title, artist, album, origin_ticks";
        private IntPtr db;
        private readonly object sync = new object();

        public History(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                Check(sqlite3_open16(path, out db));
                Check(sqlite3_busy_timeout(db, 2500));
                Run("CREATE TABLE IF NOT EXISTS plays (id INTEGER PRIMARY KEY, heard_at TEXT NOT NULL, title TEXT NOT NULL, artist TEXT NOT NULL, album TEXT NOT NULL, origin_ticks INTEGER NOT NULL)", null);
            }
            catch { Dispose(); throw; }
        }

        public List<Play> Read(bool latestOnly = false)
        {
            var plays = new List<Play>();
            Run("SELECT " + Columns + " FROM plays ORDER BY id DESC" + (latestOnly ? " LIMIT 1" : ""), s =>
                plays.Add(new Play {
                    Id = sqlite3_column_int64(s, 0),
                    HeardAt = DateTimeOffset.Parse(Text(s, 1), CultureInfo.InvariantCulture),
                    Title = Text(s, 2), Artist = Text(s, 3), Album = Text(s, 4),
                    OriginTicks = sqlite3_column_int64(s, 5)
                }));
            return plays;
        }

        public long Add(Play p)
        {
            return Run("INSERT INTO plays (heard_at, title, artist, album, origin_ticks) VALUES (?, ?, ?, ?, ?)", null,
                p.HeardAt.ToString("o", CultureInfo.InvariantCulture), p.Title, p.Artist, p.Album, Number(p.OriginTicks));
        }

        public void Update(long id, string artist, string album, long origin)
        {
            Run("UPDATE plays SET artist = ?, album = ?, origin_ticks = ? WHERE id = ?", null, artist, album, Number(origin), Number(id));
        }

        private static string Number(long n) { return n.ToString(CultureInfo.InvariantCulture); }

        private static string Text(IntPtr stmt, int column)
        {
            var p = sqlite3_column_text16(stmt, column);
            return p == IntPtr.Zero ? "" : Marshal.PtrToStringUni(p, sqlite3_column_bytes16(stmt, column) / 2);
        }

        private long Run(string sql, Action<IntPtr> read, params string[] values)
        {
            lock (sync)
            {
                IntPtr stmt;
                Check(sqlite3_prepare16_v2(db, sql, -1, out stmt, IntPtr.Zero));
                try
                {
                    for (int i = 0; i < values.Length; i++)
                        Check(sqlite3_bind_text16(stmt, i + 1, values[i], values[i].Length * 2, new IntPtr(-1)));
                    int rc;
                    while ((rc = sqlite3_step(stmt)) == Row)
                        if (read != null) read(stmt);
                    if (rc != Done) Check(rc);
                    return sqlite3_last_insert_rowid(db);
                }
                finally { sqlite3_finalize(stmt); }
            }
        }

        private void Check(int rc)
        {
            if (rc != 0)
                throw new IOException("SQLite " + rc + " · " + (db == IntPtr.Zero ? "" : Marshal.PtrToStringUni(sqlite3_errmsg16(db))));
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (db != IntPtr.Zero) sqlite3_close(db);
                db = IntPtr.Zero;
            }
        }

        private const string Sqlite = "winsqlite3.dll";
        [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int sqlite3_open16(string path, out IntPtr db);
        [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int bytes, out IntPtr stmt, IntPtr tail);
        [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int sqlite3_bind_text16(IntPtr stmt, int index, string value, int bytes, IntPtr destructor);
        [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_last_insert_rowid(IntPtr db);
        [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr stmt, int col);
        [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text16(IntPtr stmt, int col);
        [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes16(IntPtr stmt, int col);
        [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_errmsg16(IntPtr db);
        [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    }
}
