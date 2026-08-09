# libs/ — Required DLLs

Copy the following DLLs into this folder before building.
**Do NOT commit these DLLs to source control** (they belong to IronGate/Unity).

---

## From your Valheim dedicated server installation:
`[Valheim Server Root]/valheim_Data/Managed/`

| File | Purpose |
|------|---------|
| `assembly_valheim.dll` | Core Valheim game code |
| `assembly_utils.dll` | Valheim utility and file helper types |
| `UnityEngine.dll` | Unity engine base |
| `UnityEngine.CoreModule.dll` | Unity core types |

## From your BepInEx installation:
`[Valheim Server Root]/BepInEx/core/`

| File | Purpose |
|------|---------|
| `BepInEx.dll` | BepInEx plugin framework |
| `0Harmony.dll` | HarmonyX patching library |

> **Note:** `Newtonsoft.Json.dll` is **not** needed here — it's pulled automatically via NuGet when you build.

---

## Recommended: Publicize the assembly

By default, many of Valheim's types and methods are `internal` or `private`.
Using [BepInEx.AssemblyPublicizer](https://github.com/BepInEx/BepInEx.AssemblyPublicizer)
to create a publicized copy of `assembly_valheim.dll` will prevent needing Reflection.

```
# Install the tool
dotnet tool install -g BepInEx.AssemblyPublicizer.Cli

# Publicize the DLL (run from this libs/ directory)
assembly-publicizer assembly_valheim.dll
```

This creates `assembly_valheim_publicized.dll` — rename it to `assembly_valheim.dll` and use it as your reference.

---

## .gitignore suggestion

Add this to your `.gitignore`:

```
libs/*.dll
```
