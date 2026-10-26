# Launcher.App/Gpu

The 3D views' drawing in OpenGL, inside Avalonia. All of the launcher's 3D is drawn on the graphics card, never on
the processor. What is drawn from where is worked out without a card in `Launcher.Core`
(`Studio.cs`, `MapCamera.cs`, `PackedModel.cs`, `CourseShot.cs`, `VenueSky.cs`), which the tests cover.

| File | What it is |
|---|---|
| `GpuView.cs` | A 3D view: Avalonia's OpenGL control with a target of its own; it draws only when what it shows changed or moves |
| `Gl.cs` | The OpenGL calls used, with the few entry points Avalonia leaves out |
| `RenderTarget.cs` | Where a view draws: multisampled colour and depth, resolved into a plain texture |
| `GpuModel.cs` | A packed model on the card: vertices and indices uploaded once |
| `Studio.cs` | The Garage's studio: the lights and the shaders for machines |
| `StillPainter.cs` | The still pictures of machines, drawn once and read back |
| `MapPainter.cs` | The track map: the course's model in flat shades, the racers as discs over it; a venue's sky behind it |
| `CourseStillPainter.cs` | The Practice page's course stills, drawn once per course and disc, kept as PNGs in the data folder |
| `FrameWait.cs` | A wait of a few milliseconds between two frames, kept exact |
| `GpuDebug.cs` | For checking the views: a log line a second and saved pictures, off unless asked for |
