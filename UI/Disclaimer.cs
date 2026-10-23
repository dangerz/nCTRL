using Godot;
using NGarage.Core;
using System;
using System.Globalization;
using System.Threading.Tasks;

public partial class Main
{
    private DisclaimerAcceptance? disclaimer;
    private string disclaimerTyped="";
    private bool disclaimerChecked;
    // Phones always require the native device-owner check; desktop is a development target only.
    private bool deviceCheckRequired;
    private PendingSignIn? pendingSignIn;
    // Credentials held in memory while a different account reads the disclaimer. Never persisted.
    private sealed class PendingSignIn(string email,string password,string pin,bool remember,bool fromVault)
    {
        public string Email=>email;public string Password=>password;public string Pin=>pin;
        public bool Remember=>remember;public bool FromVault=>fromVault;
        public string AccountKey { get; }=VehicleLink.AccountKey(email);
        public override string ToString()=>"PendingSignIn [redacted]";
    }
    private bool DisclaimerRequired=>!Disclaimer.IsCurrent(disclaimer)||pendingSignIn!=null;

    // Stops a sign-in before any network request when this account has not accepted.
    private bool HoldForDisclaimer(string email,string password,string pin,bool remember,bool fromVault)
    {
        if(string.IsNullOrWhiteSpace(email)||Disclaimer.Allows(disclaimer,VehicleLink.AccountKey(email)))return false;
        pendingSignIn=new(email,password,pin,remember,fromVault);
        notice="This Hyundai account has not accepted the disclaimer. Read and accept it to continue.";noticeError=false;
        return true;
    }
    private void BindDisclaimer()
    {
        var bound=Disclaimer.Bind(disclaimer,accountKey);
        if(bound!=disclaimer){disclaimer=bound;SavePrefs();}
    }
    private void DisclaimerScreen()
    {
        Title("BEFORE YOU CONTINUE.",pendingSignIn!=null?"This Hyundai account needs its own acceptance.":"Third-party app · completely unsupported · your risk");
        DisclaimerText(196,270);
        Label(24,478,412,22,"TYPE \""+Disclaimer.Phrase.ToUpperInvariant()+"\" TO CONFIRM",11,Muted,true);
        var phrase=AccountInput(24,504,412,Disclaimer.Phrase,false);
        phrase.Name="DisclaimerPhrase";phrase.Text=disclaimerTyped;phrase.CaretColumn=phrase.Text.Length;
        var check=new CheckBox {Name="DisclaimerCheck",Position=new(24,560),Size=new(412,48),
            Text="I accept every risk and hold no one else liable.",ButtonPressed=disclaimerChecked,Disabled=liveBusy};
        check.AddThemeFontOverride("font",regular);check.AddThemeFontSizeOverride("font_size",13);canvas.AddChild(check);
        var submit=LiveButton(24,624,412,52,liveBusy?"Waiting for confirmation…":"Submit",15,()=>_ = AcceptDisclaimer(),true,"DisclaimerSubmit");
        void Refresh()=>submit.Disabled=liveBusy||!disclaimerChecked||!Disclaimer.PhraseMatches(disclaimerTyped);
        // Update in place: re-rendering would drop keyboard focus while typing.
        phrase.TextChanged+=text=>{disclaimerTyped=text;Refresh();};
        check.Toggled+=value=>{disclaimerChecked=value;Refresh();};
        Refresh();
        Label(24,684,412,36,deviceCheckRequired?"Submitting asks for "+vault.BiometricName+" or your passcode to record your acceptance.":
            "Desktop development build: no device check is available.",12,Muted,false,true);
        if(pendingSignIn!=null)LiveButton(24,726,412,44,"Cancel and use another account",13,()=>{pendingSignIn=null;notice="";Render();},false,"DisclaimerCancel");
    }
    private void DisclaimerText(int y,int height)
    {
        Panel(24,y,412,height,new("#0e2031"),false);
        var scroll=new ScrollContainer {Name="DisclaimerScroll",Position=new(32,y+8),Size=new(398,height-16),
            HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
        var text=new Label {Text=Disclaimer.Text,AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new(382,0)};
        text.AddThemeFontOverride("font",regular);text.AddThemeFontSizeOverride("font_size",12);text.AddThemeColorOverride("font_color",White);
        scroll.AddChild(text);canvas.AddChild(scroll);
    }
    private async Task AcceptDisclaimer()
    {
        if(liveBusy||!disclaimerChecked||!Disclaimer.PhraseMatches(disclaimerTyped))return;
        var pending=pendingSignIn;bool accepted=false;int version=liveEpoch;
        await RunLive(async()=>{
            if(deviceCheckRequired) {
                try {await vault.AuthenticateAsync("Confirm that you accept every risk of using nCTRL",liveLifetime.Token);}
                catch(VaultException ex) {
                    EnsureLive(version);noticeError=true;
                    notice=ex.Code switch {
                        "cancelled"=>"Confirmation cancelled. The disclaimer was not accepted.",
                        "unavailable"=>"This device cannot confirm with "+vault.BiometricName+" or a passcode, so nCTRL stays locked. Set a device passcode, then try again.",
                        _=>"Face ID or passcode was not confirmed. The disclaimer was not accepted."
                    };
                    return;
                }
            }
            EnsureLive(version);
            disclaimer=Disclaimer.Accept(pending?.AccountKey??"",DateTimeOffset.UtcNow);
            disclaimerTyped="";disclaimerChecked=false;pendingSignIn=null;SavePrefs();
            notice="Disclaimer accepted.";accepted=true;
        },"Waiting for confirmation…");
        // Resume the held sign-in only after the gate is recorded; it never ran a request before.
        if(!accepted||version!=liveEpoch)return;
        // With saved status, open the Car screen; the first request asks for Face ID.
        if(pending==null){if(EnterOffline())Render();return;}
        if(pending.FromVault)await UnlockAccount();
        else await SignIn(pending.Email,pending.Password,pending.Pin,pending.Remember);
    }
    private void DisclaimerView()
    {
        string accepted=disclaimer==null?"":disclaimer.AcceptedAt.ToLocalTime().ToString("MMM d, yyyy · h:mm tt",CultureInfo.CurrentCulture);
        Title("RISK DISCLAIMER.","You accepted this on "+accepted+".");
        DisclaimerText(196,480);
        Button(24,696,412,48,"Back to settings",13,()=>Navigate("Settings"),false,"DisclaimerBack");
    }
}
