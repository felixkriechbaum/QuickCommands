using Avalonia.Data.Converters;
using Avalonia.Media;

namespace QuickCommands.App.ViewModels;

public static class Converters
{
    public static IValueConverter BoldIfTrue { get; } =
        new FuncValueConverter<bool, FontWeight>(value => value ? FontWeight.SemiBold : FontWeight.Normal);
}
