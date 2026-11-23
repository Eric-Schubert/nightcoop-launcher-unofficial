namespace NightCoopLauncher.App.Services;

public interface IUserPrompts
{
    bool Confirm(string title, string message);

    string? PickFolder(string title);

    string? PickSaveLocation(string title, string suggestedFileName);

    void OpenInShell(string target);
}
