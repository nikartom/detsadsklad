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

        /// <summary>Контракт, действующий на дату; если такого нет — самый свежий.</summary>
        public static Contract PickContract(Supplier supplier, DateTime date)
        {
            if (supplier == null)
                return null;
            return supplier.Contracts.Where(c => c.Covers(date)).OrderByDescending(c => c.SignedOn).FirstOrDefault()
                ?? supplier.Contracts.OrderByDescending(c => c.ValidTo).FirstOrDefault();
        }

        /// <summary>Следующий рабочий день: заявка в пятницу — поставка в понедельник.</summary>
        public static DateTime NextWorkday(DateTime date)
        {
            var next = date.Date.AddDays(1);
            while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
                next = next.AddDays(1);
            return next;
        }

        /// <summary>Период нового контракта: квартал после предыдущего или текущий квартал.</summary>
        public static void SuggestContractPeriod(Supplier supplier, DateTime today, out DateTime from, out DateTime to)
        {
            var lastEnd = supplier?.Contracts.OrderByDescending(c => c.ValidTo).FirstOrDefault()?.ValidTo;
            var anchor = lastEnd.HasValue ? lastEnd.Value.Date.AddDays(1) : today.Date;
            GetQuarter(anchor, out from, out to);
            if (lastEnd.HasValue)
                from = anchor;
        }

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
