#nullable enable


using MediaViewModel = Odeon.Core.ViewModels.MediaViewModel;

namespace Odeon.Core.Common
{
    public interface IPropertiesDialog : IDialog
    {
        MediaViewModel? Media { get; set; }
    }
}
