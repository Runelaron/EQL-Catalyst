using EqlTracker.Core;

internal static class CascadeWidthTests
{
    internal static void Register(Action<string, Action> test)
    {
        static void Near(double expected, double actual) { if (Math.Abs(expected - actual) > .00001) throw new Exception($"Expected {expected}, got {actual}"); }
        static double[] Resize(WidthTrack[] tracks, int boundary, double delta) { var result = new double[tracks.Length]; CascadeWidths.Resize(tracks, boundary, delta, result); return result; }
        test("Cascade left consumes nearest donor before earlier columns", () =>
        {
            WidthTrack[] tracks = [new(300,100),new(250,150),new(200,100)];
            var small = Resize(tracks,1,-60); Near(300,small[0]); Near(190,small[1]); Near(260,small[2]);
            var large = Resize(tracks,1,-160); Near(240,large[0]); Near(150,large[1]); Near(360,large[2]);
        });
        test("Cascade right mirrors nearest first across four tracks", () =>
        {
            WidthTrack[] tracks = [new(200,100),new(250,150),new(300,180),new(150,100)];
            var widths = Resize(tracks,0,150); Near(350,widths[0]); Near(150,widths[1]); Near(250,widths[2]); Near(150,widths[3]);
        });
        test("Cascade stops at all donor minimums and recipient maximum", () =>
        {
            WidthTrack[] tracks = [new(300,100),new(250,150),new(200,100,600)];
            var widths = Resize(tracks,1,-10000); Near(100,widths[0]); Near(150,widths[1]); Near(500,widths[2]);
            widths = Resize([new(200,100,230),new(200,100),new(200,100)],0,1000); Near(230,widths[0]); Near(170,widths[1]); Near(200,widths[2]);
        });
        test("Cascade reversal is calculated from the starting snapshot", () =>
        {
            WidthTrack[] tracks = [new(300,100),new(250,150),new(200,100)];
            _ = Resize(tracks,1,-160); var reversed = Resize(tracks,1,-20); Near(300,reversed[0]); Near(230,reversed[1]); Near(220,reversed[2]);
            var crossed = Resize(tracks,1,40); Near(300,crossed[0]); Near(290,crossed[1]); Near(160,crossed[2]);
        });
        test("Cascade preserves fractional widths and exact total without allocations", () =>
        {
            WidthTrack[] tracks = [new(123.25,90.5),new(111.125,90.25),new(167.75,100.5)];
            var result = new double[3]; CascadeWidths.Resize(tracks,1,-40.125,result); Near(tracks.Sum(t=>t.Width),result.Sum()); Near(104,result[0]); Near(90.25,result[1]);
            long before = GC.GetAllocatedBytesForCurrentThread(); for(int i=0;i<10000;i++) CascadeWidths.Resize(tracks,1,(i%101)-50,result);
            if(GC.GetAllocatedBytesForCurrentThread()!=before)throw new Exception("Width solver allocated during dragging");
        });
        test("Cascade adapter order may omit hidden tracks and use displayed order", () =>
        {
            // The adapter supplies displayed B,A,C; hidden D never contributes width.
            WidthTrack[] displayed = [new(130,100),new(180,120),new(220,100)];
            var widths=Resize(displayed,1,-80); Near(110,widths[0]);Near(120,widths[1]);Near(300,widths[2]);
        });
        test("Cascade frozen tracks are skipped and outside boundaries do nothing", () =>
        {
            WidthTrack[] tracks=[new(200,100),new(24,24,24),new(200,100)];
            var widths=Resize(tracks,1,-40);Near(160,widths[0]);Near(24,widths[1]);Near(240,widths[2]);
            widths=Resize(tracks,0,40);Near(240,widths[0]);Near(24,widths[1]);Near(160,widths[2]);
            foreach(int edge in new[]{-1,2}) { widths=Resize(tracks,edge,40); for(int i=0;i<3;i++)Near(tracks[i].Width,widths[i]); }
        });
        test("Cascade window fit preserves preferences within bounded available width", () =>
        {
            WidthTrack[] tracks=[new(300,100),new(200,150),new(80,80,80)];var result=new double[3];
            CascadeWidths.Fit(tracks,480,result);Near(480,result.Sum());Near(80,result[2]);Near(1.5,result[0]/result[1]);
            CascadeWidths.Fit(tracks,250,result);Near(330,result.Sum());Near(100,result[0]);Near(150,result[1]);
            CascadeWidths.Fit([new(200,100,210),new(200,100)],600,result.AsSpan(0,2));Near(210,result[0]);Near(390,result[1]);
        });
        test("Cascade random boundaries conserve size and change distant donors only after nearer minimums", () =>
        {
            var random=new Random(18923);
            for(int sample=0;sample<500;sample++)
            {
                var tracks=Enumerable.Range(0,7).Select(_=>new WidthTrack(100+random.NextDouble()*300,70,450)).ToArray();
                int boundary=random.Next(6);double delta=(random.NextDouble()-.5)*1600;var widths=Resize(tracks,boundary,delta);
                Near(tracks.Sum(t=>t.Width),widths.Sum());
                for(int i=0;i<7;i++)if(widths[i]<tracks[i].Minimum-.00001||widths[i]>tracks[i].Maximum+.00001)throw new Exception("Boundary violated");
                int start=delta<0?boundary:boundary+1,step=delta<0?-1:1;bool earlierNotMinimum=false;
                for(int i=start;i>=0&&i<7;i+=step){if(earlierNotMinimum)Near(tracks[i].Width,widths[i]);if(widths[i]>tracks[i].Minimum+.00001)earlierNotMinimum=true;}
            }
        });
    }
}
