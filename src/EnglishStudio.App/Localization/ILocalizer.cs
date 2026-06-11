using System.ComponentModel;
using EnglishStudio.Modules.Dictionary.Images;

namespace EnglishStudio.App.Localization;

/// <summary>
/// Central UI-string provider. Strings live in <c>Strings.resx</c> (neutral = Russian) and
/// <c>Strings.en.resx</c>. Implements <see cref="INotifyPropertyChanged"/> on its indexer so that
/// <c>{loc:Tr Key}</c> bindings re-read every value when the language changes — the whole UI
/// switches live, without an app restart.
/// </summary>
public interface ILocalizer : INotifyPropertyChanged
{
    /// <summary>Looks up a key in the current language. Missing keys return the key itself.</summary>
    string this[string key] { get; }

    /// <summary><see cref="string.Format(IFormatProvider, string, object[])"/> over the looked-up template.</summary>
    string Format(string key, params object[] args);

    /// <summary>The currently active interface language.</summary>
    AppLanguage Current { get; }

    /// <summary>Switches language: updates the thread cultures and notifies all <c>{loc:Tr}</c> bindings.</summary>
    void SetLanguage(AppLanguage language);
}
