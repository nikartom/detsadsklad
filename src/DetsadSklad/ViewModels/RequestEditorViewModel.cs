using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DetsadSklad.Models;
using DetsadSklad.Services;
using DetsadSklad.Views;

namespace DetsadSklad.ViewModels
{
    /// <summary>Строка заявки на экране: количество и цену можно вводить как угодно, пока не поздно поправить.</summary>
    public class LineRow : ObservableObject
    {
        private readonly Action _changed;
        private string _quantityText;
        private string _priceText;
        private int _position;
        private bool _focusQuantity;

        public LineRow(RequestLine line, Action changed)
        {
            ProductId = line.ProductId;
            Name = line.Name;
            Description = line.Description;
            Unit = line.Unit;
            _quantityText = line.Quantity > 0 ? RuFormat.Quantity(line.Quantity) : "";
            _priceText = RuFormat.DocMoney(line.Price);
            _changed = changed;
        }

        public Guid? ProductId { get; }
        public string Name { get; }
        public string Description { get; }
        public string Unit { get; }

        /// <summary>Описание без повторения наименования — подзаголовок строки.</summary>
        public string Details => Description != null && Description.StartsWith(Name + ".")
            ? Description.Substring(Name.Length + 1).Trim()
            : Description == Name ? "" : Description;

        public int Position
        {
            get => _position;
            set => Set(ref _position, value);
        }

        public bool FocusQuantity
        {
            get => _focusQuantity;
            set => Set(ref _focusQuantity, value);
        }

        public string QuantityText
        {
            get => _quantityText;
            set
            {
                if (!Set(ref _quantityText, value))
                    return;
                NotifyAmounts();
            }
        }

        public string PriceText
        {
            get => _priceText;
            set
            {
                if (!Set(ref _priceText, value))
                    return;
                NotifyAmounts();
            }
        }

        public bool IsQuantityValid => RuFormat.TryParseDecimal(_quantityText, out var value) && value > 0;
        public bool IsPriceValid => RuFormat.TryParseDecimal(_priceText, out var value) && value >= 0;
        public bool QuantityHasError => ShowErrors && !IsQuantityValid;
        public bool PriceHasError => ShowErrors && !IsPriceValid;

        private bool _showErrors;
        public bool ShowErrors
        {
            get => _showErrors;
            set
            {
                if (!Set(ref _showErrors, value))
                    return;
                OnPropertyChanged(nameof(QuantityHasError));
                OnPropertyChanged(nameof(PriceHasError));
            }
        }

        public decimal Cost => IsQuantityValid && IsPriceValid ? ToLine().Cost : 0;
        public string CostText => IsQuantityValid && IsPriceValid ? RuFormat.DocMoney(Cost) : "—";

        public RequestLine ToLine()
        {
            RuFormat.TryParseDecimal(_quantityText, out var quantity);
            RuFormat.TryParseDecimal(_priceText, out var price);
            return new RequestLine { ProductId = ProductId, Name = Name, Description = Description, Unit = Unit, Quantity = quantity, Price = price };
        }

        private void NotifyAmounts()
        {
            OnPropertyChanged(nameof(QuantityHasError));
            OnPropertyChanged(nameof(PriceHasError));
            OnPropertyChanged(nameof(CostText));
            _changed();
        }
    }

    public class RequestEditorViewModel : ObservableObject, ILeaveGuard
    {
        private readonly MainViewModel _main;
        private readonly AppData _data;
        private readonly SupplyRequest _existing;

        private Supplier _supplier;
        private Contract _contract;
        private string _numberText;
        private bool _numberIsAutomatic;
        private DateTime? _deliveryDate;
        private DateTime? _sentDate;
        private string _deliveryTime;
        private string _catalogSearch = "";
        private bool _showAllProducts;
        private string _errorMessage;
        private bool _isDirty;
        private bool _refreshRequisites;
        private bool _showErrors;

        public ObservableCollection<Supplier> Suppliers { get; } = new ObservableCollection<Supplier>();
        public ObservableCollection<Contract> Contracts { get; } = new ObservableCollection<Contract>();
        public ObservableCollection<LineRow> Lines { get; } = new ObservableCollection<LineRow>();
        public ObservableCollection<ProductRow> CatalogItems { get; } = new ObservableCollection<ProductRow>();

        public ICommand AddProductCommand { get; }
        public ICommand RemoveLineCommand { get; }
        public ICommand NewProductCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SaveAndOpenCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand RefreshRequisitesCommand { get; }

        public static RequestEditorViewModel CreateNew(MainViewModel main) => new RequestEditorViewModel(main, null, null);
        public static RequestEditorViewModel CreateCopy(MainViewModel main, SupplyRequest source) => new RequestEditorViewModel(main, null, source);
        public static RequestEditorViewModel Edit(MainViewModel main, SupplyRequest request) => new RequestEditorViewModel(main, request, null);

        private RequestEditorViewModel(MainViewModel main, SupplyRequest existing, SupplyRequest copyFrom)
        {
            _main = main;
            _data = main.Store.Data;
            _existing = existing;

            AddProductCommand = new RelayCommand(row => AddProduct(((ProductRow)row).Product));
            RemoveLineCommand = new RelayCommand(l => RemoveLine((LineRow)l));
            NewProductCommand = new RelayCommand(CreateProduct);
            SaveCommand = new RelayCommand(() => { if (Save()) _main.ShowRequests(_saved); });
            SaveAndOpenCommand = new RelayCommand(SaveAndOpen);
            BackCommand = new RelayCommand(() => _main.ShowRequests(_existing));
            RefreshRequisitesCommand = new RelayCommand(() =>
            {
                _refreshRequisites = true;
                _isDirty = true;
                OnPropertyChanged(nameof(RequisitesNote));
            });

            foreach (var supplier in _data.Suppliers.Where(s => s.IsActive || s.Id == existing?.SupplierId).OrderBy(s => s.ShortName))
                Suppliers.Add(supplier);

            if (existing != null)
                LoadExisting(existing);
            else
                LoadNew(copyFrom);

            _isDirty = false;
            RebuildCatalog();
        }

        public string Title => _existing == null ? "Новая заявка" : "Заявка №" + _existing.Number;
        public bool HasSuppliers => Suppliers.Count > 0;

        public Supplier SelectedSupplier
        {
            get => _supplier;
            set
            {
                if (!Set(ref _supplier, value))
                    return;
                _isDirty = true;
                RebuildContracts();
                SelectedContract = RequestLogic.PickContract(_supplier, _deliveryDate ?? DateTime.Today);
                RebuildCatalog();
                OnPropertyChanged(nameof(SupplierHasError));
                OnPropertyChanged(nameof(NoContractsNote));
            }
        }

        public Contract SelectedContract
        {
            get => _contract;
            set
            {
                if (!Set(ref _contract, value))
                    return;
                _isDirty = true;
                if (_numberIsAutomatic && _contract != null)
                    SetNumberAutomatically();
                OnPropertyChanged(nameof(ContractWarning));
                OnPropertyChanged(nameof(ContractHasError));
                OnPropertyChanged(nameof(NumberHasError));
            }
        }

        public string NumberText
        {
            get => _numberText;
            set
            {
                if (!Set(ref _numberText, value))
                    return;
                _numberIsAutomatic = false;
                _isDirty = true;
                OnPropertyChanged(nameof(NumberHasError));
            }
        }

        public DateTime? DeliveryDate
        {
            get => _deliveryDate;
            set
            {
                if (!Set(ref _deliveryDate, value))
                    return;
                _isDirty = true;
                // Для новой заявки контракт подбирается сам под дату поставки.
                if (_existing == null && _deliveryDate.HasValue && _supplier != null && _contract != null && !_contract.Covers(_deliveryDate.Value))
                    SelectedContract = RequestLogic.PickContract(_supplier, _deliveryDate.Value);
                OnPropertyChanged(nameof(ContractWarning));
                OnPropertyChanged(nameof(DateWarning));
                OnPropertyChanged(nameof(DeliveryDateHasError));
            }
        }

        public DateTime? SentDate
        {
            get => _sentDate;
            set
            {
                if (!Set(ref _sentDate, value))
                    return;
                _isDirty = true;
                OnPropertyChanged(nameof(DateWarning));
                OnPropertyChanged(nameof(SentDateHasError));
            }
        }

        public string DeliveryTime
        {
            get => _deliveryTime;
            set
            {
                if (Set(ref _deliveryTime, value))
                    _isDirty = true;
            }
        }

        public string CatalogSearch
        {
            get => _catalogSearch;
            set
            {
                if (Set(ref _catalogSearch, value))
                    RebuildCatalog();
            }
        }

        public bool ShowAllProducts
        {
            get => _showAllProducts;
            set
            {
                if (Set(ref _showAllProducts, value))
                    RebuildCatalog();
            }
        }

        public bool HasCatalogItems => CatalogItems.Count > 0;
        public bool HasLines => Lines.Count > 0;

        public string TotalText => RuFormat.Money(Lines.Sum(l => l.Cost));

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => Set(ref _errorMessage, value);
        }

        public bool SupplierHasError => _showErrors && _supplier == null;
        public bool ContractHasError => _showErrors && _contract == null;
        public bool NumberHasError => _showErrors && (!TryGetNumber(out var number) || IsNumberTaken(number));
        public bool DeliveryDateHasError => _showErrors && !_deliveryDate.HasValue;
        public bool SentDateHasError => _showErrors && !_sentDate.HasValue;

        public string NoContractsNote => Suppliers.Count == 0
            ? "Поставщиков пока нет. Добавьте поставщика и контракт на странице «Поставщики»."
            : _supplier != null && _supplier.Contracts.Count == 0
                ? "У этого поставщика ещё нет контрактов. Добавьте контракт на странице «Поставщики»."
                : null;

        public string ContractWarning => _contract != null && _deliveryDate.HasValue && !_contract.Covers(_deliveryDate.Value)
            ? $"Этот контракт действует {_contract.PeriodText}, а дата поставки {RuFormat.ShortDate(_deliveryDate.Value)} в этот срок не попадает. Если контракт уже новый, добавьте его на странице «Поставщики»."
            : null;

        public string DateWarning => _sentDate.HasValue && _deliveryDate.HasValue && _deliveryDate < _sentDate
            ? "Дата поставки раньше даты направления заявки. Проверьте, всё ли верно."
            : null;

        public string RequisitesNote => _existing != null && !_refreshRequisites && RequestLogic.DiffersFromCatalog(_existing, _data)
            ? "Реквизиты в настройках или у поставщика изменились после оформления этой заявки. В документе пока остаются прежние."
            : null;

        public bool CanLeave()
        {
            if (!_isDirty)
                return true;
            var answer = Ask.SaveOrDiscard("Сохранить заявку?", "Вы внесли изменения, но ещё не сохранили их.");
            switch (answer)
            {
                case AskResult.Primary:
                    return Save();
                case AskResult.Secondary:
                    return true;
                default:
                    return false;
            }
        }

        private SupplyRequest _saved;

        /// <summary>Проверяет данные и сохраняет заявку. Первая найденная ошибка показывается словами.</summary>
        public bool Save()
        {
            _showErrors = true;
            foreach (var line in Lines)
                line.ShowErrors = true;
            NotifyErrorFlags();

            var error = Validate();
            ErrorMessage = error;
            if (error != null)
                return false;

            var request = _existing ?? new SupplyRequest { CreatedAtUtc = DateTime.UtcNow, CreatedBy = _data.Organization.EmployeeName };
            var previousDocument = _existing == null ? null : _main.Documents.GetPath(_existing);

            TryGetNumber(out var number);
            request.Number = number;
            request.DeliveryDate = _deliveryDate.Value.Date;
            request.SentDate = _sentDate.Value.Date;
            request.DeliveryTime = (_deliveryTime ?? "").Trim();
            request.Lines = Lines.Select(l => l.ToLine()).ToList();
            request.UpdatedAtUtc = DateTime.UtcNow;

            var supplierChanged = request.SupplierId != _supplier.Id || request.ContractId != _contract.Id;
            if (_existing == null || _refreshRequisites)
                RequestLogic.CopyOrganization(request, _data.Organization);
            if (_existing == null || _refreshRequisites || supplierChanged)
                RequestLogic.CopySupplier(request, _supplier, _contract);

            if (_existing == null)
                _data.Requests.Add(request);
            try
            {
                _main.Store.Save();
            }
            catch (Exception)
            {
                // Новая заявка не записалась — убираем её, чтобы повторное нажатие не создало дубль.
                if (_existing == null)
                    _data.Requests.Remove(request);
                throw;
            }

            // Номер, дата или поставщик могли поменяться — старый файл заявки больше не нужен.
            if (previousDocument != null && previousDocument != _main.Documents.GetPath(request))
                TryDeleteOldDocument(previousDocument);

            _saved = request;
            _isDirty = false;
            return true;
        }

        private void TryDeleteOldDocument(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch (System.IO.IOException)
            {
                // Старый файл открыт в Word — это не должно мешать сохранению заявки.
            }
        }

        private string Validate()
        {
            var org = _data.Organization;
            if (_existing == null && (string.IsNullOrWhiteSpace(org.Name) || string.IsNullOrWhiteSpace(org.SignerName)))
                return "Сначала заполните страницу «Настройки»: название детского сада и подпись.";
            if (_supplier == null)
                return "Выберите поставщика.";
            if (_contract == null)
                return "Выберите контракт. Если его нет в списке, добавьте на странице «Поставщики».";
            if (!TryGetNumber(out var number))
                return "Укажите номер заявки — целое число, например 8.";
            if (IsNumberTaken(number))
                return $"Заявка №{number} к этому контракту уже есть. Укажите другой номер.";
            if (!_deliveryDate.HasValue)
                return "Укажите дату поставки.";
            if (!_sentDate.HasValue)
                return "Укажите дату направления заявки.";
            if (Lines.Count == 0)
                return "Добавьте в заявку хотя бы один продукт: нажмите на него в списке справа.";
            var badQuantity = Lines.FirstOrDefault(l => !l.IsQuantityValid);
            if (badQuantity != null)
            {
                badQuantity.FocusQuantity = true;
                return $"Укажите количество для «{badQuantity.Name}» — число больше нуля, например 3,6.";
            }
            var badPrice = Lines.FirstOrDefault(l => !l.IsPriceValid);
            if (badPrice != null)
                return $"Проверьте цену для «{badPrice.Name}» — нужно число, например 65,00.";
            return null;
        }

        private void NotifyErrorFlags()
        {
            OnPropertyChanged(nameof(SupplierHasError));
            OnPropertyChanged(nameof(ContractHasError));
            OnPropertyChanged(nameof(NumberHasError));
            OnPropertyChanged(nameof(DeliveryDateHasError));
            OnPropertyChanged(nameof(SentDateHasError));
        }

        private void SaveAndOpen()
        {
            if (!Save())
                return;
            var saved = _saved;
            _main.ShowRequests(saved);
            DocumentActions.Run(_main.Documents, saved, path => _main.Documents.Open(path));
        }

        private bool TryGetNumber(out int number) => int.TryParse((_numberText ?? "").Trim().TrimStart('№'), out number) && number > 0;

        private bool IsNumberTaken(int number) =>
            _contract != null && RequestLogic.IsNumberTaken(_data, _contract.Id, number, _existing?.Id);

        private void SetNumberAutomatically()
        {
            _numberText = RequestLogic.NextNumber(_data, _contract.Id).ToString();
            OnPropertyChanged(nameof(NumberText));
        }

        private void LoadExisting(SupplyRequest request)
        {
            _supplier = _data.FindSupplier(request.SupplierId);
            RebuildContracts();
            _contract = _supplier?.FindContract(request.ContractId);
            _numberText = request.Number.ToString();
            _deliveryDate = request.DeliveryDate;
            _sentDate = request.SentDate;
            _deliveryTime = request.DeliveryTime;
            foreach (var line in request.Lines)
                AddRow(line);
        }

        private void LoadNew(SupplyRequest copyFrom)
        {
            _sentDate = DateTime.Today;
            _deliveryDate = RequestLogic.NextWorkday(DateTime.Today);
            _deliveryTime = _data.Organization.DefaultDeliveryTime;
            _numberIsAutomatic = true;

            _supplier = copyFrom != null ? _data.FindSupplier(copyFrom.SupplierId) : null;
            if (_supplier != null && !Suppliers.Contains(_supplier))
                Suppliers.Add(_supplier);
            if (_supplier == null && Suppliers.Count == 1)
                _supplier = Suppliers[0];
            RebuildContracts();
            _contract = RequestLogic.PickContract(_supplier, _deliveryDate.Value);
            if (_contract != null)
                SetNumberAutomatically();

            if (copyFrom == null)
                return;
            foreach (var line in copyFrom.Lines)
            {
                // При повторе заявки цены и описания берём свежие из каталога.
                var product = _data.Products.FirstOrDefault(p => p.Id == line.ProductId);
                var row = product != null ? RequestLogic.CreateLine(product, line.Quantity) : line;
                AddRow(row);
            }
        }

        private void RebuildContracts()
        {
            Contracts.Clear();
            if (_supplier == null)
                return;
            foreach (var contract in _supplier.Contracts.OrderByDescending(c => c.ValidTo))
                Contracts.Add(contract);
        }

        private void RebuildCatalog()
        {
            CatalogItems.Clear();
            IEnumerable<Product> products = _data.Products;
            if (!_showAllProducts && _supplier != null)
                products = products.Where(p => p.SupplierId == null || p.SupplierId == _supplier.Id);
            var words = (_catalogSearch ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var row in products.Select(p => new ProductRow(p, _data)).Where(r => r.Matches(words)).OrderBy(r => r.Name).ThenBy(r => r.Subtitle))
                CatalogItems.Add(row);
            OnPropertyChanged(nameof(HasCatalogItems));
        }

        private void AddProduct(Product product)
        {
            // Повторный щелчок не дублирует строку, а ставит курсор на количество.
            var existing = Lines.FirstOrDefault(l => l.ProductId == product.Id);
            if (existing != null)
            {
                existing.FocusQuantity = true;
                return;
            }
            var row = AddRow(RequestLogic.CreateLine(product, 0));
            row.FocusQuantity = true;
            _isDirty = true;
            ErrorMessage = null;
        }

        private LineRow AddRow(RequestLine line)
        {
            var row = new LineRow(line, OnAmountsChanged) { ShowErrors = _showErrors };
            Lines.Add(row);
            Renumber();
            return row;
        }

        private void RemoveLine(LineRow row)
        {
            Lines.Remove(row);
            Renumber();
            _isDirty = true;
        }

        private void Renumber()
        {
            var position = 1;
            foreach (var line in Lines)
                line.Position = position++;
            OnPropertyChanged(nameof(HasLines));
            OnPropertyChanged(nameof(TotalText));
        }

        private void OnAmountsChanged()
        {
            _isDirty = true;
            OnPropertyChanged(nameof(TotalText));
        }

        private void CreateProduct()
        {
            var form = new ProductForm(_data, Product.CreateNew(_supplier?.Id));
            var dialog = new ProductDialog { DataContext = form, Owner = System.Windows.Application.Current.MainWindow };
            if (dialog.ShowDialog() != true)
                return;
            var product = form.CreateProduct();
            _data.Products.Add(product);
            _main.Store.Save();
            RebuildCatalog();
            AddProduct(product);
        }
    }
}
