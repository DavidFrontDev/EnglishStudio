using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace EnglishStudio.App.Localization;

/// <summary>
/// XAML markup extension that localizes a string by key, e.g. <c>Text="{loc:Tr Settings_Save}"</c>.
/// It produces a one-way binding to <see cref="LocalizationManager.Instance"/>'s indexer, so the
/// value re-reads automatically whenever the language changes.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    /// <summary>The resource key to look up (e.g. <c>Settings_Save</c>).</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Setter.Value (style/trigger setters) cannot hold a Binding — WPF throws there. In that
        // context resolve to a plain string now (it loses live-switching, but such spots are rare
        // and usually re-evaluated by their trigger anyway).
        if (serviceProvider?.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget pvt
            && pvt.TargetObject is Setter)
        {
            return LocalizationManager.Instance[Key];
        }

        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
