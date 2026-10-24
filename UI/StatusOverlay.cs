using Godot;
using NGarage.Core;

// Status drawn over the car art: static red/blue tints for the windshield (climate), front
// seats (heat/ventilation) and steering wheel (heat), plus a pulsing padlock on the front door.
// Positions are the picture's own pixels (see CarArt), so they follow its aspect-fit scaling.
// The car faces left, so its driver's side is in view and the nearer seat is the driver's.
public partial class StatusOverlay : Control
{
    public LockEffect Lock;
    public ComfortTint Windshield,DriverSeat,PassengerSeat;
    public bool WheelHeat;
    // Flash lights / horn & lights: blink the headlights (and show the horn "!") until this engine time.
    public ulong FlashUntilMs;
    public bool Horn;
    public Font? ExclaimFont;
    private FontVariation? italic;
    private bool drewFlash;
    public bool Flashing=>Time.GetTicksMsec()<FlashUntilMs;
    // Reported openings on the visible (driver's) side; passenger-side doors and the tailgate are not in view.
    public bool FrontDoorOpen,RearDoorOpen,FrontWindowOpen,RearWindowOpen,HoodOpen;
    public Vector2 TextureSize=new(1774,887);
    public CarArt Art=CarArt.For("ioniq5n");
    private float time;
    private static readonly Color LockRed=new("#f24a4d"),HeatTint=new("#ff3b30"),CoolTint=new("#3aa0ff");
    private static readonly Color Beam=new(1f,0.96f,0.78f),OpenAmber=new("#e0a43a");
    private static Color Outline=>Skin.Text;
    private static Color Dark=>Skin.Back;
    private static Color Backdrop=>new(Skin.Back,0.78f);
    private static readonly string[] Closed={"..XXXXX..",".X.....X.",".X.....X.","XXXXXXXXX","XXXXXXXXX","XXXX.XXXX","XXXX.XXXX","XXXXXXXXX","XXXXXXXXX"};
    private static readonly string[] Open={"..XXXXX..",".X.....X.",".X.......",".X.......","XXXXXXXXX","X.......X","X...X...X","X.......X","XXXXXXXXX"};

    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Ignore;
        // Italic by slanting the bold font: the top of each glyph leans right.
        if(ExclaimFont!=null)italic=new FontVariation {BaseFont=ExclaimFont,VariationTransform=new Transform2D(new Vector2(1,0),new Vector2(-0.28f,1),Vector2.Zero)};
    }
    // The padlock pulses; headlights blink only while a flash is showing (one more redraw clears it).
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree()||Lock==LockEffect.None&&!Flashing&&!drewFlash)return;
        time+=(float)delta;QueueRedraw();
    }
    private float ArtScale=>Mathf.Min(Size.X/TextureSize.X,Size.Y/TextureSize.Y);
    private Vector2 At(Vector2 source)=>(Size-TextureSize*ArtScale)/2+source*ArtScale;
    // The padlock's tap area in this control's coordinates (larger than the 28-point badge).
    public Rect2 LockArea()=>new(At(Art.Lock)-new Vector2(24,24),new Vector2(48,48));
    private static Color Tint(ComfortTint tint,float alpha)=>new(tint==ComfortTint.Heat?HeatTint:CoolTint,alpha);
    public override void _Draw()
    {
        if(Windshield!=ComfortTint.None) {
            var glass=new Vector2[Art.Windshield.Length];
            for(int i=0;i<glass.Length;i++)glass[i]=At(Art.Windshield[i]);
            DrawColoredPolygon(glass,Tint(Windshield,0.30f));
        }
        if(HoodOpen)Opening(Art.Hood,0.16f);
        if(FrontDoorOpen)Opening(Art.FrontDoor,0.16f);if(RearDoorOpen&&Art.RearDoor!=null)Opening(Art.RearDoor,0.16f);
        if(FrontWindowOpen)Opening(Art.FrontWindow,0.32f);if(RearWindowOpen&&Art.RearWindow!=null)Opening(Art.RearWindow,0.32f);
        if(DriverSeat!=ComfortTint.None)SeatBack(Art.DriverSeat,DriverSeat);
        if(PassengerSeat!=ComfortTint.None)SeatBack(Art.PassengerSeat,PassengerSeat);
        if(WheelHeat) {
            var center=At(Art.WheelCenter);var radius=Art.WheelRadius*ArtScale;
            var ring=new Vector2[25];
            for(int i=0;i<ring.Length;i++){float a=i*Mathf.Tau/24;ring[i]=center+new Vector2(Mathf.Cos(a)*radius.X,Mathf.Sin(a)*radius.Y);}
            DrawPolyline(ring,new Color(HeatTint,0.9f),2.5f);
        }
        if(Lock!=LockEffect.None)DrawLock();
        drewFlash=Flashing;
        if(drewFlash)DrawFlash();
    }
    private void DrawFlash()
    {
        // Hazard-style blink: about 0.3 s on, 0.3 s off.
        if(Time.GetTicksMsec()/300%2==0)
            foreach(var light in Art.Headlights) {
                var rect=new Rect2(At(light.Position),light.Size*ArtScale);
                DrawRect(rect.Grow(8),new Color(Beam,0.22f));DrawRect(rect.Grow(4),new Color(Beam,0.45f));DrawRect(rect,new Color(Beam,0.95f));
            }
        if(Horn&&italic!=null) {
            var at=At(Art.Horn)-new Vector2(6,0);
            DrawStringOutline(italic,at,"!",HorizontalAlignment.Left,-1,30,6,Dark);
            DrawString(italic,at,"!",HorizontalAlignment.Left,-1,30,LockRed);
        }
    }
    // Amber outline and light fill for a panel Hyundai reports open.
    private void Opening(Vector2[] source,float fill)
    {
        var shape=new Vector2[source.Length+1];
        for(int i=0;i<source.Length;i++)shape[i]=At(source[i]);
        shape[^1]=shape[0];
        DrawColoredPolygon(shape[..^1],new Color(OpenAmber,fill));
        DrawPolyline(shape,OpenAmber,2);
    }
    private void SeatBack(Rect2 source,ComfortTint tint)
    {
        var rect=new Rect2(At(source.Position),source.Size*ArtScale);
        // Seat back with a narrower headrest above it.
        float head=rect.Size.Y*0.3f;
        DrawRect(new Rect2(rect.Position+new Vector2(rect.Size.X*0.25f,0),new Vector2(rect.Size.X*0.5f,head*0.8f)),Tint(tint,0.55f));
        DrawRect(new Rect2(rect.Position+new Vector2(0,head),new Vector2(rect.Size.X,rect.Size.Y-head)),Tint(tint,0.55f));
    }
    private void DrawLock()
    {
        // Badge on the front door beside its handle; the backdrop keeps it readable on any paint.
        var center=At(Art.Lock);
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
