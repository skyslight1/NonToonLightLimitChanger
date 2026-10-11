using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kuro.NTLLC
{
    /// <summary>
    /// NonToon Light Limit Changer 设置组件。
    /// 挂在 Avatar 根层级下任意物体上；NDMF 构建时（Transforming 阶段、Modular Avatar 之前）
    /// 读取这里的配置生成 FX 动画层、表情菜单与同步参数，然后本组件会被删除。
    /// </summary>
    [AddComponentMenu("Modular Avatar/NonToon Light Limit Changer")]
    [DisallowMultipleComponent]
    public sealed class NonToonLightLimitChanger : MonoBehaviour, VRC.SDKBase.IEditorOnly
    {
        public NTLLCParameters Parameters = new NTLLCParameters();

        /// <summary>预设列表：每个预设生成一个菜单按钮，一键把所有启用的滑条设为该预设的值。</summary>
        public List<NTLLCPreset> Presets = new List<NTLLCPreset>();

        /// <summary>不参与控制的例外：可以是 GameObject / Renderer / Material。</summary>
        public List<UnityEngine.Object> Excludes = new List<UnityEngine.Object>();

        public WriteDefaultsSetting WriteDefaults = WriteDefaultsSetting.MatchAvatar;

        /// <summary>在 Avatar 根上生成 MA Mesh Settings，统一所有网格的 Probe Anchor 与 Bounds（LLC v2 的「网格设置对齐」）。</summary>
        public bool AlignMeshSettings = false;
    }

    /// <summary>表情菜单插入位置。</summary>
    public enum NTLLCMenuMode
    {
        /// <summary>跟随组件物体层级：放在 MA 菜单文件夹（带 ModularAvatarMenuItem 的物体）下时装进该页面，否则装到表情菜单根。</summary>
        Auto = 0,
        /// <summary>始终装到表情菜单根。</summary>
        Root = 1,
        /// <summary>按 MenuCustomPath 自动生成页面链（如 插件/NTLLC），挂在组件物体下。</summary>
        CustomPath = 2,
    }

    [Serializable]
    public class NTLLCParameters
    {
        // ---- 常规 ----

        /// <summary>初始状态即启用（菜单开关默认值）。</summary>
        public bool IsDefaultUse = false;

        /// <summary>参数跨世界保存（saved）。</summary>
        public bool IsValueSave = false;

        /// <summary>true：用下面的默认值覆盖材质的当前亮度值；false：读取各材质当前值作为菜单关闭时的默认状态。</summary>
        public bool OverwriteDefaultLightMinMax = true;

        // ---- 亮度（v2 风格：下限 / 上限为两个独立滑条）----

        /// <summary>合并为单一滑条（v1 风格，min=max 同扫）。默认关闭 = v2 风格的「最暗」「最亮」独立滑条。</summary>
        public bool IsSeparateLightControl = true;

        /// <summary>单滑条模式初始位置（0-1）。</summary>
        [Range(0f, 1f)] public float DefaultLightValue = 0.5f;

        /// <summary>「最暗」滑条初始位置（0-1）。</summary>
        [Range(0f, 1f)] public float DefaultMinLightValue = 0.05f;

        /// <summary>「最亮」滑条初始位置（0-1）。</summary>
        [Range(0f, 1f)] public float DefaultMaxLightValue = 1f;

        /// <summary>滑条左端映射到的材质亮度值（默认 0 = 允许全黑）。</summary>
        [Range(0f, 2f)] public float MinLightValue = 0f;

        /// <summary>滑条右端映射到的材质亮度值（默认 1 = 全亮；Built-in RP 下超过 1 无额外效果）。</summary>
        [Range(0f, 2f)] public float MaxLightValue = 1f;

        // ---- 附加控制 ----

        /// <summary>明度（整体亮度倍数，0~2）。</summary>
        public bool AllowValueControl = false;

        /// <summary>明度初始位置（0-1，映射到 0~2）。</summary>
        [Range(0f, 1f)] public float InitialValueControlValue = 0.5f;

        /// <summary>色相（±180° 旋转，绕灰轴）。</summary>
        public bool AllowHueControl = false;

        /// <summary>色相初始位置（0-1，映射到 -1~1）。</summary>
        [Range(0f, 1f)] public float InitialHueControlValue = 0.5f;

        /// <summary>伽马（0~2，&lt;1 提亮中间调）。</summary>
        public bool AllowGammaControl = false;

        /// <summary>伽马初始位置（0-1，映射到 0~2）。</summary>
        [Range(0f, 1f)] public float InitialGammaControlValue = 0.5f;

        /// <summary>灯光单色化（削弱彩色灯光影响，0~1）。</summary>
        public bool AllowMonochromeControl = false;

        /// <summary>单色化初始位置（0-1）。</summary>
        [Range(0f, 1f)] public float InitialMonochromeControlValue = 0f;

        public bool AllowColorTempControl = false;

        /// <summary>色温滑条初始位置（0.5 = 不改变）。</summary>
        [Range(0f, 1f)] public float InitialTempControlValue = 0.5f;

        public bool AllowSaturationControl = false;

        /// <summary>饱和度滑条初始位置（0.5 = 不改变，映射到 0-2 的饱和度）。</summary>
        [Range(0f, 1f)] public float InitialSaturationControlValue = 0.5f;

        public bool AllowUnlitControl = false;

        /// <summary>Unlit 滑条初始位置（0 = 不改变）。</summary>
        [Range(0f, 1f)] public float InitialUnlitControlValue = 0f;

        // ---- 布局 ----

        /// <summary>添加重置按钮（把所有滑条恢复默认值）。</summary>
        public bool AddResetButton = false;

        /// <summary>把颜色系控制（明度/色相/伽马/饱和度/单色化/无光照/色温）收进「颜色」子菜单。</summary>
        public bool IsGroupingAdditionalControls = false;

        // ---- 表情菜单插入位置 ----

        /// <summary>表情菜单的插入位置。</summary>
        public NTLLCMenuMode MenuInstallMode = NTLLCMenuMode.Auto;

        /// <summary>MenuInstallMode=CustomPath 时的页面路径（用 / 分层，如「插件/NTLLC」）；构建时在组件物体下自动生成页面链。</summary>
        public string MenuCustomPath = "插件/NTLLC";

        /// <summary>生成的表情菜单文字语言。</summary>
        public NTLLCMenuLanguage MenuLanguage = NTLLCMenuLanguage.Chinese;
    }

    /// <summary>表情菜单文字语言。</summary>
    public enum NTLLCMenuLanguage
    {
        /// <summary>中文（默认）。</summary>
        Chinese = 0,
        /// <summary>English menu labels.</summary>
        English = 1,
    }

    /// <summary>一个预设 = 一个菜单按钮，按下时把所有启用的滑条设为这里的值（0-1 的滑条位置）。</summary>
    [Serializable]
    public class NTLLCPreset
    {
        public string Name = "预设";

        [Range(0f, 1f)] public float MinLight = 0.05f;
        [Range(0f, 1f)] public float MaxLight = 1f;
        [Range(0f, 1f)] public float Value = 0.5f;
        [Range(0f, 1f)] public float Hue = 0.5f;
        [Range(0f, 1f)] public float Gamma = 0.5f;
        [Range(0f, 1f)] public float Monochrome = 0f;
        [Range(0f, 1f)] public float Saturation = 0.5f;
        [Range(0f, 1f)] public float Unlit = 0f;
        [Range(0f, 1f)] public float ColorTemp = 0.5f;
    }

    public enum WriteDefaultsSetting
    {
        MatchAvatar,
        OFF,
        ON,
    }
}
