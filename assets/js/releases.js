// Snapshot of the latest GitHub releases (offline fallback; the page refreshes it from the GitHub API when online).
window.RELEASES = [
 {
  "tag_name": "v2.0.7-online-beta",
  "name": "ReStreet v2.0.7 — ONLINE BETA (keyboard fix)",
  "published_at": "2026-10-21T14:40:07Z",
  "body": "## ReStreet 2.0.7 — fixes\n\n- **Keyboard: A and B did nothing** (stuck on the \"INFORMATION — autosave\" pop-up). Buttons with two keys in the launcher's Controls page (A = J / Enter, B = K / Backspace, or the mouse buttons in the keyboard + mouse layout) were ignored by the game since 2.0. Both keys now work.\n- **Keyboard: skill-move keys** (right stick, the arrow keys in the keyboard-only layout) now work, and turning off \"Mouse for skill moves\" really leaves the mouse free.\n- On the **PRESS START** screen, press **Esc** (Start). Wait for the intro to end first: the game ignores keys while it plays.\n\nIf a crash happens, send us `Binaries\\crash_motivo.txt` and the logs from `Binaries\\Logs`.\nOn AMD graphics cards, use Direct3D 12 (Vulkan can crash in the AMD driver).\n",
  "html_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.0.7-online-beta",
  "assets": [
   {
    "name": "ReStreet-2.0.7-Setup.exe",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.7-online-beta/ReStreet-2.0.7-Setup.exe"
   },
   {
    "name": "ReStreet-2.0.7-Setup.exe.sha256",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.7-online-beta/ReStreet-2.0.7-Setup.exe.sha256"
   },
   {
    "name": "ReStreet-Linux-launcher.tar.gz",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.7-online-beta/ReStreet-Linux-launcher.tar.gz"
   },
   {
    "name": "ReStreet-Linux-launcher.tar.gz.sha256",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.7-online-beta/ReStreet-Linux-launcher.tar.gz.sha256"
   }
  ]
 },
 {
  "tag_name": "v2.0.6-online-beta",
  "name": "ReStreet v2.0.6 — ONLINE BETA (crash and save fixes)",
  "published_at": "2026-10-21T04:15:03Z",
  "body": "## ReStreet 2.0.6 — fixes\n\n- **Crash in the middle of a match** (\"memory at 0x0000000100000000 could not be read\", exit code -1073741819) fixed: when the game's own memory filled up, some of its requests got nothing. Every request is now covered, and borrowed memory is given back and reused.\n- **Crash ~2 seconds after start** on PCs whose sound device refuses to be opened twice: the game now keeps the first working audio setup.\n- **World Tour progress from 2.0.0/2.0.1** (saved under your online name) is loaded again: if your save folder has no save with data, your own save is brought back automatically.\n- **\"Save file corrupt\" after a crash while saving**: the empty save is skipped instead of offered for deletion, and a deleted save is now kept in `_apagados` (last 10) instead of being destroyed.\n- **Portuguese (PT-BR)** chosen in the launcher now really shows the game in Portuguese.\n- **Achievements** load again when your Windows user name has accents or non-Latin letters.\n- **Launcher rooms**: the \"Loading game\" page is now fully silent (no EA SPORTS intro sound behind it).\n\nIf a crash happens, send us `Binaries\\crash_motivo.txt` and the logs from `Binaries\\Logs`.\nOn AMD graphics cards, use Direct3D 12 (Vulkan can crash in the AMD driver).\n",
  "html_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.0.6-online-beta",
  "assets": [
   {
    "name": "ReStreet-2.0.6-Setup.exe",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.6-online-beta/ReStreet-2.0.6-Setup.exe"
   },
   {
    "name": "ReStreet-2.0.6-Setup.exe.sha256",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.6-online-beta/ReStreet-2.0.6-Setup.exe.sha256"
   }
  ]
 },
 {
  "tag_name": "v2.0.5-online-beta",
  "name": "ReStreet v2.0.5 — ONLINE BETA (crash and sign-in fixes)",
  "published_at": "2026-10-20T22:37:52Z",
  "body": "## ReStreet 2.0.5 — fixes\n\n- **Crash entering a match (World Tour and others)** fixed: the game ran out of its own GPU memory when loading players' heads and stopped with an error. It now uses the free memory instead.\n- **Online sign-in** works again when the server is slow (it gave up after 1 second and showed OSDK_SIGNIN_REQUIRED).\n- **Crash ~30 s after start** when the save folder path has special characters.\n- **Ultrawide (21:9) screens**: no more crash loading a match.\n- **\"Settings File damaged\" / World Tour not saving** with OneDrive or a blocked Documents folder.\n- **My Squad player faces** are no longer noise.\n- **Launcher rooms** start the game on time, also with the launcher minimized.\n- **Older CPUs without AVX2** (before ~2015): the launcher now uses a build made for them.\n\nIf a crash happens, send us `Binaries\\crash_motivo.txt` and the logs from `Binaries\\Logs`.\nOn AMD graphics cards, use Direct3D 12 (Vulkan can crash in the AMD driver).\n",
  "html_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.0.5-online-beta",
  "assets": [
   {
    "name": "ReStreet-2.0.5-Setup.exe",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.5-online-beta/ReStreet-2.0.5-Setup.exe"
   },
   {
    "name": "ReStreet-2.0.5-Setup.exe.sha256",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.5-online-beta/ReStreet-2.0.5-Setup.exe.sha256"
   }
  ]
 },
 {
  "tag_name": "v2.0.4-online-beta",
  "name": "ReStreet v2.0.4 — ONLINE BETA (sign-in fix)",
  "published_at": "2026-10-19T23:33:57Z",
  "body": "# ReStreet 2.0.4 — Online Beta (sign-in fix)\n\nOpen the launcher and click **Update**. Your name, server, settings and World Tour progress are kept.\n\n## Fixed\n- **\"SIGN IN FAILED\" / `OSDK_SIGNIN_REQUIRED` on 2.0.3.** The 2.0.3 game was built without its online sign-in part, so nobody could go online. 2.0.4 puts it back.\n- Keeps the 2.0.3 fixes: World Tour no longer closes the game when leaving a match, and saving no longer closes the game when the Documents folder can't be used.\n\n## Still known\n- **Player portraits in World Tour (My Squad) look scrambled.** We're working on it.\n- \"Download not available\" for some World Tour teams: a harmless server message, you can keep playing.\n\nNew player? Download `ReStreet-2.0.4-Setup.exe`.\n",
  "html_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.0.4-online-beta",
  "assets": [
   {
    "name": "ReStreet-2.0.4-Setup.exe",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.4-online-beta/ReStreet-2.0.4-Setup.exe"
   },
   {
    "name": "ReStreet-2.0.4-Setup.exe.sha256",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.4-online-beta/ReStreet-2.0.4-Setup.exe.sha256"
   }
  ]
 },
 {
  "tag_name": "v2.0.3-online-beta",
  "name": "ReStreet v2.0.3 — ONLINE BETA (World Tour crash fixes)",
  "published_at": "2026-10-19T22:56:51Z",
  "body": "# ReStreet 2.0.3 — Online Beta (World Tour crash fixes)\n\nOpen the launcher and click **Update**. Your name, server, settings and World Tour progress are kept.\n\n## Fixed\n- **World Tour closed the game when leaving a match** (after playing or quitting). A piece of the game's code that World Tour calls at the end of a match was missing in 2.0.\n- **The game closed when saving** if Windows' Documents folder can't be used (for example a OneDrive folder that is missing or blocked). The game now reports the error instead of closing, and if Documents can't be used it saves in `Binaries\\UserData` next to the game.\n\n## Still known\n- **Player portraits in World Tour (My Squad) look scrambled.** We're working on it for the next update.\n- \"Download not available\" for some World Tour teams: a harmless server message, you can keep playing.\n- Same limits as 2.0: no mouse for skill moves yet, small stutters on Vulkan (use Direct3D 12), chat only in the launcher.\n\nNew player? Download `ReStreet-2.0.3-Setup.exe`.\n",
  "html_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.0.3-online-beta",
  "assets": [
   {
    "name": "ReStreet-2.0.3-Setup.exe",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.3-online-beta/ReStreet-2.0.3-Setup.exe"
   },
   {
    "name": "ReStreet-2.0.3-Setup.exe.sha256",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.3-online-beta/ReStreet-2.0.3-Setup.exe.sha256"
   }
  ]
 },
 {
  "tag_name": "v2.0.2-online-beta",
  "name": "ReStreet v2.0.2 — ONLINE BETA (World Tour saves restored)",
  "published_at": "2026-10-19T21:03:02Z",
  "body": "# ReStreet 2.0.2 — Online Beta (World Tour saves restored)\n\n**Your World Tour progress is back.** 2.0 looked for saves in a new, empty profile folder, so the game started from zero. Nothing was deleted: 2.0.2 loads the same saves as 1.5 again (`Documents\\fifastreet\\B13EBABEBABEBABE`).\n\nNote: anything played on 2.0 / 2.0.1 before this update stays in its own folder and is not merged.\n\nAlso includes the 2.0.1 sign-in fix. Open the launcher and click Update, or download `ReStreet-2.0.2-Setup.exe`.\n",
  "html_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.0.2-online-beta",
  "assets": [
   {
    "name": "ReStreet-2.0.2-Setup.exe",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.2-online-beta/ReStreet-2.0.2-Setup.exe"
   },
   {
    "name": "ReStreet-2.0.2-Setup.exe.sha256",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.2-online-beta/ReStreet-2.0.2-Setup.exe.sha256"
   }
  ]
 },
 {
  "tag_name": "v2.0.1-online-beta",
  "name": "ReStreet v2.0.1 — ONLINE BETA (sign-in fix)",
  "published_at": "2026-10-19T20:47:42Z",
  "body": "# ReStreet 2.0.1 — Online Beta (sign-in fix)\n\n**Fixes \"SIGN IN FAILED\" on 2.0.** The 2.0 game tried to sign in through a wrong local network address, so it never reached the servers. Your name, server region, settings and World Tour progress are kept on the server: nothing was lost, it just could not load while sign-in failed.\n\nAlready on 2.0 or 1.5.x? Open the launcher and accept the update. New here? Download `ReStreet-2.0.1-Setup.exe`.\n\nEverything else is as in [2.0](https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.0-online-beta).\n",
  "html_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.0.1-online-beta",
  "assets": [
   {
    "name": "ReStreet-2.0.1-Setup.exe",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.1-online-beta/ReStreet-2.0.1-Setup.exe"
   },
   {
    "name": "ReStreet-2.0.1-Setup.exe.sha256",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0.1-online-beta/ReStreet-2.0.1-Setup.exe.sha256"
   }
  ]
 },
 {
  "tag_name": "v2.0-online-beta",
  "name": "ReStreet v2.0 — ONLINE BETA",
  "published_at": "2026-10-19T20:21:00Z",
  "body": "# ReStreet 2.0 — Online Beta\n\nA new graphics engine, a launcher with friends, chat and game rooms, and online play that works from start to kick-off.\n\n## What's new\n- **New graphics engine** based on AnDr3zF's \"Deck\" port: sharper image and much higher frame rates (about 400 FPS in a match on the test PC, against about 165 on 1.5.7). Direct3D 12 and Vulkan.\n- **Graphics options:** FSR 1 modes, internal resolution up to 4x, aspect ratio, anisotropic filtering, ambient occlusion (SSAO, HBAO, GTAO), MSAA, FXAA and an FPS limit, the same as the in-game F5 menu.\n- **Launcher:** social page with online players, friends, requests and chat; game rooms that bring both players to the lobby; performance, display, graphics and controls pages.\n- **In-game friends Guide:** F6 or the controller's Xbox button, with your friends list and game invites.\n- **Game invites:** accept with A / Space, decline with B / Shift. Quit an online match with START + B.\n- **Brazilian Portuguese** translation by Eze_, with heights and weights in cm and kg.\n- **One installer** (`ReStreet-2.0-Setup.exe`): installs from your FIFA Street ISO or updates an older ReStreet install. 1.5.x launchers offer the update automatically; your name, server region and settings are kept.\n\n## Tested\nTwo players through the live-like server path (about 180 ms ping): sign-in and News Feed, friend requests, invites (accept and decline), launcher rooms to kick-off, START + B, launcher options (language, monitor) reaching the game. Not yet tested on two separate real PCs by the team, so please report what you find.\n\n## Known limits\n- No mouse for skill moves yet on 2.0 (the launcher option is hidden).\n- Vulkan has small stutters; use Direct3D 12 if you see them.\n- The game always renders internally at 1280x720 and is scaled to your screen.\n- START + B also shows the game's own \"leave this session?\" question behind it; close it with A.\n- A launcher room does not pull a player out of another lobby; leave that lobby first.\n- Chat is only in the launcher, not in the game.\n- Two games on the same PC can lose the connection after an invite (does not affect different PCs).\n\n## Install\nDownload `ReStreet-2.0-Setup.exe` (check it with `ReStreet-2.0-Setup.exe.sha256` if you like) and run it. Already on 1.5.x? Open the launcher and accept the update.\n\nBugs: report them on the Discord.\n\nCredits: SamuelitoDaVila (port, online, launcher, 2.0 integration), AnDr3zF (2.0 \"Deck\" engine base), Eze_ (Brazilian Portuguese).\n",
  "html_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.0-online-beta",
  "assets": [
   {
    "name": "ReStreet-2.0-Setup.exe",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0-online-beta/ReStreet-2.0-Setup.exe"
   },
   {
    "name": "ReStreet-2.0-Setup.exe.sha256",
    "browser_download_url": "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.0-online-beta/ReStreet-2.0-Setup.exe.sha256"
   }
  ]
 }
];
