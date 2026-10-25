using Godot;
using NGarage.Core;

// Drawn over the map tiles: the car (seen from above, turned the way it faces) and your phone's position,
// or an arrow at the map's edge pointing to it when it's out of view.
public partial class MapMarks : Control
{
    public MapFrame Frame=null!;
    public VehicleLocation Car=null!;
    public (double Latitude,double Longitude,double Accuracy)? Phone;
    public UnitSystem Units;
    public Font? LabelFont;
    private static readonly Color PhoneBlue=new("#0a84ff");
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        if(Phone is var (lat,lon,accuracy)) {
            var (x,y)=Frame.Pixel(lat,lon);var at=new Vector2((float)x,(float)y);
            if(Frame.Shows(lat,lon)) {
                float radius=(float)Mathf.Clamp(accuracy/Frame.MetersPerPixel,0,160);
                if(radius>8)DrawCircle(at,radius,new Color(PhoneBlue,0.15f));
                DrawCircle(at,8,Colors.White);DrawCircle(at,6,PhoneBlue);
            } else Arrow(at,IgnoredPlaces.Meters(Car.Latitude,Car.Longitude,lat,lon));
        }
    }
    // Toward the phone from the middle of the map, stopped just inside the edge, with the distance.
    private void Arrow(Vector2 phone,double meters)
    {
        var middle=Size/2;var toward=phone-middle;
        if(toward.Length()<1)return;
        float scale=Mathf.Min((middle.X-18)/Mathf.Max(Mathf.Abs(toward.X),0.001f),(middle.Y-18)/Mathf.Max(Mathf.Abs(toward.Y),0.001f));
        var tip=middle+toward*scale;var dir=toward.Normalized();var side=new Vector2(-dir.Y,dir.X);
        var points=new[]{tip,tip-dir*16+side*9,tip-dir*16-side*9};
        DrawColoredPolygon(points,PhoneBlue);DrawPolyline(new[]{points[0],points[1],points[2],points[0]},Colors.White,2,true);
        if(LabelFont==null)return;
        string text="You · "+NGarage.Core.Units.Short(meters,Units);
        var size=LabelFont.GetStringSize(text,HorizontalAlignment.Left,-1,12);
        var at=tip-dir*30-size/2;at=new(Mathf.Clamp(at.X,6,Size.X-size.X-6),Mathf.Clamp(at.Y,6,Size.Y-size.Y-6));
        DrawRect(new Rect2(at-new Vector2(5,3),size+new Vector2(10,6)),new Color(0,0,0,0.6f));
        DrawString(LabelFont,at+new Vector2(0,size.Y-4),text,HorizontalAlignment.Left,-1,12,Colors.White);
    }
}

// The car marker: its top-down picture when there is one, otherwise a drawn car. Front up, turned to the heading.
public partial class CarMarker : Control
{
    public Texture2D? Picture;
    public Color Paint=new("#89b7f0");
    public double? Heading;
    public const float Width=22,Length=44;
    private static readonly Color Glass=new("#14202c"),Tire=new("#0b0f14"),Lamp=new("#f4f1e4"),NRed=new("#f24a4d");
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        DrawSetTransform(Size/2,Mathf.DegToRad((float)(Heading??0)),Vector2.One);
        if(Picture!=null){DrawTextureRect(Picture,new Rect2(-Width/2-2,-Length/2-2,Width+4,Length+4),false);return;}
        float w=Width/2,l=Length/2;
        foreach(float x in new[]{-w-1,w-3})foreach(float y in new[]{-l+6,l-15})DrawRect(new Rect2(x,y,4,9),Tire);
        var body=new StyleBoxFlat {BgColor=Paint,BorderColor=Colors.White,CornerRadiusTopLeft=7,CornerRadiusTopRight=7,CornerRadiusBottomLeft=5,CornerRadiusBottomRight=5};
        body.SetBorderWidthAll(2);
        DrawStyleBox(body,new Rect2(-w,-l,Width,Length));
        DrawColoredPolygon(new Vector2[]{new(-w+3,-l+12),new(w-3,-l+12),new(w-5,-l+18),new(-w+5,-l+18)},Glass);
        DrawColoredPolygon(new Vector2[]{new(-w+5,l-12),new(w-5,l-12),new(w-3,l-8),new(-w+3,l-8)},Glass);
        DrawRect(new Rect2(-w+3,-l+1,5,2),Lamp);DrawRect(new Rect2(w-8,-l+1,5,2),Lamp);
        DrawRect(new Rect2(-w+3,l-3,5,2),NRed);DrawRect(new Rect2(w-8,l-3,5,2),NRed);
        DrawCircle(new Vector2(0,1),3,NRed);
    }
}

// A place pin, its point at the bottom middle.
public partial class PlacePin : Control
{
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        var head=new Vector2(Size.X/2,11);
        DrawColoredPolygon(new[]{new Vector2(Size.X/2-6,17),new Vector2(Size.X/2+6,17),new Vector2(Size.X/2,Size.Y)},Colors.White);
        DrawCircle(head,11,Colors.White);DrawCircle(head,8,new Color("#f24a4d"));DrawCircle(head,3,Colors.White);
    }
}
