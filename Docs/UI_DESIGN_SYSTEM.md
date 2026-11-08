# Colony command UI

The colony uses dark iron panels, aged brass borders, parchment text and oxblood command accents. Teal identifies an informational signal, amber identifies a warning and coral identifies critical pressure. Write the state in words as well as showing color. Keep the established cinematic scene art as the backdrop when integrating the title menu.

Use `CommandUi` in Unity's existing uGUI stack. A UI screen owns its layout and callbacks. The shared factory owns the palette, visual states, and control construction. Keep these boundaries when adding controls.

| Need | Template |
| --- | --- |
| World-blocking menu or decision | `Screen` for the full-screen backdrop and `Panel` for the card |
| HUD pane or inspector | `Panel` plus a title, labels and optional `Accent` |
| Tooltip or advisory layer | `Panel(..., receivesPointer: false)` with noninteractive labels |
| Action | `Button` with a verb and a disabled state when unavailable |
| Active mode or primary action | `Tone` for a persistent selected state and `MarkPrimary` for one main action |
| Field | An existing Unity input, dropdown or slider. Use `StyleControl` for selectable backgrounds and handles. |
| Advisory vox | `CreatePortraitCard` with the speaker, portrait, priority and a short message |

Example from a runtime menu that already has a Unity font:

```csharp
var card = CommandUi.Panel("SurveySummary", parent, new Vector2(380, 148),
    new Vector2(16, -114), TextAnchor.UpperLeft);
CommandUi.Accent(card, CommandUi.Signal);
CommandUi.Label("Title", card.transform, font, "SURVEY SUMMARY", 13, new Vector2(16, -14),
    348, 26, CommandUi.Brass);
var action = CommandUi.Button("OpenSurvey", card.transform, font, new Vector2(16, -94),
    "OPEN SURVEY", out var label, 348, 36);
action.onClick.AddListener(OpenSurvey);
```

Use 16 px outer spacing, 8 px between related controls, and 34–44 px for normal actions on the 1280×800 reference canvas. Compact HUD controls may retain smaller dimensions where needed. Use 20–28 px for screen titles, 13–16 px for headings and body, and 10–12 px for compact readouts. Keep the text away from panel edges. Prefer responsive anchors for screen edges and separate tooltip placement from the object being inspected.

Add reusable style colors in `CommandUi`. The CI guard checks menu and urban panel sources for hardcoded color constructors and ad hoc button and text construction. Review responsive layout, control behavior and remaining component exceptions in the PR. The documented 3D landing preview material is a world visual and has its own color. Preserve object names used by scene checks, existing button callbacks, save schema, and input focus. Advisory visuals must leave camera and world pointer events available.

Review the title, landing, HUD, inspector, dialogs, and save/load at 1280×800, 1920×1080, and 2560×890. Check warnings and focus, modal click blocking, pointer placement behind informational surfaces, long text, and game speed. This package includes the cinematic menu and representative non-Urban panels; Urban source integration is deferred to issue #29, and exhaustive long-copy checks are deferred to issue #30.


This scoped migration preserves existing navigation and field behavior and includes the cinematic menu, archive flow, shared Vox card, and reviewed non-Urban panels. The Game Director approved deferring Urban integration and exhaustive long-copy/populated-state checks across all panels: track Urban in issue #29 and broad copy acceptance in issue #30. The Director's physical-input and save/load playtest remains part of approval for this package.
