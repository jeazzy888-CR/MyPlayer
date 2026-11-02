using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MyPlayer_NET9.Controls
{
    /// <summary>
    /// Текст, который правится на месте кликом: Enter / потеря фокуса — сохранить, Esc — отменить.
    /// Не знает ничего про ViewModel: работает через Text (двусторонняя привязка),
    /// Placeholder (что показывать, пока текста нет) и IsEditable (можно ли вообще править).
    /// </summary>
    public partial class InlineEditText : UserControl
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                nameof(Text), typeof(string), typeof(InlineEditText),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    (d, _) => ((InlineEditText)d).UpdateDisplay()));

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(
                nameof(Placeholder), typeof(string), typeof(InlineEditText),
                new PropertyMetadata(string.Empty, (d, _) => ((InlineEditText)d).UpdateDisplay()));

        public static readonly DependencyProperty IsEditableProperty =
            DependencyProperty.Register(
                nameof(IsEditable), typeof(bool), typeof(InlineEditText),
                new PropertyMetadata(true, (d, _) => ((InlineEditText)d).OnIsEditableChanged()));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        public bool IsEditable
        {
            get => (bool)GetValue(IsEditableProperty);
            set => SetValue(IsEditableProperty, value);
        }

        private bool _isEditing;

        public InlineEditText()
        {
            InitializeComponent();
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            // Элементы могут быть ещё не созданы, если свойство выставилось во время InitializeComponent.
            if (Display == null) return;

            bool empty = string.IsNullOrEmpty(Text);
            Display.Text = empty ? Placeholder : Text;
            Display.Opacity = empty ? 0.4 : 1.0;
            Display.Cursor = IsEditable ? Cursors.IBeam : Cursors.Arrow;
        }

        private void OnIsEditableChanged()
        {
            if (!IsEditable) CommitEdit();
            UpdateDisplay();
        }

        // ===================== Редактирование =====================

        private void Display_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            BeginEdit();
            e.Handled = true;
        }

        private void BeginEdit()
        {
            if (!IsEditable || _isEditing) return;

            _isEditing = true;
            Editor.Text = Text ?? string.Empty;
            Display.Visibility = Visibility.Hidden; // Hidden, а не Collapsed — высота строки не прыгает
            EditorHost.Visibility = Visibility.Visible;

            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                Editor.Focus();
                Keyboard.Focus(Editor);
                Editor.SelectAll();
            }));
        }

        private void CommitEdit()
        {
            if (!_isEditing) return;

            _isEditing = false;
            string newText = Editor.Text.Trim();
            EndEditUi();
            Text = newText;
        }

        private void CancelEdit()
        {
            if (!_isEditing) return;

            _isEditing = false;
            EndEditUi();
        }

        private void EndEditUi()
        {
            EditorHost.Visibility = Visibility.Collapsed;
            Display.Visibility = Visibility.Visible;
            UpdateDisplay();
        }

        private void Editor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelEdit();
                e.Handled = true;
            }
        }

        private void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitEdit();
    }
}
