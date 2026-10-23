#import <Foundation/Foundation.h>
#import <Security/Security.h>
#import <LocalAuthentication/LocalAuthentication.h>
#import <UIKit/UIKit.h>
#include <stdlib.h>
#include <string.h>

// C ABI bridge: no managed callbacks or credential logging. All results are consumed once.
#define NG_EXPORT extern "C" __attribute__((visibility("default"), used))
static NSObject *guard;
static NSData *resultData;
static LAContext *activeContext;
static NSUInteger generation;
static BOOL busy;
static NSMutableArray<UIView *> *privacyViews;
static NSMutableArray *observers;
static NSString *const service=@"com.ngarage.bluelink.credentials.v1";
static NSMutableDictionary *query(void) {
    return [@{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
              (__bridge id)kSecAttrService:service,(__bridge id)kSecAttrAccount:@"account",
              (__bridge id)kSecAttrSynchronizable:@NO} mutableCopy];
}
static void initialize(void) {
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        guard=[NSObject new];privacyViews=[NSMutableArray new];observers=[NSMutableArray new];
        // Keychain items may survive uninstall. Clear an orphan on a fresh install.
        NSUserDefaults *defaults=NSUserDefaults.standardUserDefaults;
        if(![defaults boolForKey:@"NGVaultInstalledV1"]) {
            SecItemDelete((__bridge CFDictionaryRef)query());
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
static NSString *errorCode(OSStatus status) {
    if(status==errSecUserCanceled)return @"cancelled";
    if(status==errSecItemNotFound)return @"missing";
    if(status==errSecAuthFailed || status==errSecInteractionNotAllowed)return @"auth_failed";
    return @"storage_failed";
}
NG_EXPORT int ng_vault_availability(void) {
    initialize();LAContext *ctx=[LAContext new];NSError *error=nil;
    if(![ctx canEvaluatePolicy:LAPolicyDeviceOwnerAuthenticationWithBiometrics error:&error])return 0;
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
    if(ng_vault_availability()==0){finish(ticket,NO,@"unavailable",nil);return;}
    if(![op isEqualToString:@"save"] && ![op isEqualToString:@"unlock"]){finish(ticket,NO,@"invalid_data",nil);return;}
    if(text.length>65536){finish(ticket,NO,@"invalid_data",nil);return;}
    LAContext *ctx=[LAContext new];ctx.localizedFallbackTitle=@"";ctx.touchIDAuthenticationAllowableReuseDuration=0;
    @synchronized(guard){if(ticket!=generation)return;activeContext=ctx;}
    if([op isEqualToString:@"unlock"]) {
        // The ACL on the item itself requires biometry; there is no separate boolean gate.
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0), ^{
            NSMutableDictionary *q=query();q[(__bridge id)kSecReturnData]=@YES;
            q[(__bridge id)kSecUseAuthenticationContext]=ctx;
            q[(__bridge id)kSecUseOperationPrompt]=@"Unlock your saved Hyundai account in nCTRL";
            CFTypeRef value=NULL;OSStatus status=SecItemCopyMatching((__bridge CFDictionaryRef)q,&value);
            NSData *data=value?CFBridgingRelease(value):nil;
            NSString *account=status==errSecSuccess?[[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding]:nil;
            finish(ticket,status==errSecSuccess && account!=nil,status==errSecSuccess?@"":errorCode(status),account);
        });return;
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
// Godot's generated plugin initialization code uses C++ linkage.
void ng_vault_init(void) {
    initialize();dispatch_async(dispatch_get_main_queue(), ^{
        NSNotificationCenter *nc=NSNotificationCenter.defaultCenter;
        id hide=[nc addObserverForName:UIApplicationWillResignActiveNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note){
            for(UIScene *scene in UIApplication.sharedApplication.connectedScenes)if([scene isKindOfClass:UIWindowScene.class]) {
                for(UIWindow *window in ((UIWindowScene *)scene).windows)if(window.isKeyWindow) {
                    UIView *cover=[[UIView alloc] initWithFrame:window.bounds];cover.backgroundColor=UIColor.blackColor;
                    cover.autoresizingMask=UIViewAutoresizingFlexibleWidth|UIViewAutoresizingFlexibleHeight;
                    [window addSubview:cover];[privacyViews addObject:cover];
                }
            }
        }];
        id reveal=[nc addObserverForName:UIApplicationDidBecomeActiveNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note){
            for(UIView *cover in privacyViews)[cover removeFromSuperview];[privacyViews removeAllObjects];
        }];
        [observers addObject:hide];[observers addObject:reveal];
    });
}
void ng_vault_deinit(void) {
    ng_vault_cancel();dispatch_async(dispatch_get_main_queue(), ^{
        for(id observer in observers)[NSNotificationCenter.defaultCenter removeObserver:observer];[observers removeAllObjects];
        for(UIView *cover in privacyViews)[cover removeFromSuperview];[privacyViews removeAllObjects];
    });
}
