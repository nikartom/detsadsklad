using System.Linq;
using System.Text.RegularExpressions;
using DetsadSklad.Models;

namespace DetsadSklad.Services
{
    /// <summary>Приводит данные, сохранённые старой версией программы, к текущему виду.</summary>
    public static class DataMigrations
    {
        private const int PackagingSplitVersion = 3;
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly Regex Country = new Regex(@"[,.;]?\s*страна происхождения\s*[-–—:]?\s*(?<v>[^.]+)\.?\s*$", Options);
        private static readonly Regex Suitable = new Regex(@"пригодно для питания детей(\s*\(воспитанников\))?\s*[.,;]?", Options);
        private static readonly Regex Packaging = new Regex(@"[,;]?\s*\bупаковка\s+(?<v>.+?)(?=\.\s|\.$|$)", Options);
        private static readonly Regex Kind = new Regex(@"\bтип\b[^:.\-–—]*[:\-–—]\s*(?<v>[^.,]+)", Options);
        private static readonly Regex Grade = new Regex(@"[,.]?\s*\bсорт\b[:\s-]*(?<v>[^.,]+)", Options);
        private static readonly Regex IndividualPackaging = new Regex(@"^\s*индивидуальная от производителя\s*[-–—,:]?\s*(?<rest>.*)$", Options);
        private static readonly Regex NameThenOrigin = new Regex(@"^(?<name>.*?\d\s*%)\s+(?<rest>из\s.+)$", Options);

        /// <summary>Возвращает true, если данные изменились и их нужно сохранить.</summary>
        public static bool Upgrade(AppData data)
        {
            if (data.SchemaVersion >= AppData.CurrentSchemaVersion)
                return false;

            foreach (var product in data.Products.Where(p => !string.IsNullOrWhiteSpace(p.LegacyDescription)))
            {
                SplitDescription(product, product.LegacyDescription);
                product.LegacyDescription = null;
            }
            if (data.SchemaVersion < PackagingSplitVersion)
            {
                foreach (var product in data.Products)
                    SplitPackaging(product);
            }
            data.SchemaVersion = AppData.CurrentSchemaVersion;
            return true;
        }

        /// <summary>Разбирает прежнее описание одной строкой на поля. Что не распозналось — в «Особенности».</summary>
        public static void SplitDescription(Product product, string description)
        {
            var text = description.Trim();
            product.Country = Take(Country, ref text);
            product.Packaging = Take(Packaging, ref text);
            product.Kind = Take(Kind, ref text);
            product.Grade = Take(Grade, ref text);
            product.SuitableForChildren = Suitable.IsMatch(text);
            text = Suitable.Replace(text, "");

            var sentences = text.Split('.')
                .Select(s => s.Trim(' ', ',', ';'))
                .Where(s => s.Length > 0)
                .ToList();
            var name = sentences.FirstOrDefault() ?? product.Name;
            var features = sentences.Skip(1).ToList();

            var origin = NameThenOrigin.Match(name);
            if (origin.Success)
            {
                name = origin.Groups["name"].Value;
                var rest = origin.Groups["rest"].Value;
                features.Insert(0, char.ToUpper(rest[0]) + rest.Substring(1));
            }

            product.Name = name;
            product.Features = string.Join(". ", features);
        }

        /// <summary>«индивидуальная от производителя - массой до 1 кг» → галочка и «массой до 1 кг».</summary>
        public static void SplitPackaging(Product product)
        {
            var match = IndividualPackaging.Match(product.Packaging ?? "");
            if (!match.Success)
                return;
            product.IndividualPackaging = true;
            product.Packaging = match.Groups["rest"].Value.Trim();
        }

        private static string Take(Regex pattern, ref string text)
        {
            var match = pattern.Match(text);
            if (!match.Success)
                return "";
            text = text.Remove(match.Index, match.Length);
            return match.Groups["v"].Value.Trim(' ', '.', ',');
        }
    }
}
