using System.Windows.Data;
using System.Windows.Markup;

namespace NightCoopLauncher.App.Localization;

/// <summary>
/// XAML shorthand for a translated text: <c>{l:T play.title}</c>. It is a binding, so the
/// text changes as soon as the language does.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension(string key) : MarkupExtension
{
    [ConstructorArgument("key")]
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Localizer.Current, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
