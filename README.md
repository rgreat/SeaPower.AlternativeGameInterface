# Smart Windows

A Sea Power mod that makes the game's windows smarter.

## Features

1. Remembers the position and size of chosen windows and restores them when a window is opened again.
2. Double-click a window's title bar to maximize it, double-click again to restore it. A window closed maximized opens maximized next time.
3. Clicking a window brings it to the front of the other windows. A maximized Tactical Display or World View always stays at the back.
4. Several windows of one kind (e.g. Weapon Reference) open slightly shifted, so they don't cover each other.
5. Lets you drag any window partly outside the visible screen area.
6. Remembered windows have no minimum size of their own.
7. Remembers which mission windows were open or closed and restores that at the next mission start.
8. Thin frame around draggable windows for better edge visibility. Customizable.
9. Enhances the UI of the Unit Reference (encyclopedia) window:
   - Adds a movable splitter between the unit tree and the unit data; the tree width is remembered.
   - The chosen country and whether the unit tree is shown are remembered.
   - The Show/Hide unit tree button also hides the search panel and adjusts the window width.
10. Unit Status window, Damage Control tab: adjustable minimum height, so the Auto DC status panel at the bottom stays visible.

## Compatibility

- Supports Sea Power versions 0.8.3 – 0.8.5.
- Requires Anchor Chain.

## Install

1. Install the Anchor Chain preloader as described in the [documentation](https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html).
2. Subscribe to this mod.
3. Restart the game.

## Usage

- Windows remembered by default: Tactical Display, World View, Unit Reference, Weapon Reference, Unit Status, Event Log, Formation Manager, Formation Editor, Task Force Builder.
  Move or resize them as usual; they come back where you left them.
- To remember another window, hover the mouse over it and press F12. Its name is added to the mod's config file.
- Most features can be turned off or customized. Settings are stored in:
  `Sea Power/BepInEx/config/com.rgreat.seapower.smartwindows.cfg`

P.S. Highly recommended to use with the [Camera Offset](https://steamcommunity.com/sharedfiles/filedetails/?id=3676689138) mod.
