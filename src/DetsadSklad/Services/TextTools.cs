using System;
using System.Linq;

namespace DetsadSklad.Services
{
    public static class TextTools
    {
        // Перевод строки (CR, LF), а также разделители строк и абзацев из Word (U+2028, U+2029).
        private static readonly char[] LineBreaks = { (char)13, (char)10, (char)0x2028, (char)0x2029 };

        /// <summary>
        /// Текст из нескольких строк для однострочного поля: строки через пробел.
        /// Адрес из контракта часто разбит на строки, а поле иначе вставило бы только первую.
        /// </summary>
        public static string ToSingleLine(string text)
        {
            if (text == null || text.IndexOfAny(LineBreaks) < 0)
                return text;
            var lines = text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Replace('\t', ' ').Trim())
                .Where(line => line.Length > 0);
            return string.Join(" ", lines);
        }
    }
}
