# NASCAR Heat 5 Music Editor

Replace the menu music in **NASCAR Heat 5**, and the title and artist Heat shows.

## Download

`dist/Standalone/NASCAR Heat 5 Music Editor.exe` is the public drop on GitHub. Open **NASCAR Heat 5 Music Editor** in this folder to start it.

If Windows Defender quarantines it as `Trojan:Win32/Wacatac.B!ml`, that is a machine-learning false positive, not a real virus. Allow the file in Windows Security:

1. Open **Windows Security** → **Virus & threat protection**.
2. Open **Protection history**.
3. Select the blocked `NASCAR Heat 5 Music Editor.exe`.
4. Choose **Actions** → **Allow on device** (or Restore, then Allow).

You can also add it under **Virus & threat protection** → **Manage settings** → **Exclusions** → **Add an exclusion** → **File**, and pick `NASCAR Heat 5 Music Editor.exe`.

Close NASCAR Heat 5 before APPLY or RESTORE.

## Use

Close NASCAR Heat 5 before making changes.

If the game is not found, click **Locate game…** and pick the NASCAR Heat 5 folder.

The list is the music already in that install. That includes the original soundtrack, songs an older editor already replaced, and a mod that changed or added songs. Choose an mp3 or wav for a song to replace it, edit the title and artist Heat shows, then APPLY. Click a row to replace that song. If the list is already full, or you choose more files than there are open rows, the extra files are added and play with the rest of the menu music.

PLAY, PAUSE, and STOP play the selected song. The rail beside those buttons moves through that preview. The game still starts each song at the beginning and plays it to the end. An assigned file plays that file. Otherwise the tool plays the song already in the game. Play on a new selection stops the current song and starts the selected one.

The first APPLY saves a copy of your current music files. That copy is your game as it is, not a stock original. RESTORE puts that copy back. If songs were already replaced, a later APPLY keeps those songs and makes each one play to the end of the file that is in the game.

APPLY writes the music banks, and on the original soundtrack it also changes one name in `Master Bank.strings.bank` so a skipped song plays. RESTORE puts that file back too. Paints, tracks, and other mods stay as they are.

## Build

Source is in `src/`. Testers only need the exe from `dist/Standalone/`.

```
dotnet publish src\Heat5Music.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o dist-out
```

Copy `NASCAR Heat 5 Music Editor.exe` into `dist/Standalone/`. Requires the .NET 8 SDK to build. The exe runs on its own.

## Layout

```
NASCAR Heat 5 Music Editor.lnk   opens the standalone
src/          C# source, Heat5Music.csproj, Assets/
dist/Standalone/   NASCAR Heat 5 Music Editor.exe
```

## License

Copyright (C) 2026 henderyeht

The original code in this project is free software under the GNU General Public License, version 3. You may share it and change it. Every copy, including a changed or built copy, must keep this copyright notice and the license in the LICENSE file. Third-party libraries keep their own licenses.
