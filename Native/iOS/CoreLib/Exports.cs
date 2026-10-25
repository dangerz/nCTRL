using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

// C entry points for the Share extension (Native/iOS/Extensions/Share). Each call takes an operation name and UTF-8
// JSON and returns UTF-8 JSON, freed with nctrl_core_free. Calls block, so the extension makes them off the main thread.
public static class Exports
{
    [UnmanagedCallersOnly(EntryPoint="nctrl_core_call")]
    public static IntPtr Call(IntPtr operation,IntPtr json)
    {
        string result;
        try {
            string op=Marshal.PtrToStringUTF8(operation)??"",input=Marshal.PtrToStringUTF8(json)??"";
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            result=op switch {
                "read_share"=>Task.Run(()=>ShareSender.ReadJsonAsync(input,null,timeout.Token)).GetAwaiter().GetResult(),
                "send"=>Task.Run(()=>ShareSender.SendAsync(input,null,timeout.Token)).GetAwaiter().GetResult(),
                _=>"{\"ok\":false,\"error\":\"Unknown operation.\"}"
            };
        } catch(Exception) {result="{\"ok\":false,\"error\":\"nCTRL couldn't do that from the Share sheet.\"}";}
        return Marshal.StringToCoTaskMemUTF8(result);
    }
    [UnmanagedCallersOnly(EntryPoint="nctrl_core_free")]
    public static void Free(IntPtr text)=>Marshal.FreeCoTaskMem(text);
}
