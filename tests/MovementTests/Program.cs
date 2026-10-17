using System;
using RimFront;

internal static class Program
{
    static int checks;
    static int Main()
    {
        try { Run(); return 0; }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
    static void Run()
    {
        FloatStoreRegression();
        TightCornerRegression();
        const double r = .22;
        Func<int,int,bool> empty = (_,_) => false;
        var p = AnalogCollision.Move(.5,.5, .123,.456,r,empty);
        Near(p.X,.623,"fractional horizontal movement"); Near(p.Z,.956,"fractional forward movement");
        var direction = AnalogCollision.Direction(0,1,1);
        Near(Math.Sqrt(direction.X*direction.X+direction.Z*direction.Z),1,"diagonal input speed normalized");
        direction=AnalogCollision.Direction(Math.PI/2,1,0);
        Near(direction.X,1,"yaw rotates forward east"); Near(direction.Z,0,"yaw forward east has no north component");
        direction=AnalogCollision.Direction(0,.25,0); Near(direction.Z,.25,"analog magnitude preserved");
        direction=AnalogCollision.Direction(double.NaN,1,0); Near(direction.X,0,"nonfinite direction suppressed");

        Func<int,int,bool> wall=(x,z)=>x==2;
        p=AnalogCollision.Move(.5,.5,8,0,r,wall);
        Check(p.X<=2-r && p.X>2-r-.001,"high displacement cannot tunnel through a one-cell wall"); Near(p.Z,.5,"wall hit leaves other axis");
        p=AnalogCollision.Move(.5,.5,5,3,r,wall);
        Check(p.X<=2-r && p.X>2-r-.001,"diagonal sweep stops perpendicular to wall"); Near(p.Z,3.5,"wall slide preserves tangent motion",.001);
        p=AnalogCollision.Move(2-r,.5,0,2,r,wall); Near(p.Z,2.5,"touching wall permits tangential movement");
        p=AnalogCollision.Move(2-r,.5,-.5,0,r,wall); Near(p.X,1.5-r,"moving away from wall is free");

        p=AnalogCollision.Move(.5,.5,2,2,r,(x,z)=>(x==1&&z==0)||(x==0&&z==1));
        Check(p.X<=1-r && p.Z<=1-r,"cannot cut diagonal between touching solid corners");
        p=AnalogCollision.Move(.5,.5,2,2,r,(x,z)=>x==1&&z==1);
        Check(p.X<=1-r && p.Z<=1-r,"simultaneous corner impact stops both axes");
        p=AnalogCollision.Move(.5,.5,0,8,r,(x,z)=>x==-1||x==1); Near(p.Z,8.5,"one-cell corridor remains traversable");
        p=AnalogCollision.Move(.5,.5,0,3,r,(x,z)=>x==0&&z==1);
        Check(p.Z<=1-r,"closed door blocks sweep");
        p=AnalogCollision.Move(.5,.5,0,3,r,empty); Near(p.Z,3.5,"opening door restores path");
        p=AnalogCollision.Move(1.5,1.5,2,0,r,(x,z)=>x==1&&z==1);
        Near(p.X,1.5,"new solid overlapping current footprint freezes movement");
        p=AnalogCollision.Move(.5,.5,double.NaN,1,r,empty); Near(p.X,.5,"NaN delta rejected"); Near(p.Z,.5,"NaN delta does not move other axis");
        p=AnalogCollision.Move(.5,.5,65,0,r,empty); Near(p.X,.5,"unbounded displacement rejected");

        // Exercise the swept result against a real geometric non-overlap invariant over
        // multiple shapes/directions rather than reproducing the collision algorithm.
        var random=new Random(1739);
        for(int sample=0;sample<1200;sample++)
        {
            int bx=random.Next(-3,4), bz=random.Next(-3,4);
            double x=random.NextDouble()*8-4,z=random.NextDouble()*8-4;
            if(Overlaps(x,z,r,bx,bz)){sample--;continue;}
            double dx=random.NextDouble()*12-6,dz=random.NextDouble()*12-6;
            p=AnalogCollision.Move(x,z,dx,dz,r,(cx,cz)=>cx==bx&&cz==bz);
            Check(double.IsFinite(p.X)&&double.IsFinite(p.Z),"random sweep remains finite");
            Check(!Overlaps(p.X,p.Z,r,bx,bz),"random sweep does not penetrate block");
            Check(Math.Sqrt((p.X-x)*(p.X-x)+(p.Z-z)*(p.Z-z))<=Math.Sqrt(dx*dx+dz*dz)+.0001,"collision never adds travel");
            var shifted=AnalogCollision.Move(x+17,z-9,dx,dz,r,(cx,cz)=>cx==bx+17&&cz==bz-9);
            Near(shifted.X-17,p.X,"translation-invariant collision X",.0001); Near(shifted.Z+9,p.Z,"translation-invariant collision Z",.0001);
        }
        Console.WriteLine($"PASS: {checks} analog direction, collision, sliding and non-penetration checks. No game session was started.");
    }
    static void FloatStoreRegression()
    {
        foreach (int cell in new[] { 64, 128, 256, 400 })
        foreach (double radius in new[] { (double).355f, (double).489f, (double).49f })
        {
            double x=cell-1.5, z=cell+.5;
            Func<int,int,bool> wall=(cx,cz)=>cx==cell;
            for(int i=0;i<120;i++)
            {
                var next=AnalogCollision.Move(x,z,.07,0,radius,wall);
                x=(float)next.X; z=(float)next.Z;
                Check(x+radius<=cell+.000001,"float-store wall contact cannot penetrate at "+cell+" radius "+radius+" tick "+i+" (x="+x+")");
            }
            var slide=AnalogCollision.Move(x,z,0,.07,radius,wall);
            Check(slide.Z>z+.069,"float-store wall contact can slide at "+cell+" radius "+radius+" (x="+x+", z="+z+", resultZ="+slide.Z+")");
            var retreat=AnalogCollision.Move(x,z,-.07,0,radius,wall);
            Check(retreat.X<x-.069,"float-store wall contact can retreat");
            Check(x+radius<=cell+.000001,"float-store wall contact cannot penetrate at "+cell+" radius "+radius+" (x="+x+")");
        }
        // A previously rounded contact is repairable immediately, including while retreating.
        const int edge=128;
        double body=(double).489f, embedded=(float)(edge-body);
        Check(embedded+body>edge,"regression fixture actually contains float embedding");
        var escaped=AnalogCollision.Move(embedded,128.5,-.07,0,body,(x,z)=>x==edge);
        Check(escaped.X<embedded-.069,"rounding overlap can retreat without tunneling");
        var tangent=AnalogCollision.Move(embedded,128.5,0,.07,body,(x,z)=>x==edge);
        Check(tangent.X+body<=edge && tangent.Z>128.569,"rounding overlap repairs and permits tangent motion");
        var deeper=AnalogCollision.Move(edge-body+.002,128.5,-.07,0,body,(x,z)=>x==edge);
        Near(deeper.X,edge-body+.002,"real overlap beyond float tolerance cannot escape through geometry");
    }
    static void TightCornerRegression()
    {
        foreach(int origin in new[]{64,128,256,400})
        foreach(double radius in new[]{(double).355f,(double).489f,(double).49f})
        {
            Func<int,int,bool> room=(x,z)=>x==origin+1 || z==origin+1;
            double px=origin+.5,pz=origin+.5;
            for(int tick=0;tick<120;tick++)
            {
                var p=AnalogCollision.Move(px,pz,.0495,.0495,radius,room);
                px=(float)p.X; pz=(float)p.Z;
                Check(px+radius<=origin+1+.000001 && pz+radius<=origin+1+.000001,"stored float corner cannot penetrate either wall");
            }
            var retreat=AnalogCollision.Move(px,pz,-.0495,-.0495,radius,room);
            Check(retreat.X<px-.049 && retreat.Z<pz-.049,"retreat from tight corner remains responsive");

            // A real one-cell L-shaped passage, including the near-maximal supported footprint.
            Func<int,int,bool> passage=(x,z)=>!((x==origin && z>=origin && z<=origin+4) || (z==origin+4 && x>=origin && x<=origin+4));
            px=origin+.5; pz=origin+.5;
            for(int tick=0;tick<180;tick++)
            {
                var p=AnalogCollision.Move(px,pz,tick<90?0:.07,tick<90?.07:0,radius,passage);
                px=(float)p.X; pz=(float)p.Z;
                Check(Clear(px,pz,radius,passage),"float-store L passage preserves full AABB wall clearance");
            }
            Check(px>origin+4.4 && pz>origin+4.4,"one-cell L passage completes turn without a corner freeze");
        }
    }
    static bool Clear(double x,double z,double radius,Func<int,int,bool> blocked)
    {
        for(int cz=(int)Math.Floor(z-radius);cz<=(int)Math.Floor(z+radius);cz++)
        for(int cx=(int)Math.Floor(x-radius);cx<=(int)Math.Floor(x+radius);cx++)
            if(blocked(cx,cz) && Overlaps(x,z,radius,cx,cz))return false;
        return true;
    }
    static bool Overlaps(double x,double z,double r,int bx,int bz)=> x>bx-r+.000001 && x<bx+1+r-.000001 && z>bz-r+.000001 && z<bz+1+r-.000001;
    static void Near(double actual,double expected,string label,double tolerance=.00001)=>Check(Math.Abs(actual-expected)<tolerance,label+" ("+actual+" vs "+expected+")");
    static void Check(bool condition,string label){checks++;if(!condition)throw new Exception("FAIL: "+label);}
}
