using System.Globalization;
using System.Windows;
using System.Windows.Data;
using SystevoTune.App.ViewModels;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.App.Converters;

/// <summary><c>true</c> shows, <c>false</c> collapses.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary><c>false</c> shows, <c>true</c> collapses. For "nothing to show here" panels.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not Visibility.Visible;
}

/// <summary>Shows an element only when a string has something in it.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a tweak status to the localisation key describing it, so status words are translated
/// rather than shown as English enum names.
/// </summary>
public sealed class TweakStatusKeyConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TweakStatus status
            ? "Status_" + status
            : string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Colours an apply row by how it ended.</summary>
public sealed class OutcomeKindToBrushKeyConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ApplyOutcomeKind kind
            ? kind switch
            {
                ApplyOutcomeKind.Failed => "Danger",
                ApplyOutcomeKind.Warning => "Warning",
                ApplyOutcomeKind.Applied => "Success",
                _ => "Muted",
            }
            : "Muted";

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
