using System;
using System.Linq;
using CommunityToolkit.Mvvm.DependencyInjection;
using Odeon.Core.ViewModels;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Odeon.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        internal SettingsPageViewModel ViewModel => (SettingsPageViewModel)DataContext;

        internal CommonViewModel Common { get; }

        public SettingsPage()
        {
            this.InitializeComponent();
            DataContext = Ioc.Default.GetRequiredService<SettingsPageViewModel>();
            Common = Ioc.Default.GetRequiredService<CommonViewModel>();

            // Set the "System default" language option string
            var systemLanguageOption = ViewModel.AvailableLanguages[0];
            systemLanguageOption.NativeName = Strings.Resources.LanguageSystemDefault;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadLibraryLocations();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.OnNavigatedFrom();
        }

        private async void ShowKeyboardShortcutsButton_OnClick(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            var dialog = new Dialogs.KeyboardShortcutsDialog();
            await dialog.ShowAsync();
        }
    }
}
