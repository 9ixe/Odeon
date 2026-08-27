using CommunityToolkit.Mvvm.DependencyInjection;
using Odeon.Core.Models;
using Odeon.Core.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Odeon.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SearchResultPage : Page
    {
        internal SearchResultPageViewModel ViewModel => (SearchResultPageViewModel)DataContext;

        internal CommonViewModel Common { get; }

        public SearchResultPage()
        {
            this.InitializeComponent();
            DataContext = Ioc.Default.GetRequiredService<SearchResultPageViewModel>();
            Common = Ioc.Default.GetRequiredService<CommonViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is SearchResult result)
            {
                ViewModel.Load(result);
            }
        }

        private void GridView_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            double itemWidth = (double)App.Current.Resources["CommonGridViewItemWidth"];
            int desiredCount = (int)(e.NewSize.Width / (itemWidth + 10));
            ViewModel.UpdateGridItems(desiredCount);
        }
    }
}
