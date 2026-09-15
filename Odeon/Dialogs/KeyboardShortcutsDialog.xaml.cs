#nullable enable

using Odeon.Helpers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Odeon.Dialogs
{
    public sealed partial class KeyboardShortcutsDialog : ContentDialog
    {
        public KeyboardShortcutsDialog()
        {
            this.InitializeComponent();
            FlowDirection = GlobalizationHelper.GetFlowDirection();
        }
    }
}
