using System.Linq;
using System.Windows;

namespace DetsadSklad.Views
{
    public enum AskResult { Cancel, Primary, Secondary }

    public partial class AskWindow : Window
    {
        public AskResult Result { get; private set; } = AskResult.Cancel;

        public AskWindow(string title, string message, string primary, string secondary, string cancel, bool isDangerous)
        {
            InitializeComponent();
            Title = title;
            TitleText.Text = title;
            MessageText.Text = message;
            PrimaryButton.Content = primary;
            SecondaryButton.Content = secondary;
            CancelButton.Content = cancel;
            SecondaryButton.Visibility = secondary == null ? Visibility.Collapsed : Visibility.Visible;
            CancelButton.Visibility = cancel == null ? Visibility.Collapsed : Visibility.Visible;
            if (isDangerous)
                PrimaryButton.Style = (Style)FindResource("DangerFilledButton");
        }

        private void OnPrimary(object sender, RoutedEventArgs e) => Finish(AskResult.Primary);
        private void OnSecondary(object sender, RoutedEventArgs e) => Finish(AskResult.Secondary);
        private void OnCancel(object sender, RoutedEventArgs e) => Finish(AskResult.Cancel);

        private void Finish(AskResult result)
        {
            Result = result;
            Close();
        }
    }

    /// <summary>Вопросы пользователю. На кнопках написано действие, а не «Да/Нет».</summary>
    public static class Ask
    {
        public static bool Confirm(string title, string message, string confirmText, bool isDangerous = false) =>
            Show(title, message, confirmText, null, "Отмена", isDangerous) == AskResult.Primary;

        public static void Info(string title, string message) =>
            Show(title, message, "Понятно", null, null, false);

        public static AskResult SaveOrDiscard(string title, string message) =>
            Show(title, message, "Сохранить", "Не сохранять", "Отмена", false);

        /// <summary>Просит ввести название. null — пользователь передумал.</summary>
        public static string Text(string title, string hint)
        {
            var window = new InputWindow(title, hint);
            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
            if (owner != null && owner.IsVisible)
                window.Owner = owner;
            return window.ShowDialog() == true ? window.Value : null;
        }

        private static AskResult Show(string title, string message, string primary, string secondary, string cancel, bool isDangerous)
        {
            var window = new AskWindow(title, message, primary, secondary, cancel, isDangerous);
            // Вопрос из окна контракта должен появиться поверх него, а не за ним.
            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && !(w is AskWindow)) ?? Application.Current?.MainWindow;
            if (owner != null && owner.IsVisible)
                window.Owner = owner;
            else
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowDialog();
            return window.Result;
        }
    }
}
