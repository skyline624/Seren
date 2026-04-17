# VRMA animations — attribution

The VRM Animation files in this directory are redistributed from the
[`tk256ailab/vrm-viewer`](https://github.com/tk256ailab/vrm-viewer)
repository, which exposes them as demo assets for `three-vrm` development.

| File in this folder | Source file                                                                                        | Action marker |
|---------------------|----------------------------------------------------------------------------------------------------|---------------|
| `wave.vrma`         | [`VRMA/Goodbye.vrma`](https://github.com/tk256ailab/vrm-viewer/blob/main/VRMA/Goodbye.vrma)        | `<action:wave>` |
| `think.vrma`        | [`VRMA/Thinking.vrma`](https://github.com/tk256ailab/vrm-viewer/blob/main/VRMA/Thinking.vrma)      | `<action:think>` |

The upstream repository has no explicit license file; its README states
the files are provided "for demonstration purposes". We treat them as
development placeholders — before any public release of Seren, swap
these for animations whose redistribution terms are explicit (for
instance the 7-item free pack distributed by the VRoid Project on
[BOOTH](https://booth.pm/) or bespoke bakes).

For `<action:nod>`, `<action:bow>` and `<action:shake>` we rely on
procedural humanoid-bone rotations in `useVRMGestures` — those are
license-free because they're pure code, not asset files.
