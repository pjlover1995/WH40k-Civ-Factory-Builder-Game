using System;
using System.Collections.Generic;
using UnityEngine;

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
            Easy = 0,
            Standard = 1,
            Hard = 2,
            Grim = 3, // Historical save value; new expeditions use the five landing categories below.
            Moderate = 4,
            ExtremeHard = 5
        }

        [Serializable]
        public struct LandingSite
        {
            public Difficulty category;
            public int variant;
            public Vector3 direction;
            public float localRelief;
            public float reserveMultiplier;
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
                },
                {
                    Difficulty.Moderate,
                    new DifficultyDefinition
                    {
                        difficulty = Difficulty.Moderate,
                        displayName = "Moderate",
                        environmentHarshnessMultiplier = 1.1f,
                        resourceYieldMultiplier = 0.9f,
                        eventFrequencyMultiplier = 1.05f
                    }
                },
                {
                    Difficulty.ExtremeHard,
                    new DifficultyDefinition
                    {
                        difficulty = Difficulty.ExtremeHard,
                        displayName = "Extreme Hard",
                        environmentHarshnessMultiplier = 1.5f,
                        resourceYieldMultiplier = 0.6f,
                        eventFrequencyMultiplier = 1.45f
                    }
                }
            };

        private static readonly Difficulty[] DifficultyOrder =
        {
            Difficulty.Easy,
            Difficulty.Standard,
            Difficulty.Moderate,
            Difficulty.Hard,
            Difficulty.ExtremeHard
        };

        private static readonly float[] LandingReserveMultipliers = { 1.7f, 1.35f, 1f, .72f, .48f };

        public static Difficulty CurrentDifficulty { get; private set; } = Difficulty.Standard;
        public static int CurrentSeed { get; private set; }
        public static bool HasActiveGame { get; private set; }

        public static DifficultyDefinition GetDefinition(Difficulty difficulty) => Definitions[difficulty];

        public static float GetLandingReserveMultiplier(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Easy: return LandingReserveMultipliers[0];
                case Difficulty.Standard: return LandingReserveMultipliers[1];
                case Difficulty.Moderate: return LandingReserveMultipliers[2];
                case Difficulty.Hard: return LandingReserveMultipliers[3];
                case Difficulty.ExtremeHard: return LandingReserveMultipliers[4];
                default: return LandingReserveMultipliers[1];
            }
        }

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
