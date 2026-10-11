using nadena.dev.ndmf;
using UnityEngine;

[assembly: ExportsPlugin(typeof(Kuro.NTLLC.Editor.PluginDefinition))]

namespace Kuro.NTLLC.Editor
{
    internal sealed class PluginDefinition : Plugin<PluginDefinition>
    {
        public const string ModularAvatarQualifiedName = "nadena.dev.modular-avatar";

        public override string QualifiedName => "jp.kuro.nontoon-light-limit-changer";

        public override string DisplayName => "NonToon Light Limit Changer";

        public override Color? ThemeColor => new Color(0.45f, 0.30f, 0.80f);

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .BeforePlugin(ModularAvatarQualifiedName)
                .Run(GeneratePass.Instance);
        }
    }
}
