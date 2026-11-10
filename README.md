# Alternative Game Interface

A Sea Power mod that makes the game's windows smarter (formerly "Smart Windows").

**Version 1.9.0**

## Features

1. Remembers the position and size of chosen windows and restores them when a window is opened again.
2. Double-click a window's title bar to maximize it, double-click again to restore it. A window closed maximized opens maximized next time.
3. Clicking a window brings it to the front of the other windows. Mission briefings and scripted messages of the game always stay on top; a maximized Tactical Display or World View always stays at the back.
4. Windows snap to the screen edges and to each other.
5. Several windows of one kind (e.g. Weapon Reference) open slightly shifted.
6. Lets you drag any window partly outside the visible screen area.
7. Remembered windows have no minimum size of their own.
8. Remembers which mission windows were open and restores that at the next mission start.
9. Thin frame around draggable windows and the game's tooltips for better edge visibility. Customizable.
10. In-game settings window (the gear button in the title bar of a remembered window; on the World View it also offers the Alternative Special Effects settings): tabs, groups, hints, language selector.
11. Background pictures of the main menu and loading screens: only the mods' (default), mixed with the game's, or only the game's. Real photos can be turned off. Main menu: a slideshow (default) or the game's video.
12. Hides "_Custom Mission" in the SCENARIOS list of the main menu.
13. Mission editor: the open-mission picker starts in the last mission's folder; *.ini files are light blue.
14. Optional: deletes the game's old log files at start (off by default).
15. Enhances the UI of the Unit Reference (encyclopedia) window:
   - Adds a movable splitter between the unit tree and the unit data; the tree width is remembered.
   - The chosen country, the search panel (category, filters, search text) and whether the unit tree is shown are remembered.
   - The Show/Hide unit tree button also hides the search panel and adjusts the window width.
   - A tidier unit tree: classes sorted by service date, a unit's loadout variants (Early, Late, ASW...) gathered under its class.
   - The tree scrolls to the selected unit.
   - The wheel scrolls the tree only over the tree.
   - A click on a class selects its earliest variant (optional).
   - The unit the window was opened for stays in the tree, even if the filters would hide it.
   - The country list shows only countries that have units for the current search.
   - Without "Search by name only" the search also looks into descriptions and unit ids (<Id>.ini).
   - Tooltips with a short help on the highlighted links to weapons, sensors and systems.
   - Loadout selector for ships, submarines and land units too, not only aircraft.
   - "Armed only" check box: hides unarmed units (rafts, targets, tankers, civilian ships...).
   - Propulsion of ships and submarines (engine type and power) next to Max Speed.
16. Unit Status window:
   - Reference tab: the Unit Reference of the selected unit right in the Unit Status window (also of a contact of a known type).
   - Damage Control tab: adjustable minimum height, so the Auto DC status panel at the bottom stays visible; the damage lists scroll with the wheel.
   - The full name of the unit in the header.
17. Right click on the flag or name in the unit panel opens the unit's context menu.
18. Fixed: empty sensor / age fields in the contact Information window.
19. Campaign, "Strict loadouts" (optional): a check box on the campaign start screen; the Task Force Builder then offers ship versions only for ships available in the mission or already owned.
20. Game messages in the bottom right corner no longer block clicks on buttons under them.
21. Localization: all texts of the mod's settings window and hints in the 9 languages of the game (en, ru, cn, de, es, fr, ko, ja, vn); the language of the game or [General] Language in the .cfg.
22. Optional desktop mode ("Enable desktop mode"): Tactical Display and World View work as ordinary windows (free move and size, snapping, levels). The "Toggle fullscreen minimap" key (Tab by default) switches between 2 - 5 desktops (the number is a setting): each keeps the place, size, look and open state of the windows; the active desktop is remembered. They start as built-in arrangements; the "Desktops" page of the settings window shows what each desktop keeps for each window - edit it or reset it. A key (Insert by default) opens and closes the World View. Both windows open and close independently. A backdrop picture (AlternativeGameInterface_Backdrop.jpg next to the dll, replaceable) is behind them. Backspace (photo mode) shows the 3D view on the whole screen. The compass tape is moved into the World View window and is white or black by the picture behind it.
23. A small arrow next to the gear of a window opens its look menu: opacity, scale and level (bottom / default / top). The Tactical Display and the World View start at the bottom level.
24. Windows docked to a screen edge can still be resized from that edge.
25. The mission's time panel can be dragged; double-click puts it back. Its place is kept per desktop; in desktop mode it starts a little lower and to the left.
26. Unit Status: a "Track State" tab for a selected contact of another side (the contact's Information).
27. Campaign: the "Upgrade ship..." button in the Task Force Builder; a damaged ship can be dismissed (the repair cost is deducted); loadouts are sorted by date; a hull swap is priced from the roster.
28. The News window of the main menu gets the mod's gear, frame and remembered position; the game's resize cursors work outside a mission.
29. Optional "Keep map view on resize": the Tactical Display's zoom follows its window size, so the visible area stays visible.
30. Event Log: every track number is a link (option); a repeated click on the same contact or unit link centres the Tactical Display on it (option).
31. Settings entry "Game Interface" in the main menu (under Mod Manager) and the pause menu (under Options). With several of the author's mods installed it becomes "Alternative Mods".
32. The settings gear and the look arrow sit among the title bar buttons and never lie over buttons other mods add.
33. Binoculars: at max zoom they start with the normal view instead of the night / thermal filter (by day, optional). Desktop mode: the frame and interface fit the World View window (up to 30% larger); Backspace gives a full-screen view.
34. Ship binoculars can look below the horizon (In Mission tab, 10 degrees by default).
35. Moving or resizing any window over the 3D view no longer turns the camera; windows of other mods drawn with Unity IMGUI (Camera Offset) can be grabbed over the game's windows.
36. Desktop mode: the other monitors work like the main one for windows (Monitor in the window's menu); the game's second-monitor map and World View monitor are then not used.

## Compatibility

- Supports Sea Power version 0.8.7 (Release and Stable Beta).
- Requires Anchor Chain.
- Can be switched on/off in the game's Mods menu.

## Install

1. Install the **Anchor Chain** preloader as described in the [documentation](https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html).
2. Subscribe to this mod.
3. Start **Sea Power**.
4. Enable the mod in the game's **Mod Manager** and press the **Apply** button.

## Usage

- Windows remembered by default: Tactical Display, World View, Unit Reference, Unit Status, Event Log, Formation Manager, Task Force Builder, Weapon Reference.
  Move or resize them as usual; they come back where you left them.
- To remember another window, add its name in the settings window (Remembered Windows).
- Most features can be turned off or customized in the settings window (the gear in the title bar of a remembered window). They are stored in:
  `Sea Power/Sea Power_Data/StreamingAssets/user/AlternativeGameInterface/AlternativeGameInterface.cfg`

P.S. Highly recommended to use with the [Camera Offset](https://steamcommunity.com/sharedfiles/filedetails/?id=3676689138) mod.
