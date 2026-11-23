# NightCoop Launcher (unofficial)

A small Windows launcher that keeps the [NightCoop](https://nightcoop.net) co-op mod for Cyberpunk 2077 up to date:
it checks for a new build, installs it with one click, backs up your saves and starts the game.

## Unofficial, and with full respect

This is a fan project. It is **not** made by, affiliated with or endorsed by the NightCoop team or CD PROJEKT RED.

NightCoop itself is the work of its developers, and all credit for the mod belongs to them. I have nothing but
respect for what they have built. This launcher only downloads the builds they publish on their own site, exactly
as their download page offers them, and changes nothing about the mod.

- The real project: **<https://nightcoop.net>**
- Downloads and release notes: <https://nightcoop.net/download.html>

If you enjoy the mod, please support its developers through the links on their site. If you are part of the
NightCoop team and would rather this launcher worked differently or did not exist, please open an issue.

## Why this exists

I play NightCoop with friends. Everyone in a session needs the same build, so every release meant each of us
downloading the archive, unpacking it into the game folder and hoping nobody was a version behind. I wanted that
to be one button, so all of us can simply play.

The launcher was developed with the help of [Claude](https://claude.com/claude-code), Anthropic's AI assistant.

## What it does

| Page | Contents |
| --- | --- |
| Play | Status at a glance and one button: install or update first, play once the mod is current |
| Updates | The update in five steps with progress, release notes, optional automatic updates, version history with rollback |
| Backups | Back up, restore, export and delete saves; automatic backups before updates and before game starts |
| Settings | Language, game folder, backup location |

- Finds the game in Steam, GOG and Epic and picks the archive built for your game version.
- Verifies every download against the SHA-256 published by NightCoop before touching the game folder.
- Puts the previous mod files back if an installation fails halfway.
- Checks the installed files and repairs them on request.
- Interface in English and German.

## Install

Download `NightCoopLauncher.exe` from the [latest release](../../releases/latest) and run it. It is a single file
and needs no .NET runtime. Windows may warn about an unknown publisher because the file is not code-signed.

You need a licensed copy of Cyberpunk 2077 in a version the current NightCoop build supports.

## Build

Requires the .NET 9 SDK on Windows.

```powershell
dotnet test
dotnet publish src\NightCoopLauncher.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
```

| Project | Contents |
| --- | --- |
| `src/NightCoopLauncher.Core` | Release list, game detection, installation, file verification, save backups; no user interface |
| `src/NightCoopLauncher.App` | WPF interface (MVVM), language dictionaries, fonts |
| `tests/` | xUnit tests of the logic and of the flows behind the interface |

## Release

A release is created by pushing a version tag:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The `Release` workflow then runs the tests, builds `NightCoopLauncher.exe` with that version and publishes a
GitHub release with the file attached. A tag with a suffix, such as `v1.1.0-beta.1`, becomes a pre-release.

## Add a language

Each language is one dictionary in `src/NightCoopLauncher.App/Languages/<code>.json`. Copy `en.json`, name it
after the language code (for example `fr.json`), set `language.name` to the name shown in the settings and
translate the values. After a build the language appears in the settings. Keys missing from a dictionary fall
back to English, and a test checks that all dictionaries carry the same keys.

## Where things are stored

| What | Where |
| --- | --- |
| Settings | `%LOCALAPPDATA%\NightCoopLauncher\settings.json` |
| Save backups | `%LOCALAPPDATA%\NightCoopLauncher\SaveBackups`, unless changed in the settings |
| Installed build | `NightCoop.updater.json` in the game folder |

Backups made before game starts are limited to the number set on the Backups page. Manual backups and backups
made before updates stay until you delete them. Restoring a backup first backs up the current saves, so it can
be undone.

## Credits

- [NightCoop](https://nightcoop.net) by its developers.
- Fonts: Russo One and IBM Plex, both under the SIL Open Font License (see `src/NightCoopLauncher.App/Fonts`).

## License

The launcher's code is released under the [MIT License](LICENSE).

The license covers this launcher only. It does not cover the NightCoop mod, which is not part of this repository
and remains the work of its developers, nor the bundled fonts, which keep their own license. It grants no rights
to the names NightCoop or Cyberpunk 2077. Cyberpunk 2077 is a trademark of CD PROJEKT S.A.
