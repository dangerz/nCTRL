using System;
using NGarage.Core;

static class PullTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Swipe down to update: "+label);count++;}
        Check(!PullToUpdate.Begins(0,PullToUpdate.Start-1)&&PullToUpdate.Begins(0,PullToUpdate.Start),"A pull starts after a small move down");
        Check(!PullToUpdate.Begins(20,15)&&PullToUpdate.Begins(-10,15),"A drag more sideways than down isn't a pull");
        Check(PullToUpdate.Abandoned(20,5)&&PullToUpdate.Abandoned(0,-12)&&!PullToUpdate.Abandoned(5,5),"Sideways or upward drags stop counting; small wobbles wait");
        Check(PullToUpdate.Offset(0)==0&&PullToUpdate.Offset(-30)==0,"No movement before the pull");
        Check(Math.Abs(PullToUpdate.Offset(10)-10)<1,"The content follows the finger at first");
        Check(PullToUpdate.Offset(200)-PullToUpdate.Offset(150)<PullToUpdate.Offset(50)-PullToUpdate.Offset(0),"It gets heavier the further you pull");
        Check(PullToUpdate.Offset(10000)<=110,"It never goes past its reach");
        float ready=0;while(!PullToUpdate.Armed(PullToUpdate.Offset(ready)))ready++;
        Check(ready>60&&ready<100,$"Ready after a comfortable pull ({ready} points past the start)");
        Check(PullToUpdate.Lit(0,69)==0&&PullToUpdate.Lit(PullToUpdate.Ready/2,69)==34&&PullToUpdate.Lit(PullToUpdate.Ready,69)==69&&PullToUpdate.Lit(100,69)==69,
            "The divider fills as you pull and is full when ready");
        Check(!PullToUpdate.Armed(PullToUpdate.Offset(40)),"A short pull isn't ready");
        Console.WriteLine($"PASS: {count} swipe-down checks.");
    }
}
