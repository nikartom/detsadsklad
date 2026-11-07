using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using DetsadSklad.Models;
using DetsadSklad.Services;

namespace DetsadSklad.Tests
{
    /// <summary>Проверки логики без экрана. Запускаются из build.ps1, код выхода 1 — что-то сломалось.</summary>
    internal static class Program
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static int _failed;
        private static int _passed;

        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            // Логика не должна зависеть от языка Windows: прогоняем под чужой культурой.
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            Run(nameof(DocumentMatchesSample), DocumentMatchesSample);
            Run(nameof(DocumentEscapesAndDropsEmptyEmail), DocumentEscapesAndDropsEmptyEmail);
            Run(nameof(DocumentHighlightCanBeTurnedOff), DocumentHighlightCanBeTurnedOff);
            Run(nameof(DocumentIsValidWordPackage), DocumentIsValidWordPackage);
            Run(nameof(NumbersAreFormattedInRussian), NumbersAreFormattedInRussian);
            Run(nameof(DecimalInputIsForgiving), DecimalInputIsForgiving);
            Run(nameof(NumberingContinuesPerContract), NumberingContinuesPerContract);
            Run(nameof(ContractIsPickedByDeliveryDate), ContractIsPickedByDeliveryDate);
            Run(nameof(NextWorkdaySkipsWeekend), NextWorkdaySkipsWeekend);
            Run(nameof(SeveralContractsAreNotGuessed), SeveralContractsAreNotGuessed);
            Run(nameof(FinishedContractsAreNotOffered), FinishedContractsAreNotOffered);
            Run(nameof(NewContractStartsOnSigningDay), NewContractStartsOnSigningDay);
            Run(nameof(RenewalKeepsNoteAndCategories), RenewalKeepsNoteAndCategories);
            Run(nameof(ContractOffersProductsOfItsCategories), ContractOffersProductsOfItsCategories);
            Run(nameof(PastedLinesBecomeOneLine), PastedLinesBecomeOneLine);
            Run(nameof(ContractNoteAndCategoriesAreSaved), ContractNoteAndCategoriesAreSaved);
            Run(nameof(SearchFindsByAnyField), SearchFindsByAnyField);
            Run(nameof(StoreRoundTrip), StoreRoundTrip);
            Run(nameof(StoreRecoversFromCorruptFile), StoreRecoversFromCorruptFile);
            Run(nameof(StoreKeepsCorruptFileWhenNoBackup), StoreKeepsCorruptFileWhenNoBackup);
            Run(nameof(DocumentFileIsKeptUntilRequestChanges), DocumentFileIsKeptUntilRequestChanges);
            Run(nameof(ProductDocumentNameIsBuiltFromFields), ProductDocumentNameIsBuiltFromFields);
            Run(nameof(EmptyProductFieldsStayOutOfDocument), EmptyProductFieldsStayOutOfDocument);
            Run(nameof(OldDescriptionsAreSplitIntoFields), OldDescriptionsAreSplitIntoFields);
            Run(nameof(OldDataFileIsUpgradedOnLoad), OldDataFileIsUpgradedOnLoad);
            Run(nameof(PackagingIsBuiltFromCheckboxAndChoice), PackagingIsBuiltFromCheckboxAndChoice);
            Run(nameof(PreviousVersionIsUpgradedToPackagingCheckbox), PreviousVersionIsUpgradedToPackagingCheckbox);
            Run(nameof(ChoicesCombineDefaultsWithUsedValues), ChoicesCombineDefaultsWithUsedValues);
            Run(nameof(DefaultsContainNoCatalogData), DefaultsContainNoCatalogData);
            Run(nameof(InitialDataFileIsReadAndUpgraded), InitialDataFileIsReadAndUpgraded);
            Run(nameof(PdfTextIsReadWithPositions), PdfTextIsReadWithPositions);
            Run(nameof(ContractIsReadFromPdf), ContractIsReadFromPdf);
            Run(nameof(OtherContractsAreNotGuessed), OtherContractsAreNotGuessed);
            Run(nameof(ContractImportCreatesSupplierAndProducts), ContractImportCreatesSupplierAndProducts);
            Run(nameof(NextQuarterContractUpdatesPrices), NextQuarterContractUpdatesPrices);
            Run(nameof(ScreensOpenWithoutErrors), ScreensOpenWithoutErrors);
            // Настоящие контракты в репозиторий не кладутся: проверка запускается, только если указать папку с ними.
            var realContracts = Environment.GetEnvironmentVariable("DETSAD_CONTRACTS");
            if (!string.IsNullOrEmpty(realContracts))
                Run(nameof(RealContractsAreReadCompletely), () => RealContractsAreReadCompletely(realContracts));

            Console.WriteLine($"Пройдено: {_passed}, не пройдено: {_failed}");
            return _failed == 0 ? 0 : 1;
        }

        private static void DocumentMatchesSample()
        {
            var data = TestData.Create();
            var text = ReadParagraphs(data.Requests[0], highlight: true);

            AssertContains(text, "Детский сад №1");
            AssertContains(text, "e–mail: sad1@example.com");
            AssertContains(text, "Общество с ограниченной ответственностью \"Василёк\" (ООО \"Василёк\")");
            AssertContains(text, "Российская Федерация, 000001, с. Примерное, ул. Школьная, 1");
            AssertContains(text, "e–mail: office@example.com");
            AssertContains(text, "Заявка на поставку товара №7");
            AssertContains(text, "К Контракту от «24» сентября 2026г. № 2026.ТЕСТ.1");
            AssertContains(text, "Адрес поставки Товара: г. Примерск, ул. Тестовая, д. 1.");
            AssertContains(text, "Дата поставки:  02.11.2026 ");
            AssertContains(text, "Время поставки: с 08:00 до 15:00 часов местного времени");
            AssertContains(text, "________________________/И.И. Иванова/");
            AssertContains(text, "Дата направления заявки:  30.10.2026");

            // Строки товаров: как в образце, до копейки.
            var expected = new[]
            {
                new[] { "1.", "Литр; Кубический дециметр", "100", "65,00", "6500,00" },
                new[] { "2.", "кг", "3,6", "250,00", "900,00" },
                new[] { "3.", "кг", "8", "890,00", "7120,00" },
                new[] { "4.", "кг", "5", "239,00", "1195,00" }
            };
            var rows = ReadTableRows(data.Requests[0]);
            AssertEqual(7, rows.Count, "шапка, нумерация колонок, 4 товара и пустая строка");
            for (var i = 0; i < expected.Length; i++)
            {
                var row = rows[i + 2];
                AssertEqual(expected[i][0], row[0], "№");
                AssertEqual(expected[i][1], row[2], "ед.");
                AssertEqual(expected[i][2], row[3], "кол-во");
                AssertEqual(expected[i][3], row[4], "цена");
                AssertEqual(expected[i][4], row[5], "стоимость");
            }
            AssertEqual(true, rows[2][1].StartsWith("Молоко ГОСТ"), "наименование первой строки");
            AssertEqual("", string.Concat(rows[6]), "последняя строка пустая, как в образце");
            AssertEqual(false, string.Concat(text).Contains("{{"), "не осталось незаполненных полей");
        }

        private static void DocumentEscapesAndDropsEmptyEmail()
        {
            var request = TestData.Create().Requests[0];
            request.SupplierFullName = "ООО <Ромашка> & Партнёры";
            request.SupplierEmail = "";
            var text = ReadParagraphs(request, highlight: true);
            AssertContains(text, "ООО <Ромашка> & Партнёры");
            AssertEqual(1, text.Count(p => p.StartsWith("e–mail")), "строка с пустой почтой поставщика убрана");
        }

        private static void DocumentHighlightCanBeTurnedOff()
        {
            var request = TestData.Create().Requests[0];
            AssertEqual(true, ReadXml(request, true).Descendants(W + "color").Any(), "с выделением");
            AssertEqual(false, ReadXml(request, false).Descendants(W + "color").Any(), "без выделения");
        }

        private static void DocumentIsValidWordPackage()
        {
            var request = TestData.Create().Requests[0];
            using (var stream = new MemoryStream())
            {
                RequestDocumentBuilder.Build(request, true, stream);
                stream.Position = 0;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var names = zip.Entries.Select(e => e.FullName).ToList();
                    foreach (var part in new[] { "[Content_Types].xml", "_rels/.rels", "word/document.xml", "word/styles.xml" })
                        AssertEqual(true, names.Contains(part), part);
                }
            }
        }

        private static void NumbersAreFormattedInRussian()
        {
            AssertEqual("6500,00", RuFormat.DocMoney(6500m), "деньги");
            AssertEqual("3,6", RuFormat.Quantity(3.6m), "дробное кол-во");
            AssertEqual("100", RuFormat.Quantity(100m), "целое кол-во");
            AssertEqual("15 715,00 руб.", RuFormat.Money(15715m), "деньги на экране");
            AssertEqual("«05» марта 2026", RuFormat.LongDate(new DateTime(2026, 3, 5)), "дата прописью");
        }

        private static void DecimalInputIsForgiving()
        {
            foreach (var input in new[] { "3,6", "3.6", " 3,6 " })
            {
                AssertEqual(true, RuFormat.TryParseDecimal(input, out var value), input);
                AssertEqual(3.6m, value, input);
            }
            AssertEqual(true, RuFormat.TryParseDecimal("1 200", out var thousands), "с пробелом");
            AssertEqual(1200m, thousands, "с пробелом");
            AssertEqual(false, RuFormat.TryParseDecimal("", out _), "пусто");
            AssertEqual(false, RuFormat.TryParseDecimal("два", out _), "слово");
            AssertEqual(5.04m, new RequestLine { Quantity = 2.8m, Price = 1.8m }.Cost, "стоимость");
            AssertEqual(0.13m, new RequestLine { Quantity = 0.25m, Price = 0.5m }.Cost, "округление копеек вверх");
        }

        private static void NumberingContinuesPerContract()
        {
            var data = TestData.Create();
            var contractId = data.Requests[0].ContractId;
            AssertEqual(8, RequestLogic.NextNumber(data, contractId), "после заявки №7");
            AssertEqual(1, RequestLogic.NextNumber(data, Guid.NewGuid()), "новый контракт начинается с 1");
            AssertEqual(true, RequestLogic.IsNumberTaken(data, contractId, 7, null), "номер занят");
            AssertEqual(false, RequestLogic.IsNumberTaken(data, contractId, 7, data.Requests[0].Id), "своя заявка не мешает");
        }

        private static void ContractIsPickedByDeliveryDate()
        {
            var supplier = new Supplier();
            var q3 = new Contract { Number = "Q3", SignedOn = new DateTime(2026, 6, 25), ValidFrom = new DateTime(2026, 7, 1), ValidTo = new DateTime(2026, 9, 30) };
            var q4 = new Contract { Number = "Q4", SignedOn = new DateTime(2026, 9, 24), ValidFrom = new DateTime(2026, 10, 1), ValidTo = new DateTime(2026, 12, 31) };
            supplier.Contracts.Add(q3);
            supplier.Contracts.Add(q4);
            AssertEqual("Q3", RequestLogic.PickContract(supplier, new DateTime(2026, 8, 1)).Number, "август");
            AssertEqual("Q4", RequestLogic.PickContract(supplier, new DateTime(2026, 11, 2)).Number, "ноябрь");
            AssertEqual(null, RequestLogic.PickContract(supplier, new DateTime(2027, 2, 1)), "нет действующего — не подставляем");
            AssertEqual(null, RequestLogic.PickContract(new Supplier(), DateTime.Today), "контрактов нет");
        }

        private static void SeveralContractsAreNotGuessed()
        {
            var supplier = new Supplier();
            var groceryQ3 = Quarter("Б-3", "Бакалея", 2026, 7);
            var grocery = Quarter("Б-4", "Бакалея", 2026, 10);
            var frozen = Quarter("З-4", "Заморозка", 2026, 10);
            supplier.Contracts.AddRange(new[] { groceryQ3, grocery, frozen });
            var november = new DateTime(2026, 11, 2);

            AssertEqual(null, RequestLogic.PickContract(supplier, november), "два действующих — пусть выберет пользователь");
            AssertEqual("Б-4", RequestLogic.PickContract(supplier, november, groceryQ3).Number, "повтор заявки — продолжение бакалеи");
            AssertEqual("З-4", RequestLogic.PickContract(supplier, november, frozen).Number, "та же заморозка");
            AssertEqual(true, grocery.DisplayName.EndsWith("· Бакалея"), "примечание видно в списке: " + grocery.DisplayName);
        }

        private static void FinishedContractsAreNotOffered()
        {
            var supplier = new Supplier();
            supplier.Contracts.Add(Quarter("Q3", "", 2026, 7));
            supplier.Contracts.Add(Quarter("Q4", "", 2026, 10));
            supplier.Contracts.Add(Quarter("Q1", "", 2027, 1));
            var open = RequestLogic.OpenContracts(supplier, new DateTime(2026, 11, 2)).Select(c => c.Number).ToList();
            AssertEqual("Q4,Q1", string.Join(",", open), "завершённый не предлагается, будущий — да");
            AssertEqual(true, supplier.Contracts[0].IsFinished(new DateTime(2026, 10, 1)), "срок вышел");
            AssertEqual(false, supplier.Contracts[0].IsFinished(new DateTime(2026, 9, 30)), "последний день ещё действует");
        }

        private static void NewContractStartsOnSigningDay()
        {
            var contract = RequestLogic.SuggestNewContract(new DateTime(2026, 11, 2));
            AssertEqual(new DateTime(2026, 11, 2), contract.SignedOn, "заключён сегодня");
            AssertEqual(contract.SignedOn, contract.ValidFrom, "действует со дня заключения");
            AssertEqual(new DateTime(2026, 12, 31), contract.ValidTo, "до конца квартала");
        }

        private static void RenewalKeepsNoteAndCategories()
        {
            var previous = Quarter("Б-4", "Бакалея", 2026, 10);

            var late = RequestLogic.SuggestRenewal(previous, new DateTime(2027, 1, 12));
            AssertEqual("Бакалея", late.Note, "примечание");
            AssertEqual(new DateTime(2027, 1, 12), late.ValidFrom, "после окончания — со дня заключения");
            AssertEqual(new DateTime(2027, 3, 31), late.ValidTo, "до конца квартала");
            AssertEqual(null, late.Number, "номер новый");

            var early = RequestLogic.SuggestRenewal(previous, new DateTime(2026, 12, 24));
            AssertEqual(new DateTime(2027, 1, 1), early.ValidFrom, "действует, когда закончится прежний");
            AssertEqual(new DateTime(2027, 3, 31), early.ValidTo, "следующий квартал");
        }

        private static void ContractOffersProductsOfItsCategories()
        {
            var data = new AppData();
            var supplier = new Supplier();
            var other = new Supplier();
            data.Suppliers.Add(supplier);
            data.Suppliers.Add(other);
            data.Products.Add(new Product { Name = "Гречка", Category = "Бакалея", SupplierId = supplier.Id });
            data.Products.Add(new Product { Name = "Пельмени", Category = "Заморозка", SupplierId = supplier.Id });
            data.Products.Add(new Product { Name = "Соль", Category = "", SupplierId = supplier.Id });
            data.Products.Add(new Product { Name = "Рис", Category = "бакалея ", SupplierId = other.Id });
            data.Products.Add(new Product { Name = "Сахар", Category = "Бакалея", SupplierId = null });

            var grocery = Quarter("Б", " бакалея", 2026, 10);
            AssertEqual("Гречка,Соль,Сахар", Names(RequestLogic.ProductsFor(data, supplier, grocery)), "бакалея + без категории + общий");
            AssertEqual(true, RequestLogic.FiltersByCategory(data, supplier, grocery), "отбор по категории");

            var noNote = Quarter("В", "", 2026, 10);
            AssertEqual("Гречка,Пельмени,Соль,Сахар", Names(RequestLogic.ProductsFor(data, supplier, noNote)), "без примечания — всё поставщика");
            var unknown = Quarter("Г", "Молочка", 2026, 10);
            AssertEqual("Гречка,Пельмени,Соль,Сахар", Names(RequestLogic.ProductsFor(data, supplier, unknown)), "категории ещё не проставлены — ничего не прячем");
            AssertEqual(false, RequestLogic.FiltersByCategory(data, supplier, unknown), "нет отбора");
            AssertEqual("Рис,Сахар", Names(RequestLogic.ProductsFor(data, other, grocery)), "регистр и пробелы не мешают");
        }

        private static readonly string CrLf = new string(new[] { (char)13, (char)10 });

        private static void PastedLinesBecomeOneLine()
        {
            AssertEqual("660000, г. Красноярск, ул. Ленина, д. 1, оф. 2",
                TextTools.ToSingleLine("660000, г. Красноярск," + CrLf + "  ул. Ленина, д. 1," + CrLf + CrLf + "оф. 2" + CrLf), "адрес из контракта");
            AssertEqual("а б", TextTools.ToSingleLine("а" + (char)10 + "б"), "переводы строк Unix");
            AssertEqual("без  переноса", TextTools.ToSingleLine("без  переноса"), "строку без переносов не трогаем");
            AssertEqual(null, TextTools.ToSingleLine(null), "пусто");
        }

        private static void ContractNoteAndCategoriesAreSaved()
        {
            WithTempDir(dir =>
            {
                var store = new DataStore(dir);
                store.Load();
                TestData.Fill(store.Data);
                store.Data.Suppliers[0].Contracts[0].Note = "Молочная продукция";
                store.Data.Products[0].Category = "Молочная продукция";
                store.Save();

                var again = new DataStore(dir);
                again.Load();
                AssertEqual("Молочная продукция", again.Data.Suppliers[0].Contracts[0].Note, "примечание");
                AssertEqual("Молочная продукция", again.Data.Products[0].Category, "категория продукта");
                AssertEqual(true, ProductChoices.Categories(again.Data).Contains("Молочная продукция"), "примечание предлагается как категория");
            });
        }

        private static Contract Quarter(string number, string note, int year, int firstMonth)
        {
            var from = new DateTime(year, firstMonth, 1);
            return new Contract { Number = number, Note = note, SignedOn = from, ValidFrom = from, ValidTo = from.AddMonths(3).AddDays(-1) };
        }

        private static string Names(IEnumerable<Product> products) => string.Join(",", products.Select(p => p.Name));

        private static void NextWorkdaySkipsWeekend()
        {
            AssertEqual(new DateTime(2026, 11, 2), RequestLogic.NextWorkday(new DateTime(2026, 10, 30)), "пятница → понедельник");
            AssertEqual(new DateTime(2026, 11, 3), RequestLogic.NextWorkday(new DateTime(2026, 11, 2)), "понедельник → вторник");
        }



        private static void SearchFindsByAnyField()
        {
            var request = TestData.Create().Requests[0];
            foreach (var query in new[] { "", "7", "№7", "василёк", "ВАСИЛЁК", "2026.ТЕСТ", "молоко", "творог василёк", "30.10.2026", "ГОСТ" })
                AssertEqual(true, RequestLogic.MatchesSearch(request, query), "найти: " + query);
            foreach (var query in new[] { "ромашка", "99", "молоко ромашка" })
                AssertEqual(false, RequestLogic.MatchesSearch(request, query), "не найти: " + query);
        }

        private static void StoreRoundTrip()
        {
            WithTempDir(dir =>
            {
                var store = new DataStore(dir);
                store.Load();
                TestData.Fill(store.Data);

                var data = store.Data;
                data.Requests[0].Lines[1].Quantity = 4.25m;
                data.Organization.EmployeeName = "Иванова Мария Петровна";
                store.Save();

                var again = new DataStore(dir);
                again.Load();
                AssertEqual(4.25m, again.Data.Requests[0].Lines[1].Quantity, "дробное количество");
                AssertEqual("Иванова Мария Петровна", again.Data.Organization.EmployeeName, "сотрудник");
                AssertEqual(new DateTime(2026, 11, 2), again.Data.Requests[0].DeliveryDate, "дата");
                AssertEqual(again.Data.Suppliers[0].Id, again.Data.Requests[0].SupplierId, "связь с поставщиком");
                AssertEqual(1, Directory.GetFiles(again.BackupDir).Length, "копия за день");
                AssertEqual(true, File.Exists(again.SpareFile), "запасной файл");
                AssertEqual(false, File.Exists(again.DataFile + ".tmp"), "временный файл убран");
            });
        }

        private static void StoreRecoversFromCorruptFile()
        {
            WithTempDir(dir =>
            {
                var store = new DataStore(dir);
                store.Load();
                store.Data.Organization.EmployeeName = "До сбоя";
                store.Save();
                store.Save();
                File.WriteAllText(store.DataFile, "<AppData><оборвалось");

                var recovered = new DataStore(dir);
                recovered.Load();
                AssertEqual("До сбоя", recovered.Data.Organization.EmployeeName, "взято из запасного файла");
                AssertEqual(recovered.SpareFile, recovered.RecoveredFrom, "источник");
                AssertEqual(1, Directory.GetFiles(dir, "data.повреждён.*").Length, "повреждённый файл сохранён");
            });
        }

        private static void StoreKeepsCorruptFileWhenNoBackup()
        {
            WithTempDir(dir =>
            {
                var store = new DataStore(dir);
                File.WriteAllText(store.DataFile, "мусор");
                try
                {
                    store.Load();
                    throw new Exception("ожидалась ошибка");
                }
                catch (InvalidDataException)
                {
                    AssertEqual("мусор", File.ReadAllText(store.DataFile), "файл не перезаписан начальными данными");
                }
            });
        }

        private static void DocumentFileIsKeptUntilRequestChanges()
        {
            WithTempDir(dir =>
            {
                var store = new DataStore(dir);
                store.Load();
                TestData.Fill(store.Data);
                var documents = new DocumentService(store);
                var request = store.Data.Requests[0];

                var path = documents.EnsureDocument(request);
                AssertEqual(true, path.EndsWith(Path.Combine("ООО Василёк", "Контракт 2026.ТЕСТ.1", "Заявка №7 от 30.10.2026.docx")), path);

                // Правка в Word сохраняется, пока сама заявка не менялась.
                File.AppendAllText(path, "");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
                var written = File.GetLastWriteTimeUtc(path);
                documents.EnsureDocument(request);
                AssertEqual(written, File.GetLastWriteTimeUtc(path), "файл не пересоздан");

                request.UpdatedAtUtc = DateTime.UtcNow.AddMinutes(2);
                documents.EnsureDocument(request);
                AssertEqual(true, File.GetLastWriteTimeUtc(path) != written, "заявка изменилась — файл обновлён");

                documents.DeleteDocument(request);
                AssertEqual(false, Directory.Exists(Path.Combine(store.DocumentsDir, "ООО Василёк")), "пустые папки убраны");
            });
        }

        private static void ProductDocumentNameIsBuiltFromFields()
        {
            var butter = TestData.Create().Products.First(p => p.Name.StartsWith("Масло"));
            AssertEqual(
                "Масло сливочное ГОСТ, МДЖ не менее 72,5 %. Тип: Несоленое. Сорт: Высший. Пригодно для питания детей (воспитанников). "
                + "Упаковка индивидуальная от производителя не более 5 кг. Страна происхождения-Российская Федерация",
                butter.DocumentName, "масло");
        }

        private static void EmptyProductFieldsStayOutOfDocument()
        {
            AssertEqual("Гречка ядрица", new Product { Name = "Гречка ядрица" }.DocumentName, "только наименование");
            AssertEqual("Гречка ядрица. Сорт: первый. Упаковка мешок 25 кг",
                new Product { Name = "Гречка ядрица.", Grade = "первый", Packaging = "мешок 25 кг." }.DocumentName, "без лишних точек");
        }

        private static void OldDescriptionsAreSplitIntoFields()
        {
            var butter = Split("Масло сливочное ГОСТ, МДЖ не менее 72,5 %. Тип сливочного масла - Несоленое, Сорт Высший. Пригодно для питания детей (воспитанников). Упаковка индивидуальная от производителя не более 5 кг. Страна происхождения-Российская Федерация");
            AssertEqual("Масло сливочное ГОСТ, МДЖ не менее 72,5 %", butter.Name, "масло: наименование");
            AssertEqual("Несоленое", butter.Kind, "масло: тип");
            AssertEqual("Высший", butter.Grade, "масло: сорт");
            AssertEqual("", butter.Features, "масло: особенности");
            AssertEqual(true, butter.SuitableForChildren, "масло: для детей");
            AssertEqual("индивидуальная от производителя не более 5 кг", butter.Packaging, "масло: упаковка");
            AssertEqual("Российская Федерация", butter.Country, "масло: страна");

            var milk = Split("Молоко ГОСТ, МДЖ не менее 2,5 %. Тип продукта: коровье пастеризованное из натурального молока. Без обогащающих компонентов и красителей. Пригодно для питания детей (воспитанников). Упаковка индивидуальная от производителя ПЭТ (пленка/бутылка) объемом до 1л. Страна происхождения-Российская Федерация");
            AssertEqual("Молоко ГОСТ, МДЖ не менее 2,5 %", milk.Name, "молоко: наименование");
            AssertEqual("коровье пастеризованное из натурального молока", milk.Kind, "молоко: тип");
            AssertEqual("Без обогащающих компонентов и красителей", milk.Features, "молоко: особенности");
            AssertEqual("индивидуальная от производителя ПЭТ (пленка/бутылка) объемом до 1л", milk.Packaging, "молоко: упаковка");

            var cream = Split("Сметана ГОСТ, МДЖ 15 %. Из нормализованных сливок, без консервантов и красителей. Пригодно для питания детей (воспитанников). Упаковка индивидуальная от производителя массой до 0,5 кг. Страна происхождения-Российская Федерация");
            AssertEqual("Сметана ГОСТ, МДЖ 15 %", cream.Name, "сметана: наименование");
            AssertEqual("Из нормализованных сливок, без консервантов и красителей", cream.Features, "сметана: особенности");
            AssertEqual("индивидуальная от производителя массой до 0,5 кг", cream.Packaging, "сметана: упаковка");

            var curd = Split("Творог ГОСТ, МДЖ - 5% из коровьего нормализованного молока; без постороннего вкуса и запаха, упаковка индивидуальная от производителя - массой до 1 кг. Страна происхождения-Российская Федерация");
            AssertEqual("Творог ГОСТ, МДЖ - 5%", curd.Name, "творог: наименование");
            AssertEqual("Из коровьего нормализованного молока; без постороннего вкуса и запаха", curd.Features, "творог: особенности");
            AssertEqual(false, curd.SuitableForChildren, "творог: в исходном тексте нет про детей");
            AssertEqual("индивидуальная от производителя - массой до 1 кг", curd.Packaging, "творог: упаковка");

            var free = Split("Гречка ядрица первого сорта");
            AssertEqual("Гречка ядрица первого сорта", free.Name, "свободный текст не теряется");
        }

        private static void OldDataFileIsUpgradedOnLoad()
        {
            WithTempDir(dir =>
            {
                var store = new DataStore(dir);
                File.WriteAllText(store.DataFile,
                    "<AppData xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\"><Organization><Name>Сад</Name></Organization>"
                    + "<Products><Product><Description>Сметана ГОСТ, МДЖ 15 %. Упаковка до 0,5 кг</Description><Id>06ddf7a3-410f-4685-a886-f3ff426602dc</Id><Name>Сметана</Name><Price>250</Price><Unit>кг</Unit></Product></Products>"
                    + "<Requests><SupplyRequest><Lines><RequestLine><Description>Старый текст заявки</Description><Name>Сметана</Name><Price>250</Price><Quantity>2</Quantity><Unit>кг</Unit></RequestLine></Lines><Number>3</Number></SupplyRequest></Requests>"
                    + "<SchemaVersion>1</SchemaVersion></AppData>");
                store.Load();

                var product = store.Data.Products.Single();
                AssertEqual("Сметана ГОСТ, МДЖ 15 %", product.Name, "наименование");
                AssertEqual("до 0,5 кг", product.Packaging, "упаковка");
                AssertEqual(false, product.IndividualPackaging, "упаковка: без галочки");
                AssertEqual(250m, product.Price, "цена");
                AssertEqual(null, product.LegacyDescription, "старое поле очищено");
                AssertEqual("Старый текст заявки", store.Data.Requests.Single().Lines.Single().Description, "оформленная заявка не меняется");
                AssertEqual(AppData.CurrentSchemaVersion, store.Data.SchemaVersion, "версия");

                var again = new DataStore(dir);
                again.Load();
                AssertEqual("Сметана ГОСТ, МДЖ 15 %", again.Data.Products.Single().Name, "перенос записан в файл");
                AssertEqual(1, Directory.GetFiles(again.BackupDir).Length, "копия старого файла сделана до переноса");
                AssertEqual(true, File.ReadAllText(Directory.GetFiles(again.BackupDir).Single()).Contains("<SchemaVersion>1<"), "в копии старый формат");
            });
        }

        private static void PackagingIsBuiltFromCheckboxAndChoice()
        {
            AssertEqual("Соль. Упаковка индивидуальная от производителя массой до 1 кг",
                new Product { Name = "Соль", IndividualPackaging = true, Packaging = "массой до 1 кг" }.DocumentName, "галочка и размер");
            AssertEqual("Соль. Упаковка индивидуальная от производителя",
                new Product { Name = "Соль", IndividualPackaging = true }.DocumentName, "только галочка");
            AssertEqual("Соль. Упаковка мешок 25 кг",
                new Product { Name = "Соль", Packaging = "мешок 25 кг" }.DocumentName, "только размер");
            AssertEqual("Соль", new Product { Name = "Соль" }.DocumentName, "нет упаковки");
        }

        private static void PreviousVersionIsUpgradedToPackagingCheckbox()
        {
            var data = new AppData { SchemaVersion = 2 };
            data.Products.Add(new Product { Name = "Творог", Packaging = "индивидуальная от производителя - массой до 1 кг" });
            data.Products.Add(new Product { Name = "Масло", Packaging = "индивидуальная от производителя не более 5 кг" });
            data.Products.Add(new Product { Name = "Гречка", Packaging = "мешок 25 кг" });
            data.Products.Add(new Product { Name = "Соль", Packaging = "индивидуальная от производителя" });

            AssertEqual(true, DataMigrations.Upgrade(data), "есть что обновлять");
            AssertEqual(true, data.Products[0].IndividualPackaging, "творог: галочка");
            AssertEqual("массой до 1 кг", data.Products[0].Packaging, "творог: тире убрано");
            AssertEqual("не более 5 кг", data.Products[1].Packaging, "масло");
            AssertEqual(false, data.Products[2].IndividualPackaging, "гречка: без галочки");
            AssertEqual("мешок 25 кг", data.Products[2].Packaging, "гречка");
            AssertEqual(true, data.Products[3].IndividualPackaging, "соль: галочка");
            AssertEqual("", data.Products[3].Packaging, "соль: подробностей нет");
            AssertEqual(false, DataMigrations.Upgrade(data), "повторно не обновляется");
        }

        private static void ChoicesCombineDefaultsWithUsedValues()
        {
            var data = new AppData();
            data.Products.Add(new Product { Name = "А", Country = "Армения", Grade = "высший" });
            data.Products.Add(new Product { Name = "Б", Country = " армения ", Grade = "" });
            data.Products.Add(new Product { Name = "В", Country = "Российская Федерация" });

            var countries = ProductChoices.Countries(data);
            AssertEqual("Российская Федерация", countries[0], "стандартные сначала");
            AssertEqual(1, countries.Count(c => c == "Российская Федерация"), "без повтора стандартной");
            AssertEqual(1, countries.Count(c => c.Trim().Equals("армения", StringComparison.OrdinalIgnoreCase)), "регистр и пробелы не дают дублей");
            AssertEqual(false, ProductChoices.Grades(data).Contains("высший"), "«высший» совпадает с «Высший»");
            AssertEqual(false, ProductChoices.Grades(data).Contains(""), "пустых нет");

            data.Products.RemoveAt(0);
            data.Products.RemoveAt(0);
            AssertEqual(false, ProductChoices.Countries(data).Any(c => c.Contains("рмения")), "неиспользуемое значение исчезает");
        }

        private static void DefaultsContainNoCatalogData()
        {
            var data = SeedData.CreateDefaults();
            AssertEqual(0, data.Suppliers.Count + data.Products.Count + data.Requests.Count, "справочники пусты");
            AssertEqual(true, string.IsNullOrEmpty(data.Organization.Name), "название не задано");
            AssertEqual(true, data.Organization.HighlightVariableFields, "выделение как в образце");
            AssertEqual(AppData.CurrentSchemaVersion, data.SchemaVersion, "версия");
        }

        private static void InitialDataFileIsReadAndUpgraded()
        {
            WithTempDir(dir =>
            {
                var store = new DataStore(dir);
                store.Load();
                TestData.Fill(store.Data);
                store.Save();
                using (var file = File.OpenRead(store.DataFile))
                {
                    var data = SeedData.Read(file);
                    AssertEqual("Детский сад №1", data.Organization.Name, "реквизиты");
                    AssertEqual(4, data.Products.Count, "продукты");
                    AssertEqual(AppData.CurrentSchemaVersion, data.SchemaVersion, "версия");
                }
            });
        }

        private static void PdfTextIsReadWithPositions()
        {
            var pdf = new TestPdf().NewPage()
                .Text(100, 50, "Привет, мир")
                .Kerned(100, 70, "Ёлка", -2000, "ель")
                .VLine(300, 40, 200)
                .NewPage()
                .Text(60, 30, "Вторая страница")
                .Build();
            var content = DetsadSklad.Services.Pdf.PdfText.Read(pdf);
            var runs = content.Runs;
            AssertEqual("Привет, мир", runs[0].Text, "текст через ToUnicode");
            AssertEqual(100.0, Math.Round(runs[0].X, 1), "x");
            AssertEqual(50.0, Math.Round(runs[0].Y, 1), "y сверху страницы");
            AssertEqual(144.0, Math.Round(runs[0].Right, 1), "ширина: 11 букв по 4 пункта");
            var kerned = runs.Single(r => r.Text == "ель");
            AssertEqual(132.0, Math.Round(kerned.X, 1), "отступ TJ сдвигает следующее слово");
            AssertEqual(2, runs.Single(r => r.Text == "Вторая страница").Page, "номер страницы");
            var line = content.Segments.Single(s => s.IsVertical);
            AssertEqual(300.0, Math.Round(line.X1, 1), "линия таблицы");
            AssertEqual(40.0, Math.Round(line.Top, 1), "верх линии");
            var lines = DetsadSklad.Services.Pdf.PdfText.Lines(runs);
            AssertEqual("Ёлка  ель", lines[1].Text, "далеко стоящие куски — разные колонки");
        }

        private static void ContractIsReadFromPdf()
        {
            var contract = ContractPdfReader.Read(TestContract.Build());
            AssertEqual(TestContract.Number, contract.Number, "номер");
            AssertEqual("Молочная продукция", contract.Note, "что поставляет — из скобок под названием");
            AssertEqual(new DateTime(2026, 9, 21), contract.SignedOn, "заключён — по последней подписи");
            AssertEqual(new DateTime(2026, 10, 1), contract.ValidFrom, "срок с");
            AssertEqual(new DateTime(2026, 12, 31), contract.ValidTo, "срок по");
            AssertEqual(1250.50m, contract.Price, "цена контракта");
            AssertEqual("ООО \"Ромашка\"", contract.SupplierShortName, "короткое название — из скобок");
            AssertEqual("ОБЩЕСТВО С ОГРАНИЧЕННОЙ ОТВЕТСТВЕННОСТЬЮ \"РОМАШКА\" (ООО \"Ромашка\")", contract.SupplierFullName, "полное название");
            AssertEqual(TestContract.Inn, contract.SupplierInn, "ИНН поставщика, а не заказчика");
            AssertEqual("Российская Федерация, 000000, г. Примерск, ул. Полевая, 5", contract.SupplierAddress, "адрес без подписи поля");
            AssertEqual("romashka@example.com", contract.SupplierEmail, "почта");
            AssertEqual("+70000000000", contract.SupplierPhone, "телефон");

            AssertEqual(2, contract.Items.Count, "позиции");
            var milk = contract.Items[0];
            AssertEqual(TestContract.FirstItem, milk.Description, "описание: строки склеены, перенос «450-600» без пробела");
            AssertEqual("10.51.11.111", milk.Okpd, "код ОКПД2");
            AssertEqual("Литр; Кубический дециметр", milk.Unit, "единица без вылезшего слова соседней колонки");
            AssertEqual(10m, milk.Quantity, "количество");
            AssertEqual(65.50m, milk.Price, "цена");
            AssertEqual(655m, milk.Sum, "сумма");
            AssertEqual(TestContract.SecondItem, contract.Items[1].Description, "позиция, перешедшая на следующую страницу");
            AssertEqual("Килограмм", contract.Items[1].Unit, "единица второй позиции");
            AssertEqual(true, contract.ItemsAreComplete, "сумма позиций равна цене контракта");

            var broken = ContractPdfReader.Read(TestContract.Build(contractPrice: "9999.00"));
            AssertEqual(false, broken.ItemsAreComplete, "не сошлось с ценой — пользователь увидит предупреждение");
        }

        private static void OtherContractsAreNotGuessed()
        {
            AssertEqual("КУЗБАСС TRADE", ContractPdfReader.Read(TestContract.Build()).ProfileName, "профиль площадки");
            AssertEqual("КУЗБАСС TRADE", ContractPdfReader.SupportedNames, "поддерживается одна площадка");
            var other = new TestPdf().NewPage().Text(100, 50, "КОНТРАКТ № 5 на поставку").Text(100, 70, "Спецификация").Build();
            try
            {
                ContractPdfReader.Read(other);
                throw new Exception("контракт другой площадки прочитан наугад");
            }
            catch (UnsupportedContractException error)
            {
                AssertEqual(true, error.Message.Contains("КУЗБАСС TRADE"), "в сообщении сказано, что поддерживается");
            }
        }

        private static void ContractImportCreatesSupplierAndProducts()
        {
            var data = new AppData();
            var contract = ContractPdfReader.Read(TestContract.Build());
            AssertEqual(null, ContractImport.FindSupplier(data, contract), "поставщика ещё нет");
            AssertEqual(null, ContractImport.FindProblem(null, contract), "всё заполнено");

            var supplier = ContractImport.Apply(data, contract, null);
            AssertEqual(1, data.Suppliers.Count, "поставщик добавлен");
            AssertEqual(TestContract.Inn, supplier.Inn, "ИНН запомнен");
            AssertEqual("romashka@example.com", supplier.Email, "почта");
            var saved = supplier.Contracts.Single();
            AssertEqual("Молочная продукция", saved.Note, "что поставляет");
            AssertEqual(new DateTime(2026, 9, 21), saved.SignedOn, "заключён");
            AssertEqual(new DateTime(2026, 10, 1), saved.ValidFrom, "действует с — из срока поставки");
            AssertEqual(2, data.Products.Count, "продукты добавлены");
            var milk = data.Products[0];
            AssertEqual("Молоко питьевое ГОСТ", milk.Name, "наименование — первая фраза");
            AssertEqual(TestContract.FirstItem.TrimEnd('.'), milk.DocumentName, "в заявке текст как в контракте");
            AssertEqual("Литр; Кубический дециметр", milk.Unit, "единица");
            AssertEqual("Молочная продукция", milk.Category, "категория = что поставляет контракт");
            AssertEqual(2, RequestLogic.ProductsFor(data, supplier, saved).Count(), "продукты предлагаются в заявке по этому контракту");

            var again = ContractPdfReader.Read(TestContract.Build());
            AssertEqual(true, ContractImport.FindProblem(supplier, again) != null, "тот же контракт второй раз не загружается");

            // Поставщик, заведённый вручную без ИНН, узнаётся по названию с другими кавычками.
            var manual = new AppData();
            manual.Suppliers.Add(new Supplier { ShortName = "ООО «Ромашка»", FullName = "что-то другое" });
            AssertEqual(manual.Suppliers[0], ContractImport.FindSupplier(manual, contract), "узнан по названию");
        }

        private static void NextQuarterContractUpdatesPrices()
        {
            var data = new AppData();
            var supplier = ContractImport.Apply(data, ContractPdfReader.Read(TestContract.Build()), null);
            var next = ContractPdfReader.Read(TestContract.Build(number: "2027.ТЕСТ.1", firstPrice: "70,00", firstSum: "700,00", contractPrice: "1295.50"));
            AssertEqual(true, next.ItemsAreComplete, "следующий контракт прочитан");
            AssertEqual(supplier, ContractImport.FindSupplier(data, next), "поставщик узнан по ИНН");
            AssertEqual(null, ContractImport.FindProblem(supplier, next), "другой номер — можно загружать");

            ContractImport.Apply(data, next, supplier);
            AssertEqual(1, data.Suppliers.Count, "поставщик не задвоился");
            AssertEqual(2, supplier.Contracts.Count, "добавлен второй контракт");
            AssertEqual(2, data.Products.Count, "продукты не задвоились");
            AssertEqual(70m, data.Products[0].Price, "цена обновилась");
            AssertEqual(119.10m, data.Products[1].Price, "неизменная цена осталась");
        }

        private static void RealContractsAreReadCompletely(string folder)
        {
            var problems = new List<string>();
            var files = Directory.GetFiles(folder, "*.pdf");
            foreach (var path in files)
            {
                var contract = ContractPdfReader.Read(File.ReadAllBytes(path));
                var missing = new[]
                {
                    contract.Number == null ? "номер" : null, contract.Note == null ? "что поставляет" : null,
                    contract.SignedOn == null ? "дата заключения" : null, contract.ValidTo == null ? "срок" : null,
                    contract.SupplierShortName == null ? "поставщик" : null, contract.SupplierInn == null ? "ИНН" : null,
                    contract.SupplierAddress == null ? "адрес" : null, contract.SupplierEmail == null ? "почта" : null,
                    contract.ItemsAreComplete ? null : "позиции не сошлись с ценой"
                }.Where(m => m != null).ToList();
                // Печатаем только имя файла и чего не хватает — реквизиты в журнал не попадают.
                Console.WriteLine($"       {Path.GetFileName(path)}: позиций {contract.Items.Count}" + (missing.Count == 0 ? ", всё прочитано" : ", нет: " + string.Join(", ", missing)));
                if (missing.Count > 0)
                    problems.Add(Path.GetFileName(path));
            }
            AssertEqual(true, files.Length > 0, "в папке есть PDF");
            AssertEqual("", string.Join(", ", problems), "контракты прочитаны не полностью");
        }

        private static void ScreensOpenWithoutErrors()
        {
            WithTempDir(dir =>
            {
                var problems = ViewSmoke.Run(dir);
                if (problems.Count > 0)
                    throw new Exception("ошибки на экранах:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
            });
        }

        private static Product Split(string description)
        {
            var product = new Product { Name = "Короткое" };
            DataMigrations.SplitDescription(product, description);
            return product;
        }

        private static XDocument ReadXml(SupplyRequest request, bool highlight)
        {
            using (var stream = new MemoryStream())
            {
                RequestDocumentBuilder.Build(request, highlight, stream);
                stream.Position = 0;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                using (var part = zip.GetEntry("word/document.xml").Open())
                    return XDocument.Load(part);
            }
        }

        private static List<string> ReadParagraphs(SupplyRequest request, bool highlight) =>
            ReadXml(request, highlight).Descendants(W + "p").Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value))).ToList();

        private static List<string[]> ReadTableRows(SupplyRequest request)
        {
            var table = ReadXml(request, true).Descendants(W + "tbl").ElementAt(1);
            return table.Elements(W + "tr")
                .Select(tr => tr.Elements(W + "tc").Select(tc => string.Concat(tc.Descendants(W + "t").Select(t => t.Value))).ToArray())
                .ToList();
        }

        private static void WithTempDir(Action<string> test)
        {
            var dir = Path.Combine(Path.GetTempPath(), "DetsadSkladTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                test(dir);
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("  ok   " + name);
            }
            catch (Exception error)
            {
                _failed++;
                Console.WriteLine("  FAIL " + name + ": " + error.Message);
            }
        }

        private static void AssertContains(List<string> paragraphs, string expected)
        {
            if (!paragraphs.Contains(expected))
                throw new Exception($"нет абзаца «{expected}»");
        }

        private static void AssertEqual<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{what}: ожидалось «{expected}», получено «{actual}»");
        }
    }
}
