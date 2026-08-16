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
