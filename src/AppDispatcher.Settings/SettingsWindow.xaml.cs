using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AppDispatcher.Core;
using Microsoft.Win32;

namespace AppDispatcher.Settings;

public partial class SettingsWindow : Window
{
    sealed record ExtensionRow(ExtensionRule Rule, string Display);

    readonly AppConfig _config = AppConfig.Load();
    readonly string _pickerExe = Path.Combine(AppContext.BaseDirectory, "AppDispatcher.exe");

    public SettingsWindow()
    {
        InitializeComponent();
        CatalogCombo.ItemsSource = EditorCatalog.Discover();
        CatalogCombo.SelectedIndex = 0;
        var browsers = BrowserDiscovery.Discover();
        BrowserCombo.ItemsSource = browsers;
        if (browsers.Count > 0) BrowserCombo.SelectedIndex = 0;
        RefreshRules();
        RefreshExtensions(_config.Extensions.FirstOrDefault());
        RefreshStatus();
    }

    ExtensionRule? SelectedRule => (ExtensionList.SelectedItem as ExtensionRow)?.Rule;

    // ---- Registration ----

    bool IsRegistered => Registration.IsRegistered(_pickerExe);

    void RefreshStatus()
    {
        if (!File.Exists(_pickerExe))
            StatusText.Text = $"The picker isn't next to this app: {_pickerExe}";
        else if (!IsRegistered)
            StatusText.Text = "Not registered yet.";
        else
        {
            var pending = _config.Extensions.Count(e => !Registration.IsDefaultFor(e.Extension));
            StatusText.Text = pending == 0
                ? "Registered, and the default for every file type listed below."
                : $"Registered. Still to choose in Windows Default Apps: {pending} file type(s).";
        }
    }

    void Register_Click(object s, RoutedEventArgs e) => Guarded(() => Registration.Sync(_pickerExe, _config.Extensions));

    void Unregister_Click(object s, RoutedEventArgs e) => Guarded(Registration.Unregister);

    void Refresh_Click(object s, RoutedEventArgs e) => RefreshExtensions(SelectedRule);

    void DefaultApps_Click(object s, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(Registration.DefaultAppsUri) { UseShellExecute = true });

    void Guarded(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show(this, ex.Message, "App Dispatcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        RefreshExtensions(SelectedRule);
    }

    /// <summary>Saves, and keeps Windows' list of file types in step once the app is registered.</summary>
    void Commit()
    {
        Guarded(() =>
        {
            _config.Save();
            if (IsRegistered) Registration.Sync(_pickerExe, _config.Extensions);
        });
    }

    // ---- File types ----

    void RefreshExtensions(ExtensionRule? select)
    {
        var registered = IsRegistered;
        var rows = _config.Extensions
            .OrderBy(x => x.Extension, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ExtensionRow(x, registered && Registration.IsDefaultFor(x.Extension) ? $"{x.Extension}  ✓ default" : x.Extension))
            .ToList();
        ExtensionList.ItemsSource = rows;
        ExtensionList.SelectedItem = rows.FirstOrDefault(r => r.Rule == select);
        RefreshTargets(null);
        RefreshStatus();
    }

    void RefreshTargets(FileTarget? select)
    {
        TargetList.ItemsSource = null;
        TargetList.ItemsSource = SelectedRule?.Targets;
        TargetList.SelectedItem = select;
    }

    void ExtensionList_SelectionChanged(object s, SelectionChangedEventArgs e) => RefreshTargets(null);

    void AddExtension_Click(object s, RoutedEventArgs e)
    {
        var ext = AppConfig.NormalizeExtension(ExtensionBox.Text);
        if (ext == null) return;
        var rule = _config.FindExtension(ext);
        if (rule == null)
        {
            rule = new ExtensionRule { Extension = ext };
            _config.Extensions.Add(rule);
            Commit();
        }
        ExtensionBox.Clear();
        RefreshExtensions(rule);
    }

    void RemoveExtension_Click(object s, RoutedEventArgs e)
    {
        if (SelectedRule is not { } rule) return;
        _config.Extensions.Remove(rule);
        Commit();
        RefreshExtensions(null);
    }

    void AddTarget(FileTarget target)
    {
        if (SelectedRule is not { } rule) return;
        if (rule.Targets.Any(t => t.Label == target.Label)) return;
        rule.Targets.Add(target);
        CommitKeepingSelection(rule, target);
    }

    void CommitKeepingSelection(ExtensionRule rule, FileTarget? target)
    {
        Commit();
        RefreshExtensions(rule);
        RefreshTargets(target);
    }

    void AddCatalog_Click(object s, RoutedEventArgs e)
    {
        if (CatalogCombo.SelectedItem is FileTarget t)
            AddTarget(new FileTarget { Label = t.Label, Exe = t.Exe, Args = t.Args.ToList(), AppId = t.AppId });
    }

    void BrowseExe_Click(object s, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) != true) return;
        CustomExe.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(CustomLabel.Text))
            CustomLabel.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
    }

    void AddCustom_Click(object s, RoutedEventArgs e)
    {
        var exe = CustomExe.Text.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(CustomLabel.Text) || !File.Exists(exe)) return;
        AddTarget(new FileTarget { Label = CustomLabel.Text.Trim(), Exe = exe, Args = TargetResolver.ParseArgs(CustomArgs.Text) });
        CustomLabel.Clear();
        CustomExe.Clear();
        CustomArgs.Text = "{file}";
    }

    void Up_Click(object s, RoutedEventArgs e) => MoveTarget(-1);

    void Down_Click(object s, RoutedEventArgs e) => MoveTarget(1);

    void MoveTarget(int delta)
    {
        if (SelectedRule is not { } rule || TargetList.SelectedItem is not FileTarget t) return;
        var to = rule.Targets.IndexOf(t) + delta;
        if (to < 0 || to >= rule.Targets.Count) return;
        rule.Targets.Remove(t);
        rule.Targets.Insert(to, t);
        CommitKeepingSelection(rule, t);
    }

    void RemoveTarget_Click(object s, RoutedEventArgs e)
    {
        if (SelectedRule is not { } rule || TargetList.SelectedItem is not FileTarget t) return;
        rule.Targets.Remove(t);
        CommitKeepingSelection(rule, null);
    }

    // ---- Links ----

    void RefreshRules()
    {
        RulesGrid.ItemsSource = null;
        RulesGrid.ItemsSource = _config.Rules;
    }

    void AddRule_Click(object s, RoutedEventArgs e)
    {
        if (BrowserCombo.SelectedItem is not BrowserTarget t || string.IsNullOrWhiteSpace(DomainBox.Text)) return;
        _config.SetRule(DomainBox.Text, t.Browser, t.ProfileId, t.ProfileName);
        Commit();
        DomainBox.Clear();
        RefreshRules();
    }

    // Selecting a rule loads it into the edit fields; "Update selected" replaces it.
    void RulesGrid_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not Rule r) return;
        DomainBox.Text = r.Domain;
        BrowserCombo.SelectedItem = (BrowserCombo.ItemsSource as List<BrowserTarget>)?
            .FirstOrDefault(t => t.Matches(r.Browser, r.ProfileId));
    }

    void UpdateRule_Click(object s, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not Rule old ||
            BrowserCombo.SelectedItem is not BrowserTarget t ||
            string.IsNullOrWhiteSpace(DomainBox.Text)) return;
        _config.Rules.Remove(old);
        _config.SetRule(DomainBox.Text, t.Browser, t.ProfileId, t.ProfileName);
        Commit();
        RefreshRules();
    }

    void RemoveRule_Click(object s, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not Rule r) return;
        _config.Rules.Remove(r);
        Commit();
        RefreshRules();
    }
}
