# Yoke 1.0.0

A neck-and-wrist yoke for **Secret Flasher Manaka**: a metal bar across her shoulders with a ring around her neck and a cuff
at each end. Her hands are held up beside her head, and she can still walk.

Model: ["BDSM Yoke"](https://smutba.se/project/29b8fcfb-1a89-406b-910b-51a8af653906/) by Kryiat ([CC BY-NC-ND 4.0](https://creativecommons.org/licenses/by-nc-nd/4.0/)).

## Requirements
- Secret Flasher Manaka (tested with v1.1.3)
- BepInEx 6 for IL2CPP (tested with build 738)
- **RestraintHub 1.0.0** or newer

## Install
Extract the zip into the game folder (the folder with `SecretFlasherManaka.exe`). You get `BepInEx/plugins/Yoke/`.

## How to use
Hold a ring key (e.g. `1`), page with `Z` / `X` to the restraint page and click **Yoke**. With it selected in the worn list
(`B` selects):

| Key | Does |
|---|---|
| `+` / `-` | wider / narrower |
| hold `F` | struggle free |

She can walk, crouch and crawl, but not run. Everything that needs her hands is refused (toys, the handcuffs, drinking,
clothes, pose skills ...). The yoke is sized to her body.

## Settings
`BepInEx/config/FlanderDev.SFM.Yoke.cfg` (created on the first start). Most useful:

| Setting | Default | |
|---|---|---|
| Yoke.Width | 1 | width (`+` / `-` change it) |
| Model.Size | 1 | size of the yoke |
| Model.CuffClearanceCm | 0.5 | room in the wrist cuffs (raise it for sleeves) |
| Unlock.SuccessChance | 1 | chance that struggling frees her (1 = first try) |

## Good to know
- Whether the yoke is on is saved with the game.
- At the default width the forearms lean outwards; `-` stands them up.
- Wrist and neck sizes are estimated from her body, so sleeves can poke through the cuffs.

Single-player / offline only. Code: MIT licence (`LICENSE.txt`). Model: ["BDSM Yoke"](https://smutba.se/project/29b8fcfb-1a89-406b-910b-51a8af653906/) by Kryiat, CC BY-NC-ND 4.0 (smutba.se; details in `LICENSE-MODELS.txt`).

## Building from source
- .NET SDK 8, and a copy of the game with BepInEx 6 IL2CPP started once: copy `Directory.Build.local.props.example` to
  `Directory.Build.local.props` and set `GameDir`. Then `dotnet build src/Yoke/Yoke.csproj -c Release`.
- It compiles against RestraintHub: the [SFM-RestraintHub](https://github.com/FlanderDev-NSFW/SFM-RestraintHub) repository next to this one, `lib/RestraintHub/RestraintHub.dll`, or the hub installed in the game.
- The source of the mod is `src/Yoke` and its tests (`tests/`); `lib/SfmKit` is a copy of the shared [SfmKit](https://github.com/FlanderDev-NSFW/SfmKit)
  library; `docs/` holds the developer notes.
- Every push to `main` builds, tests and releases it (`.github/workflows/release.yml`, `build/ci.py`).
