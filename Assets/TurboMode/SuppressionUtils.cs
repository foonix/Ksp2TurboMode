using KSP.Game;
using System;

namespace TurboMode
{
    public static class SuppressionUtils
    {
        public static void FixedUpdateShunt(Action<object, float> orig, IFixedUpdate obj, float deltaTime)
            => GameManager.Instance.Game.UnregisterFixedUpdate(obj);
        public static void UpdateShunt(Action<object, float> orig, IUpdate obj, float deltaTime)
            => GameManager.Instance.Game.UnregisterUpdate(obj);
        public static void VoidShutoff(Action<object> orig, object origObject) { }
    }
}