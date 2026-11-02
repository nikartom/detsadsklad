using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using DetsadSklad.Models;
using DetsadSklad.Services;
using DetsadSklad.Views;
using Microsoft.Win32;

namespace DetsadSklad.ViewModels
{
    /// <summary>Элемент выпадающего фильтра: подпись и значение.</summary>
    public class FilterOption<T>
    {
        public FilterOption(string title, T value)
        {
            Title = title;
            Value = value;
        }

        public string Title { get; }
        public T Value { get; }
        public override string ToString() => Title;
    }

    public enum PeriodKind { All, ThisMonth, LastMonth, ThisQuarter, ThisYear }

    /// <summary>Заявка в списке.</summary>
    public class RequestRow
    {
        public RequestRow(SupplyRequest request)
        {
            Request = request;
        }

        public SupplyRequest Request { get; }
        public string NumberText => "№" + Request.Number;
        public string TitleText => "Заявка №" + Request.Number;
        public string Supplier => Request.SupplierShortName;
        public string ContractText => $"Контракт № {Request.ContractNumber} от {RuFormat.ShortDate(Request.ContractDate)}";
        public string SentText => RuFormat.ShortDate(Request.SentDate);
        public string DeliveryText => RuFormat.ShortDate(Request.DeliveryDate);
        public string TotalText => RuFormat.Money(Request.Total);
        public string CreatedByText => string.IsNullOrWhiteSpace(Request.CreatedBy) ? null : "Оформил(а): " + Request.CreatedBy;
        public string SignerText => "Подпись: " + Request.SignerName;
        public IEnumerable<LineSummary> Lines => Request.Lines.Select(l => new LineSummary(l));
    }

    public class LineSummary
    {
        public LineSummary(RequestLine line)
        {
            Name = line.Name;
            Details = $"{RuFormat.Quantity(line.Quantity)} {line.Unit} × {RuFormat.DocMoney(line.Price)} = {RuFormat.Money(line.Cost)}";
        }

        public string Name { get; }
        public string Details { get; }
    }

    public class RequestsViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private string _search = "";
        private FilterOption<Guid?> _supplierFilter;
        private FilterOption<Guid?> _contractFilter;
        private FilterOption<PeriodKind> _periodFilter;
        private RequestRow _selected;
        private string _summary;

        public ObservableCollection<RequestRow> Rows { get; } = new ObservableCollection<RequestRow>();
        public List<FilterOption<Guid?>> SupplierOptions { get; }
        public ObservableCollection<FilterOption<Guid?>> ContractOptions { get; } = new ObservableCollection<FilterOption<Guid?>>();
        public List<FilterOption<PeriodKind>> PeriodOptions { get; }

        public ICommand NewRequestCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand RepeatCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand PrintCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand ShowInFolderCommand { get; }
        public ICommand ClearFiltersCommand { get; }

        public RequestsViewModel(MainViewModel main, SupplyRequest select)
        {
            _main = main;
            var data = main.Store.Data;

            SupplierOptions = new List<FilterOption<Guid?>> { new FilterOption<Guid?>("Все поставщики", null) };
            SupplierOptions.AddRange(data.Suppliers.OrderBy(s => s.ShortName).Select(s => new FilterOption<Guid?>(s.ShortName, s.Id)));
            PeriodOptions = new List<FilterOption<PeriodKind>>
            {
                new FilterOption<PeriodKind>("За всё время", PeriodKind.All),
                new FilterOption<PeriodKind>("Этот месяц", PeriodKind.ThisMonth),
                new FilterOption<PeriodKind>("Прошлый месяц", PeriodKind.LastMonth),
                new FilterOption<PeriodKind>("Этот квартал", PeriodKind.ThisQuarter),
                new FilterOption<PeriodKind>("Этот год", PeriodKind.ThisYear)
            };
            _supplierFilter = SupplierOptions[0];
            _periodFilter = PeriodOptions[0];
            RebuildContractOptions();

            NewRequestCommand = new RelayCommand(() => _main.ShowEditor(RequestEditorViewModel.CreateNew(_main)));
            EditCommand = new RelayCommand(() => _main.ShowEditor(RequestEditorViewModel.Edit(_main, Selected.Request)), () => Selected != null);
            RepeatCommand = new RelayCommand(() => _main.ShowEditor(RequestEditorViewModel.CreateCopy(_main, Selected.Request)), () => Selected != null);
            DeleteCommand = new RelayCommand(Delete, () => Selected != null);
            OpenCommand = new RelayCommand(() => WithDocument(path => _main.Documents.Open(path)), () => Selected != null);
            PrintCommand = new RelayCommand(() => WithDocument(path => _main.Documents.Print(path)), () => Selected != null);
            SaveAsCommand = new RelayCommand(SaveAs, () => Selected != null);
            ShowInFolderCommand = new RelayCommand(() => WithDocument(path => _main.Documents.ShowInFolder(path)), () => Selected != null);
            ClearFiltersCommand = new RelayCommand(ClearFilters);

            Refresh(select);
        }

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) Refresh(); }
        }

        public FilterOption<Guid?> SupplierFilter
        {
            get => _supplierFilter;
            set
            {
                if (value == null || !Set(ref _supplierFilter, value))
                    return;
                RebuildContractOptions();
                Refresh();
            }
        }

        public FilterOption<Guid?> ContractFilter
        {
            get => _contractFilter;
            set { if (value != null && Set(ref _contractFilter, value)) Refresh(); }
        }

        public FilterOption<PeriodKind> PeriodFilter
        {
            get => _periodFilter;
            set { if (value != null && Set(ref _periodFilter, value)) Refresh(); }
        }

        public bool CanFilterByContract => _supplierFilter?.Value != null;

        public RequestRow Selected
        {
            get => _selected;
            set
            {
                if (Set(ref _selected, value))
                    OnPropertyChanged(nameof(HasSelection));
            }
        }

        public bool HasSelection => _selected != null;

        public string Summary
        {
            get => _summary;
            private set => Set(ref _summary, value);
        }

        public bool HasRows => Rows.Count > 0;
        public bool HasAnyRequests => _main.Store.Data.Requests.Count > 0;

        public string EmptyText => HasAnyRequests
            ? "По такому запросу заявок нет. Попробуйте изменить поиск или нажмите «Показать все»."
            : "Заявок пока нет. Нажмите «Новая заявка», чтобы оформить первую.";

        private void RebuildContractOptions()
        {
            ContractOptions.Clear();
            ContractOptions.Add(new FilterOption<Guid?>("Все контракты", null));
            var supplier = _supplierFilter?.Value == null ? null : _main.Store.Data.FindSupplier(_supplierFilter.Value.Value);
            if (supplier != null)
            {
                foreach (var contract in supplier.Contracts.OrderByDescending(c => c.ValidTo))
                    ContractOptions.Add(new FilterOption<Guid?>(contract.DisplayName, contract.Id));
            }
            _contractFilter = ContractOptions[0];
            OnPropertyChanged(nameof(ContractFilter));
            OnPropertyChanged(nameof(CanFilterByContract));
        }

        private void Refresh(SupplyRequest select = null)
        {
            var keep = select ?? Selected?.Request;
            var found = _main.Store.Data.Requests
                .Where(MatchesFilters)
                .OrderByDescending(r => r.SentDate)
                .ThenByDescending(r => r.Number)
                .ToList();

            Rows.Clear();
            foreach (var request in found)
                Rows.Add(new RequestRow(request));
            _selected = null;

            Selected = Rows.FirstOrDefault(r => r.Request == keep) ?? Rows.FirstOrDefault();
            Summary = found.Count == 0 ? "" : $"Найдено заявок: {found.Count}, на сумму {RuFormat.Money(found.Sum(r => r.Total))}";
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(EmptyText));
        }

        private bool MatchesFilters(SupplyRequest request)
        {
            if (_supplierFilter?.Value != null && request.SupplierId != _supplierFilter.Value)
                return false;
            if (_contractFilter?.Value != null && request.ContractId != _contractFilter.Value)
                return false;
            return MatchesPeriod(request.SentDate) && RequestLogic.MatchesSearch(request, _search);
        }

        private bool MatchesPeriod(DateTime date)
        {
            var today = DateTime.Today;
            switch (_periodFilter?.Value)
            {
                case PeriodKind.ThisMonth:
                    return date.Year == today.Year && date.Month == today.Month;
                case PeriodKind.LastMonth:
                    var previous = today.AddMonths(-1);
                    return date.Year == previous.Year && date.Month == previous.Month;
                case PeriodKind.ThisQuarter:
                    RequestLogic.GetQuarter(today, out var from, out var to);
                    return date >= from && date <= to;
                case PeriodKind.ThisYear:
                    return date.Year == today.Year;
                default:
                    return true;
            }
        }

        private void ClearFilters()
        {
            _search = "";
            OnPropertyChanged(nameof(Search));
            _supplierFilter = SupplierOptions[0];
            OnPropertyChanged(nameof(SupplierFilter));
            _periodFilter = PeriodOptions[0];
            OnPropertyChanged(nameof(PeriodFilter));
            RebuildContractOptions();
            Refresh();
        }

        private void Delete()
        {
            var request = Selected.Request;
            var confirmed = Ask.Confirm(
                $"Удалить заявку №{request.Number}?",
                $"Заявка поставщику {request.SupplierShortName} и её файл будут удалены. Вернуть их будет нельзя.",
                "Удалить", isDangerous: true);
            if (!confirmed)
                return;

            _main.Documents.DeleteDocument(request);
            _main.Store.Data.Requests.Remove(request);
            _main.Store.Save();
            Selected = null;
            Refresh();
            OnPropertyChanged(nameof(HasAnyRequests));
        }

        private void SaveAs()
        {
            var request = Selected.Request;
            var dialog = new SaveFileDialog
            {
                Title = "Куда сохранить заявку",
                FileName = DocumentService.GetFileName(request),
                Filter = "Документ Word|*.docx",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };
            if (dialog.ShowDialog() != true)
                return;
            WithDocument(path =>
            {
                File.Copy(path, dialog.FileName, overwrite: true);
                Ask.Info("Файл сохранён", "Заявка сохранена в файл:\n" + dialog.FileName);
            });
        }

        private void WithDocument(Action<string> action)
        {
            DocumentActions.Run(_main.Documents, Selected.Request, action);
        }
    }

    /// <summary>Создание файла Word с понятными сообщениями, если что-то помешало.</summary>
    public static class DocumentActions
    {
        public static void Run(DocumentService documents, SupplyRequest request, Action<string> action)
        {
            string path;
            try
            {
                path = documents.EnsureDocument(request);
            }
            catch (IOException)
            {
                Ask.Info("Файл открыт в Word",
                    "Заявка изменилась, но её файл сейчас открыт в Word, поэтому обновить его нельзя.\n\nЗакройте этот документ в Word и нажмите кнопку ещё раз.");
                return;
            }

            try
            {
                action(path);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Ask.Info("Не удалось открыть Word",
                    "На этом компьютере не нашлась программа для документов Word (.docx).\n\nФайл заявки готов, он лежит здесь:\n" + path);
            }
        }
    }
}
