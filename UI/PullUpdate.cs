using System.Collections.Generic;
using Godot;
using NGarage.Core;

// Swipe down to update on the Car screen. The screen's content slides down under the header, uncovering PULL TO UPDATE
// over a pixel divider (PullIndicator); letting go once it's full settles it, does exactly what the Update button does,
// and slides it away while the update runs. The pull survives the screen being redrawn, so it keeps animating then.
public partial class Main
{
    private enum PullState{Idle,Tracking,Pulling,Updating}
    private PullState pullState;
    private Vector2 pullFrom;
    private float pullOffset;
    private bool pullSwallowRelease;
    private readonly List<(Control Node,float Y)> pullContent=new();
    private PullIndicator? pullIndicator;
    private Tween? pullTween;
    private const float ContentTop=96,ContentBottom=851;

    // Only when the Update button would work and nothing is on top of the screen.
    private bool CanPull=>liveMode&&screen=="Car"&&!DisclaimerRequired&&!liveBusy&&canvas.Visible&&
        pendingOverride==null&&pendingConfirm==null&&pendingPlaceAt==null&&sentDestination==null;

    // Called by Render after the screen is built: what the screen added from child `first` on is what slides.
    private void PullLayer(int first)
    {
        pullContent.Clear();pullIndicator=null;
        if(screen!="Car"||!liveMode||DisclaimerRequired)return;
        for(int i=first;i<canvas.GetChildCount();i++)
            if(canvas.GetChild(i) is Control node)pullContent.Add((node,node.Position.Y));
        pullIndicator=new PullIndicator {Name="PullIndicator",Position=new(0,ContentTop),Font=bold};
        canvas.AddChild(pullIndicator);
        SetPull(pullOffset);
    }
    private void SetPull(float offset)
    {
        pullOffset=offset;
        foreach(var (node,y) in pullContent)
            if(IsInstanceValid(node))node.Position=new(node.Position.X,y+offset);
        if(pullIndicator!=null&&IsInstanceValid(pullIndicator)) {
            pullIndicator.Size=new(460,offset);pullIndicator.Visible=offset>0.5f;
            pullIndicator.Updating=pullState==PullState.Updating;pullIndicator.QueueRedraw();
        }
    }
    private Vector2 CanvasPoint(Vector2 at)=>canvas.GetGlobalTransform().AffineInverse()*at;

    public override void _Input(InputEvent e)
    {
        if(e is InputEventScreenTouch touch&&touch.Index==0) {
            if(touch.Pressed) {
                pullFrom=CanvasPoint(touch.Position);
                if(pullState==PullState.Idle&&CanPull&&pullFrom.Y>ContentTop&&pullFrom.Y<ContentBottom)pullState=PullState.Tracking;
            }
            else if(pullState==PullState.Tracking)pullState=PullState.Idle;
            else if(pullState==PullState.Pulling){Release();GetViewport().SetInputAsHandled();}
        }
        else if(e is InputEventScreenDrag drag&&drag.Index==0&&pullState is PullState.Tracking or PullState.Pulling) {
            var move=CanvasPoint(drag.Position)-pullFrom;
            if(pullState==PullState.Tracking) {
                if(!CanPull||PullToUpdate.Abandoned(move.X,move.Y)){pullState=PullState.Idle;return;}
                if(!PullToUpdate.Begins(move.X,move.Y))return;
                // Redrawing drops the button the finger went down on, so letting go can't press it.
                pullState=PullState.Pulling;pullTween?.Kill();Render();
            }
            SetPull(PullToUpdate.Offset(move.Y-PullToUpdate.Start));
            GetViewport().SetInputAsHandled();
        }
        else if(e is InputEventMouseButton button) {
            // The mouse events made from the pulling touch never reach the buttons.
            if(pullState==PullState.Pulling||pullSwallowRelease&&!button.Pressed){pullSwallowRelease=false;GetViewport().SetInputAsHandled();}
            else if(button.Pressed)pullSwallowRelease=false;
        }
        else if(e is InputEventMouseMotion&&pullState==PullState.Pulling)GetViewport().SetInputAsHandled();
    }
    private void Release()
    {
        bool update=PullToUpdate.Armed(pullOffset)&&CanPull;
        pullState=update?PullState.Updating:PullState.Idle;pullSwallowRelease=true;
        pullTween?.Kill();pullTween=CreateTween();
        if(update) {
            pullTween.TweenMethod(Callable.From<float>(SetPull),pullOffset,PullToUpdate.Ready,0.12).SetEase(Tween.EaseType.Out);
            pullTween.TweenCallback(Callable.From(()=>{_ = RefreshLive(false);}));
            pullTween.TweenInterval(0.45);
            pullTween.TweenMethod(Callable.From<float>(SetPull),PullToUpdate.Ready,0f,0.25).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
            pullTween.TweenCallback(Callable.From(()=>{pullState=PullState.Idle;SetPull(0);}));
        }
        else pullTween.TweenMethod(Callable.From<float>(SetPull),pullOffset,0f,0.2).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }
}
