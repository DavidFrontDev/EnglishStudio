namespace EnglishStudio.Modules.Dictionary.Localization;

/// <summary>
/// Localizes user-facing messages produced inside the modules (progress reports, validation
/// errors, verdict texts). Modules emit resource KEYS; the host application implements this over
/// its string resources. Mirrors the <c>IAppSettings</c> pattern: the module owns the contract,
/// the app owns the implementation.
/// </summary>
public interface IMessageLocalizer
{
    /// <summary>Resolves <paramref name="key"/> in the current UI language and formats the args into it.</summary>
    string Format(string key, params object[] args);
}

/// <summary>
/// Default used when the host registers nothing (unit tests, tooling): echoes the key plus args,
/// which keeps messages meaningful in logs and assertions without any resource lookup.
/// </summary>
public sealed class KeyEchoMessageLocalizer : IMessageLocalizer
{
    public string Format(string key, params object[] args) =>
        args.Length == 0 ? key : $"{key}: {string.Join(", ", args)}";
}
