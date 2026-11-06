# Building appearance

The shared architecture now uses photographic surface detail instead of the former 128-pixel random grain and identical brick joints on every material. This applies to all buildable IDs (including both water pumps), the founding core, sixty residential designs and their foundations/galleries, adaptive crowns/campuses, support piles and service roads. Building IDs, occupied bounds, save data and gameplay dimensions are unchanged. Placement previews still use their validity colours.

## Assets and rendering

- `Assets/Resources/Architecture/BuildingSurfaceAtlas.png`: generated 1254 × 1254 RGB atlas. The requested size was 2048; the built-in generator returned 1254. Top left limestone; top right iron; bottom left roof; bottom right brass. It is generated photographic artwork, not a real scanned material set.
- `Assets/Resources/Architecture/BuildingSurface.mat`: explicit Resources reference to the atlas and shader, retaining both in player builds. One shared texture, no runtime per-material grain texture generation. Texture import enables mipmaps, anisotropic filtering and compression; CPU readability is off.
- `Assets/Shaders/ArchitectureSurface_BuiltIn.shader`: Unity 2022.3 built-in Standard lighting, forward shadows, instancing and building-local metric triplanar projection. Mirrored repeats avoid hard tile boundaries. Gradient-limited atlas sampling and insets keep distant filtering within a material quadrant. Metallic/smoothness vary with surface luminance; shallow relief is derived from luminance, not an independently scanned normal/height map. Relief fades with distance. The seven material slots are preserved.
- `ArchitectureGeometry.BevelBox`: bounded chamfers on larger solid volumes, leaving fine trim unchanged. Round tanks/stacks use at least 24 sides and analytical side normals. Residential derived mesh cache keys were advanced so an Editor reload cannot reuse pre-chamfer meshes.

Photographic surfaces and restrained highlights improve realism, but silhouettes remain modular meshes. This pass does not establish photorealism, GPU cost or shader compatibility through actual rendering until the Unity checks below run. Existing gamma colour space and planetary lighting remain as configured.

## Review

After activating Unity 2022.3.2f1:

1. Run **WH30K > Run Building Appearance Checks**. It checks the real imported shader/texture, every building's material routing, matching preview geometry, all residential palettes and chamfer bounds/winding.
2. Run **WH30K > Capture Building Material Review**. It renders every catalog building at its actual dimensions into isolated Editor preview scenes, at 1280×800 and 1920×1080. Images and catalog order go under `.utmp/architecture-review/`; this command does not save or change the player colony.
3. Open `Assets/Scenes/Prototype/WH30K_VS01.unity`, enter Play Mode and run the full Gameplay and Residential checks. The gameplay suite includes the appearance import/material/geometry assertions.
4. Complete the visual acceptance in `VERIFICATION.md`: close-ups of every building and mixed residential growth, warm/cold light, day/night, curved tanks, wall/roof edges, steep/water supports, shadow contacts, valid/invalid previews, suspension, F5/F9, camera movement and both target resolutions. Check for magenta surfaces, shimmering, atlas colour leakage and exaggerated relief, and compare texture scale between the core and small additions.
5. Profile dense colonies and build/launch a standalone player. Resources retention and instancing settings do not replace a player shader/render check. Compare new draw/triangle counts and CPU/GPU cost with the previous performance fixture.

Batch entry point (graphics required): `ArchitectureAppearanceChecks.RunBatch`. It runs appearance checks, captures the review sheets, then runs Gameplay and Residential checks in the playable scene. It does not replace interactive visual acceptance.

## Generation provenance

Created with the built-in `image_gen` tool on 2026-10-26. The selected PNG was copied unchanged into the project. No external texture licence or network download is required. Original prompt:

> Use case: photorealistic-natural. Asset type: production game material albedo atlas, one square 2048x2048 PNG with four equal square quadrants touching exactly at x=50%, y=50%, no margins, no grid outlines. Primary request: photographic physically plausible gothic industrial building surface textures. Flat orthographic surface scans, even diffuse neutral illumination, no baked directional light, no perspective, no cast shadows. TOP LEFT quadrant: aged pale warm grey limestone ashlar wall, fine pores and subtle vertical rain stains, staggered small rectangular stones with narrow shallow mortar joints, about 8 stones across and 12 courses high. TOP RIGHT quadrant: dark charcoal forged steel panels with subtle rolled grain, restrained small rust freckles, thin panel seams and small flush rivets, about 4 panels across. BOTTOM LEFT quadrant: weathered dark oxide red standing seam metal roof, fine worn paint and restrained corrosion, about 12 vertical seams, no objects. BOTTOM RIGHT quadrant: muted antique brass sheet, fine scratches, dark brown tarnish and sparse subtle green verdigris, panel joins, not shiny yellow or golden. Each quadrant independently edge tileable, evenly distributed detail, natural muted palette, high-frequency tactile surface detail. Fill each quadrant edge to edge. No text, labels, borders, bevelled swatches, objects, scenery, logos or watermark. This is a flat texture atlas, not a rendered building.
