using System;
using System.Reflection;
using System.Reflection.Emit;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Readers of properties / fields made once as delegates (a call of a delegate instead of PropertyInfo.GetValue / FieldInfo.GetValue, which is
    /// several times slower on Mono and is done for every window every frame). The result is boxed as with reflection. Null is returned when no
    /// delegate can be made (the caller keeps the reflection): static members, indexers, value-type owners, a failing emit.
    /// </summary>
    static class FastGet
    {
        /// <summary>Instance property: o => (object)o.Prop.</summary>
        public static Func<object, object> Property(PropertyInfo p)
        {
            try
            {
                var m = p == null ? null : p.GetGetMethod(true);
                var dt = p == null ? null : p.DeclaringType;
                if (m == null || m.IsStatic || p.GetIndexParameters().Length > 0 || dt == null || dt.IsValueType || dt.IsGenericTypeDefinition || p.PropertyType.IsByRef || p.PropertyType.IsPointer) return null;
                var mk = typeof(FastGet).GetMethod("WrapProp", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(dt, p.PropertyType);
                return (Func<object, object>)mk.Invoke(null, new object[] { m });
            }
            catch { return null; }
        }

        /// <summary>Static property: () => (object)Type.Prop.</summary>
        public static Func<object> StaticProperty(PropertyInfo p)
        {
            try
            {
                var m = p == null ? null : p.GetGetMethod(true);
                if (m == null || !m.IsStatic || p.GetIndexParameters().Length > 0 || p.PropertyType.IsByRef || p.PropertyType.IsPointer) return null;
                var mk = typeof(FastGet).GetMethod("WrapStatic", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(p.PropertyType);
                return (Func<object>)mk.Invoke(null, new object[] { m });
            }
            catch { return null; }
        }

        /// <summary>Instance field (any visibility) of a reference-type owner: o => (object)o.field.</summary>
        public static Func<object, object> Field(FieldInfo f)
        {
            try
            {
                if (f == null || f.IsStatic || f.DeclaringType == null || f.DeclaringType.IsValueType || f.DeclaringType.IsGenericTypeDefinition || f.FieldType.IsPointer || f.FieldType.IsByRef) return null;
                var dm = new DynamicMethod("agi_get_" + f.Name, typeof(object), new[] { typeof(object) }, f.DeclaringType.Module, true);
                var il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, f.DeclaringType);
                il.Emit(OpCodes.Ldfld, f);
                if (f.FieldType.IsValueType) il.Emit(OpCodes.Box, f.FieldType);
                il.Emit(OpCodes.Ret);
                return (Func<object, object>)dm.CreateDelegate(typeof(Func<object, object>));
            }
            catch { return null; }
        }

        static Func<object, object> WrapProp<T, R>(MethodInfo m) where T : class
        {
            var d = (Func<T, R>)Delegate.CreateDelegate(typeof(Func<T, R>), m);
            return o => d((T)o);
        }

        static Func<object> WrapStatic<R>(MethodInfo m)
        {
            var d = (Func<R>)Delegate.CreateDelegate(typeof(Func<R>), m);
            return () => d();
        }
    }
}
