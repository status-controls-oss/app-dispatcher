using System.Windows;
using AppDispatcher.Core;

namespace AppDispatcher.Settings;

/// <summary>Collects a custom browser, and stays open until it's accepted or cancelled.</summary>
public partial class CustomBrowserWindow : Window
{
    readonly Func<CustomBrowser, Window, bool> _tryAdd;

    /// <param name="tryAdd">Adds the browser, or explains the problem over the given window and returns false.</param>
    public CustomBrowserWindow(Func<CustomBrowser, Window, bool> tryAdd)
    {
        InitializeComponent();
        _tryAdd = tryAdd;
    }

    void Browse_Click(object s, RoutedEventArgs e) => SettingsWindow.BrowseProgram(this, ExeBox, LabelBox);

    void Add_Click(object s, RoutedEventArgs e)
    {
        var browser = new CustomBrowser
        {
            Label = LabelBox.Text.Trim(),
            Exe = ExeBox.Text.Trim().Trim('"'),
            Args = TargetResolver.ParseArgs(ArgsBox.Text, BrowserTarget.UrlPlaceholder),
        };
        if (_tryAdd(browser, this)) DialogResult = true;
    }
}
