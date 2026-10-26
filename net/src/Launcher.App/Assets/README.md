# Launcher.App/Assets

What the launcher carries that is not code. Nothing here is from the game: its pictures and models are read from
the player's own disc when the launcher runs.

- `Icon/`: the app's icon. `app.ico` is the launcher's program file's and window's icon, and the game's runner and
  window carry the same file (`port/moderngekko/build.sh`). It is made outside the repository, from the project's
  artwork (too large for the repository), by the script `make_icon.py` kept beside the artwork; the script's head says
  which sizes are which piece of the artwork and the artwork's SHA-256.
  Only the fonts, their licence and `app.ico` are carried in the launcher (`Launcher.App.csproj` names them).
- `Fonts/`: Saira Condensed (headings, figures) and Saira Semi Condensed (text), under the SIL Open Font Licence
  (`Fonts/OFL.txt`). A family is named in the theme as its font file names it.
