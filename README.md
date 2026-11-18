# Alternative Game Interface

A Sea Power mod that makes the game's windows smarter (formerly "Smart Windows").

**Version 2.5.0**

Alternative Game Interface: a flexible Sea Power UI. Desktop mode with several monitors, windows that remember their place and state, transparency, a richer interface for missions and campaigns.

### Requires **Anchor Chain** or **R.A.T.S. Keel**

## Features

1. Remembers the position and size of chosen windows and restores them when a window is opened again.
2. Double-click a window's title bar to maximize it, double-click again to restore it. A window closed maximized opens maximized next time.
3. Clicking a window brings it to the front. Mission briefings and scripted messages always stay on top; a maximized Tactical Display or World View stays at the back.
4. Windows snap to the screen edges and to each other.
5. Several windows of one kind (e.g. Weapon Reference) open slightly shifted.
6. Windows can be dragged partly off the screen.
7. Remembers which mission windows were open and restores that at the next mission start.
8. Thin, customizable frame around draggable windows and the game's tooltips.
9. In-game settings window (gear button in the title bar of a remembered window): tabs, groups, hints, language selector.
10. Background pictures of the main menu and loading screens: only the mods' (default), mixed with the game's, or only the game's. Main menu: a slideshow (default) or the game's video.
11. Main menu: "Encyclopedia" opens the Unit Reference window at once, "Encyclopedia 3D" is the game's own (optional). Save / Load in the pause menu of Encyclopedia 3D.
12. Mission editor: the open-mission picker starts in the last mission's folder; *.ini files are light blue. Save / Load windows remember their last folder; Load game opens on entering Scenarios / Campaign, beside the menu; Create Mod fills in the folder and picture of your Workshop item.
13. Enhances the UI of the Unit Reference (encyclopedia) window:
   - Adds a movable splitter between the unit tree and the unit data; the tree width is remembered.
   - The chosen country, the search panel (category, filters, search text) and whether the unit tree is shown are remembered.
   - A tidier unit tree: classes sorted by service date, a unit's loadout variants (Early, Late, ASW...) gathered under its class.
   - The tree scrolls to the selected unit; the wheel scrolls it only over the tree.
   - A click on a class selects its earliest variant (optional).
   - The unit the window was opened for stays in the tree, even if the filters would hide it.
   - The country list shows only countries with units for the current search.
   - Without "Search by name only" the search also looks into descriptions and unit ids.
   - Tooltips on the links to weapons, sensors and systems.
   - Loadout selector for ships, submarines and land units too, not only aircraft.
   - "Source" drop-down by the search box: show units of the ticked sources (base game, mods).
   - "Armed only" check box: hides unarmed units (rafts, targets, tankers, civilian ships...).
   - Propulsion of ships and submarines (engine type and power) next to Max Speed.
14. Unit Status window:
   - Reference tab: the Unit Reference of the selected unit right in the Unit Status window (also of a contact of a known type).
   - Damage Control tab: adjustable minimum height, so the Auto DC status panel at the bottom stays visible; the damage lists scroll with the wheel.
   - The window title and the name beside the tabs show the unit's name, class or type and class, as you choose.
15. Right click on the flag or name in the unit panel opens the unit's context menu.
16. Campaign, "Strict loadouts" (optional): a check box on the campaign start screen; the Task Force Builder offers ship versions only for ships available in the mission or owned.
17. Localization: settings window and hints in the 9 languages of the game; the texts can be edited (lang folder).
18. Optional desktop mode ("Enable desktop mode"): Tactical Display and World View work as ordinary windows (free move and size, snapping, levels). The "Toggle fullscreen minimap" key (Tab by default) switches between 2 - 5 desktops (the number is a setting): each keeps the place, size, look and open state of the windows; the active desktop is remembered. A key (Insert by default) opens and closes the World View. A replaceable backdrop picture is behind them. Backspace (photo mode) shows the 3D view on the whole screen. The compass tape is moved into the World View window and is white or black by the picture behind it.
19. A small arrow next to the gear of a window opens its look menu: opacity, scale, text contrast and level; for the World View also the camera offset (the followed object off the centre, as the Camera Offset mod; off while that mod works). The Tactical Display and World View start at the bottom level.
20. The mission's time panel and the mission timer under it can be dragged; double-click puts them back. The place is kept per desktop.
21. Unit Status: a "Track State" tab for a selected contact of another side, in an optional compact layout.
22. Campaign: the "Upgrade ship..." button in the Task Force Builder; a damaged ship can be dismissed (the repair cost is deducted); loadouts are sorted by date; a hull swap is priced from the roster. A submarine away for service stays in the Reserve and is shown on the Submarines tab.
23. Main menu: the News window gets the mod's gear and frame and fills the free area; "_Custom Mission" is hidden in SCENARIOS; the game's resize cursors work outside a mission.
24. Optional "Keep map view on resize": the Tactical Display's zoom follows its window size, so the visible area stays.
25. Event Log: every track number is a link; a repeated click on the same link centres the Tactical Display on it (both optional). A compact layout (optional).
26. Settings entry "Game Interface" in the main menu (under Mod Manager) and the pause menu (under Options); with several of the author's mods it becomes "Alternative Mods".
27. Binoculars: at max zoom they start with the normal view instead of the night / thermal filter (by day, optional). Ship binoculars can look below the horizon (In Mission tab).
28. Moving a window over the 3D view no longer turns the camera; IMGUI windows of other mods (Camera Offset) can be grabbed over the game's.
29. Desktop mode: the other monitors work like the main one for windows (Monitor in the window's menu).
30. Tactical Display: unit labels on a chosen side, without overlaps (other sides first, then fewer lines); formation names around the formation (with distance and altitude for flights), formation lines, FRND inside the label; Shift + wheel zooms finer. A contact's position ellipse never reaches nearer than the sensor; many sonobuoys shrink and lose their text.
31. Video options: the Frame Rate Limit slider goes up to 250, with a "Remove frame rate limit" check box; the V-Sync list gets "Adaptive V-Sync".

## Compatibility

- Supports Sea Power version 0.9.2 and newer.
- Loaders: Anchor Chain or R.A.T.S. Keel.

## Install

1. Subscribe to [Anchor Chain](https://steamcommunity.com/workshop/filedetails/?id=3380210757) or [RATS Keel](https://steamcommunity.com/workshop/filedetails/?id=3835527459) (temporarily unavailable) and follow instructions on mods page.
2. Subscribe to this mod.
3. Start **Sea Power**.
4. Enable the mod in the game's **Mod Manager** and press the **Apply** button.

## Usage

- Windows remembered by default: Tactical Display, World View, Unit Reference, Unit Status, Event Log, Formation Manager, Task Force Builder, Weapon Reference.
  Move or resize them as usual; they come back where you left them.
- To remember another window, add its name in the settings window (Remembered Windows).
- Most features can be turned off or customized in the settings window (the gear in the title bar of a remembered window). They are stored in:
  `Sea Power/Sea Power_Data/StreamingAssets/user/AlternativeGameInterface/AlternativeGameInterface.cfg`

