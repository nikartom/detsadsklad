using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DetsadSklad.Models;
using DetsadSklad.Services;
using DetsadSklad.ViewModels;
using DetsadSklad.Views;

namespace DetsadSklad.Tests
{
    /// <summary>
    /// Открывает все экраны и окна на тестовых данных. Ошибки разметки и привязок WPF
    /// видны только при показе экрана, поэтому ловим их здесь, а не у пользователя.
    /// </summary>
    internal static class ViewSmoke
    {
        public static List<string> Run(string dataDir)
        {
            var problems = new List<string>();
            var thread = new Thread(() =>
            {
                try
                {
                    Open(dataDir, problems);
                }
                catch (Exception error)
                {
                    problems.Add(error.GetBaseException().Message);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return problems;
        }

        private static void Open(string dataDir, List<string> problems)
        {
            // Ресурсы вида pack://application:,,,/Assets/… лежат в программе, а не в сборке проверок.
            // Обычное свойство WPF уже успевает запомнить сборку проверок, поэтому меняем внутреннее поле.
            typeof(Application).GetField("_resourceAssembly", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, typeof(App).Assembly);
            var bindingErrors = new BindingErrorListener();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

            var app = new App();
            app.InitializeComponent();
            // В App.xaml программа закрывается вместе с главным окном; здесь окна открываются и закрываются по очереди.
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += (s, e) =>
            {
                problems.Add(e.Exception.GetBaseException().Message);
                e.Handled = true;
            };

            var store = new DataStore(dataDir);
            store.Load();
            TestData.Fill(store.Data);
            var supplier = store.Data.Suppliers[0];
            supplier.Contracts[0].Note = "Молочная продукция";
            supplier.Contracts.Add(new Contract { Number = "СТАРЫЙ", SignedOn = new DateTime(2020, 1, 1), ValidFrom = new DateTime(2020, 1, 1), ValidTo = new DateTime(2020, 3, 31) });
            store.Data.Products[0].Category = "Молочная продукция";

            var main = new MainViewModel(store);
            var window = new MainWindow
            {
                DataContext = main,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000
            };
            window.Show();
            Flush();

            main.ShowSuppliersCommand.Execute(null);
            Snap(window, "1-поставщики");
            var suppliers = (SuppliersViewModel)main.CurrentPage;
            suppliers.OpenCommand.Execute(suppliers.Rows[0]);
            Snap(window, "2-поставщик");
            var editor = suppliers.Editor;
            editor.RenewContractCommand.Execute(editor.FinishedContracts[0]);
            editor.Contracts.Last().Number = "НОВЫЙ";
            editor.Contracts.Last().Note = "Бакалея";
            Snap(window, "3-продление");
            suppliers.SaveCommand.Execute(null);
            if (suppliers.Editor != null)
                problems.Add("поставщик не сохранился: " + suppliers.Editor.Error);
            Snap(window, "4-после-сохранения");
            suppliers.AddCommand.Execute(null);
            Snap(window, "5-новый-поставщик");
            suppliers.CancelCommand.Execute(null);
            Flush();
            suppliers.ShowImport(ContractPdfReader.Read(TestContract.Build()), "Контракт.pdf");
            Snap(window, "5a-загрузка-контракта");
            suppliers.CancelCommand.Execute(null);
            Flush();

            main.ShowProductsCommand.Execute(null);
            Snap(window, "6-продукты");
            var products = (ProductsViewModel)main.CurrentPage;
            products.OpenCommand.Execute(products.Rows[0]);
            Snap(window, "7-продукт");
            products.CancelCommand.Execute(null);
            Flush();
            main.ShowSettingsCommand.Execute(null);
            Snap(window, "8-настройки");
            main.ShowRequestsCommand.Execute(null);
            Snap(window, "9-заявки");
            var requests = (RequestsViewModel)main.CurrentPage;
            requests.ShowCommand.Execute(requests.Rows[0]);
            Snap(window, "10-заявка");
            requests.BackCommand.Execute(null);
            Flush();
            main.ShowEditor(RequestEditorViewModel.CreateNew(main));
            Snap(window, "11-новая-заявка");
            main.ShowEditor(RequestEditorViewModel.Edit(main, store.Data.Requests[0]));
            Snap(window, "12-изменение-заявки");

            var productForm = new ProductForm(store.Data, Product.CreateNew(supplier.Id, "Бакалея"));
            ShowAndClose(new ProductDialog { DataContext = productForm, Owner = window }, "10-новый-продукт");
            var calendar = new System.Windows.Controls.Calendar { Style = (Style)app.FindResource("LargeCalendar"), DisplayDate = new DateTime(2026, 11, 1) };
            ShowAndClose(new Window { Content = new System.Windows.Controls.Border { Child = calendar }, SizeToContent = SizeToContent.WidthAndHeight }, "11-календарь");

            window.Close();
            Flush();
            app.Shutdown();
            problems.AddRange(bindingErrors.Errors.Distinct());
        }

        private static void ShowAndClose(Window dialog, string snapshot, Func<bool> beforeClose = null)
        {
            dialog.ShowActivated = false;
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Left = -20000;
            dialog.Top = -20000;
            dialog.Show();
            Snap(dialog, snapshot);
            beforeClose?.Invoke();
            dialog.Close();
        }

        /// <summary>
        /// Если задана переменная DETSAD_SCREENSHOTS — сохраняет снимок окна в эту папку,
        /// чтобы посмотреть экраны без запуска программы.
        /// </summary>
        private static void Snap(Window window, string name)
        {
            Flush();
            var dir = Environment.GetEnvironmentVariable("DETSAD_SCREENSHOTS");
            if (string.IsNullOrEmpty(dir))
                return;
            Directory.CreateDirectory(dir);
            var content = (FrameworkElement)window.Content;
            var width = content.ActualWidth + content.Margin.Left + content.Margin.Right;
            var height = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
            var picture = new DrawingVisual();
            using (var context = picture.RenderOpen())
            {
                context.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, width, height));
                context.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null,
                    new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(picture);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(dir, name + ".png")))
                encoder.Save(file);
        }

        /// <summary>Даём WPF построить и разметить экран.</summary>
        private static void Flush()
        {
            Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle);
        }

        private class BindingErrorListener : TraceListener
        {
            public List<string> Errors { get; } = new List<string>();
            public override void Write(string message) { }
            public override void WriteLine(string message) => Errors.Add(message);
        }
    }
}
