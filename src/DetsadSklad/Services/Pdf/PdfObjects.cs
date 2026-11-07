using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DetsadSklad.Services.Pdf
{
    /// <summary>Имя PDF: /Font, /Type. Хранится без косой черты.</summary>
    public sealed class PdfName
    {
        public PdfName(string value) { Value = value; }
        public string Value { get; }
        public override string ToString() => "/" + Value;
    }

    /// <summary>Ссылка на объект: 12 0 R.</summary>
    public sealed class PdfRef
    {
        public PdfRef(int number) { Number = number; }
        public int Number { get; }
    }

    public sealed class PdfString
    {
        public PdfString(byte[] bytes) { Bytes = bytes; }
        public byte[] Bytes { get; }
    }

    /// <summary>Слово-команда: оператор потока содержимого (Tj, BT) или ключевое слово (obj, stream).</summary>
    public sealed class PdfKeyword
    {
        public PdfKeyword(string value) { Value = value; }
        public string Value { get; }
        public override string ToString() => Value;
    }

    public sealed class PdfStream
    {
        public PdfStream(Dictionary<string, object> dict, byte[] raw)
        {
            Dict = dict;
            Raw = raw;
        }

        public Dictionary<string, object> Dict { get; }
        public byte[] Raw { get; }
    }

    /// <summary>Разбор синтаксиса PDF: числа, строки, имена, массивы, словари, ссылки и команды.</summary>
    internal sealed class PdfParser
    {
        public static readonly object End = new object();
        private static readonly object ArrayEnd = new object();
        private static readonly object DictEnd = new object();

        private readonly byte[] _data;

        public PdfParser(byte[] data, int position)
        {
            _data = data;
            Position = position;
        }

        public int Position { get; set; }
        public bool AtEnd => Position >= _data.Length;

        public static bool IsWhite(byte b) => b == 0 || b == 9 || b == 10 || b == 12 || b == 13 || b == 32;

        private static bool IsDelimiter(byte b) =>
            b == '(' || b == ')' || b == '<' || b == '>' || b == '[' || b == ']' || b == '{' || b == '}' || b == '/' || b == '%';

        public void SkipWhite()
        {
            while (Position < _data.Length)
            {
                var b = _data[Position];
                if (IsWhite(b))
                {
                    Position++;
                }
                else if (b == '%')
                {
                    while (Position < _data.Length && _data[Position] != '\n' && _data[Position] != '\r')
                        Position++;
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>Следующий объект; <see cref="End"/> — данные кончились.</summary>
        public object Read()
        {
            var value = ReadAny();
            return value == ArrayEnd || value == DictEnd ? End : value;
        }

        private object ReadAny()
        {
            SkipWhite();
            if (AtEnd)
                return End;
            var b = _data[Position];
            switch (b)
            {
                case (byte)'/':
                    Position++;
                    return new PdfName(ReadRegular(decodeHex: true));
                case (byte)'(':
                    return ReadLiteralString();
                case (byte)'<':
                    if (Position + 1 < _data.Length && _data[Position + 1] == '<')
                    {
                        Position += 2;
                        return ReadDictionary();
                    }
                    return ReadHexString();
                case (byte)'>':
                    Position += Position + 1 < _data.Length && _data[Position + 1] == '>' ? 2 : 1;
                    return DictEnd;
                case (byte)'[':
                    Position++;
                    return ReadArray();
                case (byte)']':
                    Position++;
                    return ArrayEnd;
                case (byte)'{':
                case (byte)'}':
                case (byte)')':
                    Position++;
                    return new PdfKeyword(((char)b).ToString());
            }
            if (b == '+' || b == '-' || b == '.' || (b >= '0' && b <= '9'))
                return ReadNumberOrRef();
            var word = ReadRegular(decodeHex: false);
            switch (word)
            {
                case "true": return true;
                case "false": return false;
                case "null": return null;
                default: return new PdfKeyword(word);
            }
        }

        private string ReadRegular(bool decodeHex)
        {
            var text = new StringBuilder();
            while (Position < _data.Length && !IsWhite(_data[Position]) && !IsDelimiter(_data[Position]))
            {
                var b = _data[Position++];
                if (decodeHex && b == '#' && Position + 1 < _data.Length
                    && int.TryParse(Encoding.ASCII.GetString(_data, Position, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                {
                    text.Append((char)code);
                    Position += 2;
                    continue;
                }
                text.Append((char)b);
            }
            return text.ToString();
        }

        private object ReadNumberOrRef()
        {
            var start = Position;
            var word = ReadRegular(decodeHex: false);
            if (!double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return new PdfKeyword(word);
            // «12 0 R» — ссылка на объект.
            if (word.IndexOf('.') < 0 && word[0] != '-' && word[0] != '+')
            {
                var afterFirst = Position;
                SkipWhite();
                var genStart = Position;
                while (Position < _data.Length && _data[Position] >= '0' && _data[Position] <= '9')
                    Position++;
                if (Position > genStart)
                {
                    SkipWhite();
                    if (Position < _data.Length && _data[Position] == 'R'
                        && (Position + 1 >= _data.Length || IsWhite(_data[Position + 1]) || IsDelimiter(_data[Position + 1])))
                    {
                        Position++;
                        return new PdfRef((int)number);
                    }
                }
                Position = afterFirst;
            }
            return number;
        }

        private PdfString ReadLiteralString()
        {
            Position++;
            var bytes = new List<byte>();
            var depth = 1;
            while (Position < _data.Length)
            {
                var b = _data[Position++];
                if (b == '\\' && Position < _data.Length)
                {
                    var e = _data[Position++];
                    switch (e)
                    {
                        case (byte)'n': bytes.Add(10); break;
                        case (byte)'r': bytes.Add(13); break;
                        case (byte)'t': bytes.Add(9); break;
                        case (byte)'b': bytes.Add(8); break;
                        case (byte)'f': bytes.Add(12); break;
                        case (byte)'\r':
                            if (Position < _data.Length && _data[Position] == '\n')
                                Position++;
                            break;
                        case (byte)'\n':
                            break;
                        default:
                            if (e >= '0' && e <= '7')
                            {
                                var value = e - '0';
                                for (var i = 0; i < 2 && Position < _data.Length && _data[Position] >= '0' && _data[Position] <= '7'; i++)
                                    value = value * 8 + (_data[Position++] - '0');
                                bytes.Add((byte)value);
                            }
                            else
                            {
                                bytes.Add(e);
                            }
                            break;
                    }
                    continue;
                }
                if (b == '(')
                    depth++;
                else if (b == ')' && --depth == 0)
                    break;
                bytes.Add(b);
            }
            return new PdfString(bytes.ToArray());
        }

        private PdfString ReadHexString()
        {
            Position++;
            var bytes = new List<byte>();
            var high = -1;
            while (Position < _data.Length)
            {
                var b = _data[Position++];
                if (b == '>')
                    break;
                var digit = HexValue(b);
                if (digit < 0)
                    continue;
                if (high < 0)
                {
                    high = digit;
                }
                else
                {
                    bytes.Add((byte)(high * 16 + digit));
                    high = -1;
                }
            }
            if (high >= 0)
                bytes.Add((byte)(high * 16));
            return new PdfString(bytes.ToArray());
        }

        private static int HexValue(byte b)
        {
            if (b >= '0' && b <= '9') return b - '0';
            if (b >= 'a' && b <= 'f') return b - 'a' + 10;
            if (b >= 'A' && b <= 'F') return b - 'A' + 10;
            return -1;
        }

        private List<object> ReadArray()
        {
            var list = new List<object>();
            while (true)
            {
                var item = ReadAny();
                if (item == ArrayEnd || item == End)
                    return list;
                if (item != DictEnd)
                    list.Add(item);
            }
        }

        private Dictionary<string, object> ReadDictionary()
        {
            var dict = new Dictionary<string, object>();
            while (true)
            {
                var key = ReadAny();
                if (key == DictEnd || key == End)
                    return dict;
                if (!(key is PdfName name))
                    continue;
                var value = ReadAny();
                if (value == DictEnd || value == End)
                    return dict;
                dict[name.Value] = value;
            }
        }

        /// <summary>Пропускает двоичные данные встроенной картинки: от ID до EI.</summary>
        public void SkipInlineImage()
        {
            Position++;
            for (; Position + 2 < _data.Length; Position++)
            {
                if (IsWhite(_data[Position]) && _data[Position + 1] == 'E' && _data[Position + 2] == 'I'
                    && (Position + 3 >= _data.Length || IsWhite(_data[Position + 3])))
                {
                    Position += 3;
                    return;
                }
            }
            Position = _data.Length;
        }
    }
}
