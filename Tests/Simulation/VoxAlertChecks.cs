using System;
using WH30K.Sim;

internal static class VoxAlertChecks
{
    public static void Run(Action<bool,string> check)
    {
        var policy = new VoxAlertPolicy();
        var pressure = new VoxAlertSnapshot { HousingPressure = true, FoodShortage = true,
            WaterShortage = true, ManufacturingBottleneck = true, ManufacturingName = "Components" };
        check(policy.Sample(0, pressure).Count == 0 && policy.Sample(89, pressure).Count == 0
            && policy.Sample(90, pressure).Count == 0,
            "new colony session receives a simulation-time grace period");
        check(policy.Sample(134, pressure).Count == 0, "conditions must remain sustained before notifying");
        var first = policy.Sample(135, pressure);
        check(first.Count == 4 && first.Exists(a => a.Officer == VoxOfficer.Captain && a.Category == VoxAlertCategory.Food && a.Urgent)
            && first.Exists(a => a.Officer == VoxOfficer.Captain && a.Category == VoxAlertCategory.Housing)
            && first.Exists(a => a.Officer == VoxOfficer.MechanicumPriest && a.Category == VoxAlertCategory.Manufacturing),
            "captain owns living conditions and priest owns manufacturing alerts");
        check(policy.Sample(1035, pressure).Count == 4 && policy.Sample(1935, pressure).Count == 0,
            "a long-lived condition may repeat once and then stops at its bounded repeat count");

        policy = new VoxAlertPolicy();
        policy.Sample(0, new VoxAlertSnapshot());
        policy.Sample(90, new VoxAlertSnapshot());
        policy.Sample(134, new VoxAlertSnapshot { WaterShortage = true });
        check(policy.Sample(135, new VoxAlertSnapshot()).Count == 0,
            "brief oscillation clears the pending sustained interval");
        check(policy.Sample(180, new VoxAlertSnapshot()).Count == 0
            && policy.Sample(181, new VoxAlertSnapshot { WaterShortage = true }).Count == 0
            && policy.Sample(225, new VoxAlertSnapshot { WaterShortage = true }).Count == 0
            && policy.Sample(226, new VoxAlertSnapshot { WaterShortage = true }).Count == 1,
            "shortage must persist continuously for the threshold interval");

        policy = new VoxAlertPolicy();
        policy.PushResearchCompleted("Surveying");
        policy.PushSurveyFinding("Iron seam");
        var pushed = policy.Sample(0, new VoxAlertSnapshot());
        check(pushed.Count == 2 && pushed[0].Officer == VoxOfficer.MechanicumPriest
            && pushed[0].Category == VoxAlertCategory.Research && pushed[1].Category == VoxAlertCategory.Survey,
            "explicit research and survey events are priest-attributed and pass through fresh-session grace");
        check(policy.Sample(0, new VoxAlertSnapshot()).Count == 0, "pushed events are delivered once");

        policy = new VoxAlertPolicy();
        policy.PushResearchCompleted("Surveying");
        policy.PushResearchCompleted("Surveying");
        policy.PushSurveyFinding("Iron seam");
        policy.PushSurveyFinding("Iron seam");
        for (var i = 0; i < 12; i++) policy.PushResearchCompleted("Project " + i);
        var bounded = policy.Sample(0, new VoxAlertSnapshot());
        check(bounded.Count == VoxAlertPolicy.MaximumPendingEvents
            && bounded.FindAll(a => a.DeduplicationKey == "research:Surveying").Count == 1
            && bounded.FindAll(a => a.DeduplicationKey == "survey:Iron seam").Count == 1,
            "pending research and survey events coalesce duplicates and stay within the queue bound");

        policy = new VoxAlertPolicy();
        policy.Sample(0, new VoxAlertSnapshot());
        policy.Sample(90, new VoxAlertSnapshot { ManufacturingBottleneck = true, ManufacturingName = "Forge A",
            ConstructionSupplyNeed = true, ConstructionSupplyName = "Components" });
        policy.Sample(134, new VoxAlertSnapshot { ManufacturingBottleneck = true, ManufacturingName = "Forge A",
            ConstructionSupplyNeed = true, ConstructionSupplyName = "Components" });
        var renamed = policy.Sample(135, new VoxAlertSnapshot { ManufacturingBottleneck = true, ManufacturingName = "Forge B",
            ConstructionSupplyNeed = true, ConstructionSupplyName = "Advanced Components" });
        check(renamed.Count == 2 && renamed.Exists(a => a.Category == VoxAlertCategory.Manufacturing && a.Body.Contains("Forge B"))
            && renamed.Exists(a => a.Category == VoxAlertCategory.Construction && a.Body.Contains("Advanced Components")),
            "renamed manufacturing and construction subjects retain fixed category tracking and current copy");
        check(renamed.Find(a => a.Category == VoxAlertCategory.Manufacturing).Body.Contains("missing recipe inputs")
            && !renamed.Find(a => a.Category == VoxAlertCategory.Manufacturing).Body.Contains("staffing"),
            "manufacturing copy identifies only the observed missing-input condition");

        policy = new VoxAlertPolicy();
        policy.Sample(0, pressure);
        policy.Sample(90, pressure);
        check(policy.Sample(135, pressure).Count == 4 && policy.Sample(136, pressure).Count == 0, "sustained alerts fire once after session grace");
        policy.Sample(137, new VoxAlertSnapshot());
        policy.Sample(227, new VoxAlertSnapshot());
        check(policy.Sample(272, pressure).Count == 0 && policy.Sample(317, pressure).Count == 4,
            "recovery hysteresis rearms only after sustained clearance and renewed pressure");
        check(policy.Sample(20, pressure).Count == 0, "simulation clock reset starts a clean grace window");
    }
}
