using System;
using System.Collections.Generic;
using System.Linq;
using WH30K.Sim.Environment;

namespace WH30K.Sim
{
    public enum ResearchBranch { Survival, Industry, Exploration }

    public sealed class ResearchProject
    {
        public readonly string Id, Name, Effect, Description, Prerequisite;
        public readonly ResearchBranch Branch;
        public readonly float Cost;

        public ResearchProject(string id, string name, ResearchBranch branch, float cost, string prerequisite, string effect, string description)
        { Id=id;Name=name;Branch=branch;Cost=cost;Prerequisite=prerequisite;Effect=effect;Description=description; }
    }

    [Serializable]
    public sealed class ResearchSnapshot
    {
        public float insight = 10f;
        public string activeProjectId = string.Empty;
        public string[] completedIds = Array.Empty<string>();
        public float[] progress = new float[ResearchRules.ProjectCount];
    }

    /// <summary>Deterministic research balance, project dependencies and additive save state.</summary>
    public static class ResearchRules
    {
        public const float BaseInsightPerSecond = .08f;
        public const float ShipInsightPerSecond = .01f;
        public const float MaximumInsight = 500f;
        private static readonly ResearchProject[] projects =
        {
            new ResearchProject("aquifer-lenses", "Aquifer Lenses", ResearchBranch.Survival, 40, "", "+20% groundwater pump output", "Auspex resonance maps shallow aquifers and seals losses in the pump draw assembly."),
            new ResearchProject("nutrient-symbiosis", "Nutrient Symbiosis", ResearchBranch.Survival, 60, "aquifer-lenses", "+15% farm food from supplied inputs", "Reclaimed nutrients and resilient crop strains improve the food yield of each supplied farm cycle."),
            new ResearchProject("metallurgic-calibration", "Metallurgic Calibration", ResearchBranch.Industry, 40, "", "+20% processing work rate", "Standardized furnace rites accelerate Smelter, Works, Refinery and Manufactorum production."),
            new ResearchProject("reactor-catechism", "Reactor Catechism", ResearchBranch.Industry, 60, "metallurgic-calibration", "+15% promethium generator output", "Revised plasma-conversion litanies raise generator output while fuel use scales with power produced."),
            new ResearchProject("wide-auspex", "Wide-Angle Auspex", ResearchBranch.Exploration, 40, "", "+25% survey radius", "Improved auspex arrays chart a wider surface around every active Frontier Survey Outpost."),
            new ResearchProject("cautious-protocols", "Cautious Recovery Protocols", ResearchBranch.Exploration, 60, "wide-auspex", "+15% discovery supply rewards", "Better containment and recovery plans raise rewards from both cautious and rapid expeditions without changing incident risk."),
        };

        public const int ProjectCount = 6;
        public static IReadOnlyList<ResearchProject> Projects => projects;
        public static ResearchProject Project(string id) => projects.FirstOrDefault(p => p.Id == id);
        public static ResearchProject ProjectAt(int index) => index >= 0 && index < projects.Length ? projects[index] : null;
        public static int IndexOf(string id) => Array.FindIndex(projects, p => p.Id == id);
        public static int BranchIndex(ResearchBranch branch) => Array.FindIndex(projects, p => p.Branch == branch);
        public static int BranchCount(ResearchBranch branch) => projects.Count(p => p.Branch == branch);
        public static string BranchName(ResearchBranch branch) => branch.ToString().ToUpperInvariant();
        public static float InsightRate(bool vesselActive, float cognitoriumOperatingFraction)
            => (vesselActive ? ShipInsightPerSecond : 0f)
                + (ColonyRules.IsFinite(cognitoriumOperatingFraction) ? Math.Max(0f, cognitoriumOperatingFraction) : 0f) * BaseInsightPerSecond;
        public static ResearchSnapshot Create() => new ResearchSnapshot();
        public static ResearchSnapshot Clone(ResearchSnapshot source)
        {
            if (source == null) return Create();
            return new ResearchSnapshot { insight=source.insight,activeProjectId=source.activeProjectId,
                completedIds=source.completedIds!=null?(string[])source.completedIds.Clone():Array.Empty<string>(),
                progress=source.progress!=null?(float[])source.progress.Clone():new float[ProjectCount] };
        }
        public static bool IsCompleted(ResearchSnapshot state, string id)
            => state != null && state.completedIds != null && Array.IndexOf(state.completedIds,id)>=0;
        public static bool IsUnlocked(ResearchSnapshot state, ResearchProject project)
            => project != null && (string.IsNullOrEmpty(project.Prerequisite) || IsCompleted(state,project.Prerequisite));
        public static bool Select(ResearchSnapshot state,string id)
        {
            if (state == null) return false;
            if (string.IsNullOrEmpty(id)) { state.activeProjectId=string.Empty; return true; }
            var project=Project(id);
            if(project==null||IsCompleted(state,id)||!IsUnlocked(state,project))return false;
            state.activeProjectId=id;return true;
        }
        public static bool AddInsight(ResearchSnapshot state,float amount)
        {
            if(state==null||!ColonyRules.IsFinite(amount)||amount<0)return false;
            state.insight=Math.Min(MaximumInsight,state.insight+amount);return true;
        }
        public static string Advance(ResearchSnapshot state,float seconds,float insightPerSecond)
        {
            if(state==null||!ColonyRules.IsFinite(seconds)||seconds<=0||!ColonyRules.IsFinite(insightPerSecond)||insightPerSecond<0)return string.Empty;
            AddInsight(state,seconds*insightPerSecond);
            var project=Project(state.activeProjectId);if(project==null)return string.Empty;
            var index=IndexOf(project.Id);
            var spent=Math.Min(state.insight,Math.Max(0,project.Cost-state.progress[index]));
            state.insight-=spent;state.progress[index]+=spent;
            if(state.progress[index]+.0001f<project.Cost)return string.Empty;
            state.progress[index]=project.Cost;
            var completed=new List<string>(state.completedIds??Array.Empty<string>());completed.Add(project.Id);state.completedIds=completed.ToArray();
            state.activeProjectId=string.Empty;return project.Id;
        }
        public static float Progress(ResearchSnapshot state,string id)
        { var index=IndexOf(id);return index>=0&&state?.progress!=null&&index<state.progress.Length?state.progress[index]:0; }
        public static float PumpYieldMultiplier(ResearchSnapshot state,string id)
            =>id=="GroundwaterPump"&&IsCompleted(state,"aquifer-lenses")?1.2f:1f;
        public static float FarmFoodMultiplier(ResearchSnapshot state)
            =>IsCompleted(state,"nutrient-symbiosis")?1.15f:1f;
        public static float ProcessorWorkMultiplier(ResearchSnapshot state,string id)
            =>IsCompleted(state,"metallurgic-calibration")&&(id=="Smelter"||id=="Works"||id=="Refinery"||id=="Manufactorum")?1.2f:1f;
        public static float GeneratorOutputMultiplier(ResearchSnapshot state)
            =>IsCompleted(state,"reactor-catechism")?1.15f:1f;
        public static float SurveyRadiusMultiplier(ResearchSnapshot state)
            =>IsCompleted(state,"wide-auspex")?1.25f:1f;
        public static DiscoveryRewardProfile DiscoveryRewards(ResearchSnapshot state,DiscoveryRewardProfile profile)
        {
            if(IsCompleted(state,"cautious-protocols")){profile.cautiousMultiplier*=1.15f;profile.rapidMultiplier*=1.15f;}
            return profile;
        }
        public static bool Valid(ResearchSnapshot state)
        {
            if(state==null||!ColonyRules.IsFinite(state.insight)||state.insight<0||state.insight>MaximumInsight
                ||state.progress==null||state.progress.Length!=ProjectCount||state.completedIds==null||state.completedIds.Length>ProjectCount)return false;
            var completed=new HashSet<string>();
            foreach(var id in state.completedIds)
            {
                var project=Project(id);if(project==null||!completed.Add(id)||!string.IsNullOrEmpty(project.Prerequisite)&&!completed.Contains(project.Prerequisite))return false;
            }
            for(var i=0;i<projects.Length;i++)
            {
                var value=state.progress[i];if(!ColonyRules.IsFinite(value)||value<0||value>projects[i].Cost+.0001f)return false;
                if(completed.Contains(projects[i].Id)&&value+.0001f<projects[i].Cost)return false;
                if(value>.0001f&&!string.IsNullOrEmpty(projects[i].Prerequisite)&&!completed.Contains(projects[i].Prerequisite))return false;
            }
            if(string.IsNullOrEmpty(state.activeProjectId))return true;
            var active=Project(state.activeProjectId);return active!=null&&!completed.Contains(active.Id)&&IsUnlocked(state,active)
                &&Progress(state,active.Id)<active.Cost;
        }
    }
}
