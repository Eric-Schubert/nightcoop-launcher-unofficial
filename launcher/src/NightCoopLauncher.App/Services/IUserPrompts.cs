namespace NightCoopLauncher.App.Services;

public interface IUserPrompts
{
    bool Confirm(string title, string message);

    string? PickGameFolder();

    void OpenInShell(string target);
}
