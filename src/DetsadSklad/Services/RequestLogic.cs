using System;
using System.Collections.Generic;
using System.Linq;
using DetsadSklad.Models;

namespace DetsadSklad.Services
{
    /// <summary>Правила работы с заявками, не зависящие от экрана.</summary>
    public static class RequestLogic
    {
        /// <summary>Заявки нумеруются внутри контракта по порядку.</summary>
        public static int NextNumber(AppData data, Guid contractId)
        {
            var numbers = data.Requests.Where(r => r.ContractId == contractId).Select(r => r.Number).ToList();
            return numbers.Count == 0 ? 1 : numbers.Max() + 1;
        }

        public static bool IsNumberTaken(AppData data, Guid contractId, int number, Guid? exceptRequestId)
        {
            return data.Requests.Any(r => r.ContractId == contractId && r.Number == number && r.Id != exceptRequestId);
        }

        /// <summary>
        /// Контракт, действующий на дату. Если таких несколько (бакалея, заморозка…), берём продолжение
        /// контракта <paramref name="like"/>; без подсказки не угадываем — пусть пользователь выберет сам.
        /// </summary>
        public static Contract PickContract(Supplier supplier, DateTime date, Contract like = null)
        {
            if (supplier == null)
                return null;
            var covering = supplier.Contracts.Where(c => c.Covers(date)).OrderByDescending(c => c.SignedOn).ToList();
            if (like != null)
            {
                var same = covering.FirstOrDefault(c => IsSameKind(c, like));
                if (same != null)
                    return same;
            }
            return covering.Count == 1 ? covering[0] : null;
        }

        /// <summary>Контракты, по которым можно оформить новую заявку: срок ещё не вышел.</summary>
        public static List<Contract> OpenContracts(Supplier supplier, DateTime today)
        {
            if (supplier == null)
                return new List<Contract>();
            return supplier.Contracts.Where(c => !c.IsFinished(today)).OrderBy(c => c.ValidFrom).ThenBy(c => c.Note).ToList();
        }

        /// <summary>Один и тот же контракт, перезаключённый на новый срок: то же примечание.</summary>
        public static bool IsSameKind(Contract a, Contract b) => SameText(a.Note, b.Note);

        /// <summary>Сравнение без учёта регистра и пробелов по краям: «бакалея » = «Бакалея».</summary>
        public static bool SameText(string a, string b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Следующий рабочий день: заявка в пятницу — поставка в понедельник.</summary>
        public static DateTime NextWorkday(DateTime date)
        {
            var next = date.Date.AddDays(1);
            while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
                next = next.AddDays(1);
            return next;
        }

        /// <summary>Новый контракт: заключён сегодня и с сегодняшнего дня действует до конца квартала.</summary>
        public static Contract SuggestNewContract(DateTime today)
        {
            GetQuarter(today, out _, out var to);
            return new Contract { SignedOn = today.Date, ValidFrom = today.Date, ValidTo = to };
        }

        /// <summary>
        /// Продление: то же примечание, поэтому продукты заново заводить не нужно.
        /// Действует со дня подписания, но не раньше, чем закончится прежний, — и до конца того квартала.
        /// </summary>
        public static Contract SuggestRenewal(Contract previous, DateTime today)
        {
            var dayAfter = previous.ValidTo.Date.AddDays(1);
            var from = dayAfter > today.Date ? dayAfter : today.Date;
            GetQuarter(from, out _, out var to);
            return new Contract
            {
                Note = previous.Note,
                SignedOn = today.Date,
                ValidFrom = from,
                ValidTo = to
            };
        }

        /// <summary>
        /// Продукты для заявки по контракту: продукты поставщика с категорией, как в примечании
        /// контракта, и продукты без категории. Если с такой категорией нет ни одного продукта
        /// (примечание написано по-другому или категории ещё не проставлены) — все продукты поставщика.
        /// </summary>
        public static IEnumerable<Product> ProductsFor(AppData data, Supplier supplier, Contract contract)
        {
            var own = data.Products.Where(p => p.SupplierId == null || p.SupplierId == supplier?.Id).ToList();
            if (contract == null || string.IsNullOrWhiteSpace(contract.Note) || !own.Any(p => SameText(p.Category, contract.Note)))
                return own;
            return own.Where(p => string.IsNullOrWhiteSpace(p.Category) || SameText(p.Category, contract.Note));
        }

        /// <summary>Продукты по контракту отобраны по категории (а не показаны все).</summary>
        public static bool FiltersByCategory(AppData data, Supplier supplier, Contract contract) =>
            contract != null && !string.IsNullOrWhiteSpace(contract.Note)
            && data.Products.Any(p => (p.SupplierId == null || p.SupplierId == supplier?.Id) && SameText(p.Category, contract.Note));

        public static void GetQuarter(DateTime date, out DateTime from, out DateTime to)
        {
            var firstMonth = (date.Month - 1) / 3 * 3 + 1;
            from = new DateTime(date.Year, firstMonth, 1);
            to = from.AddMonths(3).AddDays(-1);
        }

        public static RequestLine CreateLine(Product product, decimal quantity)
        {
            return new RequestLine
            {
                ProductId = product.Id,
                Name = product.Name,
                Description = product.DocumentName,
                Unit = product.Unit,
                Price = product.Price,
                Quantity = quantity
            };
        }

        public static void CopyOrganization(SupplyRequest request, Organization org)
        {
            request.OrgName = org.Name;
            request.OrgAddress = org.Address;
            request.OrgEmail = org.Email;
            request.SignerName = org.SignerName;
            request.DeliveryAddress = org.DeliveryAddress;
        }

        public static void CopySupplier(SupplyRequest request, Supplier supplier, Contract contract)
        {
            request.SupplierId = supplier.Id;
            request.SupplierShortName = supplier.ShortName;
            request.SupplierFullName = supplier.FullName;
            request.SupplierAddress = supplier.Address;
            request.SupplierEmail = supplier.Email;
            request.ContractId = contract.Id;
            request.ContractNumber = contract.Number;
            request.ContractDate = contract.SignedOn;
        }

        /// <summary>Изменились ли реквизиты в справочниках после оформления заявки.</summary>
        public static bool DiffersFromCatalog(SupplyRequest request, AppData data)
        {
            var org = data.Organization;
            var supplier = data.FindSupplier(request.SupplierId);
            var contract = supplier?.FindContract(request.ContractId);
            if (supplier == null || contract == null)
                return false;
            return request.OrgName != org.Name || request.OrgAddress != org.Address || request.OrgEmail != org.Email
                || request.SignerName != org.SignerName || request.DeliveryAddress != org.DeliveryAddress
                || request.SupplierShortName != supplier.ShortName || request.SupplierFullName != supplier.FullName
                || request.SupplierAddress != supplier.Address || request.SupplierEmail != supplier.Email
                || request.ContractNumber != contract.Number || request.ContractDate != contract.SignedOn;
        }

        /// <summary>Поиск: каждое слово должно встретиться в номере, поставщике, контракте или товарах.</summary>
        public static bool MatchesSearch(SupplyRequest request, string search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return true;
            var haystack = BuildSearchText(request);
            return search.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .All(word => haystack.IndexOf(word.TrimStart('№'), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string BuildSearchText(SupplyRequest request)
        {
            var parts = new List<string>
            {
                request.Number.ToString(),
                request.SupplierShortName,
                request.SupplierFullName,
                request.ContractNumber,
                RuFormat.ShortDate(request.SentDate),
                RuFormat.ShortDate(request.DeliveryDate)
            };
            parts.AddRange(request.Lines.Select(l => l.Name));
            parts.AddRange(request.Lines.Select(l => l.Description));
            return string.Join(" ", parts);
        }
    }
}
