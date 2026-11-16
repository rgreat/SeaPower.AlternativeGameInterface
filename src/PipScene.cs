using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace AlternativeGameInterface
{
    /// <summary>
    /// HidePipScene (1.7.0): while the map is fullscreen the game shows the World View picture: its main camera is shrunk to a rectangle at the
    /// bottom left of the screen (the viewport of the picture), BlitToPipImage copies that frame into the picture's texture and also blits it to
    /// the screen as usual. Under the opaque map that rectangle is never seen; with a transparent Tactical Display it is a second, displaced
    /// 3D picture. This patch paints that rectangle black after the game's blit (the copy into the picture is done by then), so only the
    /// World View window shows the 3D scene.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {

        static MethodInfo _pipActive, _pipSource, _pipInstanceExists, _pipImage, _pipDownsample;
        static PropertyInfo _pipInstance;

        void HookPipScene(Harmony harmony, Assembly asm)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var blit = asm.GetType("BlitToPipImage");
            var cm = asm.GetType("SeaPower.CameraManager");
            var sg = asm.GetType("SeaPower.Singleton`1");
            var m = blit == null ? null : blit.GetMethod("OnRenderImage", all);
            if (m == null || cm == null || sg == null) { Trace("BlitToPipImage / CameraManager not found: HidePipScene not available"); return; }
            var g = sg.MakeGenericType(cm);
            _pipInstance = g.GetProperty("Instance", all);
            _pipInstanceExists = g.GetMethod("InstanceExists", all);
            var active = cm.GetProperty("IsPipActive", all);
            _pipActive = active == null ? null : active.GetGetMethod(true);
            _pipSource = cm.GetMethod("IsPipSource", all);
            _pipImage = cm.GetMethod("GetPipImage", all);
            _pipDownsample = blit.GetMethod("Downsample", all);
            if (_pipInstance == null || _pipInstanceExists == null || _pipActive == null || _pipSource == null || _pipImage == null) { Trace("CameraManager members not found: HidePipScene not available"); return; }
            TryPatch(harmony, m, "PipSceneBlitPrefix", "PipSceneBlitPostfix", null);
            Trace("patched BlitToPipImage.OnRenderImage (HidePipScene)");
        }

        /// <summary>This camera now is the source of the World View picture and the picture is being made.</summary>
        static bool PipSourceNow(object inst, out object cm)
        {
            cm = null;
            var ex = _pipInstanceExists.GetParameters().Length == 0 ? _pipInstanceExists.Invoke(null, null) : _pipInstanceExists.Invoke(null, new object[] { false });
            if (!(ex is bool) || !(bool)ex) return false;
            cm = _pipInstance.GetValue(null, null);
            if (cm == null || !(bool)_pipActive.Invoke(cm, null)) return false;
            var cam = ((Component)inst).GetComponent<Camera>();
            return cam != null && (bool)_pipSource.Invoke(cm, new object[] { cam });
        }

        // To the screen itself (destination null) the game's own blit is skipped after the copy into the picture: the rectangle keeps what the
        // rest of the screen has (the map's UI drawn over it), as if no camera drew there. To a texture (another image effect follows) the
        // rectangle is painted black after the game's blit instead.
        static bool PipSceneBlitPrefix(object __instance, RenderTexture source, RenderTexture destination)
        {
            long p = Prof.T();
            try { return PipSceneBlitCore(__instance, source, destination); } finally { Prof.Lap(Prof.PBlit, p); }
        }

        static bool PipSceneBlitCore(object __instance, RenderTexture source, RenderTexture destination)
        {
            var a = Active;
            if ((object)a == null  || source == null) return true;
            if (MapOn())
            {
                // WindowedMapAndWorldView: the cameras render into the World View texture themselves - no copy into the game's picture, the frame goes on as usual
                try { object c0; if (PipSourceNow(__instance, out c0)) { Graphics.Blit(source, destination); return false; } } catch { }
                return true;
            }
            if (destination != null) return true;
            try
            {
                object cm;
                if (!PipSourceNow(__instance, out cm)) return true;
                var img = _pipImage.Invoke(cm, null) as RenderTexture;
                if (img != null) { if (_pipDownsample != null) _pipDownsample.Invoke(null, new object[] { source, img }); else Graphics.Blit(source, img); }   // older builds have no Downsample: a plain blit
                return false;
            }
            catch { return true; }
        }

        static void PipSceneBlitPostfix(object __instance, RenderTexture source, RenderTexture destination)
        {
            var a = Active;
            if ((object)a == null  || source == null || destination == null || MapOn()) return;
            try
            {
                object cm;
                if (!PipSourceNow(__instance, out cm)) return;
                Graphics.Blit(Texture2D.blackTexture, destination);
            }
            catch { }
        }
    }
}
