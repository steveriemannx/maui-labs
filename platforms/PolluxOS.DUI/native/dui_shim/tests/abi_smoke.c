/*
 * abi_smoke.c — end-to-end smoke test for the dui_shim C ABI.
 *
 * Two modes:
 *
 *   abi_smoke <resources-dir>              headless checks only (no display needed)
 *   abi_smoke <resources-dir> <seconds>    also opens a window, adds a Label, runs the
 *                                          message loop, dumps the widget tree, and
 *                                          closes the window after <seconds>
 *
 * The windowed mode is what a display (X11, Wayland, AppKit) is required for; run it
 * under Xvfb, on a Wayland desktop, or on a Mac. Exits non-zero on the first failure.
 *
 * Verified on macOS 26 / arm64 and FreeBSD 15 / Wayland.
 */

#include "dui_shim.h"

#include <pthread.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

static dui_shim_window* g_window = NULL;
static char* g_tree = NULL;

static void* quitter(void* arg)
{
    int seconds = *(int*)arg;
    sleep((unsigned)seconds);

    /* The tree walk needs the toolkit's UI thread; the shim marshals it. */
    size_t len = 0;
    g_tree = dui_shim_window_dump_xml(g_window, &len);
    printf("--- widget tree (%zu bytes) ---\n%s", len, g_tree != NULL ? g_tree : "(dump unavailable)\n");

    /* Renderer-level capture: evidence for what the window actually painted. */
    int captured = dui_shim_window_capture_ppm(g_window, "abi_smoke.ppm");
    printf("capture          : %s (abi_smoke.ppm)\n", captured == 0 ? "ok" : dui_shim_last_error());

    /* Closing the last window ends the loop (PostQuitMsgWhenClosed). */
    dui_shim_window_close(g_window);
    return NULL;
}

int main(int argc, char** argv)
{
    const char* resources = argc > 1 ? argv[1] : "";
    const int seconds = argc > 2 ? atoi(argv[2]) : 0;
    int failures = 0;

    printf("dui_shim version : %s\n", dui_shim_version());

    if (dui_shim_startup(resources, NULL) != 0)
    {
        printf("FAIL startup(%s): %s\n", resources, dui_shim_last_error());
        return 1;
    }
    printf("startup          : ok (resources: %s)\n", resources);

    if (seconds > 0)
    {
        g_window = dui_shim_window_create("abi-smoke", "dui_shim smoke test", 480, 320, NULL, NULL);
        if (g_window == NULL)
        {
            printf("FAIL window_create: %s\n", dui_shim_last_error());
            return 1;
        }
        printf("window_create    : ok\n");

        /* Handles are valid before the loop starts — the controls are created on
         * the UI thread when it does. */
        dui_shim_widget* root = dui_shim_window_root(g_window);
        if (root == NULL)
        {
            printf("FAIL window_root\n");
            return 1;
        }

        /* An explicit background matters: a pure-code window has no skin, so an
         * unset container paints the window black. */
        dui_shim_widget_set_attribute(root, "bkcolor", "#FFFFFFFF");

        dui_shim_widget* label = dui_shim_widget_create(root, "Label", "smokeLabel");
        if (label == NULL)
        {
            printf("FAIL widget_create: %s\n", dui_shim_last_error());
            return 1;
        }
        dui_shim_widget_set_bounds(label, 24, 24, 400, 48);
        dui_shim_widget_set_text(label, "hello from dui_shim");
        dui_shim_widget_set_attribute(label, "textcolor", "#FF000000");

        dui_shim_widget* button = dui_shim_widget_create(root, "Button", "smokeButton");
        if (button == NULL)
        {
            printf("FAIL widget_create(Button): %s\n", dui_shim_last_error());
            return 1;
        }
        dui_shim_widget_set_bounds(button, 24, 96, 160, 40);
        dui_shim_widget_set_text(button, "Click me");
        dui_shim_widget_set_attribute(button, "textcolor", "#FF000000");
        printf("widgets          : Label + Button queued\n");

        dui_shim_window_show(g_window, 1);
        printf("window_show      : requested\n");

        pthread_t thread;
        int wait_seconds = seconds;
        pthread_create(&thread, NULL, quitter, &wait_seconds);

        printf("message loop     : running for %d s ...\n", seconds);
        int rc = dui_shim_run();
        pthread_join(thread, NULL);

        if (rc != 0)
        {
            printf("FAIL dui_shim_run returned %d\n", rc);
            failures++;
        }
        else
        {
            printf("message loop     : exited cleanly\n");
        }

        if (g_tree == NULL)
        {
            printf("FAIL widget tree dump returned nothing (%s)\n", dui_shim_last_error());
            failures++;
        }
        else
        {
            /* The dump is the automation surface: the widgets must be in it. */
            struct { const char* needle; const char* what; } expectations[] = {
                { "smokeLabel", "Label by name" },
                { "hello from dui_shim", "Label text" },
                { "smokeButton", "Button by name" },
                { "Button", "Button type" },
            };
            for (size_t i = 0; i < sizeof(expectations) / sizeof(expectations[0]); i++)
            {
                if (strstr(g_tree, expectations[i].needle) == NULL)
                {
                    printf("FAIL tree dump is missing %s\n", expectations[i].what);
                    failures++;
                }
            }
            if (failures == 0)
                printf("tree dump        : contains Label text, Button and both names\n");
        }

        dui_shim_string_free(g_tree);
    }

    /* Error paths, no display required. */
    dui_shim_widget* orphan = dui_shim_widget_create(NULL, "Label", NULL);
    if (orphan != NULL)
    {
        printf("FAIL widget_create(NULL) should return NULL\n");
        failures++;
    }
    else
    {
        printf("error path       : %s\n", dui_shim_last_error());
    }

    dui_shim_shutdown();

    if (failures != 0)
    {
        printf("SMOKE FAILED (%d failure%s)\n", failures, failures == 1 ? "" : "s");
        return 1;
    }

    printf("SMOKE OK\n");
    return 0;
}
