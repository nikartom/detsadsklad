using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DetsadSklad.Models;

namespace DetsadSklad.Services
{
    /// <summary>
    /// Файлы заявок лежат в папках: Заявки\Поставщик\Контракт\Заявка №7 от 30.10.2026.docx.
    /// Файл создаётся заново, только если заявка изменилась, — правки, сделанные в Word, не теряются.
    /// </summary>
    public class DocumentService
    {
        private readonly DataStore _store;

        public DocumentService(DataStore store)
        {
            _store = store;
        }

        public string GetPath(SupplyRequest request)
        {
            return Path.Combine(
                _store.DocumentsDir,
                CleanFileName(request.SupplierShortName),
                "Контракт " + CleanFileName(request.ContractNumber),
                GetFileName(request));
        }

        public static string GetFileName(SupplyRequest request) =>
            CleanFileName($"Заявка №{request.Number} от {RuFormat.ShortDate(request.SentDate)}") + ".docx";

        /// <summary>Возвращает готовый файл заявки. IOException — файл сейчас открыт в Word.</summary>
        public string EnsureDocument(SupplyRequest request)
        {
            var path = GetPath(request);
            if (File.Exists(path))
            {
                var writtenAt = File.GetLastWriteTimeUtc(path);
                if (writtenAt >= request.UpdatedAtUtc && writtenAt >= _store.Data.DocumentsValidFromUtc)
                    return path;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                RequestDocumentBuilder.Build(request, _store.Data.Organization.HighlightVariableFields, file);
            return path;
        }

        /// <summary>Убирает файл заявки вместе с опустевшими папками.</summary>
        public void DeleteDocument(SupplyRequest request)
        {
            var path = GetPath(request);
            if (File.Exists(path))
                File.Delete(path);
            var folder = Path.GetDirectoryName(path);
            while (folder != null && folder.Length > _store.DocumentsDir.Length && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
                folder = Path.GetDirectoryName(folder);
            }
        }

        public void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        public void Print(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "print" });

        public void ShowInFolder(string path) => Process.Start("explorer.exe", $"/select,\"{path}\"");

        public void OpenFolder(string folder)
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }

        private static string CleanFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string((name ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim(' ', '.');
            return cleaned.Length == 0 ? "Без названия" : cleaned;
        }
    }
}
