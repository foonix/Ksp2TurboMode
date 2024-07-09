using System;
using System.Collections.Generic;
using System.Reflection;

namespace TurboMode
{
    public static class ReflectionUtil
    {
        public static R GetField<T, R>(this T obj, string name)
        {
            var bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
            var field = typeof(T).GetField(name, bindingFlags);
            return (R)field?.GetValue(obj);
        }

        public static T GetFieldValue<T>(this object obj, string name)
        {
            var bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
            var field = obj.GetType().GetField(name, bindingFlags);
            return (T)field?.GetValue(obj);
        }

        public static void SetField<T, V>(this T obj, string name, V value)
        {
            var bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
            var field = typeof(T).GetField(name, bindingFlags);
            field.SetValue(obj, value);
        }

        public static void SetProperty<T, V>(this T obj, string name, V value)
        {
            typeof(T).InvokeMember(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.SetProperty,
                Type.DefaultBinder, obj, new object[] { value });
        }

        public static void CallPrivateVoidMethod<T>(this T obj, string name, params object[] args)
        {
            MethodInfo method = typeof(T).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(obj, args);
        }
    }
}
