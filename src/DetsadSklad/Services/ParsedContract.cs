using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DetsadSklad.Services.Pdf;

namespace DetsadSklad.Services
{
    /// <summary>Что удалось прочитать из контракта. Поля, которых в файле не нашлось, остаются пустыми.</summary>
    public class ParsedContract
    {
        /// <summary>По какому профилю прочитан: «КУЗБАСС TRADE».</summary>
        public string ProfileName { get; set; }

        public string Number { get; set; }
        /// <summary>Что поставляет: «Бакалея» — из скобок под названием контракта.</summary>
        public string Note { get; set; }
        public DateTime? SignedOn { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public decimal? Price { get; set; }

        public string SupplierShortName { get; set; }
        public string SupplierFullName { get; set; }
        public string SupplierInn { get; set; }
        public string SupplierAddress { get; set; }
        public string SupplierEmail { get; set; }
        public string SupplierPhone { get; set; }

        public List<ParsedItem> Items { get; } = new List<ParsedItem>();

        public decimal ItemsTotal => Items.Sum(i => i.Sum);

        /// <summary>Все позиции прочитаны: у каждой количество × цена = сумма, а итог равен цене контракта.</summary>
        public bool ItemsAreComplete => Items.Count > 0 && Items.All(i => i.IsConsistent) && Price.HasValue && ItemsTotal == Price.Value;
    }

    /// <summary>Строка спецификации.</summary>
    public class ParsedItem
    {
        public int Position { get; set; }
        /// <summary>Наименование со всеми характеристиками — как в контракте.</summary>
        public string Description { get; set; }
        public string Okpd { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Sum { get; set; }
        public bool IsConsistent => Description != null && Math.Abs(Math.Round(Quantity * Price, 2) - Sum) <= 0.01m;
    }
}
