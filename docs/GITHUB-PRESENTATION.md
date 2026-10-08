# GitHub repository presentation

## Suggested repository name

`ReStreet`

## About description

ReStreet - FIFA Street (2012) for Windows PC, recompiled with ReXGlue, with an installer and launcher. Offline. Bring your own game. Ported by SamuelitoDaVila.

## Suggested topics

`fifa-street`, `recompilation`, `rexglue`, `xbox-360`, `windows`, `game-preservation`, `cpp`, `csharp`

## Repository and download layout

Commit this source folder and its documentation. Upload the installer `.exe` as a Release asset rather than committing it to the source tree. GitHub Releases are designed to contain downloadable binaries and release notes; see [GitHub's documentation](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).

The release asset is `FifaStreetSetup.exe` (v1.0). Its SHA-256 is in `FifaStreetSetup.exe.sha256` and in the release text. Use `docs/RELEASE-NOTES.md` as the release description. Keep the requirements marked as estimates until more hardware reports arrive, and keep the legal note about the generated game binaries.

## Images

`docs/images/installer.png` and the launcher images show the project's actual application interfaces. The installer capture may include a Windows user-directory path; review the image before publishing if that path should remain private.

The three developer-supplied game captures are included unchanged as start-screen.png, main-menu.png and gameplay.png. Captions identify their scope. They come from the working desktop build; the latest installer needs its own complete test. The README prominently discloses AI-assisted development.

No GitHub username or repository URL has been assumed. Add the final Releases and Issues links once the repository exists.

## Related projects reviewed

| Project | Relevant presentation pattern |
|---|---|
| [The Simpsons Game Recompiled](https://github.com/YesterMester/TheSimpsonsGameRecomp) | Separates status, requirements, installation, known issues, build instructions and credits; its [Releases](https://github.com/YesterMester/TheSimpsonsGameRecomp/releases) give installation steps alongside changes and known issues |
| [Eternal Sonata Reprise](https://github.com/birabittoh/EternalSonataReprise) | Separates release use from source builds, documents ISO requirements and provides troubleshooting and credits |
| [GTA IV Recompiled](https://github.com/luisxl15/GTA-IV-RECOMP-XBOX-360) | Describes experimental status prominently and documents unresolved compatibility problems instead of equating compilation with playability |
| [Xbox 360 native Linux ports](https://github.com/CrownParkComputing/xbox360-ports) | Keeps title-specific source and configuration separate from game data and locally generated binaries |

These projects informed the structure, not the feature claims. This project's text was written around its own implementation and observed results.
