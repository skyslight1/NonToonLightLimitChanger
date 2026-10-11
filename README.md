# NonToon Light Limit Changer

给 [NonToon](https://github.com/lilxyzw/NonToon) 提供 [Light Limit Changer](https://github.com/Azukimochi/LightLimitChangerForMA)（lilToon 用）那样的**非破坏式亮度控制**：

- 挂一个组件 → 上传时自动生成表情菜单滑条（启用开关 / 最暗 / 最亮 / 明度 / 色相 / 伽马 / 饱和度 / 灯光单色化 / 无光照 / 色温 / 预设 / 重置）
- 亮度上下限、luma 归一夹取（不把阴影拍平）
- 参数可跨世界保存（saved）、可设默认值、可加重置按钮
- NDMF 非破坏式构建（`BuildPhase.Transforming`、Modular Avatar 之前），不改场景里的任何资产

## 与 Light Limit Changer 的功能对照（对齐 v2）

| LLC v2 功能 | 本插件 | 说明 |
| --- | --- | --- |
| 亮度下限 / 上限（两个独立滑条） | ✅ | 默认即 v2 风格的「最暗」「最亮」独立滑条；也可合并成 v1 风格单滑条 |
| 明度（Brightness/Value） | ✅ | 0~2，驱动模块 `_Brightness`（作用于光照，Unlit 物体不受影响） |
| 色相（Hue） | ✅ | ±180° 绕灰轴旋转（v2 范围 -1~1） |
| 伽马（Gamma） | ✅ | 0~2，pow(col, gamma) |
| 彩度（Saturation） | ✅ | 0=无彩 1=原样 2=加深 |
| 灯光单色化（モノクロ化） | ✅ | 对灯光色去饱和，削弱彩色灯光影响（与 v2 同语义） |
| 色温 | ✅ | 与 LLC 相同曲线 |
| Unlit | ✅ | |
| 预设（プリセット） | ✅ | 每个预设 = 菜单按钮，一键切换所有滑条（Int 参数 + ParameterDriver 层） |
| 网格设置对齐（Anchor・Bounds 揃え） | ✅ | Avatar 根缺 MA Mesh Settings 时生成（Anchor/Bounds 指向根）；已有则不动 |
| 重置按钮 / WD 设置 / 排除列表 / saved | ✅ | |
| 自发光 | ❌ | NonToon 没有自发光 |
| 逆光ライト | ❌ | NonToon 的背光只有 Sharpness/Range 参数、无强度乘数，无法忠实还原 |
| lilToon/Poiyomi 专属项（顶点光、AddPass、PP Animation 等） | ➖ | shader 专属功能，与 NonToon 无关 |

## 原理

- **Shader Core 模块**（`Shaders/LightLimit/`）：挂在 `modifylight` 阶段、声明在 NonToon 自带 Lighten 之后，对 `sd.lightColor` 做「亮度倍数 → 色温 → luma 归一夹取（上下限）→ Unlit 拍平」；另有一个 `postpixel` 阶段做饱和度。逐材质属性带 `_jp_kuro_nllc_` 前缀：`_Min`、`_Max`、`_Brightness`、`_Saturation`、`_Unlit`、`_ColorTemp`、`_GlobalMaskChannel`。
- **模块注册**：Shader Core 按 `ProjectSettings/jp.lilxyzw.shadercore.asset` 的白名单编译模块，编辑器启动时会自动把本模块追加进 NonToon 的模块列表并重导入（幂等，只追加不删除）。也可手动执行菜单 **Tools → NonToon Light Limit Changer → 重新注册亮度模块**。
- **NDMF 插件**：克隆目标材质（`ObjectRegistry.RegisterReplacedObject`，其余工具能追踪）→ 生成 Direct Blend Tree 结构的 FX 层（WD ON，恒驱）→ 挂 `ModularAvatarMergeAnimator` / `ModularAvatarParameters` / MA 菜单项 → 删除设置组件。材质属性动画绑定形如 `material._jp_kuro_nllc_Min`。

## 安装

把本文件夹放进工程的 `Packages/`（或打成 VPM 包用 VCC/ALCOM 安装）。需要工程里已有 NonToon、Shader Core、Modular Avatar、NDMF。

## 使用

1. 安装后第一次打开工程，Console 会出现模块注册日志；材质面板的 NonToon 材质会多出 `NonToon LLC` 一组参数。
2. 选中 Avatar，菜单 **GameObject → NonToon Light Limit Changer** 添加组件（或在 AddComponent 里搜 "NonToon Light Limit Changer"）。
3. Inspector 里勾选要生成的控制项、调好默认值，上传即可。

## 已知限制

- 模块没注册成功时（材质上没有 `_jp_kuro_nllc_Min` 属性），构建会报错提示先执行注册菜单。
- 动画内换装（材质 swap）引用的材质不会被克隆替换；如果有表情动画换 NonToon 材质，那部分不受滑条控制。
- Built-in RP 下 NonToon 把光照上限 `saturate` 在 1，亮度倍数/上限超过 1 不会有额外效果（URP 下可以）。

## 许可

MIT。模块注册器与 luma 夹取算法参考自 [NonToonLightLimit](https://github.com/njsgdd10086/NonToonLightLimit)（MIT, ATRI_NAIXU）；
Direct Blend Tree 结构思路参考自 [Light Limit Changer v1](https://github.com/Azukimochi25/Light-Limit-Changer)（MIT, Azukimochi / gomoru.su）。
