# STATUS 历史备份：2026-10-11 及之前

- 归档日期：2026-10-19。
- 来源：[STATUS.md](STATUS.md)。从“当前候选：MapExtend独立版与双发行版兼容（2026-10-11）”起的旧记录按原顺序完整保留；其后的无独立日期说明随所属历史上下文一并归档。
- 以下候选、版本、哈希和验收结论均为当时记录，不代表当前最新实现；正文未作改写。2026-10-18 记录及后续计划保留在 STATUS.md。

---

## 当前候选：MapExtend独立版与双发行版兼容（2026-10-11）

- 独立工程位于相邻的 D:/codex/MapExtend，BepInEx插件名MapExtend、GUID sa.EC2B.MapExtend、初版1.0.0。所有地图源码、100,1移动和回家过渡、子地点、对象、状态、目录发现、图片缓存、CFG/F9依赖及作者工具均随工程带出；无UP程序集硬依赖，未带入其他UP模块。
- UP作为可选前置优先加载。已有地图提供者时独立版不发现内容、不创建会话、不注册地图/效果/保存/F9；UP地图关闭时独立版接管。效果分支独立识别已有提供者；原版CFG捕获共用一份，来源及刷新转交实际捕获者，子地点重建/根地点查询转交实际楼层提供者，刷新后恢复实际地图提供者的功能160。独立F9可接入UP同次调度，避免第二次原版CFG刷新。
- 两版模板由共用MapExtendAuthorTemplates生成到原EC2BUnofficialPatch/MapExtend位置，三种Mod布局、相对路径、数据格式和WorldMaps.v2键完全保持；作者工具内容相同，Mod无需转换。保留之前新档修复和原版家中模糊，不增加对原版物件介绍的改写。
- 177项真实双DLL检查、414项UP回归、58项地图、109项场景/ZIP、17项缓存/抢救共775项通过。Unity UI提供者去重使用Harmony元数据策略验证，效果及CFG捕获实际安装；存档互读使用原版内存模型。不能视为游戏内渲染、完整磁盘存档或实际F9键盘验收。外部小游戏9101～9113继续只读注册通过。
- UP1.0.23 DLL：885248字节，SHA-256 `3b581c47baabdf4e3b3a26ee4cb2932f6395fd09dda91c34ec940bcfd13953e5`；独立MapExtend1.0.0 DLL：356352字节，SHA-256 `58d1e04f3b945385c9432cd2812b0060c2e17fe26646ac31ced35faca3fc314c`。两工程发布包与清单同步。共存须使用本轮配套UP，旧1.0.23测试DLL不代表具备此完整兼容补丁。未安装至游戏/Workshop、提交或发布。见qa/MapExtend-Standalone-Compatibility.json及独立工程Docs/VALIDATION.md。

## 之前候选：保留原版家中模糊与UP普通图（2026-10-11）

- 用户确认上一候选的开新档黑屏已解决，但家中模糊被擅自移除、UP对象普通图仍未在介绍中保留。撤回上一候选针对原版 MainView 四物件和 MapSceneView.ShowIntroduction 的介绍改写；原版背景参数及遮罩不改动，不再统一清晰度。
- UP新增对象介绍携带本次会话，背景参数固定-1，使 CommonTalkView 不再通过 BgCfg 另载原图。打开介绍前退出悬停；家中不叠加前景背景，普通图留在原场景与原版模糊之后。地图场景复用当前已显示背景精灵到对话现有 icon_bg，将普通图副本放在该背景内部；复制二维几何，local Z固定0，避免落回来源视图深度。随条件和100,11显示指令同步，关闭后撤销副本并恢复缓存对话背景属性/几何。
- 上一候选复制世界Z深度的代码存在层级风险，但不将它宣称为用户实机问题的唯一已证实原因。DLL源码修正与离线回归已完成，Unity实际渲染仍待复测；本轮未启动游戏或改写游戏/Workshop文件。新档修复未改动，原版事件队列、配置与存档键保持。
- 原版 SetIsGuideMode 异常复现及六种初始化回归继续通过。介绍检查改为实际安装器IL不目标化原版物件方法、原版ShowTalk调用保留、UP参数禁用BgCfg原图请求及不同视图深度的二维放置。414项实际DLL、109项场景、58项地图、17项缓存/抢救，共598项通过；外部小游戏9101～9113只读验证通过。net472 Release及本地发布构建0错误、1个既有BetterAudio CS0414警告。
- DLL1.0.23：880128字节，SHA-256 `5e9a93e06aaaf14cbb4a86a142ffe187f83699c8194e15f0eeda47a16dfa769c`。dist/merged、dist/release、三份update.json、内嵌作者说明/工具、松散模板与AuthorKit/Test ZIP同步，CRC及逐文件一致性通过。证据qa/Introduction-PreserveNativeBlur-Validation.json；未安装、提交或发布。

## 之前候选：新档功能表冲突与物件介绍背景（2026-10-11）

- 移除 FuncData.OpenFunc 的无条件写 FuncCfgMap 前缀；在 SetIsGuideMode 的原版 foreach 之前登记内置160。EnsureFunction 对同一行不重复写表；新档/读档既有登记与普通 CFG 热重载后登记保留，热重载仅调用本程序集实际安装的地图提供者。
- UP 对象介绍及原版家的日历/沙发/水果/奖杯、MapSceneView.ShowIntroduction 使用同一清晰背景展示：携带本次打开的场景会话，在 CommonTalkView 文本下方复制当前背景与所有应显示的普通图，退出悬停。按实际位置、尺寸、缩放、旋转及顺序同步；显示条件与100,11覆写继续生效。复制层无点击，未复制按钮/角色/HUD，精灵继续由原场景管理；关介绍、关场景或销毁时释放复制层。原版事件队列处理保持。
- 作者 readme、导出 readme 与内嵌模板同步；CFG格式、对象配置和存档键不变。
- 实际安装游戏程序集 SHA-256 与2026-09-25托管快照一致。检查程序用旧前缀复现原版 SetIsGuideMode 集合修改异常；新补丁覆盖 GuideOn/GuideOff/Dev、缺少/已有160及重复登记，验证原版开放结果。五个原版介绍方法的实际IL改写保留其他调用并传入所属视图；416项实际DLL检查通过，包含原有外部小游戏9101～9113只读验证。离开Unity的检查程序无法安装/渲染原版UI钩子（ECall依赖），因此介绍合成与缩放须实机确认，不列为已完成视觉验收。
- 本轮交付：net472 Release及本地发布构建均0错误、1个既有BetterAudio CS0414警告；416项实际DLL、109项场景、58项地图、17项缓存/抢救检查，共600项通过。DLL1.0.23为880640字节，SHA-256 `91d1f784a7c1ab53419d3e7a5776d32f78b623dfb67d94135355adce919ae8e1`。dist/merged、dist/release、三份update.json、内嵌作者说明/工具及AuthorKit/Test ZIP同步。未安装到游戏或Workshop，未提交或发布。

## 之前候选：场景二次确认、删除与物件效果（2026-10-11）

- MapId/BgId 下增加“确认场景（创建 / 切换）”；输入不创建中间场景、不改当前编辑范围或对象归属。未确认 ID 阻止新增对象、导入参考图及导出；已确认空场景保存到同一 SceneObjectscfg.json 的 scenes，可导出重开。
- 已保存场景可删除。含对象/参考图/自定义尺寸时再次确认；删除绑定对象、选区、参考图、草稿、载入请求及独占 PNG/PersonCfg 材料，共享图片和人物行保留。取消不改内容，删除后清空画布；不删除原磁盘文件或全局 MapCfg/BgCfg。
- 导出安装说明推荐 Mod/EC2BUnofficialPatch/MapExtend，也允许 Mod/MapExtend；原版 Cfg 仍由作者注册。后续仅 MapExtend 剥离版为计划记录，本轮不制作。
- WorldMapEffects.cs 更名 MapExtendEffects.cs；增加100,11,1/-1/2/-2,PersonId显示/隐藏普通图及允许/禁止点击悬停。条件未被改写，未执行指令时按条件；执行后对应方面采用最后明确状态，普通图/点击互不影响，WorldMaps.v2.object-normal/object-click独立保存。人物须注册并绑定启用的SceneObjects及所需图片；同PersonId共享状态。背景范围不绕过，旧schema=1也支持。
- 100,1和共用MapMoveService归入MapExtend，保留UP原移动与回家过渡。移动/物件补丁分开，按前缀契约去重；第二地图模块识别已有提供者并跳过重复运行时。实际Harmony测试覆盖两种owner顺序和另一程序集的同名提供者；未来剥离版仍须遵循此契约，未声称尚未制作的独立DLL已实机联动验收。
- 109项场景、58项地图、17项缓存/抢救、368项实际DLL检查，共552项通过。含删除取消/共享资源、空场景重开、四指令与非法参数、独立覆写、旧格式及原版内存存档恢复；Workshop外部小游戏9101～9113继续只读注册验证成功。
- 浏览器实测手填ID无中间场景、确认创建、删除含对象场景、保存已确认空场景、ZIP导出及安装说明；下载ZIP CRC通过，删除场景的对象/专属PNG/人物行未复活，剩余两个对象及三个场景（含空场景）正确。取消删除由控制器检查覆盖，浏览器自动化确认弹窗未取得独立取消证据。截图qa/SceneConfirm-Workflow.png，下载包qa/SceneConfirm-BrowserExport.zip。
- net472构建0错误、1个既有BetterAudio CS0414警告。UP1.0.23 DLL 872960字节，SHA-256 `ac02561f821bf79b866a4e05e050914ed6397916d0072981b50fb9f0989958e4`；dist/merged、dist/release、三份update.json、内嵌作者工具、AuthorKit/Test ZIP同步。游戏内动态显示/禁用点击与完整磁盘存档、双发行版视觉实例仍待实机；未安装至游戏/Workshop，未提交或发布。

## 之前候选：文件引用失效修复与编辑抢救（2026-10-11）

- 后续用户报错`Cannot read properties of null (reading 'image')`已定位到抢救脚本读取地点visual.normal/hover；这些可选图允许null。收集图片改为遍历配置中的非空图片，跳过空资产，不调用旧imagePaths，保留原JSON的null值。17项缓存/抢救（新增空图与空资产）、97项场景检查通过；已更新旧页抢救脚本、说明、内嵌工具、作者包、DLL和三份update.json。最新DLL仍864768字节，SHA-256 `53fdbac534aed42a23c354124e551f298f04f2bb19f155f336ed25ec67effdac`；清单及作者包一致性通过。用户原Chrome仍需执行修正后的脚本，未声称其内容已经备份成功。

- 定位旧导出在打ZIP时才读取retainedFiles和PNG的本地File引用，源文件被替换、移动或权限变化可触发NotReadableError并中断整包。导入目录、地图PNG和场景PNG现在读成功即复制为内存Blob；正常导出不依赖原磁盘引用。参考图仍只用于定位、不导出。
- 导出读取失败显示具体相对路径及保留页面提示；新增“抢救当前编辑”，保留标准配置/选区/能读取的图片，尽量由已解码PNG恢复不可读图片。抢救说明列出重建或缺失文件，另保留未保存表单与人物草稿供核对；这些紧急材料不进入普通模组包。
- dist/release/MapExtend-抢救当前页面.txt、MapExtend-抢救说明.html供旧版仍打开页面执行，无需刷新/重导入。旧版本没有新增解码接口时仍能备份配置与可读取图片，不能取回的图片明确列出；未宣称用户当前Chrome内容已抢救，需用户在原页面执行并检查下载包。
- 15项文件缓存/失败抢救、97项场景工具、58项地图及337项实际DLL检查，共507项通过。实际浏览器导入三场景四对象后替换测试原图，正常包和抢救包均导出成功；四张PNG逐字节与原内容一致，归属/坐标/选区保留，普通包无紧急材料/Editor，抢救包说明与表单齐全，ZIP CRC及发布一致性通过。截图qa/FileRecovery-Workflow.png。
- net472编译0错误、1个既有BetterAudio CS0414警告。DLL 1.0.23：864768字节，SHA-256 `fc8ec57d60f92483be72b5a5b4d7c3fecb2e3ba66109b7107f8f6a2ab9658db4`；dist/merged、dist/release、三份update.json、作者包和测试包同步验证。未写游戏或Workshop，未提交或发布；原版运行与注册机制未改。

## 之前候选：多场景、多对象编辑隔离（2026-10-11）

- 修复场景 ID 输入与草稿保存耦合导致旧对象归属改写；MapId/BgId 只切换编辑范围。对象列表及画布按两项 ID 联合过滤，同场景的所有图片和选区同时保留，切换或删除最后一个对象不会带入其他场景。
- PNG/参考图异步载入绑定发起时的对象/场景，后到旧请求及被删对象的请求不会写入新选择；切换清除拖动、悬停、顶点与文件输入状态。
- 新增“调整此对象的所属场景”以主动迁移或校正旧版保存错误的归属，保留图片位置/尺寸/选区；不根据自定义对象名自动猜测归属。仍使用同一SceneObjectscfg.json导出重开，不新增Editor。
- 58项地图、97项场景工具、337项实际DLL读取/注册检查，共492项通过。覆盖同地点不同背景、多个地点、多对象、手填ID、空场景、删除、异步请求顺序、归属校正及重开。实际浏览器三场景四对象导出、重启、重开成功，控制台无错误；ZIP逐项核对归属、选区、图片及纯净PersonCfg相同。证据qa/SceneIsolation-Reopen.png、qa/SceneIsolation-BrowserExport.zip。
- net472编译0错误、1个既有BetterAudio CS0414警告。DLL 1.0.23：858112字节，SHA-256 `25cb5b1f44e386878a98a32060a4b6460db3578fd457098187b412ace6c04b0c`；dist/merged、dist/release、三份update.json、作者包与测试包同步并验证。未写游戏或Workshop，未提交或发布；本轮修复编辑器，不改游戏对象运行机制。

## 之前候选：模组内容直接重开，不导出Editor（2026-10-10）

- “打开已有项目”统一选择导出的MapExtend模组内容。SceneObjectscfg.json恢复对象、绑定、条件、点击行为和运行图片；可选scenes数组保存mapId/bgId/width/height，运行时继续按objects读取，编辑器无需另一份项目资料。
- 删除Editor目录及制作资料的生成与打包；旧包中的整个Editor目录在重开时忽略，重新导出不夹带。场景参考图只在当前编辑会话定位使用，不进入模组包，界面及说明明确提示玩家需自行额外导入；缺少参考图不阻止读取对象或再次导出。
- 没有scenes设置的既有模组JSON按对象MapId/BgId恢复场景，默认宽高2560×1440可调。新导出JSON保存宽高，保持重开后的坐标尺寸；普通图/悬停图仍按配置及相对路径恢复，纯净PersonCfg注册材料不变。
- 58项地图、71项场景工具、337项实际DLL读取/注册检查通过，共466项；场景测试调用实际exportPackage，覆盖旧Editor过滤、不导出参考图、同JSON恢复宽高、无参考图重开/导出和旧JSON默认尺寸。
- 浏览器实际读取旧包、额外导入参考图、导出并重新打开无Editor的包成功，控制台无错误；实际下载ZIP确认无Editor、无参考PNG，恢复812/9012、对象812-9012-xx及纯净PersonCfg 990501，CRC与发布包检查通过。截图qa/UnifiedScene-Workflow.png，下载包qa/UnifiedScene-BrowserExport.zip。
- net472编译0错误、1个既有BetterAudio CS0414警告。DLL 1.0.23：852992字节，SHA-256 `23900b3754983f9cbe812b8e84b1f1d2fe6b1e086cef870e64da0febf9b418a0`；dist/merged、dist/release、三份update.json、作者包与测试包同步。未写游戏或Workshop、未提交或发布；游戏内显示、位置及过渡仍需实机确认。

## 之前候选：场景手填绑定与纯净人物行（2026-10-10）

- 删除 SceneLayout 导入及运行时生成，MapId / BgId 由作者手动填写，无需先注册地点、绑定背景并进入游戏。场景坐标宽高默认2560×1440并支持手动调整；这是编辑器默认值，程序集参考不包含背景prefab尺寸。
- “场景”页导入定位参考图并保存手填设置，多场景资料写入 Editor/SceneSources.json；对象默认名字 MapId-BgId-xx，可自定义。其余四步、独立条件、图片操作、交通强制开放及共用回家过渡保持。
- 删除 PersonCfg 导入入口，人物行仍通过独立按钮生成。导出 Cfg/PersonCfg.json 只包含对象对应的 id、name、init=[-1]、introduction、clickAudio；不夹带整份原版人物表或无关字段。重开旧项目剔除无关人物行和旧SceneLayout制作资料；作者自行按原版Mod流程注册。
- 58项地图流程、66项场景工具、337项实际DLL读取/注册检查通过，共461项。浏览器从空项目手填812/9012、使用建议名字812-9012-xx生成对象及PersonCfg 990501、导出并重新打开验证通过，控制台无错误；真实ZIP只含该人物行，不含SceneLayout，CRC、参考图及内嵌脚本/说明、发布包一致性通过。
- net472编译0错误、1个既有BetterAudio CS0414警告。DLL 1.0.23：852480字节，SHA-256 `4af1ce6b7559bb9d50c0eb542b3b47e40c254b20e59d0d48ff229053922abea5`；dist/merged、dist/release、三份update.json、作者包与测试包同步。
- 浏览器截图 qa/ManualScene-Workflow.png（测试参考图），实际下载包 qa/ManualScene-BrowserExport.zip。游戏内对象位置、背景切换及回家过渡仍待实机确认；未写入游戏或Workshop，未提交或发布。

## 之前候选：场景对象编辑、交通入口与回家过渡（2026-10-10）

- 场景编辑采用“场景 / 对象 / 点击行为 / 图片与区域”四个并排小流程；参考图、SceneLayout 坐标及人物表必须导入，随项目保存在 Editor 制作资料中。
- 对象先填写所属 MapCfg ID 和作者命名，内部 key 不展示；PersonCfg ID、介绍和音效单独生成/更新原版行，不生成 kzoneHeadId。未生成的人物表改动阻止导出，同人物多个对象不误判旧草稿。
- schema=2 将普通图显示条件与可点击条件分开，采用原版逗号/英文分号输入并转为嵌套数组；四种行为为介绍、介绍加效果、切换背景、切换场景，仅展示各自参数。实际背景 ID 绑定及条件分别控制普通图、点击和悬停；旧 schema=1 仍可运行，但非空旧项目不隐式迁移到新编辑流程。
- 参考图支持缩放/平移，两图分别调整位置、尺寸与比例，悬停图替换普通图；支持矩形、多边形与透明度点击，修复多边形前两个顶点范围为零时无法继续绘制的问题。浏览器预览只检查几何，不模拟游戏条件或效果。
- 交通入口勾选后锁定自动开放，父地点清单不能间接取消；取消交通资格后恢复手动调整，保留已勾自动开放。后端自动开放显式清单及交通父地点，不连带开放全部子地点，保持鹅城12原版剧情例外及存档一次初始化。
- 共用 MapMoveService 的100,1,1回家路径增加原版圆形遮罩过渡；编辑器场景移动与UP原有指令均使用此修复，已有遮罩时复用当前过渡。
- 58项地图流程、60项场景工具、337项实际DLL读取/注册检查全部通过，共455项；包含三种目录布局、schema=2、旧格式及未改动Workshop小游戏9101～9113注册。浏览器实际导入、四流程操作、独立图片变换、条件及人物行生成、ZIP导出和重新打开通过，控制台无错误；实际下载包的CRC、PNG字节、制作资料、CFG与嵌套数组核验通过。
- net472编译0错误、1个既有BetterAudio CS0414警告。DLL 1.0.23：854016字节，SHA-256 `ebe9ce5b1f5f65e0a21e8e3bbb6658c14e4005208d7a2250a9ecc4bfb9d88dd5`；dist/merged、dist/release、三份update.json、作者包与测试包一致，内嵌工具及说明与当前源文件一致。
- 浏览器截图：qa/SceneEditor-Workflow.png（测试参考图）；实际下载包：qa/SceneEditor-BrowserExport.zip。游戏内背景切换、显示/点击及回家过渡尚需实机确认；未写入游戏安装或Workshop目录，未提交或发布。

## 历史记录：交通入口必须自动开放（2026-10-09；已在2026-10-10候选实现）

- 父地点勾选“作为交通入口”后，“大地图解锁时自动开放此父地点”必须保持勾选，并禁止取消；交通入口是必需开放的父地点。
- 第二步的父地点 ID 清单同样不能间接移除交通入口的自动开放资格，前端勾选、清单同步、保存/导出和后端读取须遵循同一约束。
- 取消“作为交通入口”后，自动开放选项恢复可调整，不自动取消已经勾选的自动开放；单独勾选自动开放仍不反向设置交通入口。交通入口的子地点仍独立选择，不随父地点强制全部开放。
- 本要求修正下面候选中的“主动移除交通入口自动开放会被尊重”行为。具体实现及相关模板、说明、回归检查待场景对象编辑分析完成后，与本次对话提出的修复点统一处理。
- 本段为当时记录；实现与验证结果见顶部2026-10-10候选。

## 之前候选：地图编辑流程与自动开放清单（2026-10-09）

- 按四图调整资料顺序与展开状态；Worldmapscfg 导入紧邻项目打开且不加分隔线，校准默认展开，首次制作说明放到 MapLayout 后，增加原图推荐。
- 新大地图用作者填写的数字 ID 和可选名称创建，空名称为新区域<ID>；父地点 ID 不再自动分配。父地点/子地点名称和自动开放文案统一。
- 第二步父地点 ID 清单与第三步勾选双向实时同步，交通入口勾选单向补勾自动开放。编辑父地点清单保留子地点的选择，主动移除自动开放不会在保存时补回。
- 子地点取消类型选择，固定 type=2；按原版 mapId/1000 校验父子关系、占用与 int32 范围，非法输入不会先生成/覆盖父地点或提交待保存 CFG。尾号000符合规则但明确注明不推荐，建议从001开始。原版证据 assembly_ref/Assembly-CSharp/View/Main/MapSceneView.cs:400，已内部比对受管快照；当前游戏 DLL SHA-256 仍为0d004b3a8c7fdd2ac41e4db099293db112d6b24dc2a14086e9c674283f1378ea。
- 后端只按显式 autoUnlockMaps 初始化，不再隐式添加 travelMaps 和其 floors；仍排除鹅城12，保留只初始化一次的存档语义，不重置既有进度。旧包依赖隐式开放时需要补清单，旧 Mod 显式 floors 的兼容不变。
- 52 项地图编辑检查、40 项工具/ZIP 检查通过；330 项实际 DLL 读取/注册检查通过，包含4项自动开放选择回归与未修改的 Workshop 3802190178 的9101～9113注册。浏览器核验空ID拒绝、名称默认、交通补勾、列表移除取消勾选、非法子地点提示、示范项目导入、000提示和完整ZIP导出；实际导出移除父地点33后保留31001/32001/33001，PNG/CFG/脚本/readme核验通过，页面控制台无错误。
- net472 编译0错误、1个既有CS0414。DLL 1.0.23：827904字节，SHA-256 `f40f21a3bcc350eed6c379b9485f68ea884b417355707b8681b2b2dc52d936c6`；dist/merged、dist/release、三份update.json、作者包和测试包一致。
- 前端及说明内嵌至DLL，模板生成位置及写出流程保持。截图 qa/MapEditor-ChildId-Preview.png，实际浏览器ZIP qa/MapEditor-BrowserExport.zip。游戏内初始化/解锁仍需实机验收，未改动游戏或Workshop文件，未发布。

## 之前候选：MapExtend 统一制作台（2026-10-09）

- map-extend.html 合并大地图/地点与场景对象编辑。共享项目文件、PNG 和 MapCfg/BgCfg/PersonCfg/EvtCfg/TalkCfg 材料，统一导出三份扩展 JSON；scene-objects.html 仅保留跳转到 #scene 的旧入口。
- 五步流程：项目与资料、大地图、地点与标点、场景对象、检查与导出。地点页可直接为当前地点创建对象；对象草稿跨步骤自动保存并保留，Ctrl+S 保存当前编辑，未导出关闭提醒。仅制作对象无需新增大地图。
- UI 使用蓝白高对比、斜切的当前菜单、明确的中文标签；项目/导出用总览，编辑时展示对应画布和分组参数。场景画布保持坐标比例，支持多边形编辑及普通/悬停图层预览。
- 40 项工具数据/ZIP 检查、326 项实际 DLL 读取/注册回归通过。浏览器验证：对象独立制作与导出、已有完整项目导入、地点关联、切步骤保留介绍、地图与对象共同导出；下载 ZIP 另核验 CRC、JSON、CFG 和原始 PNG 字节。页面无控制台错误。离线入口的 file:// 预览被应用浏览器策略拒绝，交付本地 HTML 供作者直接打开。
- net472 编译 0 错误、1 个既有 CS0414 警告。DLL 1.0.23：821760 字节，SHA-256 `98ef5224f9a68cb3db8f04751bd3de676ed0ea98ebb098674249d3693f7560f9`；dist/merged、dist/release、三份 update.json、作者包与测试包一致。
- 不改变地图运行时协议、原版表注册规则或游戏/Workshop 文件；游戏内功能验收范围沿用之前候选。

## 之前候选：MapExtend 归并与场景对象（2026-10-09）

- 指定 Local 工作副本；旧工程目录被 Windows 阻止改名，保留原目录并完整复制（含未提交内容和 Git 数据），未删除或重置原文件。
- 源码集中到 Features/Mechanics/MapExtend，子地点服务及补丁移入。地图扩展与优化默认开启，包含多mod楼层合并；独立地图子地点扩展默认关闭，配置紧邻。旧配置迁移保留用户值。
- 作者内容及工具导出统一 MapExtend；旧 WorldMaps 读取兼容，存档协议保持。场景对象正式关联 PersonCfg，支持背景动态匹配、物件事件/介绍、同区域场景切换、背景切换、功能和原版效果、PNG/多边形点击、F9刷新及释放。
- 新增 scene-objects.html，主工具链接和完整包同步纳入；只生成原版注册材料，不写 Workshop 配置。
- net472 构建通过，0 错误、1 个既有 CS0414 警告。326 项实际 DLL 离线检查通过，覆盖配置迁移/顺序、三种布局、旧目录兼容、对象配置及外部小游戏 9101～9113 注册回归；35 项作者工具数据与 ZIP 导出检查通过，涵盖相对路径、独立悬停图层、项目文件保留、JSON 和内嵌工具同步。浏览器页面可打开，但会话随后失效，完整交互仍未验收。
- DLL 1.0.23：834048 字节，SHA-256 `4eb760aac20967df9d25765c328a0fe02f938ddbe18f32700ad55e826a8df680`。dist/merged、dist/release、三份 update.json、MapExtend 作者包和测试包已同步；旧生成包保存到 dist/release/legacy-before-MapExtend。
- 未安装到游戏，未修改 Workshop 文件，未提交或发布。Unity 场景显示、点击/音效、切换、F9 与存读档仍须实机确认。

## 之前候选：外部小游戏同 Mod 兄弟目录 DLL 修复（2026-10-08）

- 针对 3802190178 的 9101～9113 注册失败修复：外部小游戏 dll 以 JSON 所在目录解析，允许 ../../plugins/CampusMinigames.dll 这样的父级相对引用；路径规范化后须留在所属 Mod 内。所属根从扫描来源 ContentRoot 取得，不由 EC2BUnofficialPatch 固定层数推断。不接受绝对路径或访问其他 Mod。
- 仅改变外部小游戏 DLL 的路径解析和非法路径提示，公共接口及其他功能目录的资源限制保留。三种目录布局、模板生成位置和 Workshop 原文件保持原状；作者无需修改当前小游戏包。
- net472 Release 构建 0 错误、1 个既有 CS0414。295 项离线检查通过，包含真实原配置 9101～9113 全部注册与 ICustomMinigame/无参构造函数验证、三种布局下访问 Mod/plugins、绝对/越界路径拒绝及此前目录读取回归检查。测试未执行小游戏 Open()，界面、游玩和结算仍需实机验证。
- UP 1.0.23 DLL：780288 字节，SHA-256 `f4be0b31ea4bf01f04d1ccddcef378213cf80ddb6a3d2aac8bba5acabd26001b`。dist/merged 与 dist/release DLL、三份 update.json 及配套测试 ZIP 已同步；未改写游戏安装或 Workshop 目录，未提交或发布。


## 之前候选：Mod 功能目录统一读取（2026-10-08）

- 三种布局统一支持：Mod/EC2BUnofficialPatch/功能目录（最推荐）、Mod/功能目录（其次）、Mod/任意容器/功能目录（可多层）。作者只需摘取使用的功能文件夹，模板生成位置与流程保留。
- BetterAudio、comic、custombadminton、ExternalLive2D、LoveDraw、Minigame、ScreenLyrcis、ScreenPaper、ScreenVideo、WorldMaps 共用 FeatureContentDiscovery；RoleAvailability 单文件支持同样的容器布局。原版 Cfgs/Audios/Textures 继续按原版注册/逻辑 URL 处理，MapExtend/Cfg 不自动加载。
- 大地图及标点的来源 Mod ID 改由 ContentRoot.Id 取得，不依赖目录父级。增强 LoveDrawCfg/MinigameActionCfg 在三种布局均能构建同 Mod 运行时覆盖视图；多份同种增强 CFG 按 2→1→3 选第一份并警告，其他模块保留原有冲突策略。
- JSON 资源仍绑定实际配置/功能目录；BetterAudio 的 audioPath/timelinePath 和外部小游戏 dll 改为只接受目录内相对路径。内置校验音频的缓存绝对路径仍走独立通道。F9 JSON 预检补齐所有功能目录布局，原有模块回调/音频重启要求保留。
- 验证：net472 Release 编译 0 错误、1 个既有 CS0414；tools/verify_content_layouts.py 对实际构建 DLL 的 159 项检查通过，覆盖三次目录移动、模块读取/注册、音频/Live2D/大地图来源、相对资源路径、增强 CFG 替换/注入且原文件不变、跨 Mod 隔离、大小写/优先顺序及 F9 变更/错误语法预检。
- DLL：UP 1.0.23，779264 字节，SHA-256 `517963b0f6587e8d13339c7a09347b171c89a4ecddb358937977ff22a486efed`。dist/release DLL、根目录/release-manifests/dist/release 三份 update.json、作者工具包与 WorldMapsTest 包已同步。本次为本地候选，未安装到游戏、未同步旧 VS 工程、未提交或发布；尚未实机验收图片解码、音视频播放、Live2D 渲染和小游戏 DLL 执行。


## 之前候选：大地图返回溶解与重复日志修复（2026-10-08）

- 版本保持 **1.0.23**，仅处理用户反馈的重复底图成功日志、非原生区域父地点右键返回所属大地图的过渡，以及这条路径的无用代码。
- 重复信息来自 WorldMapSession.Prepare 每次地图重新打开时打印，已删除该成功日志与无用 Core 引用；底图实际读取失败仍保留错误处理。
- 原版 MapSceneView.OnBeforeCloseAnime 会先打开 MapView，再执行自身关闭淡出；BaseView.Show 在 OnOpen 后才启动目标淡入。旧方案在目标 OnOpen 内挂载副本并立即淡出，时间与原版淡入重叠，且副本随目标视图层级显示，无法保证背景溶解清晰可见。
- 父地点改直接捕获 icon_bg.image，非原生大地图直接捕获 WorldMapSession.BackgroundImage；副本挂到所属 UI 层独立 Canvas，保留退出画面直到目标 MapView.OnOpenAnimeComp，再进行0.2秒溶解。保留原版退出事件与车站右键返回所属区域规则。
- 待挂载/已显示副本分别管理，直接切图 OnClose 不误删新捕获对象；完成、关闭、重置或失败时终止动画并释放独占纹理/精灵。移除旧静态纹理/精灵持有字段，资源统一由副本组件释放。
- 原版证据：D:/codex/assembly_ref/Assembly-CSharp/View/Main/MapSceneView.cs:928、View/Main/MapView.cs:93、Sdk/BaseView.cs:183及736；已内部对照2026-09-25受管快照。构建引用游戏DLL SHA-256仍为 `0d004b3a8c7fdd2ac41e4db099293db112d6b24dc2a14086e9c674283f1378ea`。
- C#构建完成：0错误、1个既有音频CS0414警告。DLL **768512字节**，SHA-256 `9340493e5c2069f0131b7afbe9d6c62fe87d7f2fd8ba1a2121c9c16b831bb46c`；dist/release DLL、三份update.json、作者包及WorldMaps测试包已同步。CFG无需改动。
- 本轮未新增/运行自动测试，未安装游戏、同步VS库、提交推送或发布。请继续实机复测“新区域父地点右键 → 所属大地图”的可见溶解，以及连续往返、交通行动切图；编译结果不代表实机画面已验收。

## 上一轮候选：实机日志四项修复（2026-10-08）

- 依据用户提供的 zzz1.txt：第37行4016误报说明文件，第116～133行启动 SubmitHide/GetTopView 空引用，第192～207行地图过渡不可读纹理/0×0 Sprite 异常，以及第189～191行不需要的旅行标识信息。版本保持1.0.23。
- ComicResourceIndex 只登记 comic 子文件夹图片；根目录文件及其他非图片文件静默忽略，保留实际漫画图片的命名/数量/冲突校验。默认模板说明和数据格式说明同步。
- 自动控制台等待 DebugView.Start 和 UIMgr.Init 完成；模块启用时只在游戏 UI 未初始化阶段跳过原版 SubmitHide，初始化后恢复原版处理。原报错是输入回调访问未创建的 viewDict，并非控制台功能永久失效。
- WorldMapTransitions 以 GPU Blit/RenderTexture 读回复制背景，避免 Instantiate 不可读纹理得到0×0结果。保留独立副本、0.2秒溶解及销毁释放；捕获失败仅放弃本次过渡，原版关闭/切换继续。
- 删除旅行标识成功识别日志；原版标识仍正常隐藏。
- 原版相关机制先查 assembly_ref 拆分类 DebugView/DebugMgr/Control/UIMgr，再对照2026-09-25受管快照。日志中的纸条图片缺失、Mod CFG反序列化/KZoneColorCfg.img格式错误和Steam成就/状态信息属于独立问题，未混入这四项修复。
- C#构建完成：0错误、1个既有音频CS0414警告。DLL **768512字节**，SHA-256 `3af3fe74c9431ad8e0981649cfbc2b92d0799f6ad21c7c32e57eefa99ac16efe`；dist/release DLL、根目录/release-manifests/dist/release update.json及WorldMaps测试包已同步。尚未安装到游戏或发布；新版启动输入和地图溶解需继续实机复测。

## 上一轮候选：大地图解锁、合并与工具优化（2026-10-08）

- 版本保持 **1.0.23**，范围仅为用户确认的八项地图扩展与优化优化。
- DLL 内置并保留 Func **160**/type=0“去其他地方”原有业务字段，固定鹅城交通父地点 **12**；开局不主动开启 Func、不强制开放客运站。功能开启后补齐 type=1 特殊行动，读取行动及出发时核验功能/所属地图资格。
- 新增效果 **[101,5,地图ID]** / **[101,-5,地图ID]** 及显示说明。原版101分派、EffectorUI、FuncData、MapView、BaseView按拆分类源码核对并内部比较2026-09-25快照。地图ID使用数字0～16777215；鹅城0固定返回且不允许移除。
- WorldMapState 在原版 union 已支持的 FuncModel 容器中，以 WorldMaps.v2.worlds/maps/actions 独立键保存当前地图、开放状态、初始化地点和行动分配。defaultUnlocked 默认false，只初始化一次；提前解锁地图后开启Func会直接出现。移除不删进度/素材，当前区域移除后等事件/遮罩结束回鹅城。
- autoUnlockMaps/非鹅城车站及其内部floors仅首次初始化，不反复强制解锁。鹅城12排除自动初始化，既有存档里的原版地点解锁不会被本轮自动撤销。
- 多Mod在鹅城追加父地点同时合并；非鹅城一份主底图定义，其他Mod通过 extension=true 贡献地点，缺失主定义时暂不应用。跨图父地点ID或重复完整定义报错；原版地点/Bg ID仍须作者协调。自动行动ID统一检查占用、备用分配并存档，避免作者基数冲突，存档碰撞重分配时修正对应行动实例。
- 原版父地点优先级改为2，新建默认3、最低1；原版/新增显示和输入同排序，同值新增在原版之后、新增之间较大mapId先响应。移除 enabled/visibility.requireUnlocked，所有标点遵循原版地点解锁/条件/NPC显示。
- WorldMapTransitions 保留退出背景的独立副本，在新图就绪后0.2秒淡出，与原版视图动画配合；直接切图补调用PlayViewAnime(true)。背景副本不接收输入，动画完成/销毁释放纹理，保持原版地点退出事件及车站右键往返规则。
- MapExtend工具只生成原版MapCfg/BgCfg两表；删FuncCfg生成/导入及功能ID、行动基数、鹅城交通父地点和解锁绕过开关。新地图可选默认开放；父/子地点分别选首次开放；加入已有地图贡献流程。后备描边改色即时显示，与透明命中参数分开；Ctrl滚轮缩放、空格/中键平移、适应窗口及100%仅改变编辑视图，坐标和尺寸不变且视图不导出。
- 模板readme、导出包说明、外置WorldMaps示范、GAME_DATA_FORMATS/ARCHITECTURE/CHANGELOG同步。Cfg仍仅为原版注册材料，不由插件读取；外置示范990001默认关闭，删除游离FuncCfg；测试需原版注册两表后使用[101,4,12]、[101,1,160]、[101,5,990001]。
- C#构建通过（0错误，1个既有音频CS0414警告）；工具JavaScript语法编译通过。DLL **767488 字节**，SHA-256 `284bd72d7477e2ee20295836a8e0358cd77b2e2ce0c86f5d2e522567f4056b61`。根目录/release-manifests/dist/release的update.json与DLL一致，MapExtend作者包/WorldMaps完整测试包已更新。
- 输出仅在当前D:/codex开发副本；未安装到游戏、未同步VS库、未提交推送或发布。新版解锁/多Mod/存读档/溶解/工具交互仍需用户实机验收；本轮没有新增或运行自动测试。

## 上一轮候选：MapExtend 作者模板与完整制作流程（2026-10-08）

- 版本仍为 **1.0.23**。大地图新增父地点与大地图切换统一显示为 `[机制] 地图扩展与优化`；内部模块 key / C# 目录不改。
- plugins 下模板改为 **EC2BUnofficialPatch/MapExtend/**，DLL 只写 readme.txt 和 map-extend.html；不预置示范 JSON。旧 MapLocations 目录完整移入 MapExtend/旧版资料，避免作者文件丢失。MapLayout 及区域辅助文件也改在 MapExtend 生成。
- 单文件离线工具保留圈选/拖动图片/原版标签流程，新增显眼的“大地图与切换 → 父地点注册 → 标点与悬停 → 导出安装”导航。支持新区域 ID/名称/PNG/coordinateSize、父地点与内部地点原版行、场景背景行、车站和自动解锁、原版三表导入/精确编辑。父地点原版行可在第二步独立生成，无需先圈选；第三步再保存最终标点。新区域可复用鹅城 MapLayout 的标签样式，从底图原图直接制作。
- 最终扩展 JSON 由工具生成；可导入整个 MapExtend 文件夹（两表、Cfg、PNG），导出同结构 MapExtend.zip，包含 readme、工具、两表、原版 Cfg 三表及所引用 PNG。工具保存仅在内存，必须导出后保存到磁盘；未导出关闭有提示。车站标点/标签、跨图归属和图片缺失会阻止不完整导出。
- **MapExtend/Cfg 仅为工具生成的原版注册材料，不由插件读取，本地 plugins 调试目录也不读取。** 已删除 WorldMapsRuntime 的本地 Merge 三表逻辑；作者必须将原版表内容按原版 Mod 流程注册到启用 Mod 的 Cfgs/zh-cn 并注册场景资源。插件继续自动生成交通 ActionCfg，大地图底图继续直接读 PNG。工具与所有当前作者说明已明确双阶段安装。
- 发布准备工具同步输出 dist/release/MapExtend/ 和 MapExtend-AuthorKit-1.0.23.zip，完整测试包附作者工具；示范 WorldMaps 的 Cfg 必须手动完成原版注册，不能再只解压即可注册地点。
- 已完成 C# 构建与工具 JavaScript 语法编译：0错误，1个既有音频 CS0414 警告。当前 DLL **753152 字节**，SHA-256 `036a94f661b1486652d24978ce3a09a89c5c4e18b186d6742e8e60f174d1a330`。根目录/release-manifests/dist/release 的 update.json 已同步，独立作者包和完整测试包已更新。尚未安装到游戏、未发布/推送，未进行浏览器交互或实机测试。

## 上一轮候选：目的地 UI 与右键车站往返（2026-10-07）

- 用户反馈上一轮测试“流程基本良好”；已确认旅游式目的地列表及区域切换基本可用，同时发现原版“旅行”标识残留、“出发/前往”重叠和新区域右键回家问题。本轮只处理三项反馈，版本仍 **1.0.23**，配置格式和外置内容路径保持不变。
- WorldTravelView 删除附加的“前往” Text 与原版公交图标位置改写，保留原生“出发”按钮。保留“去其他地方”标题，按左上角标题图片位置/尺寸/名称识别并隐藏原版“旅行”装饰；只影响本窗口实例。源码归档没有原 prefab 本体，日志记录识别节点/精灵，实际隐藏结果待本轮实机确认。
- 新增 WorldMapNavigation：接管 Game.Back 及 Esc 的同类返回路径。非鹅城的 MapView 右键/Esc 进入当前区域 travelMaps 第一项车站，调用原版 MapView.OnClickMap，保留原版过渡与进入事件；缺少可见车站时日志提示并保留地图，不回家。
- 用户进一步修正规则：当前区域车站及其 MapCfg.floors 子地点内，再次右键/Esc 返回所属区域的大地图，不切回鹅城。已删除车站场景的返回键吞掉分支，沿用原版 MapSceneView 退出事件和重开 MapView；ActiveWorldId 保持不变。其他内部地点也按原版退出到当前区域大地图；其他弹窗/剧情取消仍走原版。返回鹅城必须通过交通特殊行动选择鹅城并“出发”。主动 SwitchTo 的正常场景关闭不受返回键拦截。
- 无新增 CFG 字段。多个车站时作者以 travelMaps 数组顺序选择默认返回车站。外置测试包默认仍为江畔车站31；作者规范、架构、数据格式及 changelog 已同步。
- 已重新编译：0错误，1个既有音频 CS0414 警告。当前 DLL **716800 字节**，SHA-256 `514b446d1f2b1b9920116aa9d58a54f1f25eb67b3845ae5ef4bd6c9bb1cbc63d`。根目录/release-manifests/dist/release 的 update.json 已同步；完整包 WorldMapsTest-1.0.23.zip 含新版 DLL 与说明，CFG/图片继续外置。
- 本轮未安装到游戏、未发布/推送。标题隐藏、原版出发按钮布局、大地图右键进站、车站/子地点右键返回所属区域与交通行动返回鹅城仍待用户实机确认。已有外置配置无需重置，仅替换 DLL 即可使用本轮修改。

## 上一轮候选：大地图外置测试迭代（2026-10-07）

- 版本保持 **1.0.23**。用户已实测上一版出现单目的地确认窗口、旧鹅城底图仍显示及左下角按钮不协调；本轮按反馈调整，当前代码/配置以下述约定为准。
- 目的地选择改用原版旅游 TripUI/Cell_TripItemUI 的滚动卡片、字体和关闭按钮；点击任意非当前区域卡片，再点“前往”。可有多张地图，鹅城固定为 worldId=0，也作为返回目的地。取消清除 recordFuncId。
- 删除左下角返回按钮与其创建代码。每张非原生大地图必须有至少一个车站式父地点，在 travelMaps 声明；共享 FuncCfg 功能解锁后自动注册 type=1“去其他地方”、自动解锁车站。新区域车站须有可见标点、有效背景且 MapCfg.cond=[]；创建失败则记录日志并恢复鹅城。
- 大地图背景改读 Worldmapscfg.url 直接 PNG 路径，不再使用 BgCfg 概览行。WorldMapSession 隐藏原生 MapView 的概览 Image/RawImage，关闭恢复 enabled；新图与标点共用局部坐标变换，MapLayout 优先导出当前底图。日志含实际 worldId、图片路径、尺寸及原生底图层数量。实际 prefab 图层遮挡及不同分辨率效果待实机确认。
- 作者扩展和测试数据、原版 CFG 补充表、示范 PNG 全部从 DLL 剥离；源内容位于 **ModAuthorTemplate/MapExtend/**，删除 BuiltInContent/WorldMaps 内嵌与嵌入资源项。仅作者说明/离线圈选工具仍随 DLL 提供，不自动生成或恢复示范数据。旧 WorldMapsPrototype/WorldMaps.json/MapLocations.json 不读取，无兼容路径。
- **Worldmapscfg.json** schema=1：functionId/actionBaseId/autoUnlock、worlds[].id/name/url/coordinateSize/mapIds/autoUnlockMaps/travelMaps。id=0 为鹅城；非原生区域必有父地点和车站。底图不用 BgCfg；父地点仍原版 MapCfg。
- **MapLocationscfg.json** schema=2：locations[].worldId/mapId 与既有 priority/hitArea/visual/label/visibility；圈选工具增加大地图 ID、按当前辅助文件筛选编辑/绘制、导出同名文件，保留其他地图登记。内部子楼层仍使用 MapCfg.floors/type/bg，不把子楼层数据移入展示表。
- 本地测试读取 BepInEx/plugins/EC2BUnofficialPatch/MapExtend/，其中 Cfg/MapCfg.json、BgCfg.json、FuncCfg.json 仅补充不存在的行；Workshop 读取 Mod/EC2BUnofficialPatch/MapExtend/ 两份扩展 JSON 与图片，原版三表继续放 Mod/Cfgs/zh-cn。只启用的 Workshop 包参与；同 mapId 的展示以已启用 Mod 优先于本地测试包。
- 外置测试区域990001“江畔示范区”，父地点31/32/33、子地点31001/32001/33001；功能990100。按 actionBaseId=990000 自动生成行动990012（原客运站12）、990013（大桥13）、990031（江畔车站31）。不再提供手工 ActionCfg 表；新大地图 BgCfg990200已删除，内部背景990201～990206保留。autoUnlock=true方便测试；正式剧情可false，用[101,1,990100]开启功能。
- F9 同时重读两份扩展表，重开地图应用；原版 Cfg 三表、移除交通行动或改功能ID/行动基数需要重启。地区存档仍复用独立键 EC2BUnofficialPatch.WorldMaps.v1 的 FuncModel 容器；未知区域回鹅城。
- 已重新编译：0错误，1个既有音频 CS0414 警告。**当前 DLL 为 713216 字节，SHA-256 `a9d794e6087ba9b836d8ba303afad2348e8f78de93c3a39372e99dcad3322235`**。根目录/release-manifests/dist/release 的 update.json 已同步。输出 dist/release/MapExtend/ 与 WorldMapsTest-1.0.23.zip，ZIP包含按游戏目录排列的 DLL、外置CFG/图片/工具和根目录update.json。
- 此轮未安装到游戏、未发布或同步远程；尚未进行新版实机验收。需用户继续确认：多目的地卡片选择、示范PNG实际显示、车站行动返回鹅城、父子地点进出及存读档。完整作者规范见 ModAuthorTemplate/MapExtend/readme.txt；专题架构与数据格式已同步。

## 历史迭代记录（以下旧内嵌/按钮/表结构已由当前候选取代）

2026-10-07 特殊行动点击无窗口修复：用户日志为 Instantiate Prefabs/UI/Common/ConfirmView@Common failed: obj is null。功能/行动已登记，失败发生在选择窗口 prefab 加载。核对拆分 GenUI/Common/ConfirmUI.cs:15 与 GenUI/Hint/CommonConfirmUI.cs:15、View/Hint/CommonComfirmView.cs:12，并内部比对 2026-09-25 托管快照；本机游戏 Addressables catalog.json 仅包含 Hint/CommonConfirmView@Hint 的资源条目，不含旧 Common/ConfirmView@Common。WorldTravelView 改为继承 CommonConfirmUI，绑定 img_bg/txtex_desc（TMP）、txt_title、group_btn 和按钮，使用原版 Tips 层及加载提示；多目的地导航按钮放在操作按钮上方。101,1,990100 仍只负责功能解锁，无需改 CFG。版本保持 1.0.23，重新编译通过；修复后弹窗和后续地图切换待实机确认。

2026-10-07 实机启动报错修复：用户日志定位到 WorldMapsContent.Entry，内嵌大桥的 visual.normal=null 是合法配置，但 Newtonsoft 将 JSON null 表示为非空引用 JValue；旧代码的空条件访问仍尝试读取它的 image 子字段，抛出 Cannot access child value on Newtonsoft.Json.Linq.JValue，导致整个 mechanics.map-locations 初始化中断。普通/悬停图层改为先转为 JObject 再读取 image，正确处理省略/null/对象三种合法情况；原有解析器仍负责拒绝非法字段类型。未修改 CFG 或要求用户删除 null，版本仍为 1.0.23。已重新编译与生成清单/游离包，游戏内重新启动结果待确认。

2026-10-07 当前最新本地候选（大地图切换第一版，含 JValue 空图层与确认窗口资源修复）：dist/release/EC2BUnofficialPatch.dll 为 4406272 字节，SHA-256 `f63102aaf9d3863bef1fbc1f17211457c397344d748f10056f17a41ff7fb9b1a`；根目录、release-manifests/、dist/release/ 的 update.json 已由发布准备工具同步，版本 1.0.23、游戏版本 1.94。下方同日 691712 字节候选是本轮之前的标签修复构建记录。

2026-10-07 大地图切换第一版（版本仍为 1.0.23）：预置 FuncCfg 990100“去其他地方”，以及客运站 map=12 的 ActionCfg 990101、大桥 map=13 的 990102，均为 type=1、funcId=990100、unlock=[[101,1,990100]]。功能 type=0，解锁后由 UP 登记两个行动；默认 autoUnlock=true 方便第一轮体验，可用完整游离包改为 false，再由原版剧情效果 [101,1,990100] 开启。原生 CFG/Mod 已存在的同 ID 行保留；已启用 Mod 的 mapId=13 展示登记优先于预置大桥。

2026-10-07 示范区域与资源：预置区域 990001“江畔示范区”，底图 BgCfg 990200；父地点 31/32/33 与子地点 31001/32001/33001 由 MapCfg.bg/floors/type 注册，内部场景暂复用原版车站、商场、小卖部、山顶和海滩背景。新底图为本轮生成的示范素材，大桥悬停 PNG 使用用户提供的 img_bridge.png；所有新增 CFG、底图、悬停图和内容说明均嵌入 DLL。游离包为 dist/release/WorldMapsPrototype/ 和 WorldMapsPrototype-1.0.23.zip，含原有圈选工具；放到 BepInEx/plugins/EC2BUnofficialPatch/WorldMapsPrototype 后整包替代内嵌内容，修改后重启生效。结构细节见 BuiltInContent/MapExtend/readme.txt。

2026-10-07 生命周期：沿用原版确认窗口 prefab 制作目的地选择，取消/选择清除 recordFuncId；切换沿用 MapSceneView.CloseView 的退出事件与重新打开大地图。新区域左下角“返回原生大地图”克隆原版地点胶囊，在 MapView 内释放并重建格子，避免整窗 CloseView 的回家分支。原生标点暂存到 group_item 以外，关闭恢复，释放区域图片/控件。区域 ID 使用 SaveMgrEx 独立键和已受 MessagePack Union 支持的 FuncModel 容器保存；新开局/读档重置待切换状态。新区域另导出 MapLayout-990001.json，包含 worldId。

2026-10-07 第一版编译记录：本机游戏 1.94、net472 引用构建通过，0 错误、1 个既有音频模块 CS0414 警告。本轮仅生成本地 DLL、清单和游离包，未安装或发布，未进行游戏内验收。待实机确认：两个入口、取消与遮罩关闭、区域切换、父子地点进出、返回、存读档恢复、不同分辨率的底图/标签/悬停对齐。无正式旅行费用、时间推进、新剧情或专属内部美术。

2026-10-07 标签错图/黑字修正：只读检查本机已生成的 MapLayout.json，外框 PNG 实为橙色图标碎片、底色层也非胶囊；字体 PNG 的 RGB 全为 0，字形在 Alpha 通道。撤销直接按 sprite.textureRect 截取标签图片的方式，改为取原版显示组件的有效 Sprite、实际网格/网格效果和材质，在独立目标中绘制图层；黑底/白底两次绘制恢复直通透明度，避免 UI 混合导致黑边或透明度平方。浏览器字形按 Alpha 蒙版着色，颜色不再乘黑色 RGB。新增 labelStyle.rendering=unity-image-alpha-v1，工具拒绝未包含修复标记的旧辅助文件；需更新 DLL 后重新打开大地图并导入新 MapLayout.json。地点登记格式不变。此条为资产诊断与实现记录，修复后的浏览器/游戏视觉效果尚待实机确认。

2026-10-07 最新本地发布候选已生成：版本保持 1.0.23，游戏清单版本 1.94。`dist/release/EC2BUnofficialPatch.dll` 为 691712 字节，SHA-256 `57d1dd7a9ff7786f79ec1731505863c9b0c2831ba144b0336b670bd47dc3823b`。根目录、`release-manifests/` 与 `dist/release/` 的 `update.json` 同步；包含下方标签错图/黑字、共享布局、实机样式导出和解包背景映射修正。编译通过（0 错误、1 个既有音频模块 CS0414 警告）。仅生成本地候选，尚未安装或发布；本次未进行浏览器/实机对照测试。

2026-10-07 工具/游戏标签尺寸与定位修正：确认旧预览硬编码 180×40、22 号字体，而运行时读取原版模板尺寸/字体；指定 size 时父框与子图层、边框的布局也不是同一计算。新增 MapLocationLabelLayout 共用原版基准、字形进位、父框/子层 Resize，冻结冲突的布局适配器，统一 label.position 为父框中心；size 调整外框，scale 同时调整文字与装饰。辅助文件改为 schema=2，包含实际原版图片图层、矩形宽高变化、九宫格边框与留白、字体字形图集/进位/基线/阴影。工具按实机导出值绘制，取消默认尺寸/字号猜测；未包含字形暂用浏览器字体，登记后重开地图补齐。地点登记 schema=2 不变。

2026-10-07 解包原图映射：工具新增截图/解包背景原图来源选择；截图使用屏幕尺寸，解包图使用实际 Image/RawImage 的显示矩形、preserveAspect/UV 和屏幕坐标轴映射。无法识别背景时要求完整截图，不因分辨率相同而套用截图映射。读取到的旧本地辅助文件为 2560×1440、中心 [1280,720]、单位基向量，并未支持标签样式数据；该文件不能证明解包图/截图的背景映射一致。用户需更新 DLL、重新打开地图生成新辅助文件并导入新版工具，再重新对照截图。尚未进行游戏内或浏览器对照实测。

2026-10-07 圈选工具交互修正：针对菜单纵向过长及悬停 PNG 导入后不显示的问题，改为左侧紧凑步骤面板、右侧固定地图，顶部常驻地点选择/保存/导出，高级参数与说明折叠。圈选/移点/删点分为独立操作。编辑时直接显示悬停 PNG，导入后自动进入图层调整；提供图片矩形/右下缩放控制点、等比缩放、5% 微调和移到圈选中心。普通/悬停图分别编辑；只有预览模式按鼠标命中开关悬停图，编辑显示开关和控制框不写入配置。修正少量顶点产生零宽初始图片的情况。schema=2 和游戏运行行为不变，作者 readme、数据格式与内嵌工具同步。

2026-10-07 大地图模块按实机截图修订：展示登记改为 schema=2，hitArea（鼠标区域）、visual.normal/hover（普通/悬停 PNG，独立位置尺寸）、label（完整原版样式/颜色/位置/缩放/可选尺寸）、visibility（解锁要求）分离。按用户确认未公开发布，不兼容 schema=1；移除 parentMapId 和父入口联动。此处制作的是独立大地图入口，内部节点仍由 MapCfg.floors 与既有地图子地点模块负责。

2026-10-07 标签与悬停：克隆原版完整 btn_name 保留图片层级/边框/箭头/阴影/字体和 NPC 容器，清除源入口交互引用，只设置底色层与文字色。鼠标区域独立于图片留边：进入显示悬停 PNG，离开隐藏；有普通图时切换两层。无有效悬停 PNG 时画后备轮廓，缺失/损坏 PNG 记录路径并回退，不因悬停资源失败移除多边形入口。alpha 命中仅使用普通 PNG，普通图无效则该 alpha 入口跳过。原版入口 priority=1，新增默认 2，较高者响应，同值较大 mapId 响应；只加载已启用 Mod，地点/背景仍由 MapCfg/BgCfg 注册。

2026-10-07 圈选工具修订：导入对应完整截图/MapLayout.json；添加普通/悬停 PNG 独立导入、路径、拖动定位、等比尺寸、原版标签外形示意、颜色/透明度、缩放/可选尺寸、标签定位、顶点拖动/删除及鼠标悬停预览。已有 schema=2 登记可以选择回填，同 ID 保存保留其他未编辑字段；JSON 与各 PNG 分别导出。解锁缺省与解析器一致为 true，逐项说明用途。浏览器标签为外形示意，游戏端使用实际原版组件；既有本地 schema=1 示例需重新制作。模板 readme/JSON、数据格式、架构和版本说明同步。

2026-10-07 生命周期沿用：F9 完整替换展示登记，重开地图生效，当前会话保留旧资源，关闭时释放。源码机制已与 2026-09-25 托管快照核对。原版证据为 assembly_ref 的 GenUI/Main/Cell_MapItemUI.cs（32）、View/Main/MapView.cs（60、143）；实际标签资产样式、大桥 PNG 对齐和多地点悬停切换仍需游戏内观察。

2026-09-28 本轮改动的本地发布候选文件已生成：`dist/release/EC2BUnofficialPatch.dll` 为 586240 字节，SHA-256 `601086a74d41cb6f6700ba7da0a30adb9a93bd409bb1da9056f92f0bd37c1d91`；`dist/release/update.json`、`release-manifests/update.json` 与根目录 `update.json` 内容一致，版本 1.0.23、游戏版本 1.94，大小及哈希均匹配 DLL。构建为 0 错误、1 个既有未使用字段警告。仅制作本地候选，尚未发布或安装到游戏。

2026-09-28 本轮按限定范围实现三项改动，尚待游戏内复测。CG：`CGOptimization` 接管图鉴格子的缩略图请求，格子每次重绑递增代次并清除旧图；回调仅在格子仍显示同一 CG 时写入贴图，成功、空结果、路径错误和 20 秒超时都会结束当前转圈，翻页重试会发起新请求。此处理仅限图鉴缩略图，未修改全局 `UISprite` 或 `ResInfo` 行为；若原版 `ResInfo.GetSpriteAsync` 内部异常令共享请求永久停在 `isLoading`，需要后续实机日志再单独定位。配置键迁至 `[优化] CG播放与图鉴优化`，沿用旧键的值，说明同步加入分页缩略图修复。

2026-09-28 羽毛球：新增默认开启的 `[机制] 羽毛球模型贴图新增` 开关；对手 `custombadmintoncfg.json` 可用 `outfits` 数组逐套登记 `url/prefab`，同一 ID 跨 Mod 追加，重复 url 仅保留先加载的有效项，资源无效则逐套跳过。每次打开小游戏时，原版 `BadmintonModelCfg` 中存在该 ID 则将原版球服作为一项，连同全部有效外置球服等概率随机选择；新 Mod ID 只在有效外置项间选择。旧对手 `url` 数组仍只取首项作为一套；`id=0` 玩家保持既有配置和男女选择逻辑，不随机换装。实例加载失败沿用模板回退路径。作者模板 readme/JSON、数据格式和 README 已同步。

2026-09-28 作者模板：运行时生成的 `comic说明.txt` 改放 `comic/comic说明.txt`；启动时迁移旧根目录说明文件，模板说明文字也改为与新位置一致。构建使用游戏目录 `G:\SteamLibrary\steamapps\common\StudentAge` 的 `net472` 引用，0 错误、1 个既有未使用字段警告；未进行游戏内 CG 快速翻页及羽毛球概率/对象池复测。

2026-09-28 大地图新增地点（历史需求记录；2026-10-07 实现见上方）：目标是让普通 Mod 声明全新的大地图地点，并自定义地点标签文字、颜色、位置与尺寸、地点图标、鼠标悬停描边区域及点击/跳转行为。现有“多 Mod 地图子地点兼容”和“地图子地点扩展”只处理已有地图层级的合并/嵌套，不等同于注册新的大地图地点。用户提供的 `D:\VS2026 project\studentages\StudentAgeMapModGenerator\StudentAgeMapModGenerator.zip`（标点与自动描边工具，并生成独立 BepInEx DLL）作为算法参考；后续评估改为 UP 共用运行时读取 Mod 数据/资源的注册流程，避免作者按地点单独生成 DLL。数据 schema、轮廓/命中区域、点击事件路由、跨 Mod 冲突与兼容策略尚未确定；本条保留当时需求；当前实现与验证范围见上方 2026-10-07 记录。

2026-09-28 羽毛球对手多套球服（原始需求记录；实现见上方，待实机验证）：仅影响对手，不修改玩家模型。原版 `BadmintonModelCfg` 角色始终保留为候选；`custombadminton` 中同一角色 ID 的有效 Mod 服装作为追加项。每次打开羽毛球小游戏时，在原版服装及所有追加服装中等概率随机选择一套。按等概率规则，无追加的原版角色始终为 100%；原版加 20 套 Mod 服装共 21 套，各为 1/21（约 4.76%）；只有一套的新 Mod 角色为 100%，有 10 套时各为 10%。此前外置角色模块是每个 ID 一条登记，重复 ID 会保留先加载项；普通 NPC 只取该条目的 `url[0]`，因此现有 `url` 数组不代表同角色随机换装。上述跨 Mod 追加、逐套 `url/prefab`、重复资源处理、随机选择及失败回退已按本轮记录实现；尚未实机验证。制作流程见 [羽毛球模型贴图制作指南](BADMINTON_MODEL_TEXTURE_GUIDE.md)。

2026-09-28 CG 图鉴分页缩略图偶发转圈（源码诊断；实现见上方，待实机验证）：实测翻页后同一缩略图有时正常、有时转圈，更符合“格子复用 + 异步回调竞争”，不能只归因于图片文件随机缺失。原版 `CGLibraryView.SetPage` 每页最多 9 项并复用 `UIItemGroup` 中已显示的 `UICell`；`UICell.SetData` 会重跑渲染，令同一个 `UISprite` 在前一页请求尚未完成时改绑到新 CG。`UISprite.SetTextureUrl` 的回调只绑定实例方法，不捕获/校验本次 URL 或绑定代次；旧请求完成可把旧 Sprite 写入当前格，空结果还会清掉新 URL。换 URL 时图片对象会先重新显示，而旧 Sprite 未清空。`ResInfo` 按路径缓存并合并进行中的请求，缓存命中可立即回调、冷加载异步完成，时序变化能解释间歇性。另有独立加载状态问题：原版 `OnRenderCG` 每次都启用 `root_loading`，C# 渲染路径没有对应完成关闭；因归档不含 prefab/Animator，若实测转圈有时自行消失，还需确认 prefab 是否另行控制。修复前 `CGOptimizationModule.LibraryOnRenderCgPostfix` 只改图号，不处理贴图加载。修复方向：为每次贴图请求及格子绑定加 URL/代次校验，过期回调不得写图或改加载状态；增加同时覆盖成功、失败/取消的完成回调，以结束转圈并显示占位/重试状态；换 URL 时清除旧图；`ResInfo.GetSpriteAsync` 加异常收尾，确保 `isLoading` 复位并通知等待者；核对 `cg_min/`、`cg2_min/` 对应资源 key。共享资源请求不宜因单格翻页而整体取消。证据为 `D:\codex\StudentAge-CodeOnly\assemblies\Assembly-CSharp\Assembly-CSharp.cs`（2026-09-25 快照）：`CGLibraryView.SetPage/OnRenderCG` 61638、61676，`UIItemGroup.SetData/UICell.SetData` 107456、108906，`UISprite.SetTextureUrl/OnLoadSpriteEnd` 108441、108523，`ResInfo.GetSpriteAsync` 105289；缩略图请求保护已按本轮记录实现；快速连续翻页复测尚未进行。

2026-09-28 CG 图鉴排序修复：在原有 `CGOptimizationModule` 内调整 Mod Group 3 的排序。`idx > 0` 的 CG 先按 idx 升序排列（相同 idx 按 ID 升序），其余 CG 再按 ID 升序排列；显示图号仍从 001 连续编号。以 2026-09-27 SHA-256 为 `3401d10d07ef789ad3965b607e23ff29882d3a0437318d4f0dc6b7db43d0ebc6` 的 release DLL 为基线；新候选 DLL 版本仍为 1.0.23，大小 580608 字节，SHA-256 为 `285a9fafec781f1b0846fc8f4272f9238cc0859f0419a6975be674b0ab73257e`。net472 编译通过，游戏内图鉴显示待复测。

2026-09-27 羽毛球外置角色实机修复：1.0.23 初版在同一 `Armature` GameObject 上释放并重建 DragonBones，旧 `UnitySlot` 的显示对象由 Unity 延迟销毁，新骨骼在当帧复用了这些对象，随后产生 `UnitySlot._UpdateTransform` 空引用。敌方骨骼的异常还会中断共享 `WorldClock`，使未替换的玩家小人也停止运动。现改为在全新 RectTransform 宿主中完整构建、校验全部插槽及七个玩法动画，成功后再切换 `anim_enermy/Armature` 或 `anim_player/Armature`，最后释放旧宿主；对象池恢复模板也使用相同替换流程。已通过 net472 编译，等待游戏内复测。

2026-09-27 羽毛球角色外置贴图：新增 `custombadminton/custombadmintoncfg.json` 注册表。每条登记沿用 `BadmintonModelCfg` 的 `id/url`，并以 `prefab` 指定复用的原版模板；运行时改载模板 prefab，再从 url 同名子目录读取 `_ske.json`、`_tex.json` 与 tex 中 `imagePath` 指定的 PNG，克隆模板材质并只重建当前实例的 DragonBones Armature。无需 Mod 作者提供 `.mat`。支持 NPC 和 `id=0` 玩家男女 url，F9 可重载登记和资源版本；失败时恢复模板角色。已通过 net472 编译，游戏内动画、对象池复用与多 Mod 冲突仍待实测。

2026-09-27 1.0.23 羽毛球实机修复候选构建完成：`dist/release/EC2BUnofficialPatch.dll` 大小 580608 字节，SHA-256 为 `3401d10d07ef789ad3965b607e23ff29882d3a0437318d4f0dc6b7db43d0ebc6`；根目录、`release-manifests/` 与 `dist/release/` 的 `update.json` 已同步为游戏 1.94、版本 1.0.23。编译为 0 错误、1 个既有未使用字段警告；尚未发布，修复后游戏内复测待完成。

2026-09-22 DialogueSave 0.1.5 兼容：本机订阅包 `3804899191` 与已安装 cfg（`DialogueStyle = ADV`）确认问题场景。其 [`AdvDialogueController`](https://github.com/360478712scy-gif/StudentAge-DialogueSave/blob/c40402ce621e47d3c27c8291e3265d996a0ae8d9/src/UI/AdvDialogueController.cs) 把 CG 实际文字移到排序 29000 的独立 `DialogueSave.ADV` Canvas，并把原 `mask_talk_cg` 移进透明 `ADV.NativeShield`；上一版将视频放在该 mask 的父节点，因而被透明遮罩隐藏。现在按文字是否位于 ADV Canvas 自动切换：ADV 时将视频层挂在遮罩外的原 CG 根下，排序在 CG 图片与 ADV 文字之间，不再改隐藏的原生文字 Canvas；切回原版/退出 CG 时恢复上一布局。播放期间 DebugCanvas 也会高于 DialogueSave 的模态画布。未修改 DialogueSave 或游戏文件，游戏内联动视觉效果仍待实测。

2026-09-22 CG 视频层级重做：移除上一版仅将 `roleOnTop=true` 视频插到 `mask_talk_cg` 前的方案；CG 子视图出现时建立临时视频 Canvas，按实际 CG 图片 Canvas 的排序层和值，将视频设在图片之上；原版文字模式下，再将 `mask_talk_cg` 排在视频之上。CG 退出或视频结束时销毁临时视频层并恢复文字 Canvas 原设置；Debug 控制台保持最上层。普通对话仍把动态背景置于角色后方。视频画面及底色保留与 CG 优化相同的 0.22 秒渐变节奏：首帧出现后淡入，正常播完、跳过、显式停止和替换时淡出，淡出完成才释放视频占用的原版音道；错误、准备失败和场景销毁立即清理。游戏内视觉效果仍待实测。

2026-09-22 CG 与编辑器预览：视频播放期间检测剧情 `CGView`（正常对话和编辑器预览的子视图），将完整覆盖视频画布调到当前 CG 画布上方，并让原版 `DebugCanvas` 始终高于视频；视频结束时恢复调试画布原层级。编辑器 `ModPreviewTipsView` 打开预览时仅加载当前 Mod 根目录的 `ScreenVideo` 注册表；`PreviewTalkView` 在有字对话的 `DoText` 和空文本对话的 `RefreshTalk` 执行 1164，预览关闭时停止其视频。动态背景和长按左键跳过也接受预览对话视图。未把游戏目录作者模板加入资源扫描。

2026-09-22 模板行为修正：恢复启动时在游戏 `BepInEx/plugins/EC2BUnofficialPatch` 创建 UP 作者模板；启用 BA 时也恢复在 `BepInEx/plugins/BetterAudio` 创建 BA 作者模板。仅创建供作者复制的文件，运行时 `ContentRootCatalog` 仍只纳入 Workshop Mod，不自动读取这两个模板目录作为资源。模板创建不覆盖现有 JSON，原有模板写入失败日志保留。

2026-09-21 后续优化：视频播放时将原版独立 `DebugCanvas` 临时提升到视频画布之上，结束后还原；若调试画布晚于视频创建，播放期间会继续检测。此举让控制台画面与输入位于视频遮挡层之上，不改变原版控制台快捷键逻辑。BA 启动自检成功时始终报告音频条目和资源包数量，视频模块启动时始终报告注册视频数量；两项日志开关现在只控制运行期间的信息日志，启动失败及资源错误仍始终输出。cfg 注释已同步。

2026-09-22 最新发布候选构建：按 `AGENTS.md` 指定的 `net472` / `GameDir` 流程构建通过（1 个原有未使用字段警告、0 错误），随后由 `build.py` 生成内置更新助手的单 DLL 和本地候选清单。发布 DLL 位于 `dist/release/EC2BUnofficialPatch.dll`，版本 `1.0.22`，对应游戏 `1.94`，大小 558592 字节，SHA-256 为 `71474b74ea9befc945872160323a4b808212168a20aaf3c2469369d111f121ce`。根目录、`release-manifests/` 与 `dist/release/` 的 `update.json` 完全一致，清单大小及哈希与发布 DLL 相符。游戏内运行测试和 GitHub 发布尚未进行。

2026-09-21：基于提交 `7c9d16b` 的独立克隆修改，原项目未改动。视频 1164、5002 歌词音频与内置《更好的音频效果》共用原版音乐通道优先级；视频从启动到真正退出一直持有暂停，循环到下一轮不会释放。跳过、`1164,0`、替换、报错和离开场景均通过统一结束路径释放。BA 的独立音源不被视频暂停；若 BA 或 5002 仍在播放，原版音乐继续保持暂停。

视频配置取消 `block`，由 `roleOnTop` 决定动态背景或盖住界面；动态背景优先于 `skippable`。盖住界面且允许跳过时，长按左键约一秒，复用原版歌词跳过素材。旧 JSON 中的 `block` 会被忽略。

配置区合并为“屏幕特效与效果”，3003 迁入“优化”；《更好的音频效果》及视频模块在“机制”中分别有启用开关（默认开启）和信息日志开关（默认关闭），旧开关值迁移。作者模板照常创建，但内容扫描不把插件安装目录的模板作为 Mod 数据读取。Release 构建通过（net472，0 错误，1 个原有未使用字段警告）；运行时尚未在游戏内实测。

复核：旧 `[屏幕特效] 1164屏幕视频扩展` 仅作为配置迁移输入，新 cfg 不再生成该键，视频开关统一写在 `[机制]`。5002 继续先解析原版 AudioCfg，再解析 BA 音频 ID；BA 关闭时仍支持原版。5002 播放自身音频期间持有原版音乐暂停，结束时先归还原有 BA 会话，再释放原版通道；内置 BA 的解析与暂停接口改为直接调用，移除了同程序集内的反射桥接。视频替换改走完整释放路径，动态背景无法嵌入对话时跳过播放。

本版统一为 UP 单 DLL、单 BepInEx 入口，音频演出内置，不再发布 BA 或分离版。音频功能由 UP 持久运行时初始化、保活和清理，F9 热重载直接使用内置音频注册表。1.0.24 的音频弱引用/有界日志缓存、版本比较和测试输出隔离，以及 1.0.25 的原 schema=1 更新流程、layout 匹配、嵌入助手和退出后单文件替换，均以合并版实现保留。

保留 Mod 的旧 JSON、资源路径、1163 指令及 UP 小游戏接口。根工程为 EC2BUnofficialPatch.csproj。在线更新恢复原 update.json 入口及 EC2BUnofficialPatch.dll 发布附件名。旧 BA 用户升级前须移除独立 BA DLL。1.0.23～1.0.25 测试版改为本版需手动覆盖一次。

[安装与完整功能说明](../../README.md) · [结构与兼容](../../Docs/MERGE.md) · [在线更新](../../Docs/AUTO_UPDATE.md) · [验证范围](../../Docs/VALIDATION.md)

原 CHANGELOG/Validation 文件仍保留历史版本标注；1.0.24/1.0.25 的内容和验收边界已在 1.0.21 当前记录中合并说明。
