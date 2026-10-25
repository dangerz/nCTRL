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
    // The phone's own position for the map: asks iOS (and the user, the first time), then poll PhoneLocation:
    // 0 none, 1 locating, 2 located, 3 not allowed, 4 failed, 5 usage text missing. Other platforms have none.
    public static void PhoneLocate()
    {
        if(OS.GetName()!="iOS")return;
        try {ng_phone_locate();}
        catch(DllNotFoundException) { }
        catch(EntryPointNotFoundException) { }
    }
    public static (int State,double Latitude,double Longitude,double Accuracy) PhoneLocation()
    {
        if(OS.GetName()!="iOS")return (0,0,0,0);
        try {int state=ng_phone_location(out double lat,out double lon,out double accuracy);return (state,lat,lon,accuracy);}
        catch(DllNotFoundException){return (0,0,0,0);}
        catch(EntryPointNotFoundException){return (0,0,0,0);}
    }
    // Looks up an address with Apple's geocoder (iPhone only); poll GeocodeResult: 0 none, 1 looking, 2 found
    // (with the place as JSON), 3 not found, 4 failed.
    public static bool CanGeocode=>OS.GetName()=="iOS";
    public static void Geocode(string query)
    {
        if(!CanGeocode)return;
        try {ng_geocode(query);}
        catch(DllNotFoundException) { }
        catch(EntryPointNotFoundException) { }
    }
    public static (int State,string Place) GeocodeResult()
    {
        if(!CanGeocode)return (0,"");
        try {
            IntPtr p=ng_geocode_result(out int state);
            if(p==IntPtr.Zero)return (state,"");
            try {return (state,Marshal.PtrToStringUTF8(p)??"");} finally {ng_vault_free_text(p);}
        }
        catch(DllNotFoundException){return (4,"");}
        catch(EntryPointNotFoundException){return (4,"");}
    }
    [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern void ng_geocode([MarshalAs(UnmanagedType.LPUTF8Str)]string query);
    [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr ng_geocode_result(out int state);
    [DllImport("__Internal",EntryPoint="ng_vault_free",CallingConvention=CallingConvention.Cdecl)]private static extern void ng_vault_free_text(IntPtr pointer);
    // Files in the App Group shared with the widget and the Share extension (iPhone only). Writing asks the
    // widget to redraw; no bytes deletes the file.
    public static bool HasAppGroup=>OS.GetName()=="iOS";
    public static bool GroupWrite(string name,byte[]? bytes)
    {
        if(!HasAppGroup)return false;
        try {return ng_group_write(name,bytes,bytes?.Length??0)==1;}
        catch(DllNotFoundException){return false;}
        catch(EntryPointNotFoundException){return false;}
    }
    // The App Group folder (shared with the widget and the Share extension); empty without one.
    public static string GroupPath()
    {
        if(!HasAppGroup)return "";
        try {
            IntPtr p=ng_group_path();
            if(p==IntPtr.Zero)return "";
            try {return Marshal.PtrToStringUTF8(p)??"";} finally {ng_vault_free_text(p);}
        }
        catch(DllNotFoundException){return "";}
        catch(EntryPointNotFoundException){return "";}
    }
    [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr ng_group_path();
    // A file from the Share extension (share.json, share-log.json), taken once; empty when there is none.
    public static string GroupTake(string name)
    {
        if(!HasAppGroup)return "";
        try {
            IntPtr p=ng_group_take(name);
            if(p==IntPtr.Zero)return "";
            try {return Marshal.PtrToStringUTF8(p)??"";} finally {ng_vault_free_text(p);}
        }
        catch(DllNotFoundException){return "";}
        catch(EntryPointNotFoundException){return "";}
    }
    [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern int ng_group_write([MarshalAs(UnmanagedType.LPUTF8Str)]string name,byte[]? bytes,int length);
    [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr ng_group_take([MarshalAs(UnmanagedType.LPUTF8Str)]string name);
    [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern void ng_phone_locate();
    [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern int ng_phone_location(out double latitude,out double longitude,out double accuracy);
    public static bool CanSavePhotos=>OS.GetName()=="iOS";
    // Starts an add-only save to Photos; poll PhotoStatus: 1 saving, 2 saved, 3 not allowed, 4 failed.
    public static bool SavePhoto(byte[] image)
    {
        if(!CanSavePhotos)return false;
        try {ng_photo_save(image,image.Length);return true;}
        catch(DllNotFoundException){return false;}
        catch(EntryPointNotFoundException){return false;}
    }
    public static int PhotoStatus()=>CanSavePhotos?ng_photo_status():0;
    [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern void ng_photo_save(byte[] bytes,int length);
    [DllImport("__Internal",CallingConvention=CallingConvention.Cdecl)]private static extern int ng_photo_status();
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
