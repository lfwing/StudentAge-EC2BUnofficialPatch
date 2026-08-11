using System;
using System.Collections.Generic;
using Config;
using EC2BUnofficialPatch.Core;
using Effect;
using Increase;
using Sdk;
using TheEntity;
using UnityEngine;

namespace EC2BUnofficialPatch.Features.Effects
{
    internal static class AnimeEffectPatches
    {
        internal static bool GenEffectorPrefix(
            List<float> _effect,
            Effector _effector,
            int _toRoleId,
            int _fromRoleId,
            ref Effector __result)
        {
            if (_effect == null || _effect.Count == 0 || !Mathf.Approximately(_effect[0], 36f))
            {
                return true;
            }

            string arguments = FormatArguments(_effect);
            PatchLog.Debug(
                "效果模块-36effect 已命中 GenEffector 补丁：" +
                $"args={arguments}, previous={DescribeEffector(_effector)}, " +
                $"toRoleId={_toRoleId}, fromRoleId={_fromRoleId}");

            try
            {
                EffectorAnime effect = new EffectorAnime(_effector, _effect)
                {
                    // 看番数据只属于主角；避免事件上下文把长期被动错误挂到 NPC。
                    toRoleId = 0,
                    fromRoleId = _fromRoleId
                };

                if (!effect.IsValid)
                {
                    PatchLog.Error(
                        "效果模块-36effect 参数无效，未生成新效果：" +
                        $"args={arguments}, reason={effect.ValidationError}");
                    __result = _effector;
                    return false;
                }

                __result = effect;
                PatchLog.Debug(
                    "效果模块-36effect 生成成功：" +
                    $"args={arguments}, parsed={effect.DescribeConfiguration()}");
                return false;
            }
            catch (Exception exception)
            {
                PatchLog.Exception(
                    $"效果模块-36effect 生成失败 args={arguments}",
                    exception);
                __result = _effector;
                return false;
            }
        }

        private static string FormatArguments(List<float> effect)
        {
            return effect == null ? "<null>" : "[" + string.Join(",", effect) + "]";
        }

        private static string DescribeEffector(Effector effector)
        {
            return effector == null ? "<null>" : effector.GetType().FullName;
        }
    }

    internal sealed class EffectorAnime : Effector
    {
        private readonly int _subType;
        private readonly int _subType2;
        private readonly int _personAttrId;
        private readonly float _value;
        private readonly bool _valid;
        private readonly string _validationError;

        internal EffectorAnime(Effector previous, List<float> effect)
            : base(previous, effect)
        {
            if (effect == null)
            {
                _validationError = "参数列表为 null";
                return;
            }

            if (effect.Count < 2)
            {
                _validationError = "至少需要 [36,subType] 两个参数";
                return;
            }

            _subType = Convert.ToInt32(effect[1]);
            if (_subType == 1 || _subType == -1)
            {
                if (effect.Count < 4)
                {
                    _validationError = $"subType={_subType} 需要 [36,{_subType},personAttrId,value]";
                    return;
                }

                _personAttrId = Convert.ToInt32(effect[2]);
                _value = effect[3];
                _valid = true;
                return;
            }

            if (_subType == 2 || _subType == -2 || _subType == 3)
            {
                if (effect.Count < 3)
                {
                    _validationError = $"subType={_subType} 需要 [36,{_subType},value]";
                    return;
                }

                _value = effect[2];
                _valid = true;
                return;
            }

            if (_subType == 4)
            {
                if (effect.Count < 4)
                {
                    _validationError = "subType=4 需要 [36,4,subType2,value]";
                    return;
                }

                _subType2 = Convert.ToInt32(effect[2]);
                _value = effect[3];
                _valid = _subType2 == 1 || _subType2 == 2;
                if (!_valid)
                {
                    _validationError = $"subType=4 的 subType2={_subType2} 无效，只允许 1 或 2";
                }
                return;
            }

            if (_subType == 999)
            {
                if (effect.Count < 3)
                {
                    _validationError = "subType=999 需要 [36,999,subType2]";
                    return;
                }

                _subType2 = Convert.ToInt32(effect[2]);
                _valid = _subType2 == 1 || _subType2 == 2;
                if (!_valid)
                {
                    _validationError = $"subType=999 的 subType2={_subType2} 无效，只允许 1 或 2";
                }
                return;
            }

            _validationError =
                $"未知 subType={_subType}；支持 1、-1、2、-2、3、4、999";
        }

        internal bool IsValid => _valid;

        internal string ValidationError =>
            string.IsNullOrEmpty(_validationError) ? "未知参数错误" : _validationError;

        internal string DescribeConfiguration()
        {
            return $"subType={_subType}, subType2={_subType2}, " +
                   $"personAttrId={_personAttrId}, value={_value:0.###}";
        }

        public override void OnRun(float _rate = 1f, bool _toast = false)
        {
            if (!_valid)
            {
                PatchLog.Error(
                    "效果模块-36effect OnRun 被调用但参数无效：" +
                    ValidationError);
                return;
            }

            Role role = Singleton<RoleMgr>.Ins.GetRole();
            if (role == null)
            {
                PatchLog.Error(
                    "效果模块-36effect 执行失败：RoleMgr.GetRole() 返回 null；" +
                    DescribeConfiguration());
                return;
            }

            PatchLog.Info(
                "效果模块-36effect 开始执行：" +
                $"{DescribeConfiguration()}, rate={_rate:0.###}, toast={_toast}");

            try
            {
                switch (_subType)
                {
                    case 1:
                        if (Cfg.PersonAttrCfgMap == null ||
                            !Cfg.PersonAttrCfgMap.ContainsKey(_personAttrId))
                        {
                            PatchLog.Error(
                                "效果模块-36effect 执行失败：" +
                                $"PersonAttrCfgMap 中不存在属性 id={_personAttrId}");
                            return;
                        }

                        role.AddEffect(
                            RoleIncType.DoSomething,
                            AnimeExtensionIds.WatchFixedEffects,
                            new IncreaserAttr(null)
                            {
                                attrId = _personAttrId,
                                value = _value
                            },
                            this);
                        PatchLog.Info(
                            "效果模块-36effect 执行完成：" +
                            $"注册固定看番属性效果，trigger={AnimeExtensionIds.WatchFixedEffects}, " +
                            $"personAttrId={_personAttrId}, value={_value:0.###}");
                        return;

                    case -1:
                        if (Cfg.PersonAttrCfgMap == null ||
                            !Cfg.PersonAttrCfgMap.ContainsKey(_personAttrId))
                        {
                            PatchLog.Error(
                                "效果模块-36effect 执行失败：" +
                                $"PersonAttrCfgMap 中不存在属性 id={_personAttrId}");
                            return;
                        }

                        role.AddEffect(
                            RoleIncType.DoSomething,
                            AnimeExtensionIds.WatchLevelEffects,
                            new IncreaserOther(null)
                            {
                                otherAttrId = AnimeExtensionIds.AnimeAttrFromLevel,
                                id = _personAttrId,
                                value = _value
                            },
                            this);
                        PatchLog.Info(
                            "效果模块-36effect 执行完成：" +
                            $"注册等级看番属性效果，trigger={AnimeExtensionIds.WatchLevelEffects}, " +
                            $"sourceOtherAttr={AnimeExtensionIds.AnimeAttrFromLevel}, " +
                            $"personAttrId={_personAttrId}, value={_value:0.###}");
                        return;

                    case 2:
                        AddOtherAttribute(role, AnimeExtensionIds.AnimeCount, _value);
                        PatchLog.Info(
                            "效果模块-36effect 执行完成：" +
                            $"otherAttr={AnimeExtensionIds.AnimeCount}, value={_value:0.###}");
                        return;

                    case -2:
                        role.SetToggle(AnimeExtensionIds.AgainSearchUnlocked, 1f, this, true);
                        role.SetToggle(
                            AnimeExtensionIds.AgainSearchEnergyCost,
                            Mathf.Max(0f, _value),
                            this,
                            true);
                        PatchLog.Info(
                            "效果模块-36effect 执行完成：解锁再次找番，" +
                            $"unlockToggle={AnimeExtensionIds.AgainSearchUnlocked}, " +
                            $"costToggle={AnimeExtensionIds.AgainSearchEnergyCost}, " +
                            $"cost={Mathf.Max(0f, _value):0.###}");
                        return;

                    case 3:
                        role.UpdateAttr(361, _value * _rate, 1f, tag, 2);
                        PatchLog.Info(
                            "效果模块-36effect 执行完成：" +
                            $"attrId=361, delta={_value * _rate:0.###}");
                        return;

                    case 4:
                        int otherAttrId = _subType2 == 1
                            ? AnimeExtensionIds.AnimeGodWeight
                            : AnimeExtensionIds.AnimeGodPersonality;
                        AddOtherAttribute(role, otherAttrId, _value);
                        PatchLog.Info(
                            "效果模块-36effect 执行完成：" +
                            $"otherAttr={otherAttrId}, value={_value:0.###}");
                        return;

                    case 999:
                        if (_subType2 == 1)
                        {
                            role.SetToggle(
                                AnimeExtensionIds.ToggleAnimeSearch,
                                1f,
                                this,
                                true);
                            PatchLog.Info(
                                "效果模块-36effect 执行完成：" +
                                $"解锁搜索 toggle={AnimeExtensionIds.ToggleAnimeSearch}");
                        }
                        else
                        {
                            role.SetToggle(
                                AnimeExtensionIds.ToggleAnimeConvention,
                                1f,
                                this,
                                true);
                            Singleton<FuncMgr>.Ins.OpenFunc(52, true);
                            PatchLog.Info(
                                "效果模块-36effect 执行完成：" +
                                $"解锁漫展 toggle={AnimeExtensionIds.ToggleAnimeConvention}, funcId=52");
                        }

                        return;
                }
            }
            catch (Exception exception)
            {
                PatchLog.Exception(
                    "效果模块-36effect 运行异常：" + DescribeConfiguration(),
                    exception);
            }
        }

        public override string OnToString(float _rate = 1f, int _type = 0)
        {
            if (!_valid)
            {
                return null;
            }

            string signedValue = FormatSigned(_value * ((_subType == 3) ? _rate : 1f));
            switch (_subType)
            {
                case 1:
                    return $"每次看番时{GetPersonAttrName()}属性{signedValue}点";
                case -1:
                    return $"每次看番时{GetPersonAttrName()}属性变化（二次元等级×{FormatSigned(_value)}）点";
                case 2:
                    return $"每次找番时额外出现{signedValue}部";
                case -2:
                    return $"解锁再次找番功能（每自然年一次），消耗{Mathf.Max(0f, _value):0.##}点精力";
                case 3:
                    return $"二次元浓度{signedValue}点";
                case 4:
                    return _subType2 == 1
                        ? $"神作权重{signedValue}"
                        : $"神作对应人格{signedValue}";
                case 999:
                    return _subType2 == 1 ? "解锁搜索功能" : "解锁漫展功能";
                default:
                    return null;
            }
        }

        private void AddOtherAttribute(Role role, int id, float value)
        {
            role.AddEffect(
                RoleIncType.OtherAttrInc,
                id,
                new IncreaserAttr(null)
                {
                    attrId = id,
                    isOtherAttr = true,
                    value = value
                },
                this);
        }

        private string GetPersonAttrName()
        {
            PersonAttrCfg cfg;
            return Cfg.PersonAttrCfgMap != null &&
                   Cfg.PersonAttrCfgMap.TryGetValue(_personAttrId, out cfg) &&
                   cfg.name != null &&
                   cfg.name.Count > 0
                ? cfg.name[0]
                : $"角色属性{_personAttrId}";
        }

        private static string FormatSigned(float value)
        {
            return value >= 0f ? $"+{value:0.##}" : value.ToString("0.##");
        }
    }
}
