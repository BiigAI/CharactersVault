# ServerCharacters

A **client + server** BepInEx mod for Valheim that enforces one character per player and prevents offline progression cheating by making the server the authoritative source of all character data.

**Both the server and all connecting clients must have this mod installed.**

---

## What It Does

| Feature | Description |
|---------|-------------|
| **Server-Authoritative Profile** | On every join, the server pushes the player's stored `.fch` file to the client. The client is forced to load this data — any items or XP gained offline are discarded. |
| **Character Lock** | Each Steam ID may only join with the character they first registered with. Trying to switch characters results in a kick. |
| **Blank Slate on First Join** | First-time players receive an empty character (no items, no skills). The server becomes the origin of truth from day one. |
| **Client Mod Enforcement** | Clients without the mod (or with the wrong version) are kicked after a configurable timeout. |
| **Mid-Session Sync** | The client sends the full character file to the server on every auto-save (default: every 5 minutes). |
| **ZDO Integrity Check** | Secondary layer: after the player spawns in-world, the server compares their live ZDO data against the last snapshot. Mismatch (e.g. from memory editing) → kick. |
| **Admin Overrides** | Admins can grant one-time bypasses, reset character bindings, or completely wipe a player's server data. |

---

## Installation

1. Install [BepInEx](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) on your Valheim **dedicated server** AND on all **client machines**.
2. Drop `ServerCharacters.dll` into `BepInEx/plugins/ServerCharacters/` on **both**.
3. Start the server. The first run creates the config and data files.

> **Important**: Players without the mod will be kicked immediately on join attempt. This is intentional.

---

## First-Time Setup (DLL References)

To build from source, copy the required DLLs into `libs/`. See [`libs/COPY_DLLS_HERE.md`](libs/COPY_DLLS_HERE.md).

```
dotnet build -c Debug
```

---

## Configuration

Edit `BepInEx/config/ServerCharacters.cfg`. Changes take effect on the next player join.

| Key | Default | Description |
|-----|---------|-------------|
| `EnforceCharacterBinding` | `true` | Enforce 1 character per Steam ID |
| `EnforceInventorySnapshot` | `true` | Kick on ZDO inventory mismatch |
| `EnforceSkillSnapshot` | `true` | Kick on ZDO skills mismatch |
| `ZdoSyncWaitSeconds` | `1.0` | Seconds between ZDO polling attempts on join |
| `ZdoSyncMaxWaitSeconds` | `90.0` | Max seconds to wait for ZDO before skipping check |
| `AutoSaveIntervalMinutes` | `5.0` | How often the client syncs its profile to the server |
| `ProfileSyncTimeoutSeconds` | `15.0` | How long client waits for server profile data before disconnecting |
| `KickMessageWrongCharacter` | *(see cfg)* | Message shown when wrong character is used |
| `KickMessageMismatch` | *(see cfg)* | Message shown on ZDO snapshot mismatch |
| `VerboseLogging` | `false` | Extra debug output in BepInEx log |

---

## Admin Management

### File-Based (Recommended for Headless Servers)

All data lives in `BepInEx/config/ServerCharacters/` on the **server**:

```
BepInEx/config/ServerCharacters/
├── bindings.json       ← Steam ID → character name mappings
├── overrides.json      ← Active one-time override grants
└── snapshots/
    └── 76561198XXXXXXXXX.json   ← Per-player snapshots (includes .fch bytes)
```

**To grant a one-time override** (allow a player in despite ZDO mismatch):
1. Open `overrides.json`
2. Add the player's Steam ID:
   ```json
   {
     "76561198XXXXXXXXX": true
   }
   ```
3. Save. The override is read on the player's next join and automatically consumed.

**To reset a character binding** (allow a player to use a different character):
1. Open `bindings.json`
2. Delete the entry for that Steam ID
3. Save. The player will re-register on next join.

### In-Game Commands (Admin Must Be in Server's `adminlist.txt`)

Type these in chat while connected to the server:

| Command | Effect |
|---------|--------|
| `/sc allow [steamId]` | Grant one-time ZDO override |
| `/sc deny [steamId]` | Revoke pending override |
| `/sc remove [steamId]` | Remove character binding (player re-registers next join) |
| `/sc wipe [steamId]` | **Delete ALL server data** — player gets blank character on next join |
| `/sc list` | Show all bindings |
| `/sc status [steamId]` | Show binding + snapshot info for a player |
| `/sc help` | List all commands |

---

## How It Works (Technical)

```
Player joins (client side)
  └── Game.Start() prefix patch
        ├── Sets "waiting for profile" flag
        └── Registers RPCs

  └── PlayerProfile.Load() prefix patch
        └── Returns false (BLOCKS original load)
              └── Starts coroutine: wait for server profile data

[Meanwhile on the server]
  └── ZNet.RPC_PeerInfo (postfix patch)
        ├── Check binding: SteamID → CharacterName
        │     Mismatch → Kick immediately
        └── Send handshake request to client

  └── Client replies with mod version
        ├── Version mismatch → Kick
        └── Version OK → Server sends stored .fch bytes (chunked RPC)

[Back on client — coroutine wakes up when bytes arrive]
  └── Write server .fch bytes to local character file
  └── Call PlayerProfile.Load() → loads server's character data
  └── Player spawns in with server's authoritative state

Player saves (mid-session or on disconnect)
  └── PlayerProfile.SavePlayerToDisk() postfix patch
        └── Read .fch bytes from disk → send to server (chunked RPC)
              └── Server overwrites snapshot file for this Steam ID

Player leaves (server side)
  └── ZNet.Disconnect (prefix patch)
        └── Clean up handshake tracking for this peer

Every 5 minutes (configurable)
  └── ClientSyncManager coroutine → triggers SavePlayerProfile()
        └── Intercepted by SavePlayerToDisk patch → sent to server

[After player fully loads in-world]
  └── ZDO polling coroutine (server side)
        └── Read live ZDO bytes (inventory + skills)
              Match stored snapshot → OK
              Mismatch → Check admin override
                    Override active → Consume, update baseline, allow
                    No override    → Kick
```

---

## Known Limitations

- **Byte-level ZDO comparison**: The ZDO snapshot stores raw bytes. After a **Valheim game update** that changes serialization format, all players may trigger a false-positive ZDO mismatch on their next join. Use `/sc allow` or add overrides in bulk — their new-format snapshot becomes the new baseline.
- **P2P hosted games**: The host player bypasses the join flow and won't have their profile checked by the server. Dedicated servers are strongly recommended.
- **Profile sync is not atomic**: If the server crashes between a client save and the snapshot being written to disk, the player may lose a few minutes of progress on next join (they'll be rolled back to the last successful server snapshot).

---

## Version

`2.0.0` — Client + Server architecture. Server is now the authoritative source of all character data.

`1.0.0` — Server-side only initial release.
