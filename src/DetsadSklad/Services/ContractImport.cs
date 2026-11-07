using System;
using System.Linq;
using System.Text.RegularExpressions;
using DetsadSklad.Models;

namespace DetsadSklad.Services
{
    /// <summary>
    /// Переносит прочитанный контракт в справочники: поставщика (нового или уже известного),
    /// контракт и продукты. Продукты, которые уже есть у поставщика, не дублируются — у них обновляется цена.
    /// </summary>
    public static class ContractImport
    {
        /// <summary>Тот же поставщик: по ИНН, а если ИНН ещё не записан — по названию.</summary>
        public static Supplier FindSupplier(AppData data, ParsedContract contract)
        {
            if (!string.IsNullOrWhiteSpace(contract.SupplierInn))
            {
                var byInn = data.Suppliers.FirstOrDefault(s => s.Inn == contract.SupplierInn);
                if (byInn != null)
                    return byInn;
            }
            var shortName = NormalizeName(contract.SupplierShortName);
            var fullName = NormalizeName(contract.SupplierFullName);
            return data.Suppliers.FirstOrDefault(s => string.IsNullOrWhiteSpace(s.Inn) || string.IsNullOrWhiteSpace(contract.SupplierInn)
                ? (shortName.Length > 0 && NormalizeName(s.ShortName) == shortName) || (fullName.Length > 0 && NormalizeName(s.FullName) == fullName)
                : false);
        }

        /// <summary>Продукт этого поставщика с тем же описанием, что в контракте.</summary>
        public static Product FindProduct(AppData data, Supplier supplier, ParsedItem item)
        {
            if (supplier == null)
                return null;
            var text = NormalizeText(item.Description);
            return data.Products.FirstOrDefault(p => p.SupplierId == supplier.Id && NormalizeText(p.DocumentName) == text);
        }

        /// <summary>Что мешает сохранить: null — можно сохранять.</summary>
        public static string FindProblem(Supplier existing, ParsedContract contract)
        {
            if (string.IsNullOrWhiteSpace(contract.SupplierShortName) || string.IsNullOrWhiteSpace(contract.SupplierFullName))
                return "Напишите название поставщика.";
            if (string.IsNullOrWhiteSpace(contract.Number))
                return "Укажите номер контракта.";
            if (!contract.SignedOn.HasValue)
                return "Укажите, когда заключён контракт.";
            if (!contract.ValidTo.HasValue)
                return "Укажите, по какое число действует контракт.";
            if (contract.ValidTo < (contract.ValidFrom ?? contract.SignedOn))
                return "Дата окончания контракта раньше даты начала.";
            if (existing != null && existing.Contracts.Any(c => RequestLogic.SameText(c.Number, contract.Number)))
                return $"Контракт № {contract.Number.Trim()} у этого поставщика уже есть — этот файл уже загружали.";
            return null;
        }

        /// <summary>Записывает поставщика, контракт и продукты. Возвращает поставщика.</summary>
        public static Supplier Apply(AppData data, ParsedContract contract, Supplier existing)
        {
            var supplier = existing;
            if (supplier == null)
            {
                supplier = new Supplier
                {
                    ShortName = contract.SupplierShortName.Trim(),
                    FullName = contract.SupplierFullName.Trim(),
                    Address = (contract.SupplierAddress ?? "").Trim(),
                    Email = (contract.SupplierEmail ?? "").Trim(),
                    Phone = (contract.SupplierPhone ?? "").Trim()
                };
                data.Suppliers.Add(supplier);
            }
            else
            {
                // Чего у известного поставщика не было — дописываем, остальное оставляем как есть.
                if (string.IsNullOrWhiteSpace(supplier.Address)) supplier.Address = contract.SupplierAddress;
                if (string.IsNullOrWhiteSpace(supplier.Email)) supplier.Email = contract.SupplierEmail;
                if (string.IsNullOrWhiteSpace(supplier.Phone)) supplier.Phone = contract.SupplierPhone;
                supplier.IsActive = true;
            }
            if (string.IsNullOrWhiteSpace(supplier.Inn))
                supplier.Inn = contract.SupplierInn;

            var signedOn = contract.SignedOn.Value.Date;
            supplier.Contracts.Add(new Contract
            {
                Number = contract.Number.Trim(),
                Note = (contract.Note ?? "").Trim(),
                SignedOn = signedOn,
                ValidFrom = (contract.ValidFrom ?? signedOn).Date,
                ValidTo = contract.ValidTo.Value.Date
            });

            var category = string.IsNullOrWhiteSpace(contract.Note) ? null : contract.Note.Trim();
            foreach (var item in contract.Items.Where(i => i.Description != null))
            {
                var product = FindProduct(data, supplier, item);
                if (product == null)
                {
                    data.Products.Add(CreateProduct(item, supplier.Id, category));
                    continue;
                }
                product.Price = item.Price;
                if (!string.IsNullOrWhiteSpace(item.Unit))
                    product.Unit = item.Unit;
                if (string.IsNullOrWhiteSpace(product.Category))
                    product.Category = category;
            }
            return supplier;
        }

        /// <summary>
        /// Продукт из строки спецификации. Описание переносится дословно: первая фраза — наименование,
        /// остальное — «Особенности и состав». В документе заявки получится тот же текст, что в контракте.
        /// </summary>
        public static Product CreateProduct(ParsedItem item, Guid supplierId, string category)
        {
            var text = item.Description.Trim();
            var split = Regex.Match(text, @"^(?<name>.+?)\.\s+(?<rest>\S.*)$", RegexOptions.Singleline);
            var name = split.Success ? split.Groups["name"].Value : text;
            var rest = split.Success ? split.Groups["rest"].Value : "";
            return new Product
            {
                Name = name.Trim().TrimEnd('.'),
                Features = rest.Trim().TrimEnd('.'),
                Category = category,
                Unit = (item.Unit ?? "").Trim(),
                Price = item.Price,
                SupplierId = supplierId
            };
        }

        private static string NormalizeName(string name)
        {
            var text = Regex.Replace((name ?? "").ToLowerInvariant(), @"[«»""'„“”]", "");
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string NormalizeText(string text) =>
            Regex.Replace((text ?? "").Trim().TrimEnd('.'), @"\s+", " ").ToLowerInvariant();
    }
}
