using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using DetsadSklad.Models;
using DetsadSklad.Services;
using DetsadSklad.Views;

namespace DetsadSklad.ViewModels
{
    /// <summary>Контракт в форме поставщика.</summary>
    public class ContractRow : ObservableObject
    {
        private readonly Action _changed;
        private string _number;
        private DateTime? _signedOn;
        private DateTime? _validFrom;
        private DateTime? _validTo;
        private bool _showErrors;
        private bool _focusNumber;

        public ContractRow(Contract contract, bool isUsed, Action changed)
        {
            Id = contract.Id;
            IsUsed = isUsed;
            _number = contract.Number;
            _signedOn = contract.SignedOn == default(DateTime) ? (DateTime?)null : contract.SignedOn;
            _validFrom = contract.ValidFrom == default(DateTime) ? (DateTime?)null : contract.ValidFrom;
            _validTo = contract.ValidTo == default(DateTime) ? (DateTime?)null : contract.ValidTo;
            _changed = changed;
        }

        public Guid Id { get; }

        public bool FocusNumber
        {
            get => _focusNumber;
            set => Set(ref _focusNumber, value);
        }

        /// <summary>К контракту уже есть заявки — удалять нельзя.</summary>
        public bool IsUsed { get; }

        public string Number { get => _number; set => Edit(ref _number, value); }
        public DateTime? SignedOn { get => _signedOn; set => Edit(ref _signedOn, value); }
        public DateTime? ValidFrom { get => _validFrom; set => Edit(ref _validFrom, value); }
        public DateTime? ValidTo { get => _validTo; set => Edit(ref _validTo, value); }

        public bool ShowErrors
        {
            get => _showErrors;
            set
            {
                if (!Set(ref _showErrors, value))
                    return;
                NotifyErrors();
            }
        }

        public bool NumberHasError => _showErrors && string.IsNullOrWhiteSpace(_number);
        public bool SignedOnHasError => _showErrors && !_signedOn.HasValue;
        public bool ValidFromHasError => _showErrors && !_validFrom.HasValue;
        public bool ValidToHasError => _showErrors && (!_validTo.HasValue || _validTo < _validFrom);

        public string FindError()
        {
            if (string.IsNullOrWhiteSpace(_number))
                return "Укажите номер контракта.";
            if (!_signedOn.HasValue)
                return $"Укажите дату заключения контракта № {_number}.";
            if (!_validFrom.HasValue || !_validTo.HasValue)
                return $"Укажите, с какого и по какое число действует контракт № {_number}.";
            if (_validTo < _validFrom)
                return $"У контракта № {_number} дата окончания раньше даты начала.";
            return null;
        }

        public void ApplyTo(Contract contract)
        {
            contract.Number = _number.Trim();
            contract.SignedOn = _signedOn.Value.Date;
            contract.ValidFrom = _validFrom.Value.Date;
            contract.ValidTo = _validTo.Value.Date;
        }

        private void Edit<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        {
            if (!Set(ref field, value, name))
                return;
            NotifyErrors();
            _changed();
        }

        private void NotifyErrors()
        {
            OnPropertyChanged(nameof(NumberHasError));
            OnPropertyChanged(nameof(SignedOnHasError));
            OnPropertyChanged(nameof(ValidFromHasError));
            OnPropertyChanged(nameof(ValidToHasError));
        }
    }

    public class SuppliersViewModel : ObservableObject, ILeaveGuard
    {
        private readonly MainViewModel _main;
        private readonly AppData _data;

        private Supplier _selected;
        private bool _refreshing;
        private bool _isNew;
        private bool _isDirty;
        private bool _hasForm;
        private bool _showErrors;
        private string _shortName;
        private string _fullName;
        private string _address;
        private string _email;
        private string _phone;
        private bool _isActive;
        private string _error;
        private string _savedNote;

        public ObservableCollection<Supplier> Rows { get; } = new ObservableCollection<Supplier>();
        public ObservableCollection<ContractRow> Contracts { get; } = new ObservableCollection<ContractRow>();

        public ICommand AddCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand AddContractCommand { get; }
        public ICommand RemoveContractCommand { get; }

        public SuppliersViewModel(MainViewModel main)
        {
            _main = main;
            _data = main.Store.Data;
            AddCommand = new RelayCommand(AddNew);
            SaveCommand = new RelayCommand(() => Save());
            DeleteCommand = new RelayCommand(Delete, () => _selected != null);
            AddContractCommand = new RelayCommand(AddContract);
            RemoveContractCommand = new RelayCommand(c => RemoveContract((ContractRow)c));

            Refresh();
            Selected = Rows.FirstOrDefault();
        }

        public Supplier Selected
        {
            get => _selected;
            set
            {
                // Пока список перестраивается, он сам сбрасывает выделение — это не выбор пользователя.
                if (_refreshing || value == _selected)
                    return;
                if (!CanLeave())
                {
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(nameof(Selected))));
                    return;
                }
                _selected = value;
                OnPropertyChanged();
                LoadForm(value, isNew: false);
            }
        }

        public bool HasForm { get => _hasForm; private set => Set(ref _hasForm, value); }
        public bool HasRows => Rows.Count > 0;
        public string FormTitle => _isNew ? "Новый поставщик" : "Поставщик";

        public string ShortName { get => _shortName; set => Edit(ref _shortName, value, nameof(ShortName), nameof(ShortNameHasError)); }
        public string FullName { get => _fullName; set => Edit(ref _fullName, value, nameof(FullName), nameof(FullNameHasError)); }
        public string Address { get => _address; set => Edit(ref _address, value, nameof(Address), nameof(AddressHasError)); }
        public string Email { get => _email; set => Edit(ref _email, value, nameof(Email)); }
        public string Phone { get => _phone; set => Edit(ref _phone, value, nameof(Phone)); }
        public bool IsActive { get => _isActive; set => Edit(ref _isActive, value, nameof(IsActive)); }

        public bool ShortNameHasError => _showErrors && string.IsNullOrWhiteSpace(_shortName);
        public bool FullNameHasError => _showErrors && string.IsNullOrWhiteSpace(_fullName);
        public bool AddressHasError => _showErrors && string.IsNullOrWhiteSpace(_address);

        public string Error { get => _error; private set => Set(ref _error, value); }
        public string SavedNote { get => _savedNote; private set => Set(ref _savedNote, value); }

        public bool CanLeave()
        {
            if (!_isDirty)
                return true;
            switch (Ask.SaveOrDiscard("Сохранить поставщика?", "Вы внесли изменения, но ещё не сохранили их."))
            {
                case AskResult.Primary:
                    return Save();
                case AskResult.Secondary:
                    _isDirty = false;
                    return true;
                default:
                    return false;
            }
        }

        private void AddNew()
        {
            if (!CanLeave())
                return;
            _selected = null;
            OnPropertyChanged(nameof(Selected));
            LoadForm(new Supplier(), isNew: true);
        }

        private void LoadForm(Supplier supplier, bool isNew)
        {
            _isNew = isNew;
            _showErrors = false;
            Error = null;
            SavedNote = null;
            Contracts.Clear();
            HasForm = supplier != null;
            if (supplier != null)
            {
                _shortName = supplier.ShortName;
                _fullName = supplier.FullName;
                _address = supplier.Address;
                _email = supplier.Email;
                _phone = supplier.Phone;
                _isActive = supplier.IsActive;
                foreach (var contract in supplier.Contracts.OrderByDescending(c => c.ValidTo))
                    Contracts.Add(new ContractRow(contract, IsContractUsed(contract.Id), MarkDirty));
            }
            foreach (var name in new[] { nameof(ShortName), nameof(FullName), nameof(Address), nameof(Email), nameof(Phone), nameof(IsActive), nameof(FormTitle),
                                         nameof(ShortNameHasError), nameof(FullNameHasError), nameof(AddressHasError) })
                OnPropertyChanged(name);
            _isDirty = false;
        }

        private bool IsContractUsed(Guid contractId) => _data.Requests.Any(r => r.ContractId == contractId);

        private void AddContract()
        {
            var supplier = _selected ?? new Supplier();
            RequestLogic.SuggestContractPeriod(supplier, DateTime.Today, out var from, out var to);
            // Если в форме уже есть новый контракт, следующий предлагаем за ним.
            var latest = Contracts.Where(c => c.ValidTo.HasValue).Select(c => c.ValidTo.Value).DefaultIfEmpty().Max();
            if (latest != default(DateTime) && latest >= from)
            {
                from = latest.AddDays(1);
                RequestLogic.GetQuarter(from, out _, out to);
            }
            var contract = new Contract { SignedOn = DateTime.Today, ValidFrom = from, ValidTo = to };
            Contracts.Insert(0, new ContractRow(contract, false, MarkDirty) { FocusNumber = true });
            MarkDirty();
        }

        private void RemoveContract(ContractRow row)
        {
            if (row.IsUsed)
            {
                Ask.Info("Контракт нельзя удалить", $"К контракту № {row.Number} уже оформлены заявки.");
                return;
            }
            Contracts.Remove(row);
            MarkDirty();
        }

        private bool Save()
        {
            _showErrors = true;
            foreach (var contract in Contracts)
                contract.ShowErrors = true;
            OnPropertyChanged(nameof(ShortNameHasError));
            OnPropertyChanged(nameof(FullNameHasError));
            OnPropertyChanged(nameof(AddressHasError));

            Error = FindError();
            if (Error != null)
                return false;

            var supplier = _isNew || _selected == null ? new Supplier() : _selected;
            supplier.ShortName = _shortName.Trim();
            supplier.FullName = _fullName.Trim();
            supplier.Address = _address.Trim();
            supplier.Email = (_email ?? "").Trim();
            supplier.Phone = (_phone ?? "").Trim();
            supplier.IsActive = _isActive;

            var old = supplier.Contracts;
            supplier.Contracts = Contracts.Select(row =>
            {
                var contract = old.FirstOrDefault(c => c.Id == row.Id) ?? new Contract { Id = row.Id };
                row.ApplyTo(contract);
                return contract;
            }).ToList();

            if (_isNew)
                _data.Suppliers.Add(supplier);
            _main.Store.Save();

            _isDirty = false;
            _selected = supplier;
            Refresh();
            OnPropertyChanged(nameof(Selected));
            LoadForm(supplier, isNew: false);
            SavedNote = "Сохранено";
            return true;
        }

        private string FindError()
        {
            if (string.IsNullOrWhiteSpace(_shortName))
                return "Напишите короткое название поставщика — оно будет в списках.";
            if (string.IsNullOrWhiteSpace(_fullName))
                return "Напишите полное наименование поставщика — оно попадёт в документ.";
            if (string.IsNullOrWhiteSpace(_address))
                return "Укажите адрес поставщика — он попадёт в документ.";
            foreach (var contract in Contracts)
            {
                var error = contract.FindError();
                if (error != null)
                    return error;
            }
            var duplicate = Contracts.GroupBy(c => c.Number.Trim()).FirstOrDefault(g => g.Count() > 1);
            return duplicate == null ? null : $"Контракт № {duplicate.Key} указан дважды.";
        }

        private void Delete()
        {
            if (_data.Requests.Any(r => r.SupplierId == _selected.Id))
            {
                Ask.Info("Поставщика нельзя удалить",
                    "У этого поставщика есть заявки. Чтобы он больше не предлагался в новых заявках, снимите галочку «Работаем с этим поставщиком» и нажмите «Сохранить».");
                return;
            }
            var confirmed = Ask.Confirm($"Удалить {_selected.ShortName}?", "Поставщик и его контракты будут удалены.", "Удалить", isDangerous: true);
            if (!confirmed)
                return;
            _data.Suppliers.Remove(_selected);
            foreach (var product in _data.Products.Where(p => p.SupplierId == _selected.Id))
                product.SupplierId = null;
            _main.Store.Save();
            _isDirty = false;
            _selected = null;
            Refresh();
            Selected = Rows.FirstOrDefault();
            if (_selected == null)
                LoadForm(null, isNew: false);
        }

        private void Refresh()
        {
            _refreshing = true;
            Rows.Clear();
            foreach (var supplier in _data.Suppliers.OrderByDescending(s => s.IsActive).ThenBy(s => s.ShortName))
                Rows.Add(supplier);
            _refreshing = false;
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(HasRows));
        }

        private void MarkDirty()
        {
            _isDirty = true;
            SavedNote = null;
        }

        private void Edit<T>(ref T field, T value, string name, string errorFlag = null)
        {
            if (!Set(ref field, value, name))
                return;
            if (errorFlag != null)
                OnPropertyChanged(errorFlag);
            MarkDirty();
        }
    }
}
