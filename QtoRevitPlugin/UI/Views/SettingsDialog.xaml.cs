using QtoRevitPlugin.Models;
using QtoRevitPlugin.Services;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace QtoRevitPlugin.UI.Views
{
    /// <summary>
    /// Dialog impostazioni CME — costruito in code-behind per evitare file XAML separato.
    /// Attualmente espone: AutoSave on/off + intervallo minuti (minimo 30).
    /// </summary>
    public class SettingsDialog : Window
    {
        private readonly CmeSettings _settings;
        private readonly CheckBox _autoSaveCheck;
        private readonly TextBox _intervalBox;

        public SettingsDialog()
        {
            _settings = SettingsService.Load();

            Title = "CME – Impostazioni";
            Width = 500;
            Height = 330;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush?)new BrushConverter().ConvertFromString("#FAFAF9") ?? Brushes.White;

            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // section label
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // checkbox
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // interval row
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // help text
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // buttons

            // Header
            var header = new TextBlock
            {
                Text = "Impostazioni",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 16,
                Foreground = (Brush?)new BrushConverter().ConvertFromString("#0C0A09") ?? Brushes.Black
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Section label
            var sectionLbl = new TextBlock
            {
                Text = "SALVATAGGIO AUTOMATICO",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush?)new BrushConverter().ConvertFromString("#EA580C") ?? Brushes.OrangeRed
            };
            Grid.SetRow(sectionLbl, 2);
            root.Children.Add(sectionLbl);

            // Checkbox enable
            _autoSaveCheck = new CheckBox
            {
                Content = "Abilita salvataggio automatico della sessione attiva",
                IsChecked = _settings.AutoSaveEnabled,
                FontSize = 12
            };
            Grid.SetRow(_autoSaveCheck, 4);
            root.Children.Add(_autoSaveCheck);

            // Interval row
            var intervalPanel = new StackPanel { Orientation = Orientation.Horizontal };
            intervalPanel.Children.Add(new TextBlock
            {
                Text = "Intervallo:",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(22, 0, 8, 0)
            });
            _intervalBox = new TextBox
            {
                Text = _settings.NormalizedAutoSaveIntervalMinutes.ToString(CultureInfo.InvariantCulture),
                Width = 70,
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 13,
                FontFamily = new FontFamily("Consolas"),
                BorderBrush = (Brush?)new BrushConverter().ConvertFromString("#D6D3D1") ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            _intervalBox.PreviewTextInput += OnIntervalPreviewTextInput;
            intervalPanel.Children.Add(_intervalBox);
            intervalPanel.Children.Add(new TextBlock
            {
                Text = "minuti",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
            Grid.SetRow(intervalPanel, 6);
            root.Children.Add(intervalPanel);

            // Help text
            var help = new TextBlock
            {
                Text = "Il valore minimo è 30 minuti. Il salvataggio è sempre immediato ad " +
                       "ogni operazione di tagging / modifica — questo timer è un fallback.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10,
                Foreground = (Brush?)new BrushConverter().ConvertFromString("#78716C") ?? Brushes.Gray,
                Margin = new Thickness(22, 8, 0, 0)
            };
            Grid.SetRow(help, 7);
            root.Children.Add(help);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var cancelBtn = new Button
            {
                Content = "Annulla",
                Width = 100,
                Padding = new Thickness(0, 6, 0, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };
            cancelBtn.Click += (_, _) => { DialogResult = false; };

            var okBtn = new Button
            {
                Content = "Salva",
                Width = 100,
                Padding = new Thickness(0, 6, 0, 6),
                IsDefault = true,
                Background = (Brush?)new BrushConverter().ConvertFromString("#EA580C") ?? Brushes.OrangeRed,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderBrush = (Brush?)new BrushConverter().ConvertFromString("#EA580C") ?? Brushes.OrangeRed
            };
            okBtn.Click += OnSave;

            buttonPanel.Children.Add(cancelBtn);
            buttonPanel.Children.Add(okBtn);
            Grid.SetRow(buttonPanel, 9);
            root.Children.Add(buttonPanel);

            Content = root;

            Loaded += (_, _) =>
            {
                try
                {
                    var helper = new WindowInteropHelper(this);
                    if (helper.Owner == IntPtr.Zero)
                        helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                }
                catch { }
            };
        }

        private void OnIntervalPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Accetta solo cifre
            foreach (var ch in e.Text)
                if (!char.IsDigit(ch)) { e.Handled = true; return; }
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(_intervalBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                               out var minutes) || minutes <= 0)
            {
                MessageBox.Show(this,
                    $"Inserisci un numero intero di minuti (minimo {CmeSettings.MinAutoSaveIntervalMinutes}).",
                    "Valore non valido",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (minutes < CmeSettings.MinAutoSaveIntervalMinutes)
            {
                var result = MessageBox.Show(this,
                    $"L'intervallo minimo è {CmeSettings.MinAutoSaveIntervalMinutes} minuti.\n" +
                    $"Verrà impostato a {CmeSettings.MinAutoSaveIntervalMinutes} minuti. Procedere?",
                    "Valore sotto soglia",
                    MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (result != MessageBoxResult.OK) return;
                minutes = CmeSettings.MinAutoSaveIntervalMinutes;
            }

            _settings.AutoSaveEnabled = _autoSaveCheck.IsChecked == true;
            _settings.AutoSaveIntervalMinutes = minutes;

            SettingsService.Save(_settings);
            DialogResult = true;
        }
    }
}
