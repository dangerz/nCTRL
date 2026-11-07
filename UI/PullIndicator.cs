using Godot;
using NGarage.Core;

// What shows in the gap above the Car screen's content while you pull it down: PULL TO UPDATE over a pixel divider
// whose blocks light up as you pull, RELEASE TO UPDATE once it's full, then UPDATING… with a lit run sweeping along
// it. Clipped to the gap, with the text and divider kept at its bottom so they come down with the content.
public partial class PullIndicator : Control
{
    public Font? Font;
    public bool Updating;
    private const float Left=24,Width=412;
    private const int Block=4,Pitch=6,Run=14;
    private static readonly int Blocks=(int)((Width+Pitch-Block)/Pitch);
    private float time;
    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;ClipContents=true;}
    public override void _Process(double delta)
    {
        if(!Updating||!IsVisibleInTree())return;
        time+=(float)delta;QueueRedraw();
    }
    public override void _Draw()
    {
        float gap=Size.Y;
        if(gap<=0||Font==null)return;
        bool armed=Updating||PullToUpdate.Armed(gap);
        DrawString(Font,new(Left,gap-24),Updating?"UPDATING…":armed?"RELEASE TO UPDATE":"PULL TO UPDATE",HorizontalAlignment.Left,Width,11,armed?Skin.Text:Skin.Muted);
        int lit=PullToUpdate.Lit(gap,Blocks);
        // While updating, a run of lit blocks sweeps left to right, brightest at its head, like the charging line.
        float head=Mathf.PosMod(time*70,Blocks+Run);
        for(int i=0;i<Blocks;i++) {
            var color=Skin.BarOff;
            if(Updating){float behind=head-i;if(behind>=0&&behind<Run)color=Skin.BarOff.Lerp(Skin.Accent,1-behind/Run);}
            else if(i<lit)color=Skin.Accent;
            DrawRect(new Rect2(Left+i*Pitch,gap-14,Block,Block),color);
        }
    }
}
