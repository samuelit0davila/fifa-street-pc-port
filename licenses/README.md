# License scope and third-party components

The root MIT license applies to original project-authored installer, launcher and scripts. It does not relicense third-party components, copied SDK scaffolding or game material.

- ReXGlue and the local SDK patch: BSD 3-Clause; the upstream notice is in `ReXGlue-BSD-3-Clause.txt`, including its Xenia attribution.
- The managed LZX decoder from MonoGame is used under Microsoft Public License (Ms-PL), one of its offered licenses. Its copyright/header is retained in `Installer/FifaStreetSetupTool/ThirdParty/LzxDecoder.cs`; the full notice is in `Installer/FifaStreetSetupTool/ThirdParty/MonoGame-LICENSE.txt`. It is not covered by the root MIT license.
- LLVM, CMake, Ninja, extract-xiso, .NET and ReXGlue dependencies: preserve their own license files and notices when packaging them. The public installer must include the notices applicable to the exact bundled versions.
- FIFA Street branding images, icon and screenshots: rights remain with their respective holders; these are not released under the project's MIT license.

The repository excludes third-party compiled tools. A locally produced installer bundles public tools and must carry their corresponding notices. The packaging script collects the SDK dependency notices, CMake license and MonoGame LZX license; the full set of exact-version tool-specific notices still needs to be checked before a public release.

The credit recipe records differences for local application to the user's game data. Original game archives and the original font are not included. Presentation images and screenshots are separate from the MIT code license.
