using CommunityToolkit.Mvvm.ComponentModel;

namespace Odeon.Core.ViewModels
{
    public sealed partial class ChapterViewModel : ObservableObject
    {
        [ObservableProperty]
        private double _value;

        [ObservableProperty]
        private double _minimum;

        [ObservableProperty]
        private double _maximum;

        [ObservableProperty]
        private double _width;

        [ObservableProperty]
        private double _fillWidth;

        [ObservableProperty]
        private double _height = 5.6;

        [ObservableProperty]
        private string? _title;
    }
}
