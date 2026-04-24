# Pink Salt – Claudia Edition

> **Author:** PocketPixel

Pink Salt is a cozy, pink-and-white, Obsidian‑inspired note‑taking app for Windows. This open-source "Claudia Edition" adds rich Markdown editing, note graph visualization, wiki‑style links, and a full soft pink theme.

---

## Features

- **Markdown editor with live preview**
  - Write in Markdown using an embedded AvalonEdit editor.
  - Live preview renders in real time alongside the editor (150 ms debounce for smooth typing).
  - GitHub-style extras via Markdig:
    - Headings, bold, italics, inline code, blockquotes.
    - Task lists: `- [ ]` and `- [x]`.
    - Tables, footnotes, emojis, and more.
- **Inline formatting shortcuts**
  - Toolbar buttons and keyboard shortcuts to wrap selected text or insert at the caret:

    | Action | Button | Shortcut |
    | --- | --- | --- |
    | Bold | **B** | `Ctrl+B` |
    | Italic | **I** | `Ctrl+I` |
    | Inline code | **Code** | `Ctrl+K` |
    | Wiki link | **[[Link]]** | `Ctrl+L` |
    | Heading (H1) | **# H1** | — |
    | Bullet list | **• List** | — |

  - Pressing **Enter** on a list line (`- item`, `1. item`) continues the list automatically, with numbered lists incrementing.
- **Note rename**
  - Edit the title directly in the title bar — renames save on Enter or focus loss.
  - Use the **Rename** toolbar button (or press **F2**) for a modal rename dialog.
- **Pink Salt wiki links**
  - Use `[[Note Title]]` to link between notes.
  - Links render as styled anchors in the preview.
  - Wiki links are indexed by the graph view.
- **Graph view**
  - Visualize notes and their `[[links]]` as a circular graph.
  - Click a node to open that note.
- **Folders & tags**
  - Organize notes into folders and add comma‑separated tags.
  - Filter by folder from the scrollable left sidebar.
- **Image insertion**
  - Click **🖼️ Image** to copy a local image into the notes folder and insert Markdown syntax at the caret.
- **Backup & export**
  - **Backup**: one‑click backup of all notes to a timestamped folder inside `Backups`.
  - **Export**: choose any folder and export a copy of the latest backup there.
- **Full pink & white theme**
  - Rounded buttons, pink accent bar on selected items, soft‑pink borders — the entire UI and Markdown preview use only soft pinks and white.

---

## Running the App

### Option 1 – Use the standalone `.exe`

The repository includes a self‑contained Windows build.

1. Locate the published executable:

   ```text
   PinkSalt/PinkSalt/bin/Release/net9.0-windows/win-x64/publish/PinkSalt.exe
   ```

2. Double‑click `PinkSalt.exe` to run.
3. You can copy this folder anywhere or zip it to share with others.

> The standalone build is self‑contained: users do **not** need to install .NET separately (target: `win-x64`).

### Option 2 – Run from source (developers)

Requirements:

- **Windows 10/11**
- **.NET SDK 9.0**

From the `PinkSalt` folder (where the solution lives):

```powershell
# Run the WPF app from source
dotnet run --project "PinkSalt/PinkSalt.csproj"
```

To build in Release mode:

```powershell
dotnet build "PinkSalt/PinkSalt.csproj" -c Release
```

To create a fresh standalone single‑file `.exe`:

```powershell
dotnet publish "PinkSalt/PinkSalt.csproj" -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

Output will be in:

```text
PinkSalt/PinkSalt/bin/Release/net9.0-windows/win-x64/publish/
```

---

## Running the Tests

The solution includes an xUnit test project (`PinkSalt.Tests`) covering core logic independently of the WPF UI.

```powershell
dotnet test "PinkSalt.Tests/PinkSalt.Tests.csproj"
```

### What's tested

| Area | Coverage |
| --- | --- |
| `NoteManager` | Save, load, delete, `GetFolders`, `CreateBackup` — including corrupt-file handling, sort order, and edge cases |
| `Note` model | Default values, property setters, `INotifyPropertyChanged` |
| Tag parsing | Split/trim, empty filtering, trailing commas |
| Search filter | Title, content, and tag matching; case‑insensitivity; partial match |
| Wiki‑link regex | Single/multiple links, embedded text, special characters, empty brackets |
| List continuation | `"-"`, `"*"`, numbered increment, plain text |

---

## How Your Notes Are Stored

Notes are saved as individual JSON files in your **Documents** folder:

```text
%USERPROFILE%\Documents\PinkSaltNotes\
```

Backups live in:

```text
%USERPROFILE%\Documents\PinkSaltNotes\Backups\
```

Each note file is named by its internal GUID (`<id>.json`) and contains the note's ID, title, content, folder, tags, and timestamps.

---

## Basic Usage

1. **Create a note** — click **+ New** in the left sidebar.

2. **Edit & format** — type in the Markdown editor; use toolbar buttons or keyboard shortcuts.

3. **Rename a note** — click the title bar at the top of the editor and type a new name (press Enter to confirm), or click the **Rename** button / press **F2** for a dialog.

4. **Organize** — set the **Folder** field and add comma‑separated **Tags**. Filter by folder using the sidebar list.

5. **Link notes** — type `[[Note Title]]` to create a wiki link. The graph view shows all connections.

6. **Save** — notes auto‑save when you switch between notes or close the app. Click **Save** to save manually at any time.

7. **Back up** — use **Backup** for a quick local snapshot, or **Export** to copy a backup to any folder.

---

## Technologies Used

- **.NET 9 WPF** (`net9.0-windows`)
- **AvalonEdit 6.3** — syntax‑aware Markdown editor
- **Markdig 0.43** — Markdown parsing with advanced extensions
- **xUnit 2.9** — unit test framework

---

## Project Structure

```text
PinkSalt/
├── PinkSalt/                  # WPF application
│   ├── App.xaml               # Global pink theme resources
│   ├── App.xaml.cs
│   ├── MainWindow.xaml        # UI layout
│   ├── MainWindow.xaml.cs     # Note, NoteManager, and MainWindow logic
│   └── PinkSalt.csproj
├── PinkSalt.Tests/            # xUnit test project
│   ├── NoteManagerTests.cs
│   ├── NoteTests.cs
│   ├── NoteLogicTests.cs
│   └── PinkSalt.Tests.csproj
├── PinkSalt.sln
├── LICENSE.txt
└── README.md
```

---

## Editing / Contributing

1. Open `PinkSalt.sln` in **Visual Studio 2022+** or **Rider**.
2. Restore NuGet packages (AvalonEdit, Markdig, xUnit).
3. Build and run the `PinkSalt` project.
4. Run `dotnet test` to verify the test suite before submitting changes.

---

## License

© 2026 PocketPixel. Released under the CC0 1.0 Universal license — see `LICENSE.txt`.
