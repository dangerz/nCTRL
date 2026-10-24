using Godot;

// Lines drawn on the edge of the car picture's frame (the same chamfered shape as PanelArt): a yellow
// light running counter-clockwise while charging, and a white dash sliding along the bottom edge, the way
// the car faces, while it reports moving. Animates only while one of them is on and the frame is visible.
public partial class CarFrame : Control
{
    public bool Charging,Moving;
    private const float Chamfer=12;
    private static readonly Color Yellow=new(1f,0.82f,0.16f),White=new("#eef5fb");
    private float time;
    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;}
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree()||!Charging&&!Moving)return;
        time+=(float)delta;QueueRedraw();
    }
    // The frame's corners in clockwise order, closed.
    private Vector2[] Edge()=>new Vector2[]{new(0,0),new(Size.X,0),new(Size.X,Size.Y-Chamfer),new(Size.X-Chamfer,Size.Y),new(0,Size.Y),new(0,0)};
    private static float Length(Vector2[] path){float n=0;for(int i=1;i<path.Length;i++)n+=path[i-1].DistanceTo(path[i]);return n;}
    // The point at a distance along the path, wrapping around.
    private static Vector2 At(Vector2[] path,float distance)
    {
        distance=Mathf.PosMod(distance,Length(path));
        for(int i=1;i<path.Length;i++) {
            float piece=path[i-1].DistanceTo(path[i]);
            if(distance<=piece)return path[i-1].Lerp(path[i],piece>0?distance/piece:0);
            distance-=piece;
        }
        return path[^1];
    }
    public override void _Draw()
    {
        if(Charging) {
            var edge=Edge();float length=Length(edge);
            DrawPolyline(edge,new Color(Yellow,0.3f),2,true);
            // The head moves backwards along the clockwise path, so it runs counter-clockwise; the tail trails it.
            float head=length*(1-Mathf.PosMod(time*0.18f,1f)),tail=length*0.3f;
            const int pieces=60;
            for(int i=0;i<pieces;i++) {
                float a=head+tail*i/pieces,b=head+tail*(i+1)/pieces;
                DrawLine(At(edge,a),At(edge,b),new Color(Yellow,1f-(float)i/pieces),2.5f,true);
            }
        }
        if(Moving) {
            // A dash sliding right to left along the bottom edge, brightest at its leading (left) end.
            float run=Size.X-Chamfer,dash=70;
            float lead=run+dash-Mathf.PosMod(time*160,run+dash*2);
            const int pieces=14;
            for(int i=0;i<pieces;i++) {
                float a=Mathf.Clamp(lead+dash*i/pieces,0,run),b=Mathf.Clamp(lead+dash*(i+1)/pieces,0,run);
                if(b>a)DrawLine(new(a,Size.Y),new(b,Size.Y),new Color(White,1f-(float)i/pieces),2.5f,true);
            }
        }
    }
}
