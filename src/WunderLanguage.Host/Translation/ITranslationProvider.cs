using System.Threading.Tasks;

namespace WunderLanguage.Host.Translation
{
    public interface ITranslationProvider
    {
        string Name { get; }
        Task<string> TranslateAsync(string text, string sourceLang, string targetLang, string? context = null);
    }
}
