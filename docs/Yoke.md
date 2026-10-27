# Yoke

> Developer notes: how it works, settings in detail, diagnostics and the test history. The player readme (shipped in the
> release zip) is [`mods/Yoke/README.md`](../../mods/Yoke/README.md).
>
> **1.0.0 (2026-10-27): first release** (F3M). Plugin GUID `FlanderDev.SFM.Yoke` (was `com.local.yoke`), so the
> config file is `BepInEx/config/FlanderDev.SFM.Yoke.cfg`. The release zips no longer include RestraintHub (it is its own download).

A neck-and-wrist yoke for SecretFlasherManaka (BepInEx 6 IL2CPP): a metal bar across her shoulders, the neck ring in the middle
and a wrist cuff at each end; her hands are held up beside her head. Worn on the body like SpreaderBar, so she can still walk.
Single-player/offline only.

**Needs RestraintHub** (1.0.0+, a separate download): ring entry, number shortcuts, the list of worn restraints, F and + / -
routing (only while the yoke is the selected restraint), input blocking and conflicts are the hub's (`docs/mods/RestraintHub.md`).

**Status 0.3.0:** usable, L3 (see Verification). Pick **Yoke** in the common ring (hold 1; the restraint page after the game's, page
5) or put it on a number key; hold **F** to struggle free (`Unlock.SuccessChance`, default: the first attempt).

- **Pose:** both arms are aimed every frame on top of the animation (walking, crawling, idle ...): the forearm passes up through
  its cuff as upright as the arm allows, the cuff near the wrist (`Pose.CuffOnForearm`), the upper arm reaching its elbow exactly
  (`Logic/YokePose.cs`). The yoke rides on her upper body (neck ring around the neck, bar along the shoulder line).
- **Swivelling, fitted cuffs:** each wrist cuff turns on its rod (about the middle of its wall, where the rod enters) to follow the
  forearm, up to `Model.CuffMaxAngle` (70 deg), so a leaning forearm never lies across it. The cuffs are sized to her wrist
  (estimated as 7 % of the forearm) with `Model.CuffClearanceCm` to spare - about 2.3 cm hole radius instead of the model's 4.4.
- **Neck ring follows her neck:** it turns about its centre from the shoulders' line towards her neck (neck -> head joint, up to
  `Model.NeckRingMaxAngle`, 45 deg), so it stays square to the neck when she looks down or leans her head; the rods are aimed
  from its turned knobs to the cuffs' joints. It is fitted to her neck on its own (7 % of the spine + `Model.NeckClearanceCm`:
  3.3 cm hole radius on the first save); the yoke's size and the cuffs' spacing do not change with it.
- **Like handcuffs:** walking and crouching / crawling work; no running (Shift is ignored, except to end a pose skill). What needs
  her hands is refused with "Can't do that with the yoke on.": masturbating, dildo, the game's handcuffs, eye mask, drinking,
  dropping items, taking clothes off / opening the coat, pose skills. A pose skill that runs when the yoke goes on is ended.
  Refused while something else holds her neck or arms (the game's handcuffs, the wall restraint) - the hub checks.
- **Width:** `+` / `-` while it is on (2 % steps, saved in `Yoke.Width`): 1 = the model's proportions. Narrower stands her forearms
  up; `+` stops when a cuff reaches its swivel limit or the arms cannot reach (first save: up to 1.28, arms nearly straight; with
  fixed cuffs, `CuffMaxAngle` 0, at a 45 deg lean as before).
- **Size:** the yoke (rods, cuffs' spacing) is scaled from her spine (sizes differ per save), the rings fitted to her neck and
  wrists; all sized again when her body changes (after a load it is measured before her body settings apply).
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

## Config (`BepInEx/config/FlanderDev.SFM.Yoke.cfg`)
| Key | Default | Meaning |
|---|---|---|
| General.RingLabel | Yoke | name of the ring entry |
| Unlock.AttemptSeconds | 4.5 | seconds of holding F per attempt |
| Unlock.SuccessChance | 1 | chance an attempt frees her (failures add 0.02, like the game's handcuffs) |
| Yoke.Width | 1 | cuffs' distance from the neck ring as a factor of the model; + / - store their result here |
| Yoke.StepPercent | 2 | one + / - step (% of the model's width) |
| Model.Size | 1 | the yoke (rods, cuffs' spacing) relative to her size; the rings keep their fit |
| Model.NeckClearanceCm | 0.4 | room between neck and neck ring (the neck is estimated; down to -1 for tighter) |
| Model.CuffClearanceCm | 0.5 | room between wrist and cuff (the wrist is estimated; raise it for sleeves, down to -1 for tighter) |
| Model.CuffMaxAngle | 70 | how far the cuffs swivel to follow the forearms (deg; 0 = fixed upright as modelled) |
| Model.NeckRingMaxAngle | 45 | how far the neck ring turns with her neck (deg; 0 = fixed to the shoulders' line) |
| Model.MetalShade | 0.3 | brightness of the metal |
| Pose.NeckRingAt | 0.25 | neck ring height on the neck (0 = base, 1 = head joint) |
| Pose.CuffOnForearm | 0.88 | preferred cuff place on the forearm (0 = elbow, 1 = wrist; capped so the ring stays below the hand) |
| Debug.Disable | 0 | bit 1 = do not pose the arms; 2 = aim with the LateUpdate bone lengths (negative controls; blocking no input: RestraintHub's Debug.Disable 1) |

## Diagnostics (bridge, session started with `--allow-invoke`)
`game call Yoke.YokeController LockHere | Release`, `game get Yoke.YokeController::Status` (scale, neck / hole sizes, width,
wrist and cuff hole, per arm: cuff place on the forearm, forearm lean, how far the cuff turned, forearm to cuff, how far the upper
arm misses its elbow); `game get Yoke.YokeController::NeckDeg` (the neck ring's turn as last drawn).

## Verification
- L1: `./tools/dev.ps1 test Yoke` (80) - model file and anchors, format errors, frame (winding kept), width / rod stretch to the
  cuff's joint, rod ends with the neck ring turned / tightened and the rod aimed between them, cuff turned / sized about its joint, arm solve (exact reach, forearm through the cuff, out-of-reach miss), swivel
  solve (settles with the forearm through the turned cuff, 0 deg off; fixed and limited swivel) for the first save's and the
  "Amateur" body.
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
  Details: `docs/mods/RestraintHub.md`.

- L3 0.3.0 swivelling cuffs (2026-10-24, first save, Apartment): `LockHere` at width 0.71 -> wrist r 2.5 cm, cuff fit 0.66 (hole r
  2.9 cm), forearms 6 deg, cuffs turned 6 deg; screenshot (mirror, cropped): cuffs snug on the wrists, no clipping. Width 1.0, then
  `+` x 14 -> stopped at 1.28: forearms 69 deg, cuffs turned 69 deg, forearm to cuff 0 deg; screenshot: arms nearly straight, the
  cuff turned around the wrist. Negative control `CuffMaxAngle` 0 from width 1.0: cuffs not turned, `+` stopped at 1.08 (44.5 deg,
  the old rule). Values put back afterwards (Width 0.71, CuffMaxAngle 70), nothing saved. A wrist measurement from the skin bake
  (as SpreaderBar's) found no points: the bake is 10-34 cm off her arms and head (see api-notes "player-and-movement").
- L3 0.3.0 neck ring and tighter cuffs (2026-10-24, same save): wrist estimate 7 % of the forearm -> wrist r 1.8 cm, cuff fit 0.47,
  hole r 2.1 cm; screenshot at width 0.75 (mirror, cropped): cuffs snug on the wrists, nothing through them. Standing idle the neck
  ring turned 27 deg with her neck; screenshots side by side: following, the ring sits square around the neck; negative control
  `NeckRingMaxAngle` 0 (`NeckDeg` 0): level with the shoulders, cutting into the chin. Rods meet the turned knobs. Values put back
  (the player's Width 1.03, NeckRingMaxAngle 45); CuffClearanceCm set to its new default 0.3 (0.4 was this version's earlier default).
- L3 0.3.0 fit tuned (2026-10-24, same save; the player saw the 2.1 cm wrist cuffs clip): screenshot rows at width 0.75 with neck
  room 0.6 / 0 / -0.5 / -1 / -1.5 cm on the old 4.9 cm neck estimate - the ring met the neck at about -1.5 (neck ~2.7 cm), so the
  ring now fits 7 % of the spine (2.9 cm) + 0.4 cm; wrist room 0.5 and 0.7 showed no clipping, 0.5 taken. Final: neck ring hole
  3.3 cm, cuff hole 2.3 cm, yoke scale unchanged (0.145); screenshots front (mirror) and back: snug, no skin through the rings.
  Width put back to the player's 1.03; the clearances set to the new defaults (the old ones were this version's own).

## Known limits / next
- At the model's width the forearms lean ~40 deg out (the cuffs are wider than her bent arms reach upright); `-` to ~0.85 stands
  them up. The cuffs turn with the forearm (a ball joint: the model has no hinge to show it).
- The wrist size is an estimate (7 % of the forearm); sleeves are not measured (`Model.CuffClearanceCm`).
- The neck ring and the rods meet at a ball joint the model does not show; turned far, the knobs leave the bar's straight line.
- The hands keep the animation's angle at the wrist (they bend inward a little).
- The neck size is an estimate (7 % of the spine for the ring; the yoke's size still uses 12 %, so the cuffs' spacing did not change).
