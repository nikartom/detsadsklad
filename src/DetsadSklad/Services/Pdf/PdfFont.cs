using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DetsadSklad.Services.Pdf
{
    /// <summary>Шрифт страницы: как перевести байты строки в буквы и какой ширины каждая буква.</summary>
    internal sealed class PdfFont
    {
        private static readonly Encoding WinAnsi = Encoding.GetEncoding(1252);

        private readonly bool _twoByte;
        private readonly Dictionary<int, string> _toUnicode = new Dictionary<int, string>();
        private readonly Dictionary<int, double> _widths = new Dictionary<int, double>();
        private readonly Dictionary<int, string> _differences = new Dictionary<int, string>();
        private readonly double _defaultWidth;

        public PdfFont(PdfDocument doc, Dictionary<string, object> dict)
        {
            var subtype = doc.Name(PdfDocument.Get(dict, "Subtype"));
            _twoByte = subtype == "Type0";
            if (_twoByte)
            {
                var descendant = doc.Dict(doc.Array(PdfDocument.Get(dict, "DescendantFonts"))?.FirstOrDefault());
                _defaultWidth = doc.Number(PdfDocument.Get(descendant, "DW"), 1000);
                ReadCidWidths(doc, doc.Array(PdfDocument.Get(descendant, "W")));
            }
            else
            {
                var first = (int)doc.Number(PdfDocument.Get(dict, "FirstChar"), 0);
                var widths = doc.Array(PdfDocument.Get(dict, "Widths"));
                if (widths != null)
                {
                    for (var i = 0; i < widths.Count; i++)
                        _widths[first + i] = doc.Number(widths[i]);
                }
                var descriptor = doc.Dict(PdfDocument.Get(dict, "FontDescriptor"));
                _defaultWidth = doc.Number(PdfDocument.Get(descriptor, "MissingWidth"), 500);
                ReadDifferences(doc, doc.Dict(PdfDocument.Get(dict, "Encoding")));
            }
            if (doc.Resolve(PdfDocument.Get(dict, "ToUnicode")) is PdfStream cmap)
                ReadToUnicode(doc.Decode(cmap));
        }

        /// <summary>Буквы строки: текст и ширина в долях кегля.</summary>
        public IEnumerable<Glyph> Decode(byte[] bytes)
        {
            var step = _twoByte ? 2 : 1;
            for (var i = 0; i + step <= bytes.Length; i += step)
            {
                var code = _twoByte ? (bytes[i] << 8) | bytes[i + 1] : bytes[i];
                var width = (_widths.TryGetValue(code, out var w) ? w : _defaultWidth) / 1000.0;
                yield return new Glyph(TextOf(code), width, !_twoByte && code == 32);
            }
        }

        private string TextOf(int code)
        {
            if (_toUnicode.TryGetValue(code, out var text))
                return text;
            if (_twoByte)
                return "";
            if (_differences.TryGetValue(code, out var glyphName))
                return GlyphNameToText(glyphName);
            return WinAnsi.GetString(new[] { (byte)code });
        }

        private void ReadCidWidths(PdfDocument doc, List<object> w)
        {
            if (w == null)
                return;
            for (var i = 0; i < w.Count;)
            {
                var first = (int)doc.Number(w[i]);
                if (i + 1 < w.Count && doc.Resolve(w[i + 1]) is List<object> list)
                {
                    for (var j = 0; j < list.Count; j++)
                        _widths[first + j] = doc.Number(list[j]);
                    i += 2;
                }
                else if (i + 2 < w.Count)
                {
                    var last = (int)doc.Number(w[i + 1]);
                    var width = doc.Number(w[i + 2]);
                    for (var code = first; code <= last && code - first < 65536; code++)
                        _widths[code] = width;
                    i += 3;
                }
                else
                {
                    break;
                }
            }
        }

        private void ReadDifferences(PdfDocument doc, Dictionary<string, object> encoding)
        {
            var differences = doc.Array(PdfDocument.Get(encoding, "Differences"));
            if (differences == null)
                return;
            var code = 0;
            foreach (var item in differences.Select(doc.Resolve))
            {
                if (item is double number)
                    code = (int)number;
                else if (item is PdfName name)
                    _differences[code++] = name.Value;
            }
        }

        private static string GlyphNameToText(string name)
        {
            if (name.StartsWith("uni", StringComparison.Ordinal) && name.Length == 7
                && int.TryParse(name.Substring(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                return ((char)code).ToString();
            switch (name)
            {
                case "space": return " ";
                case "period": return ".";
                case "comma": return ",";
                case "hyphen": return "-";
                default: return name.Length == 1 ? name : "";
            }
        }

        /// <summary>Таблица ToUnicode: какому символу соответствует код буквы.</summary>
        private void ReadToUnicode(byte[] cmap)
        {
            var parser = new PdfParser(cmap, 0);
            var items = new List<object>();
            while (true)
            {
                var item = parser.Read();
                if (item == PdfParser.End)
                    break;
                if (item is PdfKeyword keyword && (keyword.Value == "endbfchar" || keyword.Value == "endbfrange"))
                {
                    if (keyword.Value == "endbfchar")
                        ReadBfChar(items);
                    else
                        ReadBfRange(items);
                }
                if (item is PdfKeyword start && (start.Value == "beginbfchar" || start.Value == "beginbfrange"))
                    items.Clear();
                else
                    items.Add(item);
            }
        }

        private void ReadBfChar(List<object> items)
        {
            for (var i = 0; i + 1 < items.Count; i += 2)
            {
                if (items[i] is PdfString src && items[i + 1] is PdfString dst)
                    _toUnicode[ToCode(src.Bytes)] = Utf16(dst.Bytes);
            }
        }

        private void ReadBfRange(List<object> items)
        {
            for (var i = 0; i + 2 < items.Count; i += 3)
            {
                if (!(items[i] is PdfString low) || !(items[i + 1] is PdfString high))
                    continue;
                var from = ToCode(low.Bytes);
                var to = ToCode(high.Bytes);
                if (to - from > 65536)
                    continue;
                if (items[i + 2] is PdfString dst)
                {
                    var chars = Utf16(dst.Bytes).ToCharArray();
                    for (var code = from; code <= to; code++)
                    {
                        _toUnicode[code] = new string(chars);
                        if (chars.Length > 0)
                            chars[chars.Length - 1]++;
                    }
                }
                else if (items[i + 2] is List<object> list)
                {
                    for (var code = from; code <= to && code - from < list.Count; code++)
                    {
                        if (list[code - from] is PdfString each)
                            _toUnicode[code] = Utf16(each.Bytes);
                    }
                }
            }
        }

        private static int ToCode(byte[] bytes) => bytes.Aggregate(0, (code, b) => (code << 8) | b);

        private static string Utf16(byte[] bytes) => bytes.Length % 2 == 0
            ? Encoding.BigEndianUnicode.GetString(bytes)
            : WinAnsi.GetString(bytes);

        public struct Glyph
        {
            public Glyph(string text, double width, bool isSpaceCode)
            {
                Text = text;
                Width = width;
                IsSpaceCode = isSpaceCode;
            }

            public string Text { get; }
            public double Width { get; }
            public bool IsSpaceCode { get; }
        }
    }
}
