using System;
using DetsadSklad.Models;
using DetsadSklad.Services;

namespace DetsadSklad.Tests
{
    /// <summary>Вымышленные реквизиты для проверок.</summary>
    internal static class TestData
    {
        public static AppData Create()
        {
            var now = DateTime.UtcNow;
            var data = new AppData
            {
                Organization = new Organization
                {
                    Name = "Детский сад №1",
                    Address = "000000, г. Примерск, ул. Тестовая, 1;",
                    Email = "sad1@example.com",
                    DeliveryAddress = "г. Примерск, ул. Тестовая, д. 1.",
                    DefaultDeliveryTime = "с 08:00 до 15:00 часов",
                    SignerName = "И.И. Иванова",
                    EmployeeName = "",
                    HighlightVariableFields = true
                }
            };

            var contract = new Contract
            {
                Number = "2026.ТЕСТ.1",
                SignedOn = new DateTime(2026, 9, 24),
                ValidFrom = new DateTime(2026, 10, 1),
                ValidTo = new DateTime(2026, 12, 31)
            };
            var supplier = new Supplier
            {
                ShortName = "ООО \"Василёк\"",
                FullName = "Общество с ограниченной ответственностью \"Василёк\" (ООО \"Василёк\")",
                Address = "Российская Федерация, 000001, с. Примерное, ул. Школьная, 1",
                Email = "office@example.com"
            };
            supplier.Contracts.Add(contract);
            data.Suppliers.Add(supplier);

            var milk = AddProduct(data, supplier, "Молоко ГОСТ, МДЖ не менее 2,5 %", "Литр; Кубический дециметр", 65m,
                "коровье пастеризованное из натурального молока", "", "Без обогащающих компонентов и красителей", true,
                "ПЭТ (пленка/бутылка) объемом до 1л");
            var sourCream = AddProduct(data, supplier, "Сметана ГОСТ, МДЖ 15 %", "кг", 250m,
                "", "", "Из нормализованных сливок, без консервантов и красителей", true,
                "массой до 0,5 кг");
            var butter = AddProduct(data, supplier, "Масло сливочное ГОСТ, МДЖ не менее 72,5 %", "кг", 890m,
                "Несоленое", "Высший", "", true,
                "не более 5 кг");
            var curd = AddProduct(data, supplier, "Творог ГОСТ, МДЖ - 5%", "кг", 239m,
                "", "", "Из коровьего нормализованного молока; без постороннего вкуса и запаха", false,
                "массой до 1 кг");

            var request = new SupplyRequest
            {
                Number = 7,
                DeliveryDate = new DateTime(2026, 11, 2),
                SentDate = new DateTime(2026, 10, 30),
                DeliveryTime = data.Organization.DefaultDeliveryTime,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            RequestLogic.CopyOrganization(request, data.Organization);
            RequestLogic.CopySupplier(request, supplier, contract);
            request.Lines.Add(RequestLogic.CreateLine(milk, 100m));
            request.Lines.Add(RequestLogic.CreateLine(sourCream, 3.6m));
            request.Lines.Add(RequestLogic.CreateLine(butter, 8m));
            request.Lines.Add(RequestLogic.CreateLine(curd, 5m));
            data.Requests.Add(request);

            return data;
        }

        /// <summary>Заполняет уже загруженные данные: результат не зависит от того, с чем собрана программа.</summary>
        public static void Fill(AppData target)
        {
            var source = Create();
            target.Organization = source.Organization;
            target.Suppliers = source.Suppliers;
            target.Products = source.Products;
            target.Requests = source.Requests;
        }

        private static Product AddProduct(AppData data, Supplier supplier, string name, string unit, decimal price,
            string kind, string grade, string features, bool suitableForChildren, string packaging)
        {
            var product = new Product
            {
                Name = name,
                Unit = unit,
                Price = price,
                Kind = kind,
                Grade = grade,
                Features = features,
                SuitableForChildren = suitableForChildren,
                IndividualPackaging = true,
                Packaging = packaging,
                Country = "Российская Федерация",
                SupplierId = supplier.Id
            };
            data.Products.Add(product);
            return product;
        }
    }
}
