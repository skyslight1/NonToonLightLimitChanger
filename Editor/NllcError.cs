using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;

namespace Kuro.NTLLC.Editor
{
    /// <summary>NDMF 错误上报用的双语（en / zh-Hans）错误定义。</summary>
    internal sealed class NllcError : SimpleError
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["nllc.no_targets"] = "No NonToon materials found on this avatar. The NonToon Light Limit Changer component was removed.",
            ["nllc.no_targets_hint"] = "Add the component only on avatars whose materials use the NonToon shader.",
            ["nllc.module_missing"] = "The NonToon LLC brightness module is not compiled into NonToon. Brightness sliders would do nothing, so nothing was generated.",
            ["nllc.module_missing_hint"] = "Run Tools > NonToon Light Limit Changer > Re-register brightness module, wait for NonToon to reimport, then build again.",
        };

        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>
        {
            ["nllc.no_targets"] = "这个 Avatar 上没有找到 NonToon 材质，已自动移除 NonToon Light Limit Changer 组件。",
            ["nllc.no_targets_hint"] = "请只在材质使用 NonToon 着色器的 Avatar 上挂这个组件。",
            ["nllc.module_missing"] = "NonToon LLC 亮度模块没有被编译进 NonToon（材质上找不到 _jp_kuro_nllc_Min 属性），滑条不会生效，因此没有生成任何内容。",
            ["nllc.module_missing_hint"] = "执行菜单 Tools → NonToon Light Limit Changer → 重新注册亮度模块，等 NonToon 重导入完成后再构建。",
        };

        public override Localizer Localizer { get; }
        public override string TitleKey { get; }
        public override string HintKey { get; }
        public override ErrorSeverity Severity { get; }

        private NllcError(string titleKey, string hintKey, ErrorSeverity severity)
        {
            Localizer = new Localizer("zh-Hans", () => new List<(string, Func<string, string>)>
            {
                ("en-US", key => En.TryGetValue(key, out var v) ? v : key),
                ("zh-Hans", key => Zh.TryGetValue(key, out var v) ? v : key),
            });
            TitleKey = titleKey;
            HintKey = hintKey;
            Severity = severity;
        }

        public static NllcError NoTargets() => new NllcError("nllc.no_targets", "nllc.no_targets_hint", ErrorSeverity.NonFatal);
        public static NllcError ModuleMissing() => new NllcError("nllc.module_missing", "nllc.module_missing_hint", ErrorSeverity.Error);
    }
}
