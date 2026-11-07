using System;
using System.Collections.Generic;

namespace WH30K.Sim
{
    public enum VoxOfficer { Captain, MechanicumPriest }
    public enum VoxAlertCategory { Housing, Food, Water, Manufacturing, Construction, Research, Survey }

    public sealed class VoxAlert
    {
        public VoxOfficer Officer { get; }
        public VoxAlertCategory Category { get; }
        public string Title { get; }
        public string Body { get; }
        public string DeduplicationKey { get; }
        public bool Urgent { get; }

        public VoxAlert(VoxOfficer officer, VoxAlertCategory category, string title, string body, string deduplicationKey, bool urgent = false)
        { Officer = officer; Category = category; Title = title; Body = body; DeduplicationKey = deduplicationKey; Urgent = urgent; }
    }

    // Values are derived by the observer from live colony state; this policy owns no simulation state.
    public sealed class VoxAlertSnapshot
    {
        public bool HousingPressure { get; set; }
        public bool FoodShortage { get; set; }
        public bool WaterShortage { get; set; }
        public bool ManufacturingBottleneck { get; set; }
        public bool ConstructionSupplyNeed { get; set; }
        public string ManufacturingName { get; set; } = "production";
        public string ConstructionSupplyName { get; set; } = "construction supplies";
    }

    /// <summary>Simulation-time hysteresis and bounded alert repetition for officer messages.</summary>
    public sealed class VoxAlertPolicy
    {
        public const double FreshSessionGraceSeconds = 90;
        public const double SustainedSeconds = 45;
        public const double RecoverySeconds = 90;
        public const double RepeatIntervalSeconds = 900;
        public const int MaximumAlertsPerEpisode = 2;
        public const int MaximumPendingEvents = 8;
        private readonly Dictionary<string, Condition> conditions = new Dictionary<string, Condition>();
        private readonly Queue<VoxAlert> pushed = new Queue<VoxAlert>();
        private readonly HashSet<string> pendingEventKeys = new HashSet<string>();
        private double sessionStart;
        private double lastSample;

        private sealed class Condition
        {
            public double Since;
            public double LastSent;
            public double ClearSince;
            public bool Active;
            public bool WasPresent;
            public int Sent;
        }

        public VoxAlertPolicy() { Reset(); }
        public void Reset(double simulationSeconds = 0)
        {
            conditions.Clear(); pushed.Clear(); pendingEventKeys.Clear(); sessionStart = Finite(simulationSeconds) ? simulationSeconds : 0; lastSample = sessionStart;
        }

        public void PushResearchCompleted(string researchName)
        {
            var name = Safe(researchName, "Research project");
            EnqueueEvent(new VoxAlert(VoxOfficer.MechanicumPriest, VoxAlertCategory.Research,
                "Research completed", name + " is complete. Review the capability at the research station.", "research:" + name));
        }
        public void PushSurveyFinding(string findingName)
        {
            var name = Safe(findingName, "new finding");
            EnqueueEvent(new VoxAlert(VoxOfficer.MechanicumPriest, VoxAlertCategory.Survey,
                "Survey finding", "Auspex crews charted " + name + ". Review the survey report.", "survey:" + name));
        }

        private void EnqueueEvent(VoxAlert alert)
        {
            if (pendingEventKeys.Contains(alert.DeduplicationKey) || pushed.Count >= MaximumPendingEvents) return;
            pendingEventKeys.Add(alert.DeduplicationKey);
            pushed.Enqueue(alert);
        }

        public bool HasPendingEvents => pushed.Count > 0;

        public List<VoxAlert> DrainEvents()
        {
            var result = new List<VoxAlert>();
            while (pushed.Count > 0)
            {
                var alert = pushed.Dequeue();
                pendingEventKeys.Remove(alert.DeduplicationKey);
                result.Add(alert);
            }
            return result;
        }

        public List<VoxAlert> Sample(double simulationSeconds, VoxAlertSnapshot snapshot)
        {
            var result = DrainEvents();
            if (!Finite(simulationSeconds) || snapshot == null) return result;
            if (simulationSeconds < lastSample)
            {
                Reset(simulationSeconds); // clock reset/load begins a clean grace window
                return result;
            }
            lastSample = simulationSeconds;
            if (simulationSeconds - sessionStart < FreshSessionGraceSeconds) return result;
            Evaluate(result, "housing", snapshot.HousingPressure, simulationSeconds, Make(VoxAlertCategory.Housing,
                "Berths running short", "Housing headroom is tightening. Build residential capacity before growth stalls.", "housing"));
            Evaluate(result, "food", snapshot.FoodShortage, simulationSeconds, Make(VoxAlertCategory.Food,
                "Food endurance low", "Food endurance is low at current use. Restore farm output or arrange deliveries.", "food", true));
            Evaluate(result, "water", snapshot.WaterShortage, simulationSeconds, Make(VoxAlertCategory.Water,
                "Water endurance low", "Water endurance is low at current use. Restore pump output or connect another source.", "water", true));
            var manufacturing = Safe(snapshot.ManufacturingName, "production");
            Evaluate(result, "manufacturing", snapshot.ManufacturingBottleneck, simulationSeconds,
                Make(VoxAlertCategory.Manufacturing, "Production input missing", manufacturing + " is missing recipe inputs. Check linked supply and stock.", "manufacturing"));
            var supplies = Safe(snapshot.ConstructionSupplyName, "construction supplies");
            Evaluate(result, "construction", snapshot.ConstructionSupplyNeed, simulationSeconds,
                Make(VoxAlertCategory.Construction, "Construction supplies needed", supplies + " reserves are low at the recent rate of use. Restore production or arrange supply.", "construction"));
            return result;
        }

        private static VoxAlert Make(VoxAlertCategory category, string title, string body, string key, bool urgent = false) => new VoxAlert(
            category == VoxAlertCategory.Manufacturing || category == VoxAlertCategory.Construction
                ? VoxOfficer.MechanicumPriest : VoxOfficer.Captain, category, title, body, "condition:" + key, urgent);

        private void Evaluate(List<VoxAlert> output, string key, bool present, double now, VoxAlert alert)
        {
            if (!conditions.TryGetValue(key, out var state)) { state = new Condition { Since = now }; conditions.Add(key, state); }
            if (present)
            {
                if (!state.WasPresent) state.Since = now;
                state.WasPresent = true;
                state.ClearSince = 0;
                if (!state.Active && now - state.Since >= SustainedSeconds) { state.Active = true; state.Sent = 0; }
                if (state.Active && state.Sent < MaximumAlertsPerEpisode
                    && (state.Sent == 0 || now - state.LastSent >= RepeatIntervalSeconds))
                { output.Add(alert); state.LastSent = now; state.Sent++; }
            }
            else if (state.Active)
            {
                state.WasPresent = false;
                if (state.ClearSince == 0) state.ClearSince = now;
                if (now - state.ClearSince >= RecoverySeconds)
                { state.Active = false; state.Sent = 0; state.Since = now; state.ClearSince = 0; }
            }
            else { state.WasPresent = false; state.Since = now; }
        }

        private static string Safe(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
