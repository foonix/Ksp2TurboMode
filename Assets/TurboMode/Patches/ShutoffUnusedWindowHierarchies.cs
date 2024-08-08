using KSP.Game;
using MonoMod.RuntimeDetour;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TurboMode.Patches
{
    public static class ShutoffUnusedWindowHierarchies
    {
        public static List<IDetour> MakeHooks() => new()
        {
            // PopUpUIManagerBase
            new Hook(
                typeof(PopUpUIManagerBase).GetMethod("set_IsVisible"),
                (Action<Action<PopUpUIManagerBase, bool>, PopUpUIManagerBase, bool>)SetPopUpUIVisibility
            ),
            new Hook(
                typeof(DeltaVToolManagerUI).GetMethod("set_IsVisible"),
                (Action<Action<PopUpUIManagerBase, bool>, PopUpUIManagerBase, bool>)SetPopUpUIVisibility
            ),
            new Hook(
                typeof(MissionTracker).GetMethod("SetVisible"),
                (Action<Action<PopUpUIManagerBase, bool>, PopUpUIManagerBase, bool>)SetPopUpUIVisibility
            ),

            // CheatsMenu
            new Hook(
                typeof(CheatsMenu).GetMethod("ShowWindow", BindingFlags.Instance | BindingFlags.NonPublic),
                (Action<Action<CheatsMenu, bool>, CheatsMenu, bool>)SetActiveOnVisibilityChangeCheats
            ),

            // KSPUtil.SetVisible() extension is used by several windows
            new Hook(
                typeof(KSPUtil).GetMethod("SetVisible"),
                (Action<Action<CanvasGroup, bool>, CanvasGroup, bool>)SetActiveOnVisibilityKSPUtil
            ),

            // GlobalEscapeMenu needs to be wrapped from the UIManager end, because UI manager
            // actually implments the toggle and will use GetComponentInChildren() before turning it on.
            new Hook(
                typeof(UIManager).GetMethod("SetPauseVisible"),
                (Action<Action<UIManager, bool>, UIManager, bool>)SetEscapeMenuVisible
            ),
        };

        public static void SetPopUpUIVisibility(Action<PopUpUIManagerBase, bool> orig, PopUpUIManagerBase window, bool isVisible)
        {
#if TURBOMODE_TRACE_EVENTS
            Debug.Log($"TM: Setting window {window?.gameObject.name} active {isVisible}");
#endif
            if (isVisible)
            {
                window.gameObject.SetActive(true);
            }
            orig(window, isVisible);
            if (!isVisible)
            {
                window.gameObject.SetActive(false);
            }
        }

        // I can't easily DRY here because many of the windows don't have a common ancestor with a shared visbility control.
        // CheatsMenu
        public static void SetActiveOnVisibilityChangeCheats(Action<CheatsMenu, bool> orig, CheatsMenu window, bool isVisible)
        {
#if TURBOMODE_TRACE_EVENTS
            Debug.Log($"TM: Cheats window {window.gameObject.name} active {isVisible}");
#endif
            if (isVisible)
            {
                window.gameObject.SetActive(true);
            }
            orig(window, isVisible);
            if (!isVisible)
            {
                window.gameObject.SetActive(false);
            }
        }

        public static void SetActiveOnVisibilityKSPUtil(
            Action<CanvasGroup, bool> orig,
            CanvasGroup window,
            bool isVisible)
        {
#if TURBOMODE_TRACE_EVENTS
            Debug.Log($"TM: Setting window {window.gameObject.name} active {isVisible}");
#endif

            // blacklisting LaunchpadDialog's children because it will call SetVisible(true)
            // on inactive children while it itself isn't visible,
            // and then later SetActive() to turn the children on/off.
            // The resulting Awake() we trigger here would then throw NREs.
            var isBlacklisted = window.transform.parent.GetComponentInParent<LaunchpadDialog>()
                || window.GetComponentInParent<GlobalEscapeMenu>();

            if (isVisible && !isBlacklisted)
            {
                window.gameObject.SetActive(isVisible);
            }
            orig(window, isVisible);
            if (!isVisible && !isBlacklisted)
            {
                window.gameObject.SetActive(isVisible);
            }
        }

        private static void SetEscapeMenuVisible(Action<UIManager, bool> orig, UIManager uiManager, bool isVisible)
        {
            // It will try to turn off the escape menu before the prefab is instantiated.
            if (!uiManager.EscapeMenu) return;

#if TURBOMODE_TRACE_EVENTS
            Debug.Log($"TM: Menu {uiManager.EscapeMenu.name} active {isVisible}");
#endif
            if (isVisible)
            {
                uiManager.EscapeMenu.gameObject.SetActive(isVisible);
            }
            orig(uiManager, isVisible);
            if (!isVisible && uiManager.EscapeMenu.isActiveAndEnabled)
            {
                uiManager.EscapeMenu.StartCoroutine(WaitForUiInit(uiManager, uiManager.EscapeMenu.gameObject, isVisible));
            }
        }

        private static IEnumerator WaitForUiInit(UIManager ui, GameObject target, bool setActive)
        {
            // wait for children to finish initilizing, or else it will throw errors first time we turn it on.
            while (!ui.Initialized)
            {
                yield return null;
            }
            target.SetActive(setActive);
        }
    }
}
