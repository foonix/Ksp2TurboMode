using KSP.Game;
using KSP.UI;
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
                (Action<Action<PopUpUIManagerBase, bool>, PopUpUIManagerBase, bool>)SetVisibility
            ),
            new Hook(
                typeof(DeltaVToolManagerUI).GetMethod("set_IsVisible"),
                (Action<Action<PopUpUIManagerBase, bool>, PopUpUIManagerBase, bool>)SetVisibility
            ),
            new Hook(
                typeof(MissionTracker).GetMethod("SetVisible"),
                (Action<Action<PopUpUIManagerBase, bool>, PopUpUIManagerBase, bool>)SetVisibility
            ),

            // CheatsMenu
            new Hook(
                typeof(CheatsMenu).GetMethod("ShowWindow", BindingFlags.Instance | BindingFlags.NonPublic),
                (Action<Action<CheatsMenu, bool>, CheatsMenu, bool>)SetVisibility
            ),

            new Hook(
                typeof(SaveLoadDialog).GetMethod("SetVisiblity"),
                (Action<Action<SaveLoadDialog, bool>, SaveLoadDialog, bool>)SetVisibility
            ),

            new Hook(
                typeof(MissionControlMenuController).GetMethod("SetVisible"),
                (Action<Action<MissionControlMenuController, bool>, MissionControlMenuController, bool>)SetVisibility
            ),

            new Hook(
                typeof(SaveLoadDialog).GetMethod("OnHideAnimationComplete"),
                (Action<Action<SaveLoadDialog>, SaveLoadDialog>)SetActiveOnVisibilitySaveLoadAfterTween
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

        private static void SetVisibility<T>(Action<T, bool> orig, T window, bool isVisible) where T : Behaviour
        {
            bool windowIsAlive = window;
#if TURBOMODE_TRACE_EVENTS
            Debug.Log($"TM: Setting window {(windowIsAlive ? window.gameObject.name : "<null>")} active {isVisible}");
#endif
            if (isVisible && windowIsAlive)
            {
                window.gameObject.SetActive(true);
            }
            orig(window, isVisible);
            if (!isVisible && windowIsAlive)
            {
                WaitForFrames(window, 1, isVisible);
            }
        }

        private static void SetActiveOnVisibilitySaveLoadAfterTween(Action<SaveLoadDialog> orig, SaveLoadDialog window)
        {
#if TURBOMODE_TRACE_EVENTS
            Debug.Log($"TM: Save/Load window {window.gameObject.name} inactive after tween");
#endif

            orig(window);
            window.gameObject.SetActive(false);
        }

        private static void SetActiveOnVisibilityKSPUtil(
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
                WaitForFrames(window, 1, isVisible);
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
                WaitForFrames(uiManager.EscapeMenu, 1, isVisible);
            }
        }

        private static void WaitForFrames(Behaviour target, int frames, bool enable)
        {
            GameManager.Instance.StartCoroutine(WaitForFramesCoroutine(target, frames, enable));
        }

        private static IEnumerator WaitForFramesCoroutine(Behaviour target, int frames, bool enable)
        {
            var canvasGroup = target.GetComponent<CanvasGroup>();
            if (canvasGroup && !canvasGroup.enabled)
            {
                // The alpha change will have no effect.  We actually don't want to disable the object then.
                // Fixes throttle percentage turning on/off, which appears to be a feature that was implemented
                // and then later disabled by just disabling the canvas group.
                yield break;
            }

            while (frames > 0)
            {
                yield return null;
                frames--;
            }

            // Target may have been set disabled just before being destroyed
            if (!target)
            {
                yield break;
            }

            if (canvasGroup)
            {
                // they may have turned it off band back on again in the same frame
                var stillActive = canvasGroup.alpha > 0f;

                target.gameObject.SetActive(stillActive);
                yield break;
            }
            target.gameObject.SetActive(enable);
        }
    }
}
