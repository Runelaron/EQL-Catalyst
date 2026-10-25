# Transparent workspace buttons

Tracker, Combat Lab and Overview have transparent navigation-button faces in every state. The existing translucent toolbar surface remains continuous beneath them. The selected workspace keeps a blue outline; hover, pressing and keyboard focus show two small gold corner brackets. Tabs retain their separate three-dash treatment.

The 24-DIP button bounds, 28-DIP toolbar, pointer cursors, reordering, independent workspace dimensions and guarded switching are unchanged. Settings, update and Close buttons keep their existing action styling.

The [native preview](previews/toolbar-button-polish-review.html) shows idle, hover, selected, selected/hover, keyboard-focus and pressed states across the three themes and 100%, 150%, 200% and 300% synthetic rendering scales. The state study sets isolated WPF state properties to exercise the real template; it does not claim physical pointer-input coverage. Existing native drag, resize, panel switching and restart checks remain separate.
