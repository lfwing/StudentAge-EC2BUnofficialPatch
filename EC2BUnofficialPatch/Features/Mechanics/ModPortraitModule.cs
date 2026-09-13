using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Config;
using EC2BUnofficialPatch.Core;
using HarmonyLib;
using Sdk;
using TheEntity;
using UnityEngine;
using View.Main;

namespace EC2BUnofficialPatch.Features.Mechanics
{
    internal sealed class ModPortraitModule : IPluginModule
    {
        public string Key => "mechanics.mod-profile-portrait";
        public IReadOnlyList<ModuleLogItem> LogItems => new[] { new ModuleLogItem("机制", "Mod社交资料立绘与服装") };
        public void Load(Harmony harmony, PluginServices services)
        {
            harmony.Patch(AccessTools.Method(typeof(DetailSocialView), "RefreshProfile"),
                prefix:new HarmonyMethod(typeof(ModPortraitPatches), nameof(ModPortraitPatches.BeforeProfile)),
                postfix:new HarmonyMethod(typeof(ModPortraitPatches), nameof(ModPortraitPatches.AfterProfile)),
                finalizer:new HarmonyMethod(typeof(ModPortraitPatches), nameof(ModPortraitPatches.ProfileFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(UISprite), nameof(UISprite.SetTextureUrl)),
                prefix:new HarmonyMethod(typeof(ModPortraitPatches), nameof(ModPortraitPatches.TexturePrefix)));
            harmony.Patch(AccessTools.Method(typeof(RoleMgr), nameof(RoleMgr.GetExpressionIcon)),
                prefix:new HarmonyMethod(typeof(ModPortraitPatches), nameof(ModPortraitPatches.ExpressionPrefix)));
        }
    }
    internal static class ModPortraitPatches
    {
        private sealed class Layout
        {
            internal long Generation;
            internal Vector3 Scale, Position;
            internal Vector2 Size;
            internal UISpriteSizeType SizeType;
            internal bool Aspect;
            internal Action OriginalCallback;
            internal Action InstalledCallback;
            internal void Restore(UISprite icon)
            {
                icon.transform.localScale=Scale; icon.transform.localPosition=Position;
                icon.transform.sizeDelta=Size; icon.sizeType=SizeType; icon.image.preserveAspect=Aspect; icon.image.enabled=true;
                if (icon.endCallback == InstalledCallback) icon.endCallback=OriginalCallback;
                InstalledCallback=null;
            }
        }
        [ThreadStatic] private static DetailSocialView _profile;
        [ThreadStatic] private static Role _role;
        private static readonly ConditionalWeakTable<UISprite, Layout> Layouts = new ConditionalWeakTable<UISprite, Layout>();
        internal static void BeforeProfile(DetailSocialView __instance, Role ___role)
        {
            _profile=__instance; _role=___role;
            var icon=__instance.icon_role;
            if (icon != null && Layouts.TryGetValue(icon,out var layout)) { layout.Generation++; layout.Restore(icon); }
        }
        internal static Exception ProfileFinalizer(Exception __exception)
        { _profile=null; _role=null; return __exception; }
        private static Layout GetLayout(UISprite k) => Layouts.GetValue(k,icon=>new Layout { Scale=icon.transform.localScale, Position=icon.transform.localPosition, Size=icon.transform.sizeDelta, SizeType=icon.sizeType, Aspect=icon.image.preserveAspect });
        internal static bool TexturePrefix(UISprite __instance,string _url)
        {
            if (_profile==null || _role==null || !ReferenceEquals(_profile.icon_role,__instance)) return true;
            try
            {
                if (!Cfg.PersonCfgMap.TryGetValue(_role.id,out var cfg)) return true;
                int stage=Singleton<RoleMgr>.Ins.GetRole().GradeState;
                if (!MapRoleStaticClothPatches.IsStaticModRole(cfg,stage)) return true;
                int cloth=MapRoleStaticClothPatches.ResolveStaticCloth(cfg,Math.Max(0,_role.ClothId),stage);
                string path=RoleMgr.GetExpressionIcon(cfg,cloth,0,stage);
                if (string.IsNullOrWhiteSpace(path)) path=_url;
                if (path!=null && path.StartsWith("Mods",StringComparison.OrdinalIgnoreCase)) path=Singleton<ModCtrl>.Ins.GetFullUrl(path);
                if (string.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path)||!File.Exists(path)) return true;
                var layout=GetLayout(__instance); long generation=++layout.Generation;
                var icon=__instance;
                // Capture the request generation: slow previous character loads must not overwrite the new portrait.
                icon.image.enabled=false;
                ResMgr.LoadExternSpriteAsync(path,sprite=> {
                    if (icon.image==null || layout.Generation!=generation) return;
                    icon.image.enabled=true;
                    if (sprite!=null) icon.SetSprite(sprite);
                },false);
                return false;
            }
            catch(Exception e) { PatchLog.Warning("Mod资料图片加载失败，沿用原版："+e.Message); return true; }
        }
        internal static void AfterProfile(DetailSocialView __instance, Role ___role)
        {
            try
            {
                if (___role == null || !Cfg.PersonCfgMap.TryGetValue(___role.id, out var cfg)) return;
                int stage=Singleton<RoleMgr>.Ins.GetRole().GradeState;
                if (!MapRoleStaticClothPatches.IsStaticModRole(cfg,stage)) return;
                var icon=__instance.icon_role;
                if (icon?.image == null) return;
                var layout=GetLayout(icon);
                var parm=ReadLayout(cfg,stage);
                // PersonCfg coordinates describe the game's 1080-high portrait canvas.
                // Preserve PNG pixel aspect and apply exactly the same authored scale and offset in this smaller frame.
                float factor=Mathf.Abs(layout.Size.y * layout.Scale.y)/1080f;
                if (factor <= 0) factor=1;
                layout.OriginalCallback=icon.endCallback;
                Action apply=()=> {
                    if (icon.image == null) return;
                    icon.image.SetNativeSize(); icon.image.preserveAspect=true;
                    icon.transform.localScale=new Vector3(factor*parm.z,factor*parm.z,1);
                    icon.transform.localPosition=layout.Position+new Vector3(parm.x*factor,parm.y*factor,0);
                };
                layout.InstalledCallback=()=> { layout.OriginalCallback?.Invoke(); apply(); };
                icon.endCallback=layout.InstalledCallback;
                icon.sizeType=UISpriteSizeType.NativeSize;
                apply();
            }
            catch (Exception e)
            {
                if (__instance.icon_role!=null && Layouts.TryGetValue(__instance.icon_role,out var layout)) layout.Restore(__instance.icon_role);
                PatchLog.Warning("Mod社交资料立绘适配失败，保留原版显示："+ModuleHost.GetReason(e));
            }
        }
        internal static Vector3 ReadLayout(PersonCfg cfg,int stage)
        {
            var p=cfg.GetRoleUrlParms(stage);
            float x=p!=null&&p.Count>0?p[0]:0, y=p!=null&&p.Count>1?p[1]:0, scale=p!=null&&p.Count>2?p[2]:1;
            return new Vector3(Finite(x)?x:0,Finite(y)?y:0,Finite(scale)&&scale>0?scale:1);
        }
        private static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        // Native fallback swapped primary/middle images when an expression row was absent.
        internal static bool ExpressionPrefix(PersonCfg _personCfg,int _clothId,int _faceId,int _gradeState,Dictionary<int,ModFaceCfg> cfgMap,ref string __result)
        {
            if (_personCfg==null || !MapRoleStaticClothPatches.IsStaticModRole(_personCfg,_gradeState)) return true;
            var map=cfgMap??Cfg.ModFaceCfgMap;
            if (map==null) return true;
            long baseKey=(long)_personCfg.id*1000+(long)_clothId*100;
            if (baseKey<int.MinValue || baseKey+_faceId>int.MaxValue) return true;
            bool primary=(_gradeState==0&&_personCfg.url!=null&&_personCfg.url.Count>0)||(_gradeState>0&&(_personCfg.url2==null||_personCfg.url2.Count==0));
            if (map.TryGetValue((int)(baseKey+_faceId),out var exact))
            {
                string selected=primary?exact.icon_xx:exact.icon;
                if (!string.IsNullOrWhiteSpace(selected)) {__result=selected;return false;}
            }
            if (map.TryGetValue((int)baseKey,out var normal)) {__result=primary?normal.icon_xx:normal.icon;return false;}
            __result=null;return false;
        }
    }
}
