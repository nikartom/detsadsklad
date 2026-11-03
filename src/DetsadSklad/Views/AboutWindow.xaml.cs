using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace DetsadSklad.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            VersionText.Text = "Версия " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        }

        private void OnLinkClick(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Win32Exception)
            {
                // Например, на компьютере нет почтовой программы для ссылок mailto.
                Ask.Info("Не удалось открыть ссылку", "Откройте её вручную:\n" + e.Uri.OriginalString.Replace("mailto:", ""));
            }
            e.Handled = true;
        }
    }
}
