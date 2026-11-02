using System.Windows;

namespace MyPlayer_NET9.Behaviors
{
    /// <summary>
    /// Вложенное свойство «эта вкладка сейчас открыта» для кнопок боковой панели.
    /// Стиль SidebarButtonStyle по нему рисует оранжевый текст и окантовку вместо заливки при наведении.
    /// </summary>
    public static class NavButton
    {
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.RegisterAttached("IsActive", typeof(bool), typeof(NavButton), new PropertyMetadata(false));

        public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);
        public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);
    }
}
