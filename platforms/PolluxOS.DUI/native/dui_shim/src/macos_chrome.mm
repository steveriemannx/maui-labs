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

    // No style-mask surgery here: the host asks for the system caption at *creation*
    // (WindowCreateAttributes::m_bUseSystemCaption, see DuiWindowImpl in dui_shim.cpp), so
    // dui already builds a titled AppKit window and leaves the buttons alone. Changing the
    // mask afterwards leaves dui's own view with a bogus frame.
    [[window standardWindowButton:NSWindowCloseButton] setHidden:NO];
    [[window standardWindowButton:NSWindowMiniaturizeButton] setHidden:NO];
    [[window standardWindowButton:NSWindowZoomButton] setHidden:NO];

    // dui also hides the title text (titleVisibility = NSWindowTitleHidden). Keep the bar
    // transparent like dui does — an opaque bar would cover the top of the host's content.
    window.titleVisibility = NSWindowTitleVisible;
    if (title != nil) {
        [window setTitle:title];
    }

    [window setHasShadow:YES];

    // dui's own NSView is created without resizing behaviour, so it keeps the size it had
    // at creation: the window grows and the content stays put (and reports stale sizes).
    // Let it follow the content view, which is what makes window resizes re-layout.
    NSView* contentView = window.contentView;
    if (contentView != nil) {
        for (NSView* subview in contentView.subviews) {
            subview.frame = contentView.bounds;
            subview.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
        }
    }
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

extern "C" int dui_shim_macos_content_insets(void* nsWindow, int* top, int* left, int* bottom, int* right,
                                            int* viewHeight)
{
    if (nsWindow == nullptr) {
        return 0;
    }

    NSWindow* window = (__bridge NSWindow*)nsWindow;
    __block int result = 0;
    void (^work)(void) = ^{
        NSView* contentView = window.contentView;
        if (contentView == nil) {
            return;
        }

        // Standard way to get the title bar height: the difference between the window frame
        // and the content rect belonging to it. contentLayoutRect proved unreliable for
        // dui's windows (it can come back degenerate), and the content view covers the whole
        // window because dui uses FullSizeContentView, so the buttons float over it.
        const NSRect frame = window.frame;
        const NSRect contentRect = [window contentRectForFrameRect:frame];
        const NSRect viewBounds = contentView.bounds;

        const CGFloat titleBar = NSHeight(frame) - NSHeight(contentRect);
        const CGFloat leftInset = 0;
        const CGFloat rightInset = 0;
        const CGFloat bottomInset = 0;

        if (top != nullptr) {
            *top = (int)lround(titleBar > 0 ? titleBar : 0);
        }
        if (left != nullptr) {
            *left = (int)lround(leftInset > 0 ? leftInset : 0);
        }
        if (bottom != nullptr) {
            *bottom = (int)lround(bottomInset > 0 ? bottomInset : 0);
        }
        if (right != nullptr) {
            *right = (int)lround(rightInset > 0 ? rightInset : 0);
        }
        if (viewHeight != nullptr) {
            *viewHeight = (int)lround(NSHeight(viewBounds));
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

extern "C" int dui_shim_macos_set_content_size(void* nsWindow, int width, int height)
{
    if (nsWindow == nullptr || width <= 0 || height <= 0) {
        return 0;
    }

    NSWindow* window = (__bridge NSWindow*)nsWindow;
    __block int result = 0;
    void (^work)(void) = ^{
        // setContentSize: is ignored for windows that were created borderless; going through
        // the frame (and letting AppKit convert the content rect) works in both cases.
        const NSRect contentRect = NSMakeRect(0, 0, (CGFloat)width, (CGFloat)height);
        const NSRect frame = [window frameRectForContentRect:contentRect];
        [window setFrame:frame display:YES];

        // Resize every view in the content view tree to the new bounds: dui's own view does
        // not follow the window by itself.
        // dui's own view is created without resizing behaviour, so nudge it (and anything
        // it nests) to the window's new bounds.
        NSView* contentView = window.contentView;
        void (^fit)(NSView*) = ^(NSView* view) {
            view.frame = contentView.bounds;
            for (NSView* child in view.subviews) {
                fit(child);
            }
        };
        if (contentView != nil && contentView.subviews.count > 0) {
            for (NSView* subview in contentView.subviews) {
                fit(subview);
            }
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
