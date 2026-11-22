using System;
using System.Reflection;

namespace AlternativeGameInterface
{
    /// <summary>
    /// A field / property of the game's types read through a delegate made once (FastGet) instead of FieldInfo.GetValue / PropertyInfo.GetValue (several times slower on Mono):
    /// the map label code reads a dozen of them for every label every frame. It is a drop-in for the FieldInfo / PropertyInfo it replaces (GetValue, SetValue, FieldType,
    /// PropertyType; an implicit conversion from both), so a place that assigns a FieldInfo keeps working. Writes stay reflection. The fields x / y of a UnityEngine.Vector2
    /// are read with GetFloat without boxing.
    /// </summary>
    sealed class Acc
    {
        readonly FieldInfo _f; readonly PropertyInfo _p; readonly Func<object, object> _g; readonly int _v2;   // _v2: 1 = Vector2.x, 2 = Vector2.y

        Acc(FieldInfo f) { _f = f; _g = FastGet.Field(f); if (f.DeclaringType == typeof(UnityEngine.Vector2)) _v2 = f.Name == "x" ? 1 : f.Name == "y" ? 2 : 0; }
        Acc(PropertyInfo p) { _p = p; _g = FastGet.Property(p); }

        public static implicit operator Acc(FieldInfo f) { return f == null ? null : new Acc(f); }
        public static implicit operator Acc(PropertyInfo p) { return p == null ? null : new Acc(p); }

        public object GetValue(object o) { return _g != null ? _g(o) : _f != null ? _f.GetValue(o) : _p.GetValue(o, null); }
        public object GetValue(object o, object[] index) { return GetValue(o); }
        public void SetValue(object o, object v) { if (_f != null) _f.SetValue(o, v); else _p.SetValue(o, v, null); }
        public void SetValue(object o, object v, object[] index) { SetValue(o, v); }
        public Type FieldType { get { return _f == null ? null : _f.FieldType; } }
        public Type PropertyType { get { return _p == null ? null : _p.PropertyType; } }

        /// <summary>A float field of a value type (a boxed Vector2): x / y without reflection.</summary>
        public float GetFloat(object o)
        {
            if (_v2 != 0 && o is UnityEngine.Vector2) { var v = (UnityEngine.Vector2)o; return _v2 == 1 ? v.x : v.y; }
            return (float)GetValue(o);
        }
    }
}
