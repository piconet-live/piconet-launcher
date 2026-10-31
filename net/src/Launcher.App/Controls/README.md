# Launcher.App/Controls

The launcher's own controls, used by the views. The 3D ones draw on the graphics card through `../Gpu`.

| File | What it is |
|---|---|
| `MachinePreview.cs` | A machine in 3D on a turntable (the left column, the Garage), dragged round or turning by itself |
| `MachineStill.cs` | A machine drawn still in its look (the Garage's cells), drawn again only when the look changes |
| `CourseHero.cs` | The Practice page's hero: the course's model turning on its venue's sky |
| `CourseStill.cs` | A course's still on its venue's sky (the course picker's tiles) |
| `CourseMap.cs` | The race's 3D map: the course's model, the racers on it, the racer it follows |
| `ColorWheelPicker.cs` | The colour wheel with its brightness bar |
| `Stepper.cs` | A small whole number between - and + |
| `RulesPanel.axaml` | Groups of rows, a label and its value or control each (`ViewModels/RuleRows.cs`: a value, the machine with the Garage, a choice dropped down, segmented, a stepper, a slider, a switch, text, a range), and the one big action under them: Practice's set-up, a custom lobby's rules later |
| `CoursePicker.axaml` | The courses as cards with the venues' pictures, the cups as chips over them; one course, or a pool with All, None and its count (`ViewModels/CoursePickerModel.cs`) |
| `PadOverlay.cs`, `PadSkin.cs` | m-overlay's controller skin drawn live (the Controller page's test), and its numbers |
| `CellRows.cs` | Rows of cells that together are exactly as wide as their room |
| `ShareRow.cs` | Parts side by side that share the row's width by their shares (the course vote's cards: the one drawn widens) |
| `ScaledRoot.cs` | The window's content at the player's interface scale, at least the window's least size at it (scrolled beyond) |
| `Unscaled.cs` | Takes the interface scale back off a 3D view, so that it draws every pixel it shows |
