#import <Cocoa/Cocoa.h>

static NSColor *rgb(unsigned c) {
    return [NSColor colorWithSRGBRed:((c>>16)&255)/255.0 green:((c>>8)&255)/255.0 blue:(c&255)/255.0 alpha:1];
}
static NSView *surface(NSView *parent, NSRect frame, unsigned color, CGFloat radius) {
    NSView *v=[[NSView alloc] initWithFrame:frame]; v.wantsLayer=YES;
    v.layer.backgroundColor=rgb(color).CGColor; v.layer.cornerRadius=radius;v.layer.cornerCurve=kCACornerCurveContinuous; [parent addSubview:v]; return v;
}
static NSTextField *label(NSView *parent, NSRect frame, NSString *text, CGFloat size, CGFloat weight,unsigned color) {
    NSTextField *l=[NSTextField wrappingLabelWithString:text]; l.frame=frame;
    l.font=[NSFont systemFontOfSize:size weight:weight]; l.textColor=rgb(color); [parent addSubview:l]; return l;
}
@interface DesktopApp : NSObject <NSApplicationDelegate>
@property(strong) NSWindow *window;
@property(strong) NSTextField *state,*detail;
@property(strong) NSView *dot;
@property(strong) NSTask *agent;
@property(strong) NSPipe *input,*events;
@property(strong) NSMutableData *pending;
@property(strong) NSAlert *approval;
@property BOOL closing,failed,stopping,expanded;
@property(strong) NSView *drawer,*body;
@property(strong) NSButton *start,*stop,*toggle;
@end
@implementation DesktopApp
- (void)applicationDidFinishLaunching:(NSNotification *)note {
    NSRect frame=NSMakeRect(0,0,800,395);
    self.window=[[NSWindow alloc] initWithContentRect:frame styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskClosable|NSWindowStyleMaskMiniaturizable backing:NSBackingStoreBuffered defer:NO];
    self.window.titlebarAppearsTransparent=YES;self.window.titleVisibility=NSWindowTitleHidden;self.window.backgroundColor=rgb(0xf6f6f8);
    self.window.title=@"雄兴屏拓 · ZZXX CloudTech";
    self.window.appearance=[NSAppearance appearanceNamed:NSAppearanceNameAqua];
    NSView *root=self.window.contentView;root.wantsLayer=YES;root.layer.backgroundColor=rgb(0xf6f6f8).CGColor;
    NSView *top=surface(root,NSMakeRect(0,289,800,106),0xf6f6f8,0);
    NSImageView *logo=[[NSImageView alloc] initWithFrame:NSMakeRect(28,27,50,50)];
    logo.image=[NSImage imageNamed:NSImageNameApplicationIcon];logo.imageScaling=NSImageScaleProportionallyUpOrDown;[top addSubview:logo];
    label(top,NSMakeRect(95,25,280,19),@"ZZXX CloudTech",12,NSFontWeightMedium,0x676b73);
    label(top,NSMakeRect(93,45,320,39),@"雄兴屏拓",28,NSFontWeightSemibold,0x21242b);
    self.expanded=YES;
    self.toggle=[NSButton buttonWithTitle:@"收起选项" target:self action:@selector(toggleOptions:)];
    self.toggle.bezelStyle=NSBezelStyleRoundRect;self.toggle.frame=NSMakeRect(650,44,125,34);[top addSubview:self.toggle];
    self.drawer=surface(root,NSMakeRect(20,20,164,252),0xffffff,20);
    self.start=[NSButton buttonWithTitle:@"启动服务" target:self action:@selector(startAction:)];
    self.stop=[NSButton buttonWithTitle:@"关闭服务" target:self action:@selector(stopAction:)];
    NSButton *settings=[NSButton buttonWithTitle:@"系统设置" target:self action:@selector(openSettings:)];
    NSInteger row=0;for(NSButton *button in @[self.start,self.stop,settings]){
        button.bezelStyle=NSBezelStyleRoundRect;button.controlSize=NSControlSizeLarge;
        button.frame=NSMakeRect(14,194-row*49,136,38);[self.drawer addSubview:button];row++;
    }
    self.body=[[NSView alloc]initWithFrame:NSMakeRect(180,0,620,289)];[root addSubview:self.body];
    NSView *connection=surface(self.body,NSMakeRect(20,145,580,127),0xffffff,20);
    label(connection,NSMakeRect(20,86,400,25),@"电脑连接",17,NSFontWeightSemibold,0x21242b);
    self.dot=surface(connection,NSMakeRect(22,63,9,9),0x7ab8ff,4.5);
    self.state=label(connection,NSMakeRect(40,51,512,27),@"正在启动",15,NSFontWeightMedium,0x011d4f);
    self.detail=label(connection,NSMakeRect(20,7,540,41),@"正在准备本地键鼠服务。",13,NSFontWeightRegular,0x5d636e);
    NSView *permissions=surface(self.body,NSMakeRect(20,20,580,109),0xffffff,20);
    label(permissions,NSMakeRect(20,68,310,25),@"系统权限",17,NSFontWeightSemibold,0x21242b);
    label(permissions,NSMakeRect(20,11,535,48),@"首次使用时，在「隐私与安全性」中允许辅助功能和本地网络访问。",13,NSFontWeightRegular,0x5d636e);
    [self.window center];[self.window makeKeyAndOrderFront:nil];[NSApp activateIgnoringOtherApps:YES];
    self.pending=[NSMutableData data];[self startServices];
}
- (void)toggleOptions:(id)sender {
    self.expanded=!self.expanded;self.toggle.title=self.expanded?@"收起选项":@"展开选项";
    [NSAnimationContext runAnimationGroup:^(NSAnimationContext *ctx){
        ctx.duration=.20;[self.drawer.animator setFrameOrigin:NSMakePoint(self.expanded?20:-164,20)];
        [self.body.animator setFrameOrigin:NSMakePoint(self.expanded?180:90,0)];
        self.drawer.animator.alphaValue=self.expanded?1:0;
    } completionHandler:nil];
}
- (void)startAction:(id)sender {[self startServices];}
- (void)stopAction:(id)sender {
    self.stopping=YES;self.stop.enabled=NO;
    if(self.approval.window.sheetParent)[self.window endSheet:self.approval.window returnCode:NSModalResponseCancel];
    [self.input.fileHandleForWriting closeFile];self.input=nil;
    [self status:@"正在关闭" detail:@"正在释放键鼠连接。" color:0x676b73];
}
- (void)openSettings:(id)sender {
    NSURL *url=[[NSWorkspace sharedWorkspace] URLForApplicationWithBundleIdentifier:@"com.apple.systempreferences"];
    if(url)[[NSWorkspace sharedWorkspace] openURL:url];
}
- (BOOL)applicationShouldTerminateAfterLastWindowClosed:(NSApplication *)app {return YES;}
- (void)applicationWillTerminate:(NSNotification *)note {
    self.closing=YES;self.events.fileHandleForReading.readabilityHandler=nil;
    [self.input.fileHandleForWriting closeFile];
}
- (void)status:(NSString *)state detail:(NSString *)detail color:(unsigned)color {
    self.state.stringValue=state;self.detail.stringValue=detail;self.dot.layer.backgroundColor=rgb(color).CGColor;
}
- (void)failure:(NSString *)message {
    self.failed=YES;[self status:@"连接未就绪" detail:message color:0xba561b];
    if(self.approval.window.sheetParent)[self.window endSheet:self.approval.window returnCode:NSModalResponseCancel];
}
- (NSTask *)task:(NSURL *)exe arguments:(NSArray *)args {
    NSTask *t=[[NSTask alloc]init];t.executableURL=exe;t.arguments=args;
    t.standardInput=NSFileHandle.fileHandleWithNullDevice;t.standardOutput=NSFileHandle.fileHandleWithNullDevice;
    t.standardError=NSFileHandle.fileHandleWithNullDevice;return t;
}
- (void)startServices {
    if(self.agent.running)return;
    self.stopping=NO;self.failed=NO;self.start.enabled=NO;self.stop.enabled=YES;[self.pending setLength:0];
    NSURL *res=[NSBundle.mainBundle.bundleURL URLByAppendingPathComponent:@"Contents/Helpers"];
    NSURL *engine=[res URLByAppendingPathComponent:@"zzxx-input"];
    NSError *err=nil;
#if __arm64__
    NSString *helper=@"pair-arm64";
#else
    NSString *helper=@"pair-intel";
#endif
    self.agent=[self task:[res URLByAppendingPathComponent:helper] arguments:@[@"mac",engine.path]];
    self.input=[NSPipe pipe];self.events=[NSPipe pipe];self.agent.standardInput=self.input;self.agent.standardOutput=self.events;
    __weak DesktopApp *weakSelf=self;__weak NSTask *current=self.agent;
    self.agent.terminationHandler=^(NSTask *done){
        dispatch_async(dispatch_get_main_queue(),^{DesktopApp *owner=weakSelf;if(owner.closing || owner.agent!=done)return;
            owner.stop.enabled=NO;owner.start.enabled=YES;
            if(owner.stopping)[owner status:@"服务已关闭" detail:@"可在选项中重新启动。" color:0x676b73];
            else if(!owner.failed)[owner failure:@"后台连接已结束，可重新启动服务。"];
        });
    };
    self.events.fileHandleForReading.readabilityHandler=^(NSFileHandle *handle){
        NSData *data=handle.availableData;if(!data.length){handle.readabilityHandler=nil;}
        dispatch_async(dispatch_get_main_queue(),^{DesktopApp *owner=weakSelf;if(!owner || owner.closing || owner.stopping || owner.agent!=current)return;
            if(data.length)[owner consume:data];});
    };
    if(![self.agent launchAndReturnError:&err]){self.stop.enabled=NO;self.start.enabled=YES;[self failure:err.localizedDescription];}
}
- (void)consume:(NSData *)data {
    [self.pending appendData:data];NSData *nl=[@"\n" dataUsingEncoding:NSUTF8StringEncoding];
    while(self.pending.length){
        NSRange r=[self.pending rangeOfData:nl options:0 range:NSMakeRange(0,self.pending.length)];if(r.location==NSNotFound)break;
        NSData *line=[self.pending subdataWithRange:NSMakeRange(0,r.location)];[self.pending replaceBytesInRange:NSMakeRange(0,NSMaxRange(r)) withBytes:NULL length:0];
        NSDictionary *e=[NSJSONSerialization JSONObjectWithData:line options:0 error:nil];NSString *kind=e[@"Kind"];
        if([kind isEqual:@"ready"])[self status:@"等待电脑连接" detail:@"在同一局域网的 Windows 端选择这台 Mac，然后确认连接。" color:0x006cff];
        else if([kind isEqual:@"paired"])[self status:@"已配对" detail:[NSString stringWithFormat:@"%@ · 屏幕位置可在 Windows 端的「屏幕布局」中调整。",e[@"Name"]] color:0x207456];
        else if([kind isEqual:@"error"])[self failure:e[@"Message"]?:@"连接失败"];
        else if([kind isEqual:@"approve"])[self confirm:e];
    }
}
- (void)confirm:(NSDictionary *)e {
    if(self.approval.window.sheetParent)[self.window endSheet:self.approval.window returnCode:NSModalResponseCancel];
    NSAlert *a=[[NSAlert alloc]init];self.approval=a;a.messageText=@"允许这台电脑控制键鼠？";
    a.informativeText=[NSString stringWithFormat:@"%@\n请核对 Windows 窗口中的配对码：%@\n允许后可双向控制，并替换该边缘上原有的电脑。",e[@"Name"],e[@"Message"]];
    [a addButtonWithTitle:@"拒绝"];[a addButtonWithTitle:@"允许连接"];[self.window makeKeyAndOrderFront:nil];[NSApp activateIgnoringOtherApps:YES];
    [a beginSheetModalForWindow:self.window completionHandler:^(NSModalResponse response){
        if(self.approval==a)self.approval=nil;if(!self.agent.running || self.closing || self.stopping)return;
        NSDictionary *reply=@{@"Kind":response==NSAlertSecondButtonReturn?@"allow":@"deny",@"ID":e[@"ID"]};
        NSMutableData *bytes=[[NSJSONSerialization dataWithJSONObject:reply options:0 error:nil] mutableCopy];
        [bytes appendData:[@"\n" dataUsingEncoding:NSUTF8StringEncoding]];[self.input.fileHandleForWriting writeData:bytes error:nil];
    }];
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW,52*NSEC_PER_SEC),dispatch_get_main_queue(),^{
        if(self.approval==a && a.window.sheetParent)[self.window endSheet:a.window returnCode:NSModalResponseCancel];
    });
}
@end

int main(void){
    @autoreleasepool{
        NSApplication *app=NSApplication.sharedApplication;[app setActivationPolicy:NSApplicationActivationPolicyRegular];
        __attribute__((objc_precise_lifetime)) DesktopApp *delegate=[[DesktopApp alloc]init];app.delegate=delegate;
        NSMenu *bar=[[NSMenu alloc]init];NSMenuItem *item=[[NSMenuItem alloc]init];[bar addItem:item];
        NSMenu *menu=[[NSMenu alloc]init];[menu addItemWithTitle:@"退出雄兴屏拓" action:@selector(terminate:) keyEquivalent:@"q"];
        item.submenu=menu;app.mainMenu=bar;[app run];(void)delegate;
    }
    return 0;
}
