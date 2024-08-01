using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

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

        public static void InvokeOtherObjectEvent<T>(this System.Object instance, FieldInfo eventField, T args)
        {
            if (eventField.GetValue(instance) is not MulticastDelegate multicastDelegate)
                return;

            var invocationList = multicastDelegate.GetInvocationList();

            foreach (var invocationMethod in invocationList)
                invocationMethod.DynamicInvoke(args);
        }

#nullable enable
        // This is the fastest way to invoke or enumerate events I've found.
        // https://stackoverflow.com/a/71053418
        public class EventHelper<TClass, TDelegate> where TDelegate : Delegate
        {
            public EventHelper(string eventName)
            {
                var fieldInfo = typeof(TClass).GetField(eventName, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance) ??
                        throw new ArgumentException("Event was not found", nameof(eventName));

                var thisArg = Expression.Parameter(typeof(TClass));
                var body = Expression.Convert(Expression.Field(thisArg, fieldInfo), typeof(TDelegate));
                Get = Expression.Lambda<Func<TClass, TDelegate?>>(body, thisArg).Compile();
            }

            // Can be used to invoke the vent without garbage.
            // thisHelper.Get(srcObj).Invoke(args)
            public Func<TClass, TDelegate?> Get { get; }

            public IEnumerable<TDelegate> GetInvocationList(TClass forInstance)
            {
                var eventDelegate = Get(forInstance);
                if (eventDelegate is null)
                    yield break;

                foreach (var d in eventDelegate.GetInvocationList())
                    yield return (TDelegate)d;
            }
        }
#nullable disable

        // Zero garbage private field get/set
        // Faster than reflection to boot.
        // https://stackoverflow.com/questions/16073091/is-there-a-way-to-create-a-delegate-to-get-and-set-values-for-a-fieldinfo
        public class FieldHelper<S, T>
        {
            public Func<S, T> Get;
            public Action<S, T> Set;

            public FieldHelper(FieldInfo field)
            {
                Get = CreateGetter(field);
                Set = CreateSetter(field);
            }

            static Func<S, T> CreateGetter(FieldInfo field)
            {
                string methodName = field.ReflectedType.FullName + ".get_" + field.Name;
                DynamicMethod setterMethod = new(methodName, typeof(T), new Type[1] { typeof(S) }, true);
                ILGenerator gen = setterMethod.GetILGenerator();
                if (field.IsStatic)
                {
                    gen.Emit(OpCodes.Ldsfld, field);
                }
                else
                {
                    gen.Emit(OpCodes.Ldarg_0);
                    gen.Emit(OpCodes.Ldfld, field);
                }
                gen.Emit(OpCodes.Ret);
                return (Func<S, T>)setterMethod.CreateDelegate(typeof(Func<S, T>));
            }

            static Action<S, T> CreateSetter(FieldInfo field)
            {
                string methodName = field.ReflectedType.FullName + ".set_" + field.Name;
                DynamicMethod setterMethod = new(methodName, null, new Type[2] { typeof(S), typeof(T) }, true);
                ILGenerator gen = setterMethod.GetILGenerator();
                if (field.IsStatic)
                {
                    gen.Emit(OpCodes.Ldarg_1);
                    gen.Emit(OpCodes.Stsfld, field);
                }
                else
                {
                    gen.Emit(OpCodes.Ldarg_0);
                    gen.Emit(OpCodes.Ldarg_1);
                    gen.Emit(OpCodes.Stfld, field);
                }
                gen.Emit(OpCodes.Ret);
                return (Action<S, T>)setterMethod.CreateDelegate(typeof(Action<S, T>));
            }
        }
    }
}
