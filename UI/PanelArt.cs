using Godot;

public partial class PanelArt : Control
{
    public Color Fill = Skin.PanelFill;
    public Color Edge = Skin.PanelEdge;
    public bool Accent = true;
    public bool Stripes;
    public bool BottomEdgeOnly;
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        float w = Size.X, h = Size.Y, c = 12;
        Vector2[] pts = { new(0, 0), new(w, 0), new(w, h-c), new(w-c,h), new(0,h) };
        DrawColoredPolygon(pts, Fill);
        if(BottomEdgeOnly)DrawLine(new(0,h),new(w,h),Edge,1,true);
        else DrawPolyline(new Vector2[]{pts[0],pts[1],pts[2],pts[3],pts[4],pts[0]}, Edge, 1, true);
        if (Accent) DrawColoredPolygon(new Vector2[]{new(0,0),new(19,0),new(0,19)}, Skin.Detail);
        if (Stripes)
        {
            DrawColoredPolygon(new Vector2[]{new(w-130,0),new(w-62,0),new(w-195,h),new(w-263,h)},Skin.Stripe);
            DrawColoredPolygon(new Vector2[]{new(w-40,0),new(w-20,0),new(w-153,h),new(w-173,h)},Skin.DetailDark);
        }
    }
}
