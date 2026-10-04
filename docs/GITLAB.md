# GitLab migration

The repository, commit history, backup branch and experimental version tags have been migrated to GitLab.

The current CI input manifest selects the published `validated-build-inputs/v0.3.0/inputs.zip`.
That exact version must exist in the generic package registry before starting
`build-installer`; publishing a Git tag does not upload its dependency archive.
An HTTP 404 during the download means the package is missing or inaccessible to
the job token. The archive checksum and all backend DLL hashes remain mandatory.
CI uses `ci/backend-manifest.json` for that snapshot. It builds the current launcher
and source installer with the older validated runtime, not the precompiled v0.4
release. Local source builds compile the current patched runtime instead.
Publishing a newer dependency snapshot requires package registry/API write access;
change both CI manifests together only after uploading and verifying the archive.
The installer bundle now carries the backend manifest used during packaging, so
installation verifies the same DLL hashes instead of using a different version.

The GitLab pipeline checks PowerShell syntax on each commit. The Windows installer build is manual: open **Build > Pipelines** and start `build-installer`. It requires available GitLab Windows runner capacity and CI minutes. Installers and checksums are uploaded to the generic package registry under `installer-builds`, identified by commit hash. The small checksum artifact is also retained for 30 days. New builds do not replace the historical release automatically.

The installer build compiles the launcher and installer from current source. It reuses the versioned public SDK, graphics DLLs and compiler toolchain selected by `ci/build-inputs.json`, downloading the dependency archive from the project's `validated-build-inputs` generic package and checking its SHA-256 before extraction. The archive contains no game code or data. Existing backend hash checks remain enabled. It does not rebuild ReXGlue or revalidate gameplay. Developers rebuilding the SDK should follow `BUILDING.md` and regenerate the validated dependency archive only after testing.

Packaging uses the cover images tracked in the repository and accepts explicit LLVM, CMake, Ninja and Clang resource paths. It no longer depends on Samuel's local game directory or Miniconda installation.

Git tags and release pages are separate objects. Local installer files must be positively identified before attaching them to historical versions. Missing GitHub issues, comments, release notes or assets cannot be reconstructed from Git history alone.
