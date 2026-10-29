using System;
using System.Collections.Generic;
using WH30K.Sim.Environment;

namespace WH30K.Sim
{
    public enum DiscoveryKind { AncientRuins, ImperialInstallation, HazardousRegion, ArchaeotechCache }

    [Serializable]
    public struct DiscoveryRecord
    {
        public int id, kind;
        public float x, y, z, reward;
        public bool discovered, resolved;
        // Additive to schema 8. Legacy resolved sites keep their reward lock without inventing an outcome.
        public bool outcomeRecorded, rapidChoice, outcomeHazard;
        public float rewardReceived;
        public EnvironmentImpact outcomeImpact;
    }

    [Serializable]
    public struct DiscoveryRewardProfile
    {
        public float cautiousMultiplier, rapidMultiplier, hazardChance;
        public EnvironmentImpact cautiousImpact, rapidImpact, hazardImpact;
    }

    public struct DiscoveryOutcome
    {
        public float reward;
        public bool hazard;
        public EnvironmentImpact impact;
    }

    public static class ExplorationRules
    {
        public const int LayoutVersion = 1;
        public const int MaximumDiscoveries = 64;
        public const int DefaultCount = 18;
        public const int SurveyCellCount = 1024;

        public static DiscoveryRewardProfile DefaultProfile(DiscoveryKind kind)
        {
            var profile=new DiscoveryRewardProfile
            {
                cautiousMultiplier=.7f,rapidMultiplier=1.8f,hazardChance=.35f,
                hazardImpact=new EnvironmentImpact{co2Delta=5,waterDelta=2,temperatureDelta=.02f}
            };
            switch(kind)
            {
                case DiscoveryKind.ImperialInstallation:
                    profile.cautiousMultiplier=1;profile.rapidMultiplier=2.2f;profile.hazardChance=.2f;break;
                case DiscoveryKind.HazardousRegion:
                    profile.cautiousMultiplier=0;profile.rapidMultiplier=.5f;profile.hazardChance=.45f;
                    profile.cautiousImpact=new EnvironmentImpact{co2Delta=-4,waterDelta=-3,temperatureDelta=-.02f};
                    profile.hazardImpact=new EnvironmentImpact{co2Delta=10,waterDelta=5,temperatureDelta=.04f};break;
                case DiscoveryKind.ArchaeotechCache:
                    profile.cautiousMultiplier=3;profile.rapidMultiplier=5;profile.hazardChance=.2f;break;
            }
            return profile;
        }

        public static void SurveyCellDirection(int index,out float x,out float y,out float z)
        {
            if(index<0||index>=SurveyCellCount)throw new ArgumentOutOfRangeException(nameof(index));
            y=1-2*(index+.5f)/SurveyCellCount;
            var planar=(float)Math.Sqrt(Math.Max(0,1-y*y));var angle=index*2.399963229728653;
            x=planar*(float)Math.Cos(angle);z=planar*(float)Math.Sin(angle);
        }

        // The layout is engine independent so saves and tests use the same seeded points.
        public static DiscoveryRecord[] Generate(int seed)
        {
            var random = new Random(unchecked(seed ^ 0x2A71D3));
            var result = new DiscoveryRecord[DefaultCount];
            for (var i = 0; i < result.Length; i++)
            {
                var y = (float)(random.NextDouble() * 2 - 1);
                var angle = (float)(random.NextDouble() * Math.PI * 2);
                var planar = (float)Math.Sqrt(Math.Max(0, 1 - y * y));
                var kind = i == 17 ? DiscoveryKind.ArchaeotechCache
                    : i % 5 == 0 ? DiscoveryKind.HazardousRegion
                    : i % 3 == 0 ? DiscoveryKind.ImperialInstallation : DiscoveryKind.AncientRuins;
                result[i] = new DiscoveryRecord
                {
                    id = i, kind = (int)kind, x = planar * (float)Math.Cos(angle), y = y,
                    z = planar * (float)Math.Sin(angle), reward = 40 + (float)random.NextDouble() * 80
                };
            }
            return result;
        }

        public static bool WithinSurvey(float ax, float ay, float az, float bx, float by, float bz,
            float planetRadius, float surveyRadius)
        {
            var dot = ax * bx + ay * by + az * bz;
            var aLength = (float)Math.Sqrt(ax * ax + ay * ay + az * az);
            var bLength = (float)Math.Sqrt(bx * bx + by * by + bz * bz);
            if (!Finite(dot) || !Finite(aLength) || !Finite(bLength) || aLength < .99f || bLength < .99f
                || !Finite(planetRadius) || !Finite(surveyRadius) || planetRadius <= 0 || surveyRadius < 0) return false;
            return WH30K.Planet.PlanetScale.SurfaceDistance(ax, ay, az, bx, by, bz, planetRadius) <= surveyRadius;
        }

        public static bool TryResolve(DiscoveryRecord record, int seed, bool rapid,
            out DiscoveryRecord resolved, out DiscoveryOutcome outcome)
            =>TryResolve(record,seed,rapid,DefaultProfile((DiscoveryKind)record.kind),out resolved,out outcome);

        public static bool TryResolve(DiscoveryRecord record,int seed,bool rapid,DiscoveryRewardProfile profile,
            out DiscoveryRecord resolved,out DiscoveryOutcome outcome)
        {
            resolved=record;outcome=default;
            if(!record.discovered||record.resolved||!ValidProfile(profile)||!Finite(record.reward)||record.reward<0||record.reward>10000)return false;
            var hazard=false;
            if(rapid)hazard=new Random(unchecked(seed^record.id*104729^0x61B)).NextDouble()<profile.hazardChance;
            outcome=new DiscoveryOutcome{hazard=hazard,reward=hazard?0:record.reward*(rapid?profile.rapidMultiplier:profile.cautiousMultiplier),
                impact=hazard?profile.hazardImpact:rapid?profile.rapidImpact:profile.cautiousImpact};
            resolved.resolved=true;resolved.outcomeRecorded=true;resolved.rapidChoice=rapid;
            resolved.outcomeHazard=hazard;resolved.rewardReceived=outcome.reward;resolved.outcomeImpact=outcome.impact;
            return true;
        }

        public static bool ValidProfile(DiscoveryRewardProfile profile)
            =>Finite(profile.cautiousMultiplier)&&profile.cautiousMultiplier>=0&&profile.cautiousMultiplier<=10
                &&Finite(profile.rapidMultiplier)&&profile.rapidMultiplier>=0&&profile.rapidMultiplier<=10
                &&Finite(profile.hazardChance)&&profile.hazardChance>=0&&profile.hazardChance<=1
                &&ValidImpact(profile.cautiousImpact)&&ValidImpact(profile.rapidImpact)&&ValidImpact(profile.hazardImpact);

        private static bool ValidImpact(EnvironmentImpact impact)
            =>Finite(impact.co2Delta)&&Math.Abs(impact.co2Delta)<=1000&&Finite(impact.o2Delta)&&Math.Abs(impact.o2Delta)<=1000
                &&Finite(impact.waterDelta)&&Math.Abs(impact.waterDelta)<=1000&&Finite(impact.temperatureDelta)&&Math.Abs(impact.temperatureDelta)<=1000;

        public static bool Valid(DiscoveryRecord[] discoveries)
        {
            if (discoveries == null || discoveries.Length > MaximumDiscoveries) return discoveries == null || discoveries.Length == 0;
            var ids = new HashSet<int>();
            foreach (var discovery in discoveries)
            {
                if (discovery.id < 0 || discovery.id >= MaximumDiscoveries || !ids.Add(discovery.id) || discovery.kind < 0 || discovery.kind > 3
                    || !Finite(discovery.x) || !Finite(discovery.y) || !Finite(discovery.z)
                    || !Finite(discovery.reward) || discovery.reward < 0 || discovery.reward > 10000
                    || (discovery.x * discovery.x + discovery.y * discovery.y + discovery.z * discovery.z) < .99f
                    || (discovery.x * discovery.x + discovery.y * discovery.y + discovery.z * discovery.z) > 1.01f
                    || discovery.resolved && !discovery.discovered
                    || !Finite(discovery.rewardReceived)||discovery.rewardReceived<0||discovery.rewardReceived>100000||!ValidImpact(discovery.outcomeImpact)
                    || discovery.outcomeRecorded&&!discovery.resolved
                    || !discovery.outcomeRecorded&&(discovery.rapidChoice||discovery.outcomeHazard||discovery.rewardReceived!=0)) return false;
            }
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
