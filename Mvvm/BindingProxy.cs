using System.Windows;

namespace EsoAddons.Mvvm;

/// <summary>Carries the DataContext to elements outside the visual tree (e.g. DataGridColumn),
/// which can't inherit it. Declared as a window resource with Data bound to the VM, then referenced
/// via {Binding Data..., Source={StaticResource Proxy}}.</summary>
public class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
}
