# Precompiled installation (local candidate)

The precompiled installer asks for an ISO and a destination. It extracts the ISO on the destination volume, checks SHA-256 hashes of the three original executable modules, validates the entire runtime payload, moves the extracted data into `GameData`, applies the existing credits patch, and installs the launcher and both graphics backends. It does not install Visual Studio or run code generation or compilation.

The existing source installer remains available. A precompiled installer accepts only the executable version recorded in its manifest; an unrecognised ISO is rejected before the extracted data and runtime payload are installed. Region names are not inferred from those hashes.

`BuildPrecompiled.py` takes explicit paths to a tested runtime, the original module inputs, extract-xiso, an existing license bundle, and the official x64 CRT redistributable folder and notices. It builds into a new output directory and never overwrites the tested runtime. It bundles the CRT locally with the game and extraction tool. Windows 10/11 and appropriate graphics drivers are still required. The launcher and setup include their .NET runtime.

The build creates `precompiled.json`, a self-contained setup executable, and `build.json` with the setup hash and complete payload manifest. No ISO or extracted game assets are packaged. Recompiled executable code is included; its redistribution rights need a separate review before publication. This candidate must remain local until that review and gameplay validation are complete.

Validation: run the setup with `--install-test <ISO> <new destination>` for a logged installation without opening the form. Run the installed launcher afterwards to validate gameplay and World Tour. Installer timing depends on storage, caching, antivirus and the ISO; a local installation measurement is not a guarantee for other machines.
