using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DetsadSklad.ViewModels;

namespace DetsadSklad.Views
{
    public partial class RequestsView : UserControl
    {
        public RequestsView()
        {
            InitializeComponent();
        }

        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var list = (ListBox)sender;
            var row = ItemsControl.ContainerFromElement(list, (DependencyObject)e.OriginalSource);
            if (row != null && DataContext is RequestsViewModel page && page.EditCommand.CanExecute(null))
                page.EditCommand.Execute(null);
        }
    }
}
