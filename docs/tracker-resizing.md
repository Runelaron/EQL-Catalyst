# Tracker and Combat Lab placement

Drag the lower-right handle to resize an expanded, unlocked window. Releasing the mouse keeps the new size. Escape or genuine capture loss cancels the drag. Collapse, hide, minimize and DPI changes invalidate pending drag frames so they cannot overwrite the restored dimensions. The handle returns after restoring or reopening an expanded window.

Tracker and Combat Lab remember their own size and position for each character. Completed moves and resizes save automatically; hiding the window or exiting flushes pending placement. The saved expanded dimensions survive title-bar collapse, minimization and maximization. Existing saved sizes are retained. Without saved dimensions, Tracker starts at 915 DIP wide and Combat Lab at 878 DIP; their previous default heights and minimums remain. Windows' native tracking limits still apply.

Placement records include monitor and DPI context. If a monitor disappears or its scaling changes, the window is restored onto an available display with a reachable title bar. Existing layout records that contain only dimensions remain compatible. Table columns and pane proportions are independent of the outer-window placement.

Pet Details has the same 28-DIP header height as neighboring controls. An 8-DIP gap separates the complete Build toolbar from the Inventory/Search row, including when the toolbar wraps.
