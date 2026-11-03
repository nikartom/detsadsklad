using System.Windows;
using System.Windows.Input;
using DetsadSklad.Models;
using DetsadSklad.Services;
using DetsadSklad.Views;

namespace DetsadSklad.ViewModels
{
    public enum Section { Requests, Suppliers, Products, Settings }

    public class MainViewModel : ObservableObject
    {
        private object _currentPage;
        private Section _section;

        public DataStore Store { get; }
        public DocumentService Documents { get; }

        public ICommand ShowRequestsCommand { get; }
        public ICommand ShowSuppliersCommand { get; }
        public ICommand ShowProductsCommand { get; }
        public ICommand ShowSettingsCommand { get; }
        public ICommand ShowAboutCommand { get; }

        public MainViewModel(DataStore store)
        {
            Store = store;
            Documents = new DocumentService(store);
            ShowRequestsCommand = new RelayCommand(ShowRequests);
            ShowSuppliersCommand = new RelayCommand(() => Navigate(Section.Suppliers, new SuppliersViewModel(this)));
            ShowProductsCommand = new RelayCommand(() => Navigate(Section.Products, new ProductsViewModel(this)));
            ShowSettingsCommand = new RelayCommand(() => Navigate(Section.Settings, new SettingsViewModel(this)));
            ShowAboutCommand = new RelayCommand(() => new AboutWindow { Owner = Application.Current.MainWindow }.ShowDialog());
            ShowRequests();
        }

        public object CurrentPage
        {
            get => _currentPage;
            private set => Set(ref _currentPage, value);
        }

        public bool IsRequestsActive => _section == Section.Requests;
        public bool IsSuppliersActive => _section == Section.Suppliers;
        public bool IsProductsActive => _section == Section.Products;
        public bool IsSettingsActive => _section == Section.Settings;

        public string OrganizationName => Store.Data.Organization.Name;

        public string EmployeeText => string.IsNullOrWhiteSpace(Store.Data.Organization.EmployeeName)
            ? "Сотрудник не указан"
            : Store.Data.Organization.EmployeeName;

        public void ShowRequests() => ShowRequests(null);

        public void ShowRequests(SupplyRequest select)
        {
            Navigate(Section.Requests, new RequestsViewModel(this, select));
        }

        public void ShowEditor(RequestEditorViewModel editor) => Navigate(Section.Requests, editor);

        public void RefreshHeader()
        {
            OnPropertyChanged(nameof(OrganizationName));
            OnPropertyChanged(nameof(EmployeeText));
        }

        /// <summary>Закрытие окна: спрашиваем то же, что при переходе на другой экран.</summary>
        public bool CanClose() => !(CurrentPage is ILeaveGuard guard) || guard.CanLeave();

        private void Navigate(Section section, object page)
        {
            if (CurrentPage is ILeaveGuard guard && !guard.CanLeave())
                return;
            _section = section;
            CurrentPage = page;
            OnPropertyChanged(nameof(IsRequestsActive));
            OnPropertyChanged(nameof(IsSuppliersActive));
            OnPropertyChanged(nameof(IsProductsActive));
            OnPropertyChanged(nameof(IsSettingsActive));
        }
    }
}
