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
        SetupState.NotRegistered => $"Next: click Register above, so that Windows can offer App Dispatcher for {extension} files.",
        SetupState.NoApps => $"Next: add at least one app below. Until then, App Dispatcher has nothing to open {extension} files with.",
        SetupState.NotDefault => $"Last step: Windows still decides how {extension} files open, and only you can change that. Click the button, then choose App Dispatcher for {extension}. Or double-click a {extension} file, choose App Dispatcher and click Always.",
        _ when apps == 1 => $"All set: {extension} files open straight in this app. Add another app to get the picker.",
        _ => $"All set: double-clicking a {extension} file shows the picker.",
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
