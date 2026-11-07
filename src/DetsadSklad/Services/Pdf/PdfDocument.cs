using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DetsadSklad.Services.Pdf
{
    /// <summary>
    /// Минимальное чтение PDF: таблица объектов, страницы, распаковка потоков.
    /// Хватает для документов, которые формируют электронные площадки; шифрование и сканы не поддерживаются.
    /// </summary>
    public sealed class PdfDocument
    {
        private readonly byte[] _data;
        private readonly Dictionary<int, int> _offsets = new Dictionary<int, int>();
        private readonly Dictionary<int, object> _cache = new Dictionary<int, object>();

        public PdfDocument(byte[] data)
        {
            _data = data;
            if (!TryReadXref())
                ScanObjects();
            if (Trailer == null || !Trailer.ContainsKey("Root"))
                throw new InvalidDataException("Файл не похож на документ PDF.");
            if (Trailer.ContainsKey("Encrypt"))
                throw new InvalidDataException("Документ PDF защищён паролем.");
        }

        public Dictionary<string, object> Trailer { get; private set; }

        public object Resolve(object value)
        {
            for (var depth = 0; value is PdfRef reference && depth < 32; depth++)
                value = GetObject(reference.Number);
            return value;
        }

        public Dictionary<string, object> Dict(object value)
        {
            var resolved = Resolve(value);
            return resolved as Dictionary<string, object> ?? (resolved as PdfStream)?.Dict;
        }

        public List<object> Array(object value) => Resolve(value) as List<object>;

        public double Number(object value, double fallback = 0) => Resolve(value) is double number ? number : fallback;

        public string Name(object value) => (Resolve(value) as PdfName)?.Value;

        public static object Get(Dictionary<string, object> dict, string key) =>
            dict != null && dict.TryGetValue(key, out var value) ? value : null;

        /// <summary>Страницы по порядку: словарь страницы с унаследованными ресурсами и размером.</summary>
        public List<PdfPage> Pages()
        {
            var pages = new List<PdfPage>();
            var root = Dict(Trailer["Root"]);
            CollectPages(Dict(Get(root, "Pages")), null, null, pages, 0);
            return pages;
        }

        private void CollectPages(Dictionary<string, object> node, object resources, object mediaBox, List<PdfPage> pages, int depth)
        {
            if (node == null || depth > 64)
                return;
            resources = Get(node, "Resources") ?? resources;
            mediaBox = Get(node, "MediaBox") ?? mediaBox;
            var kids = Array(Get(node, "Kids"));
            if (kids != null && Name(Get(node, "Type")) != "Page")
            {
                foreach (var kid in kids)
                    CollectPages(Dict(kid), resources, mediaBox, pages, depth + 1);
                return;
            }
            var box = Array(mediaBox)?.Select(v => Number(v)).ToArray();
            var height = box != null && box.Length == 4 ? Math.Abs(box[3] - box[1]) : 842;
            pages.Add(new PdfPage(node, Dict(resources), height));
        }

        /// <summary>Содержимое страницы: один или несколько потоков подряд.</summary>
        public byte[] PageContent(PdfPage page)
        {
            var contents = Resolve(Get(page.Dict, "Contents"));
            var streams = contents is List<object> list ? list.Select(Resolve).OfType<PdfStream>() : new[] { contents as PdfStream };
            using (var all = new MemoryStream())
            {
                foreach (var stream in streams.Where(s => s != null))
                {
                    var bytes = Decode(stream);
                    all.Write(bytes, 0, bytes.Length);
                    all.WriteByte((byte)'\n');
                }
                return all.ToArray();
            }
        }

        /// <summary>Распаковывает поток. Поддерживается только сжатие FlateDecode — им пользуются почти все программы.</summary>
        public byte[] Decode(PdfStream stream)
        {
            var filter = Resolve(Get(stream.Dict, "Filter"));
            var filters = filter is List<object> list ? list.Select(Name).ToList() : new List<string> { Name(filter) };
            var data = stream.Raw;
            foreach (var name in filters.Where(n => n != null))
            {
                if (name != "FlateDecode" && name != "Fl")
                    throw new NotSupportedException("Сжатие " + name + " не поддерживается.");
                data = Inflate(data);
            }
            return data;
        }

        private static byte[] Inflate(byte[] data)
        {
            // Заголовок zlib (2 байта) DeflateStream не понимает — пропускаем его.
            var skip = data.Length > 2 && (data[0] & 0x0F) == 8 ? 2 : 0;
            using (var input = new MemoryStream(data, skip, data.Length - skip))
            using (var inflater = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[16384];
                try
                {
                    int read;
                    while ((read = inflater.Read(buffer, 0, buffer.Length)) > 0)
                        output.Write(buffer, 0, read);
                }
                catch (InvalidDataException)
                {
                    // Повреждённый хвост потока: берём то, что успели распаковать.
                }
                return output.ToArray();
            }
        }

        private object GetObject(int number)
        {
            if (_cache.TryGetValue(number, out var cached))
                return cached;
            object value = null;
            if (_offsets.TryGetValue(number, out var offset))
                value = ReadObjectAt(offset);
            _cache[number] = value;
            return value;
        }

        private object ReadObjectAt(int offset)
        {
            var parser = new PdfParser(_data, offset);
            parser.Read();
            parser.Read();
            if (!(parser.Read() is PdfKeyword obj) || obj.Value != "obj")
                return null;
            var value = parser.Read();
            if (!(value is Dictionary<string, object> dict))
                return value;
            var afterDict = parser.Position;
            if (!(parser.Read() is PdfKeyword keyword) || keyword.Value != "stream")
            {
                parser.Position = afterDict;
                return dict;
            }
            var start = parser.Position;
            if (start < _data.Length && _data[start] == '\r')
                start++;
            if (start < _data.Length && _data[start] == '\n')
                start++;
            var length = (int)Number(Get(dict, "Length"), -1);
            if (length < 0 || start + length > _data.Length || !EndstreamFollows(start + length))
                length = FindEndstream(start) - start;
            var raw = new byte[Math.Max(0, length)];
            Buffer.BlockCopy(_data, start, raw, 0, raw.Length);
            return new PdfStream(dict, raw);
        }

        private bool EndstreamFollows(int position)
        {
            var parser = new PdfParser(_data, position);
            return parser.Read() is PdfKeyword keyword && keyword.Value == "endstream";
        }

        private int FindEndstream(int start)
        {
            var marker = Encoding.ASCII.GetBytes("endstream");
            for (var i = start; i <= _data.Length - marker.Length; i++)
            {
                var match = true;
                for (var j = 0; j < marker.Length && match; j++)
                    match = _data[i + j] == marker[j];
                if (match)
                {
                    var end = i;
                    while (end > start && (_data[end - 1] == '\n' || _data[end - 1] == '\r'))
                        end--;
                    return end;
                }
            }
            return _data.Length;
        }

        /// <summary>Классическая таблица xref с цепочкой /Prev (дописанные версии документа).</summary>
        private bool TryReadXref()
        {
            try
            {
                var tail = Encoding.ASCII.GetString(_data, Math.Max(0, _data.Length - 2048), Math.Min(2048, _data.Length));
                var match = Regex.Match(tail, @"startxref\s+(\d+)", RegexOptions.RightToLeft);
                if (!match.Success)
                    return false;
                var offset = int.Parse(match.Groups[1].Value);
                var visited = new HashSet<int>();
                while (offset > 0 && offset < _data.Length && visited.Add(offset))
                {
                    var parser = new PdfParser(_data, offset);
                    if (!(parser.Read() is PdfKeyword xref) || xref.Value != "xref")
                        return false;
                    while (true)
                    {
                        var first = parser.Read();
                        if (first is PdfKeyword trailerWord && trailerWord.Value == "trailer")
                            break;
                        var count = parser.Read();
                        if (!(first is double start) || !(count is double total))
                            return false;
                        for (var i = 0; i < (int)total; i++)
                        {
                            var entryOffset = parser.Read();
                            parser.Read();
                            var kind = parser.Read() as PdfKeyword;
                            var number = (int)start + i;
                            // Более новая часть документа читается первой и имеет приоритет.
                            if (kind?.Value == "n" && entryOffset is double at && !_offsets.ContainsKey(number))
                                _offsets[number] = (int)at;
                        }
                    }
                    var trailer = parser.Read() as Dictionary<string, object>;
                    if (trailer == null)
                        return false;
                    if (Trailer == null)
                        Trailer = trailer;
                    offset = (int)Number(Get(trailer, "Prev"), 0);
                }
                return _offsets.Count > 0;
            }
            catch (Exception error) when (error is FormatException || error is OverflowException || error is InvalidCastException)
            {
                return false;
            }
        }

        /// <summary>Запасной путь, если таблица объектов испорчена: ищем «N 0 obj» по всему файлу.</summary>
        private void ScanObjects()
        {
            _offsets.Clear();
            Trailer = null;
            var text = Encoding.GetEncoding(28591).GetString(_data);
            foreach (Match match in Regex.Matches(text, @"(?<![\d])(\d+)\s+\d+\s+obj\b"))
                _offsets[int.Parse(match.Groups[1].Value)] = match.Index;
            var trailerAt = text.LastIndexOf("trailer", StringComparison.Ordinal);
            if (trailerAt >= 0)
                Trailer = new PdfParser(_data, trailerAt + 7).Read() as Dictionary<string, object>;
            if (Trailer == null)
            {
                // Нет trailer (таблица в виде потока): ищем каталог документа сами.
                foreach (var number in _offsets.Keys.ToList())
                {
                    if (Name(Get(Dict(GetObject(number)), "Type")) == "Catalog")
                    {
                        Trailer = new Dictionary<string, object> { ["Root"] = new PdfRef(number) };
                        break;
                    }
                }
            }
        }
    }

    public sealed class PdfPage
    {
        public PdfPage(Dictionary<string, object> dict, Dictionary<string, object> resources, double height)
        {
            Dict = dict;
            Resources = resources;
            Height = height;
        }

        public Dictionary<string, object> Dict { get; }
        public Dictionary<string, object> Resources { get; }
        public double Height { get; }
    }
}
