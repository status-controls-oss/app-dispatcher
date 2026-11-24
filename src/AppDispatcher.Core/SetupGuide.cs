namespace AppDispatcher.Core;

public enum SetupState { NotRegistered, NoApps, NotDefault, Ready }

/// <summary>Works out what a file type still needs before double-clicking it shows the picker.</summary>
public static class SetupGuide
{
    public static SetupState StateOf(bool registered, int apps, bool isDefault) =>
        !registered ? SetupState.NotRegistered :
        apps == 0 ? SetupState.NoApps :
        !isDefault ? SetupState.NotDefault :
        SetupState.Ready;

    public static string Message(string extension, SetupState state, int apps) => state switch
    {
        SetupState.NotRegistered => $"Next: click Register, so that Windows can offer App Dispatcher for {extension} files.",
        SetupState.NoApps => $"Next: add at least one app below. Until then, App Dispatcher has nothing to open {extension} files with.",
        SetupState.NotDefault => $"Last step: Windows still decides how {extension} files open, and only you can change that. Click the button, then choose App Dispatcher for {extension}. Or double-click a {extension} file, choose App Dispatcher and click Always.",
        _ when apps == 1 => $"All set: {extension} files open straight in this app. Add another app to get the picker.",
        _ => $"All set: double-clicking a {extension} file shows the picker.",
    };

    /// <param name="defaultBrowser">The browser that opens links without a rule, or null if they show the picker.</param>
    public static string LinkMessage(SetupState state, string? defaultBrowser) => state switch
    {
        SetupState.NotRegistered => "Next: click Register, so that Windows can send links to App Dispatcher.",
        SetupState.NoApps => "Next: add a browser under Browsers in the picker. App Dispatcher has none to open links with.",
        SetupState.NotDefault => "Last step: Windows still decides how links open, and only you can change that. Click the button, then choose App Dispatcher for HTTP and HTTPS.",
        _ when defaultBrowser == null => "All set: links show the picker unless a rule below says otherwise. Add a rule to send a site straight to one browser.",
        _ => $"All set: links open in {defaultBrowser} unless a rule below says otherwise. Add a rule to show the picker for a site.",
    };

    /// <summary>Short note shown beside a file type in the list.</summary>
    public static string ListNote(SetupState state) => state switch
    {
        SetupState.NoApps => "⚠ no apps",
        SetupState.NotDefault => "⚠ not default yet",
        SetupState.Ready => "✓ default",
        _ => "",
    };
}
