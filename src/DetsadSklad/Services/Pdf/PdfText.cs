using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DetsadSklad.Services.Pdf
{
    /// <summary>Кусок текста на странице. Координаты в пунктах, Y отсчитывается сверху страницы.</summary>
    public sealed class PdfTextRun
    {
        public int Page { get; set; }
        public double X { get; set; }
        public double Right { get; set; }
        public double Y { get; set; }
        public double Size { get; set; }
        public string Text { get; set; }
        public override string ToString() => $"p{Page} x{X:0} y{Y:0} «{Text}»";
    }

    /// <summary>Нарисованный отрезок (линия таблицы). Y отсчитывается сверху страницы.</summary>
    public sealed class PdfSegment
    {
        public int Page { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public bool IsVertical => Math.Abs(X1 - X2) < 0.6 && Math.Abs(Y1 - Y2) >= 3;
        public double Top => Math.Min(Y1, Y2);
        public double Bottom => Math.Max(Y1, Y2);
    }

    /// <summary>Текст и линии всех страниц.</summary>
    public sealed class PdfContent
    {
        public List<PdfTextRun> Runs { get; } = new List<PdfTextRun>();
        public List<PdfSegment> Segments { get; } = new List<PdfSegment>();
    }

    /// <summary>Строка страницы: куски текста слева направо. Между далеко отстоящими кусками — колонки таблицы.</summary>
    public sealed class PdfLine
    {
        public int Page { get; set; }
        public double Y { get; set; }
        public List<PdfTextRun> Runs { get; } = new List<PdfTextRun>();
        public string Text => string.Join("  ", Runs.Select(r => r.Text));
        public override string ToString() => $"p{Page} y{Y:0}: {Text}";
    }

    /// <summary>Достаёт из PDF текст вместе с положением на странице.</summary>
    public static class PdfText
    {
        public static List<PdfTextRun> ReadRuns(byte[] pdf) => Read(pdf).Runs;

        public static PdfContent Read(byte[] pdf)
        {
            var doc = new PdfDocument(pdf);
            var result = new PdfContent();
            var pageNumber = 0;
            foreach (var page in doc.Pages())
            {
                pageNumber++;
                byte[] content;
                try
                {
                    content = doc.PageContent(page);
                }
                catch (NotSupportedException)
                {
                    continue;
                }
                new ContentReader(doc, pageNumber, page.Height, result).Run(content, page.Resources, Matrix.Identity, 0);
            }
            return result;
        }

        /// <summary>
        /// Собирает куски в строки. Соседние куски склеиваются в одну фразу, если между ними меньше
        /// <paramref name="columnGap"/> кеглей; больший промежуток — граница колонки.
        /// </summary>
        public static List<PdfLine> Lines(IEnumerable<PdfTextRun> runs, double columnGap = 1.2)
        {
            var lines = new List<PdfLine>();
            foreach (var pageRuns in runs.Where(r => r.Text.Trim().Length > 0).GroupBy(r => r.Page).OrderBy(g => g.Key))
            {
                var pageLines = new List<List<PdfTextRun>>();
                foreach (var run in pageRuns.OrderBy(r => r.Y).ThenBy(r => r.X))
                {
                    var line = pageLines.LastOrDefault();
                    var tolerance = Math.Max(1.5, run.Size * 0.35);
                    if (line != null && Math.Abs(line[0].Y - run.Y) <= tolerance)
                        line.Add(run);
                    else
                        pageLines.Add(new List<PdfTextRun> { run });
                }
                foreach (var raw in pageLines)
                {
                    var line = new PdfLine { Page = pageRuns.Key, Y = raw.Average(r => r.Y) };
                    PdfTextRun current = null;
                    foreach (var run in raw.OrderBy(r => r.X))
                    {
                        var gap = current == null ? double.MaxValue : run.X - current.Right;
                        var size = Math.Max(1, Math.Max(run.Size, current?.Size ?? 0));
                        if (current != null && gap < columnGap * size)
                        {
                            var needsSpace = gap > size * 0.15 && !current.Text.EndsWith(" ") && !run.Text.StartsWith(" ");
                            current.Text += (needsSpace ? " " : "") + run.Text;
                            current.Right = Math.Max(current.Right, run.Right);
                        }
                        else
                        {
                            current = new PdfTextRun { Page = run.Page, X = run.X, Right = run.Right, Y = run.Y, Size = run.Size, Text = run.Text };
                            line.Runs.Add(current);
                        }
                    }
                    foreach (var run in line.Runs)
                        run.Text = CollapseSpaces(run.Text);
                    lines.Add(line);
                }
            }
            return lines;
        }

        private static string CollapseSpaces(string text)
        {
            var result = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                var ch = c == ' ' ? ' ' : c;
                if (ch == ' ' && (result.Length == 0 || result[result.Length - 1] == ' '))
                    continue;
                result.Append(ch);
            }
            return result.ToString().Trim();
        }

        /// <summary>Выполняет команды страницы и записывает, где какой текст напечатан.</summary>
        private sealed class ContentReader
        {
            private const int MaxFormDepth = 8;
            private readonly PdfDocument _doc;
            private readonly int _page;
            private readonly double _pageHeight;
            private readonly List<PdfTextRun> _runs;
            private readonly List<PdfSegment> _segments;
            private readonly Dictionary<object, PdfFont> _fonts = new Dictionary<object, PdfFont>();
            // Текущий контур: отрезки в координатах страницы, ещё не нарисованные.
            private readonly List<PdfSegment> _path = new List<PdfSegment>();
            private double _pathX, _pathY, _startX, _startY;

            public ContentReader(PdfDocument doc, int page, double pageHeight, PdfContent content)
            {
                _doc = doc;
                _page = page;
                _pageHeight = pageHeight;
                _runs = content.Runs;
                _segments = content.Segments;
            }

            public void Run(byte[] content, Dictionary<string, object> resources, Matrix ctm, int depth)
            {
                var state = new State { Ctm = ctm };
                var stack = new Stack<State>();
                var textMatrix = Matrix.Identity;
                var lineMatrix = Matrix.Identity;
                var operands = new List<object>();
                var parser = new PdfParser(content, 0);

                while (true)
                {
                    var item = parser.Read();
                    if (item == PdfParser.End)
                        break;
                    if (!(item is PdfKeyword op))
                    {
                        operands.Add(item);
                        continue;
                    }
                    try
                    {
                        switch (op.Value)
                        {
                            case "q": stack.Push(state.Clone()); break;
                            case "Q": if (stack.Count > 0) state = stack.Pop(); break;
                            case "cm": state.Ctm = Matrix.From(operands).Multiply(state.Ctm); break;
                            case "BT": textMatrix = lineMatrix = Matrix.Identity; break;
                            case "Tf":
                                state.Font = FontFor(resources, operands[0] as PdfName);
                                state.FontSize = Num(operands, 1);
                                break;
                            case "Tc": state.CharSpacing = Num(operands, 0); break;
                            case "Tw": state.WordSpacing = Num(operands, 0); break;
                            case "Tz": state.Scale = Num(operands, 0) / 100.0; break;
                            case "TL": state.Leading = Num(operands, 0); break;
                            case "Ts": state.Rise = Num(operands, 0); break;
                            case "Td":
                                lineMatrix = Matrix.Translate(Num(operands, 0), Num(operands, 1)).Multiply(lineMatrix);
                                textMatrix = lineMatrix;
                                break;
                            case "TD":
                                state.Leading = -Num(operands, 1);
                                lineMatrix = Matrix.Translate(Num(operands, 0), Num(operands, 1)).Multiply(lineMatrix);
                                textMatrix = lineMatrix;
                                break;
                            case "Tm":
                                textMatrix = lineMatrix = Matrix.From(operands);
                                break;
                            case "T*":
                                lineMatrix = Matrix.Translate(0, -state.Leading).Multiply(lineMatrix);
                                textMatrix = lineMatrix;
                                break;
                            case "Tj":
                                textMatrix = Show(state, textMatrix, (operands.LastOrDefault() as PdfString)?.Bytes);
                                break;
                            case "'":
                                lineMatrix = Matrix.Translate(0, -state.Leading).Multiply(lineMatrix);
                                textMatrix = Show(state, lineMatrix, (operands.LastOrDefault() as PdfString)?.Bytes);
                                break;
                            case "\"":
                                state.WordSpacing = Num(operands, 0);
                                state.CharSpacing = Num(operands, 1);
                                lineMatrix = Matrix.Translate(0, -state.Leading).Multiply(lineMatrix);
                                textMatrix = Show(state, lineMatrix, (operands.LastOrDefault() as PdfString)?.Bytes);
                                break;
                            case "TJ":
                                foreach (var part in operands.LastOrDefault() as List<object> ?? new List<object>())
                                {
                                    if (part is PdfString text)
                                        textMatrix = Show(state, textMatrix, text.Bytes);
                                    else if (part is double adjust)
                                        textMatrix = Matrix.Translate(-adjust / 1000.0 * state.FontSize * state.Scale, 0).Multiply(textMatrix);
                                }
                                break;
                            case "Do":
                                if (depth < MaxFormDepth)
                                    RunForm(resources, operands.LastOrDefault() as PdfName, state.Ctm, depth);
                                break;
                            case "BI":
                                parser.SkipInlineImage();
                                break;
                            case "m":
                                MoveTo(state, Num(operands, 0), Num(operands, 1));
                                break;
                            case "l":
                                LineTo(state, Num(operands, 0), Num(operands, 1));
                                break;
                            case "re":
                                {
                                    double x = Num(operands, 0), y = Num(operands, 1), w = Num(operands, 2), h = Num(operands, 3);
                                    MoveTo(state, x, y);
                                    LineTo(state, x + w, y);
                                    LineTo(state, x + w, y + h);
                                    LineTo(state, x, y + h);
                                    LineTo(state, x, y);
                                    break;
                                }
                            case "h":
                                _path.Add(Segment(_pathX, _pathY, _startX, _startY));
                                break;
                            case "S": case "s": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*":
                                _segments.AddRange(_path);
                                _path.Clear();
                                break;
                            case "n":
                                _path.Clear();
                                break;
                        }
                    }
                    catch (Exception error) when (error is ArgumentOutOfRangeException || error is InvalidCastException || error is NotSupportedException)
                    {
                        // Непонятная команда не должна мешать прочитать остальной текст.
                    }
                    operands.Clear();
                }
            }

            private void MoveTo(State state, double x, double y)
            {
                var point = new Matrix(1, 0, 0, 1, x, y).Multiply(state.Ctm);
                _pathX = _startX = point.E;
                _pathY = _startY = point.F;
            }

            private void LineTo(State state, double x, double y)
            {
                var point = new Matrix(1, 0, 0, 1, x, y).Multiply(state.Ctm);
                _path.Add(Segment(_pathX, _pathY, point.E, point.F));
                _pathX = point.E;
                _pathY = point.F;
            }

            private PdfSegment Segment(double x1, double y1, double x2, double y2) =>
                new PdfSegment { Page = _page, X1 = x1, Y1 = _pageHeight - y1, X2 = x2, Y2 = _pageHeight - y2 };

            private Matrix Show(State state, Matrix textMatrix, byte[] bytes)
            {
                if (bytes == null || state.Font == null)
                    return textMatrix;
                var text = new StringBuilder();
                double startX = 0, startY = 0, size = 0;
                var first = true;
                foreach (var glyph in state.Font.Decode(bytes))
                {
                    var rendering = new Matrix(state.FontSize * state.Scale, 0, 0, state.FontSize, 0, state.Rise)
                        .Multiply(textMatrix).Multiply(state.Ctm);
                    if (first)
                    {
                        startX = rendering.E;
                        startY = rendering.F;
                        size = Math.Sqrt(rendering.C * rendering.C + rendering.D * rendering.D);
                        first = false;
                    }
                    text.Append(glyph.Text);
                    var advance = (glyph.Width * state.FontSize + state.CharSpacing + (glyph.IsSpaceCode ? state.WordSpacing : 0)) * state.Scale;
                    textMatrix = Matrix.Translate(advance, 0).Multiply(textMatrix);
                }
                if (!first && text.Length > 0)
                {
                    var end = textMatrix.Multiply(state.Ctm);
                    _runs.Add(new PdfTextRun
                    {
                        Page = _page,
                        X = startX,
                        Right = Math.Max(startX, end.E),
                        Y = _pageHeight - startY,
                        Size = size,
                        Text = text.ToString()
                    });
                }
                return textMatrix;
            }

            private PdfFont FontFor(Dictionary<string, object> resources, PdfName name)
            {
                var fonts = _doc.Dict(PdfDocument.Get(resources, "Font"));
                var reference = PdfDocument.Get(fonts, name?.Value ?? "");
                if (reference == null)
                    return null;
                var key = reference is PdfRef r ? (object)r.Number : reference;
                if (!_fonts.TryGetValue(key, out var font))
                {
                    var dict = _doc.Dict(reference);
                    font = dict == null ? null : new PdfFont(_doc, dict);
                    _fonts[key] = font;
                }
                return font;
            }

            private void RunForm(Dictionary<string, object> resources, PdfName name, Matrix ctm, int depth)
            {
                var xobjects = _doc.Dict(PdfDocument.Get(resources, "XObject"));
                if (!(_doc.Resolve(PdfDocument.Get(xobjects, name?.Value ?? "")) is PdfStream form)
                    || _doc.Name(PdfDocument.Get(form.Dict, "Subtype")) != "Form")
                    return;
                var matrix = _doc.Array(PdfDocument.Get(form.Dict, "Matrix"));
                var formCtm = matrix != null && matrix.Count == 6 ? Matrix.From(matrix.Select(_doc.Resolve).ToList()).Multiply(ctm) : ctm;
                var formResources = _doc.Dict(PdfDocument.Get(form.Dict, "Resources")) ?? resources;
                Run(_doc.Decode(form), formResources, formCtm, depth + 1);
            }

            private static double Num(List<object> operands, int index) =>
                index < operands.Count && operands[index] is double value ? value : 0;
        }

        private sealed class State
        {
            public Matrix Ctm;
            public PdfFont Font;
            public double FontSize;
            public double CharSpacing;
            public double WordSpacing;
            public double Scale = 1;
            public double Leading;
            public double Rise;

            public State Clone() => (State)MemberwiseClone();
        }
    }

    /// <summary>Матрица преобразования PDF [a b c d e f].</summary>
    internal struct Matrix
    {
        public static readonly Matrix Identity = new Matrix(1, 0, 0, 1, 0, 0);

        public Matrix(double a, double b, double c, double d, double e, double f)
        {
            A = a; B = b; C = c; D = d; E = e; F = f;
        }

        public double A { get; }
        public double B { get; }
        public double C { get; }
        public double D { get; }
        public double E { get; }
        public double F { get; }

        public static Matrix Translate(double x, double y) => new Matrix(1, 0, 0, 1, x, y);

        public static Matrix From(List<object> values)
        {
            double V(int i) => i < values.Count && values[i] is double v ? v : 0;
            return new Matrix(V(0), V(1), V(2), V(3), V(4), V(5));
        }

        /// <summary>this × other — сначала это преобразование, затем other.</summary>
        public Matrix Multiply(Matrix o) => new Matrix(
            A * o.A + B * o.C, A * o.B + B * o.D,
            C * o.A + D * o.C, C * o.B + D * o.D,
            E * o.A + F * o.C + o.E, E * o.B + F * o.D + o.F);
    }
}
