# Turbo Mode

Performance optimizations for KSP2.

This mod's goal is to improve CPU performance of the main KSP2 game loop without significantly altering any behavior.  Similar to a [Turbo button](https://en.wikipedia.org/wiki/Turbo_button) found on old computers might do.

## Features

- Reduce load times and dock/undock/staging stutter for vessels with large part counts.
- Reduce per-part overhead on active vessel by avoiding PhysX synchronization overhead when the game moves parts around manually.
- Reduce UI overhead by shutting off incative UI windows. (This can cause slight stutters when opening a window.)
- Reduce overhead from resource flow requests, such as battery charing/discharing, RCS thrusters, and engine firing.
- Faster Burst based code for KSP's `TransformFrame`s speeds up a number of systems.
- Significant reductions in C# garbage reduces slowdowns from garbage collection.

## CKAN Installation
 - Download the CKAN file TurboMode-x.y.z.ckan from the [release page](https://github.com/foonix/Ksp2TurboMode/releases).
 - Open it in CKAN, from the `File` -> `Install from .ckan...` menu.

## Manual Installation

- BepInEx must be installed into your KSP2 game files.  I recommend using CKAN to install it.
- Download the TurboMode-x.y.z.zip from the [release page](https://github.com/foonix/Ksp2TurboMode/releases).
- Unzip to `BepInEx/plugins/TurboMode`
- Move the file `TurboMode.Prepatch.dll` from `BepInEx\plugins\TurboMode` to `BepInEx\patchers` folder.
  If it is installed correctly, the Player.log should show the message `Patching [Assembly-CSharp] with [TurboMode.BurstMathInjector]`.

## Troubleshooting

The various optimizations this mod provides can be turned on/off by editing `BepInEx/config/TurboMode.cfg`.

Please report any issues either on github `issues`, or feel free to ping me on discord `@foonix`.

## Building from source code

- Open this project in Unity 2022.3.5f1.
- (One time) Configure ThunderKit to point to your KSP2 installation directory, and run the importer.
  Note: TurboMode assemblies will have errors until the import is complete.
- Build with one of the build menus under Tools -> TurboMode
  The build output will go directly to your KSP2 installation under `BepInEx/plugins/TurboMode`.

License: MIT
