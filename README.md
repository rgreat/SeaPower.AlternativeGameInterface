# Alternative Game Interface

A Sea Power mod that makes the game's windows smarter (formerly "Smart Windows").

**Version 1.8.0**

## Features

1. Remembers the position and size of chosen windows and restores them when a window is opened again.
2. Double-click a window's title bar to maximize it, double-click again to restore it. A window closed maximized opens maximized next time.
3. Clicking a window brings it to the front of the other windows. Mission briefings and scripted messages of the game always stay on top; a maximized Tactical Display or World View always stays at the back.
4. Windows snap to the screen edges and to each other while you move or resize them.
5. Several windows of one kind (e.g. Weapon Reference) open slightly shifted, so they don't cover each other.
6. Lets you drag any window partly outside the visible screen area.
7. Remembered windows have no minimum size of their own.
8. Remembers which mission windows were open or closed and restores that at the next mission start.
9. Thin frame around draggable windows and the game's tooltips for better edge visibility. Customizable.
10. In-game settings window (the gear button in the title bar of a remembered window): tabs, groups, hints, language selector.
11. Background pictures of the main menu and loading screens: only the mods' (default), mixed with the game's, or only the game's.
12. Hides "_Custom Mission" in the SCENARIOS list of the main menu.
13. Mission editor: the file picker for opening a mission starts in the folder of the last opened mission; *.ini files are light blue in its file pickers.
14. Optional: deletes the game's old log files at start (off by default).
15. Enhances the UI of the Unit Reference (encyclopedia) window:
   - Adds a movable splitter between the unit tree and the unit data; the tree width is remembered.
   - The chosen country, the search panel (category, filters, search text) and whether the unit tree is shown are remembered.
   - The Show/Hide unit tree button also hides the search panel and adjusts the window width.
   - A tidier unit tree: classes sorted by service date, a unit's loadout variants (Early, Late, ASW...) gathered under its class.
   - The tree scrolls to the selected unit.
   - The mouse wheel scrolls the tree only while the cursor is over it.
   - A click on a class selects its earliest variant (optional).
   - The unit the window was opened for stays in the tree, even if the filters would hide it.
   - The country list shows only countries that have units for the current search.
   - Without "Search by name only" the search also looks into descriptions and unit ids (<Id>.ini).
   - Tooltips with a short help on the highlighted links to weapons, sensors and systems.
   - Loadout selector for ships, submarines and land units too, not only aircraft.
   - "Armed only" check box: hides unarmed units (rafts, targets, tankers, civilian ships...).
16. Unit Status window:
   - Reference tab: the Unit Reference of the selected unit right in the Unit Status window.
   - Damage Control tab: adjustable minimum height, so the Auto DC status panel at the bottom stays visible.
   - The full name of the unit in the header.
17. Right click on the flag or name in the unit panel opens the unit's context menu.
18. Fixed: empty sensor / age fields in the contact Information window.
19. Campaign, "Strict loadouts" (optional): a check box on the campaign start screen. The Task Force Builder then offers ship versions only for ships available in the mission or already owned. Stored per campaign; the settings window opened from the Task Force Builder starts on the Campaign tab.
20. Game messages in the bottom right corner no longer block clicks on buttons under them.
21. Localization: all texts of the mod's settings window and hints in the 9 languages of the game (en, ru, cn, de, es, fr, ko, ja, vn); the language of the game or [General] Language in the .cfg. The texts are in the lang folder of the mod and can be edited.
22. Optional desktop mode ("Enable desktop mode"): Tactical Display and World View work as ordinary windows (free move and size, snapping, order and level like the others). The "Toggle fullscreen minimap" key (Tab by default) switches between 2 - 5 desktops (the number is a setting): each keeps the position and size, open / closed, opacity, scale and level of the windows and whether the World View is open; the active desktop is remembered. The desktops start as built-in arrangements of the mod (1: World View on top, 2: Tactical Display on top - on an ultrawide screen three columns of 30 / 40 / 30 % of the width with the main view in the middle, 3: map on the whole screen, 4: 3D view on the whole screen, 5: both side by side); the "Desktops" page of the "Desktop mode" tab in the settings window shows what is saved for each window of each desktop - edit it or bring it back to the built-in arrangement. A key (Insert by default; never one the game uses) opens and closes the World View. The World View picture is rendered into a texture of the window's own size, pixel for pixel; both windows open and close independently of each other. A topographic backdrop (AlternativeGameInterface_Backdrop.jpg next to the dll - replace it with any picture) is behind them. Backspace (photo mode) shows the 3D view on the whole screen. The compass tape is moved into the World View window and is white or black by the picture behind it.
23. A small arrow next to the gear of a window opens its look menu: opacity, scale and level (bottom / default / top). The Tactical Display and the World View start at the bottom level.
24. Windows docked to a screen edge can still be resized from that edge.
25. The mission's time panel can be dragged; double-click puts it back. Its place is kept per desktop; in desktop mode it starts a little lower and to the left.
26. Unit Status: a "Track State" tab for a selected contact of another side (the contact's Information).
27. Campaign: the "Upgrade ship..." button in the Task Force Builder; a damaged ship can be dismissed (the repair cost is deducted); the ship's loadouts are sorted by date; the price of a hull swap is counted from the roster.
28. The News window of the main menu gets the mod's gear, frame and remembered position; the game's resize cursors work outside a mission.
29. Optional "Keep map view on resize" (the Tactical Display tab, shown when the settings are opened from the map window): when the Tactical Display window is resized, its zoom follows, so the area that was visible stays visible.
30. Event Log: every track number is a link, not only some of them (option); a click on a contact or unit link also centres the Tactical Display on it (option, the Event Log tab of the settings window).
31. Settings entry "Game Interface" in the main menu (under Mod Manager) and in the pause menu (under Options): opens the settings window with every tab, without a game window behind it. With several of the author's mods installed it becomes "Alternative Mods" with a list.
32. The settings gear and the arrow of the window look menu sit among the buttons of the window's title bar, so they never lie over buttons that other mods add there.

## Compatibility

- Supports Sea Power version 0.8.7 (Release and Stable Beta).
- Requires Anchor Chain.
- Can be switched on/off in the game's Mods menu.

## Install

1. Install the Anchor Chain preloader as described in the [documentation](https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html).
2. Subscribe to this mod.
3. Restart the game.

## Usage

- Windows remembered by default: Tactical Display, World View, Unit Reference, Unit Status, Event Log, Formation Manager, Task Force Builder, Weapon Reference.
  Move or resize them as usual; they come back where you left them.
- To remember another window, add its name in the settings window (Remembered Windows).
- Most features can be turned off or customized in the settings window (the gear in the title bar of a remembered window). They are stored in:
  `Sea Power/Sea Power_Data/StreamingAssets/user/AlternativeGameInterface/AlternativeGameInterface.cfg`

P.S. Highly recommended to use with the [Camera Offset](https://steamcommunity.com/sharedfiles/filedetails/?id=3676689138) mod.
