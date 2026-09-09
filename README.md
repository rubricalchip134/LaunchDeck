# LaunchDeck

A native Windows launcher with a 15-button, Stream Deck-style grid. Drag `.exe`
files and shortcuts onto tiles, click to launch them, and drag tiles to rearrange
the layout. The app browser searches desktop and Microsoft Store apps registered
with Windows, recommends commonly used installed apps, and puts their Windows
supplied artwork on the deck.

## Download

Download `LaunchDeck.exe` from the [latest release](https://github.com/rubricalchip134/LaunchDeck/releases/latest).
No installer or administrator access is required.

LaunchDeck checks this repository's Releases feed when it opens. A new release is
downloaded inside the app, verified against its SHA-256 checksum, installed over
the current executable, and restarted. The saved deck remains in
`%LOCALAPPDATA%\LaunchDeck\deck.xml`.

## Build

Run `src/build.ps1` on Windows. It uses the .NET Framework C# compiler included
with Windows and produces `LaunchDeck.exe` at the repository root.

Pushing a version tag such as `v1.0.1` runs the release workflow. Update the
`Updater.Version` value in `src/LaunchDeck.cs` first. The workflow builds the
executable, creates its checksum, and publishes both files as a GitHub Release.
