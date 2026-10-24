using Godot;

public partial class Glyph : Control
{
    public string Kind = "Car";
    public Color Ink = new("#8ecbfa");
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        float s = Size.X / 32;
        DrawSetTransform(Vector2.Zero,0,new Vector2(s,s));
        void L(float x,float y,float a,float b) => DrawLine(new(x,y),new(a,b),Ink,2,true);
        void R(float x,float y,float w,float h,bool fill=false) => DrawRect(new Rect2(x,y,w,h),Ink,fill,fill?-1:2);
        switch(Kind)
        {
            case "Lock": R(7,14,18,14); DrawArc(new(16,13),7,Mathf.Pi,Mathf.Tau,18,Ink,2,true); L(16,19,16,23); break;
            case "Climate":
                DrawCircle(new(16,16),3,Ink);
                for(int i=0;i<4;i++) { float a=i*Mathf.Pi/2; Vector2 pos=new(16+Mathf.Cos(a)*8,16+Mathf.Sin(a)*8); DrawCircle(pos,4,Ink); }
                break;
            case "Charge": DrawColoredPolygon(new Vector2[]{new(19,2),new(6,19),new(14,19),new(11,30),new(27,12),new(18,12)},Ink); break;
            case "Locate": DrawArc(new(16,12),8,Mathf.Pi,Mathf.Tau*2,32,Ink,2,true); L(9,17,16,29);L(23,17,16,29);DrawCircle(new(16,12),3,Ink);break;
            case "Activity": DrawArc(new(16,16),12,0,Mathf.Tau,32,Ink,2,true); L(16,8,16,16);L(16,16,22,19); break;
            case "Settings": R(6,6,20,20);DrawCircle(new(16,16),5,Ink,false,2);L(16,1,16,6);L(16,26,16,31);L(1,16,6,16);L(26,16,31,16);break;
            case "Energy": R(4,19,4,10,true);R(14,11,4,18,true);R(24,3,4,26,true);break;
            case "Details": DrawArc(new(16,16),13,0,Mathf.Tau,32,Ink,3,true);DrawArc(new(16,16),5,0,Mathf.Tau,18,Ink,2,true);
                for(int i=0;i<5;i++){float a=i*Mathf.Tau/5;L(16+Mathf.Cos(a)*5,16+Mathf.Sin(a)*5,16+Mathf.Cos(a)*10,16+Mathf.Sin(a)*10);} break;
            case "Cold":
                for(int i=0;i<6;i++) {
                    float a=i*Mathf.Pi/3;Vector2 arm=new(Mathf.Cos(a),Mathf.Sin(a)),mid=new Vector2(16,16)+arm*9;
                    DrawLine(new(16,16),new Vector2(16,16)+arm*14,Ink,3,true);
                    foreach(float side in new[]{-.8f,.8f})DrawLine(mid,mid+arm.Rotated(side)*5,Ink,3,true);
                }
                break;
            default: R(4,13,24,12);L(6,13,10,6);L(10,6,22,6);L(22,6,26,13);R(5,25,4,4,true);R(23,25,4,4,true);R(8,17,3,3,true);R(21,17,3,3,true);break;
        }
    }
}
