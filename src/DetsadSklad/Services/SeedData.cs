using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using DetsadSklad.Models;

namespace DetsadSklad.Services
{
    /// <summary>
    /// Данные при первом запуске. Если при сборке рядом лежал файл Assets/InitialData.local.xml
    /// (он не хранится в git), программа стартует с ним, иначе — пустой.
    /// </summary>
    public static class SeedData
    {
        private const string InitialDataResource = "DetsadSklad.InitialData.xml";

        public static AppData Create()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(InitialDataResource))
                return stream == null ? CreateDefaults() : Read(stream);
        }

        public static AppData CreateDefaults() => new AppData
        {
            Organization = new Organization
            {
                DefaultDeliveryTime = "с 08:00 до 15:00 часов",
                HighlightVariableFields = true
            }
        };

        public static AppData Read(Stream stream)
        {
            var data = (AppData)new DataContractSerializer(typeof(AppData)).ReadObject(stream);
            DataMigrations.Upgrade(data);
            return data;
        }
    }
}
