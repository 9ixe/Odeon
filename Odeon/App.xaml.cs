#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Odeon.Core;
using Odeon.Core.Helpers;
using Odeon.Core.Messages;
using Odeon.Core.Services;
using Odeon.Core.ViewModels;
using Odeon.Helpers;
using Odeon.Pages;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace Odeon;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
sealed partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();

        // Surface fatal exceptions (page load / XAML parse failures in particular) in the debug
        // output, with the full inner-exception chain, so they can be diagnosed.
        UnhandledException += OnUnhandledException;

        if (DeviceInfoHelper.IsXbox)
        {
            // Disable pointer mode on Xbox
            // https://learn.microsoft.com/en-us/windows/uwp/xbox-apps/how-to-disable-mouse-mode#xaml
            RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;

            // Use Reveal focus for 10-foot experience
            // https://learn.microsoft.com/en-us/windows/apps/design/input/gamepad-and-remote-interactions#reveal-focus
            FocusVisualKind = FocusVisualKind.Reveal;
        }


        // Disable automatic High Contrast adjustments
        // https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes#setting-highcontrastadjustment-to-none
        HighContrastAdjustment = ApplicationHighContrastAdjustment.None;

        Suspending += OnSuspending;

        // Register bundled fonts process-private before any player is created.
        // This is a one-time main-thread operation (~10-30ms on SSD) and must happen before mpv's libass
        // can resolve the subtitle font by family name. Running it here avoids blocking the UI thread on every Play().
        _ = PrivateFontRegistration.EnsureRegisteredAsync();

        IServiceProvider services = ConfigureServices();
        CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.ConfigureServices(services);

        // Eagerly create VolumeViewModel so it registers its ChangeVolumeRequestMessage handler.
        CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<VolumeViewModel>();
    }

    private static void OnUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogService.Log($"Unhandled exception: {e.Message}{Environment.NewLine}{Describe(e.Exception)}");
    }

    /// <summary>
    /// Flattens an exception and its inner exceptions into a single readable block.
    /// </summary>
    private static string Describe(Exception? exception)
    {
        var builder = new System.Text.StringBuilder();
        for (int depth = 0; exception != null; depth++, exception = exception.InnerException)
        {
            builder.AppendLine($"[{depth}] {exception.GetType().FullName}: {exception.Message}");

            builder.AppendLine(exception.StackTrace);
        }

        return builder.ToString();
    }

    private static IServiceProvider ConfigureServices()
    {
        ServiceCollection services = new();
        ServiceHelpers.PopulateCoreServices(services);

        // View models
        services.AddTransient<Odeon.ViewModels.NotificationViewModel>();
        services.AddTransient<Odeon.ViewModels.PropertyViewModel>();

        // Services
        services.AddSingleton<INavigationService, NavigationService>(_ => new NavigationService(
            new KeyValuePair<Type, Type>(typeof(HomePageViewModel), typeof(HomePage)),
            new KeyValuePair<Type, Type>(typeof(PlaylistsPageViewModel), typeof(PlaylistsPage)),
            new KeyValuePair<Type, Type>(typeof(PlaylistDetailsPageViewModel), typeof(PlaylistDetailsPage)),
            new KeyValuePair<Type, Type>(typeof(VideosPageViewModel), typeof(VideosPage)),
            new KeyValuePair<Type, Type>(typeof(AllVideosPageViewModel), typeof(AllVideosPage)),
            new KeyValuePair<Type, Type>(typeof(MusicPageViewModel), typeof(MusicPage)),
            new KeyValuePair<Type, Type>(typeof(SongsPageViewModel), typeof(SongsPage)),
            new KeyValuePair<Type, Type>(typeof(ArtistsPageViewModel), typeof(ArtistsPage)),
            new KeyValuePair<Type, Type>(typeof(AlbumsPageViewModel), typeof(AlbumsPage)),
            new KeyValuePair<Type, Type>(typeof(NetworkPageViewModel), typeof(NetworkPage)),
            new KeyValuePair<Type, Type>(typeof(PlayQueuePageViewModel), typeof(PlayQueuePage)),
            new KeyValuePair<Type, Type>(typeof(SettingsPageViewModel), typeof(SettingsPage)),
            new KeyValuePair<Type, Type>(typeof(AlbumDetailsPageViewModel), typeof(AlbumDetailsPage)),
            new KeyValuePair<Type, Type>(typeof(ArtistDetailsPageViewModel), typeof(ArtistDetailsPage)),
            new KeyValuePair<Type, Type>(typeof(SearchResultPageViewModel), typeof(SearchResultPage)),
            new KeyValuePair<Type, Type>(typeof(ArtistSearchResultPageViewModel), typeof(ArtistSearchResultPage)),
            new KeyValuePair<Type, Type>(typeof(AlbumSearchResultPageViewModel), typeof(AlbumSearchResultPage)),
            new KeyValuePair<Type, Type>(typeof(SongSearchResultPageViewModel), typeof(SongSearchResultPage)),
            new KeyValuePair<Type, Type>(typeof(VideoSearchResultPageViewModel), typeof(VideoSearchResultPage)),
            new KeyValuePair<Type, Type>(typeof(FolderViewPageViewModel), typeof(FolderViewPage)),
            new KeyValuePair<Type, Type>(typeof(FolderListViewPageViewModel), typeof(FolderListViewPage))
        ));

        return services.BuildServiceProvider();
    }

    private void SetMinWindowSize()
    {
        //var view = ApplicationView.GetForCurrentView();
        //view.SetPreferredMinSize(new Size(480, 270));
    }

    protected override void OnFileActivated(FileActivatedEventArgs args)
    {
        Frame rootFrame = InitRootFrame();
        if (rootFrame.Content is not MainPage)
        {
            rootFrame.Navigate(typeof(MainPage), true);
        }
        else if (rootFrame.Content is MainPage mainPage)
        {
            mainPage.EnsurePlayerVisible();
        }

        Window.Current.Activate();
        WeakReferenceMessenger.Default.Send(new PlayFilesMessage(args.Files, args.NeighboringFilesQuery));
    }

    /// <summary>
    /// Invoked when the application is launched normally by the end user.  Other entry points
    /// will be used such as when the application is launched to open a specific file.
    /// </summary>
    /// <param name="e">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        Frame rootFrame = InitRootFrame();
        // mpv initialization happens in PlayerService.Initialize()

        if (e.PrelaunchActivated) return;
        CoreApplication.EnablePrelaunch(true);
        if (rootFrame.Content == null)
        {
            SetMinWindowSize();
            rootFrame.Navigate(typeof(MainPage));
        }

        // Ensure the current window is active
        Window.Current.Activate();

#if DEBUG
        if (System.Diagnostics.Debugger.IsAttached)
        {
            //DebugSettings.EnableFrameRateCounter = true;
            //DebugSettings.EnableRedrawRegions = true;
            //DebugSettings.FailFastOnErrors = true;
            //DebugSettings.IsBindingTracingEnabled = true;
            //DebugSettings.IsOverdrawHeatMapEnabled = true;
            //DebugSettings.IsTextPerformanceVisualizationEnabled = true;
        }
#endif
    }

    /// <summary>
    /// Invoked when Navigation to a certain page fails
    /// </summary>
    /// <param name="sender">The Frame which failed navigation</param>
    /// <param name="e">Details about the navigation failure</param>
    void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
    }

    /// <summary>
    /// Invoked when application execution is being suspended.  Application state is saved
    /// without knowing whether the application will be terminated or resumed with the contents
    /// of memory still intact.
    /// </summary>
    /// <param name="sender">The source of the suspend request.</param>
    /// <param name="e">Details about the suspend request.</param>
    private async void OnSuspending(object sender, SuspendingEventArgs e)
    {
        SuspendingDeferral deferral = e.SuspendingOperation.GetDeferral();
        try
        {
            var tasks = WeakReferenceMessenger.Default.Send<SuspendingMessage>().Responses;
            await Task.WhenAll(tasks);
        }
        catch (Exception)
        {
            // pass
        }
        finally
        {
            deferral.Complete();
        }
    }

    private Frame InitRootFrame()
    {
        // Do not repeat app initialization when the Window already has content,
        // just ensure that the window is active
        if (Window.Current.Content is not Frame rootFrame)
        {
            // Create a Frame to act as the navigation context and navigate to the first page
            rootFrame = new Frame();

            rootFrame.NavigationFailed += OnNavigationFailed;

            // Place the frame in the current Window
            Window.Current.Content = rootFrame;
            SetMinWindowSize();

            // Turn off overscan on Xbox
            // https://learn.microsoft.com/en-us/windows/uwp/xbox-apps/turn-off-overscan
            if (DeviceInfoHelper.IsXbox)
            {
                Windows.UI.ViewManagement.ApplicationView.GetForCurrentView()
                    .SetDesiredBoundsMode(Windows.UI.ViewManagement.ApplicationViewBoundsMode.UseCoreWindow);
            }

            // Check for RTL flow direction
            if (GlobalizationHelper.IsRightToLeftLanguage)
            {
                rootFrame.FlowDirection = FlowDirection.RightToLeft;
            }

            rootFrame.RequestedTheme = ElementTheme.Dark;

            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            SetupTitleBarColors(ElementTheme.Dark);
            rootFrame.ActualThemeChanged += (sender, args) =>
            {
                SetupTitleBarColors(ElementTheme.Dark);
            };
        }

        return rootFrame;
    }

    public static void SetupTitleBarColors(ElementTheme theme = ElementTheme.Dark)
    {
        var titleBar = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView().TitleBar;
        if (titleBar == null) return;

        titleBar.ButtonBackgroundColor = Windows.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Windows.UI.Colors.Transparent;

        titleBar.ButtonForegroundColor = Windows.UI.Colors.White;
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(24, 255, 255, 255);
        titleBar.ButtonHoverForegroundColor = Windows.UI.Colors.White;
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(48, 255, 255, 255);
        titleBar.ButtonPressedForegroundColor = Windows.UI.Colors.White;
        titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 113, 113, 113);
    }
}