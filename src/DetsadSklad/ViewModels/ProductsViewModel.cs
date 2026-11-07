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
    /// <summary>Продукт в списке: наименование, под ним поставщик и цена.</summary>
    public class ProductRow
    {
        public ProductRow(Product product, AppData data)
        {
            Product = product;
            var supplier = product.SupplierId.HasValue ? data.FindSupplier(product.SupplierId.Value) : null;
            var category = string.IsNullOrWhiteSpace(product.Category) ? "" : product.Category.Trim() + " · ";
            Subtitle = $"{supplier?.ShortName ?? "Любой поставщик"} · {category}{product.PriceText}";
            SupplierName = supplier?.ShortName ?? "Любой";
            CategoryText = string.IsNullOrWhiteSpace(product.Category) ? "—" : product.Category.Trim();
            PriceText = RuFormat.DocMoney(product.Price);
        }

        public Product Product { get; }
        public string Name => Product.Name;
        public string Subtitle { get; }

        // Колонки таблицы в списке продуктов.
        public string SupplierName { get; }
        public string CategoryText { get; }
        public string PriceText { get; }
        public string Unit => Product.Unit;
        public string Details => Product.DocumentName;

        public bool Matches(string[] words) => words.All(word =>
            Details.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0
            || Subtitle.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>Поля товара: используется и на странице каталога, и в окне быстрого добавления из заявки.</summary>
    public class ProductForm : ObservableObject
    {
        private readonly AppData _data;
        private readonly Guid _productId;
        private string _name;
        private string _kind;
        private string _features;
        private bool _suitableForChildren;
        private bool _individualPackaging;
        private string _unit;
        private string _priceText;
        private FilterOption<Guid?> _supplier;
        private string _error;

        public static readonly string[] CommonUnits = { "кг", "шт", "л", "Литр; Кубический дециметр", "г", "упак", "банка", "бутылка" };

        public List<FilterOption<Guid?>> SupplierOptions { get; }
        public IEnumerable<string> Units => CommonUnits;
        public Action Changed { get; set; }

        public ProductForm(AppData data, Product product)
        {
            _data = data;
            _productId = product.Id;
            SupplierOptions = new List<FilterOption<Guid?>> { new FilterOption<Guid?>("Любой поставщик", null) };
            SupplierOptions.AddRange(data.Suppliers.OrderBy(s => s.ShortName).Select(s => new FilterOption<Guid?>(s.ShortName, s.Id)));
            _name = product.Name;
            _kind = product.Kind;
            _features = product.Features;
            _suitableForChildren = product.SuitableForChildren;
            _individualPackaging = product.IndividualPackaging;
            CategoryChoice = new ChoiceList(ProductChoices.Categories(data), product.Category, "Без категории", "Новая категория", "Например: Бакалея", NotifyChanged);
            GradeChoice = new ChoiceList(ProductChoices.Grades(data), product.Grade, "Не указан", "Новый сорт", "Например: Высший", NotifyChanged);
            PackagingChoice = new ChoiceList(ProductChoices.Packaging(data), product.Packaging, "Не указана", "Новая упаковка", "Например: массой до 1 кг", NotifyChanged);
            CountryChoice = new ChoiceList(ProductChoices.Countries(data), product.Country, "Не указана", "Новая страна", "Например: Республика Казахстан", NotifyChanged);
            _unit = product.Unit;
            _priceText = product.Price > 0 ? RuFormat.DocMoney(product.Price) : "";
            _supplier = SupplierOptions.FirstOrDefault(o => o.Value == product.SupplierId) ?? SupplierOptions[0];
        }

        public string Name { get => _name; set => Edit(ref _name, value); }
        public string Kind { get => _kind; set => Edit(ref _kind, value); }
        public string Features { get => _features; set => Edit(ref _features, value); }
        public bool SuitableForChildren { get => _suitableForChildren; set => Edit(ref _suitableForChildren, value); }
        public bool IndividualPackaging { get => _individualPackaging; set => Edit(ref _individualPackaging, value); }
        public ChoiceList CategoryChoice { get; }
        public ChoiceList GradeChoice { get; }
        public ChoiceList PackagingChoice { get; }
        public ChoiceList CountryChoice { get; }
        public string Unit { get => _unit; set => Edit(ref _unit, value); }
        public string PriceText { get => _priceText; set => Edit(ref _priceText, value); }

        public FilterOption<Guid?> Supplier
        {
            get => _supplier;
            // null приходит от самого списка, когда форму меняют на другую.
            set { if (value != null) Edit(ref _supplier, value); }
        }

        public string DocumentPreview => string.IsNullOrWhiteSpace(_name) ? "Заполните наименование." : CreateProduct().DocumentName;

        public string Error
        {
            get => _error;
            private set => Set(ref _error, value);
        }

        public bool Validate()
        {
            Error = FindError();
            return Error == null;
        }

        public void ApplyTo(Product product)
        {
            RuFormat.TryParseDecimal(_priceText, out var price);
            product.Name = (_name ?? "").Trim();
            product.Category = CategoryChoice.Value;
            product.Kind = (_kind ?? "").Trim();
            product.Grade = GradeChoice.Value;
            product.Features = (_features ?? "").Trim();
            product.SuitableForChildren = _suitableForChildren;
            product.IndividualPackaging = _individualPackaging;
            product.Packaging = PackagingChoice.Value;
            product.Country = CountryChoice.Value;
            product.Unit = (_unit ?? "").Trim();
            product.Price = price;
            product.SupplierId = _supplier?.Value;
        }

        public Product CreateProduct()
        {
            var product = new Product { Id = _productId };
            ApplyTo(product);
            return product;
        }

        private string FindError()
        {
            if (string.IsNullOrWhiteSpace(_name))
                return "Напишите наименование, например «Масло сливочное ГОСТ, МДЖ не менее 72,5 %».";
            if (string.IsNullOrWhiteSpace(_unit))
                return "Укажите единицу измерения, например «кг».";
            if (!RuFormat.TryParseDecimal(_priceText, out var price) || price < 0)
                return "Укажите цену числом, например 65,00.";
            var documentName = CreateProduct().DocumentName;
            var isDuplicate = _data.Products.Any(p => p.Id != _productId && p.SupplierId == _supplier?.Value
                && string.Equals(p.DocumentName, documentName, StringComparison.OrdinalIgnoreCase));
            if (isDuplicate)
                return "Такой продукт у этого поставщика уже есть. Выберите другого поставщика или измените описание.";
            return null;
        }

        private void Edit<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        {
            if (Set(ref field, value, name))
                NotifyChanged();
        }

        private void NotifyChanged()
        {
            OnPropertyChanged(nameof(DocumentPreview));
            Changed?.Invoke();
        }
    }

    public class ProductsViewModel : ObservableObject, ILeaveGuard
    {
        private readonly MainViewModel _main;
        private readonly AppData _data;
        private string _search = "";
        private FilterOption<Guid?> _supplierFilter;
        private FilterOption<string> _categoryFilter;
        private Product _selected;
        private ProductForm _form;
        private bool _isNew;
        private bool _isDirty;
        private string _savedNote;

        public ObservableCollection<ProductRow> Rows { get; } = new ObservableCollection<ProductRow>();
        public List<FilterOption<Guid?>> SupplierOptions { get; }
        public List<FilterOption<string>> CategoryOptions { get; }

        public ICommand AddCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand DeleteCommand { get; }

        public ProductsViewModel(MainViewModel main)
        {
            _main = main;
            _data = main.Store.Data;

            SupplierOptions = new List<FilterOption<Guid?>> { new FilterOption<Guid?>("Все поставщики", null) };
            SupplierOptions.AddRange(_data.Suppliers.OrderBy(s => s.ShortName).Select(s => new FilterOption<Guid?>(s.ShortName, s.Id)));
            _supplierFilter = SupplierOptions[0];

            // null — все категории, пустая строка — продукты без категории.
            CategoryOptions = new List<FilterOption<string>> { new FilterOption<string>("Все категории", null) };
            CategoryOptions.AddRange(ProductChoices.UsedCategories(_data).Select(c => new FilterOption<string>(c, c)));
            if (_data.Products.Any(p => string.IsNullOrWhiteSpace(p.Category)))
                CategoryOptions.Add(new FilterOption<string>("Без категории", ""));
            _categoryFilter = CategoryOptions[0];

            AddCommand = new RelayCommand(AddNew);
            OpenCommand = new RelayCommand(row => Open(((ProductRow)row).Product));
            SaveCommand = new RelayCommand(() => { if (Save()) BackToList(); });
            CancelCommand = new RelayCommand(() => { if (CanLeave()) BackToList(); });
            CopyCommand = new RelayCommand(CopyForAnotherSupplier, () => _selected != null);
            DeleteCommand = new RelayCommand(Delete, () => _selected != null);

            // Сначала — только список. Продукт открывается на его месте, когда на него нажали.
            Refresh();
        }

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) Refresh(); }
        }

        public FilterOption<Guid?> SupplierFilter
        {
            get => _supplierFilter;
            set { if (value != null && Set(ref _supplierFilter, value)) Refresh(); }
        }

        public FilterOption<string> CategoryFilter
        {
            get => _categoryFilter;
            set { if (value != null && Set(ref _categoryFilter, value)) Refresh(); }
        }

        public ProductForm Form
        {
            get => _form;
            private set
            {
                if (!Set(ref _form, value))
                    return;
                OnPropertyChanged(nameof(HasForm));
                OnPropertyChanged(nameof(IsListVisible));
            }
        }

        public bool HasForm => _form != null;
        public bool IsListVisible => _form == null;
        public string FormTitle => _isNew ? "Новый продукт" : _selected?.Name;
        public bool IsExisting => !_isNew;

        public string SavedNote
        {
            get => _savedNote;
            private set => Set(ref _savedNote, value);
        }

        public string CountText => $"Всего продуктов: {_data.Products.Count}";
        public bool HasRows => Rows.Count > 0;

        public bool CanLeave()
        {
            if (!_isDirty)
                return true;
            switch (Ask.SaveOrDiscard("Сохранить продукт?", "Вы внесли изменения, но ещё не сохранили их."))
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

        /// <summary>Открывает продукт на месте списка.</summary>
        public void Open(Product product)
        {
            _selected = product;
            LoadForm(product, isNew: false);
        }

        private void BackToList()
        {
            _selected = null;
            LoadForm(null, isNew: false);
            Refresh();
        }

        private void AddNew()
        {
            if (!CanLeave())
                return;
            _selected = null;
            LoadForm(Product.CreateNew(_supplierFilter?.Value, string.IsNullOrEmpty(_categoryFilter?.Value) ? null : _categoryFilter.Value), isNew: true);
        }

        // Та же номенклатура у другого поставщика: всё заполнено, остаётся выбрать поставщика и цену.
        private void CopyForAnotherSupplier()
        {
            if (!CanLeave())
                return;
            var copy = _selected.Copy();
            copy.Price = 0;
            _selected = null;
            LoadForm(copy, isNew: true);
        }

        private void LoadForm(Product product, bool isNew)
        {
            _isNew = isNew;
            _isDirty = false;
            SavedNote = null;
            if (_form != null)
                _form.Changed = null;
            if (product == null)
            {
                Form = null;
            }
            else
            {
                var form = new ProductForm(_data, product);
                form.Changed = () =>
                {
                    _isDirty = true;
                    SavedNote = null;
                };
                Form = form;
            }
            OnPropertyChanged(nameof(FormTitle));
            OnPropertyChanged(nameof(IsExisting));
            // «Удалить» и «Копировать» должны сразу стать доступными, не дожидаясь движения мыши.
            CommandManager.InvalidateRequerySuggested();
        }

        private bool Save()
        {
            if (_form == null || !_form.Validate())
                return false;

            var product = _selected;
            if (_isNew || product == null)
            {
                product = _form.CreateProduct();
                _data.Products.Add(product);
            }
            else
            {
                _form.ApplyTo(product);
            }
            _main.Store.Save();

            _isDirty = false;
            _isNew = false;
            _selected = product;
            OnPropertyChanged(nameof(CountText));
            return true;
        }

        private void Delete()
        {
            var confirmed = Ask.Confirm(
                $"Удалить «{_selected.Name}» из каталога?",
                "Уже оформленные заявки останутся без изменений, но в новых заявках этого продукта не будет.",
                "Удалить", isDangerous: true);
            if (!confirmed)
                return;
            _data.Products.Remove(_selected);
            _main.Store.Save();
            _isDirty = false;
            OnPropertyChanged(nameof(CountText));
            BackToList();
        }

        private void Refresh()
        {
            var words = (_search ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var found = _data.Products
                .Where(p => _supplierFilter?.Value == null || p.SupplierId == _supplierFilter.Value)
                .Where(MatchesCategory)
                .Select(p => new ProductRow(p, _data))
                .Where(r => r.Matches(words))
                .OrderBy(r => r.Name)
                .ThenBy(r => r.Subtitle)
                .ToList();
            Rows.Clear();
            foreach (var row in found)
                Rows.Add(row);
            OnPropertyChanged(nameof(HasRows));
        }

        private bool MatchesCategory(Product product)
        {
            var wanted = _categoryFilter?.Value;
            if (wanted == null)
                return true;
            var category = (product.Category ?? "").Trim();
            return string.Equals(category, wanted, StringComparison.OrdinalIgnoreCase);
        }
    }
}
