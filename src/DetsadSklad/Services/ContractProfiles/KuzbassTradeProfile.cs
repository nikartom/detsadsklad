using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DetsadSklad.Services.Pdf;

namespace DetsadSklad.Services.ContractProfiles
{
    /// <summary>
    /// Читает контракт, сформированный площадкой (КУЗБАСС TRADE / РТС-тендер): шапку, поставщика из
    /// раздела реквизитов, срок, цену и таблицу спецификации. Таблица разбирается по положению текста
    /// на странице: границы колонок — нарисованные линии таблицы.
    /// </summary>
    public sealed class KuzbassTradeProfile : IContractProfile
    {
        public string Name => "КУЗБАСС TRADE";

        /// <summary>Контракт этой площадки: в спецификации написано «…на площадке КУЗБАСС TRADE…».</summary>
        public bool Matches(PdfContent content) =>
            content.Runs.Any(r => r.Text.IndexOf("КУЗБАСС TRADE", StringComparison.OrdinalIgnoreCase) >= 0)
            || PdfText.Lines(content.Runs).Any(l => l.Text.IndexOf("КУЗБАСС TRADE", StringComparison.OrdinalIgnoreCase) >= 0);

        ParsedContract IContractProfile.Parse(PdfContent content) => Parse(content);

        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        private static readonly Regex DateRx = new Regex(@"\b(\d{2})\.(\d{2})\.(\d{4})\b");
        private static readonly Regex MoneyRx = new Regex(@"^\d[\d\s]*(?:[.,]\d{1,2})?$");
        private static readonly Regex OkpdRx = new Regex(@"\d{2}\.\d{2}\.\d{2}\.\d{3}");
        private static readonly Regex EmailRx = new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}");
        private static readonly char[] Invisible = { '⁠', '​', '‌', '‍', '﻿', '­' };
        private static readonly string[] SupplierLabels =
        {
            "Банковские реквизиты", "Наименование банка", "БИК", "Расчетный", "Расчётный", "р/с", "Корреспондентский", "к/с",
            "Телефон", "Тел", "Email", "E-mail", "Электронная почта", "ИНН", "КПП", "ОГРН", "Директор", "Генеральный", "Адрес банка"
        };

        public static ParsedContract Parse(PdfContent content)
        {
            var runs = content.Runs.Where(r => !string.IsNullOrWhiteSpace(r.Text)).Select(Clean).ToList();
            var lines = PdfText.Lines(runs);
            var text = string.Join("\n", lines.Select(l => l.Text));
            var flat = Regex.Replace(text, @"\s+", " ");

            var contract = new ParsedContract();
            ReadHeader(contract, lines.Where(l => l.Page == 1).Take(8).ToList());
            ReadPreamble(contract, string.Join(" ", lines.Where(l => l.Page == 1).Select(l => l.Text)));

            var period = Regex.Match(flat, @"Срок поставки Товара:?\s*с\s*(\d{2}\.\d{2}\.\d{4})\s*(?:г\.?\s*)?по\s*(\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase);
            if (period.Success)
            {
                contract.ValidFrom = ParseDate(period.Groups[1].Value);
                contract.ValidTo = ParseDate(period.Groups[2].Value);
            }
            var price = Regex.Match(flat, @"Цена Контракта составляет\s*(\d[\d\s]*(?:[.,]\d{1,2})?)", RegexOptions.IgnoreCase);
            if (price.Success)
                contract.Price = ParseMoney(price.Groups[1].Value);

            // Контракт заключён, когда его подписала последняя сторона.
            contract.SignedOn = lines.Where(l => l.Text.Contains("Дата подписания"))
                .SelectMany(l => DateRx.Matches(l.Text).Cast<Match>())
                .Select(m => ParseDate(m.Value))
                .Where(d => d.HasValue)
                .DefaultIfEmpty(null)
                .Max();

            ReadSupplierColumn(contract, runs);
            ReadSpecification(contract, runs, content.Segments);
            return contract;
        }

        private static void ReadHeader(ParsedContract contract, List<PdfLine> firstLines)
        {
            foreach (var line in firstLines)
            {
                var number = Regex.Match(line.Text, @"КОНТРАКТ.*?№\s*(\S+)", RegexOptions.IgnoreCase);
                if (contract.Number == null && number.Success)
                    contract.Number = number.Groups[1].Value.Trim();
                var subject = Regex.Match(line.Text.Trim(), @"^\((.+)\)$");
                if (contract.Note == null && subject.Success)
                    contract.Note = Capitalize(subject.Groups[1].Value.Trim());
            }
        }

        /// <summary>«… с одной стороны и ООО "Ромашка" (ООО "Ромашка"), именуемый в дальнейшем «Поставщик»».</summary>
        private static void ReadPreamble(ParsedContract contract, string pageText)
        {
            var flat = Regex.Replace(pageText, @"\s+", " ");
            var match = Regex.Match(flat, @"с одной стороны,? и\s+(?<who>.+?)\s*,?\s*именуем\w*\s+в\s+дальнейшем\s+[«""]?Поставщик", RegexOptions.IgnoreCase);
            if (!match.Success)
                return;
            var who = match.Groups["who"].Value.Trim().TrimEnd(',').Trim();
            contract.SupplierFullName = who;
            var brackets = Regex.Match(who, @"\(([^()]+)\)\s*$");
            if (brackets.Success)
            {
                contract.SupplierShortName = brackets.Groups[1].Value.Trim();
                return;
            }
            var person = Regex.Match(who, @"^(?:Индивидуальный предприниматель|ИП)\s+(?<last>[А-ЯЁ][а-яё-]+)\s+(?<first>[А-ЯЁ])[а-яё]*\s+(?<middle>[А-ЯЁ])?", RegexOptions.IgnoreCase);
            contract.SupplierShortName = person.Success
                ? $"ИП {person.Groups["last"].Value} {person.Groups["first"].Value}.{(person.Groups["middle"].Success ? person.Groups["middle"].Value + "." : "")}"
                : who;
        }

        /// <summary>Раздел «Местонахождение и банковские реквизиты Сторон»: правая колонка «ПОСТАВЩИК».</summary>
        private static void ReadSupplierColumn(ParsedContract contract, List<PdfTextRun> runs)
        {
            var title = runs.FirstOrDefault(r => r.Text.Trim().StartsWith("ПОСТАВЩИК", StringComparison.Ordinal)
                && runs.Any(o => o.Page == r.Page && Math.Abs(o.Y - r.Y) < 3 && o.Text.Trim().StartsWith("ЗАКАЗЧИК", StringComparison.Ordinal)));
            if (title == null)
                return;
            // Заголовок раздела бывает внизу страницы, а реквизиты — уже на следующей.
            var lines = new List<string>();
            for (var page = title.Page; page <= title.Page + 1; page++)
            {
                var top = page == title.Page ? title.Y + 2 : 0;
                var footer = FooterTop(runs, page);
                var column = runs.Where(r => r.Page == page && r.X >= title.X - 4 && r.Y > top && r.Y < footer).ToList();
                var signature = column.Where(r => r.Text.Contains("____")).Select(r => r.Y).DefaultIfEmpty(double.MaxValue).Min();
                lines.AddRange(TextLines(column.Where(r => r.Y < signature - 2)));
                if (signature < double.MaxValue)
                    break;
            }
            var all = string.Join("\n", lines);

            var inn = Regex.Match(all, @"ИНН\D{0,3}(\d{12}|\d{10})\b");
            if (inn.Success)
                contract.SupplierInn = inn.Groups[1].Value;

            var addressStart = lines.FindIndex(l => l.StartsWith("Адрес", StringComparison.OrdinalIgnoreCase) && !l.StartsWith("Адрес банка", StringComparison.OrdinalIgnoreCase));
            if (addressStart >= 0)
            {
                var parts = new List<string> { Regex.Replace(lines[addressStart], @"^Адрес\s*(?:юридический|места нахождения и почтовый адрес|места нахождения|почтовый)?\s*:?\s*", "", RegexOptions.IgnoreCase) };
                for (var i = addressStart + 1; i < lines.Count && !SupplierLabels.Any(label => lines[i].StartsWith(label, StringComparison.OrdinalIgnoreCase)); i++)
                    parts.Add(lines[i]);
                contract.SupplierAddress = JoinWrapped(parts.Where(p => p.Length > 0)).Trim().TrimEnd(',');
            }

            // Почта может перенестись на следующую строку после дефиса: «nxz-» + «zakaz@…».
            var joined = Regex.Replace(all, @"-\n", "-");
            var emails = joined.Split('\n')
                .Select(l => new { Line = l, Match = EmailRx.Match(l) })
                .Where(x => x.Match.Success)
                .ToList();
            var email = emails.FirstOrDefault(x => x.Line.IndexOf("заяв", StringComparison.OrdinalIgnoreCase) >= 0) ?? emails.FirstOrDefault();
            if (email != null)
                contract.SupplierEmail = email.Match.Value.Trim('-', '.');

            var phone = lines.Select(l => Regex.Match(l, @"^(?:Телефон|Тел)[^\d+]*([+\d][\d\s(),+-]{5,})", RegexOptions.IgnoreCase))
                .FirstOrDefault(m => m.Success);
            if (phone != null)
                contract.SupplierPhone = phone.Groups[1].Value.Trim().TrimEnd(',');
        }

        /// <summary>Таблица спецификации: колонки по заголовку, строки — по номеру позиции в первой колонке.</summary>
        private static void ReadSpecification(ParsedContract contract, List<PdfTextRun> runs, List<PdfSegment> segments)
        {
            var header = FindTableHeader(runs, null);
            if (header == null)
                return;
            UseTableBorders(header, runs, segments);
            ParsedItem current = null;
            var rowRuns = new List<PdfTextRun>();
            var finished = false;

            foreach (var page in runs.Select(r => r.Page).Where(p => p >= header.Page).Distinct().OrderBy(p => p))
            {
                var pageHeader = page == header.Page ? header : FindTableHeader(runs, page);
                // Колонки берём с первой страницы таблицы: на следующих страницах она та же.
                var columns = header;
                var top = pageHeader?.Bottom ?? 0;
                var footer = FooterTop(runs, page);
                foreach (var run in runs.Where(r => r.Page == page && r.Y > top + 2 && r.Y < footer - 2).OrderBy(r => r.Y).ThenBy(r => r.X))
                {
                    var trimmed = run.Text.Trim();
                    if (trimmed.StartsWith("ИТОГО", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Итого", StringComparison.OrdinalIgnoreCase))
                    {
                        finished = true;
                        break;
                    }
                    if (run.X < columns.NameLeft)
                    {
                        if (int.TryParse(trimmed, out var position) && position == contract.Items.Count + (current == null ? 1 : 2))
                        {
                            FinishRow(contract, current, rowRuns, columns);
                            current = new ParsedItem { Position = position };
                            rowRuns.Clear();
                            continue;
                        }
                        // Текст с левого края, который не номер позиции, — таблица кончилась.
                        if (current != null)
                        {
                            finished = true;
                            break;
                        }
                        continue;
                    }
                    if (current != null)
                        rowRuns.Add(run);
                }
                if (finished)
                    break;
            }
            FinishRow(contract, current, rowRuns, header);
        }

        private static void FinishRow(ParsedContract contract, ParsedItem item, List<PdfTextRun> rowRuns, TableHeader columns)
        {
            if (item == null)
                return;
            string Column(double from, double to) =>
                JoinWrapped(TextLines(rowRuns.Where(r => r.X >= from && r.X < to)));
            decimal FirstNumber(double from, double to)
            {
                foreach (var line in TextLines(rowRuns.Where(r => r.X >= from && r.X < to)))
                {
                    if (MoneyRx.IsMatch(line.Trim()))
                        return ParseMoney(line) ?? 0;
                }
                return 0;
            }

            item.Description = Column(columns.NameLeft, columns.CodeLeft);
            var code = OkpdRx.Match(Column(columns.CodeLeft, columns.UnitLeft));
            item.Okpd = code.Success ? code.Value : null;
            // Названия единиц пишутся с заглавной («Штука», «Литр; Кубический дециметр»). Слово со строчной
            // в начале — это хвост соседней колонки, вылезший за линию таблицы.
            item.Unit = Regex.Replace(Column(columns.UnitLeft, columns.QuantityLeft), @"^(?:[а-яё]+\s+)+(?=[А-ЯЁA-Z])", "");
            item.Quantity = FirstNumber(columns.QuantityLeft, columns.PriceLeft);
            item.Price = FirstNumber(columns.PriceLeft, columns.SumLeft);
            item.Sum = FirstNumber(columns.SumLeft, double.MaxValue);
            if (item.Description.Length == 0)
                item.Description = null;
            contract.Items.Add(item);
        }

        private sealed class TableHeader
        {
            public int Page;
            public double Bottom;
            public double NameLeft;
            public double CodeLeft;
            public double UnitLeft;
            public double QuantityLeft;
            public double PriceLeft;
            public double SumLeft;
        }

        /// <summary>
        /// Заголовок таблицы: «Наименование товара», «Код классификатора», «Единицы измерения», «Количество»,
        /// «Стоимость единицы», «Стоимость поставляемого…». Подписи стоят по центру колонок, а данные —
        /// от левого края, поэтому граница колонки чуть левее начала подписи.
        /// </summary>
        private static TableHeader FindTableHeader(List<PdfTextRun> runs, int? page)
        {
            const double Indent = 12;
            foreach (var quantity in runs.Where(r => (page == null || r.Page == page) && r.Text.Trim().StartsWith("Количеств", StringComparison.Ordinal)))
            {
                var near = runs.Where(r => r.Page == quantity.Page && Math.Abs(r.Y - quantity.Y) < 60).ToList();
                double? Left(params string[] prefixes)
                {
                    var found = near.Where(r => prefixes.Any(p => r.Text.Trim().StartsWith(p, StringComparison.Ordinal))).ToList();
                    return found.Count == 0 ? (double?)null : found.Min(r => r.X);
                }
                var name = Left("Наименовани");
                var code = Left("Код", "классификат");
                var unit = Left("Единиц", "измерени");
                var price = Left("единиц", "продукц");
                var sum = Left("поставляем", "выполняем", "оказываем");
                if (name == null || code == null || unit == null || price == null || sum == null)
                    continue;
                var headerWords = new[] { "Наименовани", "Код", "классификат", "Единиц", "измерени", "Количеств", "единиц", "продукц",
                                          "поставляем", "выполняем", "оказываем", "Стои", "товара", "работ", "услуг", "№" };
                var bottom = near.Where(r => headerWords.Any(w => r.Text.Trim().StartsWith(w, StringComparison.Ordinal)) && r.Y >= quantity.Y - 50)
                    .Max(r => r.Y);
                return new TableHeader
                {
                    Page = quantity.Page,
                    Bottom = bottom,
                    NameLeft = name.Value - Indent,
                    CodeLeft = code.Value - Indent,
                    UnitLeft = unit.Value - Indent,
                    QuantityLeft = quantity.X - Indent,
                    PriceLeft = price.Value - Indent,
                    SumLeft = sum.Value - Indent
                };
            }
            return null;
        }

        /// <summary>
        /// Границы колонок — вертикальные линии, которые пересекают первую строку таблицы:
        /// левый край, шесть границ между семью колонками и правый край. Если линий не нашлось,
        /// остаются границы, прикинутые по подписям заголовка.
        /// </summary>
        private static void UseTableBorders(TableHeader header, List<PdfTextRun> runs, List<PdfSegment> segments)
        {
            var first = runs.Where(r => r.Page == header.Page && r.Y > header.Bottom + 2 && r.X < header.NameLeft + 12 && r.Text.Trim() == "1")
                .OrderBy(r => r.Y).FirstOrDefault();
            if (first == null)
                return;
            var borders = new List<double>();
            foreach (var x in segments.Where(s => s.Page == header.Page && s.IsVertical && s.Top <= first.Y + 1 && s.Bottom >= first.Y - 1)
                         .Select(s => s.X1).OrderBy(x => x))
            {
                if (borders.Count == 0 || x - borders[borders.Count - 1] > 2)
                    borders.Add(x);
            }
            if (borders.Count != 8)
                return;
            header.NameLeft = borders[1];
            header.CodeLeft = borders[2];
            header.UnitLeft = borders[3];
            header.QuantityLeft = borders[4];
            header.PriceLeft = borders[5];
            header.SumLeft = borders[6];
        }

        /// <summary>Подпись площадки внизу страницы («Документ подписан на ЭП…») — не часть документа.</summary>
        private static double FooterTop(List<PdfTextRun> runs, int page) =>
            runs.Where(r => r.Page == page && r.Text.Contains("Документ подписан на ЭП")).Select(r => r.Y).DefaultIfEmpty(double.MaxValue).Min();

        /// <summary>Строки из кусков текста одной колонки: куски на одной высоте склеиваются слева направо.</summary>
        private static IEnumerable<string> TextLines(IEnumerable<PdfTextRun> runs)
        {
            // Ячейка может начаться на одной странице и закончиться на следующей — сначала страница, потом высота.
            var sorted = runs.OrderBy(r => r.Page).ThenBy(r => r.Y).ThenBy(r => r.X).ToList();
            var groups = new List<List<PdfTextRun>>();
            foreach (var run in sorted)
            {
                var last = groups.LastOrDefault();
                if (last != null && last[0].Page == run.Page && Math.Abs(last[0].Y - run.Y) <= Math.Max(1.5, run.Size * 0.35))
                    last.Add(run);
                else
                    groups.Add(new List<PdfTextRun> { run });
            }
            foreach (var group in groups)
            {
                var line = new StringBuilder();
                PdfTextRun previous = null;
                foreach (var run in group.OrderBy(r => r.X))
                {
                    if (previous != null && run.X - previous.Right > Math.Max(1, run.Size) * 0.15
                        && line.Length > 0 && line[line.Length - 1] != ' ' && !run.Text.StartsWith(" "))
                        line.Append(' ');
                    line.Append(run.Text);
                    previous = run;
                }
                var text = Regex.Replace(line.ToString(), @"\s+", " ").Trim();
                if (text.Length > 0)
                    yield return text;
            }
        }

        /// <summary>Склеивает перенесённые строки ячейки; перенос по дефису («450-» + «600 гр») — без пробела.</summary>
        private static string JoinWrapped(IEnumerable<string> lines)
        {
            var result = new StringBuilder();
            foreach (var line in lines)
            {
                if (result.Length > 0)
                {
                    var last = result[result.Length - 1];
                    var previous = result.Length > 1 ? result[result.Length - 2] : ' ';
                    var hyphenWrap = last == '-' && char.IsLetterOrDigit(previous) && line.Length > 0 && (char.IsLower(line[0]) || char.IsDigit(line[0]));
                    if (!hyphenWrap)
                        result.Append(' ');
                }
                result.Append(line);
            }
            return result.ToString().Trim();
        }

        private static PdfTextRun Clean(PdfTextRun run)
        {
            var text = run.Text;
            if (text.IndexOfAny(Invisible) >= 0)
                text = new string(text.Where(c => Array.IndexOf(Invisible, c) < 0).ToArray());
            return new PdfTextRun { Page = run.Page, X = run.X, Right = run.Right, Y = run.Y, Size = run.Size, Text = text.Replace(' ', ' ') };
        }

        private static string Capitalize(string text) =>
            text.Length == 0 ? text : char.ToUpper(text[0], Ru) + text.Substring(1);

        private static DateTime? ParseDate(string text) =>
            DateTime.TryParseExact(text, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateTime?)null;

        private static decimal? ParseMoney(string text)
        {
            var digits = Regex.Replace(text, @"\s", "").Replace(',', '.').TrimEnd('.');
            return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : (decimal?)null;
        }
    }
}
