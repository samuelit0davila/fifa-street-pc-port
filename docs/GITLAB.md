# GitLab migration

The repository, commit history, backup branch and experimental version tags have been migrated to GitLab.

The GitLab pipeline checks PowerShell syntax on each commit. The Windows installer build is manual: open **Build > Pipelines** and start `build-installer`. It requires available GitLab Windows runner capacity and CI minutes. Installers and checksums are uploaded to the generic package registry under `installer-builds`, identified by commit hash. The small checksum artifact is also retained for 30 days. New builds do not replace the historical release automatically.

The build uses the recorded ReXGlue revision and project patch in an isolated CI checkout. It retains the exact validated v0.3.0 graphics DLL pairs and their existing hash checks, downloading them from the project's `validated-backends` generic package. Upstream SDK and tool downloads still use their original GitHub locations.

Packaging uses the cover images tracked in the repository and accepts explicit LLVM, CMake, Ninja and Clang resource paths. It no longer depends on Samuel's local game directory or Miniconda installation.

Git tags and release pages are separate objects. Local installer files must be positively identified before attaching them to historical versions. Missing GitHub issues, comments, release notes or assets cannot be reconstructed from Git history alone.
