# SnapNote

A friendlier Snipping Tool for when you are working with AI tools.

**Snap it, mark it, share it with AI or save it.**

SnapNote is a small screenshot tool for talking to AI chats. Press a hotkey, select part of the screen, then mark it up:

- **Highlights** with a frosted comment bubble each, numbered to match.
- **Arrows** with three nodes you can pull: the middle one curves the arrow, an end one gives it a double bend. Arrows get numbered bubbles too.
- An optional **overall question** for the whole screenshot.

Then **Copy for chat** and press paste once in Claude, ChatGPT, Gemini or any chat box: the marked-up screenshot goes in first, then your question and a numbered list of your comments. Or **Save** it as a PNG plus a .txt note.

Small hairline animations (the marks draw themselves in, a paper plane on copy, a tray on save, a breathing bubble while it waits for your paste) follow the Linework design system and switch off with the system's reduce-motion setting.

| | Windows | Mac |
| --- | --- | --- |
| Folder | [`windows/`](windows) | [`mac/`](mac) |
| Built with | C# WinForms, compiled by `setup.bat` with the C# compiler that ships with Windows | Swift and AppKit, compiled by `Set up SnapNote.command` with Apple's Command Line Tools |
| Lives in | the system tray | the menu bar |
| Hotkey | Win+Shift+C | Control+Shift+S |
| Status | tested on Windows 11 | written on Windows, not yet built or tested on a Mac |

Nothing is downloaded or installed beyond the compiler each system already has (or Apple's free Command Line Tools on a Mac). Each folder's README has setup steps.

## Layout

```
windows/   src/*.cs, setup.bat, Run SnapNote.bat, Add/Remove from Startup.bat, README.md
mac/       main.swift, Set up SnapNote.command, AppIcon.icns, README.md
```

Built files (`SnapNote.exe`, `SnapNote.app`) and the per-user `settings.ini` are not in the repository; the setup scripts make them.

## License

MIT. See [LICENSE](LICENSE).
