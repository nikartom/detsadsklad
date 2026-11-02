using System;
using System.Windows.Input;
using DetsadSklad.Models;
using DetsadSklad.Views;

namespace DetsadSklad.ViewModels
{
    public class SettingsViewModel : ObservableObject, ILeaveGuard
    {
        private readonly MainViewModel _main;
        private readonly Organization _organization;

        private string _name;
        private string _address;
        private string _email;
        private string _deliveryAddress;
        private string _deliveryTime;
        private string _employeeName;
        private string _signerName;
        private bool _highlight;
        private bool _isDirty;
        private bool _showErrors;
        private string _error;
        private string _savedNote;

        public ICommand SaveCommand { get; }
        public ICommand OpenDataFolderCommand { get; }

        public SettingsViewModel(MainViewModel main)
        {
            _main = main;
            _organization = main.Store.Data.Organization;
            _name = _organization.Name;
            _address = _organization.Address;
            _email = _organization.Email;
            _deliveryAddress = _organization.DeliveryAddress;
            _deliveryTime = _organization.DefaultDeliveryTime;
            _employeeName = _organization.EmployeeName;
            _signerName = _organization.SignerName;
            _highlight = _organization.HighlightVariableFields;

            SaveCommand = new RelayCommand(() => Save());
            OpenDataFolderCommand = new RelayCommand(() => _main.Documents.OpenFolder(_main.Store.DataDir));
        }

        public string Name { get => _name; set => Edit(ref _name, value, nameof(Name), nameof(NameHasError)); }
        public string Address { get => _address; set => Edit(ref _address, value, nameof(Address), nameof(AddressHasError)); }
        public string Email { get => _email; set => Edit(ref _email, value, nameof(Email)); }
        public string DeliveryAddress { get => _deliveryAddress; set => Edit(ref _deliveryAddress, value, nameof(DeliveryAddress), nameof(DeliveryAddressHasError)); }
        public string DeliveryTime { get => _deliveryTime; set => Edit(ref _deliveryTime, value, nameof(DeliveryTime)); }
        public string EmployeeName { get => _employeeName; set => Edit(ref _employeeName, value, nameof(EmployeeName)); }
        public string SignerName { get => _signerName; set => Edit(ref _signerName, value, nameof(SignerName), nameof(SignerNameHasError)); }
        public bool Highlight { get => _highlight; set => Edit(ref _highlight, value, nameof(Highlight)); }

        public bool NameHasError => _showErrors && string.IsNullOrWhiteSpace(_name);
        public bool AddressHasError => _showErrors && string.IsNullOrWhiteSpace(_address);
        public bool DeliveryAddressHasError => _showErrors && string.IsNullOrWhiteSpace(_deliveryAddress);
        public bool SignerNameHasError => _showErrors && string.IsNullOrWhiteSpace(_signerName);

        public string Error { get => _error; private set => Set(ref _error, value); }
        public string SavedNote { get => _savedNote; private set => Set(ref _savedNote, value); }

        public string DataFolderText => _main.Store.DataDir;

        public bool CanLeave()
        {
            if (!_isDirty)
                return true;
            switch (Ask.SaveOrDiscard("Сохранить настройки?", "Вы внесли изменения, но ещё не сохранили их."))
            {
                case AskResult.Primary:
                    return Save();
                case AskResult.Secondary:
                    return true;
                default:
                    return false;
            }
        }

        private bool Save()
        {
            _showErrors = true;
            OnPropertyChanged(nameof(NameHasError));
            OnPropertyChanged(nameof(AddressHasError));
            OnPropertyChanged(nameof(DeliveryAddressHasError));
            OnPropertyChanged(nameof(SignerNameHasError));

            Error = FindError();
            if (Error != null)
                return false;

            if (_organization.HighlightVariableFields != _highlight)
                _main.Store.Data.DocumentsValidFromUtc = DateTime.UtcNow;

            _organization.Name = _name.Trim();
            _organization.Address = _address.Trim();
            _organization.Email = (_email ?? "").Trim();
            _organization.DeliveryAddress = _deliveryAddress.Trim();
            _organization.DefaultDeliveryTime = (_deliveryTime ?? "").Trim();
            _organization.EmployeeName = (_employeeName ?? "").Trim();
            _organization.SignerName = _signerName.Trim();
            _organization.HighlightVariableFields = _highlight;
            _main.Store.Save();
            _main.RefreshHeader();

            _isDirty = false;
            SavedNote = "Настройки сохранены";
            return true;
        }

        private string FindError()
        {
            if (string.IsNullOrWhiteSpace(_name))
                return "Напишите название детского сада.";
            if (string.IsNullOrWhiteSpace(_address))
                return "Напишите адрес детского сада.";
            if (string.IsNullOrWhiteSpace(_deliveryAddress))
                return "Напишите адрес поставки товара.";
            if (string.IsNullOrWhiteSpace(_signerName))
                return "Укажите, чья подпись будет в документах, например: И.И. Иванова.";
            return null;
        }

        private void Edit<T>(ref T field, T value, string name, string errorFlag = null)
        {
            if (!Set(ref field, value, name))
                return;
            if (errorFlag != null)
                OnPropertyChanged(errorFlag);
            _isDirty = true;
            SavedNote = null;
        }
    }
}
