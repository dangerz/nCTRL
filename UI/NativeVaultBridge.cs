using Godot;
using NGarage.Core;
using System;
using System.Runtime.InteropServices;

public static class NativeVaultBridge
{
    public static bool OpenVehicleMap(double latitude,double longitude)
    {
        if(OS.GetName()!="iOS")return false;
        try {ng_vault_open_map(latitude,longitude);return true;}
        catch(DllNotFoundException){return false;}
        catch(EntryPointNotFoundException){return false;}
    }
    [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern void ng_vault_open_map(double latitude,double longitude);
    public static void RestoreForeground()
    {
        if(OS.GetName()!="iOS")return;
        try { ng_vault_resume(); }
        catch(DllNotFoundException) { }
        catch(EntryPointNotFoundException) { } // Older bridge still uses OS activation notifications.
    }
    [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern void ng_vault_resume();
    public static IVaultBridge Create()
    {
        if(OS.GetName()=="iOS") {
            try {var bridge=new IosVaultBridge();bridge.Availability();return bridge;}
            catch(DllNotFoundException){return new UnavailableVaultBridge(-6);}
            catch(EntryPointNotFoundException){return new UnavailableVaultBridge(-6);}
        }
        if(OS.GetName()=="Android" && Engine.HasSingleton("NGarageVault"))
            return new AndroidVaultBridge(Engine.GetSingleton("NGarageVault"));
        return new UnavailableVaultBridge(OS.GetName()=="Android"?-6:0);
    }
    private sealed class AndroidVaultBridge(GodotObject plugin) : IVaultBridge
    {
        public bool Present=>true;
        public int Availability()=>plugin.Call("availability").AsInt32();
        public bool HasAccount()=>plugin.Call("hasAccount").AsBool();
        public void Start(string op,string payload)=>plugin.Call("start",op,payload);
        public string TakeResult()=>plugin.Call("takeResult").AsString();
        public void Cancel()=>plugin.Call("cancel");
    }
    private sealed class IosVaultBridge : IVaultBridge
    {
        public bool Present=>true;
        public int Availability()=>ng_vault_availability();
        public bool HasAccount()=>ng_vault_has_account()!=0;
        public void Start(string op,string payload)=>ng_vault_start(op,payload);
        public string TakeResult(){IntPtr p=ng_vault_take_result();if(p==IntPtr.Zero)return "";try{return Marshal.PtrToStringUTF8(p)??"";}finally{ng_vault_free(p);}}
        public void Cancel()=>ng_vault_cancel();
        [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern int ng_vault_availability();
        [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern int ng_vault_has_account();
        [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern void ng_vault_start([MarshalAs(UnmanagedType.LPUTF8Str)]string op,[MarshalAs(UnmanagedType.LPUTF8Str)]string payload);
        [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr ng_vault_take_result();
        [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern void ng_vault_free(IntPtr pointer);
        [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern void ng_vault_cancel();
    }
}
