using System;
using System.Collections.Generic;
using System.Linq;
using DetsadSklad.Models;

namespace DetsadSklad.Services
{
    /// <summary>
    /// Варианты для выпадающих списков продукта: стандартные плюс уже используемые в каталоге.
    /// Отдельно списки не хранятся — значение с опечаткой исчезнет, как только его не будет ни у одного продукта.
    /// </summary>
    public static class ProductChoices
    {
        private static readonly string[] DefaultGrades = { "Экстра", "Высший", "Первый", "Второй" };
        private static readonly string[] DefaultPackaging = { "массой до 0,5 кг", "массой до 1 кг", "массой до 5 кг" };
        private static readonly string[] DefaultCountries = { "Российская Федерация", "Республика Беларусь" };

        public static List<string> Grades(AppData data) => Build(DefaultGrades, data.Products.Select(p => p.Grade));
        public static List<string> Packaging(AppData data) => Build(DefaultPackaging, data.Products.Select(p => p.Packaging));
        public static List<string> Countries(AppData data) => Build(DefaultCountries, data.Products.Select(p => p.Country));

        /// <summary>Категории — это примечания контрактов («Бакалея», «Заморозка») и уже заданные у продуктов.</summary>
        public static List<string> Categories(AppData data) => Build(new string[0],
            data.Suppliers.SelectMany(s => s.Contracts).Select(c => c.Note).Concat(data.Products.Select(p => p.Category)));

        /// <summary>Только те категории, что есть у продуктов, — для фильтра в каталоге.</summary>
        public static List<string> UsedCategories(AppData data) => Build(new string[0], data.Products.Select(p => p.Category));

        private static List<string> Build(IEnumerable<string> defaults, IEnumerable<string> used)
        {
            var result = defaults.ToList();
            var extra = used
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Where(value => !result.Contains(value, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase);
            result.AddRange(extra);
            return result;
        }
    }
}
