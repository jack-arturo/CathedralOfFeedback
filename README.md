# Cathedral of Feedback

Meta Quest (Unity 6000.3.18f1, Meta XR SDK 205) audio-reactive kaleidoscopic cathedral.

- `SpectrumAnalyzer` – 512-pt FFT -> 16 smoothed bands -> one `float4[4]` global (`_CathedralSpectrum`) + `_CathedralMetrics` (bass/mid/high/beat phase) + `_CathedralLatticeAngle`.
- `Cathedral/FractalWall` – 4-fold kaleidoscope tile shader, bass-pulsed. `Cathedral/Lattice` – ceiling, spin speed driven by treble.
- `GrabbableOrb` + `OrbGrabber` – grip/trigger grab (Space in editor); `TrailEcho` replays the path as beat-delayed ghosts (pooled, ring buffer).
- `ProceduralBeat` synthesises a 120 BPM loop; assign a clip to the AudioSource to use real music.

Setup: open in Unity, run **Cathedral > Build Scene** (creates `Assets/Scenes/Cathedral.unity`, materials, rig),
then in XR Plug-in Management enable OpenXR + Meta Quest feature group for Android.
CLI build: `Unity -batchmode -quit -projectPath . -executeMethod CathedralBuilder.BuildAndroid`.

Status: scripts/shaders have NOT been compiled in the editor yet (no Unity license available when scaffolded).
