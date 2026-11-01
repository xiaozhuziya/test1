using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
namespace IndustrialHmi.App.Controls;
public sealed class SignalBrushConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? new SolidColorBrush(Color.FromRgb(66,214,176)) : new SolidColorBrush(Color.FromRgb(74,88,108));
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
