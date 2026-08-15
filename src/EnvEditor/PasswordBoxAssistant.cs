using System.Windows;
using System.Windows.Controls;

namespace EnvEditor;

/// <summary>
/// 让 PasswordBox.Password 可被 MVVM 双向绑定（Password 不是依赖属性，需此辅助类桥接）。
/// 用法：<PasswordBox assist:PasswordBoxAssistant.Password="{Binding PasswordInput, Mode=TwoWay}" />
/// </summary>
public static class PasswordBoxAssistant
{
    public static readonly DependencyProperty PasswordProperty =
        DependencyProperty.RegisterAttached(
            "Password", typeof(string), typeof(PasswordBoxAssistant),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached("IsUpdating", typeof(bool), typeof(PasswordBoxAssistant), new PropertyMetadata(false));

    public static string GetPassword(DependencyObject obj) => (string)obj.GetValue(PasswordProperty);
    public static void SetPassword(DependencyObject obj, string value) => obj.SetValue(PasswordProperty, value);

    private static bool GetIsUpdating(DependencyObject obj) => (bool)obj.GetValue(IsUpdatingProperty);
    private static void SetIsUpdating(DependencyObject obj, bool value) => obj.SetValue(IsUpdatingProperty, value);

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        box.PasswordChanged -= PasswordChanged;
        if (!GetIsUpdating(box))
            box.Password = (string)e.NewValue;
        box.PasswordChanged += PasswordChanged;
    }

    private static void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;
        SetIsUpdating(box, true);
        SetPassword(box, box.Password);
        SetIsUpdating(box, false);
    }
}
