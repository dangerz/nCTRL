using NGarage.Core;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// The background alert check's C entry point, exported from the app's own code so the iOS background task
// (Native/iOS/NGarageVault.mm) runs the same Core as the app, with no second runtime. Blocks; called off the main thread.
public static class AlertExport
{
    [UnmanagedCallersOnly(EntryPoint="nctrl_alert_check")]
    public static IntPtr Check(IntPtr json)
    {
        string result;
        try {
            string input=Marshal.PtrToStringUTF8(json)??"";
            // iOS gives a background refresh about 30 seconds.
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            result=Task.Run(()=>AlertCheck.CheckJsonAsync(input,null,timeout.Token)).GetAwaiter().GetResult();
        } catch(Exception) {result="{\"ok\":false,\"skipped\":\"failed\"}";}
        return Marshal.StringToCoTaskMemUTF8(result);
    }
    [UnmanagedCallersOnly(EntryPoint="nctrl_alert_free")]
    public static void Free(IntPtr text)=>Marshal.FreeCoTaskMem(text);
}
