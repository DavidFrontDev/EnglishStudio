using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.Modules.Dictionary.Images;

namespace EnglishStudio.App.Localization;

/// <summary>
/// Singleton <see cref="ILocalizer"/> backed by the embedded <c>Strings</c> resource set.
/// A single shared instance is used by both the DI container and the <c>{loc:Tr}</c> markup
/// extension / <see cref="Loc"/> facade, so a language switch from anywhere updates every binding.
/// </summary>
public sealed class LocalizationManager : ILocalizer
{
    /// <summary>The process-wide instance. DI resolves <see cref="ILocalizer"/> to this same object.</summary>
    public static LocalizationManager Instance { get; } = new();

    // Base name = "{RootNamespace}.{folder}.{file}". RootNamespace of EnglishStudio.App is
    // "EnglishStudio.App", the resx lives in Localization/, so this resolves the embedded set.
    private static readonly ResourceManager ResourceManager =
        new("EnglishStudio.App.Localization.Strings", typeof(LocalizationManager).Assembly);

    // Resource-lookup culture follows the selected language…
    private CultureInfo _culture = CultureInfo.GetCultureInfo("ru");
    private AppLanguage _current = AppLanguage.Ru;

    // …but number/date FORMATTING is pinned to a stable culture so IELTS band scores, percentages
    // etc. always render the same way in both languages (point decimals "6.5", "85%" — the IELTS
    // convention). en-US (not Invariant) is used because Invariant prints percents as "85 %".
    private static readonly CultureInfo FormatCulture = CultureInfo.GetCultureInfo("en-US");

    // Pseudo-localization QA mode: set env var ESTUDIO_PSEUDO_LOC=1 before launch to wrap every
    // localized string in ⟦…⟧. Anything on screen WITHOUT brackets is a hardcoded (un-localized)
    // literal that escaped the resx — a fast visual sweep for missed strings.
    private static readonly bool PseudoMode =
        Environment.GetEnvironmentVariable("ESTUDIO_PSEUDO_LOC") == "1";

    private LocalizationManager()
    {
        ToggleCommand = new RelayCommand(ToggleLanguage);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage Current => _current;

    /// <summary>Two-letter code for the title-bar switch, e.g. "RU" / "EN".</summary>
    public string CurrentCode => _current == AppLanguage.En ? "EN" : "RU";

    /// <summary>
    /// Persists the chosen language. The App wires this at startup (to <c>IAppSettings</c>); it is
    /// invoked only on an explicit user toggle, not on the initial startup apply.
    /// </summary>
    public Action<AppLanguage>? Persist { get; set; }

    /// <summary>Toggles RU↔EN — bound to the title-bar language button.</summary>
    public IRelayCommand ToggleCommand { get; }

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            string value;
            try
            {
                // Russian is the neutral resource set, so a "ru" lookup falls back to it; "en"
                // resolves the satellite assembly. Missing keys surface as the key (handy mid-migration).
                value = ResourceManager.GetString(key, _culture) ?? key;
            }
            catch (MissingManifestResourceException)
            {
                value = key;
            }
            return PseudoMode ? "⟦" + value + "⟧" : value;
        }
    }

    public string Format(string key, params object[] args)
    {
        var template = this[key];
        try { return string.Format(FormatCulture, template, args); }
        catch (FormatException) { return template; }
    }

    public void SetLanguage(AppLanguage language)
    {
        _current = language;
        _culture = language == AppLanguage.En
            ? CultureInfo.GetCultureInfo("en")
            : CultureInfo.GetCultureInfo("ru");

        // UI culture follows the language (framework resource lookups); formatting stays pinned to
        // FormatCulture so numbers/dates render identically in both languages.
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
        CultureInfo.DefaultThreadCurrentCulture = FormatCulture;
        Thread.CurrentThread.CurrentUICulture = _culture;
        Thread.CurrentThread.CurrentCulture = FormatCulture;

        // "Item[]" tells every indexer binding ({loc:Tr Key}) to re-evaluate → live UI refresh.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCode)));
    }

    private void ToggleLanguage()
    {
        var next = _current == AppLanguage.En ? AppLanguage.Ru : AppLanguage.En;
        SetLanguage(next);
        Persist?.Invoke(next);
    }
}
