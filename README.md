# CharactersVault

> **CharactersVault is a lightweight mod designed to store Character Data to a Server instead of the Player's disk.**

---

## Features
- **Character Data Persistence**: Characters are saved to the server's `CharacterVault` folder inside of `/config/bepinex/` instead of the player's local folder.
- **Server-Side Character Management**: Admins can see which players have bound characters on the server, and unbind if needed.

---

### Installation Type
- **Location:** Must be installed on both the Server and the Client.
- **Enforcement:** Client versions must match the server version.

### Manual Install
1. Ensure the prerequisites listed above are installed.
2. Extract the downloaded `.zip` archive.
3. Copy `CharactersVault.dll` into your `Valheim/BepInEx/plugins/` folder.
4. Launch the game once to generate the default configuration file.

---

## Configuration
The configuration file is automatically created at `BepInEx/config/com.charactervault.valheim.cfg` after running the game once.

| Section | Setting | Default | Description |
| :--- | :--- | :--- | :--- |
| `Enforcement` | `EnforceCharacterBinding` | `true` | If true, each account/platform ID is locked to their first registered character. |
| `ClientSync` | `AutoSaveIntervalMinutes` | `5.0` | How often (in minutes) client profiles automatically sync to the server in the background. |
| `ClientSync` | `ProfileSyncTimeoutSeconds` | `15.0` | Max seconds to wait for server profile data on join before timing out. |
| `Messages` | `KickMessageWrongCharacter` | `Wrong Character` | Message displayed when a player is kicked for attempting to join with the wrong character. |
| `UI` | `ShowCharacterSelectWarning` | `true` | Displays a warning banner on the character select screen reminding players about server-side character progression. |
| `Debug` | `VerboseLogging` | `false` | Enables extra diagnostic logging in the BepInEx console and log file. |

---

## Controls & Commands
- **Keybinds:** None.
- **Admin Commands:** *(Chat or Console; requires admin permissions in `adminlist.txt`)*
  - `/cv list` *(alias `/vault list`)*: Lists all registered player platform IDs and their bound character names.
  - `/cv status <playerId>` *(alias `/vault status <playerId>`)*: Views the character binding and last saved snapshot timestamp for a player.
  - `/cv reset <playerId>` *(aliases `/cv wipe`, `/cv remove`, `/cv unbind`, `/cv delete`)*: Wipes a player's server-side progression snapshot and removes their character lock so they can start fresh.
  - `/cv help` *(alias `/vault help`)*: Displays the in-game command help overview.

---

## Compatibility & Safe Removal
- **Save Integrity:** Always join a server with CharactersVault with a new character! Joining with old characters *WILL* wipe their inventory!

### AI Disclosure 

I made this mod using AI. Most of the code in this mod was AI generated. If you have an issue with this, I completely understand and urge you to not use this mod. This mod ("CharactersVault") is meant as a lightweight mod for small servers that don't need all the bells and whistles of a more complex mod.