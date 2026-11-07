using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using DetsadSklad.Services;
using DetsadSklad.ViewModels;
using DetsadSklad.Views;

namespace DetsadSklad
{
    public partial class App : Application
    {
        private const int RestoreWindow = 9;
        private const string PortableFolderName = "Данные программы";
        private const string DocumentsFolderName = "ДетсадСклад";

        private DataStore _store;
        private FileStream _lock;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            SetRussianCulture();
            DispatcherUnhandledException += OnUnhandledException;
            EventManager.RegisterClassHandler(typeof(TextBox), DataObject.PastingEvent, new DataObjectPastingEventHandler(OnPasting));

            _store = new DataStore(ResolveDataDir());
            if (!TryTakeLock())
            {
                ActivateRunningCopy();
                Shutdown();
                return;
            }

            try
            {
                _store.Load();
            }
            catch (InvalidDataException error)
            {
                _store.LogError(error);
                MessageBox.Show(
                    "Не удалось прочитать файл с данными, а подходящей резервной копии нет.\n\n"
                    + "Ничего не удалено. Покажите это сообщение тому, кто настраивал программу:\n" + _store.DataFile,
                    "Детсад-Склад", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            var window = new MainWindow { DataContext = new MainViewModel(_store) };
            MainWindow = window;
            window.Show();

            if (_store.RecoveredFrom != null)
            {
                Ask.Info("Данные восстановлены",
                    "Основной файл с данными оказался повреждён, поэтому программа взяла последнюю резервную копию. "
                    + "Проверьте, пожалуйста, последние заявки.");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _lock?.Dispose();
            base.OnExit(e);
        }

        private static void SetRussianCulture()
        {
            var ru = CultureInfo.GetCultureInfo("ru-RU");
            Thread.CurrentThread.CurrentCulture = ru;
            Thread.CurrentThread.CurrentUICulture = ru;
            CultureInfo.DefaultThreadCurrentCulture = ru;
            CultureInfo.DefaultThreadCurrentUICulture = ru;
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("ru-RU")));
        }

        /// <summary>Однострочное поле вставило бы только первую строку — склеиваем строки через пробел.</summary>
        private static void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!(sender is TextBox box) || box.AcceptsReturn || !e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
                return;
            var text = e.SourceDataObject.GetData(DataFormats.UnicodeText, true) as string;
            var singleLine = TextTools.ToSingleLine(text);
            if (singleLine == text)
                return;
            var replacement = new DataObject();
            replacement.SetData(DataFormats.UnicodeText, singleLine);
            replacement.SetData(DataFormats.Text, singleLine);
            e.DataObject = replacement;
        }

        /// <summary>
        /// Папка «Данные программы» рядом с exe — работа с флешки. Иначе данные в «Документах»,
        /// чтобы они не потерялись, если сам файл программы перенесут на Рабочий стол.
        /// </summary>
        private static string ResolveDataDir()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var portable = Path.Combine(exeDir, PortableFolderName);
            if (Directory.Exists(portable))
                return portable;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DocumentsFolderName);
        }

        // Пока программа открыта, файл-замок занят: вторая копия не перезапишет чужие изменения.
        private bool TryTakeLock()
        {
            try
            {
                _lock = new FileStream(_store.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void ActivateRunningCopy()
        {
            var current = Process.GetCurrentProcess();
            var other = Process.GetProcessesByName(current.ProcessName)
                .FirstOrDefault(p => p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero);
            if (other != null)
            {
                ShowWindow(other.MainWindowHandle, RestoreWindow);
                SetForegroundWindow(other.MainWindowHandle);
                return;
            }
            MessageBox.Show("Программа уже открыта на другом компьютере, который работает с этой же папкой данных.\nЗакройте её там и попробуйте снова.",
                "Детсад-Склад", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _store?.LogError(e.Exception);
            e.Handled = true;

            // Окно ещё не открылось — работать дальше не с чем, сообщаем и закрываемся.
            if (MainWindow == null || !MainWindow.IsVisible)
            {
                MessageBox.Show("Программу не удалось запустить.\n\n" + e.Exception.Message, "Детсад-Склад", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            var isFileProblem = e.Exception is IOException || e.Exception is UnauthorizedAccessException;
            Ask.Info("Что-то пошло не так",
                isFileProblem
                    ? "Не удалось записать файл. Проверьте, что файл не открыт в другой программе и на диске есть место, затем попробуйте ещё раз.\n\n" + e.Exception.Message
                    : "Произошла ошибка, но программа продолжает работать. Если она повторится, сообщите о ней настройщику: подробности записаны в файл «ошибки.log».\n\n" + e.Exception.Message);
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);
    }
}
