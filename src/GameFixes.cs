using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace SmartWindows
{
    /// <summary>
    /// FixContactInfo (1.6.0): the "Information" window of a contact (right click on an unidentified contact) lists who detected it, by
    /// which sensor and how long ago. BaseVehicleViewModel.UpdateFrom takes the detection time from the epoch (Epoch.ToLive() - the live
    /// session clock, GameTime.missionSessionTime) but ages the detections and throws out the old ones with GameTime.missionElapsedTime
    /// (the whole mission's clock). After a loaded save or a restarted session the two differ: every detection is "older than its MaxAge" at once
    /// and the sensor / age fields stay empty. A transpiler makes UpdateFrom read missionSessionTime instead (via ContactClock).
    /// </summary>
    public partial class SmartWindowsPlugin
    {
        ConfigEntry<bool> _fixContact;
        static MethodInfo _elapsedGet;
        static FieldInfo _sessionField;
        static Func<float> _sessionFn;
        static Func<double> _elapsedFn;

        void BindGameFixConfig()
        {
            _fixContact = Config.Bind("General", "FixContactInfo", true,
                "Information window of a contact (who detected it, by which sensor, how long ago): the detections are aged by the same clock the game " +
                "stamps them with (the game mixes two clocks, the fields stay empty in a loaded game).");
        }

        void HookGameFixes(Harmony harmony, Assembly asm)
        {
            var bvm = asm.GetType("SeapowerUI.ViewModels.BaseVehicleViewModel");
            var upd = bvm == null ? null : bvm.GetMethod("UpdateFrom", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var gt = asm.GetType("SeaPower.GameTime");
            _elapsedGet = gt == null ? null : gt.GetProperty("missionElapsedTime", BindingFlags.Static | BindingFlags.Public).GetGetMethod(true);
            _sessionField = gt == null ? null : gt.GetField("missionSessionTime", BindingFlags.Static | BindingFlags.Public);
            if (upd == null || _elapsedGet == null || _sessionField == null) { Trace("BaseVehicleViewModel.UpdateFrom / GameTime not found: FixContactInfo not available"); return; }
            try
            {
                // direct delegates: ContactClock runs for every contact on every update, reflection calls would be slow and allocate
                var sf = new System.Reflection.Emit.DynamicMethod("SW_Session", typeof(float), Type.EmptyTypes, typeof(SmartWindowsPlugin), true);
                var il = sf.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldsfld, _sessionField);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                _sessionFn = (Func<float>)sf.CreateDelegate(typeof(Func<float>));
                if (_elapsedGet.ReturnType == typeof(double)) _elapsedFn = (Func<double>)Delegate.CreateDelegate(typeof(Func<double>), _elapsedGet);
            }
            catch (Exception e) { Trace("FixContactInfo delegates: " + e.Message); _sessionFn = null; _elapsedFn = null; }
            try
            {
                harmony.Patch(upd, transpiler: new HarmonyMethod(typeof(SmartWindowsPlugin).GetMethod("ContactClockTranspiler", BindingFlags.Static | BindingFlags.NonPublic)));
                Trace("patched BaseVehicleViewModel.UpdateFrom (FixContactInfo)");
            }
            catch (Exception e) { Trace("FixContactInfo patch failed: " + e.Message); }
        }

        static IEnumerable<CodeInstruction> ContactClockTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var mine = typeof(SmartWindowsPlugin).GetMethod("ContactClock", BindingFlags.Static | BindingFlags.NonPublic);
            int n = 0;
            foreach (var ci in instructions)
            {
                var m = ci.operand as MethodInfo;
                if (m != null && ci.opcode == System.Reflection.Emit.OpCodes.Call && m == _elapsedGet) { ci.operand = mine; n++; }
                yield return ci;
            }
            Trace("FixContactInfo: " + n + " call(s) of missionElapsedTime redirected");
        }

        static double ContactClock()
        {
            var a = Active;
            if ((object)a != null && a._fixContact.Value && _sessionField != null) return _sessionFn != null ? _sessionFn() : (float)_sessionField.GetValue(null);
            return _elapsedFn != null ? _elapsedFn() : (double)_elapsedGet.Invoke(null, null);
        }
    }
}
