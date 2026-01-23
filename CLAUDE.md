# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build (Debug)
dotnet build ManageNotepadWindows/ManageNotepadWindows.csproj

# Build (Release)
dotnet build ManageNotepadWindows/ManageNotepadWindows.csproj -c Release

# Run
dotnet run --project ManageNotepadWindows/ManageNotepadWindows.csproj
```

## Architecture

**Single-form WinForms application** targeting .NET 10 that manages open Notepad instances and browser tabs.

### Core Components

- **NotepadsManager.cs**: Main form containing all UI logic, window enumeration, and P/Invoke declarations
- **Program.cs**: Application entry point with PerMonitorV2 DPI awareness

### Key Patterns

**Window Enumeration**: Uses Win32 `EnumWindows` P/Invoke to discover open windows. Notepad windows are identified by class names `Notepad` (classic) or `CascadiaWindow` (Windows 11). Browser windows use `Chrome_WidgetWin_1` (Chromium-based) or `MozillaWindowClass` (Firefox).

**Content Extraction**: Multi-strategy approach with timeouts to prevent UI hangs:
1. `WM_GETTEXT` via `SendMessageTimeout` to Edit controls (fastest, classic Notepad)
2. UI Automation `ValuePattern`/`TextPattern` (modern Notepad, Windows 11)
3. Recursive child window search for various RichEdit classes

**UI Library**: Uses AntdUI (v2.2.7) for modern-looking WinForms controls (Button, Input, Checkbox).

**Virtualized DataGridView**: Both grids use `VirtualMode=true` with `CellValueNeeded` events for efficient display of window lists.

### Data Storage

Unsaved notepad content is backed up to `%APPDATA%\NotepadBackups` every 5 minutes and restored on application startup.

## Guidelines

- Provide minimal diffs and ensure compile-clean output
- All P/Invoke calls that communicate with external windows must use `SendMessageTimeout` with `SMTO_ABORTIFHUNG` to prevent hangs
