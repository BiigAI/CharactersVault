## v2.7.0

- **Accidental Wipe Protection & Safety Gate:** Joining with an existing character that has progression now safely disconnects the player instead of wiping their character, keeping singleplayer saves 100% safe.
- **Automatic Character Backups:** Automatically creates a timestamped local backup of `.fch` files in `CharactersVault_Backups/` before any reset.
- **Flexible Unbind & Character Import:** Split `/cv unbind` (retains server snapshot) from `/cv reset` (full wipe), and added `/cv allow-import` / `AllowCharacterImportOnFirstJoin` to allow intentional character imports.
- **Reduced Server Lag:** Server saves now process asynchronously in the background, eliminating stutter and lag spikes when saving profiles.
- **Connection & Join Fixes:** Fixed connection timeout kicks during world load, and added full support for Direct IP and LAN servers.
- **Safer Saves & Logouts:** Quitting to the menu now immediately flushes character progress to prevent rollbacks.
- **Server Stability & Security:** Hardened data transfers and improved save reliability on Linux and Docker servers.

## v2.6.1

- **Valheim 1.0 Compatibility:** Added full support for the Valheim 1.0 release! Resolved the crash during character saving and syncing caused by the game's internal updates, while maintaining seamless backwards compatibility with older versions.
- **Save System Update:** Updated profile writing to work cleanly with Valheim 1.0's new platform storage system.

## v2.6.0

- **Unified Player Reset:** Streamlined player unbinding and data wipes into a single command: `/cv reset [playerId]` (with aliases `/cv wipe`, `/cv remove`, `/cv unbind`, `/cv delete`). This cleanly wipes server-side progress, deletes old saves, and removes the character lock in one step.
- **Immediate Disconnect on Reset:** If an admin resets an online player, they are now immediately disconnected with a helpful message so they can join back fresh with a new character.
- **Character Name Validation:** Joining with a new character name after an unbind or reset now automatically discards any old character saves to prevent accidental carryover.
- **Updated README and Icon:** Updated the README and icon for the mod.

### Bug Fixes & Improvements

- **First-Join Reset Fix:** Fixed an issue where local inventory, skills, or items could carry over to the server when joining with a new character for the first time.
- **Upload Protection:** Prevented players from accidentally overwriting server saves if they are not registered to that character name.
- **Automatic Config Cleanup:** The mod now automatically removes old, unused configuration settings from previous versions on startup.

## v2.5.0

- **Smoother Syncing & Performance:** Fast actions (like sorting items in chests or rapid skilling) are now bundled intelligently, greatly reducing network lag and server load without risking any lost progress.
- **Admin Commands Prefix:** Updated admin commands to `/cv` (with `/vault` alias).

### Bug Fixes

- **Chat Formatting Fix:** Fixed an issue where raw color tags were displayed in chat when running admin commands.

## v2.4.0

- **Character Selection Warning:** Added a reminder banner on the Character Selection screen to prevent accidental loss of existing character progression (can be toggled in config).
- **Clear Disconnect Messages:** If a connection fails or is rejected (such as a mod version mismatch), the exact reason is now displayed directly on the disconnect screen.

## v2.3.4

- Resolved Thunderstore packaging and automated scanner compatibility issues.
- Excluded bundled `Newtonsoft.Json.dll` dependency from package output.
- General packaging and internal cleanup.

## v2.3.1

- **Platform ID Support:** Added support for platform IDs (`Steam_...` and `Xbox_...`) for player bindings and snapshot files.
- **Sync Reliability:** Added transfer size limits, chunk validation, and timeout handling for safer profile syncs.
- **Data Safety:** Atomic file saves for bindings and snapshots to prevent corruption if the server crashes.
- **Cleanup:** Removed obsolete config options, unused mismatch reports, and legacy code.
