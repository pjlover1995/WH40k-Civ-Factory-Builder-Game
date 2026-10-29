using System;
using System.Collections.Generic;

namespace WH30K.Sim
{
    // Convex ground polygons in metres. Height cannot bypass ownership of the land below a facility.
    public static class TerritoryRules
    {
        public static FarmPoint[] Intersect(FarmPoint[] a, FarmPoint[] b)
        {
            for (var i=0;i<b.Length && a.Length>=3;i++)
            { var p=b[i]; var q=b[(i+1)%b.Length]; a=FarmLayoutRules.Clip(a,p.z-q.z,q.x-p.x,(p.z-q.z)*p.x+(q.x-p.x)*p.z); }
            return a;
        }
        public static bool Overlaps(FarmPoint[] a,FarmPoint[] b) => FarmLayoutRules.Area(Intersect(a,b))>.0001f;
        public static bool Touches(FarmPoint[] a,FarmPoint[] b,float distance=.5f)
        {
            if(Overlaps(a,b))return true;
            foreach(var p in a) for(var j=0;j<b.Length;j++) if(DistanceSquared(p,b[j],b[(j+1)%b.Length])<=distance*distance)return true;
            foreach(var p in b) for(var j=0;j<a.Length;j++) if(DistanceSquared(p,a[j],a[(j+1)%a.Length])<=distance*distance)return true;
            return false;
        }
        private static float DistanceSquared(FarmPoint p,FarmPoint a,FarmPoint b)
        {
            var x=b.x-a.x;var z=b.z-a.z;var t=Math.Max(0,Math.Min(1,((p.x-a.x)*x+(p.z-a.z)*z)/Math.Max(.000001f,x*x+z*z)));
            var dx=p.x-a.x-x*t;var dz=p.z-a.z-z*t;return dx*dx+dz*dz;
        }
        public static FarmPoint[] SubtractLargest(FarmPoint[] parcel,FarmPoint[] occupied)
        {
            if(!Overlaps(parcel,occupied))return parcel;
            var fragments=new List<FarmPoint[]>();var remaining=parcel;
            for(var i=0;i<occupied.Length && remaining.Length>=3;i++)
            {
                var p=occupied[i];var q=occupied[(i+1)%occupied.Length];var nx=p.z-q.z;var nz=q.x-p.x;var limit=nx*p.x+nz*p.z;
                var outside=FarmLayoutRules.Clip(remaining,-nx,-nz,-limit);
                if(FarmLayoutRules.Area(outside)>.0001f)fragments.Add(outside);
                remaining=FarmLayoutRules.Clip(remaining,nx,nz,limit);
            }
            var best=Array.Empty<FarmPoint>();foreach(var fragment in fragments)if(FarmLayoutRules.Area(fragment)>FarmLayoutRules.Area(best))best=fragment;
            return best;
        }
        public static FarmPoint[] Expand(FarmPoint[] polygon,float margin)
        {
            var minX=float.MaxValue;var minZ=float.MaxValue;var maxX=float.MinValue;var maxZ=float.MinValue;
            foreach(var p in polygon){minX=Math.Min(minX,p.x);minZ=Math.Min(minZ,p.z);maxX=Math.Max(maxX,p.x);maxZ=Math.Max(maxZ,p.z);}
            var result=new[]{new FarmPoint(minX-margin,minZ-margin),new FarmPoint(minX-margin,maxZ+margin),
                new FarmPoint(maxX+margin,maxZ+margin),new FarmPoint(maxX+margin,minZ-margin)};
            for(var i=0;i<polygon.Length;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Length];var nx=a.z-b.z;var nz=b.x-a.x;
                result=FarmLayoutRules.Clip(result,nx,nz,nx*a.x+nz*a.z+margin*(float)Math.Sqrt(nx*nx+nz*nz));
            }
            return result;
        }
        public static bool ValidPolygon(FarmPoint[] polygon)
        {
            if(polygon==null || polygon.Length<3 || polygon.Length>64 || FarmLayoutRules.Area(polygon)<.00001f)return false;
            for(var i=0;i<polygon.Length;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Length];var c=polygon[(i+2)%polygon.Length];
                if(!ColonyRules.IsFinite(a.x+a.z) || Math.Abs(a.x)>5 || Math.Abs(a.z)>5
                    || (b.x-a.x)*(c.z-b.z)-(b.z-a.z)*(c.x-b.x)>.000001f)return false;
            }
            return true;
        }
    }
}
