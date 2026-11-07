using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace DetsadSklad.Tests
{
    /// <summary>
    /// Собирает PDF для проверок так же, как это делает площадка: шрифт Type0/Identity-H с таблицей
    /// ToUnicode, сжатые потоки, линии таблицы. Координаты задаются сверху страницы, как в PdfTextRun.
    /// </summary>
    internal sealed class TestPdf
    {
        public const double PageHeight = 842;
        public const int GlyphWidth = 500;

        private readonly List<StringBuilder> _pages = new List<StringBuilder>();
        private readonly List<char> _alphabet = new List<char>();

        public TestPdf NewPage()
        {
            _pages.Add(new StringBuilder());
            return this;
        }

        /// <summary>Строка текста кеглем size; каждая буква шириной половина кегля.</summary>
        public TestPdf Text(double x, double y, string text, double size = 8)
        {
            var codes = string.Concat(text.Select(c => Code(c).ToString("X4")));
            Current.AppendFormat(CultureInfo.InvariantCulture, "BT /F1 {0} Tf 1 0 0 1 {1} {2} Tm <{3}> Tj ET\n", size, x, PageHeight - y, codes);
            return this;
        }

        /// <summary>Текст через TJ с отступом (как площадка пишет слова с подгонкой ширины).</summary>
        public TestPdf Kerned(double x, double y, string first, double adjust, string second, double size = 8)
        {
            Current.AppendFormat(CultureInfo.InvariantCulture, "BT /F1 {0} Tf {1} {2} Td [<{3}> {4} <{5}>] TJ ET\n", size, x, PageHeight - y,
                string.Concat(first.Select(c => Code(c).ToString("X4"))), adjust, string.Concat(second.Select(c => Code(c).ToString("X4"))));
            return this;
        }

        /// <summary>Вертикальная линия таблицы.</summary>
        public TestPdf VLine(double x, double top, double bottom)
        {
            Current.AppendFormat(CultureInfo.InvariantCulture, "q 0.5 w {0} {1} m {0} {2} l S Q\n", x, PageHeight - top, PageHeight - bottom);
            return this;
        }

        public byte[] Build()
        {
            var objects = new List<byte[]>();
            int Add(string text) { objects.Add(Encoding.ASCII.GetBytes(text)); return objects.Count; }
            int AddStream(string dict, byte[] data)
            {
                var head = Encoding.ASCII.GetBytes($"<< {dict} /Length {data.Length} /Filter /FlateDecode >>\nstream\n");
                var tail = Encoding.ASCII.GetBytes("\nendstream");
                objects.Add(head.Concat(data).Concat(tail).ToArray());
                return objects.Count;
            }

            var toUnicode = AddStream("", Zlib(Encoding.ASCII.GetBytes(CMap())));
            var widths = string.Join(" ", _alphabet.Select(_ => GlyphWidth));
            var cidFont = Add($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /TestFont /DW 1000 /W [1 [{widths}]] >>");
            var font = Add($"<< /Type /Font /Subtype /Type0 /BaseFont /TestFont /Encoding /Identity-H /DescendantFonts [{cidFont} 0 R] /ToUnicode {toUnicode} 0 R >>");
            var pagesNumber = objects.Count + 1 + _pages.Count * 2;
            var pageNumbers = new List<int>();
            foreach (var page in _pages)
            {
                var content = AddStream("", Zlib(Encoding.ASCII.GetBytes(page.ToString())));
                pageNumbers.Add(Add($"<< /Type /Page /Parent {pagesNumber} 0 R /Contents {content} 0 R >>"));
            }
            var kids = string.Join(" ", pageNumbers.Select(n => n + " 0 R"));
            Add($"<< /Type /Pages /Kids [{kids}] /Count {pageNumbers.Count} /MediaBox [0 0 595 {PageHeight}] /Resources << /Font << /F1 {font} 0 R >> >> >>");
            var catalog = Add($"<< /Type /Catalog /Pages {pagesNumber} 0 R >>");

            using (var output = new MemoryStream())
            {
                void Write(string text) { var b = Encoding.ASCII.GetBytes(text); output.Write(b, 0, b.Length); }
                Write("%PDF-1.7\n");
                var offsets = new List<long>();
                for (var i = 0; i < objects.Count; i++)
                {
                    offsets.Add(output.Position);
                    Write($"{i + 1} 0 obj\n");
                    output.Write(objects[i], 0, objects[i].Length);
                    Write("\nendobj\n");
                }
                var xref = output.Position;
                Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
                foreach (var offset in offsets)
                    Write($"{offset:D10} 00000 n \n");
                Write($"trailer\n<< /Size {objects.Count + 1} /Root {catalog} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
                return output.ToArray();
            }
        }

        private StringBuilder Current => _pages.Count > 0 ? _pages[_pages.Count - 1] : NewPage()._pages[0];

        private int Code(char c)
        {
            var index = _alphabet.IndexOf(c);
            if (index < 0)
            {
                _alphabet.Add(c);
                index = _alphabet.Count - 1;
            }
            return index + 1;
        }

        private string CMap()
        {
            var text = new StringBuilder("/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n1 begincodespacerange <0000> <FFFF> endcodespacerange\n");
            // Половина букв — отдельными bfchar, остальные — диапазонами bfrange по одной букве.
            var half = _alphabet.Count / 2;
            text.AppendFormat("{0} beginbfchar\n", half);
            for (var i = 0; i < half; i++)
                text.AppendFormat("<{0:X4}> <{1:X4}>\n", i + 1, (int)_alphabet[i]);
            text.Append("endbfchar\n");
            text.AppendFormat("{0} beginbfrange\n", _alphabet.Count - half);
            for (var i = half; i < _alphabet.Count; i++)
                text.AppendFormat("<{0:X4}> <{0:X4}> <{1:X4}>\n", i + 1, (int)_alphabet[i]);
            text.Append("endbfrange\nendcmap CMapName currentdict /CMap defineresource pop end end\n");
            return text.ToString();
        }

        private static byte[] Zlib(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x9C);
                using (var deflate = new DeflateStream(output, CompressionMode.Compress, leaveOpen: true))
                    deflate.Write(data, 0, data.Length);
                uint a = 1, b = 0;
                foreach (var x in data)
                {
                    a = (a + x) % 65521;
                    b = (b + a) % 65521;
                }
                var adler = (b << 16) | a;
                output.WriteByte((byte)(adler >> 24));
                output.WriteByte((byte)(adler >> 16));
                output.WriteByte((byte)(adler >> 8));
                output.WriteByte((byte)adler);
                return output.ToArray();
            }
        }
    }
}
