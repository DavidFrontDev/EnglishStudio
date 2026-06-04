using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Dictionary.Images;
using Microsoft.Extensions.Options;

namespace EnglishStudio.App.Configuration;

/// <summary>
/// Bridges <see cref="IAppSettings.ClaudeCliPath"/> into the Ai-module's options pipeline at runtime,
/// so changes saved via SettingsWindow take effect on next CLI invocation.
/// </summary>
public sealed class ClaudeCliOptionsFromAppSettings : IPostConfigureOptions<ClaudeCliOptions>
{
    private readonly IAppSettings _settings;

    public ClaudeCliOptionsFromAppSettings(IAppSettings settings)
    {
        _settings = settings;
    }

    public void PostConfigure(string? name, ClaudeCliOptions options)
    {
        options.ConfiguredPath = _settings.ClaudeCliPath;
    }
}
