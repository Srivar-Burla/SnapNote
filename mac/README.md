# SnapNote for Mac

**Snap it, mark it, share it with AI or save it.**

Take a screenshot, highlight the spots you care about, write a comment on each one, then paste it all into any AI chat (Claude, ChatGPT, Gemini…) with a single ⌘V, or save it as a note. SnapNote lives in the menu bar. Everything stays on your Mac.

> **Tested on a Mac** (November 2026): setup, capture, marking up, copy and save work. If something misbehaves on your Mac, the checklist at the end says what to look at, and `snapnote.log` in this folder records each capture.

## What you need first

1. **macOS 12.3 (Monterey) or newer.** Apple silicon or Intel. On macOS 14 (Sonoma) and newer SnapNote captures the screen with Apple’s ScreenCaptureKit.
2. **Apple's Command Line Tools** (free, from Apple). If they are missing, setup opens Apple's installer for you. Run setup again when that finishes. You do not need the full Xcode.

Nothing else is downloaded. No Python, no Homebrew.

## Set up (once)

1. Unzip and put the **SnapNote** folder somewhere it can stay, for example **Applications** or **Documents**.
2. Right-click **Set up SnapNote.command** and choose **Open**, then **Open** again. (macOS asks because the script isn't from the App Store.)
3. A Terminal window builds **SnapNote.app** in this folder (about 30 seconds) and opens it.
4. **Allow Screen Recording.** The first capture asks for it. Turn SnapNote on under **System Settings > Privacy & Security > Screen & System Audio Recording** (called **Screen Recording** on older macOS). Then quit SnapNote from its menu bar icon and open **SnapNote.app** again.
5. **Optional, for one-⌘V pasting:** the first time you use **Copy for chat**, macOS asks to let SnapNote control your computer. Turn it on under **Privacy & Security > Accessibility**. Without it, SnapNote puts the image and text on the clipboard together instead.

Leave SnapNote.app inside the SnapNote folder, because its `settings.ini` sits next to it.

## Use it

1. Press **⌃⇧S** (Control+Shift+S), or click the menu bar icon and choose **Capture**. The screen freezes.
   - **Drag** to select an area, **click** to grab the window under the pointer, **Return** for the whole screen, **Esc** to cancel.
2. In the editor (it shows in the Dock while it's open):
   - Drag on the image to draw a **Highlight**. A frosted comment bubble pops up next to it, numbered to match. Type your comment. **Return** finishes it, and **Shift+Return** adds a line.
   - **Arrow** draws an arrow, and it gets its own numbered bubble at its tail. Click an arrow to show its three round nodes. Pull the **middle** node to curve the whole arrow one way, or node **1 or 3** to give it a double bend. Pulled nodes turn solid, and clicking one without dragging lets it go back.
   - Click a bubble (or inside a highlight) to edit it, drag a bubble to move it, and click its **×** to remove that mark. The dots pick the colour (ink, blue, red). **⌘Z** undoes the last mark.
   - The box at the bottom is for your overall question (optional).
3. Then choose:
   - **Copy for chat** (⌘Return): click into your AI chat box and press **⌘V once**. The screenshot (with marks and bubbles) pastes, then your question and a numbered list of your comments are pasted for you.
   - **Copy image only** (⌘⇧C): just the marked-up screenshot.
   - **Save** (⌘S): the marked-up screenshot as a PNG plus a .txt with your question and comments, in **Pictures/SnapNote**. The menu bar icon's **Open saved notes** opens that folder.

What gets pasted as text looks like this:

```
Why is this button greyed out?

About the numbered areas in the screenshot:
1. This header wraps onto two lines.
2. This should point here instead
```

**Put note inside the image** writes your overall question as a caption under the screenshot instead, so one plain paste carries everything.

## Little animations

SnapNote plays a few quick line drawings, each once, when you do the thing it belongs to:

- **Drawn-in marks:** a finished highlight or arrow draws its own line, the number badge drops in and the bubble unfolds out of its pointer.
- **Paper plane:** Copy for chat folds the marked-up screenshot into a paper plane that flies to the corner where "Ready to paste" appears.
- **Tray:** Save drops the screenshot into a little tray before "Saved" appears.
- **Waiting bubble:** while SnapNote waits for your ⌘V, the bubble on its menu bar icon breathes. After the paste it shows a tick for a moment.

They are off when **Reduce motion** is on (System Settings > Accessibility > Display), or when you set `Animations=false` in settings.ini.

## Start with your Mac (optional)

**System Settings > General > Login Items**, click **+** and pick **SnapNote.app**.

## Settings

Menu bar icon > **Edit settings** opens `settings.ini`: hotkey, SmartPaste timing, default colour, theme (auto, light or dark), where Save puts files (`SaveFolder`, empty = Pictures/SnapNote), and whether every copy is also saved (`AlsoSaveOnCopy`). After editing, quit SnapNote from the menu bar and open it again.

The hotkey is **Control+Shift+S** because ⌘⇧3, 4 and 5 belong to the Mac's own screenshot tool, and Control shortcuts rarely clash with apps. If another app has it, SnapNote tries Control+Option+S, Control+Shift+X and Control+Option+A, and tells you which one it got.

## If something goes wrong

- **"cannot be opened because it is from an unidentified developer":** right-click the file, choose **Open**, then **Open**. Or allow it in **System Settings > Privacy & Security**.
- **Setup says the build failed:** copy the red messages from Terminal and send them to Claude. This version hasn't been built on a Mac yet, so a small fix may be needed.
- **The captured screen is black:** macOS isn't letting SnapNote see the screen, usually because SnapNote was rebuilt after you allowed it (macOS treats the rebuilt app as a new one). In **System Settings > Privacy & Security > Screen & System Audio Recording**, select SnapNote and remove it with the **minus** button, add **SnapNote.app** again with **plus** and turn it on, then quit SnapNote from the menu bar and open it again. Running setup again also clears the old entry, so macOS asks afresh. `snapnote.log` in this folder records each capture (method, size and brightness), which helps if it still fails.
- **The capture shows only your wallpaper, or a "Could not capture" message:** Screen Recording isn't allowed yet. See step 4 above, then quit and reopen SnapNote.
- **Pressing ⌘V pastes only the image:** allow SnapNote under **Accessibility** (step 5), or raise `SmartPasteDelayMs` in settings.ini (try 700).
- **After running setup again, permissions stop working:** rebuilding makes a new app as far as macOS is concerned. Remove SnapNote from Screen Recording and Accessibility, add it again, and reopen it.

## What to check on your Mac (first run)

1. Setup finishes without a red `[!]` line, and **SnapNote.app** appears in the folder with the SnapNote icon (black tile, crop marks, red note bubble).
2. A crop-marks icon appears in the menu bar, and a card at the top right says "SnapNote is running". There is no Dock icon yet.
3. After allowing Screen Recording and reopening, **⌃⇧S** freezes the screen with a dark tint and a hint pill at the top. Dragging shows an ink outline with square handles and a pixel size. Hovering over a window outlines it.
4. Releasing opens the editor at the image's actual size, and SnapNote shows in the Dock while it's open. The top bar shows Highlight, Arrow, three colour dots, Undo and the tagline.
5. Drawing a highlight dims the rest of the image slightly and pops up a frosted, see-through bubble with a pointer and the number 1. Typing goes into the bubble.
6. Drawing an arrow gives it bubble 2. Clicking the arrow shows three nodes. Dragging the middle node curves the arrow, and dragging an end node makes an S-shape.
7. **⌘Return**, then **⌘V** in a chat box: the picture pastes, and a moment later the numbered text pastes (after allowing Accessibility).
8. **Save** puts a PNG and a .txt in Pictures/SnapNote, and the picture has the bubbles in it.
9. The animations play: a new highlight draws itself in, Copy for chat sends a paper plane to the top-right corner, Save drops the picture into a tray, and the menu bar bubble breathes until you paste.
10. With two displays, capture works on the display under the pointer. (The display numbering is a best guess.)

## What's in this folder

- `Set up SnapNote.command` one-time setup: builds `SnapNote.app` from `main.swift` with Apple's Swift compiler
- `main.swift` the whole app (AppKit): menu bar icon, hotkey, capture, editor, bubbles, copy and save. Styled with the Linework design system. Fonts fall back to the Mac system fonts when Hanken Grotesk and JetBrains Mono aren't installed.
- `AppIcon.icns`, `app.png` the app icon
- `settings.ini` is made on first run, next to SnapNote.app
