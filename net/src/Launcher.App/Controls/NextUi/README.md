# Launcher.App/Controls/NextUi

The redesigned interface's own controls ("Experimental UI"; off by default), beside the launcher's others in `..`.
Their colours and sizes are the theme's (`../../Theme/NextUi/Theme.axaml`).

| File | What it is |
|---|---|
| `Chamfer.cs` | A plate with two parallel corners cut off (top left, bottom right; or the top left alone, for a panel at the window's edge): its fill, its content cut to its shape, the focus outline round it and a bar down its left edge |
| `StepButton.cs` | A button whose value left and right step by a command of its own (the Garage's machine setting) |
| `StepTrail.cs` | Where the player is on a page gone through in steps (F-Zero GX's way: one choice a screen): the steps numbered, the one they are on lit, those behind done (the Garage, Practice, a lobby's rules, the builder) |
| `PageDeck.cs` | The shell's place for the page: the page showing, and the pages shown before it kept in the window and hidden (their views made, styled and laid out once), so that a change of page shows a view instead of building and laying out its tree anew; where the cursor stood on each page, landed on again when it shows (else where the page says, else its own list's item picked); the large pages made ahead while the window is idle (`Prepare`); a page view told when it shows and hides (`IDeckPage`) |
| `OptionButton.cs` | An option row's button: left and right (the keyboard's and the pad's: `../IPadValue.cs`) step its value, a press works it; a click on an arrow steps that way |
| `OnlineCount.cs` | How many players are online, beside the profile in the top bar: a figure of a person and the count, which rolls to a new value while the figure swells once (washed with the accent when the count went up); a dash while it is not known |
| `ClockRing.cs` | A clock as a ring round its figure: a faint track, and what is left of the time in the accent |
| `HoloFlag.cs` | The one background: a dark chequered flag as a hologram, drawn live on the graphics card over the whole window (`../../Gpu/GpuView`); turned down and softened for the pages that need it quiet, still under Reduced motion and while a game runs, absent without graphics acceleration |
