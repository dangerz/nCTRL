package com.ngarage.vault;

import android.app.Activity;
import android.content.DialogInterface;
import android.hardware.biometrics.BiometricManager;
import android.hardware.biometrics.BiometricPrompt;
import android.os.Build;
import android.os.CancellationSignal;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyPermanentlyInvalidatedException;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;
import android.util.Base64;
import android.view.WindowManager;
import org.godotengine.godot.Godot;
import org.godotengine.godot.plugin.GodotPlugin;
import org.godotengine.godot.plugin.UsedByGodot;
import org.json.JSONObject;
import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.Arrays;
import java.util.UUID;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** No plaintext preferences, log output, cloud backup or software encryption key. */
public final class NGarageVault extends GodotPlugin {
    private static final String PREFIX="ngarage.biometric.v1.";
    private static final byte[] AAD="N Garage / Bluelink credentials / v1".getBytes(StandardCharsets.UTF_8);
    private final Object guard=new Object();
    private String result="";
    private long generation;
    private boolean busy;
    private CancellationSignal cancellation;
    private byte[] pendingPlain;
    private String pendingAlias;

    public NGarageVault(Godot godot) { super(godot); }
    @Override public String getPluginName() { return "NGarageVault"; }
    @Override public void onMainResume() {
        Activity a=getActivity();
        if(a!=null)a.runOnUiThread(new Runnable(){@Override public void run(){a.getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);}});
    }
    @Override public void onMainPause() { cancel(); }
    @Override public void onMainDestroy() { cancel(); }
    @UsedByGodot public int availability() {
        Activity a=getActivity();
        if(Build.VERSION.SDK_INT<30 || a==null)return 0;
        BiometricManager manager=a.getSystemService(BiometricManager.class);
        return manager!=null && manager.canAuthenticate(BiometricManager.Authenticators.BIOMETRIC_STRONG)==BiometricManager.BIOMETRIC_SUCCESS?3:0;
    }
    private AtomicFile storage() {
        return new AtomicFile(new File(getActivity().getNoBackupFilesDir(),"ngarage-credentials-v1.json"));
    }
    private KeyStore keys() throws Exception { KeyStore k=KeyStore.getInstance("AndroidKeyStore");k.load(null);return k; }
    private boolean validAlias(String alias) {return alias!=null && alias.startsWith(PREFIX) && alias.length()<=PREFIX.length()+36;}
    private JSONObject readEnvelope() throws Exception {
        byte[] bytes=storage().readFully();
        if(bytes.length>131072)throw new Exception("invalid_data");
        JSONObject j=new JSONObject(new String(bytes,StandardCharsets.UTF_8));
        if(j.getInt("version")!=1 || !validAlias(j.getString("alias")))throw new Exception("invalid_data");
        return j;
    }
    @UsedByGodot public boolean hasAccount() {
        try {return storage().getBaseFile().exists() || new File(storage().getBaseFile().getPath()+".bak").exists();}catch(Exception e){return false;}
    }
    @UsedByGodot public String takeResult() {synchronized(guard){String r=result;result="";return r;}}
    private void erasePending() {
        if(pendingPlain!=null){Arrays.fill(pendingPlain,(byte)0);pendingPlain=null;}
        if(pendingAlias!=null){try{keys().deleteEntry(pendingAlias);}catch(Exception ignored){}pendingAlias=null;}
    }
    @UsedByGodot public void cancel() {
        CancellationSignal old;
        synchronized(guard){generation++;busy=false;result="";old=cancellation;cancellation=null;erasePending();}
        if(old!=null)old.cancel();
    }
    private void finish(long ticket,boolean ok,String code,String data) {
        synchronized(guard){
            if(ticket!=generation || !busy)return;
            try{JSONObject j=new JSONObject();j.put("ok",ok);j.put("code",code);if(data!=null)j.put("data",data);result=j.toString();}
            catch(Exception ignored){result="{\"ok\":false,\"code\":\"storage_failed\"}";}
            busy=false;cancellation=null;erasePending();
        }
    }
    @UsedByGodot public void start(String operation,String payload) {
        final long ticket;
        synchronized(guard){if(busy)return;ticket=++generation;busy=true;result="";}
        Activity activity=getActivity();
        if(activity==null){finish(ticket,false,"unavailable",null);return;}
        activity.runOnUiThread(new Runnable(){@Override public void run(){
            synchronized(guard){
                if(ticket!=generation || !busy)return;
                if(operation.equals("forget")) {
                    try {
                        // Delete only aliases belonging to this app's vault, including orphaned rotations.
                        KeyStore k=keys();java.util.Enumeration<String> aliases=k.aliases();
                        while(aliases.hasMoreElements()){String alias=aliases.nextElement();if(validAlias(alias))k.deleteEntry(alias);}
                        storage().delete();
                        finish(ticket,!storage().getBaseFile().exists(),"storage_failed",null);
                    }catch(Exception e){finish(ticket,false,"storage_failed",null);}return;
                }
                if(availability()==0){finish(ticket,false,"unavailable",null);return;}
                if(!operation.equals("save")&&!operation.equals("unlock")){finish(ticket,false,"invalid_data",null);return;}
                try {
                    final boolean saving=operation.equals("save");
                    final String alias;
                    final String previous;
                    final byte[] encrypted;
                    final Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");
                    KeyStore k=keys();
                    if(saving) {
                        if(payload.length()>65536){finish(ticket,false,"invalid_data",null);return;}
                        alias=PREFIX+UUID.randomUUID();pendingAlias=alias;
                        String old=null;try{old=readEnvelope().getString("alias");}catch(Exception ignored){}previous=old;
                        KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
                        generator.init(new KeyGenParameterSpec.Builder(alias,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT)
                            .setKeySize(256).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                            .setUserAuthenticationRequired(true)
                            .setUserAuthenticationParameters(0,KeyProperties.AUTH_BIOMETRIC_STRONG)
                            .setInvalidatedByBiometricEnrollment(true).build());
                        SecretKey key=generator.generateKey();cipher.init(Cipher.ENCRYPT_MODE,key);
                        pendingPlain=payload.getBytes(StandardCharsets.UTF_8);encrypted=null;
                    }else{
                        if(!hasAccount()){finish(ticket,false,"missing",null);return;}
                        JSONObject envelope=readEnvelope();alias=envelope.getString("alias");previous=null;
                        SecretKey key=(SecretKey)k.getKey(alias,null);
                        if(key==null){finish(ticket,false,"invalidated",null);return;}
                        byte[] iv=Base64.decode(envelope.getString("iv"),Base64.NO_WRAP);
                        if(iv.length!=12){finish(ticket,false,"invalid_data",null);return;}
                        encrypted=Base64.decode(envelope.getString("ciphertext"),Base64.NO_WRAP);
                        cipher.init(Cipher.DECRYPT_MODE,key,new GCMParameterSpec(128,iv));
                    }
                    cancellation=new CancellationSignal();
                    BiometricPrompt prompt=new BiometricPrompt.Builder(activity)
                        .setTitle(saving?"nCTRL: Save Hyundai account":"nCTRL: Unlock Hyundai account")
                        .setSubtitle("Use your device's secure biometrics")
                        .setAllowedAuthenticators(BiometricManager.Authenticators.BIOMETRIC_STRONG)
                        .setNegativeButton("Cancel",activity.getMainExecutor(),new DialogInterface.OnClickListener(){@Override public void onClick(DialogInterface dialog,int which){finish(ticket,false,"cancelled",null);}}).build();
                    prompt.authenticate(new BiometricPrompt.CryptoObject(cipher),cancellation,activity.getMainExecutor(),new BiometricPrompt.AuthenticationCallback(){
                        @Override public void onAuthenticationError(int code,CharSequence message) {
                            finish(ticket,false,code==BiometricPrompt.BIOMETRIC_ERROR_USER_CANCELED||code==BiometricPrompt.BIOMETRIC_ERROR_CANCELED?"cancelled":"auth_failed",null);
                        }
                        @Override public void onAuthenticationSucceeded(BiometricPrompt.AuthenticationResult auth) {
                            synchronized(guard){
                                if(ticket!=generation || !busy)return;
                                byte[] plain=null;
                                try {
                                    Cipher unlocked=auth.getCryptoObject()==null?null:auth.getCryptoObject().getCipher();
                                    if(unlocked==null || unlocked!=cipher){finish(ticket,false,"auth_failed",null);return;}
                                    unlocked.updateAAD(AAD);
                                    if(saving) {
                                        byte[] ciphertext=unlocked.doFinal(pendingPlain);
                                        JSONObject envelope=new JSONObject();envelope.put("version",1);envelope.put("alias",alias);
                                        envelope.put("iv",Base64.encodeToString(unlocked.getIV(),Base64.NO_WRAP));
                                        envelope.put("ciphertext",Base64.encodeToString(ciphertext,Base64.NO_WRAP));
                                        AtomicFile store=storage();FileOutputStream output=null;
                                        try{output=store.startWrite();output.write(envelope.toString().getBytes(StandardCharsets.UTF_8));store.finishWrite(output);}
                                        catch(Exception e){if(output!=null)store.failWrite(output);throw e;}
                                        if(!readEnvelope().getString("alias").equals(alias))throw new Exception("storage_failed");
                                        pendingAlias=null; // now referenced by the committed encrypted file
                                        if(validAlias(previous))try{keys().deleteEntry(previous);}catch(Exception ignored){}
                                        finish(ticket,true,"",null);
                                    }else{
                                        plain=unlocked.doFinal(encrypted);
                                        finish(ticket,true,"",new String(plain,StandardCharsets.UTF_8));
                                    }
                                }catch(Exception e){finish(ticket,false,"storage_failed",null);}
                                finally{if(plain!=null)Arrays.fill(plain,(byte)0);}
                            }
                        }
                    });
                }catch(KeyPermanentlyInvalidatedException e){finish(ticket,false,"invalidated",null);}
                catch(Exception e){finish(ticket,false,"storage_failed",null);}
            }
        }});
    }
}
