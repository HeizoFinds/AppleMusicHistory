using System;
using System.Linq;

namespace AppleMusicHistory
{
    internal sealed class Track
    {
        public string Title, Artist, Album;

        public static Track FromApple(string title, string artist, string album, string albumArtist)
        {
            var t = new Track { Title = (title ?? "").Trim(), Artist = (artist ?? "").Trim(), Album = (album ?? "").Trim() };
            const string separator = " — ";
            int split = t.Artist.IndexOf(separator, StringComparison.Ordinal);
            if (t.Album == "" && split > 0)
            {
                t.Album = t.Artist.Substring(split + separator.Length).Trim();
                t.Artist = t.Artist.Substring(0, split).Trim();
            }
            else if (t.Album != "" && t.Artist.EndsWith(separator + t.Album, StringComparison.Ordinal))
                t.Artist = t.Artist.Substring(0, t.Artist.Length - separator.Length - t.Album.Length).Trim();
            if (t.Artist == "") t.Artist = (albumArtist ?? "").Trim();
            return t;
        }

        public bool Matches(Play p)
        {
            return p != null && p.Title == Title && Compatible(p.Artist, Artist) && Compatible(p.Album, Album);
        }

        private static bool Compatible(string a, string b) { return a == b || a == "" || b == ""; }
    }

    internal struct Timeline
    {
        public double Position, Duration;
        public DateTimeOffset Updated;

        public long Origin
        {
            get { return Updated.Year > 2000 && Position >= 0 ? Updated.UtcTicks - TimeSpan.FromSeconds(Position).Ticks : 0; }
        }

        public bool WrappedTo(Timeline next, bool wasPlaying)
        {
            if (Duration <= 0 || Math.Abs(Duration - next.Duration) > 1 || next.Updated <= Updated || Origin <= 0 || next.Origin <= 0)
                return false;
            double edge = Math.Min(5, Duration / 4);
            double before = Position + (wasPlaying ? (next.Updated - Updated).TotalSeconds : 0);
            return next.Position <= edge && before >= Duration - edge && before <= Duration + edge
                && next.Origin - Origin > TimeSpan.FromSeconds(Duration / 2).Ticks;
        }
    }

    internal sealed class Playback
    {
        private static readonly long OriginTolerance = TimeSpan.FromSeconds(3).Ticks;
        private readonly History history;
        private Play active, resume;
        private Timeline previous;
        private bool wasPlaying;
        private DateTimeOffset? repeatAt;

        public Playback(History history)
        {
            this.history = history;
            resume = history.Read(true).FirstOrDefault();
        }

        public void Paused(Timeline timeline)
        {
            previous = timeline;
            wasPlaying = false;
            repeatAt = null;
        }

        public void Ended()
        {
            active = resume = null;
            Paused(new Timeline());
        }

        public bool Playing(Track t, Timeline timeline, DateTimeOffset now)
        {
            if (t.Title == "") return false;
            long origin = timeline.Origin;
            if (t.Matches(resume) && origin > 0 && resume.OriginTicks > 0 && Math.Abs(origin - resume.OriginTicks) <= OriginTolerance)
                active = resume;
            resume = null;

            bool same = t.Matches(active);
            if (!same) repeatAt = null;
            // Apple can reset the timeline just before publishing a different title.
            if (same && !repeatAt.HasValue && previous.WrappedTo(timeline, wasPlaying)) repeatAt = now;
            bool repeat = repeatAt.HasValue && (now - repeatAt.Value).TotalSeconds >= 1;
            bool changed = !same || repeat;
            if (changed)
            {
                var entry = new Play { HeardAt = repeatAt ?? now, Title = t.Title, Artist = t.Artist, Album = t.Album, OriginTicks = origin };
                entry.Id = history.Add(entry);
                active = entry;
                repeatAt = null;
            }
            else
            {
                string artist = t.Artist == "" ? active.Artist : t.Artist;
                string album = t.Album == "" ? active.Album : t.Album;
                changed = artist != active.Artist || album != active.Album;
                bool moved = origin > 0 && Math.Abs(origin - active.OriginTicks) > OriginTolerance;
                if (changed || (moved && !repeatAt.HasValue))
                {
                    long savedOrigin = moved && !repeatAt.HasValue ? origin : active.OriginTicks;
                    history.Update(active.Id, artist, album, savedOrigin);
                    active.Artist = artist;
                    active.Album = album;
                    active.OriginTicks = savedOrigin;
                }
            }
            previous = timeline;
            wasPlaying = true;
            return changed;
        }
    }
}
