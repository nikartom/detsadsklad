using System.Windows;
using DetsadSklad.ViewModels;

namespace DetsadSklad.Views
{
    public partial class ProductDialog : Window
    {
        public ProductDialog()
        {
            InitializeComponent();
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (((ProductForm)DataContext).Validate())
                DialogResult = true;
        }
    }
}
