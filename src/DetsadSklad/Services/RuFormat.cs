using System;
using System.Globalization;

namespace DetsadSklad.Services
{
    /// <summary>Числа и даты по-русски: как в документе и как удобно вводить.</summary>
    public static class RuFormat
    {
        public static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

        private static readonly string[] MonthsGenitive =
        {
            "января", "февраля", "марта", "апреля", "мая", "июня",
            "июля", "августа", "сентября", "октября", "ноября", "декабря"
        };

        /// <summary>Для документа: 6500,00</summary>
        public static string DocMoney(decimal value) => value.ToString("0.00", Ru);

        /// <summary>Для документа и полей ввода: 100 или 3,6</summary>
        public static string Quantity(decimal value) => value.ToString("0.###", Ru);

        /// <summary>Для экрана: 15 715,00 руб.</summary>
        public static string Money(decimal value) => value.ToString("#,##0.00", Ru).Replace(' ', ' ') + " руб.";

        /// <summary>Для документа: «24» сентября 2026</summary>
        public static string LongDate(DateTime date) =>
            $"«{date:dd}» {MonthsGenitive[date.Month - 1]} {date.Year}";

        public static string ShortDate(DateTime date) => date.ToString("dd.MM.yyyy", Ru);

        /// <summary>Понимает и «3,6», и «3.6», и «1 200».</summary>
        public static bool TryParseDecimal(string text, out decimal value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var normalized = text.Trim().Replace(" ", "").Replace(" ", "").Replace('.', ',');
            return decimal.TryParse(normalized, NumberStyles.Number, Ru, out value);
        }
    }
}
