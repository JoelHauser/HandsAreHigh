using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Mercy
{
    // 0.20.1 (performance): compiled readers for members of optional mods (SAIN) found by
    // reflection. PropertyInfo/FieldInfo.GetValue on recurring paths cost an invoke each and box
    // every enum (SAIN's suppression state, its active layer): SAIN's ChooseEnemy alone runs every
    // 0.1 s per bot. Each reader is a DynamicMethod (castclass + call/ldfld, no allocation) built
    // once at resolve time.
    //
    // Behaviour is never left to the compiled code alone: the first VerifyCalls reads are made both
    // ways and compared; any exception or disagreement drops the compiled reader for good (logged
    // once) and the plain reflection read is used from then on, exactly as before 0.20.1.
    // No Unity or game types here, so tools/perftest runs this file as it is.
    internal static class FastAccess
    {
        internal static Action<string> Warn;   // set by the plugin (BepInEx log); null offline
    }

    internal sealed class FastGetter<R>
    {
        internal const int VerifyCalls = 8;

        private readonly string _name;
        private readonly Func<object, object> _slow;
        private Func<object, R> _fast;
        private int _verified;

        internal bool Compiled => _fast != null;
        internal bool Verified => _fast != null && _verified >= VerifyCalls;

        private FastGetter(string name, Func<object, object> slow, Func<object, R> fast)
        {
            _name = name;
            _slow = slow;
            _fast = fast;
        }

        // An instance property of a class (any visibility). Null when the property is missing.
        internal static FastGetter<R> For(PropertyInfo p)
        {
            if (p == null || p.GetGetMethod(true) == null) return null;
            return new FastGetter<R>(p.DeclaringType?.Name + "." + p.Name, o => p.GetValue(o, null), Compile(p));
        }

        // An instance field of a class (any visibility). Null when the field is missing.
        internal static FastGetter<R> For(FieldInfo f)
        {
            if (f == null || f.IsStatic) return null;
            return new FastGetter<R>(f.DeclaringType?.Name + "." + f.Name, o => f.GetValue(o), Compile(f));
        }

        internal R Get(object target)
        {
            Func<object, R> fast = _fast;
            if (fast != null && _verified >= VerifyCalls) return fast(target);
            R slow = Convert(_slow(target));
            if (fast == null) return slow;
            try
            {
                R quick = fast(target);
                if (!EqualityComparer<R>.Default.Equals(slow, quick))
                    Drop($"compiled read gave {Show(quick)}, reflection {Show(slow)}");
                else
                    _verified++;
            }
            catch (Exception e)
            {
                Drop(e.GetType().Name + ": " + e.Message);
            }
            return slow;
        }

        private void Drop(string why)
        {
            _fast = null;
            FastAccess.Warn?.Invoke($"{_name}: compiled reader disagreed with reflection ({why}); using reflection");
        }

        private static string Show(R v) => v == null ? "null" : v.ToString();

        // Reflection's answer in R: enums (and other integral values) as their int value.
        private static R Convert(object v)
        {
            if (v == null) return default;
            if (typeof(R) == typeof(int) && !(v is int)) return (R)(object)System.Convert.ToInt32(v);
            return (R)v;
        }

        private static bool Fits(Type t)
        {
            if (typeof(R) == typeof(int))
                return t == typeof(int) || (t.IsEnum && Enum.GetUnderlyingType(t) == typeof(int));
            return !t.IsValueType && typeof(R).IsAssignableFrom(t);
        }

        private static Func<object, R> Compile(PropertyInfo p)
        {
            try
            {
                MethodInfo get = p.GetGetMethod(true);
                Type owner = p.DeclaringType;
                if (get == null || get.IsStatic || owner == null || owner.IsValueType || get.GetParameters().Length != 0 || !Fits(p.PropertyType))
                    return null;
                var dm = new DynamicMethod("HAH_get_" + owner.Name + "_" + p.Name, typeof(R), new[] { typeof(object) }, typeof(FastGetter<R>).Module, true);
                ILGenerator il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, owner);
                il.Emit(get.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, get);
                il.Emit(OpCodes.Ret);
                return (Func<object, R>)dm.CreateDelegate(typeof(Func<object, R>));
            }
            catch { return null; }
        }

        private static Func<object, R> Compile(FieldInfo f)
        {
            try
            {
                Type owner = f.DeclaringType;
                if (owner == null || owner.IsValueType || !Fits(f.FieldType)) return null;
                var dm = new DynamicMethod("HAH_ld_" + owner.Name + "_" + f.Name, typeof(R), new[] { typeof(object) }, typeof(FastGetter<R>).Module, true);
                ILGenerator il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, owner);
                il.Emit(OpCodes.Ldfld, f);
                il.Emit(OpCodes.Ret);
                return (Func<object, R>)dm.CreateDelegate(typeof(Func<object, R>));
            }
            catch { return null; }
        }
    }
}
