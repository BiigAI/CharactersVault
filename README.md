# CharactersVault

CharactersVault keeps player progression strictly on your Valheim server. It prevents players from bringing in cheated or offline items by saving, syncing, and locking characters directly to the server.

>  **IMPORTANT: Always make a brand new character before joining!**  
> Joining a CharactersVault server with an existing character **will wipe** their inventory, skills, recipes, and progression on first join (visual appearance like hair and clothes is kept).

---

## For Players

1. **Install the mod:** Install via **Thunderstore** or **r2modman** (recommended), or manually place `CharactersVault.dll` in your `BepInEx/plugins/CharactersVault/` folder with [BepInEx](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) installed.
2. **Create a fresh character:** Pick any name and appearance, then join the server.
3. **Play normally:** Your inventory and skills automatically sync to the server as you play and save when you disconnect.

> **Note:** Each player account is bound to one character on the server. If you try to join with a different character, the connection will be rejected.

---

## For Server Hosts

CharactersVault is designed for **dedicated servers**. Both the server and connecting clients must have the mod installed with matching versions.

### Installation

1. **Install the mod:** Install via **Thunderstore** / **r2modman**, or manually place `CharactersVault.dll` in your dedicated server's `BepInEx/plugins/CharactersVault/` folder with [BepInEx](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) installed.
2. **Start your server.**

### Admin Commands

Admins listed in `adminlist.txt` can manage player bindings and snapshots in-game using `/sc` chat commands:

| Command | Description |
|---|---|
| `/sc list` | List all registered players and their bound characters. |
| `/sc status [playerId]` | View character binding and last saved snapshot info for a player. |
| `/sc remove [playerId]` | Unbind a player so they can register a new character on their next join. |
| `/sc wipe [playerId]` | Delete all saved character data and binding for a player (starts fresh). |
| `/sc help` | Show in-game command help. |

*Note: Use the platform ID shown in `/sc list` (e.g. `Steam_76561198XXXXXXXXX` or `Xbox_...`).*

### Configuration

Config file location: `BepInEx/config/CharacterVault.cfg`

| Setting | Default | Description |
|---|---|---|
| `EnforceCharacterBinding` | `true` | Lock each account/platform ID to their first registered character. |
| `AutoSaveIntervalMinutes` | `5.0` | How often client profiles automatically sync to the server in the background. |
| `ProfileSyncTimeoutSeconds` | `15.0` | Max seconds to wait for server data before disconnecting high-latency clients. |
| `ShowCharacterSelectWarning` | `true` | Show warning banner on the character select screen. |
| `VerboseLogging` | `false` | Enable extra debug logs in the BepInEx console. |

### Server Storage

All character snapshots and bindings are saved under `BepInEx/config/CharacterVault/`. Remember to back up this folder during regular server backups or before major Valheim game updates.