using System.ComponentModel;
using System.Windows;
using DetsadSklad.ViewModels;

namespace DetsadSklad.Views
{
    public partial class MainWindow : Window
    {
        private const double NarrowWindowWidth = 1150;
        private const double NarrowSidebarWidth = 186;
        private const double WideSidebarWidth = 218;

        public MainWindow()
        {
            InitializeComponent();
            if (SystemParameters.WorkArea.Height < Height || SystemParameters.WorkArea.Width < Width)
                WindowState = WindowState.Maximized;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            // На маленьком экране меню уступает место таблицам.
            SidebarColumn.Width = new GridLength(sizeInfo.NewSize.Width < NarrowWindowWidth ? NarrowSidebarWidth : WideSidebarWidth);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is MainViewModel main && !main.CanClose())
                e.Cancel = true;
            base.OnClosing(e);
        }
    }
}
