# CharactersVault

CharactersVault makes the server the authoritative source for player characters in Valheim. It locks each platform ID to one character and synchronizes that character between the server and the client.

> **WARNING: Joining a CharactersVault server with an existing character WILL wipe that character.**
>
> Use a new character when joining a CharactersVault server for the first time. Back up any character you care about before connecting.

## What Players Need to Know

- CharactersVault must be installed on the dedicated server and on every connecting client.
- On a first join, the server creates a blank character. Inventory, skills, recipes, food, trophies, and other progression are cleared. Appearance details such as hair and clothing are preserved.
- On later joins, the server replaces the local character file with the server's saved copy. Any items, skills, or other progress made offline are discarded.
- Each platform ID is locked to the first character it registers. Joining with a different character is rejected.
- The client sends character updates to the server after relevant actions, such as picking up or discarding items and gaining or losing skills. It also performs a general profile sync automatically, normally every five minutes.

## Installation

1. Install [BepInEx](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) on the dedicated server and on every client.
2. Install `CharactersVault.dll` in `BepInEx/plugins/CharactersVault/` on both the server and clients.
3. Start the server and connect with a new character.

Clients without CharactersVault, or clients running a different version, are disconnected.

PC Xbox Game Pass users should also be supported when connecting through Valheim crossplay, but this has not been tested with CharactersVault. They must still be able to install and run BepInEx and CharactersVault on the client.

## Server Data

CharactersVault stores its data on the server in:

```text
BepInEx/config/CharacterVault/
|-- bindings.json                 Platform ID to character name bindings
`-- snapshots/
    `-- Steam_76561198XXXXXXXXX.json
                                  Saved character data for each player
```

The configuration file is:

```text
BepInEx/config/CharacterVault.cfg
```

## Admin Commands

These commands require the admin to be listed in the server's `adminlist.txt`.

| Command | Effect |
|---------|--------|
| `/sc remove [playerId]` | Remove a character binding. The player can register a different character on the next join. |
| `/sc wipe [playerId]` | Delete the player's server snapshot and binding. The player receives a blank character on the next join. |
| `/sc list` | List all character bindings. |
| `/sc status [playerId]` | Show the player's binding and snapshot status. |
| `/sc help` | List available commands. |

## Configuration

Changes take effect on the next player join.

| Setting | Default | Description |
|---------|---------|-------------|
| `EnforceCharacterBinding` | `true` | Lock each platform ID to one character. |
| `AutoSaveIntervalMinutes` | `5.0` | How often the client sends its character data to the server. |
| `ProfileSyncTimeoutSeconds` | `15.0` | How long the client waits for the server's character data before disconnecting. |
| `VerboseLogging` | `false` | Enable additional log output. |

## Upgrading from CharacterVault

Install `CharactersVault.dll` on the server and every client, then remove the old `CharacterVault.dll`. Do not install both files at the same time. Existing `CharacterVault.cfg` settings and `BepInEx/config/CharacterVault/` data are preserved.

## Limitations

- CharactersVault is designed for dedicated servers. The host player in a peer-to-peer game does not go through the normal server join flow.
- Profile synchronization is not atomic. If the server stops while a save is being received, the next join may use the last successfully stored snapshot.
- The server stores character data in the Valheim profile format. Back up the `snapshots` directory before major Valheim updates.