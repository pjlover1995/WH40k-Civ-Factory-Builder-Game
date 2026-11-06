using UnityEngine;
using WH30K.Sim;

namespace WH30K.Gameplay.Construction
{
    public static partial class BuildingVisuals
    {
        public static Mesh FarmSupportMesh(int design, int seed = 0)
        {
            var b = new Architecture(); FarmBuilding(b, design, 0, 0, 0, seed);
            return b.Build(FarmRules.BuildingDesign(design));
        }
        private static void FarmBuilding(Architecture b, int design, float x, float z, float y, int seed)
        {
            Vector3 P(float px, float py, float pz) => new Vector3(x+px,y+py,z+pz);
            void Box(int material,float px,float py,float pz,float sx,float sy,float sz)
                => b.Box(material,P(px,py,pz),new Vector3(sx,sy,sz));
            var variation = Mathf.Abs(seed % 5);
            if (design == 0)
            {
                Box(Stone,0,13,0,22,26,23); b.Gable(Roof,P(0,26,0),25,13,26);
                Box(Iron,0,8,-12,11,16,1);
                b.Beam(Brass,P(-5,1,-13),P(5,15,-13),1); Box(Amber,0,19,-12,4,2,1);
                b.Cylinder(Iron,P(16,0,1),4,4,22); b.Cylinder(Roof,P(16,22,1),5,0,7);
                Box(Stone,-16,4,1,6,8,12); b.Gable(Roof,P(-16,8,1),7,4,13);
            }
            else if (design == 1)
            {
                // Broad L-plan grain store, loading canopy and a low side wing.
                Box(Stone,-6,13,0,25,26,23); b.Gable(Roof,P(-6,26,0),28,10+variation,26);
                Box(Stone,11,8,6,16,16,12); b.Gable(Roof,P(11,16,6),18,8,14);
                Box(Iron,-6,8,-12,12,16,1); Box(Roof,-5,14,-12,27,2,5);
                for(var side=-1;side<=1;side+=2) Box(Brass,-5+side*12,7,-14,1,14,1);
                for(var column=0;column<3;column++)
                { Box(Iron,-14+column*8,20,-12,4,5,1); Box(Amber,-14+column*8,20,-12.6f,2,3,.4f); }
                Box(Brass,11,10,-.5f,13,2,1); Box(Iron,11,5,-.5f,10,8,1);
            }
            else if (design == 2)
            {
                // Raised tower granary with twin short bins, steep caps and an elevated transfer gallery.
                Box(Stone,0,2,0,40,4,27);
                b.Cylinder(Iron,P(-7,4,2),8,8,43+variation,16);
                b.Cylinder(Roof,P(-7,47+variation,2),10,0,14,16);
                for(var side=-1;side<=1;side+=2)
                {
                    b.Cylinder(Iron,P(11,4,side*7),5,5,25,16);
                    b.Cylinder(Roof,P(11,29,side*7),6,0,8,16);
                    b.Beam(Brass,P(-7,31,2),P(11,29,side*7),1.6f);
                }
                for(var ring=0;ring<4;ring++) b.Cylinder(Brass,P(-7,10+ring*9,2),8.4f,8.4f,1,16);
                Box(Brass,-7,18,-11,24,2,5);
                for(var side=-1;side<=1;side+=2) Box(Iron,-7+side*11,10,-11,1,16,1);
                Box(Amber,-7,33,-6.2f,2,4,1);
            }
            else
            {
                // Long machinery workshop: three garage bays, roof monitors, vents and a water tank.
                Box(Stone,0,12,0,40,24,20); b.Gable(Roof,P(0,24,0),43,10+variation,23);
                for(var bay=-1;bay<=1;bay++)
                {
                    Box(Iron,bay*13,7,-10.5f,10,14,1);
                    Box(Brass,bay*13,15,-11,11,1,1); Box(Amber,bay*13,19,-10.7f,5,2,1);
                }
                for(var side=-1;side<=1;side+=2)
                {
                    b.Cylinder(Iron,P(side*11,29,2),2,2,15,12);
                    b.Cylinder(Brass,P(side*11,44,2),3,3,1,12);
                }
                Box(Iron,0,34,0,18,5,5); b.Gable(Roof,P(0,36,0),20,5,7);
                b.Cylinder(Iron,P(15,1,10),4,4,16,16); b.Cylinder(Roof,P(15,17,10),4.5f,1,4,16);
            }
        }
    }
}
