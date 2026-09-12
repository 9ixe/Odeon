#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Odeon.Core.Interop;
using Windows.Media.Core;

namespace Odeon.Core.Playback
{
    public sealed class PlaybackChapterList : ReadOnlyCollection<ChapterCue>, IEnumerable<ChapterCue>, IEnumerable
    {
        private readonly object _syncLock = new();
        private readonly List<ChapterCue> _chapters;
        private readonly PlaybackItem _item;

        internal PlaybackChapterList(PlaybackItem item) : base(new List<ChapterCue>())
        {
            _item = item;
            _chapters = (List<ChapterCue>)Items;
        }

        public new IEnumerator<ChapterCue> GetEnumerator()
        {
            ChapterCue[] snapshot;
            lock (_syncLock)
            {
                snapshot = _chapters.ToArray();
            }
            return ((IEnumerable<ChapterCue>)snapshot).GetEnumerator();
        }

        IEnumerator<ChapterCue> IEnumerable<ChapterCue>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public ChapterCue[] ToArray()
        {
            lock (_syncLock)
            {
                return _chapters.ToArray();
            }
        }

        public void Load(IMediaPlayer player)
        {
            if (player is MpvMediaPlayer mpvPlayer)
            {
                IReadOnlyList<MpvChapterInfo> chapters = mpvPlayer.GetChapters();
                Load(chapters, mpvPlayer.NaturalDuration);
            }
        }

        public void Load(IReadOnlyList<MpvChapterInfo> rawChapters, TimeSpan naturalDuration = default)
        {
            if (rawChapters == null || rawChapters.Count == 0)
            {
                lock (_syncLock)
                {
                    _chapters.Clear();
                }
                return;
            }

            var newChapters = new List<ChapterCue>(rawChapters.Count);
            for (int i = 0; i < rawChapters.Count; i++)
            {
                MpvChapterInfo raw = rawChapters[i];
                TimeSpan start = TimeSpan.FromSeconds(Math.Max(0, raw.Time));
                TimeSpan duration = TimeSpan.Zero;

                if (i < rawChapters.Count - 1)
                {
                    TimeSpan nextStart = TimeSpan.FromSeconds(Math.Max(0, rawChapters[i + 1].Time));
                    if (nextStart > start)
                        duration = nextStart - start;
                }
                else if (naturalDuration > start)
                {
                    duration = naturalDuration - start;
                }

                string title = !string.IsNullOrWhiteSpace(raw.Title) ? raw.Title : $"Chapter {i + 1}";

                newChapters.Add(new ChapterCue
                {
                    Title = title,
                    StartTime = start,
                    Duration = duration
                });
            }

            lock (_syncLock)
            {
                _chapters.Clear();
                _chapters.AddRange(newChapters);
            }
        }
    }
}
