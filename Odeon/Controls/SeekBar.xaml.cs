#nullable enable

using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI;
using Odeon.Core;
using Odeon.Core.ViewModels;
using System;
using System.ComponentModel;
using System.Numerics;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Odeon.Controls
{
    public sealed partial class SeekBar : UserControl
    {
        public static readonly DependencyProperty ProgressOnlyProperty = DependencyProperty.Register(
            nameof(ProgressOnly),
            typeof(bool),
            typeof(SeekBar),
            new PropertyMetadata(false));

        public bool ProgressOnly
        {
            get => (bool)GetValue(ProgressOnlyProperty);
            set => SetValue(ProgressOnlyProperty, value);
        }

        internal SeekBarViewModel ViewModel => (SeekBarViewModel)DataContext;
        private readonly DispatcherQueueTimer _previewToolTipTimer;
        private readonly DispatcherQueueTimer _scrollExpandTimer;
        private Thumb? _seekBarThumb;

        private readonly ToolTip _previewToolTip;

        public SeekBar()
        {
            this.InitializeComponent();
            DataContext = Ioc.Default.GetRequiredService<SeekBarViewModel>();
            RegisterSeekBarPointerHandlers();
            DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _previewToolTipTimer = dispatcherQueue.CreateTimer();
            _previewToolTip = new ToolTip { Padding = new Thickness(8, 3, 8, 5), FontSize = 15, VerticalOffset = 20 };

            _scrollExpandTimer = dispatcherQueue.CreateTimer();
            _scrollExpandTimer.Interval = TimeSpan.FromMilliseconds(600);
            _scrollExpandTimer.IsRepeating = false;
            _scrollExpandTimer.Tick += (s, e) =>
            {
                if (!_isSeekingPointerPressed)
                {
                    VisualStateManager.GoToState(this, "Normal", true);
                }
            };

            ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        }

        private void ViewModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.ShouldShowPreview) && !ViewModel.ShouldShowPreview)
            {
                ResetPreviewToolTip();
            }
        }

        private bool _isSeekingPointerPressed;

        private void RegisterSeekBarPointerHandlers()
        {
            SeekBarSlider.AddHandler(PointerPressedEvent, (PointerEventHandler)PointerPressedEventHandler, true);
            SeekBarSlider.AddHandler(PointerReleasedEvent, (PointerEventHandler)PointerReleasedEventHandler, true);
            SeekBarSlider.AddHandler(PointerCanceledEvent, (PointerEventHandler)PointerReleasedEventHandler, true);
            SeekBarSlider.AddHandler(PointerMovedEvent, (PointerEventHandler)PointerMovedEventHandler, true);
        }

        private void PointerReleasedEventHandler(object sender, PointerRoutedEventArgs e)
        {
            _isSeekingPointerPressed = false;
            ViewModel.OnSeekBarPointerEvent(false);
            if (!_scrollExpandTimer.IsRunning)
            {
                VisualStateManager.GoToState(this, "Normal", true);
            }
        }

        private void PointerPressedEventHandler(object sender, PointerRoutedEventArgs e)
        {
            _isSeekingPointerPressed = true;
            ViewModel.OnSeekBarPointerEvent(true);
            VisualStateManager.GoToState(this, "Seeking", true);
        }

        private void PointerMovedEventHandler(object s, PointerRoutedEventArgs e)
        {
            PointerPoint pointerPoint = e.GetCurrentPoint(SeekBarSlider);
            UpdatePreviewTime(pointerPoint);
            double halfWidth = SeekBarSlider.ActualWidth / 2;
            _previewToolTip.Translation = new Vector3((float)(-halfWidth + pointerPoint.Position.X), 0, 0);
        }

        private void SeekBarSlider_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (ToolTipService.GetToolTip(SeekBarSlider) is not ToolTip)
            {
                ToolTipService.SetToolTip(SeekBarSlider, _previewToolTip);
            }

            ViewModel.ShouldShowPreview = true;
            _previewToolTipTimer.Debounce(() => _previewToolTip.IsOpen = true, TimeSpan.FromMilliseconds(100));
        }

        private void SeekBarSlider_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isSeekingPointerPressed && !_scrollExpandTimer.IsRunning)
            {
                VisualStateManager.GoToState(this, "Normal", true);
            }

            ViewModel.ShouldShowPreview = false;
            _previewToolTipTimer.Stop();
            _previewToolTip.IsOpen = false;
            ToolTipService.SetToolTip(SeekBarSlider, null);
        }

        private void SeekBarSlider_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _previewToolTip.PlacementRect = new Rect(new Point(0, 0), e.NewSize);
        }

        private void SeekBarSlider_OnLoaded(object sender, RoutedEventArgs e)
        {
            _seekBarThumb = SeekBarSlider.FindDescendant<Thumb>();
            if (_seekBarThumb != null)
            {
                // When Thumb is pressed, it hijacks pointer events until it's released
                _seekBarThumb.PointerReleased += SeekBarThumb_PointerReleased;
            }
        }

        private void SeekBarThumb_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isSeekingPointerPressed = false;
            if (!_scrollExpandTimer.IsRunning)
            {
                VisualStateManager.GoToState(this, "Normal", true);
            }
            var position = e.GetCurrentPoint(SeekBarSlider).Position;
            bool inBound = position.X >= 0 && position.X <= SeekBarSlider.ActualWidth &&
                           position.Y >= 0 && position.Y <= SeekBarSlider.ActualHeight;
            if (!inBound)
            {
                SeekBarSlider_OnPointerExited(sender, e);
            }
        }

        private void SeekBarSlider_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (e.IsGenerated) return;
            VisualStateManager.GoToState(this, "Seeking", true);
            _scrollExpandTimer.Stop();
            _scrollExpandTimer.Start();

            PointerPoint pointer = e.GetCurrentPoint((UIElement)sender);
            int mouseWheelDelta = pointer.Properties.MouseWheelDelta;
            ViewModel.OnSeekBarPointerWheelChanged(mouseWheelDelta);
        }

        private void ResetPreviewToolTip()
        {
            _previewToolTipTimer.Stop();
            _previewToolTip.IsOpen = false;
        }

        private void UpdatePreviewTime(PointerPoint pointerPoint)
        {
            double pointerOffset = pointerPoint.Position.X;
            double pointerOffsetRelative = pointerOffset / SeekBarSlider.ActualWidth; // have not accounted for padding
            double thumbOffset = 0;
            if (_seekBarThumb != null)
            {
                double thumbWidth = _seekBarThumb.ActualWidth;
                thumbOffset = thumbWidth * (pointerOffsetRelative - 0.5);
            }

            double normalizedPosition = (pointerOffset + thumbOffset) / SeekBarSlider.ActualWidth;
            ViewModel.UpdatePreviewTime(normalizedPosition);
            
            string timeStr = Humanizer.ToDuration(ViewModel.PreviewTime);
            var chapter = ViewModel.GetChapterAt(TimeSpan.FromMilliseconds(ViewModel.PreviewTime));
            string newContent = chapter != null && !string.IsNullOrWhiteSpace(chapter.Title)
                ? $"{timeStr} • {chapter.Title}"
                : timeStr;
            if (_previewToolTip.Content as string != newContent)
            {
                _previewToolTip.Content = newContent;
            }
        }
    }
}
