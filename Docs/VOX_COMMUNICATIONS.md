# Vox communications

Two officers report on the live colony after landing. The Captain covers housing headroom and crew food and water reserves. The Mechanicum Priest covers active Manufactorum lines missing recipe inputs, low component reserves at the recent rate of use, completed research, and newly charted deposits or sites.

A portrait and speaker name appear below the command header. Advisories neither pause the clock nor consume world pointer input. Urgent crew warnings are labeled and colored. Existing decisions take precedence. The **Vox Log** beside **Research** shows the last five transmissions for the current session and closes with Escape or Close.

Pressure is sampled every five simulation seconds. Warnings begin after 90 seconds of grace on a new or loaded colony and 45 seconds of observed sustained pressure. A condition rearms after 90 seconds of recovery. At most two messages are sent per sustained episode and repeats are 900 simulation seconds apart. Research and survey events can reach the transcript while paused. The queue and transcript are bounded. They reset on loading or starting an expedition. Save data, simulation rules and existing event decisions are preserved.

## Verification

Run the .NET simulation suite and the UI palette check. In Play Mode, land, dismiss the guide, and observe sustained pressure at 1× and 10×. Verify advisory visibility behind a modal and the separate Research and Vox Log targets. Complete research and survey a new site. Save and load to verify old and new saves have a clean session transcript. Confirm portraits and message legibility at 1280×800 and 1920×1080.

## Portrait provenance

Both square source portraits were generated with built-in ImageGen and imported as 256 px UI textures with stable Unity metadata.

### Captain

Asset: `Assets/Resources/Vox/Captain.png`

> Use case: stylized-concept. Game asset: square original portrait for a tiny vox communications panel in a gothic industrial space colony game. Centered head and shoulders of the veteran captain of a crashed interstellar colony ship, middle-aged, composed, tired but protective, dark charcoal naval coat with high collar and small worn brass clasps, one subtle oxblood accent, short graying hair. Face is well lit, visually readable at 72px, direct three-quarter view. Restrained cinematic realism, dark iron bulkhead behind him, warm parchment key light, subtle cool rim light. Square crop, fill frame with face and shoulders, leave a little headroom. No text, logos, official insignia, border, frame or watermark.

### Mechanicum Priest

Asset: `Assets/Resources/Vox/MechanicumPriest.png`

> Use case: stylized-concept. Game asset: square original portrait for a tiny vox communications panel in a gothic industrial space colony game. Centered head and shoulders of an austere Mechanicum priest in weathered dark oxblood hood and aged brass collar, one clearly visible human eye and one small teal bionic optic, functional cybernetic jaw and subtle cables. Measured, intent expression. Face well lit and readable at 72px, three-quarter view. Restrained cinematic realism with a dark iron engine-room backdrop, warm parchment key light and cool teal rim light. Square crop, fill frame with face and shoulders, leave a little headroom. No text, logos, official insignia, border, frame or watermark.
