using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppleMusicHistory
{
    internal sealed class HistoryWindow : Form
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "AppleMusicHistory";
        private readonly History history;
        private readonly Label status = new Label(), count = new Label();
        private readonly TextBox search = new TextBox();
        private readonly DataGridView list = new DataGridView();
        private readonly CheckBox atLogin = new CheckBox();
        private readonly ToolStripMenuItem loginMenu = new ToolStripMenuItem("登录时自动记录");
        private readonly ContextMenuStrip trayMenu = new ContextMenuStrip(), rowMenu = new ContextMenuStrip();
        private readonly NotifyIcon tray;
        private List<Play> all = new List<Play>();

        public HistoryWindow(History history)
        {
            this.history = history;
            Text = "Apple Music 播放历史";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(890, 480);
            MinimumSize = new Size(620, 330);
            BackColor = Color.White;
            Padding = new Padding(16, 10, 16, 8);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Controls.Add(layout);

            status.Text = "正在连接 Apple Music…";
            status.Font = new Font(Font.FontFamily, 12, FontStyle.Bold);
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.AutoEllipsis = true;
            status.UseMnemonic = false;
            status.Dock = DockStyle.Fill;
            layout.Controls.Add(status, 0, 0);

            var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tools.Controls.Add(new Label { Text = "搜索", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            search.Name = "historySearch";
            search.AccessibleName = "搜索歌名、艺人或专辑";
            search.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            search.TextChanged += delegate { Filter(); };
            tools.Controls.Add(search, 1, 0);
            atLogin.Text = "登录时自动记录";
            atLogin.AutoSize = true;
            atLogin.Anchor = AnchorStyles.Right;
            atLogin.Margin = new Padding(16, 0, 2, 0);
            atLogin.Click += delegate { SetLogin(atLogin.Checked); };
            tools.Controls.Add(atLogin, 2, 0);
            layout.Controls.Add(tools, 0, 1);

            list.Name = "historyGrid";
            list.AccessibleName = "播放历史";
            list.Dock = DockStyle.Fill;
            list.ReadOnly = true;
            list.AutoGenerateColumns = false;
            list.AllowUserToAddRows = false;
            list.AllowUserToDeleteRows = false;
            list.AllowUserToResizeRows = false;
            list.RowHeadersVisible = false;
            list.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            list.MultiSelect = false;
            list.BackgroundColor = SystemColors.Window;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.CellBorderStyle = DataGridViewCellBorderStyle.None;
            list.ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable;
            var time = new DataGridViewTextBoxColumn {
                HeaderText = "播放时间", DataPropertyName = "LocalTime",
                Width = TextRenderer.MeasureText("0000-00-00 00:00:00    ", Font).Width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            time.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm:ss";
            list.Columns.Add(time);
            AddTextColumn("歌名", "Title", 38);
            AddTextColumn("艺人", "Artist", 27);
            AddTextColumn("专辑", "Album", 35);
            list.CellMouseDown += (s, e) => {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    list.CurrentCell = list[e.ColumnIndex, e.RowIndex];
                    list.Rows[e.RowIndex].Selected = true;
                }
            };
            list.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C) { CopySong(); e.SuppressKeyPress = true; } };
            rowMenu.Items.Add("复制歌名和艺人", null, (s, e) => CopySong());
            rowMenu.Opening += (s, e) => { e.Cancel = list.SelectedRows.Count == 0; };
            list.ContextMenuStrip = rowMenu;
            layout.Controls.Add(list, 0, 2);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            count.Anchor = AnchorStyles.Left;
            count.AutoSize = true;
            footer.Controls.Add(count, 0, 0);
            footer.Controls.Add(new Label { Text = "关闭窗口后继续记录，可在托盘菜单退出", AutoSize = true, Anchor = AnchorStyles.Right, ForeColor = SystemColors.GrayText }, 1, 0);
            layout.Controls.Add(footer, 0, 3);

            trayMenu.Items.Add("查看播放历史", null, (s, e) => OpenHistory());
            loginMenu.Click += delegate { SetLogin(!loginMenu.Checked); };
            trayMenu.Items.Add(loginMenu);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出记录器", null, (s, e) => Application.ExitThread());
            tray = new NotifyIcon { Icon = Icon, Text = Text, ContextMenuStrip = trayMenu, Visible = true };
            tray.DoubleClick += delegate { OpenHistory(); };
            SyncLogin();
            FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        }

        public void OpenHistory()
        {
            Reload();
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            search.Focus();
        }

        public void Post(Action action)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(action); }
            catch (InvalidOperationException) { /* The message loop is closing. */ }
        }

        public void Report(string message, bool changed)
        {
            Post(() => {
                status.Text = message;
                string tip = Text + "\n" + message;
                tray.Text = tip.Length > 62 ? tip.Substring(0, 62) + "…" : tip;
                if (changed && Visible) Reload();
            });
        }

        private void Reload()
        {
            try { all = history.Read(); Filter(); }
            catch (System.IO.IOException ex) { status.Text = "读取历史失败 · " + ex.Message; }
        }

        private void Filter()
        {
            string query = search.Text.Trim();
            var shown = query == "" ? all : all.Where(p => Contains(p.Title, query) || Contains(p.Artist, query) || Contains(p.Album, query)).ToList();
            list.DataSource = shown;
            list.ClearSelection();
            count.Text = query == "" ? "共 " + all.Count + " 次播放" : shown.Count + " 条匹配 / 共 " + all.Count + " 次播放";
        }

        private static bool Contains(string text, string query) { return text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0; }

        private void AddTextColumn(string header, string property, int weight)
        {
            list.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = header, DataPropertyName = property,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private void CopySong()
        {
            if (list.SelectedRows.Count == 0) return;
            var p = (Play)list.SelectedRows[0].DataBoundItem;
            try { Clipboard.SetText(p.Title + (p.Artist == "" ? "" : " — " + p.Artist)); }
            catch (System.Runtime.InteropServices.ExternalException ex) { MessageBox.Show(this, ex.Message, "复制失败"); }
        }

        private void SyncLogin()
        {
            string value = (string)Registry.GetValue(@"HKEY_CURRENT_USER\" + RunKey, RunName, null);
            atLogin.Checked = loginMenu.Checked = string.Equals(value, StartupCommand, StringComparison.OrdinalIgnoreCase);
        }

        private static string StartupCommand { get { return "\"" + Application.ExecutablePath + "\" --tray"; } }

        private void SetLogin(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                    if (enabled) key.SetValue(RunName, StartupCommand);
                    else key.DeleteValue(RunName, false);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "设置登录启动失败"); }
            SyncLogin();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tray.Visible = false;
                tray.Dispose();
                trayMenu.Dispose();
                rowMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
