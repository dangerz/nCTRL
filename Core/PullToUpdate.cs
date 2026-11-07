using System;

namespace NGarage.Core;

/// <summary>Swipe down to update on the Car screen (UI/PullUpdate.cs): when a drag counts as a pull, how far the
/// content follows the finger, when letting go updates, and how much of the pixel divider is lit. In the app's points.</summary>
public static class PullToUpdate
{
    /// <summary>How far the finger moves before a drag counts as a pull, so taps and small wobbles stay taps.</summary>
    public const float Start=12;
    /// <summary>How far the content moves before letting go updates, and where it rests while the update starts.</summary>
    public const float Ready=56;
    private const float Reach=110;
    /// <summary>A drag is a pull once it has gone far enough down, and more down than sideways.</summary>
    public static bool Begins(float dx,float dy)=>dy>=Start&&dy>Math.Abs(dx);
    /// <summary>A drag that has moved this far without becoming a pull never will (sideways or upward).</summary>
    public static bool Abandoned(float dx,float dy)=>!Begins(dx,dy)&&(Math.Abs(dx)>=Start||dy<=-Start);
    /// <summary>The content follows the finger at first and gets heavier, never passing Reach. drag is past Start.</summary>
    public static float Offset(float drag)=>drag<=0?0:Reach*(1-MathF.Exp(-drag/Reach));
    public static bool Armed(float offset)=>offset>=Ready;
    /// <summary>Blocks of the divider lit for a pull this far; all of them once it's ready.</summary>
    public static int Lit(float offset,int blocks)=>Math.Clamp((int)(offset/Ready*blocks),0,blocks);
}
