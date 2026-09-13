地图社交界面外置 Live2D（moc3 直读）

一、作用范围

本功能默认开启，可在 BepInEx 配置的 [机制] 地图社交界面外置Live2D 中关闭。
它只替换 MapRoleView 中原本显示静态立绘的 Mod 角色，不接入 Talk，也不替换原版 Live2D。
按 personId + clothId + schoolStage 匹配；未登记、冲突或加载失败时自动使用同一服装、同一学段的静态立绘。

二、准备和导出

1. 在 Cubism Editor 完成模型、参数、剪贴遮罩和物理设置。
2. 导出运行时文件：model3.json、moc3、全部 PNG 纹理；physics3.json 可选。
3. cmo3 是工程源文件，不是运行时资源。当前版本不读取 motion3.json、exp3.json 或 Animator Controller。
4. 不要打包 Live2D.Cubism.dll 或 Live2DCubismCore.dll，UP 使用游戏自带 Cubism SDK/Core。
5. 纹理只支持 PNG。model3、moc3、纹理和可选 physics3 必须放在同一模型目录树内。

三、目录结构

把本目录复制到 Mod 根目录的 EC2BUnofficialPatch/ExternalLive2D：

EC2BUnofficialPatch/ExternalLive2D/
  ExternalLive2D.json
  1001/
    cloth0/
      model.model3.json
      model.moc3
      texture_00.png
      model.physics3.json    （可选）

model 字段相对 ExternalLive2D.json；model3 内的 Moc、Textures 和 Physics 引用相对模型目录。
禁止绝对路径和 ..。多个 Mod 重复登记同一 personId + clothId + schoolStage 时，冲突项全部禁用。

四、登记示例

{
  "models": [
    {
      "personId": 1001,
      "clothId": 0,
      "schoolStage": "primary",
      "model": "1001/cloth0/primary/model.model3.json",
      "fitScale": 1.0,
      "xOffset": 0,
      "yOffset": 0,
      "staticAnchorX": 0.5,
      "staticAnchorY": 0.5,
      "modelAnchorX": 0.5,
      "modelAnchorY": 0.5,
      "flip": false
    },
    {
      "personId": 1001,
      "clothId": 0,
      "schoolStage": "middle",
      "model": "1001/cloth0/middle/model.model3.json",
      "fitScale": 1.0,
      "xOffset": 0,
      "yOffset": 0,
      "staticAnchorX": 0.5,
      "staticAnchorY": 0.5,
      "modelAnchorX": 0.5,
      "modelAnchorY": 0.5,
      "flip": false
    }
  ]
}

personId 是 PersonCfg 的人物 ID；clothId 是游戏本次请求的服装编号。
schoolStage 可填 primary（小学）、middle（中学）或 all（两个学段共用）。省略时等同 all，兼容旧配置。
游戏中 GradeState=0 使用 primary；GradeState>0 使用 middle，这与 PersonCfg 选择 url/urlParm 和 url2/urlParm2 的规则一致。
同一 personId + clothId 可以同时登记 primary 和 middle；查找时优先精确学段，再使用 all 作为回退。
flip=true 会水平翻转模型。

五、静态立绘自动换算

插件直接读取游戏中同一服装的静态立绘 RectTransform，所以 PersonCfg.urlParm/urlParm2 的缩放和 XY 偏移已经包含在结果中，不要把那三个数字再次抄入本 JSON。
插件会排除静态 PNG 的透明留白，并与模型当前可见 Drawable 的外接范围对应：

基础倍率 = 静态立绘可见像素的游戏内高度 / 模型可见 Drawable 高度
最终倍率 = 基础倍率 × fitScale
最终位置 = 静态立绘锚点 - 模型锚点 + (xOffset, yOffset)

此计算不要求静态图与模型拥有相同分辨率、画布或人物相对位置。
fitScale 大于 1 放大，小于 1 缩小。
xOffset 正数向右、负数向左；yOffset 正数向上、负数向下，单位为 UI 像素。

四个锚点都是 0 到 1：X=0/0.5/1 表示左/中/右，Y=0/0.5/1 表示下/中/上。
默认四项均为 0.5，即静态可见边界中心和模型可见边界中心对齐。
若两者姿势、长发或裙摆不同，外接框中心可能不是同一个人体位置；可让双方锚点改为同一语义位置，例如头顶、下巴或腰线。

用“发卡中心”寻找锚点（Photoshop + Cubism）：
1. 在 Photoshop 打开静态 PNG，确保没有裁掉原画布。Ctrl+单击人物图层缩略图取得非透明选区，在“信息”或“属性”面板记下选区左 L、上 T、右 R、下 B。
2. 用两条参考线交叉在发卡的视觉中心，读取交点像素坐标 Px、Py。Photoshop 的 Y 从上向下增加。
3. 计算 staticAnchorX=(Px-L)/(R-L)，staticAnchorY=(B-Py)/(B-T)。第二式已经把 Photoshop 的向下 Y 转为游戏的向上 Y。
4. 在 Cubism 把所有参数恢复默认值，关闭网格和背景，导出一张透明背景的默认姿势 PNG；不要改变模型画布、缩放或导出范围。
5. 在 Photoshop 对这张模型 PNG 重复步骤 1～3，得到 modelAnchorX/Y。这里选择的也必须是同一个发卡视觉中心。
6. 把四个结果保留 3～4 位小数写入 JSON。进入游戏后如果只差少量位置，每次以 0.005～0.02 调整 modelAnchor；最后才使用 xOffset/yOffset。

通用公式：X=(锚点X-可见左)/(可见右-可见左)，Y=(可见下-锚点Y)/(可见下-可见上)。
示例：可见范围 L=200、T=100、R=1200、B=2100，发卡中心 Px=800、Py=300，则 AnchorX=0.600，AnchorY=0.900。
Cubism 导出图的非透明边界与运行时 ArtMesh 几何边界可能有轻微差异，所以这是可靠的初始值，不应期待一次测量达到像素级重合。

推荐校准顺序：
1. 保留该服装的静态立绘，用 fitScale=1、四个锚点=0.5、偏移=0 首次测试。
2. 只看可见人物高度：模型太小就提高 fitScale，太大就降低。
3. 高度正确后，用 staticAnchorX/Y 与 modelAnchorX/Y 对齐头、脸或腰线。
4. 最后才用 xOffset/yOffset 消除少量剩余误差。
5. 测试睁眼、闭眼、鼠标四角、重新关闭并打开界面。

若静态 Sprite 因特殊打包方式不可读取像素，UP 会退回完整 Rect；锚点和偏移仍可校准。
旧 scale/x/y 仍作为 Cubism 绝对倍率和坐标兼容，但不建议使用；不能与 fitScale/xOffset/yOffset 或锚点字段混用。

六、动作和参数

不需要 motion3.json。模型存在以下标准 ID 时，UP 自动驱动：
- ParamAngleX/Y/Z：轻微待机与鼠标头部跟随
- ParamEyeBallX/Y：鼠标眼球跟随
- ParamEyeLOpen/ParamEyeROpen：周期眨眼
- ParamBodyAngleX/ParamBodyAngleZ、ParamBreath：身体待机与呼吸
- physics3.json：读取以上参数并驱动物理输出

缺少某个参数只会跳过对应效果。自定义 ID 不会自动映射。

七、遮罩要求

UP 使用游戏自带的 Cubism 遮罩资源与绘制链。
Mod 作者仍必须在 Cubism Editor 中正确设置剪贴：例如瞳孔 ArtMesh 应被正确的眼白/眼睑遮罩限制。
导出后请整套替换 model3、moc3 和纹理，避免新 moc3 配旧 model3/旧 PNG。
若编辑器正确但游戏错误，先删除旧 DLL，确认游戏加载的是新 DLL，再查看首次成功日志的 masks 和 maskTexture。

八、加载、日志和排错

PNG 通过 Unity 异步读取和解码；准备期间保持静态立绘，完成后才切换。
同一界面实例再次打开相同模型会复用模型和纹理，不重新解析 moc3；静态图透明边界也会缓存。
每个模型在一次游戏进程中只有第一次“资源正确载入且游戏内成功显示”会输出成功日志，后续打开不重复输出；失败与冲突始终记录。

完全不显示：检查 personId、clothId、相对路径、moc3 版本、全部 PNG 和错误日志。
显示但不动：检查上述标准参数 ID；物理还需有效 physics3 和正确输入输出参数。
眨眼漏瞳孔：回 Cubism Editor 检查剪贴 ID，并整套重新导出和替换运行时文件。
位置不一致：严格按“比例 -> 锚点 -> 偏移”校准，不要复制另一分辨率素材的绝对数值。
