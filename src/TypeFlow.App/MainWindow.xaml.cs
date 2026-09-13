using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using TypeFlow.App.Localization;
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
    private bool _addSectionExpanded = true;
    private bool _languageInitializing = true;
    private bool _restoringToggles;

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
        _restoringToggles = true;
        try
        {
            ActiveToggle.IsChecked = _engine.IsEnabled;
            InputToggle.IsChecked = _engine.IsInputEnabled;
        }
        finally
        {
            _restoringToggles = false;
        }

        _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        ShortcutList.ItemsSource = _view;

        SearchBox.TextChanged += (s, e) => { _view.Refresh(); RefreshCount(); };

        // Language switcher: reflect the applied language, then subscribe to
        // live changes (ResourceDictionary swap re-resolves all DynamicResource).
        LanguageCombo.SelectedIndex = L10n.Code == "ar" ? 1 : 0;
        _languageInitializing = false;
        L10n.LanguageChanged += OnLanguageChanged;
        OnLanguageChanged();

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
        CountText.Text = L10n.Tf("ui.countFormat", _view.Cast<object>().Count(), _store.Count);
        EmptyText.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshCount()
    {
        CountText.Text = L10n.Tf("ui.countFormat", _view.Cast<object>().Count(), _store.Count);
        EmptyText.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_languageInitializing || sender is not System.Windows.Controls.ComboBox cb) return;
        string code = cb.SelectedIndex == 1 ? "ar" : "en";
        if (code == L10n.Code) return;
        L10n.Apply(code);
        PersistSettings();
    }

    private void OnLanguageChanged()
    {
        FlowDirection = L10n.IsRtl
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;
        ApplyFormState();
        RefreshCount();
    }

    /// <summary>Form title + button reflect add vs edit state (called after every edit-mode change).</summary>
    private void ApplyFormState()
    {
        bool editing = _editingKey != null;
        FormTitle.Text = L10n.T(editing ? "ui.editShortcut" : "ui.addShortcut");
        AddButton.Content = L10n.T(editing ? "ui.updateShortcut" : "ui.addShortcut");
    }

    private void AddSectionToggle_Click(object sender, RoutedEventArgs e)
    {
        _addSectionExpanded = !_addSectionExpanded;
        ApplyAddSectionState();
    }

    private void ApplyAddSectionState()
    {
        AddFormCard.Visibility = _addSectionExpanded ? Visibility.Visible : Visibility.Collapsed;
        AddSectionChevron.Text = _addSectionExpanded ? "\uE70E" : "\uE70D";
    }

    // Editing a row opens the form even if the section was collapsed.
    private void ExpandAddSection()
    {
        if (!_addSectionExpanded)
        {
            _addSectionExpanded = true;
            ApplyAddSectionState();
        }
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_restoringToggles) return;
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
            var cfg = new AppSettings
            {
                IsInputEnabled = _engine.IsInputEnabled,
                Language = L10n.Code
            };
            SettingsIo.Save(_configPath, cfg);
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
            ValidationText.Text = L10n.T("msg.shortcutRequired");
            return;
        }
        if (shortcut.Length > 50)
        {
            ValidationText.Text = L10n.T("msg.shortcutTooLong");
            return;
        }
        if (expansion.Length == 0)
        {
            ValidationText.Text = L10n.T("msg.expansionRequired");
            return;
        }
        if (expansion.Length > 5000)
        {
            ValidationText.Text = L10n.T("msg.expansionTooLong");
            return;
        }

        string key = shortcut.ToLowerInvariant();

        if (_editingKey != null)
        {
            if (_editingKey != key && _store.Contains(key))
            {
                ValidationText.Text = L10n.Tf("msg.existsEdit", key);
                return;
            }
            _store.Remove(_editingKey);
            _editingKey = null;
        }
        else
        {
            if (_store.Contains(key))
            {
                var result = MessageBox.Show(L10n.Tf("msg.existsAdd", key), "TypeFlow",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }
        }

        _store.Set(key, expansion);
        _store.Save();
        _engine.ReplaceShortcuts(_store.Shortcuts);

        ShortcutBox.Text = string.Empty;
        ExpansionBox.Text = string.Empty;
        ApplyFormState();
        CancelEditButton.Visibility = Visibility.Collapsed;

        Refresh();
        StatusText.Text = L10n.T("msg.saved");
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.Tag is not ShortcutRow row) return;
        ExpandAddSection();
        _editingKey = row.Shortcut;
        ShortcutBox.Text = row.Shortcut;
        ExpansionBox.Text = row.Expansion;
        ApplyFormState();
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
        ApplyFormState();
        CancelEditButton.Visibility = Visibility.Collapsed;
        ValidationText.Text = string.Empty;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.Tag is not ShortcutRow row) return;
        var result = MessageBox.Show(L10n.Tf("msg.confirmDelete", row.Shortcut),
            "TypeFlow", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _store.Remove(row.Shortcut);
        if (_editingKey == row.Shortcut) CancelEditButton_Click(sender, e);
        _store.Save();
        _engine.ReplaceShortcuts(_store.Shortcuts);
        Refresh();
        StatusText.Text = L10n.Tf("msg.deleted", row.Shortcut);
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = L10n.T("dlg.importTitle")
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            string text = CsvParser.ReadCsvFile(dlg.FileName);
            int count = _store.ImportCsv(text);
            _engine.ReplaceShortcuts(_store.Shortcuts);
            Refresh();
            StatusText.Text = count > 0
                ? L10n.Tf("msg.imported", count)
                : L10n.T("msg.noValidRows");
        }
        catch (Exception ex)
        {
            MessageBox.Show(L10n.Tf("msg.importFailed", ex.Message), "TypeFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_store.Count == 0)
        {
            MessageBox.Show(L10n.T("msg.noExport"), "TypeFlow", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "typeflow_shortcuts.csv",
            Title = L10n.T("dlg.exportTitle")
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            CsvWriter.ExportToFile(dlg.FileName, _store.Shortcuts);
            StatusText.Text = L10n.Tf("msg.exported", dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(L10n.Tf("msg.exportFailed", ex.Message), "TypeFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            L10n.T("msg.confirmClearAll"),
            "TypeFlow", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            _store.ClearAll();
            _engine.ReplaceShortcuts(_store.Shortcuts);
            Refresh();
            StatusText.Text = L10n.T("msg.clearedAll");
        }
        catch (Exception ex)
        {
            MessageBox.Show(L10n.Tf("msg.clearFailed", ex.Message), "TypeFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            L10n.T("msg.confirmReset"),
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
            StatusText.Text = L10n.Tf("msg.restored", count);
        }
        catch (Exception ex)
        {
            MessageBox.Show(L10n.Tf("msg.resetFailed", ex.Message), "TypeFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}