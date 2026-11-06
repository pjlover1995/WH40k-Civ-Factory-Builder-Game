using System.Collections.Generic;

namespace WH30K.Sim
{
    /// <summary>Player-facing founding route and reference text for every player facility.</summary>
    public sealed class ColonyGuideEntry
    {
        public readonly string Id;
        public readonly string Name;
        public readonly int CatalogGroup;
        public readonly bool Essential;
        public readonly string HowItWorks;
        public readonly string ColonyRole;

        public ColonyGuideEntry(string id, string name, int catalogGroup, bool essential, string howItWorks, string colonyRole)
        {
            Id = id; Name = name; CatalogGroup = catalogGroup; Essential = essential;
            HowItWorks = howItWorks; ColonyRole = colonyRole;
        }
    }

    public static class ColonyGuideContent
    {
        private static readonly ColonyGuideEntry[] entries =
        {
            new ColonyGuideEntry("GroundwaterPump", "Groundwater Pump", 2, true,
                "Place on dry land. A staffed, powered pump produces up to 180 water per month. Starting crew demand is 97.5/month; one connected pump can cover it.",
                "Water is consumed continuously by the population. Establish pumping before the ship's finite reserve is exhausted."),
            new ColonyGuideEntry("Farm", "Colony Farm", 4, true,
                "A staffed, powered farm turns fertilizer and manufactured components into food. At full operation its founding plots make 6,000 food per calendar month; shortages reduce production.",
                "Food is consumed by the population. Ship stores buy time, while a working farm provides the continuing supply. Six full operating months add a field."),
            new ColonyGuideEntry("HabBlock", "Residential Block", 0, false,
                "Housing modules grow over time when Automatic development is enabled. Modules add housing and jobs; focus and layout preferences change their trade-offs.",
                "The ship has 10,000 berths. Build housing when the population needs room beyond the ship or when you want dedicated residential growth."),
            new ColonyGuideEntry("Mine", "Deep Core Mine", 1, true,
                "Place at a discovered mineral deposit. The mine automatically selects its iron, copper or crystal at placement and sends material through the automatic cargo network.",
                "Mined ores feed metal production and other recipes. Each pit occupies and protects a sizeable working area as it expands."),
            new ColonyGuideEntry("Quarry", "Stone Quarry", 1, true,
                "Place at a stone deposit. Workers extract finite stone and dispatch it to connected consumers or storage.",
                "Stone is used by industrial recipes and advanced nutrient production. Survey and inspect a deposit before committing the site."),
            new ColonyGuideEntry("Harvester", "Biomass Harvester", 1, true,
                "Place in a biomass stand. It cuts and hauls biomass; forest maturity improves yield, and the stand has finite harvestable material.",
                "Biomass supplies industrial processing and intermediate compost blend for farms."),
            new ColonyGuideEntry("PromethiumExtractor", "Promethium Extractor", 1, true,
                "Place at a promethium seep. It extracts finite fuel and routes it to a generator, refinery or depot.",
                "The ship reactor powers the initial settlement. Fuel extraction is the first link in a longer-term power supply."),
            new ColonyGuideEntry("PromethiumGenerator", "Promethium Generator", 2, true,
                "A staffed generator burns delivered promethium to supply the connected colony grid. Output depends on fuel, staffing and operating state.",
                "The ship supplies up to 18 MW while active. Add generation before the grid outgrows that supply or if you suspend the ship reactor."),
            new ColonyGuideEntry("StorageDepot", "Storage Depot", 2, false,
                "A depot stores designated amounts of selected materials and extends the automatic proximity cargo network. Configure its reservations in the facility panel.",
                "Use depots to bridge distant extractors and processors or keep critical recipe inputs from being exported elsewhere."),
            new ColonyGuideEntry("Smelter", "Ore Smelter", 3, false,
                "The smelter consumes delivered ore and biomass and returns processed metal to its local inventory and cargo links.",
                "Mining alone does not provide finished construction materials. Smelting is the conversion link between raw ores and fabrication."),
            new ColonyGuideEntry("Works", "Fabrication Works", 3, false,
                "The Works consumes delivered metal, stone and crystal and manufactures components for the colony supply chain.",
                "Optional longer route: smelt ore with biomass, then feed metal + stone + crystal to Works. The Manufactorum produces components directly from raw inputs."),
            new ColonyGuideEntry("Refinery", "Promethium Refinery", 3, false,
                "The refinery processes delivered promethium into plasteel. It needs a connected flow of fuel and recipe inputs.",
                "Add refining when your fuel chain needs processed products for industrial recipes; it is not required for the ship reactor's initial power."),
            new ColonyGuideEntry("Manufactorum", "Manufactorum", 3, true,
                "Default recipe: 1 iron + 1 stone + 0.5 biomass -> 1 construction supply. Connect a mine, quarry and harvester. The facility panel selects additional products.",
                "First renewable construction route. Keep the default components line enabled until supplies recover; fertilizer and plasteel lines are later options."),
            new ColonyGuideEntry("Factory", "Macro Factory", 0, false,
                "A staffed factory reclaims basic fertilizer from its workers. Clean focus reduces emissions. It does not manufacture construction supplies.",
                "Optional support industry. Use a Manufactorum for the short construction chain: iron + stone + biomass -> components. Components fund buildings and improvements."),
            new ColonyGuideEntry("Outpost", "Frontier Survey Outpost", 0, false,
                "An active outpost surveys a 1,800 m surface radius. It reveals richer deposits and discoveries; revealed sites can be inspected and investigated.",
                "Build outposts to find resources beyond the orbital landing scan and unlock deterministic discovery decisions and rewards."),
            new ColonyGuideEntry("Cognitorium", "Mechanicum Cognitorium", 5, false,
                "A connected, staffed and powered Cognitorium generates 0.08 Insight per simulation second. More than one can increase the rate; Insight pays for one active project at a time.",
                "Choose technologies in RESEARCH. Field discoveries also award one-time Insight. Projects have branch prerequisites, their progress is saved, and completed effects improve existing colony systems."),
            new ColonyGuideEntry("Utility", "Air Scrubber", 0, false,
                "A staffed, powered scrubber reduces atmospheric and water pollution. Its focus chooses deeper purification or a more economical operating cycle.",
                "Environmental stress lowers habitability, population targets and production. Use scrubbers when pollution approaches harmful thresholds."),
            new ColonyGuideEntry("DesalinationPump", "Desalination Pump", 2, false,
                "Place on dry land within 140 m of an ocean shoreline. A staffed, powered pump produces up to 0.9 water per simulation second.",
                "This is an alternative water source when groundwater is unsuitable or unavailable; it still needs workers and grid power.")
        };

        public static IReadOnlyList<ColonyGuideEntry> Entries => entries;
        public static int PageCount => entries.Length + 2; // ship overview and completion page
        public static ColonyGuideEntry EntryAtPage(int page) => page > 0 && page <= entries.Length ? entries[page - 1] : null;
        public static string CategoryName(int group) => new[] { "COLONY", "EXTRACTION", "UTILITIES", "PROCESSING", "FOOD", "RESEARCH" }[group];
    }
}
