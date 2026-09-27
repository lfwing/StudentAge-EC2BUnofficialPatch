# 多 Mod 事件 ID 兼容

此功能集成在 UP 单 DLL 内。两个 Mod 新增相同事件 ID 时，为后续来源分配固定运行时 ID，让它们分别接受原版条件、概率、次数和队列调度；不会把一次按 ID 触发广播给所有同号剧情。

## 开启与迁移

在 `BepInEx/config/sa.EC2B.UnofficialPatch.cfg` 中设置，随后重启游戏：

```ini
[机制]
多mod事件ID兼容 = true
```

默认关闭。既有存档启用前应另存备份；原先已被原版合并丢弃的剧情历史无法自动还原。开启后请持续保留同一份映射，不要在同一周目随意关闭或删除映射。

独立测试补丁用户：移除 `StudentAge.EventCompat.dll`，开启 UP 内的开关。保留 `BepInEx/config/StudentAge.EventCompat/id-map.json`，UP 直接沿用原文件，不重新编号。两份插件同时存在时，UP 不再次映射，并暂停普通 CFG 的 F9 重载，日志提示完成迁移；其他 UP 功能照常加载。

## 原版覆盖规则

- 在首次读取 Mod CFG 前，记录游戏当前已加载的 `EvtCfg`、`TalkCfg`、`OptionCfg` 基线，包含已加载 DLC。
- 基线已有 ID 不改号。多个 Mod 覆盖同一原版 ID 时，仍按游戏的 Mod 加载顺序保留第一个；随后替代原版配置。因此原版事件与它的 Mod 覆盖事件只占一个位置，不会重复触发。
- 基线之外的新增 ID：第一个来源保留原 ID，其他来源得到别名。`id <= 1` 保留原版哨兵语义。
- 同一 Mod 的事件、对白、选项声明和已支持的引用一起改号；文本、角色 ID、奖励数量不做全局数字替换。
- Mod A 有意覆盖 Mod B 新增 ID，与两个 Mod 意外撞号在数据上无法自动区分。本功能把它们视为不同新增内容；这种补丁关系需要作者专门适配，不能承诺自动兼容。

## 映射与存档

映射记录原 ID 的所属 Mod、各 Mod 的运行时别名。即使首次只有一个 Mod，也会记录归属。新增 Mod、调整加载顺序或暂时停用原归属者，都不会抢走已有事件历史；删除过的条目保留映射占位。

运行时 ID 范围为 `8000000..15999999`，可被游戏效果数组中的单精度浮点数精确表示。按 Mod ID、表名、原 ID 生成候选并探测空位，最终结果保存到 `id-map.json`。映射跨存档共用，请连同存档备份；它不是可丢弃的缓存，不随 Steam 存档自动同步。

写入采用临时文件与原子替换，旧映射保存在 `.bak`。`.created` 标记存在但映射丢失、映射损坏、游戏/Mod 新声明占用已有别名，或者官方新 ID 与旧别名规则冲突时，拒绝自动重新分配。

启动计划失败时，整套兼容视图不生效，保留原版 CFG 加载行为并报错；此时不要继续覆盖依赖旧映射的存档，应修复原因后重启。F9 计划失败则保留当前局已生效的数据和映射。

## 支持的引用

核心链包括 `EvtCfg.talkId/options`、`TalkCfg.nextTalk/nextTalk2/option`、`OptionCfg.talkId/talkId2/nextEvtId`。

配置白名单还包含 Action/ActionEvt、AnimeCon、Basketball1On1、ExpoEvt/ExpoSite、TripSpot、EndingPart 的事件引用，以及 CG、GiftEvt、Intent、Interact、Item、LoveDraw、LoveGreeting、NpcActivity、MinigameAction、Negotiation、TalkInputMinigame 的对白引用。具体字段以 `Services/EventCompatibility/ReferenceRules.cs` 为准。

普通条件协议 `3` 的事件/选项/对白历史分支、`111` 事件变量，以及效果 `50` 的直接事件、事件变量、概率、队列和下一回合事件分支按参数位置改写。数量、类型和比较值保持原值。`[50,10]` 有共用事件 ID/数值参数的歧义：需要改号时拒绝整套计划，不猜测语义。DND 自有条件/效果协议不套用普通协议，只有 `gameEffect` 按原版游戏效果处理。

未列入白名单的自定义数组/字符串、DLL 写死 ID、UP 扩展注册表中的条件、其他 Mod 私有协议不会自动改写，需要作者适配。未声明本 Mod 目标的跨 Mod 裸 ID 继续指向原来的单一全局目标。

## 与 UP CFG 覆盖、F9 的关系

处理顺序为：原 Mod CFG → UP 同 Mod 增强 CFG 选择/注入 → ID 兼容视图 → 原版 LoadModCfgs/MergeCfgsAsync。所有转换仅写入 BepInEx 缓存，Workshop 原文件不变。

F9 使用已接受的原始文档重新规划，包括坏文件的上一次有效版本；不会把已改号的输出再改一遍。重载同时更新事件类型索引，保留玩家事件、对白、选项历史与变量。配置、索引或映射提交失败会回滚当前配置与索引。ID 占位只增不减，旧存档不依赖临时加载顺序。

F9 不重播历史、不重排已经入队的剧情，也不恢复正在显示的旧对白。修改正在执行或已经入队的剧情应保存后重启；开关和 Mod 集合变更也须重启。

## DLL 作者接口

引用 UP 后，在明确来源的调用点转换 ID：

```csharp
int runtimeId = EC2BUnofficialPatch.Services.EventIdCompatibilityApi.Resolve(
    workshopId, "EvtCfg", originalEventId);
```

表名支持 `EvtCfg`、`TalkCfg`、`OptionCfg`；`Applied` 表示当前启动计划已经生效。关闭或启动失败时返回原 ID。API 不自动拦截所有游戏调用，也不把一个事件触发扩展成多个触发。

## 验证

纯逻辑检查：`dotnet run --project tools/EventCompatibilityTests/EventCompatibilityTests.csproj`。

隔离 CrossOver 驱动及准备脚本位于 `tools/EventCompatibilityTests/`；需要本机游戏、BepInEx、UnityPy 和自行准备的合成测试存档，不提交或分发游戏程序集与玩家存档。验证结果与平台边界见 [验证记录](../../Docs/VALIDATION.md)。
