using System;
using System.Collections.Generic;
using System.Linq;
using DetsadSklad.Models;
using DetsadSklad.Services;

namespace DetsadSklad.ViewModels
{
    /// <summary>Строка спецификации на экране загрузки: что будет с продуктом.</summary>
    public class ImportItemRow
    {
        public ImportItemRow(ParsedItem item, Product existing)
        {
            Position = item.Position;
            Description = item.Description ?? "(не удалось прочитать наименование)";
            Unit = item.Unit;
            PriceText = RuFormat.DocMoney(item.Price);
            IsProblem = !item.IsConsistent;
            if (IsProblem)
                StatusText = "проверьте";
            else if (existing == null)
                StatusText = "новый";
            else if (existing.Price != item.Price)
                StatusText = $"была {RuFormat.DocMoney(existing.Price)}";
            else
                StatusText = "уже есть";
        }

        public int Position { get; }
        public string Description { get; }
        public string Unit { get; }
        public string PriceText { get; }
        public string StatusText { get; }
        public bool IsProblem { get; }
    }

    /// <summary>
    /// Проверка прочитанного контракта перед сохранением. Всё, что нашлось в файле, уже заполнено;
    /// пользователь может поправить и нажать «Сохранить».
    /// </summary>
    public class ContractImportViewModel : ObservableObject
    {
        private readonly DataStore _store;
        private readonly ParsedContract _contract;
        private readonly Supplier _existing;
        private string _error;

        public ContractImportViewModel(DataStore store, ParsedContract contract, string fileName)
        {
            _store = store;
            _contract = contract;
            FileName = fileName;
            _existing = ContractImport.FindSupplier(store.Data, contract);
            Items = contract.Items.Select(i => new ImportItemRow(i, ContractImport.FindProduct(store.Data, _existing, i))).ToList();
        }

        public string FileName { get; }

        /// <summary>«Контракт площадки КУЗБАСС TRADE · Бакалея.pdf».</summary>
        public string SourceText => string.IsNullOrEmpty(_contract.ProfileName)
            ? FileName
            : $"Контракт площадки {_contract.ProfileName} · {FileName}";
        public List<ImportItemRow> Items { get; }
        public bool IsNewSupplier => _existing == null;
        public bool IsKnownSupplier => _existing != null;

        public string KnownSupplierText => _existing == null ? null
            : $"Поставщик {_existing.ShortName} уже есть в программе — контракт добавится к нему.";

        public string ShortName { get => _contract.SupplierShortName; set => Edit(() => _contract.SupplierShortName = value); }
        public string FullName { get => _contract.SupplierFullName; set => Edit(() => _contract.SupplierFullName = value); }
        public string Address { get => _contract.SupplierAddress; set => Edit(() => _contract.SupplierAddress = value); }
        public string Email { get => _contract.SupplierEmail; set => Edit(() => _contract.SupplierEmail = value); }
        public string Phone { get => _contract.SupplierPhone; set => Edit(() => _contract.SupplierPhone = value); }

        public string Note { get => _contract.Note; set => Edit(() => _contract.Note = value); }
        public string Number { get => _contract.Number; set => Edit(() => _contract.Number = value); }
        public DateTime? SignedOn { get => _contract.SignedOn; set => Edit(() => _contract.SignedOn = value); }
        public DateTime? ValidTo { get => _contract.ValidTo; set => Edit(() => _contract.ValidTo = value); }

        public string PeriodText => _contract.ValidFrom.HasValue && _contract.ValidTo.HasValue
            ? $"Срок поставки по контракту: с {RuFormat.ShortDate(_contract.ValidFrom.Value)} по {RuFormat.ShortDate(_contract.ValidTo.Value)}"
            : null;

        public string ItemsTitle => $"Продукты по контракту: {Items.Count}";
        public int NewCount => Items.Count(i => i.StatusText == "новый");

        /// <summary>Совпала ли сумма позиций с ценой контракта — значит, ни одна строка не потерялась.</summary>
        public bool IsComplete => _contract.ItemsAreComplete;

        public string CheckText
        {
            get
            {
                if (Items.Count == 0)
                    return "В файле не нашлась таблица спецификации. Поставщика и контракт можно сохранить, а продукты добавить вручную.";
                if (_contract.ItemsAreComplete)
                    return $"Все позиции прочитаны: сумма {RuFormat.Money(_contract.ItemsTotal)} совпадает с ценой контракта.";
                var price = _contract.Price.HasValue ? RuFormat.Money(_contract.Price.Value) : "не найдена";
                return $"Сумма позиций ({RuFormat.Money(_contract.ItemsTotal)}) не совпадает с ценой контракта ({price}). "
                    + "Сверьте список с контрактом: возможно, какая-то позиция прочиталась неверно.";
            }
        }

        public string Error { get => _error; private set => Set(ref _error, value); }

        /// <summary>Сохраняет; false — что-то не заполнено, причина показана внизу экрана.</summary>
        public bool Save()
        {
            Error = ContractImport.FindProblem(_existing, _contract);
            if (Error != null)
                return false;
            ContractImport.Apply(_store.Data, _contract, _existing);
            _store.Save();
            return true;
        }

        private void Edit(Action apply)
        {
            apply();
            Error = null;
        }
    }
}
