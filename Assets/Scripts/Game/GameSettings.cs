using System;
using System.Collections.Generic;

namespace WH30K.Game
{
    /// <summary>
    /// Centralised static repository for global configuration values such as selected difficulty
    /// and seed. This keeps deterministic configuration accessible without relying on scene state.
    /// </summary>
    public static class GameSettings
    {
        public enum Difficulty
        {
            Easy,
            Standard,
            Hard,
            Grim
        }

        [Serializable]
        public struct DifficultyDefinition
        {
            public Difficulty difficulty;
            public string displayName;
            public float environmentHarshnessMultiplier;
            public float resourceYieldMultiplier;
            public float eventFrequencyMultiplier;
        }

        private static readonly Dictionary<Difficulty, DifficultyDefinition> Definitions =
            new Dictionary<Difficulty, DifficultyDefinition>
            {
                {
                    Difficulty.Easy,
                    new DifficultyDefinition
                    {
                        difficulty = Difficulty.Easy,
                        displayName = "Easy",
                        environmentHarshnessMultiplier = 0.75f,
                        resourceYieldMultiplier = 1.25f,
                        eventFrequencyMultiplier = 0.75f
                    }
                },
                {
                    Difficulty.Standard,
                    new DifficultyDefinition
                    {
                        difficulty = Difficulty.Standard,
                        displayName = "Standard",
                        environmentHarshnessMultiplier = 1f,
                        resourceYieldMultiplier = 1f,
                        eventFrequencyMultiplier = 1f
                    }
                },
                {
                    Difficulty.Hard,
                    new DifficultyDefinition
                    {
                        difficulty = Difficulty.Hard,
                        displayName = "Hard",
                        environmentHarshnessMultiplier = 1.2f,
                        resourceYieldMultiplier = 0.85f,
                        eventFrequencyMultiplier = 1.1f
                    }
                },
                {
                    Difficulty.Grim,
                    new DifficultyDefinition
                    {
                        difficulty = Difficulty.Grim,
                        displayName = "Grim",
                        environmentHarshnessMultiplier = 1.4f,
                        resourceYieldMultiplier = 0.7f,
                        eventFrequencyMultiplier = 1.35f
                    }
                }
            };

        private static readonly Difficulty[] DifficultyOrder =
        {
            Difficulty.Easy,
            Difficulty.Standard,
            Difficulty.Hard,
            Difficulty.Grim
        };

        public static Difficulty CurrentDifficulty { get; private set; } = Difficulty.Standard;
        public static int CurrentSeed { get; private set; }
        public static bool HasActiveGame { get; private set; }

        public static DifficultyDefinition GetDefinition(Difficulty difficulty) => Definitions[difficulty];

        public static DifficultyDefinition GetCurrentDefinition() => Definitions[CurrentDifficulty];

        public static IReadOnlyList<DifficultyDefinition> GetAllDefinitions()
        {
            var ordered = new List<DifficultyDefinition>(DifficultyOrder.Length);
            foreach (var difficulty in DifficultyOrder)
            {
                ordered.Add(Definitions[difficulty]);
            }

            return ordered;
        }

        public static void StartNewGame(int seed, Difficulty difficulty)
        {
            CurrentSeed = seed;
            CurrentDifficulty = difficulty;
            HasActiveGame = true;
        }

        public static void ClearActiveGame()
        {
            HasActiveGame = false;
        }

        public static void ApplyLoadedState(int seed, Difficulty difficulty)
        {
            CurrentSeed = seed;
            CurrentDifficulty = difficulty;
            HasActiveGame = true;
        }
    }
}
