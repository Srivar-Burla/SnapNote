# SnapNote

Snap it, mark it, share it with AI or save it.

Take a screenshot, highlight the spots you care about, write a comment on each one, then paste it all into any AI chat (Claude, ChatGPT, Gemini…) with a single Ctrl+V, or save it as a note.

## Use it

1. Start it: double-click **Run SnapNote.bat** the first time (it builds SnapNote.exe), then **SnapNote.exe**, which carries the SnapNote logo. It sits in the system tray as the logo: a black tile with crop marks and a red note bubble.
2. Press **Win+Shift+C** (or left-click the tray icon). The screen freezes.
   - **Drag** to select an area, **click** to grab the window under the cursor, **Enter** for the whole screen, **Esc** to cancel.
3. In the editor:
   - Drag on the image to draw a **Highlight**. A frosted comment bubble pops up next to it, numbered to match. Type your comment; **Enter** finishes it, **Shift+Enter** adds a line.
   - Draw as many highlights as you like: each gets its own bubble and number.
   - Click a bubble (or inside its highlight) to edit it, drag a bubble to move it, and click its **×** to remove that highlight.
   - **Arrow** (Alt+A) draws an arrow, and it gets its own numbered comment bubble at its tail, just like a highlight. Click an arrow to show its three round nodes: pull the **middle** node to curve the whole arrow one way, or pull node **1 or 3** to give it a double bend. Pulled nodes turn solid; click one without dragging to let it go back.
   - The dots pick the colour (ink, blue, red). **Ctrl+Z** undoes the last shape.
   - The box at the bottom is for your overall question (optional).
4. Then choose:
   - **Copy for chat** (Ctrl+Enter): click into your AI chat box and press **Ctrl+V once**. The screenshot (with highlights and bubbles) pastes, then your question and a numbered list of your comments are pasted into the text box for you. Press Enter to send.
   - **Copy image only** (Ctrl+Shift+C): just the marked-up screenshot.
   - **Save** (Ctrl+S): keeps the marked-up screenshot as a PNG plus a .txt with your question and comments, in **Pictures\SnapNote**. Tray icon → **Open saved notes** opens that folder.

What gets pasted as text looks like this:

```
Why is this button greyed out?

About the numbered areas in the screenshot:
1. This header wraps onto two lines.
2. Why is this greyed out?
```

Other options:
- **Put note inside the image**: your overall question is written as a caption under the screenshot instead, so one plain paste carries everything (the comments are already in the bubbles). The app remembers this switch.
- If a capture is too small to hold the bubbles, they sit beside it and the copied image grows to fit them.

## Little animations

SnapNote uses a few quick line drawings, each played once when you do the thing it belongs to:

- **Drawn-in marks:** when you finish a highlight or arrow, its line draws itself, the number badge drops into place and the comment bubble unfolds out of its pointer.
- **Paper plane:** Copy for chat folds the marked-up screenshot into a paper plane that flies to the corner where "Ready to paste" appears.
- **Tray:** Save drops the screenshot into a little tray before "Saved" appears.
- **Waiting bubble:** while SnapNote waits for your Ctrl+V, the red bubble on its tray icon breathes. After the paste it shows a tick for a moment. To see it, keep the SnapNote icon on the taskbar: click the ^ by the clock and drag the SnapNote icon down next to the others.

They are off when Windows animation effects are off (Settings > Accessibility > Visual effects), or when you set `Animations=false` in settings.ini.

## The hotkey

Default is **Win+Shift+C**. If another app already holds it, SnapNote tries Win+Shift+X, Ctrl+Alt+A and Win+Alt+A in turn and tells you which one it got. (Win+Shift+A, the old default, turned out to be taken by Windows on this PC.) Pick your own in settings.ini.

## How "one Ctrl+V pastes both" works (SmartPaste)

Chat apps can't reliably take an image and text from the clipboard at once, so after Copy for chat the clipboard holds only the image. The app watches for your next Ctrl+V (for up to 2 minutes). When it sees it, it waits 0.4 s, puts your text on the clipboard and presses Ctrl+V once more for you. It only listens for that single keypress and stops right after.

If the text ever lands *instead of* the image, raise `SmartPasteDelayMs` in settings.ini (try 700). To turn the trick off, set `SmartPaste=false`; image and text then go on the clipboard together.

## Settings

Tray icon → right-click → **Edit settings** opens `settings.ini`: hotkey, SmartPaste timing, default colour, theme (auto/light/dark), where Save puts files (`SaveFolder`, empty = Pictures\SnapNote) and whether every copy is also saved (`AlsoSaveOnCopy`). After editing, quit from the tray menu and start the app again.

## Start with Windows (optional)

Run **Add to Startup.bat** (adds one shortcut to your personal Startup folder). **Remove from Startup.bat** undoes it.

## Under the hood

- One small .exe built from the C# in `src\` by **setup.bat**, using the compiler that ships with Windows (.NET Framework 4.8). Nothing is downloaded or installed.
- Look and feel follows the Linework design system (tokens, buttons, chips, switch, input). Fonts fall back to Segoe UI Variable / Cascadia Mono when Hanken Grotesk / JetBrains Mono aren't installed.
- Files: `SnapNote.exe`, `settings.ini` (made on first run), `src\`, the .bat scripts.
- Remove: quit from the tray, run Remove from Startup.bat if you used it, delete the folder.
