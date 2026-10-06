# Installing mods made with Tyrant

This guide is for players. It shows how to get mods made with Tyrant (new skins, models, sounds and more) into
Prehistoric Kingdom, and how to take them out again. You do not need to make mods yourself.

Mods made with Tyrant never change the game's own files. Two small pieces make them work:

- **MelonLoader**, the community's mod loader (version **0.7.3**). It starts mods when the game starts.
- **Tyrant Framework**, a MelonLoader mod. It loads every Tyrant mod you install.

You set these up once. After that, installing a mod is unzipping one file, or one click in the Tyrant app.

There are two ways to do it. Pick one:

- [With the Tyrant app](#with-the-tyrant-app): easiest; the app does every step and can undo them.
- [Without the Tyrant app](#without-the-tyrant-app): by hand, with two downloads and some unzipping.

> **Before you start:** back up your saves if you cannot lose them. Mods are tested, but they are made by fans.

## Find your game folder

Both ways need the game folder at some point. In Steam: right-click **Prehistoric Kingdom** → **Manage** → **Browse
local files**. The folder that opens has `Prehistoric Kingdom.exe` in it. That is the **game folder**.

## With the Tyrant app

1. Download the latest **Setup** from the [releases page](https://github.com/ViraLCyclopes/Tyrant/releases) and run
   it. No admin rights are needed. If Windows says the publisher is unknown, click **More info → Run anyway** (Tyrant is
   not code-signed).
2. Start Tyrant. It finds the game through Steam. If it cannot, click **Find the game folder…** on Home and pick the
   game folder.
3. Click **New workspace…** on Home and choose an empty folder outside the game folder, for example
   `D:\Tyrant\workspace`. This is where Tyrant keeps your mods.
4. Open the **Mods** tab and click **Add mod from zip…**. Pick the mod's zip file. Repeat for each mod.
5. Click **Install to game** on the mod. The first time, Tyrant asks to install MelonLoader and Tyrant Framework: say
   yes.
6. Start the game as usual from Steam.

**Turning mods on and off:** the **On** switch on the Mods tab decides whether a mod loads at the next game start.
**Remove from game** takes a mod out.

**Removing everything:** Workspace tab → **Uninstall from game** takes out the framework and every installed mod.
It also removes MelonLoader, unless other mods still use it.

**After a Tyrant update:** if the new Tyrant has a newer framework, it offers **Update Tyrant in game** once. Click it,
or install any mod, which updates the framework too.

## Without the Tyrant app

### 1. Install MelonLoader 0.7.3 (once)

1. Download
   [MelonLoader.x64.zip (v0.7.3)](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip).
2. Unzip it into the game folder. Afterwards, `version.dll` and a `MelonLoader` folder sit right next to
   `Prehistoric Kingdom.exe`.
3. Start the game once and close it again. MelonLoader opens a console window next to the game; that is normal.

If the game folder already has a `version.dll` from another mod loader (such as BepInEx), remove that loader first.
The two do not work together.

### 2. Install Tyrant Framework (once)

1. Download `Tyrant-Framework-<version>.zip` from the
   [releases page](https://github.com/ViraLCyclopes/Tyrant/releases), under the latest release's **Assets**.
2. Unzip it into the game folder. It adds `Mods\Tyrant.Framework.dll` and files under `UserLibs\`.

### 3. Install a mod

1. Unzip the mod's zip into the game folder. The mod lands in `UserData\Tyrant\Mods\<mod id>\`.
2. Start the game.

Each mod's zip has a `README.txt` that names the framework version it needs. If a mod needs a newer framework than
you have, download the newer `Tyrant-Framework` zip and unzip it over the old one.

### Turning mods off, load order, and removing

- **Remove a mod:** delete its folder, `UserData\Tyrant\Mods\<mod id>`.
- **Turn a mod off without deleting it**, or **choose the load order:** create or edit `UserData\Tyrant\mods.json`.
  Mods listed there load from top to bottom, and `"enabled": false` turns one off. Mods not listed load after the
  listed ones, sorted by id:

  ```json
  {
    "format": 1,
    "mods": [
      { "id": "anax-voice", "enabled": true },
      { "id": "red-spot-carcharo", "enabled": false }
    ]
  }
  ```

  When two mods change the same thing (the same animal's model, say), the one that loads **later** wins.
- **Remove the framework:** delete `Mods\Tyrant.Framework.dll` and the `UserLibs\Tyrant.Framework*.dll` files.
- **Remove MelonLoader:** delete `version.dll` and the `MelonLoader` folder (and `Mods`, `Plugins`, `UserData` and
  `UserLibs` if nothing else uses them).

## Check that it works

Start the game and open `MelonLoader\Latest.log` in the game folder (any text editor). Look for a line like:

```
Tyrant framework 0.1.0: 2 mod(s) loaded, 0 skipped.
```

Above it, each loaded mod has a line saying what it changes (textures, skins, models, sounds). A mod that could not load
has a warning line `<mod id>: skipped — <reason>`, for example a missing file or a mod that needs a newer framework.
The log also lists each texture a mod replaced, and each replaced sound the first time it plays.

New skins show up in the **Nursery** next to the game's own. For species with many skins, a grid button next to
**Design** opens **All skins**, with a search box.

## When something goes wrong

- **No console window and no `Latest.log`:** MelonLoader is not installed. Check that `version.dll` and the
  `MelonLoader` folder are directly in the game folder, not in a subfolder.
- **`Latest.log` has no "Tyrant framework" line:** the framework is not installed. Check for
  `Mods\Tyrant.Framework.dll`.
- **A mod is skipped:** read the reason in `Latest.log`. Usually it needs a newer framework (download the latest
  `Tyrant-Framework` zip), or a file is missing (download the mod again).
- **The game updated and a mod stopped working:** game updates can break mods until the framework or the mod is
  updated. Check the [releases page](https://github.com/ViraLCyclopes/Tyrant/releases) and the mod's own page.
- **Still stuck:** open an issue on the [issue tracker](https://github.com/ViraLCyclopes/Tyrant/issues) and attach
  `MelonLoader\Latest.log`. With the Tyrant app, **Help → Copy diagnostics** copies what we need to the clipboard.

Only download Tyrant and the framework from the [official releases page](https://github.com/ViraLCyclopes/Tyrant/releases).
See the [terms of use](../TERMS.md) for the rules on game content and sharing.
