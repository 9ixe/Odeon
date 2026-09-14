#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.WinUI;
using Odeon.Core.Services;
using Odeon.Core.ViewModels;
using Windows.Media.Core;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Odeon.Controls
{
    public sealed partial class ChapterProgressBar : UserControl
    {
        public static readonly DependencyProperty ChaptersProperty = DependencyProperty.Register(
            nameof(Chapters),
            typeof(IReadOnlyCollection<ChapterCue>),
            typeof(ChapterProgressBar),
            new PropertyMetadata(null, OnChaptersChanged));

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(ChapterProgressBar),
            new PropertyMetadata(0d, OnValueChanged));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum),
            typeof(double),
            typeof(ChapterProgressBar),
            new PropertyMetadata(0d, OnMaximumChanged));

        public static readonly DependencyProperty ChapterIndexProperty = DependencyProperty.Register(
            nameof(ChapterIndex),
            typeof(int),
            typeof(ChapterProgressBar),
            new PropertyMetadata(-1));

        public IReadOnlyCollection<ChapterCue>? Chapters
        {
            get => (IReadOnlyCollection<ChapterCue>?)GetValue(ChaptersProperty);
            set => SetValue(ChaptersProperty, value);
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public int ChapterIndex
        {
            get => (int)GetValue(ChapterIndexProperty);
            private set => SetValue(ChapterIndexProperty, value);
        }

        private ObservableCollection<ChapterViewModel> ProgressItems { get; }

        private const double Spacing = 3;

        private readonly DispatcherQueueTimer _chaptersUpdateTimer;

        public ChapterProgressBar()
        {
            _chaptersUpdateTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            ProgressItems = new ObservableCollection<ChapterViewModel>();
            this.InitializeComponent();
            SizeChanged += OnSizeChanged;
            RegisterPropertyChangedCallback(MinHeightProperty, OnMinHeightChanged);
            Loaded += (s, e) =>
            {
                PopulateProgressItems();
            };
        }

        private void OnMinHeightChanged(DependencyObject sender, DependencyProperty dp)
        {
            double h = MinHeight > 0 ? MinHeight : 5.6;
            foreach (var item in ProgressItems)
            {
                item.Height = h;
            }
        }

        private static void OnChaptersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ChapterProgressBar view = (ChapterProgressBar)d;
            if (e.OldValue is INotifyCollectionChanged oldObservable)
            {
                oldObservable.CollectionChanged -= view.ChaptersOnCollectionChanged;
            }

            if (e.NewValue is INotifyCollectionChanged newObservable)
            {
                newObservable.CollectionChanged += view.ChaptersOnCollectionChanged;
            }

            view.PopulateProgressItems();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ChapterProgressBar view = (ChapterProgressBar)d;
            view.UpdateProgress();
        }

        private static void OnMaximumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ChapterProgressBar view = (ChapterProgressBar)d;
            view.PopulateProgressItems();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ActualWidth <= 0) return;
            double h = MinHeight > 0 ? MinHeight : 5.6;
            if (ProgressItems.Count == 1)
            {
                ProgressItems[0].Width = ActualWidth;
                ProgressItems[0].Height = h;
            }
            else if (ProgressItems.Count > 1)
            {
                foreach (ChapterViewModel item in ProgressItems)
                {
                    item.Width = GetItemWidth(item.Maximum - item.Minimum, ProgressItems.Count);
                    item.Height = h;
                }
            }
            UpdateProgress();
        }

        private void ChaptersOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            _chaptersUpdateTimer.Debounce(PopulateProgressItems, TimeSpan.FromMilliseconds(50));
        }

        private void UpdateProgress()
        {
            if (ProgressItems.Count == 0) return;

            double val = Value;
            int activeIndex = -1;

            for (int i = 0; i < ProgressItems.Count; i++)
            {
                var item = ProgressItems[i];
                if (val <= item.Minimum)
                {
                    item.FillWidth = 0;
                }
                else if (val >= item.Maximum)
                {
                    item.FillWidth = item.Width;
                }
                else
                {
                    activeIndex = i;
                    double duration = item.Maximum - item.Minimum;
                    double progress = duration > 0 ? (val - item.Minimum) / duration : 0;
                    item.FillWidth = Math.Clamp(progress * item.Width, 0, item.Width);
                }
            }

            if (activeIndex != -1 && activeIndex != ChapterIndex)
            {
                ChapterIndex = activeIndex;
            }
        }

        private void PopulateProgressItems()
        {
            ProgressItems.Clear();
            double h = MinHeight > 0 ? MinHeight : 5.6;

            if (Chapters?.Count > 0 && Maximum > 0)
            {
                ChapterIndex = -1;
                var lastChapterEndTime = TimeSpan.Zero;
                foreach (ChapterCue cue in Chapters)
                {
                    var gap = cue.StartTime - lastChapterEndTime;
                    if (gap > TimeSpan.FromMilliseconds(500))
                    {
                        ChapterViewModel gapChapter = new()
                        {
                            Minimum = lastChapterEndTime.TotalMilliseconds,
                            Maximum = cue.StartTime.TotalMilliseconds,
                            Height = h,
                            Title = string.Empty
                        };

                        ProgressItems.Add(gapChapter);
                    }

                    lastChapterEndTime = cue.StartTime + cue.Duration;
                    var startTime = cue.StartTime.TotalMilliseconds;
                    var endTime = (cue.Duration + cue.StartTime).TotalMilliseconds;
                    ChapterViewModel chapter = new()
                    {
                        Minimum = startTime,
                        Maximum = endTime,
                        Height = h,
                        Title = cue.Title
                    };

                    ProgressItems.Add(chapter);
                }

                if (Maximum - lastChapterEndTime.TotalMilliseconds > 500)
                {
                    ChapterViewModel gapChapter = new()
                    {
                        Minimum = lastChapterEndTime.TotalMilliseconds,
                        Maximum = Maximum,
                        Height = h,
                        Title = string.Empty
                    };

                    ProgressItems.Add(gapChapter);
                }

                if (ActualWidth > 0)
                {
                    foreach (ChapterViewModel item in ProgressItems)
                    {
                        item.Width = GetItemWidth(item.Maximum - item.Minimum, ProgressItems.Count);
                    }
                }
            }
            else
            {
                ChapterIndex = 0;
                ProgressItems.Add(new ChapterViewModel
                {
                    Minimum = 0,
                    Maximum = Maximum > 0 ? Maximum : 1,
                    Width = ActualWidth > 0 ? ActualWidth : 0,
                    Height = h
                });
            }

            UpdateProgress();
        }

        private double GetItemWidth(double durationMs, int chapterCount)
        {
            if (chapterCount <= 1) return ActualWidth;
            double totalSpacing = Spacing * (chapterCount - 1);
            double availableWidth = Math.Max(0, ActualWidth - totalSpacing);
            return Maximum > 0 ? Math.Max(0, (durationMs / Maximum) * availableWidth) : 0;
        }
    }
}
