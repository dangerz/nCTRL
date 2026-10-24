using System;

namespace NGarage.Core;

/// <summary>The cold-weather range estimate's settings, all in °F: below StartF range starts to drop,
/// reaching LossPercent at ColdestF and staying there when colder.</summary>
public sealed record ColdRangeSettings(bool Enabled=true,double StartF=70,double ColdestF=0,int LossPercent=50)
{
    public static readonly ColdRangeSettings Default=new();
    public const double MinF=-40,MaxF=100;
    public const int MaxLoss=90;
    /// <summary>Kept within limits, with the start always at least one degree warmer than the coldest.</summary>
    public ColdRangeSettings Valid()
    {
        double start=double.IsFinite(StartF)?Math.Clamp(StartF,MinF+1,MaxF):Default.StartF;
        double coldest=double.IsFinite(ColdestF)?Math.Clamp(ColdestF,MinF,start-1):Math.Min(Default.ColdestF,start-1);
        return this with {StartF=start,ColdestF=coldest,LossPercent=Math.Clamp(LossPercent,0,MaxLoss)};
    }
}

public static class ColdRange
{
    /// <summary>The share of the reported range left at this temperature: 1 at or above the start,
    /// falling evenly to 1 − loss at the coldest temperature, and no lower.</summary>
    public static double Factor(double outsideF,ColdRangeSettings settings)
    {
        var s=settings.Valid();
        double cold=Math.Clamp((s.StartF-outsideF)/(s.StartF-s.ColdestF),0,1);
        return 1-cold*s.LossPercent/100.0;
    }
    /// <summary>The cold-weather range, or null when it is off, a value is missing, or it isn't cold.</summary>
    public static double? Estimate(double? rangeMiles,double? outsideF,ColdRangeSettings settings)
    {
        if(!settings.Enabled||rangeMiles is not double range||outsideF is not double t||!double.IsFinite(t)||range<0)return null;
        if(t>=settings.Valid().StartF)return null;
        return range*Factor(t,settings);
    }
}
