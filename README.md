# TypeFlow

A Windows text expander and autocorrect utility that works system-wide. TypeFlow monitors your keyboard input and automatically replaces shortcuts with their expansions in any application.

## Features

- **System-wide text expansion** — works in any Windows application (Notepad, browsers, chat apps, etc.)
- **Autocorrect** — built-in dictionary of common abbreviations and autocorrections
- **Custom shortcuts** — add, edit, and delete your own shortcut/expansion pairs
- **CSV import/export** — bulk manage shortcuts via CSV files
- **Case-insensitive matching** — `brb` matches `BRB`, `Brb`, etc.
- **Ctrl+Z undo** — custom undo restores your original shortcut text
- **System tray** — runs in the background, toggle on/off from the tray menu
- **Single-line input toggle** — optionally expand only in rich/multiline editors

## How It Works

1. Type a shortcut (e.g., `brb`)
2. Press **Space** or **.** to trigger expansion
3. The shortcut is replaced with the full expansion (e.g., `be right back `)

## Installation

1. Download the latest release or build from source
2. Run `TypeFlow.exe` as **Administrator** (required for the global keyboard hook)
3. TypeFlow will appear in the system tray

## Building from Source

### Prerequisites

- [Visual Studio 2019+](https://visualstudio.microsoft.com/) or [.NET SDK 5.0+](https://dotnet.microsoft.com/download)
- Windows 10/11

### Build

```bash
dotnet build TypeFlow.sln -c Release
```

Output: `src/TypeFlow.App/bin/Release/net5.0-windows/TypeFlow.exe`

### Run Tests

```bash
dotnet run --project tests/TypeFlow.Tests/TypeFlow.Tests.csproj
```

## Project Structure

```
TypeFlow/
├── src/
│   ├── TypeFlow.App/          # WPF application (UI, tray icon)
│   └── TypeFlow.Core/         # Core engine
│       ├── Engine/            # Shortcut matching, buffer, expansion state
│       ├── Focus/             # Active window/field detection
│       ├── Hook/              # Global keyboard hook
│       ├── Input/             # Text injection, key mapping
│       ├── Interop/           # Win32 P/Invoke declarations
│       └── Storage/           # JSON/CSV shortcut persistence
└── tests/
    └── TypeFlow.Tests/        # Unit tests
```

## License

MIT
