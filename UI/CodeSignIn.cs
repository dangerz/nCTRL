using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Kia Connect asks new devices for a one-time code (text or email). After the code, Kia gives a remember token
// that lets later sign-ins skip it; the saved login keeps that token with the device id it belongs to.
public partial class Main
{
    // The service gtCTRL connects to. The UI reads names and capabilities from here, never hard-codes them.
    private static ServiceInfo AppService=>Brand.Service;
    private static readonly (string Id,string Label)[] AllServiceChoices={(Brand.Hyundai,"Hyundai · US"),(Brand.Kia,"Kia · US"),
        (Brand.HyundaiCanada,"Hyundai · Canada"),(Brand.KiaCanada,"Kia · Canada")};
    // The flavor's services, in the flavor's order (gtCTRL: Kia US and Kia Canada).
    private static (string Id,string Label)[] ServiceChoices=>Brand.Flavor.Services.Select(id=>AllServiceChoices.First(c=>c.Id==id)).ToArray();
    // Kia sees this as the phone's id. Not secret; kept in preferences so it survives sign-outs.
    private string kiaDeviceId="";
    private sealed record PendingCode(IVehicleConnection Client,string Email,string Password,bool Remember,bool FromVault,CodeDestinations Where);
    private PendingCode? pendingCode;
    private string codeSentTo="";

    private IVehicleConnection NewConnection()
    {
        var client=Brand.Connect(budget);
        if(client is IOneTimeCodeSignIn codes) {
            if(kiaDeviceId.Length==0){kiaDeviceId=codes.DeviceId;if(!smoke)SavePrefs();}
            codes.DeviceId=kiaDeviceId;
        }
        return client;
    }
    /// <summary>Switches the sign-in service (nCTRL offers Hyundai and Kia). The default picture follows it.</summary>
    private void ChooseService(string id)
    {
        if(liveBusy||id==Brand.ServiceId)return;
        // Canadian accounts most likely want kilometres and Celsius; Range & units can switch back.
        if(Brand.InCanada(id)&&!Brand.InCanada(Brand.ServiceId))units=UnitSystem.Metric;
        Brand.ServiceId=id;SelectCar();carTexture=null;SavePrefs();notice="";noticeError=false;Render();
    }
    /// <summary>Gives a new client the saved remember token (and the device id it was issued to), so no code is asked.</summary>
    private static void UseRemembered(IVehicleConnection client,SavedAccount? account)
    {
        if(client is not IOneTimeCodeSignIn codes||account is not {RememberToken.Length:>0})return;
        codes.RememberToken=account.RememberToken;
        if(account.DeviceId.Length>0)codes.DeviceId=account.DeviceId;
    }
    private static (string Token,string Device) Remembered(IVehicleConnection client)=>
        client is IOneTimeCodeSignIn codes?(codes.RememberToken,codes.DeviceId):("","");

    private void AskForCode(IVehicleConnection client,string email,string password,bool remember,bool fromVault,CodeDestinations where)
    {
        pendingCode=new(client,email,password,remember,fromVault,where);codeSentTo="";
        liveMode=false;screen="Code";noticeError=false;
        notice=fromVault?$"{Brand.Maker} wants to confirm this phone again. Choose where to get a code.":$"{Brand.Maker} wants to confirm this phone. Choose where to get a code.";
    }

    private void CodeScreen()
    {
        if(pendingCode is not {} p){ConnectionScreen();return;}
        Title("CONFIRM IT'S YOU.",AppService.ServiceName+" · one-time code");
        Label(24,204,412,60,codeSentTo.Length==0?$"{Brand.Maker} sends a code to confirm this phone. After that, it remembers the phone and won't ask each time.":
            "Code sent to "+codeSentTo+". Enter it below. It can take a minute to arrive.",14,Muted,false,true);
        float y=280;
        if(p.Where.HasPhone){LiveButton(24,y,412,48,(codeSentTo.Length>0?"Text it again to ":"Text me at ")+p.Where.Phone,14,()=>_ = SendCode(CodeChannel.Text),codeSentTo.Length==0,"CodeText");y+=60;}
        if(p.Where.HasEmail){LiveButton(24,y,412,48,(codeSentTo.Length>0?"Email it again to ":"Email me at ")+p.Where.Email,14,()=>_ = SendCode(CodeChannel.Email),codeSentTo.Length==0&&!p.Where.HasPhone,"CodeEmail");y+=60;}
        if(!p.Where.HasPhone&&!p.Where.HasEmail)
            Label(24,y,412,48,$"{Brand.Maker} didn't say where it can send a code. Check your {AppService.ServiceName} account in {Brand.Maker}'s app.",13,Muted,false,true);
        y+=16;
        Label(24,y,412,23,"ONE-TIME CODE",11,Muted,true);
        var code=AccountInput(24,(int)y+28,412,"Code",false);code.Name="CodeInput";code.MaxLength=10;
        code.VirtualKeyboardType=LineEdit.VirtualKeyboardTypeEnum.Number;
        var verify=LiveButton(24,y+92,412,53,liveBusy?"Checking…":"Verify code",15,()=>{string c=code.Text;code.Text="";_ = VerifyCode(c);},true,"CodeVerify");
        verify.Disabled|=codeSentTo.Length==0;
        LiveButton(24,y+162,412,44,"Cancel",13,CancelCode,false,"CodeCancel");
    }
    private async Task SendCode(CodeChannel channel)
    {
        if(pendingCode is not {Client: IOneTimeCodeSignIn codes} p)return;
        int version=liveEpoch;
        await RunLive(async()=>{
            await codes.SendCodeAsync(channel,liveLifetime.Token);EnsureLive(version);
            codeSentTo=(channel==CodeChannel.Text?p.Where.Phone:p.Where.Email)??"";
            notice="Code sent. Enter it when it arrives.";
        },$"Asking {Brand.Maker} to send a code…");
    }
    private async Task VerifyCode(string code)
    {
        if(pendingCode is not {Client: IOneTimeCodeSignIn codes} p)return;
        int version=liveEpoch;
        await RunLive(async()=>{
            var vehicles=await codes.VerifyCodeAsync(code,liveLifetime.Token);EnsureLive(version);
            pendingCode=null;codeSentTo="";
            // A saved login is always kept up to date with the new remember token.
            await FinishSignIn(p.Client,vehicles,p.Email,p.Password,"",p.Remember||p.FromVault,version);
        },"Checking the code…");
    }
    private void CancelCode()
    {
        var p=pendingCode;pendingCode=null;codeSentTo="";
        if(p!=null&&ReferenceEquals(connection,p.Client)){connection=null;}
        p?.Client.Dispose();
        screen="Connect";notice="";noticeError=false;
        // With a saved account and status, go back to the Car screen showing that status.
        if(p?.FromVault==true)EnterOffline();
        Render();
    }

    /// <summary>Everything after a successful sign-in (typed or with a code): bind the disclaimer, save or forget the
    /// login as chosen, and reopen the remembered vehicle.</summary>
    private async Task FinishSignIn(IVehicleConnection active,IReadOnlyList<EnrolledVehicle> vehicles,string email,string password,string pin,bool remember,int version)
    {
        accountKey=VehicleLink.AccountKey(email);BindDisclaimer();
        // Typing the password (or the code) verifies the owner for this foreground session.
        enrollment=vehicles;selectedIndex=0;screen="Connect";notice="Signed in. Select your vehicle.";
        try {
            // The saved login may be changing: the old shared session goes, and the next request shares the new one.
            await ForgetSharedSession();EnsureLive(version);
            if(remember) {
                var (token,device)=Remembered(active);
                var saved=new SavedAccount(email,password,pin,token,device,Brand.ServiceId);
                await vault.SaveAsync(saved,liveLifetime.Token);EnsureLive(version);
                sessionAccount=saved;ResumeAlerts();
                notice="Account saved securely. Select your vehicle.";
            } else if(vault.HasAccount) {
                await vault.ForgetAsync(liveLifetime.Token);EnsureLive(version);
                notice="Signed in for this session. Saved login removed.";
            }
        } catch(VaultException ex) {
            EnsureLive(version);notice="Signed in for this session. "+ex.Message;noticeError=true;
        }
        await RestoreLinkedVehicle(active,version);
    }
}
