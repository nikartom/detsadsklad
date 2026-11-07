using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DetsadSklad.Models
{
    /// <summary>Всё, что хранит программа. Сохраняется одним файлом data.xml.</summary>
    [DataContract(Namespace = "")]
    public class AppData
    {
        private Organization _organization;
        private List<Supplier> _suppliers;
        private List<Product> _products;
        private List<SupplyRequest> _requests;

        public const int CurrentSchemaVersion = 3;

        [DataMember] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        [DataMember] public Organization Organization { get => _organization ?? (_organization = new Organization()); set => _organization = value; }
        [DataMember] public List<Supplier> Suppliers { get => _suppliers ?? (_suppliers = new List<Supplier>()); set => _suppliers = value; }
        [DataMember] public List<Product> Products { get => _products ?? (_products = new List<Product>()); set => _products = value; }
        [DataMember] public List<SupplyRequest> Requests { get => _requests ?? (_requests = new List<SupplyRequest>()); set => _requests = value; }

        /// <summary>Файлы Word старше этой отметки создаются заново (меняется вместе с настройкой выделения красным).</summary>
        [DataMember] public DateTime DocumentsValidFromUtc { get; set; }

        public Supplier FindSupplier(Guid id) => Suppliers.FirstOrDefault(s => s.Id == id);
    }

    /// <summary>Реквизиты детского сада и сотрудники.</summary>
    [DataContract(Namespace = "")]
    public class Organization
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string Address { get; set; }
        [DataMember] public string Email { get; set; }
        [DataMember] public string DeliveryAddress { get; set; }
        [DataMember] public string DefaultDeliveryTime { get; set; }

        /// <summary>Кто работает с программой.</summary>
        [DataMember] public string EmployeeName { get; set; }

        /// <summary>От кого документы: подпись «Заказчик ____/И.И. Иванова/».</summary>
        [DataMember] public string SignerName { get; set; }

        /// <summary>Номер и даты в документе красным цветом, как в образце.</summary>
        [DataMember] public bool HighlightVariableFields { get; set; }
    }

    [DataContract(Namespace = "")]
    public class Supplier
    {
        private List<Contract> _contracts;

        [DataMember] public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Как поставщик называется в списках: ООО "Ромашка".</summary>
        [DataMember] public string ShortName { get; set; }

        /// <summary>Как поставщик называется в документе.</summary>
        [DataMember] public string FullName { get; set; }
        [DataMember] public string Address { get; set; }
        [DataMember] public string Email { get; set; }
        [DataMember] public string Phone { get; set; }

        /// <summary>ИНН — по нему поставщик узнаётся при загрузке следующего контракта.</summary>
        [DataMember] public string Inn { get; set; }
        [DataMember] public bool IsActive { get; set; } = true;
        [DataMember] public List<Contract> Contracts { get => _contracts ?? (_contracts = new List<Contract>()); set => _contracts = value; }

        public Contract FindContract(Guid id) => Contracts.FirstOrDefault(c => c.Id == id);
    }

    /// <summary>
    /// Контракт с поставщиком; заключается каждый квартал. У одного поставщика может быть
    /// несколько контрактов на разные группы продуктов: бакалея, заморозка, молочная продукция.
    /// </summary>
    [DataContract(Namespace = "")]
    public class Contract
    {
        [DataMember] public Guid Id { get; set; } = Guid.NewGuid();
        [DataMember] public string Number { get; set; }
        [DataMember] public DateTime SignedOn { get; set; }
        [DataMember] public DateTime ValidFrom { get; set; }
        [DataMember] public DateTime ValidTo { get; set; }

        /// <summary>
        /// Что поставляет: «Бакалея». Это же и категория продуктов — в заявке по контракту
        /// предлагаются продукты с такой категорией.
        /// </summary>
        [DataMember] public string Note { get; set; }

        public string DisplayName => $"№ {Number} от {SignedOn:dd.MM.yyyy}" + (string.IsNullOrWhiteSpace(Note) ? "" : " · " + Note.Trim());
        public string PeriodText => $"{ValidFrom:dd.MM.yyyy} – {ValidTo:dd.MM.yyyy}";
        public bool Covers(DateTime date) => date.Date >= ValidFrom.Date && date.Date <= ValidTo.Date;

        /// <summary>Срок вышел — контракт в завершённых, новые заявки по нему не оформляются.</summary>
        public bool IsFinished(DateTime today) => ValidTo.Date < today.Date;
    }

    /// <summary>
    /// Продукт каталога. Свойства общие для любых товаров, пустые просто не попадают в документ.
    /// Одна и та же номенклатура у разных поставщиков — это отдельные записи со своей ценой.
    /// </summary>
    [DataContract(Namespace = "")]
    public class Product
    {
        private const string SuitableForChildrenText = "Пригодно для питания детей (воспитанников)";
        private const string IndividualPackagingText = "индивидуальная от производителя";

        [DataMember] public Guid Id { get; set; } = Guid.NewGuid();
        [DataMember] public string Name { get; set; }

        /// <summary>Группа продуктов: «Бакалея», «Молочная продукция». По ней продукты подбираются к контракту.</summary>
        [DataMember] public string Category { get; set; }
        [DataMember] public string Kind { get; set; }
        [DataMember] public string Grade { get; set; }

        /// <summary>Особенности и состав: «без консервантов и красителей».</summary>
        [DataMember] public string Features { get; set; }
        [DataMember] public bool SuitableForChildren { get; set; }

        /// <summary>Упаковка индивидуальная от производителя.</summary>
        [DataMember] public bool IndividualPackaging { get; set; }

        /// <summary>Какая именно упаковка: «массой до 1 кг».</summary>
        [DataMember] public string Packaging { get; set; }
        [DataMember] public string Country { get; set; }
        [DataMember] public string Unit { get; set; }
        [DataMember] public decimal Price { get; set; }

        /// <summary>Поставщик; пусто — товар можно заказывать у любого.</summary>
        [DataMember] public Guid? SupplierId { get; set; }

        /// <summary>Описание одной строкой из старых версий; переносится в поля при загрузке.</summary>
        [DataMember(Name = "Description", EmitDefaultValue = false)] public string LegacyDescription { get; set; }

        public string PriceText => $"{Services.RuFormat.DocMoney(Price)} руб. за {Unit}";

        /// <summary>Наименование для документа, собранное из свойств.</summary>
        public string DocumentName
        {
            get
            {
                var parts = new List<string> { Name };
                if (!string.IsNullOrWhiteSpace(Kind)) parts.Add("Тип: " + Kind);
                if (!string.IsNullOrWhiteSpace(Grade)) parts.Add("Сорт: " + Grade);
                if (!string.IsNullOrWhiteSpace(Features)) parts.Add(Features);
                if (SuitableForChildren) parts.Add(SuitableForChildrenText);
                var packaging = string.Join(" ", new[] { IndividualPackaging ? IndividualPackagingText : "", Packaging }.Where(p => !string.IsNullOrWhiteSpace(p)));
                if (packaging.Length > 0) parts.Add("Упаковка " + packaging);
                if (!string.IsNullOrWhiteSpace(Country)) parts.Add("Страна происхождения-" + Country);
                return string.Join(". ", parts.Select(part => (part ?? "").Trim().TrimEnd('.')));
            }
        }

        public static Product CreateNew(Guid? supplierId, string category = null) => new Product
        {
            SupplierId = supplierId,
            Category = category,
            Unit = "кг",
            SuitableForChildren = true,
            IndividualPackaging = true,
            Country = "Российская Федерация"
        };

        public Product Copy()
        {
            var copy = (Product)MemberwiseClone();
            copy.Id = Guid.NewGuid();
            return copy;
        }
    }

    /// <summary>
    /// Заявка на поставку. Реквизиты копируются в заявку: если позже изменится адрес
    /// или цена, уже оформленный документ останется прежним.
    /// </summary>
    [DataContract(Namespace = "")]
    public class SupplyRequest
    {
        private List<RequestLine> _lines;

        [DataMember] public Guid Id { get; set; } = Guid.NewGuid();
        [DataMember] public int Number { get; set; }
        [DataMember] public Guid SupplierId { get; set; }
        [DataMember] public Guid ContractId { get; set; }
        [DataMember] public DateTime DeliveryDate { get; set; }
        [DataMember] public DateTime SentDate { get; set; }
        [DataMember] public string DeliveryTime { get; set; }
        [DataMember] public string DeliveryAddress { get; set; }

        [DataMember] public string OrgName { get; set; }
        [DataMember] public string OrgAddress { get; set; }
        [DataMember] public string OrgEmail { get; set; }
        [DataMember] public string SignerName { get; set; }
        [DataMember] public string CreatedBy { get; set; }

        [DataMember] public string SupplierShortName { get; set; }
        [DataMember] public string SupplierFullName { get; set; }
        [DataMember] public string SupplierAddress { get; set; }
        [DataMember] public string SupplierEmail { get; set; }

        [DataMember] public string ContractNumber { get; set; }
        [DataMember] public DateTime ContractDate { get; set; }

        [DataMember] public List<RequestLine> Lines { get => _lines ?? (_lines = new List<RequestLine>()); set => _lines = value; }

        [DataMember] public DateTime CreatedAtUtc { get; set; }
        [DataMember] public DateTime UpdatedAtUtc { get; set; }

        public decimal Total => Lines.Sum(l => l.Cost);
    }

    [DataContract(Namespace = "")]
    public class RequestLine
    {
        [DataMember] public Guid? ProductId { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Description { get; set; }
        [DataMember] public string Unit { get; set; }
        [DataMember] public decimal Quantity { get; set; }
        [DataMember] public decimal Price { get; set; }

        public decimal Cost => Math.Round(Quantity * Price, 2, MidpointRounding.AwayFromZero);
        public string DocumentName => string.IsNullOrWhiteSpace(Description) ? Name : Description;
    }
}
