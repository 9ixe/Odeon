#nullable enable

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Odeon.Core.Messages;
using Odeon.Core.ViewModels;
using Odeon.Dialogs;

namespace Odeon.Commands;

internal class ShowPropertiesCommand : IRelayCommand<MediaViewModel>
{
    public event EventHandler? CanExecuteChanged;

    private readonly AsyncRelayCommand<MediaViewModel> _asyncCommand;

    public ShowPropertiesCommand()
    {
        _asyncCommand = new AsyncRelayCommand<MediaViewModel>(ShowDialog);
    }

    public bool CanExecute(object? parameter)
    {
        return parameter != null && _asyncCommand.CanExecute(parameter);
    }

    public void Execute(object? parameter)
    {
        if (parameter is MediaViewModel media)
            Execute(media);
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool CanExecute(MediaViewModel? parameter)
    {
        return parameter != null && _asyncCommand.CanExecute(parameter);
    }

    public void Execute(MediaViewModel? parameter)
    {
        if (parameter == null) return;
        _asyncCommand.Execute(parameter);
    }

    private async Task ShowDialog(MediaViewModel? parameter)
    {
        PropertiesDialog dialog = new()
        {
            Media = parameter
        };

        // Set override before showing dialog so it's active when the dialog closes
        // and FocusManagerOnFocusChanged fires during the close transition.
        WeakReferenceMessenger.Default.Send(new OverrideControlsHideDelayMessage(2000));
        await dialog.ShowAsync();
        WeakReferenceMessenger.Default.Send(new OverrideControlsHideDelayMessage(2000));

        _ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
        {
            if (Windows.UI.Xaml.Window.Current.Content is Windows.UI.Xaml.Controls.Frame frame &&
                frame.Content is Windows.UI.Xaml.Controls.Page page)
            {
                page.Focus(Windows.UI.Xaml.FocusState.Programmatic);
            }
        });
    }
}
