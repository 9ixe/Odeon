#nullable enable

using System;
using Odeon.Animations;
using Odeon.Core.Enums;
using Odeon.Core.Services;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Odeon.Helpers;

public static class ThemeHelper
{
    private static UISettings? _uiSettings;
    private static ISettingsService? _settingsService;
    private static bool _isInitialized;

    // Signature Netflix / Odeon red colors
    private static readonly Color RedAccent = Color.FromArgb(255, 232, 17, 35);       // #FFE81123
    private static readonly Color RedHover = Color.FromArgb(255, 234, 56, 72);         // #FFEA3848
    private static readonly Color RedPressed = Color.FromArgb(255, 197, 14, 31);       // #FFC50E1F
    private static readonly Color RedDisabled = Color.FromArgb(128, 232, 17, 35);     // #80E81123
    private static readonly Color RedSecondary = Color.FromArgb(230, 232, 17, 35);    // #E6E81123
    private static readonly Color RedTertiary = Color.FromArgb(204, 232, 17, 35);     // #CCE81123

    // Monochromatic / Pure white accent colors
    private static readonly Color WhiteAccent = Color.FromArgb(255, 255, 255, 255);    // #FFFFFFFF
    private static readonly Color WhiteHover = Color.FromArgb(255, 230, 230, 230);     // #FFE6E6E6
    private static readonly Color WhitePressed = Color.FromArgb(255, 190, 190, 190);   // #FFBEBEBE
    private static readonly Color WhiteDisabled = Color.FromArgb(128, 255, 255, 255); // #80FFFFFF
    private static readonly Color WhiteSecondary = Color.FromArgb(230, 255, 255, 255);// #E6FFFFFF
    private static readonly Color WhiteTertiary = Color.FromArgb(204, 255, 255, 255); // #CCFFFFFF

    public static void Initialize(ISettingsService settingsService)
    {
        if (_isInitialized) return;
        _isInitialized = true;

        _settingsService = settingsService;

        try
        {
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += (sender, args) =>
            {
                var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView?.CoreWindow?.Dispatcher;
                if (dispatcher != null)
                {
                    _ = dispatcher.RunAsync(
                        Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (_settingsService?.AccentColor == AccentColorOption.System)
                            {
                                ApplyAccentColor(AccentColorOption.System);
                            }
                        });
                }
            };
        }
        catch (Exception ex)
        {
            LogService.Log($"ThemeHelper: Failed to initialize UISettings: {ex.Message}");
        }

        // If user explicitly selected System accent or Monochrome, apply the appropriate brushes.
        // If Red (default), the XAML resources in App.xaml already define Netflix Red, so no mutation is performed.
        if (settingsService.AccentColor != AccentColorOption.Red)
        {
            ApplyAccentColor(settingsService.AccentColor);
        }
    }

    public static void ApplyAccentColor(AccentColorOption option)
    {
        try
        {
            Color accent;
            Color hover;
            Color pressed;
            Color disabled;
            Color secondary;
            Color tertiary;
            Color buttonForeground;

            if (option == AccentColorOption.System)
            {
                _uiSettings ??= new UISettings();
                accent = _uiSettings.GetColorValue(UIColorType.Accent);
                Color accentDark1 = _uiSettings.GetColorValue(UIColorType.AccentDark1);
                Color accentLight1 = _uiSettings.GetColorValue(UIColorType.AccentLight1);

                hover = accentLight1;
                pressed = accentDark1;
                disabled = Color.FromArgb(128, accent.R, accent.G, accent.B);
                secondary = Color.FromArgb(230, accent.R, accent.G, accent.B);
                tertiary = Color.FromArgb(204, accent.R, accent.G, accent.B);

                double luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
                buttonForeground = luminance > 0.5 ? Colors.Black : Colors.White;
            }
            else if (option == AccentColorOption.Monochrome)
            {
                accent = WhiteAccent;
                hover = WhiteHover;
                pressed = WhitePressed;
                disabled = WhiteDisabled;
                secondary = WhiteSecondary;
                tertiary = WhiteTertiary;
                buttonForeground = Colors.Black;
            }
            else
            {
                accent = RedAccent;
                hover = RedHover;
                pressed = RedPressed;
                disabled = RedDisabled;
                secondary = RedSecondary;
                tertiary = RedTertiary;
                buttonForeground = Colors.Black;
            }

            ResourceDictionary appResources = Application.Current.Resources;
            if (appResources.ThemeDictionaries.TryGetValue("Default", out object dictObj) && dictObj is ResourceDictionary defaultDict)
            {
                UpdateBrush(defaultDict, "SystemControlHighlightAccentBrush", accent);
                UpdateBrush(defaultDict, "SystemControlBackgroundAccentBrush", accent);
                UpdateBrush(defaultDict, "SystemControlHighlightAltAccentBrush", accent);
                UpdateBrush(defaultDict, "SystemAccentColorBrush", accent);
                UpdateBrush(defaultDict, "AccentFillColorDefaultBrush", accent);
                UpdateBrush(defaultDict, "AccentFillColorSecondaryBrush", secondary);
                UpdateBrush(defaultDict, "AccentFillColorTertiaryBrush", tertiary);
                UpdateBrush(defaultDict, "AccentFillColorCustomBrush", accent);
                UpdateBrush(defaultDict, "AccentTextFillColorPrimaryBrush", accent);
                UpdateBrush(defaultDict, "AccentTextFillColorSecondaryBrush", accent);
                UpdateBrush(defaultDict, "AccentTextFillColorTertiaryBrush", accent);
                UpdateBrush(defaultDict, "AccentTextFillColorDisabledBrush", disabled);

                UpdateBrush(defaultDict, "AccentButtonBackground", accent);
                UpdateBrush(defaultDict, "AccentButtonBackgroundPointerOver", hover);
                UpdateBrush(defaultDict, "AccentButtonBackgroundPressed", pressed);
                UpdateBrush(defaultDict, "AccentButtonForeground", buttonForeground);
                UpdateBrush(defaultDict, "AccentButtonForegroundPointerOver", buttonForeground);
                UpdateBrush(defaultDict, "AccentButtonForegroundPressed", buttonForeground);

                UpdateBrush(defaultDict, "TextOnAccentFillColorPrimaryBrush", buttonForeground);

                UpdateBrush(defaultDict, "ToggleSwitchFillOn", accent);
                UpdateBrush(defaultDict, "ToggleSwitchFillOnPointerOver", hover);
                UpdateBrush(defaultDict, "ToggleSwitchFillOnPressed", pressed);
                UpdateBrush(defaultDict, "ToggleSwitchFillOnDisabled", disabled);
                UpdateBrush(defaultDict, "ToggleSwitchStrokeOn", accent);
                UpdateBrush(defaultDict, "ToggleSwitchStrokeOnPointerOver", hover);
                UpdateBrush(defaultDict, "ToggleSwitchStrokeOnPressed", pressed);
                UpdateBrush(defaultDict, "ToggleSwitchStrokeOnDisabled", disabled);

                UpdateBrush(defaultDict, "ToggleButtonBackgroundChecked", accent);
                UpdateBrush(defaultDict, "ToggleButtonBackgroundCheckedPointerOver", hover);
                UpdateBrush(defaultDict, "ToggleButtonBackgroundCheckedPressed", pressed);
                UpdateBrush(defaultDict, "ToggleButtonForegroundChecked", buttonForeground);
                UpdateBrush(defaultDict, "ToggleButtonForegroundCheckedPointerOver", buttonForeground);
                UpdateBrush(defaultDict, "ToggleButtonForegroundCheckedPressed", buttonForeground);
                UpdateBrush(defaultDict, "ToggleButtonBorderBrushChecked", accent);
                UpdateBrush(defaultDict, "ToggleButtonBorderBrushCheckedPointerOver", hover);
                UpdateBrush(defaultDict, "ToggleButtonBorderBrushCheckedPressed", pressed);

                UpdateBrush(defaultDict, "CheckBoxCheckBackgroundFillChecked", accent);
                UpdateBrush(defaultDict, "CheckBoxCheckBackgroundFillCheckedPointerOver", hover);
                UpdateBrush(defaultDict, "CheckBoxCheckBackgroundFillCheckedPressed", pressed);
                UpdateBrush(defaultDict, "CheckBoxCheckBackgroundFillCheckedDisabled", disabled);
                UpdateBrush(defaultDict, "CheckBoxCheckBackgroundStrokeChecked", accent);
                UpdateBrush(defaultDict, "CheckBoxCheckBackgroundStrokeCheckedPointerOver", hover);
                UpdateBrush(defaultDict, "CheckBoxCheckBackgroundStrokeCheckedPressed", pressed);
                UpdateBrush(defaultDict, "CheckBoxCheckBackgroundStrokeCheckedDisabled", disabled);
                UpdateBrush(defaultDict, "CheckBoxCheckGlyphForegroundChecked", buttonForeground);
                UpdateBrush(defaultDict, "CheckBoxCheckGlyphForegroundCheckedPointerOver", buttonForeground);
                UpdateBrush(defaultDict, "CheckBoxCheckGlyphForegroundCheckedPressed", buttonForeground);

                UpdateBrush(defaultDict, "RadioButtonCheckBackgroundFillChecked", accent);
                UpdateBrush(defaultDict, "RadioButtonCheckBackgroundFillCheckedPointerOver", hover);
                UpdateBrush(defaultDict, "RadioButtonCheckBackgroundFillCheckedPressed", pressed);
                UpdateBrush(defaultDict, "RadioButtonCheckBackgroundFillCheckedDisabled", disabled);
                UpdateBrush(defaultDict, "RadioButtonCheckBackgroundStrokeChecked", accent);
                UpdateBrush(defaultDict, "RadioButtonCheckBackgroundStrokeCheckedPointerOver", hover);
                UpdateBrush(defaultDict, "RadioButtonCheckBackgroundStrokeCheckedPressed", pressed);
                UpdateBrush(defaultDict, "RadioButtonCheckBackgroundStrokeCheckedDisabled", disabled);
                UpdateBrush(defaultDict, "RadioButtonCheckGlyphFillChecked", buttonForeground);
                UpdateBrush(defaultDict, "RadioButtonCheckGlyphFillCheckedPointerOver", buttonForeground);
                UpdateBrush(defaultDict, "RadioButtonCheckGlyphFillCheckedPressed", buttonForeground);

                UpdateBrush(defaultDict, "ProgressBarForeground", accent);

                // Update Color resources so code reading SystemAccentColor gets the correct value
                UpdateColor(defaultDict, "SystemAccentColor", accent);
                UpdateColor(defaultDict, "SystemAccentColorDark1", pressed);
                UpdateColor(defaultDict, "SystemAccentColorDark2", pressed);
                UpdateColor(defaultDict, "SystemAccentColorDark3", pressed);
                UpdateColor(defaultDict, "SystemAccentColorLight1", hover);
                UpdateColor(defaultDict, "SystemAccentColorLight2", hover);
                UpdateColor(defaultDict, "SystemAccentColorLight3", hover);
                UpdateColor(defaultDict, "AccentFillColorCustom", accent);

                // Update AnimatedPlayingVisualSource instances in the visual tree
                if (Window.Current?.Content is FrameworkElement rootElement)
                {
                    UpdateAnimatedPlayingVisualColors(rootElement, accent);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"ThemeHelper.ApplyAccentColor failed: {ex.Message}");
        }
    }

    private static void UpdateBrush(ResourceDictionary dict, string key, Color color)
    {
        try
        {
            if (dict.TryGetValue(key, out object obj) && obj is SolidColorBrush brush)
            {
                brush.Color = color;
            }
            else
            {
                dict[key] = new SolidColorBrush(color);
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"ThemeHelper.UpdateBrush failed for '{key}': {ex.Message}");
        }
    }

    private static void UpdateColor(ResourceDictionary dict, string key, Color color)
    {
        try
        {
            dict[key] = color;
        }
        catch (Exception ex)
        {
            LogService.Log($"ThemeHelper.UpdateColor failed for '{key}': {ex.Message}");
        }
    }

    private static void UpdateAnimatedPlayingVisualColors(DependencyObject parent, Color color)
    {
        try
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is AnimatedPlayingVisualSource animSource)
                {
                    animSource.Color_FFFFFF = color;
                }
                UpdateAnimatedPlayingVisualColors(child, color);
            }
        }
        catch
        {
            // Visual tree traversal can fail if elements are not yet loaded
        }
    }
}
