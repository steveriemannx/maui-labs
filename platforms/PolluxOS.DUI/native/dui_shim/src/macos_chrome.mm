// macOS window decoration (title bar + traffic lights).
//
// dui converts its windows into titled document windows with the title bar hidden and then
// hides the three standard buttons, because it draws its own caption
// (src/Core/NativeWindow_MacOS.mm, ModifyNsWindowShadowType). There is no path that shows
// them again, so a host that wants the real macOS chrome has to restore it here.
//
// Two hard-won constraints:
//   * Run this only once the window is on screen: changing the style mask while dui is
//     still creating/sizing its view leaves that view with a bogus frame.
//   * Keep NSWindowStyleMaskFullSizeContentView: removing it makes AppKit recompute the
//     content view and dui's layout collapses.
//
// Compiled only on Apple platforms (see CMakeLists.txt).

#import <Cocoa/Cocoa.h>

namespace {

NSWindow* FindDuIWindow(const char* title)
{
    NSString* wanted = title != nullptr ? [NSString stringWithUTF8String:title] : nil;

    // Prefer the window whose content view is dui's own view: dui sets the title late, so
    // matching on it alone proved unreliable.
    for (NSWindow* window in [NSApp windows]) {
        NSString* viewClass = NSStringFromClass([[window contentView] class]);
        if ([viewClass containsString:@"DuI"] || [viewClass containsString:@"Dui"]) {
            return window;
        }
    }

    for (NSWindow* window in [NSApp windows]) {
        if (wanted != nil && [[window title] isEqualToString:wanted]) {
            return window;
        }
    }

    NSWindow* front = [NSApp mainWindow];
    return front != nil ? front : [NSApp keyWindow];
}

void ApplySystemChrome(NSWindow* window, NSString* title)
{
    if (window == nil) {
        return;
    }

    NSWindowStyleMask mask = [window styleMask];
    mask |= NSWindowStyleMaskTitled | NSWindowStyleMaskClosable |
            NSWindowStyleMaskMiniaturizable | NSWindowStyleMaskResizable;
    if ([window styleMask] != mask) {
        [window setStyleMask:mask];
    }

    [[window standardWindowButton:NSWindowCloseButton] setHidden:NO];
    [[window standardWindowButton:NSWindowMiniaturizeButton] setHidden:NO];
    [[window standardWindowButton:NSWindowZoomButton] setHidden:NO];

    // dui also hides the title text; the bar itself stays transparent like dui made it, so
    // the host's content still shows through underneath.
    window.titleVisibility = NSWindowTitleVisible;
    if (title != nil) {
        [window setTitle:title];
    }

    [window setHasShadow:YES];

    // Let dui's view follow window resizes. Mask only: assigning frames here would freeze
    // the view at whatever size it happens to have at this moment.
    NSView* contentView = window.contentView;
    if (contentView != nil) {
        for (NSView* subview in contentView.subviews) {
            subview.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
        }
    }
}

} // namespace

extern "C" void* dui_shim_macos_find_window(const char* title)
{
    __block NSWindow* found = nil;
    void (^work)(void) = ^{
        found = FindDuIWindow(title);
    };

    if ([NSThread isMainThread]) {
        work();
    } else {
        dispatch_sync(dispatch_get_main_queue(), work);
    }

    return (__bridge void*)found;
}

extern "C" void dui_shim_macos_show_system_chrome(void* nsWindow, const char* title)
{
    if (nsWindow == nullptr) {
        return;
    }

    NSWindow* window = (__bridge NSWindow*)nsWindow;
    NSString* windowTitle = title != nullptr ? [NSString stringWithUTF8String:title] : nil;
    void (^work)(void) = ^{
        ApplySystemChrome(window, windowTitle);
    };

    if ([NSThread isMainThread]) {
        work();
    } else {
        dispatch_sync(dispatch_get_main_queue(), work);
    }
}

extern "C" int dui_shim_macos_describe_chrome(void* nsWindow, int* styleMask, int* buttonsHidden, int* titleVisible)
{
    if (nsWindow == nullptr) {
        return 0;
    }

    NSWindow* window = (__bridge NSWindow*)nsWindow;
    __block int result = 0;
    void (^work)(void) = ^{
        if (styleMask != nullptr) {
            *styleMask = (int)[window styleMask];
        }
        if (buttonsHidden != nullptr) {
            const bool close = [[window standardWindowButton:NSWindowCloseButton] isHidden];
            const bool mini = [[window standardWindowButton:NSWindowMiniaturizeButton] isHidden];
            const bool zoom = [[window standardWindowButton:NSWindowZoomButton] isHidden];
            *buttonsHidden = (close || mini || zoom) ? 1 : 0;
        }
        if (titleVisible != nullptr) {
            *titleVisible = ([window titleVisibility] == NSWindowTitleVisible) ? 1 : 0;
        }
        result = 1;
    };

    if ([NSThread isMainThread]) {
        work();
    } else {
        dispatch_sync(dispatch_get_main_queue(), work);
    }

    return result;
}
