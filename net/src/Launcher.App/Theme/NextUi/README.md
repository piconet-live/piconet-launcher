# Launcher.App/Theme/NextUi

The redesigned interface's look, theme v3 ("Experimental UI"; off by default), beside the classic look in `..`, which
it leaves as it is. Every colour, size and duration of the redesigned interface is here; none is in a view.

| File | What it is |
|---|---|
| `Theme.axaml` | Its head says the ground, the colours, the shapes, the type, the focus, the motion and every class. Then the colours and sizes as resources (their names begin with `Next`), and the styles: each selector starts at the class `nextui`, which only the redesigned interface's root has (`../../Views/NextUi/NextShell.axaml`) |

Text holds 4.5:1 on every ground it stands on (the ground, the plates, the solid tones), and the text on the accent
too: `../../../../tests/Launcher.Tests/App/NextPolishTests.cs` measures it from these resources. Under Reduced motion
(the window's class "still", the root's "moving" off) nothing moves and nothing runs by itself. Developer mode's data
is amber in monospace (`TextBlock.devmono`).

The application takes the file in after the classic theme (`../../App.axaml`): the resources are found by every view,
the styles reach nothing outside the redesigned interface. A later style wins over an earlier one: keep their order.
