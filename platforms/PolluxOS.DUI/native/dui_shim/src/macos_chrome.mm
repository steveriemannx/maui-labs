// macOS window chrome helper.
//
// DUI deliberately hides the AppKit traffic lights and the window title for normal
// windows: it converts them into titled "document" windows with the title bar hidden and
// then calls
//
//     [[window standardWindowButton:NSWindowCloseButton] setHidden:YES];   // etc.
//
// because dui draws its own caption controls (src/Core/NativeWindow_MacOS.mm, inside
// ModifyNsWindowShadowType). There is no "show system chrome" path afterwards —
// SetUseSystemCaption() only flips a flag and is overwritten from the window XML at
// creation — so a host that wants the real macOS title bar has to restore it.
//
// This file is compiled only on Apple platforms (see CMakeLists.txt).

#import <Cocoa/Cocoa.h>

#include <cstring>

namespace {

NSWindow* FindWindowByTitle(const char* title)
{
    NSString* wanted = title != nullptr ? [NSString stringWithUTF8String:title] : nil;

    for (NSWindow* window in [NSApp windows]) {
        if (wanted != nil && [[window title] isEqualToString:wanted]) {
            return window;
        }
    }

    // Fall back to whatever the app currently has up front.
    NSWindow* front = [NSApp mainWindow];
    if (front == nil) {
        front = [NSApp keyWindow];
    }
    return front;
}

void ApplySystemChrome(NSWindow* window, NSString* title)
{
    if (window == nil) {
        return;
    }

    // A titled window is what carries the system title bar; keep the full-size content
    // view so the host's layout still covers the whole window.
    NSWindowStyleMask mask = [window styleMask];
    mask |= NSWindowStyleMaskTitled | NSWindowStyleMaskClosable |
            NSWindowStyleMaskMiniaturizable | NSWindowStyleMaskResizable;
    if ([window styleMask] != mask) {
        [window setStyleMask:mask];
    }

    [[window standardWindowButton:NSWindowCloseButton] setHidden:NO];
    [[window standardWindowButton:NSWindowMiniaturizeButton] setHidden:NO];
    [[window standardWindowButton:NSWindowZoomButton] setHidden:NO];

    // dui also hides the title text (titleVisibility = NSWindowTitleHidden).
    window.titleVisibility = NSWindowTitleVisible;
    window.titlebarAppearsTransparent = NO;
    if (title != nil) {
        [window setTitle:title];
    }

    [window setHasShadow:YES];
}

int DescribeChrome(NSWindow* window, int* styleMask, int* buttonsHidden, int* titleVisible)
{
    if (window == nil) {
        return 0;
    }

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

    return 1;
}

} // namespace

extern "C" void* dui_shim_macos_find_window(const char* title)
{
    __block NSWindow* found = nil;
    void (^work)(void) = ^{
        found = FindWindowByTitle(title);
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
        // Synchronous on purpose: the host reports the resulting state right afterwards,
        // and dui itself applies its shadow/chrome changes the same way.
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
        result = DescribeChrome(window, styleMask, buttonsHidden, titleVisible);
    };

    if ([NSThread isMainThread]) {
        work();
    } else {
        dispatch_sync(dispatch_get_main_queue(), work);
    }

    return result;
}
