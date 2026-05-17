using System.Globalization;
using PhantomVault.Core.Models;

namespace PhantomVault.Android.Converters;

/// <summary>
/// Converts a Credential's EntryType enum to a representative emoji icon.
/// </summary>
public sealed class EntryTypeIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is EntryType type ? type switch
        {
            EntryType.Password => "🔑",
            EntryType.WiFi => "📶",
            EntryType.Identity => "🪪",
            EntryType.ApiKey => "🔧",
            EntryType.Contact => "👤",
            EntryType.CreditCard => "💳",
            EntryType.BankAccount => "🏦",
            EntryType.TotpGenerator => "🔐",
            EntryType.PinCode => "🔢",
            _ => "🔑"
        } : "🔑";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Returns true when the value is not null.</summary>
public sealed class NotNullBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Returns true when the string value is not null or empty.</summary>
public sealed class NotNullOrEmptyBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Inverts a bool value.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>
/// Converts a bool to one of two strings split by '|'.
/// ConverterParameter="TrueText|FalseText"
/// </summary>
public sealed class BoolToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split('|') ?? Array.Empty<string>();
        if (value is bool b)
            return b ? (parts.Length > 0 ? parts[0] : "True") : (parts.Length > 1 ? parts[1] : "False");
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Shows busy text or normal text based on IsBusy bool. ConverterParameter="BusyText|NormalText"</summary>
public sealed class BusyTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split('|') ?? Array.Empty<string>();
        if (value is bool isBusy)
            return isBusy ? (parts.Length > 0 ? parts[0] : "Loading…") : (parts.Length > 1 ? parts[1] : "Go");
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts bool to eye icon (👁 / 🙈) for password visibility toggle.</summary>
public sealed class BoolToEyeIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool visible && visible ? "👁" : "🙈";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Masks a password string with bullets unless visible.</summary>
public sealed class PasswordMaskConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new string('•', (value as string)?.Length ?? 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts bool to Color — green for true, red for false (used as BackgroundColor).</summary>
public sealed class BoolToSuccessColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Color.FromArgb("#1E7A3E") : Color.FromArgb("#7A1E1E");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Returns true when the string is not null or empty (for IsVisible bindings).</summary>
public sealed class StringToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public sealed class BoolToTitleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split('|') ?? Array.Empty<string>();
        return value is bool b && b ? (parts.Length > 0 ? parts[0] : "") : (parts.Length > 1 ? parts[1] : "");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a bool to one of two Color values split by '|'.
/// ConverterParameter="ActiveColorHex|InactiveColorHex"
/// </summary>
public sealed class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split('|') ?? Array.Empty<string>();
        var activeHex = parts.Length > 0 ? parts[0] : "#6C3483";
        var inactiveHex = parts.Length > 1 ? parts[1] : "#1E1E1E";
        return value is bool b && b ? Color.FromArgb(activeHex) : Color.FromArgb(inactiveHex);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Highlights a type-filter chip when the bound nullable EntryType matches the int parameter.
/// ConverterParameter is the int value of the EntryType enum member.
/// </summary>
public sealed class TypeFilterChipBgConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is null) return Color.FromArgb("#1E1E1E");
        if (!int.TryParse(parameter.ToString(), out var typeInt)) return Color.FromArgb("#1E1E1E");
        var active = value is EntryType et && (int)et == typeInt;
        return active ? Color.FromArgb("#6C3483") : Color.FromArgb("#1E1E1E");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

