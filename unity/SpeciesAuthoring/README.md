# Tyrant species authoring (experimental)

> **The Unity path is parked.** Tyrant no longer downloads tools to copy the template into Unity. Tune a species'
> prefab with **Prefab settings** (any field of its components) and give it its own look with **Open in Blender**.
> A species that already has a Unity bundle keeps loading it.

Open this folder as a project in **Unity Editor 2022.3.62f3**, matching the current
PK build. This is an authoring project, not a recreation of the game. Do not use
Play Mode with PK's components. Unity is needed to build bundles; players do not
need Unity installed.

## Prepare local references

Tyrant's **Create / show Unity project** step is not available in the app while the Unity path is parked.
Close the Unity project before preparing references for an existing project.

For a project copied manually, run Tyrant's sidecar CLI:

```powershell
& 'path\to\Tyrant\sidecar\tyrant.exe' unity prepare --project 'path\to\UnityProject' --game 'E:\SteamLibrary\steamapps\common\Prehistoric Kingdom'
```

This prepares game and third-party managed DLLs in `Assets/PKReferences`; Unity
supplies its own engine and standard framework assemblies. It includes PK's UI
and runtime support DLLs and installs NUnit through Unity's package manager.
`Assembly-CSharp` and `Assembly-CSharp-firstpass` are renamed to `PK.Authoring.Gameplay`
and `PK.Authoring.Firstpass` in these copies: Unity reserves the original names and
cannot attach components from DLL plugins with those names. Their references are
updated together, and Tyrant restores the game names when attaching the exported bundle.
These references are
local, ignored by Git, and must not be distributed with a mod. Run against the
same game version as the player. **Tyrant → Configure local PK references** fixes
Editor/Windows compatibility flags if an older project imported its DLLs incorrectly.

## Author and export

1. Add the project to Unity Hub and open it with 2022.3.62f3.
2. Create the species prefab in Unity yourself. Tyrant no longer recovers the template prefab for you.
3. Build a complete PK animal prefab. It needs the root's `Animal`, `AnimalSetup`, `AnimalAnimationController`,
   `AnimalGrowthManager`, and `AnimalAudioComponent`, including their linked assets.
   Include the Animator, meshes, materials, bones, LODs and serialized component settings.
   Keep the root **inactive**. Its animations, species gameplay
   settings and skins still inherit from the AnimalData template selected in Tyrant.
4. Open **Tyrant → Species prefab exporter**. Select the saved `.prefab` asset,
   validate, select an output folder outside `Assets`, and build the Windows bundle.
5. Attaching the bundle (**Attach Unity prefab bundle**) is not available in the app while the Unity path
   is parked; a species that already has a bundle keeps loading it.
6. Install the mod with the updated framework and restart PK. Test Nursery preview,
   spawning, movement, animation, growth, audio, skins, and save/reload in a test park.

The exporter builds one bundle with local dependencies and refuses external bundle
dependencies. It checks required components, inactive root, missing scripts,
broken serialized references and missing meshes/materials/bones. Structural validation
cannot establish that PK's game-specific data is configured correctly.

The recovered Spinosaurus prefab has passed Unity 2022.3.62f3 structural checks
and a Windows AssetBundle build/load test with 155 components and three skinned renderers.
Tyrant's assembly conversion/repacking has also been verified by converting the
diagnostic bundle back to authoring identities and loading it in Unity. The
diagnostic prefab is **not a playable animal**, and a complete custom prefab has
**not yet been tested in PK**.
Recovered shaders are **preview placeholders**, named `Tyrant/Recovered/<original-name>`.
Recovered material names also carry `Tyrant/RecoveredMaterial/` markers, so recovery
works even if Unity substitutes its error shader. At spawn Tyrant creates materials
from the loaded template's native materials, preserving their shader keyword and
render-pass state, then transfers the authored property values. The Unity preview does not reproduce PK's final rendering. Shader
reconnection still needs an in-game visual test. A new shader authored independently
in Unity is left alone; it must itself be compatible with PK's renderer.
PK skin/animation/growth logic may require additional compatibility work after the
first real prefab is authored. Adding a new script to the Unity project does not
install that script in the game: custom behavior needs a separate runtime mod.

## What belongs where

* **Unity prefab/AssetBundle:** hierarchy, rig, meshes, materials, animation assets
  and PK component wiring.
* **animals.db:** species identity, template, cost, gameplay overrides and the
  `animal_prefabs(species_id, bundle, asset)` reference.
* **Separate text files:** name, description, character notes and social info per language.
* **PNG:** optional Nursery portrait.

The species still selects a vanilla AnimalData template for inherited settings and
skin definitions. Its spawned GameObject uses the custom prefab. Tyrant registers
the species before native AI allocation and assigns its identity before activation.
The game's addressable user retains instance ownership and disposal, while Tyrant
keeps the bundle loaded for the session. Changing the prefab asset path does not
change the stable species/save ID.
