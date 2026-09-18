# True Creation

A standalone Windows tool for making Palworld mods, by Mistyeyes. Pre-alpha.
Download: https://www.nexusmods.com/palworld/mods/5690

This repository is the source code the release is built from. It holds everything except the game data. The game data
is Palworld's own, belongs to Pocketpair, and ships in the release zip.

## Build it

1. Install **Unity 6000.5.10f1** (Windows, with Windows Build Support (Mono)).
2. Open this folder as a Unity project.
3. Game data: from the release zip, copy `True Creation/True Creation_Data/StreamingAssets` to `Assets/StreamingAssets`
   in this project.
4. In Unity: **Tools > True Creation > Build Windows exe...** and pick an empty folder outside the project.
5. The build writes `True Creation.exe` with its `True Creation_Data` folder there. It also writes
   `True Creation <version>.zip` next to that folder.

## What is in the release

- `True Creation.exe` is Unity's standard, unmodified player launcher.
- The program's own code is in `True Creation_Data/Managed/`, built from the scripts here: `TrueCreation.App.dll`,
  `TrueCreation.Host.dll`, `PalCreationEngine.Runtime.dll`, `PalItemgen.Runtime.dll`, `RingOfNoXp.Runtime.dll`,
  `TrueServer.dll`, `Assembly-CSharp.dll`.
- Every other DLL is Unity's engine or runtime.
- The program makes no network connections. It reads and writes files on the user's PC only: its settings, the mod
  packages it makes, and the Palworld mod folders when an Install button is pressed.

## Rights

Copyright Mistyeyes. All rights reserved. The source is published so the release can be reviewed. Ask before reusing it.
Palworld and its game data belong to Pocketpair. The Unity engine belongs to Unity Technologies.
