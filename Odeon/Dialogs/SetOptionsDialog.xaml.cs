using System.Linq;
using Odeon.Helpers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

// The Content Dialog item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Odeon.Dialogs;

public sealed partial class SetOptionsDialog : ContentDialog
{
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(string), typeof(SetOptionsDialog), new PropertyMetadata(default(string)));

    public string Options
    {
        get { return (string)GetValue(OptionsProperty); }
        set { SetValue(OptionsProperty, value); }
    }

    private string OptionTextBoxPlaceholder { get; }

    private string[] CommandLineHelpTextParts { get; }

    public SetOptionsDialog(string existingOptions, bool global = false)
    {
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ElementTheme.Dark;
        OptionTextBoxPlaceholder = "--option=value";
        Options = existingOptions;
        OptionsTextBox.Text = Options;
        var helpText = Strings.Resources.CommandLineHelpText;
        CommandLineHelpTextParts = helpText.Contains("{0}")
            ? helpText.Split("{0}").Select(s => s.Trim()).Take(2).ToArray()
            : new[] { helpText, string.Empty };

        if (global)
        {
            SecondaryButtonText = string.Empty;

            // Remove the first two inlines
            HelpText.Inlines.RemoveAt(0);
            HelpText.Inlines.RemoveAt(0);
        }
    }
}
