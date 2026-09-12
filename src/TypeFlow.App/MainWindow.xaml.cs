using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using TypeFlow.Core.Engine;
using TypeFlow.Core.Storage;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace TypeFlow.App;

public partial class MainWindow : Window
{
    private readonly ShortcutStore _store;
    private readonly ShortcutEngine _engine;
    private readonly string _configPath;
    private readonly ObservableCollection<ShortcutRow> _rows = new ObservableCollection<ShortcutRow>();
    private readonly ListCollectionView _view;
    private string? _editingKey;

    public sealed class ShortcutRow
    {
        public string Shortcut { get; set; } = string.Empty;
        public string Expansion { get; set; } = string.Empty;
    }

    public MainWindow(ShortcutStore store, ShortcutEngine engine, string configPath)
    {
        // Assign before InitializeComponent: handlers wired by XAML need them set.
        _store = store;
        _engine = engine;
        _configPath = configPath;

        InitializeComponent();

        // Set the toggle visuals from the engine AFTER the full visual tree exists;
        // setting them during XAML parse fires Toggle_Changed before all controls
        // are wired (the original silent startup crash).
        ActiveToggle.IsChecked = _engine.IsEnabled;
        InputToggle.IsChecked = _engine.IsInputEnabled;

        _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        ShortcutList.ItemsSource = _view;

        SearchBox.TextChanged += (s, e) => { _view.Refresh(); RefreshCount(); };

        Refresh();
    }

    private bool FilterRow(object item)
    {
        if (item is not ShortcutRow row) return false;
        string q = SearchBox.Text?.Trim() ?? string.Empty;
        if (q.Length == 0) return true;
        return row.Shortcut.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.Expansion.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void Refresh()
    {
        _rows.Clear();
        foreach (var pair in _store.Shortcuts)
        {
            _rows.Add(new ShortcutRow { Shortcut = pair.Key, Expansion = pair.Value });
        }
        _view.Refresh();
        CountText.Text = $"{_view.Cast<object>().Count()} of {_store.Count} shortcuts";
    }

    private void RefreshCount()
    {
        CountText.Text = $"{_view.Cast<object>().Count()} of {_store.Count} shortcuts";
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_engine == null || ActiveToggle == null || InputToggle == null) return;
            _engine.IsEnabled = ActiveToggle.IsChecked == true;
            _engine.IsInputEnabled = InputToggle.IsChecked == true;
            PersistSettings();
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(App.DataDirectory, "crash.log"), ex.ToString()); } catch { }
        }
    }

    private void PersistSettings()
    {
        try
        {
            var cfg = new Dictionary<string, bool> { ["isInputEnabled"] = _engine.IsInputEnabled };
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath) ?? App.DataDirectory);
            File.WriteAllText(_configPath, JsonSerializer.Serialize(cfg));
        }
        catch { }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        string shortcut = ShortcutBox.Text.Trim();
        string expansion = ExpansionBox.Text;

        ValidationText.Text = string.Empty;

        if (shortcut.Length == 0)
        {
            ValidationText.Text = "Shortcut is required.";
            return;
        }
        if (shortcut.Length > 50)
        {
            ValidationText.Text = "Shortcut is too long. Maximum 50 characters.";
            return;
        }
        if (expansion.Length == 0)
        {
            ValidationText.Text = "Expansion text is required.";
            return;
        }
        if (expansion.Length > 5000)
        {
            ValidationText.Text = "Expansion text is too long. Maximum 5000 characters.";
            return;
        }

        string key = shortcut.ToLowerInvariant();

        if (_editingKey != null)
        {
            if (_editingKey != key && _store.Contains(key))
            {
                ValidationText.Text = $"\"{key}\" already exists. Overwrite? (cancel edit / import it instead)";
                return;
            }
            _store.Remove(_editingKey);
            _editingKey = null;
        }
        else
        {
            if (_store.Contains(key))
            {
                var result = MessageBox.Show($"\"{key}\" already exists. Overwrite it?", "TypeFlow",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }
        }

        _store.Set(key, expansion);
        _store.Save();
        _engine.ReplaceShortcuts(_store.Shortcuts);

        ShortcutBox.Text = string.Empty;
        ExpansionBox.Text = string.Empty;
        FormTitle.Text = "Add Shortcut";
        AddButton.Content = "Add Shortcut";
        CancelEditButton.Visibility = Visibility.Collapsed;

        Refresh();
        StatusText.Text = "Shortcut saved.";
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.Tag is not ShortcutRow row) return;
        _editingKey = row.Shortcut;
        ShortcutBox.Text = row.Shortcut;
        ExpansionBox.Text = row.Expansion;
        FormTitle.Text = "Edit Shortcut";
        AddButton.Content = "Update Shortcut";
        CancelEditButton.Visibility = Visibility.Visible;
        ValidationText.Text = string.Empty;
        ShortcutBox.Focus();
        ShortcutBox.SelectAll();
    }

    private void CancelEditButton_Click(object sender, RoutedEventArgs e)
    {
        _editingKey = null;
        ShortcutBox.Text = string.Empty;
        ExpansionBox.Text = string.Empty;
        FormTitle.Text = "Add Shortcut";
        AddButton.Content = "Add Shortcut";
        CancelEditButton.Visibility = Visibility.Collapsed;
        ValidationText.Text = string.Empty;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.Tag is not ShortcutRow row) return;
        var result = MessageBox.Show($"Are you sure you want to delete the shortcut for \"{row.Shortcut}\"?",
            "TypeFlow", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _store.Remove(row.Shortcut);
        if (_editingKey == row.Shortcut) CancelEditButton_Click(sender, e);
        _store.Save();
        _engine.ReplaceShortcuts(_store.Shortcuts);
        Refresh();
        StatusText.Text = $"Deleted \"{row.Shortcut}\".";
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Import shortcuts CSV"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            string text = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
            int count = _store.ImportCsv(text);
            _engine.ReplaceShortcuts(_store.Shortcuts);
            Refresh();
            StatusText.Text = count > 0
                ? $"Imported {count} shortcuts."
                : "No valid shortcut rows found.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Import failed: " + ex.Message, "TypeFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_store.Count == 0)
        {
            MessageBox.Show("No shortcuts to export.", "TypeFlow", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "typeflow_shortcuts.csv",
            Title = "Export shortcuts CSV"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            CsvWriter.ExportToFile(dlg.FileName, _store.Shortcuts);
            StatusText.Text = "Exported to " + dlg.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Export failed: " + ex.Message, "TypeFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Replace ALL shortcuts with the bundled default set? Your current shortcuts will be lost.",
            "TypeFlow", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            string? defaults = null;
            using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("TypeFlow.defaults.csv"))
            using (var reader = new StreamReader(s!))
            {
                defaults = await reader.ReadToEndAsync();
            }

            _store.Shortcuts.Clear();
            int count = _store.ImportCsv(defaults ?? string.Empty);
            _engine.ReplaceShortcuts(_store.Shortcuts);
            Refresh();
            StatusText.Text = $"Restored {count} default shortcuts.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Reset failed: " + ex.Message, "TypeFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}