using Godot;
using NGarage.Core;

// Status drawn over the Ioniq 5 N art: static red/blue tints for the windshield (climate), front
// seats (heat/ventilation) and steering wheel (heat), plus a pulsing padlock on the front door.
// Positions are source-sprite pixels (1774×887), so they follow its aspect-fit scaling.
// The car faces left, so the nearer seat through the windshield is the passenger's.
public partial class StatusOverlay : Control
{
    public LockEffect Lock;
    public ComfortTint Windshield,DriverSeat,PassengerSeat;
    public bool WheelHeat;
    public Vector2 TextureSize=new(1774,887);
    private float time;
    private static readonly Color LockRed=new("#f24a4d"),Outline=new("#eef5fb"),Backdrop=new(0.03f,0.07f,0.11f,0.78f),
        HeatTint=new("#ff3b30"),CoolTint=new("#3aa0ff");
    private static readonly Vector2[] WindshieldGlass={new(485,312),new(700,160),new(1095,148),new(1040,312)};
    private static readonly Rect2 DriverSeatBack=new(778,196,66,64),PassengerSeatBack=new(884,198,92,98);
    private static readonly Vector2 WheelCenter=new(735,278),WheelRadius=new(42,22);
    private static readonly Vector2 LockCenter=new(1220,410);
    private static readonly string[] Closed={"..XXXXX..",".X.....X.",".X.....X.","XXXXXXXXX","XXXXXXXXX","XXXX.XXXX","XXXX.XXXX","XXXXXXXXX","XXXXXXXXX"};
    private static readonly string[] Open={"..XXXXX..",".X.....X.",".X.......",".X.......","XXXXXXXXX","X.......X","X...X...X","X.......X","XXXXXXXXX"};

    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;}
    // Only the padlock animates.
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree()||Lock==LockEffect.None)return;
        time+=(float)delta;QueueRedraw();
    }
    private float Scale=>Mathf.Min(Size.X/TextureSize.X,Size.Y/TextureSize.Y);
    private Vector2 At(Vector2 source)=>(Size-TextureSize*Scale)/2+source*Scale;
    // The padlock's tap area in this control's coordinates (larger than the 28-point badge).
    public Rect2 LockArea()=>new(At(LockCenter)-new Vector2(24,24),new Vector2(48,48));
    private static Color Tint(ComfortTint tint,float alpha)=>new(tint==ComfortTint.Heat?HeatTint:CoolTint,alpha);
    public override void _Draw()
    {
        if(Windshield!=ComfortTint.None) {
            var glass=new Vector2[WindshieldGlass.Length];
            for(int i=0;i<glass.Length;i++)glass[i]=At(WindshieldGlass[i]);
            DrawColoredPolygon(glass,Tint(Windshield,0.30f));
        }
        if(DriverSeat!=ComfortTint.None)SeatBack(DriverSeatBack,DriverSeat);
        if(PassengerSeat!=ComfortTint.None)SeatBack(PassengerSeatBack,PassengerSeat);
        if(WheelHeat) {
            var center=At(WheelCenter);var radius=WheelRadius*Scale;
            var ring=new Vector2[25];
            for(int i=0;i<ring.Length;i++){float a=i*Mathf.Tau/24;ring[i]=center+new Vector2(Mathf.Cos(a)*radius.X,Mathf.Sin(a)*radius.Y);}
            DrawPolyline(ring,new Color(HeatTint,0.9f),2.5f);
        }
        if(Lock!=LockEffect.None)DrawLock();
    }
    private void SeatBack(Rect2 source,ComfortTint tint)
    {
        var rect=new Rect2(At(source.Position),source.Size*Scale);
        // Seat back with a narrower headrest above it.
        float head=rect.Size.Y*0.3f;
        DrawRect(new Rect2(rect.Position+new Vector2(rect.Size.X*0.25f,0),new Vector2(rect.Size.X*0.5f,head*0.8f)),Tint(tint,0.55f));
        DrawRect(new Rect2(rect.Position+new Vector2(0,head),new Vector2(rect.Size.X,rect.Size.Y-head)),Tint(tint,0.55f));
    }
    private void DrawLock()
    {
        // Badge on the front door beside its handle; the backdrop keeps it readable on any paint.
        var center=At(LockCenter);
        var origin=(center-new Vector2(9,9)).Snapped(Vector2.One*2);
        bool locked=Lock==LockEffect.Locked;
        float pulse=0.5f+0.5f*Mathf.Sin(time*(locked?3f:2f));
        DrawRect(new Rect2(origin-new Vector2(5,5),new Vector2(28,28)),Backdrop);
        if(locked)DrawRect(new Rect2(origin-new Vector2(5,5),new Vector2(28,28)),new Color(LockRed,0.25f+0.35f*pulse),false,2);
        var rows=locked?Closed:Open;
        var ink=locked?LockRed:new Color(Outline,0.55f+0.45f*pulse);
        for(int y=0;y<rows.Length;y++)for(int x=0;x<rows[y].Length;x++)
            if(rows[y][x]=='X')DrawRect(new Rect2(origin+new Vector2(x*2,y*2),Vector2.One*2),ink);
        // A white glint sweeps across the closed lock every few seconds.
        if(locked) {
            float sweep=Mathf.PosMod(time,2.6f)/0.5f;
            if(sweep<1f)for(int y=3;y<9;y++){int x=(int)(sweep*12f)-(y-3);if(x>=0&&x<9&&rows[y][x]=='X')DrawRect(new Rect2(origin+new Vector2(x*2,y*2),Vector2.One*2),Outline);}
        }
    }
}
