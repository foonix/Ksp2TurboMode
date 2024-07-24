=== TurboMode ===

Performance optimizations for KSP2.

Features:

- Reduce load times and dock/undock/staging stutter for vessels with large part counts.

== Building ==

- Open this project in Unity 2022.3.5f1.
- (One time) Configure ThunderKit to point to your KSP2 installation directory, and run the importer.
  Note: TurboMode assemblies will have errors until the import is complete.
- Run one of the build pipelines under `Assets/ThunderKitSettings/Pipelines`.  
  The build output will go directly to your KSP2 installation `under BepInEx/plugins/TurboMode`.

License: MIT
