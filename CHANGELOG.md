## v2.3.1

### Character and platform identity

- Use Valheim platform IDs for bindings and snapshots instead of assuming every player has a Steam ID.
- Accept the platform ID formats currently reported by Valheim for Steam and Xbox users, including `Steam_...` and `Xbox_...`.
- Reject invalid platform IDs before they can be used as binding keys or snapshot filenames.
- Update admin commands, logs, comments, and documentation to use `playerId` or `platform ID` terminology.

### Profile transfer reliability

- Bound incoming profile transfers to 64 MB.
- Reject malformed transfers, invalid chunk indexes, inconsistent chunk counts, and oversized chunks.
- Discard incomplete transfers when a peer disconnects.
- Expire abandoned transfers after two minutes to prevent unbounded memory growth.
- Reduce temporary memory usage during reassembly by writing chunks to a pre-sized stream.

### Data safety

- Write bindings and player snapshots to temporary files before replacing the live JSON file.
- Reduce the risk of corrupted persistence files if the server stops during a write.
- Refresh the in-memory binding cache immediately after an admin wipes player data.
- Return a defensive copy from the binding list API so display code cannot mutate the live binding table accidentally.

### Cleanup

- Removed unused ZDO mismatch configuration and the inactive inventory/skill enforcement settings.
- Removed the inactive override commands and override persistence code.
- Removed the unused mismatch report model and reflection helper methods.
- Updated comments and log messages to match the profile-sync implementation that is currently active.

### Compatibility notes

- PC Xbox Game Pass users are expected to work through Valheim crossplay, but this has not been tested with CharactersVault.
- Game Pass users still need a client installation capable of running BepInEx and CharactersVault.
- The existing character warning remains important: joining a CharactersVault server with an existing local character replaces that local character with the server-authoritative copy.
