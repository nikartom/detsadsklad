using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using DetsadSklad.Models;
using DetsadSklad.Services;
using DetsadSklad.Views;

namespace DetsadSklad.ViewModels
{
    /// <summary>Поставщик в списке, одной строкой: название — 2 контракта: Бакалея, Заморозка.</summary>
    public class SupplierRow
    {
        public SupplierRow(Supplier supplier, DateTime today)
        {
            Supplier = supplier;
            var open = RequestLogic.OpenContracts(supplier, today);
            var supplies = open
                .Select(c => (c.Note ?? "").Trim())
                .Where(note => note.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            SuppliesText = open.Count == 0
                ? "нет действующих контрактов"
                : ContractsCount(open.Count) + (supplies.Count == 0 ? "" : ": " + string.Join(", ", supplies));
        }

        /// <summary>1 контракт, 2 контракта, 5 контрактов.</summary>
        public static string ContractsCount(int count)
        {
            var lastTwo = count % 100;
            var last = count % 10;
            var word = lastTwo >= 11 && lastTwo <= 14 ? "контрактов"
                : last == 1 ? "контракт"
                : last >= 2 && last <= 4 ? "контракта"
                : "контрактов";
            return count + " " + word;
        }

        public Supplier Supplier { get; }
        public string Name => Supplier.ShortName;
        public string SuppliesText { get; }
        public bool IsInactive => !Supplier.IsActive;
    }

    /// <summary>Действующий контракт — строка, которую можно править прямо в панели.</summary>
    public class ContractRow : ObservableObject
    {
        private readonly Action _changed;
        private readonly DateTime? _originalSignedOn;
        private readonly DateTime _originalValidFrom;
        private string _note;
        private string _number;
        private DateTime? _signedOn;
        private DateTime? _validTo;
        private bool _showErrors;
        private bool _focusNumber;

        public ContractRow(Contract contract, bool isUsed, Action changed)
        {
            Id = contract.Id;
            IsUsed = isUsed;
            _note = contract.Note;
            _number = contract.Number;
            _signedOn = contract.SignedOn == default(DateTime) ? (DateTime?)null : contract.SignedOn;
            _validTo = contract.ValidTo == default(DateTime) ? (DateTime?)null : contract.ValidTo;
            _originalSignedOn = _signedOn;
            _originalValidFrom = contract.ValidFrom;
            _changed = changed;
        }

        public Guid Id { get; }

        /// <summary>К контракту уже есть заявки — удалять нельзя.</summary>
        public bool IsUsed { get; }

        public bool FocusNumber { get => _focusNumber; set => Set(ref _focusNumber, value); }

        public string Note { get => _note; set => Edit(ref _note, value); }
        public string Number { get => _number; set => Edit(ref _number, value); }
        public DateTime? SignedOn { get => _signedOn; set => Edit(ref _signedOn, value); }
        public DateTime? ValidTo { get => _validTo; set => Edit(ref _validTo, value); }

        public bool ShowErrors
        {
            get => _showErrors;
            set { if (Set(ref _showErrors, value)) NotifyErrors(); }
        }

        public bool NumberHasError => _showErrors && string.IsNullOrWhiteSpace(_number);
        public bool SignedOnHasError => _showErrors && !_signedOn.HasValue;
        public bool ValidToHasError => _showErrors && (!_validTo.HasValue || _validTo < _signedOn);

        public string FindError()
        {
            if (string.IsNullOrWhiteSpace(_number))
                return "Впишите номер контракта" + (string.IsNullOrWhiteSpace(_note) ? "." : $" «{_note.Trim()}».");
            if (!_signedOn.HasValue)
                return $"Укажите, когда заключён контракт № {_number}.";
            if (!_validTo.HasValue)
                return $"Укажите, по какое число действует контракт № {_number}.";
            if (_validTo < _signedOn)
                return $"У контракта № {_number} дата окончания раньше даты заключения.";
            return null;
        }

        public void ApplyTo(Contract contract)
        {
            contract.Note = (_note ?? "").Trim();
            contract.Number = _number.Trim();
            contract.SignedOn = _signedOn.Value.Date;
            contract.ValidTo = _validTo.Value.Date;
            // Контракт действует со дня заключения. Если дату не трогали, прежнее начало сохраняем как было.
            contract.ValidFrom = _signedOn == _originalSignedOn && _originalValidFrom != default(DateTime) ? _originalValidFrom : _signedOn.Value.Date;
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
            OnPropertyChanged(nameof(ValidToHasError));
        }
    }

    /// <summary>Контракт, срок которого закончился и который ещё не продлён.</summary>
    public class FinishedContractRow
    {
        public FinishedContractRow(Contract contract)
        {
            Contract = contract;
            Title = string.IsNullOrWhiteSpace(contract.Note) ? "Контракт № " + contract.Number : contract.Note.Trim();
            Details = $"№ {contract.Number}, срок закончился {RuFormat.ShortDate(contract.ValidTo)}";
        }

        public Contract Contract { get; }
        public string Title { get; }
        public string Details { get; }
    }


    /// <summary>Карточка поставщика: реквизиты и контракты. Открывается на месте списка, только когда нажали на поставщика.</summary>
    public class SupplierEditor : ObservableObject
    {
        private readonly DataStore _store;
        private readonly AppData _data;
        private readonly Supplier _supplier;
        private bool _showErrors;
        private string _shortName;
        private string _fullName;
        private string _address;
        private string _email;
        private string _phone;
        private bool _isActive;
        private string _error;

        public ObservableCollection<ContractRow> Contracts { get; } = new ObservableCollection<ContractRow>();
        public ObservableCollection<FinishedContractRow> FinishedContracts { get; } = new ObservableCollection<FinishedContractRow>();

        public ICommand AddContractCommand { get; }
        public ICommand RemoveContractCommand { get; }
        public ICommand RenewContractCommand { get; }

        public SupplierEditor(DataStore store, Supplier supplier, bool isNew)
        {
            _store = store;
            _data = store.Data;
            _supplier = supplier;
            IsNew = isNew;
            AddContractCommand = new RelayCommand(AddContract);
            RemoveContractCommand = new RelayCommand(c => RemoveContract((ContractRow)c));
            RenewContractCommand = new RelayCommand(c => RenewContract((FinishedContractRow)c));

            _shortName = supplier.ShortName;
            _fullName = supplier.FullName;
            _address = supplier.Address;
            _email = supplier.Email;
            _phone = supplier.Phone;
            _isActive = supplier.IsActive;

            var today = DateTime.Today;
            foreach (var contract in supplier.Contracts.Where(c => !c.IsFinished(today)).OrderBy(c => c.Note).ThenBy(c => c.ValidFrom))
                Contracts.Add(new ContractRow(contract, _data.Requests.Any(r => r.ContractId == contract.Id), MarkDirty));
            // Закончившиеся показываем, только пока их не продлили: продлённые старые контракты не мешают.
            foreach (var contract in supplier.Contracts
                .Where(c => c.IsFinished(today) && !supplier.Contracts.Any(other => other != c && other.ValidTo > c.ValidTo && RequestLogic.IsSameKind(other, c)))
                .OrderBy(c => c.Note))
                FinishedContracts.Add(new FinishedContractRow(contract));
            IsDirty = false;
        }

        public bool IsNew { get; }
        public bool IsDirty { get; private set; }
        public string Title => IsNew ? "Новый поставщик" : _supplier.ShortName;
        public bool HasContracts => Contracts.Count > 0;
        public bool HasFinishedContracts => FinishedContracts.Count > 0;

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

        private void AddContract()
        {
            Contracts.Add(new ContractRow(RequestLogic.SuggestNewContract(DateTime.Today), false, MarkDirty) { FocusNumber = true });
            NotifyContracts();
            MarkDirty();
        }

        /// <summary>Тот же контракт на новый срок: остаётся вписать номер и дату, продукты прежние.</summary>
        private void RenewContract(FinishedContractRow row)
        {
            var contract = RequestLogic.SuggestRenewal(row.Contract, DateTime.Today);
            contract.SignedOn = contract.ValidFrom;
            Contracts.Add(new ContractRow(contract, false, MarkDirty) { FocusNumber = true });
            FinishedContracts.Remove(row);
            NotifyContracts();
            MarkDirty();
        }

        private void RemoveContract(ContractRow row)
        {
            if (row.IsUsed)
            {
                Ask.Info("Контракт нельзя удалить", $"По контракту № {row.Number} уже оформлены заявки.");
                return;
            }
            Contracts.Remove(row);
            NotifyContracts();
            MarkDirty();
        }

        /// <summary>Проверяет и записывает. false — есть ошибка, она показана внизу окна.</summary>
        public bool Save()
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

            _supplier.ShortName = _shortName.Trim();
            _supplier.FullName = _fullName.Trim();
            _supplier.Address = _address.Trim();
            _supplier.Email = (_email ?? "").Trim();
            _supplier.Phone = (_phone ?? "").Trim();
            _supplier.IsActive = _isActive;

            // Строки окна — действующие контракты; закончившиеся остаются как были.
            var today = DateTime.Today;
            var old = _supplier.Contracts;
            _supplier.Contracts = old.Where(c => c.IsFinished(today) && Contracts.All(row => row.Id != c.Id))
                .Concat(Contracts.Select(row =>
                {
                    var contract = old.FirstOrDefault(c => c.Id == row.Id) ?? new Contract { Id = row.Id };
                    row.ApplyTo(contract);
                    return contract;
                }))
                .ToList();

            if (IsNew)
                _data.Suppliers.Add(_supplier);
            _store.Save();
            IsDirty = false;
            return true;
        }

        /// <summary>Удаляет поставщика после подтверждения. false — нельзя или передумали.</summary>
        public bool Delete()
        {
            if (_data.Requests.Any(r => r.SupplierId == _supplier.Id))
            {
                Ask.Info("Поставщика нельзя удалить",
                    "У этого поставщика есть заявки. Чтобы он больше не предлагался в новых заявках, снимите галочку «Работаем с этим поставщиком» и нажмите «Сохранить».");
                return false;
            }
            if (!Ask.Confirm($"Удалить {_supplier.ShortName}?", "Поставщик и его контракты будут удалены.", "Удалить", isDangerous: true))
                return false;
            _data.Suppliers.Remove(_supplier);
            foreach (var product in _data.Products.Where(p => p.SupplierId == _supplier.Id))
                product.SupplierId = null;
            _store.Save();
            IsDirty = false;
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
            var numbers = Contracts.Select(c => c.Number.Trim())
                .Concat(_supplier.Contracts.Where(c => Contracts.All(row => row.Id != c.Id)).Select(c => (c.Number ?? "").Trim()));
            var duplicate = numbers.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            return duplicate == null ? null : $"Контракт № {duplicate.Key} у этого поставщика уже есть.";
        }

        private void NotifyContracts()
        {
            OnPropertyChanged(nameof(HasContracts));
            OnPropertyChanged(nameof(HasFinishedContracts));
        }

        private void MarkDirty() => IsDirty = true;

        private void Edit<T>(ref T field, T value, string name, string errorFlag = null)
        {
            if (!Set(ref field, value, name))
                return;
            if (errorFlag != null)
                OnPropertyChanged(errorFlag);
            MarkDirty();
        }
    }

    /// <summary>
    /// Страница «Поставщики»: список. Нажали на поставщика — на месте списка открывается
    /// его карточка для изменения; «Сохранить» или «Отмена» возвращают к списку.
    /// </summary>
    public class SuppliersViewModel : ObservableObject, ILeaveGuard
    {
        private readonly MainViewModel _main;
        private readonly AppData _data;
        private SupplierEditor _editor;

        public ObservableCollection<SupplierRow> Rows { get; } = new ObservableCollection<SupplierRow>();

        public ICommand AddCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand DeleteCommand { get; }

        public SuppliersViewModel(MainViewModel main)
        {
            _main = main;
            _data = main.Store.Data;
            AddCommand = new RelayCommand(() => Open(new Supplier(), isNew: true));
            OpenCommand = new RelayCommand(row => Open(((SupplierRow)row).Supplier, isNew: false));
            SaveCommand = new RelayCommand(() => { if (_editor.Save()) BackToList(); });
            CancelCommand = new RelayCommand(() => { if (CanLeave()) BackToList(); });
            DeleteCommand = new RelayCommand(() => { if (_editor.Delete()) BackToList(); });
            Refresh();
        }

        /// <summary>Открытый поставщик; null — показан список.</summary>
        public SupplierEditor Editor
        {
            get => _editor;
            private set
            {
                if (!Set(ref _editor, value))
                    return;
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(IsListVisible));
            }
        }

        public bool IsEditing => _editor != null;
        public bool IsListVisible => _editor == null;
        public bool HasRows => Rows.Count > 0;

        public bool CanLeave()
        {
            if (_editor == null || !_editor.IsDirty)
                return true;
            switch (Ask.SaveOrDiscard("Сохранить поставщика?", "Вы внесли изменения, но ещё не сохранили их."))
            {
                case AskResult.Primary:
                    return _editor.Save();
                case AskResult.Secondary:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Открывает поставщика на месте списка.</summary>
        public void Open(Supplier supplier, bool isNew)
        {
            Editor = new SupplierEditor(_main.Store, supplier, isNew);
        }

        private void BackToList()
        {
            Editor = null;
            Refresh();
        }

        private void Refresh()
        {
            var today = DateTime.Today;
            Rows.Clear();
            foreach (var supplier in _data.Suppliers.OrderByDescending(s => s.IsActive).ThenBy(s => s.ShortName))
                Rows.Add(new SupplierRow(supplier, today));
            OnPropertyChanged(nameof(HasRows));
        }
    }
}
