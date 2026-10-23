using Godot;
using NGarage.Core;

// Animated status effects drawn over the Ioniq 5 N art. Positions are fractions of the
// 1774×887 source sprite, so they follow its aspect-fit scaling. Drawn in 2-pixel blocks
// to match the pixel artwork. Animates only while visible on the Car screen.
public partial class StatusOverlay : Control
{
    public LockEffect Lock;
    public ClimateEffect Climate;
    public Vector2 TextureSize=new(1774,887);
    private float time;
    private static readonly Color LockRed=new("#f24a4d"),Outline=new("#eef5fb"),Backdrop=new(0.03f,0.07f,0.11f,0.78f),
        Heat=new("#ff8a3d"),HeatCore=new("#ffd166"),Frost=new("#dff3ff"),Air=new("#b8e2ff");
    // Cabin glass (windshield and side windows) in source-sprite pixels.
    private static readonly Vector2[] Glass={new(505,305),new(745,170),new(1095,150),new(1545,165),new(1580,300),new(1060,318)};
    private static readonly string[] Closed={"..XXXXX..",".X.....X.",".X.....X.","XXXXXXXXX","XXXXXXXXX","XXXX.XXXX","XXXX.XXXX","XXXXXXXXX","XXXXXXXXX"};
    private static readonly string[] Open={"..XXXXX..",".X.....X.",".X.......",".X.......","XXXXXXXXX","X.......X","X...X...X","X.......X","XXXXXXXXX"};

    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;}
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree()||Lock==LockEffect.None&&Climate==ClimateEffect.None)return;
        time+=(float)delta;QueueRedraw();
    }
    private Vector2 At(float fx,float fy)
    {
        float scale=Mathf.Min(Size.X/TextureSize.X,Size.Y/TextureSize.Y);
        var origin=(Size-TextureSize*scale)/2;
        return origin+new Vector2(fx*TextureSize.X*scale,fy*TextureSize.Y*scale);
    }
    private void Block(Vector2 p,Color c,float w=2,float h=2)=>DrawRect(new Rect2(p.Snapped(Vector2.One*2),new Vector2(w,h)),c);
    public override void _Draw()
    {
        if(Climate!=ClimateEffect.None)DrawClimate();
        if(Lock!=LockEffect.None)DrawLock();
    }
    private void DrawClimate()
    {
        var glass=new Vector2[Glass.Length];
        for(int i=0;i<Glass.Length;i++)glass[i]=At(Glass[i].X/TextureSize.X,Glass[i].Y/TextureSize.Y);
        float pulse=0.5f+0.5f*Mathf.Sin(time*2f);
        if(Climate==ClimateEffect.Heat) {
            DrawColoredPolygon(glass,new Color(Heat,0.10f+0.08f*pulse));
            // Heat shimmer rising off the roof in wavering columns.
            for(int column=0;column<6;column++)
            for(int k=0;k<4;k++) {
                float phase=Mathf.PosMod(time*0.55f+k/4f+column*0.17f,1f);
                var root=At(0.50f+column*0.065f,0.155f);
                var p=root+new Vector2(Mathf.Sin(time*3f+k*1.7f+column)*3f,-phase*30f);
                Block(p,new Color(phase<0.35f?HeatCore:Heat,1f-phase),2,6);
            }
        } else if(Climate==ClimateEffect.Frost) {
            DrawColoredPolygon(glass,new Color(Frost,0.10f+0.08f*pulse));
            // Snowflakes drifting down across the cabin.
            for(int i=0;i<14;i++) {
                float phase=Mathf.PosMod(time*(0.16f+0.03f*(i%4))+i*0.37f,1f);
                var p=At(0.30f+Mathf.PosMod(i*0.047f,0.58f),phase*0.42f)+new Vector2(Mathf.Sin(time*1.5f+i)*4f,0);
                var c=new Color(Frost,Mathf.Min(1f,(1f-phase)*1.6f)*0.95f);
                Block(p,c);Block(p+new Vector2(-2,0),c);Block(p+new Vector2(2,0),c);Block(p+new Vector2(0,-2),c);Block(p+new Vector2(0,2),c);
            }
        } else {
            // Neutral airflow: dashes streaming through the cabin glass.
            for(int stream=0;stream<4;stream++)
            for(int d=0;d<3;d++) {
                float phase=Mathf.PosMod(time*0.45f+d/3f+stream*0.21f,1f);
                var p=At(0.33f+phase*0.55f,0.215f+stream*0.035f);
                Block(p,new Color(Air,Mathf.Sin(phase*Mathf.Pi)),16,2);
            }
        }
    }
    private void DrawLock()
    {
        // Badge on the front door beside its handle; the backdrop keeps it readable on any paint.
        var center=At(0.688f,0.462f);
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
