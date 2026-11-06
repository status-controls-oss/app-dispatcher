using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AppDispatcher.Core;
using Microsoft.Win32;

namespace AppDispatcher.Settings;

public partial class SettingsWindow : Window
{
    sealed record ExtensionRow(ExtensionRule Rule, string Display);

    readonly AppConfig _config = AppConfig.Load();
    readonly string _pickerExe = Path.Combine(AppContext.BaseDirectory, "AppDispatcher.exe");
    readonly List<CustomBrowser> _detectedBrowsers;
    readonly Dictionary<string, List<FileTarget>> _fileHandlers = new(StringComparer.OrdinalIgnoreCase);

    static SettingsWindow()
    {
        // Help should appear promptly and stay long enough to read; the defaults are slower and only 5 seconds.
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(300));
        ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(30000));
    }

    public SettingsWindow()
    {
        InitializeComponent();
        CatalogCombo.ItemsSource = EditorCatalog.Discover();
        CatalogCombo.SelectedIndex = 0;
        var alreadyListed = BrowserDiscovery.Discover(Array.Empty<CustomBrowser>(), null).Select(b => b.ExePath);
        _detectedBrowsers = BrowserCatalog.Detect(alreadyListed.Append(_pickerExe));
        RefreshBrowsers();
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
            string Named(SetupState state) => string.Join(", ", _config.Extensions.Where(x => StateOf(x) == state).Select(x => x.Extension));
            var (noApps, notDefault) = (Named(SetupState.NoApps), Named(SetupState.NotDefault));
            StatusText.Text = noApps.Length + notDefault.Length == 0
                ? "Registered, and the default for every file type listed below."
                : "Registered." +
                  (noApps.Length > 0 ? $" Needs apps: {noApps}." : "") +
                  (notDefault.Length > 0 ? $" Still to choose in Windows Default Apps: {notDefault}." : "");
        }
    }

    SetupState StateOf(ExtensionRule rule) =>
        SetupGuide.StateOf(IsRegistered, rule.Targets.Count, Registration.IsDefaultFor(rule.Extension));

    void RefreshGuide()
    {
        var ready = false;
        if (SelectedRule is { } rule)
        {
            var state = StateOf(rule);
            ready = state == SetupState.Ready;
            GuideText.Text = SetupGuide.Message(rule.Extension, state, rule.Targets.Count);
            GuideButton.Visibility = state == SetupState.NotDefault ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            GuideText.Text = "Select a file type, or add one on the left.";
            GuideButton.Visibility = Visibility.Collapsed;
        }
        GuideBanner.Background = new SolidColorBrush(ready ? Color.FromRgb(0xDF, 0xF6, 0xDD) : Color.FromRgb(0xFF, 0xF4, 0xCE));
    }

    /// <summary>Picks up a default chosen in Windows as soon as the user comes back here.</summary>
    void Window_Activated(object? s, EventArgs e)
    {
        var target = TargetList.SelectedItem as FileTarget;
        _fileHandlers.Clear(); // An app may have been installed in the meantime.
        RefreshExtensions(SelectedRule);
        RefreshTargets(target);
    }

    void Register_Click(object s, RoutedEventArgs e) => Guarded(() =>
    {
        Registration.Sync(_pickerExe, _config.Extensions);
        Shortcut.Create(Shortcut.StartMenuPath, Environment.ProcessPath!, "Choose which apps App Dispatcher offers for files and links.");
    });

    void Unregister_Click(object s, RoutedEventArgs e) => Guarded(() =>
    {
        Registration.Unregister();
        Shortcut.Delete(Shortcut.StartMenuPath);
    });

    void Refresh_Click(object s, RoutedEventArgs e) => RefreshExtensions(SelectedRule);

    void DefaultApps_Click(object s, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(Registration.DefaultAppsUri) { UseShellExecute = true });

    void Guarded(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.Runtime.InteropServices.COMException)
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
        var rows = _config.Extensions
            .OrderBy(x => x.Extension, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ExtensionRow(x, $"{x.Extension}  {SetupGuide.ListNote(StateOf(x))}".TrimEnd()))
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
        RefreshHandlers();
        RefreshGuide();
    }

    void RefreshHandlers()
    {
        var offered = new List<FileTarget>();
        if (SelectedRule is { } rule)
        {
            if (!_fileHandlers.TryGetValue(rule.Extension, out var handlers))
                _fileHandlers[rule.Extension] = handlers = FileHandlers.Discover(rule.Extension, _pickerExe);
            offered = handlers
                .Where(h => !rule.Targets.Any(t =>
                    string.Equals(t.Exe, h.Exe, StringComparison.OrdinalIgnoreCase) && t.Args.SequenceEqual(h.Args)))
                .ToList();
        }
        var any = offered.Count > 0;
        HandlerCombo.ItemsSource = any ? offered : new List<FileTarget> { new() { Label = "No other apps found for this file type" } };
        HandlerCombo.SelectedIndex = 0;
        HandlerCombo.IsEnabled = any;
        AddHandlerButton.IsEnabled = any;
    }

    void AddHandler_Click(object s, RoutedEventArgs e)
    {
        if (HandlerCombo.SelectedItem is FileTarget t)
            AddTarget(new FileTarget { Label = t.Label, Exe = t.Exe, Args = t.Args.ToList() });
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

    void BrowseExe_Click(object s, RoutedEventArgs e) => BrowseProgram(CustomExe, CustomLabel);

    void BrowseProgram(TextBox exe, TextBox label)
    {
        var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) != true) return;
        exe.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(label.Text))
            label.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
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

    void RefreshBrowsers()
    {
        var selected = (BrowserCombo.SelectedItem as BrowserTarget)?.Display;
        var browsers = BrowserDiscovery.Discover(_config.CustomBrowsers, _pickerExe);
        BrowserCombo.ItemsSource = browsers;
        BrowserCombo.SelectedItem = browsers.FirstOrDefault(b => b.Display == selected) ?? browsers.FirstOrDefault();
        CustomBrowserList.ItemsSource = null;
        CustomBrowserList.ItemsSource = _config.CustomBrowsers;

        // Removing a browser offers it again, because this filters the list found at startup.
        var offered = _detectedBrowsers
            .Where(d => !_config.CustomBrowsers.Any(c =>
                string.Equals(c.Exe, d.Exe, StringComparison.OrdinalIgnoreCase) && c.Args.SequenceEqual(d.Args)))
            .ToList();
        var any = offered.Count > 0;
        DetectedCombo.ItemsSource = any ? offered : new List<CustomBrowser> { new() { Label = "No other browsers found" } };
        DetectedCombo.SelectedIndex = 0;
        DetectedCombo.IsEnabled = any;
        AddDetectedButton.IsEnabled = any;
    }

    void BrowseBrowser_Click(object s, RoutedEventArgs e) => BrowseProgram(BrowserExe, BrowserLabel);

    void AddDetected_Click(object s, RoutedEventArgs e)
    {
        if (DetectedCombo.SelectedItem is CustomBrowser b) TryAddBrowser(b);
    }

    void AddBrowser_Click(object s, RoutedEventArgs e)
    {
        var added = TryAddBrowser(new CustomBrowser
        {
            Label = BrowserLabel.Text.Trim(),
            Exe = BrowserExe.Text.Trim().Trim('"'),
            Args = TargetResolver.ParseArgs(BrowserArgs.Text, BrowserTarget.UrlPlaceholder),
        });
        if (!added) return;
        BrowserLabel.Clear();
        BrowserExe.Clear();
        BrowserArgs.Text = BrowserTarget.UrlPlaceholder;
    }

    bool TryAddBrowser(CustomBrowser browser)
    {
        var problem =
            browser.Label.Length == 0 ? "Enter a label." :
            !File.Exists(browser.Exe) ? "Choose a program that exists." :
            string.Equals(browser.Exe, _pickerExe, StringComparison.OrdinalIgnoreCase) ? "App Dispatcher can't be one of its own browsers." :
            _config.CustomBrowsers.Any(b => string.Equals(b.Label, browser.Label, StringComparison.OrdinalIgnoreCase)) ? $"There is already a browser called {browser.Label}." :
            null;
        if (problem != null)
        {
            MessageBox.Show(this, problem, "App Dispatcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _config.CustomBrowsers.Add(browser);
        Commit();
        RefreshBrowsers();
        return true;
    }

    void RemoveBrowser_Click(object s, RoutedEventArgs e)
    {
        if (CustomBrowserList.SelectedItem is not CustomBrowser b) return;
        _config.CustomBrowsers.Remove(b);
        // A rule for a browser that's gone could never fire again.
        _config.Rules.RemoveAll(r => r.Browser == b.Label && r.ProfileId.Length == 0);
        Commit();
        RefreshBrowsers();
        RefreshRules();
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
