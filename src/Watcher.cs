using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Media.Control;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using Manager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using Status = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace AppleMusicHistory
{
    internal sealed class Watcher : IDisposable
    {
        private readonly Playback playback;
        private readonly Action<string, bool> report;
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly Thread worker;
        private Manager manager;
        private Session session;
        private string lastStatus;

        public Watcher(History history, Action<string, bool> report)
        {
            this.report = report;
            playback = new Playback(history);
            worker = new Thread(Run) { IsBackground = true, Name = "Apple Music playback" };
            worker.Start();
        }

        internal static bool IsApple(string id)
        {
            return string.Equals(id, "AppleMusic.exe", StringComparison.OrdinalIgnoreCase)
                || (id ?? "").EndsWith(@"\AppleMusic.exe", StringComparison.OrdinalIgnoreCase)
                || (id ?? "").StartsWith("AppleInc.AppleMusicWin_", StringComparison.OrdinalIgnoreCase)
                || (id ?? "").StartsWith("AppleInc.AppleMusic_", StringComparison.OrdinalIgnoreCase);
        }

        private void Signal(object sender, object args) { wake.Set(); }

        private void Bind(Session next)
        {
            if (ReferenceEquals(next, session)) return;
            var old = session;
            session = null;
            if (old != null)
            {
                try
                {
                    old.MediaPropertiesChanged -= Signal;
                    old.PlaybackInfoChanged -= Signal;
                    old.TimelinePropertiesChanged -= Signal;
                }
                catch (COMException) { /* The player may have exited. */ }
            }
            session = next;
            if (next == null) return;
            next.MediaPropertiesChanged += Signal;
            next.PlaybackInfoChanged += Signal;
            next.TimelinePropertiesChanged += Signal;
        }

        private void Disconnect()
        {
            Bind(null);
            var old = manager;
            manager = null;
            if (old != null)
                try { old.SessionsChanged -= Signal; }
                catch (COMException) { }
        }

        private void Run()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    try { Sample(); }
                    catch (OperationCanceledException) { if (stop.IsCancellationRequested) break; }
                    catch (System.IO.IOException ex) { Notify("历史写入失败，正在重试 · " + ex.Message, false); }
                    catch (Exception ex)
                    {
                        Notify("重新连接 Apple Music · " + ex.Message, false);
                        Disconnect();
                    }
                    if (!stop.IsCancellationRequested) wake.WaitOne(1500);
                }
            }
            finally { Disconnect(); }
        }

        private static Timeline GetTimeline(Session s)
        {
            try
            {
                var t = s.GetTimelineProperties();
                if (t != null)
                    return new Timeline { Position = t.Position.TotalSeconds, Duration = t.EndTime.TotalSeconds, Updated = t.LastUpdatedTime };
            }
            catch (COMException) { /* Some streams expose only track metadata. */ }
            return new Timeline();
        }

        private void Sample()
        {
            if (manager == null)
            {
                manager = Manager.RequestAsync().AsTask(stop.Token).GetAwaiter().GetResult();
                manager.SessionsChanged += Signal;
            }
            var apple = manager.GetSessions().Where(s => IsApple(s.SourceAppUserModelId)).ToArray();
            Bind(apple.Length < 2 ? apple.FirstOrDefault() : apple.FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == Status.Playing) ?? apple[0]);
            if (session == null)
            {
                playback.Ended();
                Notify("等待 Apple Music 播放", false);
                return;
            }
            var status = session.GetPlaybackInfo().PlaybackStatus;
            if (status == Status.Playing)
            {
                var media = session.TryGetMediaPropertiesAsync().AsTask(stop.Token).GetAwaiter().GetResult();
                if (media == null) { Notify("等候歌曲信息", false); return; }
                var track = Track.FromApple(media.Title, media.Artist, media.AlbumTitle, media.AlbumArtist);
                var timeline = GetTimeline(session);
                status = session.GetPlaybackInfo().PlaybackStatus;
                if (status == Status.Playing)
                {
                    bool changed = playback.Playing(track, timeline, DateTimeOffset.Now);
                    Notify(track.Title == "" ? "等候歌曲信息" : "正在记录 · " + track.Title + (track.Artist == "" ? "" : "  /  " + track.Artist), changed);
                    return;
                }
            }
            if (status == Status.Stopped || status == Status.Closed) playback.Ended();
            else playback.Paused(GetTimeline(session));
            Notify(status == Status.Paused ? "Apple Music 已暂停" : "等待 Apple Music 播放", false);
        }

        private void Notify(string status, bool changed)
        {
            if (!changed && status == lastStatus) return;
            lastStatus = status;
            report(status, changed);
        }

        public void Dispose()
        {
            stop.Cancel();
            wake.Set();
            worker.Join();
            stop.Dispose();
            wake.Dispose();
        }
    }
}
