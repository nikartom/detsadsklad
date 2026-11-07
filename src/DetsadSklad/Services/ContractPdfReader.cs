using System;
using System.Collections.Generic;
using System.Linq;
using DetsadSklad.Services.ContractProfiles;
using DetsadSklad.Services.Pdf;

namespace DetsadSklad.Services
{
    /// <summary>
    /// Профиль распознавания: как устроены контракты одной площадки. Новая площадка — новый профиль
    /// в списке <see cref="ContractPdfReader.Profiles"/>, остальная программа не меняется.
    /// </summary>
    public interface IContractProfile
    {
        /// <summary>Название площадки для пользователя: «КУЗБАСС TRADE».</summary>
        string Name { get; }

        /// <summary>Похож ли документ на контракт этой площадки.</summary>
        bool Matches(PdfContent content);

        ParsedContract Parse(PdfContent content);
    }

    /// <summary>Контракт не подходит ни под один профиль — такой вид контрактов пока не поддерживается.</summary>
    public class UnsupportedContractException : Exception
    {
        public UnsupportedContractException(string message) : base(message) { }
    }

    /// <summary>Читает PDF контракта: определяет площадку и разбирает контракт по её профилю.</summary>
    public static class ContractPdfReader
    {
        public static readonly IReadOnlyList<IContractProfile> Profiles = new IContractProfile[]
        {
            new KuzbassTradeProfile()
        };

        /// <summary>Поддерживаемые площадки через запятую — для подсказок и сообщений.</summary>
        public static string SupportedNames => string.Join(", ", Profiles.Select(p => p.Name));

        public static ParsedContract Read(byte[] pdf)
        {
            var content = PdfText.Read(pdf);
            var profile = Profiles.FirstOrDefault(p => p.Matches(content));
            if (profile == null)
                throw new UnsupportedContractException(
                    "Такой вид контракта программа пока не понимает. Сейчас поддерживаются контракты площадки " + SupportedNames + ".");
            var contract = profile.Parse(content);
            contract.ProfileName = profile.Name;
            return contract;
        }
    }
}
