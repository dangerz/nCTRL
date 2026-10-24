#import <Foundation/Foundation.h>
#import <Security/Security.h>
#import <LocalAuthentication/LocalAuthentication.h>
#import <UIKit/UIKit.h>
#import <Photos/Photos.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

// C ABI bridge: no managed callbacks or credential logging. All results are consumed once.
#define NG_EXPORT extern "C" __attribute__((visibility("default"), used))
// No canOpenURL probe/Info.plist allowlist needed: the completion callback tells
// us whether the system could dispatch to its default navigation application.
NG_EXPORT void ng_vault_open_map(double latitude, double longitude) {
    if (!isfinite(latitude) || !isfinite(longitude) || fabs(latitude)>90 || fabs(longitude)>180) return;
    dispatch_async(dispatch_get_main_queue(), ^{
        NSString *point=[[NSString alloc] initWithFormat:@"%.8f,%.8f"
            locale:[NSLocale localeWithLocaleIdentifier:@"en_US_POSIX"],latitude,longitude];
        NSURL *preferred=[NSURL URLWithString:[@"geo-navigation:///place?coordinate=" stringByAppendingString:point]];
        NSURL *fallback=[NSURL URLWithString:[NSString stringWithFormat:@"https://maps.apple.com/?ll=%@&q=Vehicle&z=16",point]];
        [[UIApplication sharedApplication] openURL:preferred options:@{} completionHandler:^(BOOL opened) {
            if (!opened) dispatch_async(dispatch_get_main_queue(), ^{
                [[UIApplication sharedApplication] openURL:fallback options:@{} completionHandler:nil];
            });
        }];
    });
}
// Saves one Surround View picture to the photo library with add-only access (nCTRL never reads the library).
// Status: 0 idle, 1 saving, 2 saved, 3 access not allowed, 4 failed.
static volatile int photoStatus;
NG_EXPORT void ng_photo_save(const unsigned char *bytes, int length) {
    if(!bytes || length<=0 || length>16*1024*1024){photoStatus=4;return;}
    NSData *data=[NSData dataWithBytes:bytes length:(NSUInteger)length];
    photoStatus=1;
    [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelAddOnly handler:^(PHAuthorizationStatus status) {
        if(status!=PHAuthorizationStatusAuthorized && status!=PHAuthorizationStatusLimited){photoStatus=3;return;}
        [[PHPhotoLibrary sharedPhotoLibrary] performChanges:^{
            [[PHAssetCreationRequest creationRequestForAsset] addResourceWithType:PHAssetResourceTypePhoto data:data options:nil];
        } completionHandler:^(BOOL success, NSError *error) {photoStatus=success?2:4;}];
    }];
}
NG_EXPORT int ng_photo_status(void) {return photoStatus;}
static NSObject *guard;
static NSData *resultData;
static LAContext *activeContext;
static NSUInteger generation;
static BOOL busy;
static NSMutableArray<UIView *> *privacyViews;
static NSMutableArray *observers;
static void installPrivacyCover(void);
static NSString *const service=@"com.ngarage.bluelink.credentials.v1";
static NSMutableDictionary *query(void) {
    return [@{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
              (__bridge id)kSecAttrService:service,(__bridge id)kSecAttrAccount:@"account",
              (__bridge id)kSecAttrSynchronizable:@NO} mutableCopy];
}
// Ignored places (car locations where an unlocked-doors alert is hidden). Sensitive, so Keychain only:
// device-bound, never synced, readable while the device is unlocked, no biometric gate.
static NSMutableDictionary *placesQuery(void) {
    return [@{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
              (__bridge id)kSecAttrService:@"com.ngarage.nctrl.places.v1",(__bridge id)kSecAttrAccount:@"places",
              (__bridge id)kSecAttrSynchronizable:@NO} mutableCopy];
}
// The car's last known location, stored the same way. The app shows it only after Face ID.
static NSMutableDictionary *locationQuery(void) {
    return [@{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
              (__bridge id)kSecAttrService:@"com.ngarage.nctrl.location.v1",(__bridge id)kSecAttrAccount:@"location",
              (__bridge id)kSecAttrSynchronizable:@NO} mutableCopy];
}
static void initialize(void) {
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        guard=[NSObject new];privacyViews=[NSMutableArray new];observers=[NSMutableArray new];
        installPrivacyCover();
        // Keychain items may survive uninstall. Clear an orphan on a fresh install.
        NSUserDefaults *defaults=NSUserDefaults.standardUserDefaults;
        if(![defaults boolForKey:@"NGVaultInstalledV1"]) {
            SecItemDelete((__bridge CFDictionaryRef)query());
            SecItemDelete((__bridge CFDictionaryRef)placesQuery());
            SecItemDelete((__bridge CFDictionaryRef)locationQuery());
            [defaults setBool:YES forKey:@"NGVaultInstalledV1"];
        }
    });
}
static void finish(NSUInteger ticket, BOOL ok, NSString *code, NSString *data) {
    @synchronized(guard) {
        if(ticket!=generation || !busy)return;
        NSMutableDictionary *r=[@{@"ok":@(ok),@"code":code?:@""} mutableCopy];
        if(data)r[@"data"]=data;
        resultData=[NSJSONSerialization dataWithJSONObject:r options:0 error:nil];
        busy=NO;activeContext=nil;
    }
}
// Biometric UI can only appear while the app is active. A resume can start a request a moment
// before UIKit finishes activating, so wait for activation instead of failing as not interactive.
static void whenActive(void (^work)(void)) {
    dispatch_async(dispatch_get_main_queue(), ^{
        if(UIApplication.sharedApplication.applicationState==UIApplicationStateActive){work();return;}
        __block id token=nil;
        token=[NSNotificationCenter.defaultCenter addObserverForName:UIApplicationDidBecomeActiveNotification object:nil
            queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note){
                [NSNotificationCenter.defaultCenter removeObserver:token];token=nil;work();
            }];
    });
}
static NSString *errorCode(OSStatus status) {
    if(status==errSecUserCanceled)return @"cancelled";
    if(status==errSecItemNotFound)return @"missing";
    if(status==errSecAuthFailed || status==errSecInteractionNotAllowed)return @"auth_failed";
    return @"storage_failed";
}
NG_EXPORT int ng_vault_availability(void) {
    initialize();LAContext *ctx=[LAContext new];NSError *error=nil;
    // Report packaging separately; never start Face ID without its required usage text.
    id usage=[NSBundle.mainBundle objectForInfoDictionaryKey:@"NSFaceIDUsageDescription"];
    if(![usage isKindOfClass:NSString.class] || [(NSString *)usage length]==0)return -4;
    if(![ctx canEvaluatePolicy:LAPolicyDeviceOwnerAuthenticationWithBiometrics error:&error]) {
        switch(error.code) {
            case LAErrorPasscodeNotSet:return -1;
            case LAErrorBiometryNotEnrolled:return -2;
            case LAErrorBiometryLockout:return -3;
            default:return 0;
        }
    }
    return ctx.biometryType==LABiometryTypeFaceID?2:ctx.biometryType==LABiometryTypeTouchID?1:0;
}
NG_EXPORT int ng_vault_has_account(void) {
    initialize();NSMutableDictionary *q=query();
    q[(__bridge id)kSecReturnAttributes]=@YES;
    q[(__bridge id)kSecUseAuthenticationUI]=(__bridge id)kSecUseAuthenticationUIFail;
    CFTypeRef found=NULL;OSStatus status=SecItemCopyMatching((__bridge CFDictionaryRef)q,&found);
    if(found)CFRelease(found);
    return status==errSecSuccess || status==errSecInteractionNotAllowed;
}
NG_EXPORT void ng_vault_cancel(void) {
    initialize();LAContext *ctx;
    @synchronized(guard){generation++;busy=NO;resultData=nil;ctx=activeContext;activeContext=nil;}
    [ctx invalidate];
}
NG_EXPORT char *ng_vault_take_result(void) {
    initialize();@synchronized(guard){
        if(!resultData)return NULL;
        NSUInteger size=resultData.length;char *copy=(char *)calloc(size+1,1);
        if(copy)memcpy(copy,resultData.bytes,size);
        resultData=nil;return copy;
    }
}
NG_EXPORT void ng_vault_free(char *p) {
    if(!p)return;volatile char *v=p;size_t n=strlen(p);while(n--)*v++=0;free(p);
}
NG_EXPORT void ng_vault_start(const char *operation, const char *payload) {
    initialize();NSString *op=operation?[NSString stringWithUTF8String:operation]:@"";
    NSString *text=payload?[NSString stringWithUTF8String:payload]:@"";
    NSUInteger ticket;
    @synchronized(guard){if(busy)return;ticket=++generation;busy=YES;resultData=nil;}
    if([op isEqualToString:@"forget"]) {
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0), ^{
            @synchronized(guard){if(ticket!=generation)return;
                OSStatus status=SecItemDelete((__bridge CFDictionaryRef)query());
                finish(ticket,status==errSecSuccess||status==errSecItemNotFound,errorCode(status),nil);
            }
        });return;
    }
    BOOL places=[op hasPrefix:@"places_"];
    if(places||[op hasPrefix:@"location_"]) {
        NSString *kind=places?@"places_":@"location_";
        NSMutableDictionary *(*itemQuery)(void)=places?placesQuery:locationQuery;
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0), ^{
            @synchronized(guard){if(ticket!=generation)return;
                if([op isEqualToString:[kind stringByAppendingString:@"load"]]) {
                    NSMutableDictionary *q=itemQuery();q[(__bridge id)kSecReturnData]=@YES;
                    CFTypeRef value=NULL;OSStatus status=SecItemCopyMatching((__bridge CFDictionaryRef)q,&value);
                    NSData *data=value?CFBridgingRelease(value):nil;
                    if(status==errSecItemNotFound){finish(ticket,YES,@"",@"");return;}
                    NSString *stored=status==errSecSuccess?[[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding]:nil;
                    finish(ticket,stored!=nil,stored?@"":errorCode(status),stored);
                } else if([op isEqualToString:[kind stringByAppendingString:@"save"]]) {
                    if(text.length>65536){finish(ticket,NO,@"invalid_data",nil);return;}
                    SecItemDelete((__bridge CFDictionaryRef)itemQuery());
                    NSMutableDictionary *item=itemQuery();
                    item[(__bridge id)kSecValueData]=[text dataUsingEncoding:NSUTF8StringEncoding];
                    item[(__bridge id)kSecAttrAccessible]=(__bridge id)kSecAttrAccessibleWhenUnlockedThisDeviceOnly;
                    OSStatus status=SecItemAdd((__bridge CFDictionaryRef)item,NULL);
                    finish(ticket,status==errSecSuccess,errorCode(status),nil);
                } else if([op isEqualToString:[kind stringByAppendingString:@"forget"]]) {
                    OSStatus status=SecItemDelete((__bridge CFDictionaryRef)itemQuery());
                    finish(ticket,status==errSecSuccess||status==errSecItemNotFound,errorCode(status),nil);
                } else finish(ticket,NO,@"invalid_data",nil);
            }
        });
        return;
    }
    if([op isEqualToString:@"authenticate"]) {
        // Device-owner check only: Face ID/Touch ID with the system passcode fallback. Touches no Keychain data.
        initialize();LAContext *ctx=[LAContext new];NSError *error=nil;
        id usage=[NSBundle.mainBundle objectForInfoDictionaryKey:@"NSFaceIDUsageDescription"];
        if(![usage isKindOfClass:NSString.class] || [(NSString *)usage length]==0){finish(ticket,NO,@"unavailable",nil);return;}
        if(text.length==0 || text.length>200){finish(ticket,NO,@"invalid_data",nil);return;}
        if(![ctx canEvaluatePolicy:LAPolicyDeviceOwnerAuthentication error:&error]){finish(ticket,NO,@"unavailable",nil);return;}
        ctx.touchIDAuthenticationAllowableReuseDuration=0;
        @synchronized(guard){if(ticket!=generation)return;activeContext=ctx;}
        whenActive(^{
            @synchronized(guard){if(ticket!=generation)return;}
            [ctx evaluatePolicy:LAPolicyDeviceOwnerAuthentication localizedReason:text reply:^(BOOL success,NSError *error){
                finish(ticket,success,success?@"":error.code==LAErrorUserCancel||error.code==LAErrorAppCancel||error.code==LAErrorSystemCancel?@"cancelled":@"auth_failed",nil);
            }];
        });
        return;
    }
    if(ng_vault_availability()<=0){finish(ticket,NO,@"unavailable",nil);return;}
    if(![op isEqualToString:@"save"] && ![op isEqualToString:@"unlock"]){finish(ticket,NO,@"invalid_data",nil);return;}
    if(text.length>65536){finish(ticket,NO,@"invalid_data",nil);return;}
    LAContext *ctx=[LAContext new];ctx.localizedFallbackTitle=@"";ctx.touchIDAuthenticationAllowableReuseDuration=0;
    @synchronized(guard){if(ticket!=generation)return;activeContext=ctx;}
    if([op isEqualToString:@"unlock"]) {
        // The ACL on the item itself requires biometry; there is no separate boolean gate.
        whenActive(^{dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0), ^{
            @synchronized(guard){if(ticket!=generation)return;}
            NSMutableDictionary *q=query();q[(__bridge id)kSecReturnData]=@YES;
            q[(__bridge id)kSecUseAuthenticationContext]=ctx;
            q[(__bridge id)kSecUseOperationPrompt]=@"Unlock your saved Hyundai account in nCTRL";
            CFTypeRef value=NULL;OSStatus status=SecItemCopyMatching((__bridge CFDictionaryRef)q,&value);
            NSData *data=value?CFBridgingRelease(value):nil;
            NSString *account=status==errSecSuccess?[[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding]:nil;
            finish(ticket,status==errSecSuccess && account!=nil,status==errSecSuccess?@"":errorCode(status),account);
        });});return;
    }
    // Verify the owner before enabling persistence, then bind access to the current biometrics.
    dispatch_async(dispatch_get_main_queue(), ^{
        [ctx evaluatePolicy:LAPolicyDeviceOwnerAuthenticationWithBiometrics
            localizedReason:@"Save your Hyundai account securely in nCTRL on this iPhone"
            reply:^(BOOL success,NSError *error){
                if(!success){finish(ticket,NO,error.code==LAErrorUserCancel||error.code==LAErrorAppCancel||error.code==LAErrorSystemCancel?@"cancelled":@"auth_failed",nil);return;}
                dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0), ^{
                    CFErrorRef aclError=NULL;
                    SecAccessControlRef acl=SecAccessControlCreateWithFlags(kCFAllocatorDefault,
                        kSecAttrAccessibleWhenPasscodeSetThisDeviceOnly,kSecAccessControlBiometryCurrentSet,&aclError);
                    if(!acl){if(aclError)CFRelease(aclError);finish(ticket,NO,@"storage_failed",nil);return;}
                    NSDictionary *attributes=@{(__bridge id)kSecValueData:[text dataUsingEncoding:NSUTF8StringEncoding],
                        (__bridge id)kSecAttrAccessControl:(__bridge id)acl};
                    @synchronized(guard){
                        if(ticket==generation && busy) {
                            NSMutableDictionary *q=query();q[(__bridge id)kSecUseAuthenticationContext]=ctx;
                            OSStatus status=SecItemUpdate((__bridge CFDictionaryRef)q,(__bridge CFDictionaryRef)attributes);
                            if(status==errSecItemNotFound){
                                NSMutableDictionary *item=query();[item addEntriesFromDictionary:attributes];
                                item[(__bridge id)kSecUseAuthenticationContext]=ctx;
                                status=SecItemAdd((__bridge CFDictionaryRef)item,NULL);
                            }
                            finish(ticket,status==errSecSuccess,errorCode(status),nil);
                        }
                    }
                    CFRelease(acl);
                });
            }];
    });
}
// All cover bookkeeping runs on the main queue. Scene and app events can overlap.
static void hidePrivacyCover(UIScene *scene) {
    for(UIView *cover in [privacyViews copy]) {
        UIWindowScene *owner=cover.window.windowScene;
        if((scene && owner==scene) || (!scene && (!owner || owner.activationState==UISceneActivationStateForegroundActive))) {
            [cover removeFromSuperview];[privacyViews removeObject:cover];
        }
    }
}
static void showPrivacyCover(UIScene *onlyScene) {
    for(UIScene *scene in UIApplication.sharedApplication.connectedScenes)if([scene isKindOfClass:UIWindowScene.class] && (!onlyScene || scene==onlyScene)) {
        for(UIWindow *window in ((UIWindowScene *)scene).windows)if(window.isKeyWindow) {
            BOOL covered=NO;
            for(UIView *cover in privacyViews)if(cover.superview==window){covered=YES;break;}
            if(covered)continue;
            UIView *cover=[[UIView alloc] initWithFrame:window.bounds];cover.backgroundColor=UIColor.blackColor;
            cover.autoresizingMask=UIViewAutoresizingFlexibleWidth|UIViewAutoresizingFlexibleHeight;
            [window addSubview:cover];[privacyViews addObject:cover];
        }
    }
}
NG_EXPORT void ng_vault_resume(void) {
    initialize();dispatch_async(dispatch_get_main_queue(), ^{
        // Never uncover a background scene. An activation observer handles a later activation.
        hidePrivacyCover(nil);
    });
}
static void installPrivacyCover(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        if(observers.count>0)return;
        NSNotificationCenter *nc=NSNotificationCenter.defaultCenter;
        id hide=[nc addObserverForName:UIApplicationWillResignActiveNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note){
            showPrivacyCover(nil);
        }];
        id reveal=[nc addObserverForName:UIApplicationDidBecomeActiveNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note){
            hidePrivacyCover(nil);
        }];
        id sceneHide=[nc addObserverForName:UISceneWillDeactivateNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note){
            showPrivacyCover((UIScene *)note.object);
        }];
        id sceneReveal=[nc addObserverForName:UISceneDidActivateNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note){
            hidePrivacyCover((UIScene *)note.object);
        }];
        [observers addObjectsFromArray:@[hide,reveal,sceneHide,sceneReveal]];
    });
}
// Retained for older .gdip consumers; the AOT bridge initializes itself on first use.
void ng_vault_init(void) { initialize(); }
void ng_vault_deinit(void) {
    ng_vault_cancel();dispatch_async(dispatch_get_main_queue(), ^{
        for(id observer in observers)[NSNotificationCenter.defaultCenter removeObserver:observer];[observers removeAllObjects];
        for(UIView *cover in privacyViews)[cover removeFromSuperview];[privacyViews removeAllObjects];
    });
}
