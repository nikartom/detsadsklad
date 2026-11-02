using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DetsadSklad.Controls
{
    /// <summary>Значок из контура 24×24: не зависит от шрифтов Windows и не размывается при масштабе.</summary>
    public class Icon : FrameworkElement
    {
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(Geometry), typeof(Icon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
            typeof(Icon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

        public Geometry Data
        {
            get => (Geometry)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        protected override void OnRender(DrawingContext context)
        {
            if (Data == null || ActualWidth <= 0)
                return;
            const double designSize = 24;
            const double strokeWidth = 1.9;
            var pen = new Pen(Foreground, strokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            context.PushTransform(new ScaleTransform(ActualWidth / designSize, ActualHeight / designSize));
            context.DrawGeometry(null, pen, Data);
            context.Pop();
        }
    }

    /// <summary>Дополнительные свойства элементов: подсказка в поле, значок на кнопке, ошибка, фокус.</summary>
    public static class Ui
    {
        public static readonly DependencyProperty HintProperty =
            DependencyProperty.RegisterAttached("Hint", typeof(string), typeof(Ui), new PropertyMetadata(null));
        public static string GetHint(DependencyObject d) => (string)d.GetValue(HintProperty);
        public static void SetHint(DependencyObject d, string value) => d.SetValue(HintProperty, value);

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.RegisterAttached("Icon", typeof(Geometry), typeof(Ui), new PropertyMetadata(null));
        public static Geometry GetIcon(DependencyObject d) => (Geometry)d.GetValue(IconProperty);
        public static void SetIcon(DependencyObject d, Geometry value) => d.SetValue(IconProperty, value);

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.RegisterAttached("IsActive", typeof(bool), typeof(Ui), new PropertyMetadata(false));
        public static bool GetIsActive(DependencyObject d) => (bool)d.GetValue(IsActiveProperty);
        public static void SetIsActive(DependencyObject d, bool value) => d.SetValue(IsActiveProperty, value);

        public static readonly DependencyProperty HasErrorProperty =
            DependencyProperty.RegisterAttached("HasError", typeof(bool), typeof(Ui), new PropertyMetadata(false));
        public static bool GetHasError(DependencyObject d) => (bool)d.GetValue(HasErrorProperty);
        public static void SetHasError(DependencyObject d, bool value) => d.SetValue(HasErrorProperty, value);

        /// <summary>Колесо мыши над закрытым списком прокручивает страницу, а не меняет значение незаметно для пользователя.</summary>
        public static readonly DependencyProperty PassWheelToParentProperty = DependencyProperty.RegisterAttached(
            "PassWheelToParent", typeof(bool), typeof(Ui), new PropertyMetadata(false, OnPassWheelToParentChanged));
        public static bool GetPassWheelToParent(DependencyObject d) => (bool)d.GetValue(PassWheelToParentProperty);
        public static void SetPassWheelToParent(DependencyObject d, bool value) => d.SetValue(PassWheelToParentProperty, value);

        private static void OnPassWheelToParentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ComboBox combo && (bool)e.NewValue)
                combo.PreviewMouseWheel += OnComboWheel;
        }

        private static void OnComboWheel(object sender, MouseWheelEventArgs e)
        {
            var combo = (ComboBox)sender;
            if (combo.IsDropDownOpen)
                return;
            e.Handled = true;
            (VisualTreeHelper.GetParent(combo) as UIElement)?.RaiseEvent(
                new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = combo });
        }

        /// <summary>Когда значение становится true, поле получает фокус и выделяет текст.</summary>
        public static readonly DependencyProperty FocusWhenProperty = DependencyProperty.RegisterAttached(
            "FocusWhen", typeof(bool), typeof(Ui),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnFocusWhenChanged));
        public static bool GetFocusWhen(DependencyObject d) => (bool)d.GetValue(FocusWhenProperty);
        public static void SetFocusWhen(DependencyObject d, bool value) => d.SetValue(FocusWhenProperty, value);

        private static void OnFocusWhenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue || !(d is TextBox box))
                return;
            box.Dispatcher.BeginInvoke(new Action(() =>
            {
                box.Focus();
                box.SelectAll();
                box.BringIntoView();
                SetFocusWhen(box, false);
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    public class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var isEmpty = value == null || (value is string text && text.Length == 0);
            return isEmpty ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class NullToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var isEmpty = value == null || (value is string text && text.Length == 0);
            return isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class BoolToCollapsedConverter : IValueConverter
    {
        /// <summary>С параметром invert показывает элемент, наоборот, когда значение false.</summary>
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var flag = value is bool b && b;
            if (parameter as string == "invert")
                flag = !flag;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
