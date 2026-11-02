using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DetsadSklad.Models;

namespace DetsadSklad.Services
{
    /// <summary>
    /// Собирает документ Word по образцу детского сада. Шаблон лежит внутри программы
    /// (Assets/ZayavkaTemplate.docx), поля в нём записаны как {{OrgName}}.
    /// </summary>
    public static class RequestDocumentBuilder
    {
        private const string TemplateResource = "DetsadSklad.ZayavkaTemplate.docx";
        private const string DocumentPart = "word/document.xml";
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly Regex Placeholder = new Regex(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

        // Примерная разметка страницы А4 (в твипах): сколько места останется таблице с товарами.
        private const int SpaceForLines = 8100;
        private const int MinLineHeight = 1200;
        private const int LineHeightInCell = 207;
        private const int CharsPerCellLine = 27;
        private const int BlankRowMax = 1560;
        private const int BlankRowMin = 300;
        private const int ShortUnitLength = 3;

        public static void Build(SupplyRequest request, bool highlightVariableFields, Stream output)
        {
            using (var template = Assembly.GetExecutingAssembly().GetManifestResourceStream(TemplateResource))
            using (var input = new ZipArchive(template, ZipArchiveMode.Read))
            using (var result = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in input.Entries)
                {
                    var copy = result.CreateEntry(entry.FullName);
                    using (var from = entry.Open())
                    using (var to = copy.Open())
                    {
                        if (entry.FullName == DocumentPart)
                            FillDocument(from, request, highlightVariableFields).Save(to);
                        else
                            from.CopyTo(to);
                    }
                }
            }
        }

        private static XDocument FillDocument(Stream source, SupplyRequest request, bool highlight)
        {
            var document = XDocument.Load(source, LoadOptions.PreserveWhitespace);

            FillLines(document, request);
            RemoveEmailParagraphIfEmpty(document, "{{OrgEmail}}", request.OrgEmail);
            RemoveEmailParagraphIfEmpty(document, "{{SupplierEmail}}", request.SupplierEmail);
            ReplaceAll(document.Root, new Dictionary<string, string>
            {
                ["OrgName"] = request.OrgName,
                ["OrgAddress"] = request.OrgAddress,
                ["OrgEmail"] = request.OrgEmail,
                ["SupplierName"] = request.SupplierFullName,
                ["SupplierAddress"] = request.SupplierAddress,
                ["SupplierEmail"] = request.SupplierEmail,
                ["Number"] = request.Number.ToString(),
                ["ContractDate"] = RuFormat.LongDate(request.ContractDate),
                ["ContractNumber"] = request.ContractNumber,
                ["DeliveryAddress"] = request.DeliveryAddress,
                ["DeliveryDate"] = RuFormat.ShortDate(request.DeliveryDate),
                ["DeliveryTime"] = request.DeliveryTime,
                ["SignerName"] = request.SignerName,
                ["SentDate"] = RuFormat.ShortDate(request.SentDate)
            });

            if (!highlight)
            {
                document.Descendants(W + "color")
                    .Where(c => (string)c.Attribute(W + "val") == "FF0000")
                    .Remove();
            }
            return document;
        }

        private static void FillLines(XDocument document, SupplyRequest request)
        {
            var templateRow = document.Descendants(W + "tr").First(tr => tr.Descendants(W + "t").Any(t => t.Value == "{{LineNo}}"));
            var blankRow = (XElement)templateRow.NextNode;

            var number = 1;
            foreach (var line in request.Lines)
            {
                var row = new XElement(templateRow);
                ReplaceAll(row, new Dictionary<string, string>
                {
                    ["LineNo"] = number + ".",
                    ["LineName"] = line.DocumentName,
                    ["LineUnit"] = line.Unit,
                    ["LineQty"] = RuFormat.Quantity(line.Quantity),
                    ["LinePrice"] = RuFormat.DocMoney(line.Price),
                    ["LineCost"] = RuFormat.DocMoney(line.Cost)
                });
                if ((line.Unit ?? "").Length > ShortUnitLength)
                    ShrinkUnitText(row);
                templateRow.AddBeforeSelf(row);
                number++;
            }
            templateRow.Remove();

            // Пустая строка из образца остаётся, но уступает место, чтобы заявка входила на один лист.
            blankRow.Element(W + "trPr").Element(W + "trHeight").SetAttributeValue(W + "val", BlankRowHeight(request));
        }

        private static int BlankRowHeight(SupplyRequest request)
        {
            var linesHeight = request.Lines.Sum(l =>
            {
                var cellLines = (int)Math.Ceiling((l.DocumentName ?? "").Length / (double)CharsPerCellLine);
                return Math.Max(MinLineHeight, cellLines * LineHeightInCell);
            });
            return Math.Max(BlankRowMin, Math.Min(BlankRowMax, SpaceForLines - linesHeight));
        }

        // Длинная единица («Литр; Кубический дециметр») в образце набрана мельче, иначе не входит в узкую колонку.
        private static void ShrinkUnitText(XElement row)
        {
            var cell = row.Elements(W + "tc").ElementAt(2);
            foreach (var properties in cell.Descendants(W + "r").Select(r => r.Element(W + "rPr")))
            {
                var language = properties.Element(W + "lang");
                language.AddBeforeSelf(new XElement(W + "sz", new XAttribute(W + "val", 16)));
                language.AddBeforeSelf(new XElement(W + "szCs", new XAttribute(W + "val", 16)));
            }
        }

        private static void RemoveEmailParagraphIfEmpty(XDocument document, string placeholder, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return;
            document.Descendants(W + "t").First(t => t.Value.Contains(placeholder)).Ancestors(W + "p").First().Remove();
        }

        private static void ReplaceAll(XElement root, IDictionary<string, string> values)
        {
            foreach (var text in root.Descendants(W + "t"))
            {
                text.Value = Placeholder.Replace(text.Value, match =>
                    values.TryGetValue(match.Groups[1].Value, out var value) ? value ?? "" : match.Value);
            }
        }
    }
}
