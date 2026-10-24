# Project working instructions

- Target Unity 2022.3.2f1 and the built-in rendering pipeline. The playable scene is `Assets/Scenes/Prototype/WH30K_VS01.unity`.
- Read `Docs/PROJECT_BRIEF.md` before extending the game. Complete a player action through UI, simulation, persistence, and scene wiring together.
- Keep ScriptableObject building IDs stable because saves refer to them. Save schema changes must preserve existing saves or provide an explicit migration.
- Preserve Unity `.meta` GUIDs and add metadata for new assets/scripts. Include required scenes in Build Settings.
- Run `dotnet run --project Tests/Simulation/SimulationChecks.csproj -- .` and `git diff --check` for gameplay changes. Extend checks for meaningful new behavior.
- Run the Unity scene checks and visual acceptance checks in `Docs/VERIFICATION.md` when Unity is available. Clearly distinguish syntax checks, engine-independent tests, engine compilation, and actual play testing; do not claim checks passed unless they were run.
- Update the brief and verification notes when behavior or scope changes. Prioritize a reliable playable loop before adding unrelated systems.
