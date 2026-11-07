using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DetsadSklad.Models;

namespace DetsadSklad.Services
{
    /// <summary>Что будет с позицией спецификации при сохранении.</summary>
    public enum ImportAction
    {
        /// <summary>Такого продукта у поставщика нет — добавится.</summary>
        Add,
        /// <summary>Продукт уже есть, и всё совпадает.</summary>
        Same,
        /// <summary>Продукт есть, изменится только цена.</summary>
        UpdatePrice,
        /// <summary>Продукт узнан по наименованию; описание станет как в контракте.</summary>
        UpdateDescription
    }

    public class ImportMatch
    {
        public ParsedItem Item { get; set; }
        /// <summary>Продукт в программе; null — будет добавлен.</summary>
        public Product Product { get; set; }
        public ImportAction Action { get; set; }
    }

    /// <summary>
    /// Переносит прочитанный контракт в справочники: поставщика (нового или уже известного),
    /// контракт (новый или заведённый раньше вручную) и продукты. Продукты, которые уже есть у поставщика,
    /// не дублируются — у них обновляются цена и описание.
    /// </summary>
    public static class ContractImport
    {
        private const int MinPrefixLength = 10;

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

        /// <summary>Контракт с тем же номером, заведённый раньше (вручную или прошлой загрузкой).</summary>
        public static Contract FindContract(Supplier supplier, ParsedContract contract) =>
            supplier?.Contracts.FirstOrDefault(c => RequestLogic.SameText(c.Number, contract.Number));

        /// <summary>
        /// Сопоставляет позиции спецификации с продуктами поставщика: сначала по полному описанию,
        /// потом по наименованию, потом по началу описания («Творог ГОСТ, МДЖ - 5%» → «Творог ГОСТ, МДЖ-5% из…»).
        /// Каждый продукт сопоставляется не больше одного раза; если подходят несколько — не угадываем.
        /// </summary>
        public static List<ImportMatch> MatchProducts(AppData data, Supplier supplier, ParsedContract contract)
        {
            var items = contract.Items.Where(i => i.Description != null).ToList();
            var matches = items.Select(i => new ImportMatch { Item = i, Action = ImportAction.Add }).ToList();
            if (supplier == null)
                return matches;
            var free = data.Products.Where(p => p.SupplierId == supplier.Id).ToList();

            void Pass(Func<ImportMatch, Product, bool> fits)
            {
                foreach (var match in matches.Where(m => m.Product == null))
                {
                    var candidates = free.Where(p => fits(match, p)).ToList();
                    if (candidates.Count != 1)
                        continue;
                    match.Product = candidates[0];
                    free.Remove(candidates[0]);
                }
            }

            Pass((m, p) => NormalizeText(p.DocumentName) == NormalizeText(m.Item.Description));
            Pass((m, p) => NormalizeText(p.Name).Length > 0 && NormalizeText(p.Name) == NormalizeText(NameOf(m.Item.Description)));
            Pass((m, p) =>
            {
                var name = Compact(p.Name);
                return name.Length >= MinPrefixLength && Compact(m.Item.Description).StartsWith(name, StringComparison.Ordinal);
            });

            foreach (var match in matches.Where(m => m.Product != null))
            {
                match.Action = NormalizeText(match.Product.DocumentName) != NormalizeText(match.Item.Description) ? ImportAction.UpdateDescription
                    : match.Product.Price != match.Item.Price || match.Product.Unit != match.Item.Unit ? ImportAction.UpdatePrice
                    : ImportAction.Same;
            }
            return matches;
        }

        /// <summary>
        /// Продукты поставщика той же группы, которых в этом контракте нет: например, позиция изменилась
        /// или её убрали. Сами они не удаляются — старый контракт может ещё действовать.
        /// </summary>
        public static List<Product> NotInContract(AppData data, Supplier supplier, ParsedContract contract, List<ImportMatch> matches)
        {
            if (supplier == null)
                return new List<Product>();
            var matched = new HashSet<Product>(matches.Where(m => m.Product != null).Select(m => m.Product));
            var categories = matched.Select(p => p.Category).Concat(new[] { contract.Note })
                .Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            return data.Products
                .Where(p => p.SupplierId == supplier.Id && !matched.Contains(p) && categories.Any(c => RequestLogic.SameText(c, p.Category)))
                .OrderBy(p => p.Name)
                .ToList();
        }

        /// <summary>Что мешает сохранить: null — можно сохранять.</summary>
        public static string FindProblem(ParsedContract contract)
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

            // Сопоставляем до того, как что-то поменяли: так же, как показано на экране.
            var matches = MatchProducts(data, existing, contract);

            var signedOn = contract.SignedOn.Value.Date;
            var saved = FindContract(supplier, contract);
            if (saved == null)
            {
                saved = new Contract { Number = contract.Number.Trim(), Note = (contract.Note ?? "").Trim() };
                supplier.Contracts.Add(saved);
            }
            else if (string.IsNullOrWhiteSpace(saved.Note))
            {
                saved.Note = (contract.Note ?? "").Trim();
            }
            // Даты берём из файла: контракт — главный источник.
            saved.SignedOn = signedOn;
            saved.ValidFrom = (contract.ValidFrom ?? signedOn).Date;
            saved.ValidTo = contract.ValidTo.Value.Date;

            // Категория продуктов — «что поставляет» этого контракта; у заведённого вручную оно остаётся прежним.
            var category = string.IsNullOrWhiteSpace(saved.Note) ? null : saved.Note.Trim();
            foreach (var match in matches)
            {
                if (match.Product == null)
                {
                    data.Products.Add(CreateProduct(match.Item, supplier.Id, category));
                    continue;
                }
                var product = match.Product;
                if (match.Action == ImportAction.UpdateDescription)
                    SetDescription(product, match.Item.Description);
                product.Price = match.Item.Price;
                if (!string.IsNullOrWhiteSpace(match.Item.Unit))
                    product.Unit = match.Item.Unit;
                // Продукт есть в этом контракте — значит, его категория та же, что «что поставляет» контракта.
                // Иначе после смены «молочка» → «Молочная продукция» он пропал бы из заявок по новому контракту.
                if (category != null)
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
            var product = new Product
            {
                Category = category,
                Unit = (item.Unit ?? "").Trim(),
                Price = item.Price,
                SupplierId = supplierId
            };
            SetDescription(product, item.Description);
            return product;
        }

        /// <summary>Описание как в контракте; поля, из которых раньше собиралось описание, очищаются.</summary>
        private static void SetDescription(Product product, string description)
        {
            SplitDescription(description, out var name, out var rest);
            product.Name = name;
            product.Features = rest;
            product.Kind = "";
            product.Grade = "";
            product.Packaging = "";
            product.Country = "";
            product.SuitableForChildren = false;
            product.IndividualPackaging = false;
        }

        /// <summary>Первая фраза — наименование, остальное — особенности.</summary>
        private static void SplitDescription(string description, out string name, out string rest)
        {
            var text = (description ?? "").Trim();
            var split = Regex.Match(text, @"^(?<name>.+?)\.\s+(?<rest>\S.*)$", RegexOptions.Singleline);
            name = (split.Success ? split.Groups["name"].Value : text).Trim().TrimEnd('.');
            rest = (split.Success ? split.Groups["rest"].Value : "").Trim().TrimEnd('.');
        }

        private static string NameOf(string description)
        {
            SplitDescription(description, out var name, out _);
            return name;
        }

        private static string NormalizeName(string name)
        {
            var text = Regex.Replace((name ?? "").ToLowerInvariant(), @"[«»""'„“”]", "");
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string NormalizeText(string text) =>
            Regex.Replace((text ?? "").Trim().TrimEnd('.'), @"\s+", " ").ToLowerInvariant();

        /// <summary>Только буквы, цифры и знак процента: «МДЖ - 5%» и «МДЖ-5%» совпадают.</summary>
        private static string Compact(string text) =>
            Regex.Replace((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}%]", "");
    }
}
