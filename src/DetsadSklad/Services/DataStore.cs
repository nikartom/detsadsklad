using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml;
using DetsadSklad.Models;

namespace DetsadSklad.Services
{
    /// <summary>
    /// Хранит данные в одном файле. Запись атомарная (сначала во временный файл),
    /// раз в день делается копия: даже при отключении света данные не пропадут.
    /// </summary>
    public class DataStore
    {
        private const int BackupsToKeep = 30;
        private const int SaveAttempts = 3;
        private const int SaveRetryDelayMs = 200;
        private static readonly DataContractSerializer Serializer = new DataContractSerializer(typeof(AppData));

        public string DataDir { get; }
        public string DataFile => Path.Combine(DataDir, "data.xml");
        public string SpareFile => Path.Combine(DataDir, "data.xml.bak");
        public string BackupDir => Path.Combine(DataDir, "Копии");
        public string DocumentsDir => Path.Combine(DataDir, "Заявки");
        public string LockFile => Path.Combine(DataDir, "программа-открыта.lock");
        public string LogFile => Path.Combine(DataDir, "ошибки.log");

        public AppData Data { get; private set; }

        /// <summary>Если основной файл был повреждён — откуда восстановили данные.</summary>
        public string RecoveredFrom { get; private set; }

        public DataStore(string dataDir)
        {
            DataDir = dataDir;
            Directory.CreateDirectory(DataDir);
        }

        public void Load()
        {
            if (!File.Exists(DataFile) && !File.Exists(SpareFile) && !BackupFiles().Any())
            {
                Data = SeedData.Create();
                Save();
                return;
            }

            if (TryRead(DataFile, out var data))
            {
                var upgraded = DataMigrations.Upgrade(data);
                Data = data;
                MakeDailyBackup();
                if (upgraded)
                    Save();
                return;
            }

            // Основной файл не читается: не трогаем его, берём самую свежую копию.
            foreach (var candidate in new[] { SpareFile }.Concat(BackupFiles()))
            {
                if (!TryRead(candidate, out data))
                    continue;
                if (File.Exists(DataFile))
                    File.Move(DataFile, Path.Combine(DataDir, $"data.повреждён.{DateTime.Now:yyyyMMdd-HHmmss}.xml"));
                DataMigrations.Upgrade(data);
                Data = data;
                RecoveredFrom = candidate;
                Save();
                return;
            }

            throw new InvalidDataException("Файл с данными повреждён, и подходящей копии не нашлось: " + DataFile);
        }

        public void Save()
        {
            var tempFile = DataFile + ".tmp";
            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
            {
                Serializer.WriteObject(writer, Data);
            }

            ReplaceDataFile(tempFile);
        }

        // Антивирус может на мгновение занять файл: пробуем ещё пару раз, прежде чем беспокоить пользователя.
        private void ReplaceDataFile(string tempFile)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(DataFile))
                        File.Replace(tempFile, DataFile, SpareFile);
                    else
                        File.Move(tempFile, DataFile);
                    return;
                }
                catch (Exception error) when (attempt < SaveAttempts && (error is IOException || error is UnauthorizedAccessException))
                {
                    Thread.Sleep(SaveRetryDelayMs);
                }
            }
        }

        public void LogError(Exception error)
        {
            try
            {
                File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {error}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Если не удалось записать журнал, пользователь всё равно увидит сообщение об ошибке.
            }
        }

        private static bool TryRead(string path, out AppData data)
        {
            data = null;
            if (!File.Exists(path))
                return false;
            try
            {
                using (var stream = File.OpenRead(path))
                    data = Serializer.ReadObject(stream) as AppData;
                return data != null;
            }
            catch (SerializationException) { return false; }
            catch (XmlException) { return false; }
            catch (IOException) { return false; }
        }

        private string[] BackupFiles()
        {
            if (!Directory.Exists(BackupDir))
                return new string[0];
            return Directory.GetFiles(BackupDir, "data-*.xml").OrderByDescending(f => f, StringComparer.Ordinal).ToArray();
        }

        private void MakeDailyBackup()
        {
            Directory.CreateDirectory(BackupDir);
            var todayBackup = Path.Combine(BackupDir, $"data-{DateTime.Now:yyyy-MM-dd}.xml");
            if (!File.Exists(todayBackup))
                File.Copy(DataFile, todayBackup);
            foreach (var old in BackupFiles().Skip(BackupsToKeep))
                File.Delete(old);
        }
    }
}
