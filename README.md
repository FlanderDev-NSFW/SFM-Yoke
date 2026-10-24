# Yoke

A neck-and-wrist yoke for SecretFlasherManaka (BepInEx 6 IL2CPP): a metal bar across her shoulders, the neck ring in the middle
and a wrist cuff at each end; her hands are held up beside her head. Worn on the body like SpreaderBar, so she can still walk.
Single-player/offline only.

**Needs RestraintHub** (0.2.0+; included in the package): ring entry, number shortcuts, the list of worn restraints, F and + / -
routing (only while the yoke is the selected restraint), input blocking and conflicts are the hub's (`mods/RestraintHub/README.md`).

**Status 0.2.0:** usable, L3 (see Verification). Pick **Yoke** in the common ring (hold 1; the restraint page after the game's, page
5) or put it on a number key; hold **F** to struggle free (`Unlock.SuccessChance`, default: the first attempt).

- **Pose:** both arms are aimed every frame on top of the animation (walking, crawling, idle ...): the forearm passes up through
  its cuff as upright as the arm allows, the cuff near the wrist (`Pose.CuffOnForearm`), the upper arm reaching its elbow exactly
  (`Logic/YokePose.cs`). The yoke rides on her upper body (neck ring around the neck, bar along the shoulder line).
- **Like handcuffs:** walking and crouching / crawling work; no running (Shift is ignored, except to end a pose skill). What needs
  her hands is refused with "Can't do that with the yoke on.": masturbating, dildo, the game's handcuffs, eye mask, drinking,
  dropping items, taking clothes off / opening the coat, pose skills. A pose skill that runs when the yoke goes on is ended.
  Refused while something else holds her neck or arms (the game's handcuffs, the wall restraint) - the hub checks.
- **Width:** `+` / `-` while it is on (2 % steps, saved in `Yoke.Width`): 1 = the model's proportions. Narrower stands her forearms
  up; `+` stops when the forearms would lean more than 45 deg (they would lie across the cuffs) or the arms cannot reach.
- **Size:** the whole yoke is scaled so the neck ring fits her neck (estimated from the spine; sizes differ per save); it is sized
  again when her body changes (after a load it is measured before her body settings apply).
- **Saved** with the game (lock, unlock chance, number shortcuts; three keys in the slot file); a load with the yoke on puts it back.

## Model

`assets/yoke.ykm` comes from `Yoke_V1.1_Z88OaNZ.blend` (object `Yoke` in the collection `Yoke`; the `Old` collection is not used),
exported by `tools/export_model.py` and embedded in the DLL:

```bash
blender -b --disable-autoexec "D:/Assets/_3D/Yoke_V1.1_Z88OaNZ.blend" --python mods/Yoke/tools/export_model.py -- mods/Yoke/assets/yoke.ykm
```

One mesh of 124 loose pieces (all weighted to one bone), sorted along the bar into neck ring, two cuffs and two rods; units as in
the file (neck ring hole 0.379, cuff hole 0.306, cuffs 3.28 from the middle), origin at the neck ring's centre. For the width the
cuffs move along the bar and the rods stretch. 54k triangles; the game's handcuff material (toon shader), `Model.MetalShade`.

## Config (`BepInEx/config/com.local.yoke.cfg`)
| Key | Default | Meaning |
|---|---|---|
| General.RingLabel | Yoke | name of the ring entry |
| Unlock.AttemptSeconds | 4.5 | seconds of holding F per attempt |
| Unlock.SuccessChance | 1 | chance an attempt frees her (failures add 0.02, like the game's handcuffs) |
| Yoke.Width | 1 | cuffs' distance from the neck ring as a factor of the model; + / - store their result here |
| Yoke.StepPercent | 2 | one + / - step (% of the model's width) |
| Model.Size | 1 | the whole yoke relative to the fit around her neck |
| Model.NeckClearanceCm | 0.6 | room between neck and neck ring |
| Model.MetalShade | 0.3 | brightness of the metal |
| Pose.NeckRingAt | 0.25 | neck ring height on the neck (0 = base, 1 = head joint) |
| Pose.CuffOnForearm | 0.88 | preferred cuff place on the forearm (0 = elbow, 1 = wrist; capped so the ring stays below the hand) |
| Debug.Disable | 0 | bit 1 = do not pose the arms; 2 = aim with the LateUpdate bone lengths (negative controls; blocking no input: RestraintHub's Debug.Disable 1) |

## Diagnostics (bridge, session started with `--allow-invoke`)
`game call Yoke.YokeController LockHere | Release`, `game get Yoke.YokeController::Status` (scale, neck / hole sizes, width,
per arm: cuff place on the forearm, forearm lean, how far the upper arm misses its elbow).

## Verification
- L1: `./tools/dev.ps1 test Yoke` (54) - model file and anchors, format errors, frame (winding kept), width / rod stretch, arm solve
  (exact reach, forearm through the cuff, out-of-reach miss) for the first save's and the "Amateur" body.
- L3 0.1.0 (2026-10-24, dev copy, first save in the Apartment): `LockHere` -> scale 0.145 (neck r 4.9 cm), cuffs 0.95 m apart,
  forearms 39 deg from upright, miss 0; screenshots (main view, body inset): neck ring on the neck, wrists in the cuffs, hands above.
  Negative control `Debug.Disable = 1`: the arms hang, the yoke stays at the neck. Ring: page 5 slot 2 selected + left click ->
  "ring: yoke chosen", locked (key 1 kept its shortcut). Dildo shortcut (key 2) -> "Can't do that with the yoke on.", action None;
  Armpit Squat (key 5) refused, with `Debug.Disable = 4` it starts (negative control). Locked while in the squat: the pose ended.
  Shift + W: `IsDash` false, walked 0.78 m with the arms held. Width 0.84: forearms 23 deg, cuffs at 0.88 of the forearm; `+` stopped
  at 1.04 (44.7 deg) with its message. Crawling: the yoke follows the upper body, arms ahead through the cuffs. Hold F 4.5 s ->
  "attempt: success", "release: struggled free", yoke gone. Save at home with the yoke on -> restart -> load: yoke back, resized once
  (first measured 20 % small), no exception; struggled free and saved again (slot left without the yoke). With SpreaderBar: both
  shown together. "Amateur" save: scale 0.081, cuffs 0.53 m apart, forearms 41 deg, miss 0 (screenshot).

- L3 0.2.0 with RestraintHub (2026-10-24): chosen in the ring (page 5, select + click), selected in the list; `-` changed only the
  yoke while selected, F with the bar selected left the yoke on; refusals and the pose-skill negative control through the hub.
  Details: `mods/RestraintHub/README.md`.

## Known limits / next
- At the model's width the forearms lean ~40 deg out (the cuffs are wider than her bent arms reach upright); `-` to ~0.85 stands
  them up. The cuffs do not tilt with the forearm.
- The hands keep the animation's angle at the wrist (they bend inward a little).
- The neck size is an estimate (12 % of the spine), like WallRestraint's collar.
