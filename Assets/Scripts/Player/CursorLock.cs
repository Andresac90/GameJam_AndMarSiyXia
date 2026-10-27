using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Locks/unlocks the mouse for first-person play.
/// Desktop/editor: Unity's Cursor.lockState as usual.
/// WebGL: the browser only allows pointer lock inside a click, so the lock is requested from the
/// canvas click (Plugins/WebGL/PointerLock.jslib). Setting Cursor.lockState there makes Safari
/// throw "NotAllowedError: Pointer lock requires a user gesture", which crashes the page.
/// </summary>
public static class CursorLock
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void WebPointerLock_SetWanted(int wanted);
    [DllImport("__Internal")] private static extern int WebPointerLock_IsLocked();
#endif

    public static void Set(bool locked)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        WebPointerLock_SetWanted(locked ? 1 : 0);
        Cursor.visible = !locked;
#else
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
#endif
    }

    /// <summary>
    /// True when the mouse is captured. In WebGL that only happens after the player clicks
    /// the game, so mouse look and shooting wait for it (the first click shouldn't fire).
    /// </summary>
    public static bool IsLocked
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return WebPointerLock_IsLocked() == 1;
#else
            return true;
#endif
        }
    }
}
