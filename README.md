# Smart Windows

A Sea Power mod that makes the game's windows smarter.

**Version 1.5.2**

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
10. In-game settings window: the gear button in the title bar of a remembered window.
11. Enhances the UI of the Unit Reference (encyclopedia) window:
   - Adds a movable splitter between the unit tree and the unit data; the tree width is remembered.
   - The chosen country, the search panel (category, filters, search text) and whether the unit tree is shown are remembered.
   - The Show/Hide unit tree button also hides the search panel and adjusts the window width.
   - A tidier unit tree: classes sorted by service date, a unit's loadout variants (Early, Late, ASW...) gathered under its class.
   - The tree scrolls to the selected unit.
   - Loadout selector for ships, submarines and land units too, not only aircraft.
   - "Armed only" check box: hides unarmed units (rafts, targets, tankers, civilian ships...).
12. Unit Status window:
   - Reference tab: the Unit Reference of the selected unit right in the Unit Status window.
   - Damage Control tab: adjustable minimum height, so the Auto DC status panel at the bottom stays visible.

## Compatibility

- Supports Sea Power versions 0.8.3 – 0.8.5.
- Requires Anchor Chain.
- Can be switched on/off in the game's Mods menu.

## Install

1. Install the Anchor Chain preloader as described in the [documentation](https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html).
2. Subscribe to this mod.
3. Restart the game.

## Usage

- Windows remembered by default: Tactical Display, World View, Unit Reference, Weapon Reference, Unit Status, Event Log, Formation Manager, Formation Editor, Task Force Builder.
  Move or resize them as usual; they come back where you left them.
- To remember another window, hover the mouse over it and press F12.
- Most features can be turned off or customized in the settings window (the gear in the title bar of a remembered window). They are stored in:
  `Sea Power/BepInEx/config/com.rgreat.seapower.smartwindows.cfg`

P.S. Highly recommended to use with the [Camera Offset](https://steamcommunity.com/sharedfiles/filedetails/?id=3676689138) mod.
