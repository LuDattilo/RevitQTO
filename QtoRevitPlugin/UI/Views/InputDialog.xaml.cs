using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Mini-dialog WPF per chiedere una stringa all'utente (nome sessione, ecc.).
    /// Costruito in code-behind per evitare file XAML separato.
    /// </summary>
    public class InputDialog : Window
    {
        private readonly TextBox _textBox;
        public string InputValue { get; private set; } = string.Empty;

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            Title = title;
            Width = 440;
            Height = 190;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush?)new BrushConverter().ConvertFromString("#FAFAF9") ?? Brushes.White;

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var promptLabel = new TextBlock
            {
                Text = prompt,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(promptLabel, 0);
            grid.Children.Add(promptLabel);

            _textBox = new TextBox
            {
                Text = defaultValue,
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 13,
                BorderBrush = (Brush?)new BrushConverter().ConvertFromString("#D6D3D1") ?? Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            _textBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Accept();
                if (e.Key == Key.Escape) DialogResult = false;
            };
            Grid.SetRow(_textBox, 2);
            grid.Children.Add(_textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var cancelBtn = new Button
            {
                Content = "Annulla",
                Width = 90,
                Padding = new Thickness(0, 6, 0, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };
            cancelBtn.Click += (_, _) => { DialogResult = false; };

            var okBtn = new Button
            {
                Content = "OK",
                Width = 90,
                Padding = new Thickness(0, 6, 0, 6),
                IsDefault = true,
                Background = (Brush?)new BrushConverter().ConvertFromString("#EA580C") ?? Brushes.Orange,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderBrush = (Brush?)new BrushConverter().ConvertFromString("#EA580C") ?? Brushes.Orange
            };
            okBtn.Click += (_, _) => Accept();

            buttonPanel.Children.Add(cancelBtn);
            buttonPanel.Children.Add(okBtn);
            Grid.SetRow(buttonPanel, 4);
            grid.Children.Add(buttonPanel);

            Content = grid;

            Loaded += (_, _) =>
            {
                try
                {
                    var helper = new WindowInteropHelper(this);
                    if (helper.Owner == IntPtr.Zero)
                        helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                }
                catch { }
                _textBox.SelectAll();
                _textBox.Focus();
            };
        }

        private void Accept()
        {
            InputValue = _textBox.Text?.Trim() ?? string.Empty;
            DialogResult = !string.IsNullOrEmpty(InputValue);
        }

        /// <summary>Shortcut: mostra dialog e restituisce la stringa o null se annullato.</summary>
        public static string? Prompt(string title, string prompt, string defaultValue = "")
        {
            var dlg = new InputDialog(title, prompt, defaultValue);
            return dlg.ShowDialog() == true ? dlg.InputValue : null;
        }
    }
}
