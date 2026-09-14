using Odeon.Core.Enums;
using System;
using Windows.UI.Xaml;

namespace Odeon.Core.Helpers;

public static class EnumExtensions
{
    public static ElementTheme ToElementTheme(this ThemeOption themeOption)
    {
        return ElementTheme.Dark;
    }
}
