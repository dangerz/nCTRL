using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace NGarage.Core;

// A preference, never an authorization token. Match only against fresh authenticated enrollment.
public static class VehicleLink
{
    public static string AccountKey(string email)=>Hash(email.Trim().ToUpperInvariant());
    public static string Fingerprint(string accountKey,string vin)=>Hash(accountKey+"\n"+vin.ToUpperInvariant());
    private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static int Find(string fingerprint,string accountKey,IReadOnlyList<EnrolledVehicle> vehicles)
    {
        if(fingerprint.Length!=64||accountKey.Length!=64)return -1;
        for(int i=0;i<vehicles.Count;i++)if(Fingerprint(accountKey,vehicles[i].Vin)==fingerprint)return i;
        return -1;
    }
    public static ActionKind? DoorAction(bool? locked)=>locked switch {true=>ActionKind.Unlock,false=>ActionKind.Lock,_=>null};
}
