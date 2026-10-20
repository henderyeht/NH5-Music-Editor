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

The list is the music already in that install. Choose an mp3 or wav for a song, edit the title and artist Heat shows, then APPLY.

PLAY, PAUSE, and STOP play the selected song. An assigned file plays that file. Otherwise the tool plays the song already in the game. Play on a new selection stops the current song and starts the selected one.

The first APPLY saves a copy of your current music files. That copy is your game as it is, not a stock original. Later APPLY builds from that copy. Only these two files are replaced:

- `NASCARHeat5_Data\StreamingAssets\PC\FMOD\Music Bank.streams.bank`
- `NASCARHeat5_Data\StreamingAssets\PC\FMOD\Music Bank.bank`

Your other mods stay as they are. RESTORE puts those two files back to that copy.

## Build

Source is in `src/`. Testers only need the exe from `dist/Standalone/`.

```
dotnet publish src\Heat5Music.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o dist-out
```

Copy `NASCAR Heat 5 Music Editor.exe` into `dist/Standalone/`. Zip that file to `dist/NASCAR Heat 5 Music Editor.zip`. Requires the .NET 8 SDK to build. The exe runs on its own.

## Layout

```
NASCAR Heat 5 Music Editor.lnk   opens the standalone
src/          C# source, Heat5Music.csproj, Assets/
dist/Standalone/   NASCAR Heat 5 Music Editor.exe
dist/NASCAR Heat 5 Music Editor.zip
```

## License

Copyright (C) 2026 henderyeht

The original code in this project is free software under the GNU General Public License, version 3. You may share it and change it. Every copy, including a changed or built copy, must keep this copyright notice and the license in the LICENSE file. Third-party libraries keep their own licenses.
