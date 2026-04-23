# VRMA animations — attribution + workflow

## Bundled assets

| File in this folder | Source                                                                                                          | Role |
|---------------------|------------------------------------------------------------------------------------------------------------------|------|
| `wave.vrma`         | [`tk256ailab/vrm-viewer` · `VRMA/Goodbye.vrma`](https://github.com/tk256ailab/vrm-viewer/blob/main/VRMA/Goodbye.vrma)   | `<action:wave>` |
| `think.vrma`        | [`tk256ailab/vrm-viewer` · `VRMA/Thinking.vrma`](https://github.com/tk256ailab/vrm-viewer/blob/main/VRMA/Thinking.vrma) | `<action:think>` |
| `pixiv_demo.vrma`   | [`pixiv/three-vrm` · `examples/models/test.vrma`](https://github.com/pixiv/three-vrm/blob/dev/packages/three-vrm-animation/examples/models/test.vrma) (MIT) | idle scheduler — generic demo motion |

`tk256ailab/vrm-viewer` has no explicit license; its README states the
files are provided "for demonstration purposes". Treat `wave.vrma` +
`think.vrma` as development placeholders — swap them for clips with
explicit terms before any public release. `pixiv_demo.vrma` is MIT
(see [`LICENSE`](https://github.com/pixiv/three-vrm/blob/dev/LICENSE)
at the repo root) and safe to redistribute with attribution.

Previous versions of Seren backed `<action:nod>`, `<action:bow>` and
`<action:shake>` with procedural humanoid-bone rotations
(`useVRMGestures`). That pipeline has been removed — those actions
are now silent until someone drops matching `.vrma` files here and
registers them in `AvatarStage.DEFAULT_ACTION_CLIPS`.

## Where to source more .vrma

- **Mixamo** (Adobe, free account). Pick an animation, export as FBX,
  convert via [`tk256ailab/fbx2vrma-converter`](https://github.com/tk256ailab/fbx2vrma-converter)
  (MIT, Node.js CLI). Great for `idle`, `wave`, `nod`, `bow`, `shake`,
  `stretch`, and many more.
- **pixiv/three-vrm samples**. See `pixiv_demo.vrma` above — useful as
  a smoke-test asset but the motion is un-labelled and generic.
- **VRoid Hub** (https://hub.vroid.com/). Motion assets are user-
  uploaded with per-file licenses; download manually, check the terms,
  drop here.
- **Commission** an animator (ArtStation / Fiverr, ~$30–80 per clip).
- **VMD / MikuMikuDance** library → convert via Blender CLI +
  [`saturday06/VRM-Addon-for-Blender`](https://github.com/saturday06/VRM-Addon-for-Blender)
  (MIT) + MMD Tools add-on, scriptable via `blender --background --python`.

## How to wire a new .vrma in

1. Drop the file into this folder, e.g. `look_around.vrma`.
2. Open `src/ui/packages/seren-ui-shared/src/components/AvatarStage.vue`.
3. Add an entry to the right map, depending on usage :
   - **Idle rotation** (auto-fired by the scheduler during pauses) →
     `DEFAULT_IDLE_CLIPS`.
   - **LLM marker `<action:NAME>`** (fires on demand) →
     `DEFAULT_ACTION_CLIPS`.
4. The scheduler catalog is data-driven — it picks up the new entry
   automatically on next reload. No test to update.

## Suggested action ids

`idle`, `wave`, `think`, `nod`, `bow`, `shake`, `look_around`,
`stretch`, `breath_deep` — any string you like. The scheduler picks
at random from whatever is registered in `DEFAULT_IDLE_CLIPS`.
