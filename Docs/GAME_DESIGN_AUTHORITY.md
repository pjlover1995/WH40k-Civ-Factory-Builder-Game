# Game Design Authority

The Game Director owns creative direction. The Feature Architect and Design Reviewer use this file as the design index and resolve details in [the project brief](PROJECT_BRIEF.md), the subsystem documents and the matching code. If they conflict, record the mismatch before treating either as a new design decision. Keep planned and shipped behavior distinct.

- Preserve the playable loop of landing, building a colony and industry, responding to constraints, saving and resuming.
- Respect physical supplies, staffing, power, environmental effects and elapsed simulation time. Trace costs, production and cargo to their source and destination.
- Use the prototype scene, built-in rendering pipeline and Unity version in the repository instructions.
- Stable building IDs, scene and asset GUIDs, old save compatibility, and exact saved progress are contracts. Save changes require a migration or demonstrated backward compatibility.
- Assess placement, terrain, controls, persistence, economy, performance, and new versus existing colonies. Cite the affected rules and the evidence for each proposed exception.
- Record director-approved changes to core mechanics, balance direction, progression, save contracts and releases in the feature record and update the relevant design source with the feature.

Authority here is a maintained design record. On a disputed decision, the Design Reviewer summarizes options and the Game Director decides.
