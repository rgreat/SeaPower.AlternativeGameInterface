[b]Version 1.9.0[/b]

[h1]Features[/h1]
[olist]
[*]Remembers the position and size of chosen windows and restores them when a window is opened again.
[*]Double-click a window's title bar to maximize it, double-click again to restore it. A window closed maximized opens maximized next time.
[*]Clicking a window brings it to the front of the other windows. Mission briefings and scripted messages of the game always stay on top; a maximized Tactical Display or World View always stays at the back.
[*]Windows snap to the screen edges and to each other.
[*]Several windows of one kind (e.g. Weapon Reference) open slightly shifted.
[*]Lets you drag any window partly outside the visible screen area.
[*]Remembered windows have no minimum size of their own.
[*]Remembers which mission windows were open and restores that at the next mission start.
[*]Thin frame around draggable windows and the game's tooltips for better edge visibility. Customizable.
[*]In-game settings window (the gear button in the title bar of a remembered window; on the World View it also offers the Alternative Special Effects settings): tabs, groups, hints, language selector.
[*]Background pictures of the main menu and loading screens: only the mods' (default), mixed with the game's, or only the game's. Main menu: slideshow (default) or game video.
[*]Hides "_Custom Mission" in the SCENARIOS list of the main menu.
[*]Mission editor: the open-mission picker starts in the last mission's folder; *.ini files are light blue.
[*]Optional: deletes the game's old log files at start (off by default).
[*]Enhances the UI of the Unit Reference (encyclopedia) window:
Adds a movable splitter between the unit tree and the unit data; the tree width is remembered.
The chosen country, the search panel (category, filters, search text) and whether the unit tree is shown are remembered.
The Show/Hide unit tree button also hides the search panel and adjusts the window width.
A tidier unit tree: classes sorted by service date, a unit's loadout variants (Early, Late, ASW...) gathered under its class.
The tree scrolls to the selected unit.
The wheel scrolls the tree only over the tree.
A click on a class selects its earliest variant (optional).
The unit the window was opened for stays in the tree, even if the filters would hide it.
The country list shows only countries that have units for the current search.
Without "Search by name only" the search also looks into descriptions and unit ids (<Id>.ini).
Tooltips with a short help on the highlighted links to weapons, sensors and systems.
Loadout selector for ships, submarines and land units too, not only aircraft.
"Source" drop-down next to the search box: tick the sources (base game and mods) whose units the tree shows; any number, at least one.
"Armed only" check box: hides unarmed units (rafts, targets, tankers, civilian ships...).
Propulsion of ships and submarines (engine type and power) next to Max Speed.
[*]Unit Status window:
Reference tab: the Unit Reference of the selected unit right in the Unit Status window (also of a contact of a known type).
Damage Control tab: adjustable minimum height, so the Auto DC status panel at the bottom stays visible; the damage lists scroll with the wheel.
The full name of the unit in the header.
[*]Right click on the flag or name in the unit panel opens the unit's context menu.
[*]Fixed: empty sensor / age fields in contact Information.
[*]Game messages in the bottom right no longer block clicks on buttons under them.
[*]Campaign "Strict loadouts" (optional): a check box on the start screen; the Task Force Builder offers ship versions only for ships available in the mission or owned.
[*]Localization: the settings window and its hints in the 9 languages of the game (en, ru, cn, de, es, fr, ko, ja, vn); [General] Language in the .cfg overrides. The texts can be edited (lang folder).
[*]Optional desktop mode ("Enable desktop mode"): Tactical Display and World View work as ordinary windows (free move and size, snapping, levels). The "Toggle fullscreen minimap" key (Tab by default) switches between 2 - 5 desktops (the number is a setting): each keeps the place, size, look and open state of the windows; the active desktop is remembered. They start as built-in arrangements; the "Desktops" page of the settings window shows what each desktop keeps for each window - edit it or reset it. A key (Insert by default) opens and closes the World View. Both windows open and close independently. A backdrop picture (AlternativeGameInterface_Backdrop.jpg next to the dll, replaceable) is behind them. Backspace (photo mode) shows the 3D view on the whole screen. The compass tape is moved into the World View window and is white or black by the picture behind it.
[*]A small arrow next to the gear of a window opens its look menu: opacity, scale and level (bottom / default / top). The Tactical Display and the World View start at the bottom level.
[*]Windows docked to a screen edge can still be resized from that edge.
[*]The mission's time panel can be dragged; double-click puts it back. Its place is kept per desktop; in desktop mode it starts a little lower and to the left.
[*]Unit Status: a "Track State" tab for a selected contact of another side (the contact's Information).
[*]Campaign: the "Upgrade ship..." button in the Task Force Builder; a damaged ship can be dismissed (the repair cost is deducted); loadouts are sorted by date; a hull swap is priced from the roster.
[*]The News window of the main menu gets the mod's gear, frame and remembered position; the game's resize cursors work outside a mission.
[*]Optional "Keep map view on resize": the Tactical Display's zoom follows its window size, so the visible area stays visible.
[*]Event Log: every track number is a link (option); a repeated click on the same contact or unit link centres the Tactical Display on it (option).
[*]Settings entry "Game Interface" in the main menu (under Mod Manager) and the pause menu (under Options); with several of the author's mods it becomes "Alternative Mods" with a list.
[*]The settings gear and the look arrow sit among the title bar buttons and never lie over buttons other mods add.
[*]Binoculars: at max zoom they start with the normal view instead of the night / thermal filter (by day, optional). Desktop mode: the frame and interface fit the World View window (up to 30% larger); Backspace gives a full-screen view.
[*]Ship binoculars can look below the horizon (In Mission tab, 10 degrees by default).
[*]Moving or resizing any window over the 3D view no longer turns the camera; windows of other mods drawn with Unity IMGUI (Camera Offset) can be grabbed over the game's windows.
[*]Desktop mode: the other monitors work like the main one for windows (Monitor in the window's menu); the game's own second-monitor map and World View monitor are off.
[/olist]

[h1]Compatibility[/h1]
[list]
[*]Supports Sea Power version 0.8.7 (Release and Stable Beta).
[*]Requires Anchor Chain.
[*]Can be switched on/off in the game's Mods menu.
[/list]

[h1]Install[/h1]
[olist]
[*]Install the [b]Anchor Chain[/b] preloader as described in the [url=https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html]documentation[/url].
[*]Subscribe to this mod.
[*]Start [b]Sea Power[/b].
[*]Enable mod in game's [b]Mod Manager[/b] and press [b]Apply[/b] button.
[/olist]

[h1]Usage[/h1]
[list]
[*]Windows remembered by default: Tactical Display, World View, Unit Reference, Unit Status, Event Log, Formation Manager, Task Force Builder, Weapon Reference.
Move or resize them as usual; they come back where you left them.
[*]To remember another window, add its name in the settings window (Remembered Windows).
[*]Most features can be turned off or customized in the settings window (the gear in the title bar of a remembered window). They are stored in:
Sea Power/Sea Power_Data/StreamingAssets/user/AlternativeGameInterface/AlternativeGameInterface.cfg
[/list]

P.S. Highly recommended to use with the [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3676689138]Camera Offset[/url] mod.
