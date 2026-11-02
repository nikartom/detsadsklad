using System.Windows;
using System.Windows.Controls;
using DetsadSklad.Controls;

namespace DetsadSklad.Views
{
    public partial class InputWindow : Window
    {
        public InputWindow(string title, string hint)
        {
            InitializeComponent();
            Title = title;
            TitleText.Text = title;
            Ui.SetHint(ValueBox, hint);
        }

        public string Value => ValueBox.Text.Trim();

        private void OnAdd(object sender, RoutedEventArgs e)
        {
            if (Value.Length > 0)
                DialogResult = true;
            else
                ErrorText.Visibility = Visibility.Visible;
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }
}
