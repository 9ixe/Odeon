using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Odeon.Core;

namespace Odeon.Controls
{
    public sealed partial class TimeDisplay : UserControl
    {
        public static readonly DependencyProperty TimeProperty = DependencyProperty.Register(
            nameof(Time),
            typeof(double),
            typeof(TimeDisplay),
            new PropertyMetadata(0d));
        public static readonly DependencyProperty LengthProperty = DependencyProperty.Register(
            nameof(Length),
            typeof(double),
            typeof(TimeDisplay),
            new PropertyMetadata(0d));
        public static readonly DependencyProperty TitleNameProperty = DependencyProperty.Register(
            nameof(TitleName),
            typeof(string),
            typeof(TimeDisplay),
            new PropertyMetadata(string.Empty, OnNameChanged));
        public static readonly DependencyProperty ChapterNameProperty = DependencyProperty.Register(
            nameof(ChapterName),
            typeof(string),
            typeof(TimeDisplay),
            new PropertyMetadata(string.Empty, OnNameChanged));
        public static readonly DependencyProperty TextBlockStyleProperty = DependencyProperty.Register(
            nameof(TextBlockStyle),
            typeof(Style),
            typeof(TimeDisplay),
            new PropertyMetadata(null));
        public static readonly DependencyProperty ShowChapterNameProperty = DependencyProperty.Register(
            nameof(ShowChapterName),
            typeof(bool),
            typeof(TimeDisplay),
            new PropertyMetadata(true, OnNameChanged));

        public double Time
        {
            get => (double)GetValue(TimeProperty);
            set => SetValue(TimeProperty, value);
        }

        public double Length
        {
            get => (double)GetValue(LengthProperty);
            set => SetValue(LengthProperty, value);
        }

        public string TitleName
        {
            get => (string)GetValue(TitleNameProperty);
            set => SetValue(TitleNameProperty, value);
        }

        public string ChapterName
        {
            get => (string)GetValue(ChapterNameProperty);
            set => SetValue(ChapterNameProperty, value);
        }

        public Style TextBlockStyle
        {
            get => (Style)GetValue(TextBlockStyleProperty);
            set => SetValue(TextBlockStyleProperty, value);
        }

        public bool ShowChapterName
        {
            get => (bool)GetValue(ShowChapterNameProperty);
            set => SetValue(ShowChapterNameProperty, value);
        }

        private bool _showRemaining;

        public TimeDisplay()
        {
            this.InitializeComponent();
            Loaded += (s, e) =>
            {
                UpdateNameVisualState();
                UpdateTimeFlyoutChecks();
            };
        }

        private static void OnNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            TimeDisplay view = (TimeDisplay)d;
            view.UpdateNameVisualState();
        }

        private void UpdateNameVisualState()
        {
            if (!ShowChapterName)
            {
                VisualStateManager.GoToState(this, "None", false);
                return;
            }

            bool hasTitle = !string.IsNullOrWhiteSpace(TitleName);
            bool hasChapter = !string.IsNullOrWhiteSpace(ChapterName);

            if (!hasTitle && !hasChapter)
            {
                VisualStateManager.GoToState(this, "None", false);
            }
            else if (hasTitle && hasChapter)
            {
                VisualStateManager.GoToState(this, "Both", false);
            }
            else
            {
                VisualStateManager.GoToState(this, "Either", false);
            }
        }

        private string GetRemainingTime(double currentTime) => Humanizer.ToDuration(currentTime - Length);

        private void TimeDisplay_OnTapped(object sender, TappedRoutedEventArgs e)
        {
            _showRemaining = !_showRemaining;
            VisualStateManager.GoToState(this, _showRemaining ? "ShowRemaining" : "ShowElapsed", true);
            UpdateTimeFlyoutChecks();
        }

        private void MenuFlyout_OnOpening(object sender, object e)
        {
            UpdateTimeFlyoutChecks();
            UpdateChaptersFlyoutText();
        }

        private void FlyoutShowChaptersItem_Click(object sender, RoutedEventArgs e)
        {
            ShowChapterName = !ShowChapterName;
            UpdateChaptersFlyoutText();
        }

        private void UpdateChaptersFlyoutText()
        {
            if (FlyoutShowChaptersItem != null)
            {
                FlyoutShowChaptersItem.Text = ShowChapterName
                    ? Strings.Resources.HideChapters
                    : Strings.Resources.SettingsShowChaptersHeader;
            }
        }

        private void FlyoutElapsedItem_Click(object sender, RoutedEventArgs e)
        {
            _showRemaining = false;
            VisualStateManager.GoToState(this, "ShowElapsed", true);
            UpdateTimeFlyoutChecks();
        }

        private void FlyoutRemainingItem_Click(object sender, RoutedEventArgs e)
        {
            _showRemaining = true;
            VisualStateManager.GoToState(this, "ShowRemaining", true);
            UpdateTimeFlyoutChecks();
        }

        private void UpdateTimeFlyoutChecks()
        {
            if (FlyoutElapsedItem != null)
                FlyoutElapsedItem.IsChecked = !_showRemaining;
            if (FlyoutRemainingItem != null)
                FlyoutRemainingItem.IsChecked = _showRemaining;
        }
    }
}