#import <Foundation/Foundation.h>
#import <Security/Security.h>
#import <LocalAuthentication/LocalAuthentication.h>
#import <UIKit/UIKit.h>
#import <Photos/Photos.h>
#import <CoreLocation/CoreLocation.h>
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
// The phone's own position, for the map only: asked for while the map is open, never stored or sent.
// State: 0 none, 1 locating, 2 located, 3 not allowed, 4 failed, 5 usage text missing.
@interface NGPhoneLocation : NSObject <CLLocationManagerDelegate>
@end
static CLLocationManager *phoneManager;
static NGPhoneLocation *phoneDelegate;
static volatile int phoneState;
static double phoneLatitude,phoneLongitude,phoneAccuracy;
@implementation NGPhoneLocation
- (void)locationManagerDidChangeAuthorization:(CLLocationManager *)manager {
    CLAuthorizationStatus status=manager.authorizationStatus;
    if(status==kCLAuthorizationStatusAuthorizedWhenInUse || status==kCLAuthorizationStatusAuthorizedAlways){if(phoneState==1)[manager requestLocation];}
    else if(status!=kCLAuthorizationStatusNotDetermined)phoneState=3;
}
- (void)locationManager:(CLLocationManager *)manager didUpdateLocations:(NSArray<CLLocation *> *)locations {
    CLLocation *last=locations.lastObject;if(!last)return;
    phoneLatitude=last.coordinate.latitude;phoneLongitude=last.coordinate.longitude;phoneAccuracy=last.horizontalAccuracy;phoneState=2;
}
- (void)locationManager:(CLLocationManager *)manager didFailWithError:(NSError *)error {
    phoneState=error.code==kCLErrorDenied?3:phoneState==2?2:4;
}
@end
NG_EXPORT void ng_phone_locate(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        id usage=[NSBundle.mainBundle objectForInfoDictionaryKey:@"NSLocationWhenInUseUsageDescription"];
        if(![usage isKindOfClass:NSString.class] || [(NSString *)usage length]==0){phoneState=5;return;}
        if(!phoneManager){phoneManager=[CLLocationManager new];phoneDelegate=[NGPhoneLocation new];
            phoneManager.delegate=phoneDelegate;phoneManager.desiredAccuracy=kCLLocationAccuracyNearestTenMeters;}
        CLAuthorizationStatus status=phoneManager.authorizationStatus;
        if(status==kCLAuthorizationStatusDenied || status==kCLAuthorizationStatusRestricted){phoneState=3;return;}
        if(phoneState!=2)phoneState=1;
        if(status==kCLAuthorizationStatusNotDetermined)[phoneManager requestWhenInUseAuthorization];
        else [phoneManager requestLocation];
    });
}
NG_EXPORT int ng_phone_location(double *latitude, double *longitude, double *accuracy) {
    if(phoneState==2 && latitude && longitude && accuracy){*latitude=phoneLatitude;*longitude=phoneLongitude;*accuracy=phoneAccuracy;}
    return phoneState;
}
// Address lookup for Send to car, with Apple's geocoder. State: 0 none, 1 looking, 2 found, 3 not found, 4 failed.
// The result is JSON (name, address, zip, lat, lon), taken once with ng_geocode_result and freed with ng_vault_free.
static CLGeocoder *geocoder;
static volatile int geoState;
static NSData *geoResult;
NG_EXPORT void ng_geocode(const char *query) {
    NSString *text=query?[NSString stringWithUTF8String:query]:nil;
    if(text.length==0 || text.length>300){geoState=3;return;}
    geoState=1;
    dispatch_async(dispatch_get_main_queue(), ^{
        if(!geocoder)geocoder=[CLGeocoder new];
        [geocoder cancelGeocode];
        [geocoder geocodeAddressString:text completionHandler:^(NSArray<CLPlacemark *> *marks, NSError *error) {
            CLPlacemark *m=marks.firstObject;
            if(!m || !m.location){geoState=(!error || error.code==kCLErrorGeocodeFoundNoResult)?3:4;return;}
            NSString *street=m.thoroughfare?(m.subThoroughfare?[NSString stringWithFormat:@"%@ %@",m.subThoroughfare,m.thoroughfare]:m.thoroughfare):nil;
            NSMutableArray *parts=[NSMutableArray new];
            if(street)[parts addObject:street];
            if(m.locality)[parts addObject:m.locality];
            NSString *region=[[NSString stringWithFormat:@"%@ %@",m.administrativeArea?:@"",m.postalCode?:@""]
                stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceCharacterSet];
            if(region.length)[parts addObject:region];
            NSDictionary *place=@{@"name":m.name?:street?:text,@"address":[parts componentsJoinedByString:@", "],@"zip":m.postalCode?:@"",
                @"lat":@(m.location.coordinate.latitude),@"lon":@(m.location.coordinate.longitude)};
            @synchronized(NSNull.null){geoResult=[NSJSONSerialization dataWithJSONObject:place options:0 error:nil];}
            geoState=geoResult?2:4;
        }];
    });
}
NG_EXPORT char *ng_geocode_result(int *state) {
    if(state)*state=geoState;
    if(geoState!=2)return NULL;
    @synchronized(NSNull.null){
        if(!geoResult)return NULL;
        char *copy=(char *)calloc(geoResult.length+1,1);
        if(copy)memcpy(copy,geoResult.bytes,geoResult.length);
        geoResult=nil;geoState=0;return copy;
    }
}
// Files shared with the widget and the Share extension, in the App Group "group.<app bundle ID>".
// The App Group ("group.<app bundle ID>"), or nil in a build without it. Its name is also a Keychain access group
// the Share extension can read: the saved login and Hyundai's session are kept there.
static NSString *appGroup(void) {
    static NSString *group;static dispatch_once_t once;
    dispatch_once(&once, ^{
        NSString *name=[@"group." stringByAppendingString:NSBundle.mainBundle.bundleIdentifier?:@""];
        if([NSFileManager.defaultManager containerURLForSecurityApplicationGroupIdentifier:name])group=name;
    });
    return group;
}
static NSURL *groupFile(NSString *name) {
    if(name.length==0 || [name containsString:@"/"] || [name hasPrefix:@"."] || !appGroup())return nil;
    NSURL *folder=[NSFileManager.defaultManager containerURLForSecurityApplicationGroupIdentifier:appGroup()];
    return folder?[folder URLByAppendingPathComponent:name]:nil;
}
// The App Group folder's path (for the shared request limits), freed with ng_vault_free; NULL without one.
NG_EXPORT char *ng_group_path(void) {
    NSURL *folder=appGroup()?[NSFileManager.defaultManager containerURLForSecurityApplicationGroupIdentifier:appGroup()]:nil;
    const char *path=folder.path.UTF8String;
    return path?strdup(path):NULL;
}
extern "C" void ng_widget_reload_swift(void);
// Writes (or, with no bytes, deletes) one shared file, then asks the widget to redraw. Returns 1 when done.
NG_EXPORT int ng_group_write(const char *name, const unsigned char *bytes, int length) {
    NSURL *file=groupFile(name?[NSString stringWithUTF8String:name]:@"");
    if(!file || length<0 || length>4*1024*1024)return 0;
    BOOL ok;
    if(!bytes || length==0){NSError *error=nil;ok=[NSFileManager.defaultManager removeItemAtURL:file error:&error] || error.code==NSFileNoSuchFileError;}
    else ok=[[NSData dataWithBytes:bytes length:(NSUInteger)length] writeToURL:file options:NSDataWritingAtomic|NSDataWritingFileProtectionCompleteUntilFirstUserAuthentication error:nil];
    if(ok)dispatch_async(dispatch_get_main_queue(), ^{ng_widget_reload_swift();});
    return ok?1:0;
}
// A file written by the Share extension (the shared place, its receipts), taken once: returned as UTF-8 and
// deleted. Freed with ng_vault_free. NULL when there is none.
NG_EXPORT char *ng_group_take(const char *name) {
    NSURL *file=groupFile(name?[NSString stringWithUTF8String:name]:@"");
    NSData *data=file?[NSData dataWithContentsOfURL:file]:nil;
    if(file)[NSFileManager.defaultManager removeItemAtURL:file error:nil];
    if(data.length==0 || data.length>256*1024)return NULL;
    char *copy=(char *)calloc(data.length+1,1);
    if(copy)memcpy(copy,data.bytes,data.length);
    return copy;
}
static NSObject *guard;
static NSData *resultData;
static LAContext *activeContext;
static NSUInteger generation;
static BOOL busy;
static NSMutableArray<UIView *> *privacyViews;
static NSMutableArray *observers;
static void installPrivacyCover(void);
// The saved login: device-bound, never synced, readable while the device is unlocked, no Face ID gate
// (like the Hyundai app). Older builds kept it behind Face ID in the legacy item; the first unlock moves
// it here with one Face ID, then deletes the legacy item.
static NSMutableDictionary *query(void) {
    return [@{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
              (__bridge id)kSecAttrService:@"com.ngarage.nctrl.account.v2",(__bridge id)kSecAttrAccount:@"account",
              (__bridge id)kSecAttrSynchronizable:@NO} mutableCopy];
}
static NSMutableDictionary *legacyQuery(void) {
    return [@{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
              (__bridge id)kSecAttrService:@"com.ngarage.bluelink.credentials.v1",(__bridge id)kSecAttrAccount:@"account",
              (__bridge id)kSecAttrSynchronizable:@NO} mutableCopy];
}
// Ignored places (car locations where an unlocked-doors alert is hidden). Sensitive, so Keychain only:
// device-bound, never synced, readable while the device is unlocked, no biometric gate.
static NSMutableDictionary *placesQuery(void) {
    return [@{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
              (__bridge id)kSecAttrService:@"com.ngarage.nctrl.places.v1",(__bridge id)kSecAttrAccount:@"places",
              (__bridge id)kSecAttrSynchronizable:@NO} mutableCopy];
}
// The car's last known location, stored the same way.
static NSMutableDictionary *locationQuery(void) {
    return [@{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
              (__bridge id)kSecAttrService:@"com.ngarage.nctrl.location.v1",(__bridge id)kSecAttrAccount:@"location",
              (__bridge id)kSecAttrSynchronizable:@NO} mutableCopy];
}
// Hyundai's session (tokens and the selected car), shared with the Share extension through the App Group's Keychain
// access group so it can send without signing in again. Device-only, no Face ID gate.
static NSMutableDictionary *sessionQuery(void) {
    NSMutableDictionary *q=[@{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
              (__bridge id)kSecAttrService:@"com.ngarage.nctrl.session.v1",(__bridge id)kSecAttrAccount:@"session",
              (__bridge id)kSecAttrSynchronizable:@NO} mutableCopy];
    if(appGroup())q[(__bridge id)kSecAttrAccessGroup]=appGroup();
    return q;
}
// Places shared to nCTRL from other apps (Activity → Shared), stored the same way.
static NSMutableDictionary *sharedQuery(void) {
    return [@{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
              (__bridge id)kSecAttrService:@"com.ngarage.nctrl.shared.v1",(__bridge id)kSecAttrAccount:@"shared",
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
            SecItemDelete((__bridge CFDictionaryRef)legacyQuery());
            SecItemDelete((__bridge CFDictionaryRef)placesQuery());
            SecItemDelete((__bridge CFDictionaryRef)locationQuery());
            SecItemDelete((__bridge CFDictionaryRef)sharedQuery());
            SecItemDelete((__bridge CFDictionaryRef)sessionQuery());
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
static BOOL itemExists(NSMutableDictionary *q) {
    q[(__bridge id)kSecReturnAttributes]=@YES;
    q[(__bridge id)kSecUseAuthenticationUI]=(__bridge id)kSecUseAuthenticationUIFail;
    CFTypeRef found=NULL;OSStatus status=SecItemCopyMatching((__bridge CFDictionaryRef)q,&found);
    if(found)CFRelease(found);
    return status==errSecSuccess || status==errSecInteractionNotAllowed;
}
NG_EXPORT int ng_vault_has_account(void) {
    initialize();return itemExists(query()) || itemExists(legacyQuery());
}
// Writes the saved login to its item (replacing any) and removes the legacy Face ID item.
// In the App Group's access group when there is one, so the Share extension can read it too.
static OSStatus storeAccount(NSData *data) {
    SecItemDelete((__bridge CFDictionaryRef)query());
    NSMutableDictionary *item=query();
    if(appGroup())item[(__bridge id)kSecAttrAccessGroup]=appGroup();
    item[(__bridge id)kSecValueData]=data;
    item[(__bridge id)kSecAttrAccessible]=(__bridge id)kSecAttrAccessibleWhenUnlockedThisDeviceOnly;
    OSStatus status=SecItemAdd((__bridge CFDictionaryRef)item,NULL);
    if(status==errSecSuccess)SecItemDelete((__bridge CFDictionaryRef)legacyQuery());
    return status;
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
                OSStatus legacy=SecItemDelete((__bridge CFDictionaryRef)legacyQuery());
                BOOL ok=(status==errSecSuccess||status==errSecItemNotFound)&&(legacy==errSecSuccess||legacy==errSecItemNotFound);
                finish(ticket,ok,errorCode(status==errSecSuccess||status==errSecItemNotFound?legacy:status),nil);
            }
        });return;
    }
    NSString *kind=nil;NSMutableDictionary *(*itemQuery)(void)=NULL;
    if([op hasPrefix:@"places_"]){kind=@"places_";itemQuery=placesQuery;}
    else if([op hasPrefix:@"location_"]){kind=@"location_";itemQuery=locationQuery;}
    else if([op hasPrefix:@"shared_"]){kind=@"shared_";itemQuery=sharedQuery;}
    else if([op hasPrefix:@"session_"]){kind=@"session_";itemQuery=sessionQuery;}
    if(kind) {
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
    if(![op isEqualToString:@"save"] && ![op isEqualToString:@"unlock"]){finish(ticket,NO,@"invalid_data",nil);return;}
    if(text.length>65536){finish(ticket,NO,@"invalid_data",nil);return;}
    if([op isEqualToString:@"save"]) {
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0), ^{
            @synchronized(guard){if(ticket!=generation)return;
                OSStatus status=storeAccount([text dataUsingEncoding:NSUTF8StringEncoding]);
                finish(ticket,status==errSecSuccess,errorCode(status),nil);
            }
        });return;
    }
    // Unlock: read the saved login without any prompt.
    {
        NSMutableDictionary *q=query();q[(__bridge id)kSecReturnData]=@YES;q[(__bridge id)kSecReturnAttributes]=@YES;
        CFTypeRef value=NULL;OSStatus status=SecItemCopyMatching((__bridge CFDictionaryRef)q,&value);
        NSDictionary *found=value?CFBridgingRelease(value):nil;
        NSData *data=found[(__bridge id)kSecValueData];
        // A login saved before the App Group existed moves into it (no prompt).
        if(status==errSecSuccess && data && appGroup() && ![found[(__bridge id)kSecAttrAccessGroup] isEqual:appGroup()])storeAccount(data);
        if(status!=errSecItemNotFound) {
            NSString *account=status==errSecSuccess?[[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding]:nil;
            finish(ticket,account!=nil,account?@"":errorCode(status),account);return;
        }
    }
    // Only a login saved by an older build: Face ID once (its item requires it), then move it.
    if(!itemExists(legacyQuery())){finish(ticket,NO,@"missing",nil);return;}
    if(ng_vault_availability()<=0){finish(ticket,NO,@"unavailable",nil);return;}
    LAContext *ctx=[LAContext new];ctx.localizedFallbackTitle=@"";ctx.touchIDAuthenticationAllowableReuseDuration=0;
    @synchronized(guard){if(ticket!=generation)return;activeContext=ctx;}
    whenActive(^{dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0), ^{
        @synchronized(guard){if(ticket!=generation)return;}
        NSMutableDictionary *q=legacyQuery();q[(__bridge id)kSecReturnData]=@YES;
        q[(__bridge id)kSecUseAuthenticationContext]=ctx;
        q[(__bridge id)kSecUseOperationPrompt]=@"Move your saved Hyundai login so nCTRL no longer needs Face ID";
        CFTypeRef value=NULL;OSStatus status=SecItemCopyMatching((__bridge CFDictionaryRef)q,&value);
        NSData *data=value?CFBridgingRelease(value):nil;
        NSString *account=status==errSecSuccess?[[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding]:nil;
        if(account)storeAccount(data);
        finish(ticket,account!=nil,account?@"":errorCode(status),account);
    });});
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
