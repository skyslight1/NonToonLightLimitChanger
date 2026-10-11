# LightLimit 模块（Shader Core 外挂模块）

本目录是一个 Shader Core 模块：`jp.kuro.nllc.scmodule` + `properties.hlsl` + phase 代码。
注册进 NonToon 的模块白名单后（由 `Editor/NllcModuleInstaller.cs` 自动完成），
NonToon / NonToonFur 重导入时会把它编译进 shader，材质面板出现「NonToon LLC」参数组。

- 挂载点：`modifylight` 阶段（`afters: ["Lighten"]`，在 NonToon 自带 Light Boost 之后执行）
  + `postpixel` 阶段（饱和度）。
- 属性编译后带模块前缀：`_Min` → `material._jp_kuro_nllc_Min`。
- **properties.hlsl 里不能写注释和多余文本**：Shader Core 的解析器逐行匹配
  `SC_xxx` 语法，其它任何行（包括 `//` 注释）都会让 scshader 导入失败。
  说明写在文件里也只会被原样插进 shader 函数体，所以只有这一个 properties 文件例外。
- `phase_*.hlsl` 里的代码是**原样插进片段着色函数体**的：只能写语句和局部变量，
  不能声明全局变量 / 定义函数 / static const（`ll` 前缀的都是局部变量）。

属性一览（默认值）：

| 属性 | 默认 | 作用 |
| --- | --- | --- |
| `_Min` | 0 | 亮度下限（0 = 不限制） |
| `_Max` | 1 | 亮度上限（1 = NonToon 原本的上限） |
| `_Brightness` | 1 | 亮度倍数 |
| `_Saturation` | 1 | 饱和度（0 无彩 / 2 加深） |
| `_Unlit` | 0 | 无光照化 |
| `_ColorTemp` | 0.5 | 色温（0 偏暖 / 1 偏冷） |
| `_GlobalMaskChannel` | 3 | 生效范围用的共享遮罩通道（默认 A） |
