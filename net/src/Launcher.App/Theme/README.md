# Launcher.App/Theme

The launcher's look, direction C: the one place for colours, sizes, type and the controls' classes. Every view is
on these classes; none sets a colour of its own.

| File | What it is |
|---|---|
| `Theme.axaml` | Its head says the grid, the type scale, the colours and every class. Then the colours and sizes as resources, and type; it takes in the two files below at its end |
| `Buttons.axaml` | Everything that is pressed: the big action, buttons, links, tabs, the window's own buttons |
| `Forms.axaml` | Tables, the set-in view, inputs, segments, check boxes, colour swatches, picture cells, forms |
| `GameSkin.cs` | The art from the player's disc (pictures, machines in 3D, stock colours), loaded once and put into the application's resources; never raises |

A later style wins over an earlier one: keep the order of the files and of the styles in them.
