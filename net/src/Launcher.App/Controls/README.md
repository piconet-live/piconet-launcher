# Launcher.App/Controls

The launcher's own controls, used by the views. The 3D ones draw on the graphics card through `../Gpu`.

| File | What it is |
|---|---|
| `MachinePreview.cs` | A machine in 3D on a turntable (the left column, the Garage), dragged round or turning by itself; as a showcase (`Showcase`: the redesigned hub's background) on no turntable, filmed shot after shot as an advertisement is (`Launcher.Core/ShowcaseShots.cs`: close in, in slow motion: its nose, over its cockpit, its boosters, drawing back), the picture dissolving between two, its engines burning (`Launcher.Core/EngineFlames.cs`, from its burners on the disc) and boosting in the shot from behind; it stands where its page leaves it room (`FocusX`, `RoomAfter`), and the pointer turns and holds it, let go it goes on a few seconds later. On a turntable (the Garage's, the left column's) no flames burn |
| `MachineStill.cs` | A machine drawn still in its look (the Garage's cells), drawn again only when the look changes |
| `CourseHero.cs` | The Practice page's hero: the course's model turning on its venue's sky |
| `CourseStill.cs` | A course's still on its venue's sky (the course picker's tiles) |
| `CourseMap.cs` | The race's 3D map: the course's model, the racers on it, the racer it follows |
| `ColorWheelPicker.cs` | The colour wheel with its brightness bar |
| `Stepper.cs` | A small whole number between - and + |
| `RulesPanel.axaml` | Groups of rows, a label and its value or control each (`ViewModels/RuleRows.cs`: a value, the machine with the Garage, a choice dropped down, segmented, a stepper, a slider, a switch, text, a range), and the one big action under them: Practice's set-up, a custom lobby's rules later |
| `CoursePicker.axaml` | The courses as cards with the venues' pictures, the cups as chips over them; one course, or a pool with All, None and its count (`ViewModels/CoursePickerModel.cs`) |
| `PadOverlay.cs`, `PadSkin.cs`, `PadPicture.cs` | m-overlay's controller skin drawn live (the Controller page's test, the Inputs window), its numbers, and the pad it draws (clamped as the game reads it: from the controller helper or from the game's board) |
| `CellRows.cs` | Rows of cells that together are exactly as wide as their room |
| `ShareRow.cs` | Parts side by side that share the row's width by their shares (the course vote's cards: the one drawn widens) |
| `ScaledRoot.cs` | The window's content at the player's interface scale, at least the window's least size at it (scrolled beyond) |
| `Unscaled.cs` | Takes the interface scale back off a 3D view, so that it draws every pixel it shows |
| `PadNav.cs` (Typing) | The redesigned interface's keyboard on the screen: the pad stops on a text box and A opens it (none in the classic interface) |
| `PadNav.cs` (Home, Next, Tabs) | The redesigned interface's ways for the pad: a page's own list, where B brings the focus back first (Home); the control the focus goes on to after a press (Next, `../Views/PadFollow.cs`); a row of sub-tabs that R1 and Ctrl with the arrows step through (Tabs) |
| `IPadValue.cs` | A control whose value the pad's left and right step (the redesigned interface's option rows, the Garage's setting) |
| `NextUi/` | The redesigned interface's own controls ("Experimental UI"): the chamfered plate, the background's flag |
