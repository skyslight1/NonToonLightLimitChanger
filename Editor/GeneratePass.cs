using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace Kuro.NTLLC.Editor
{
    /// <summary>
    /// 核心构建 pass：收集 NonToon 材质 → 克隆 → 生成 FX 动画（Direct Blend Tree）→
    /// 挂 Modular Avatar 的 MergeAnimator / Parameters / MenuInstaller → 删除设置组件。
    /// 与 Light Limit Changer v1 同阶段（Transforming、Modular Avatar 之前）。
    /// 功能面对齐 LLC v2：独立下限/上限、明度、色相、伽马、灯光单色化、预设、网格设置对齐。
    /// </summary>
    internal sealed class GeneratePass : Pass<GeneratePass>
    {
        // ---- 模块属性（编译后带 _jp_kuro_nllc_ 前缀）----
        private const string Prefix = "_jp_kuro_nllc_";
        private const string PMin = Prefix + "Min";
        private const string PMax = Prefix + "Max";
        private const string PValue = Prefix + "Brightness";
        private const string PHue = Prefix + "Hue";
        private const string PGamma = Prefix + "Gamma";
        private const string PSaturation = Prefix + "Saturation";
        private const string PMono = Prefix + "Monochrome";
        private const string PUnlit = Prefix + "Unlit";
        private const string PTemp = Prefix + "ColorTemp";

        // ---- VRChat 参数名 ----
        private const string ParamEnable = "NTLLC_Enable";
        private const string ParamLight = "NTLLC_Light";
        private const string ParamLightMin = "NTLLC_LightMin";
        private const string ParamLightMax = "NTLLC_LightMax";
        private const string ParamValue = "NTLLC_Value";
        private const string ParamHue = "NTLLC_Hue";
        private const string ParamGamma = "NTLLC_Gamma";
        private const string ParamSaturation = "NTLLC_Saturation";
        private const string ParamMono = "NTLLC_Monochrome";
        private const string ParamUnlit = "NTLLC_Unlit";
        private const string ParamTemp = "NTLLC_ColorTemp";
        private const string ParamReset = "NTLLC_Reset";
        private const string ParamPreset = "NTLLC_Preset";
        private const string DirectDummyParam = "1";

        public override string DisplayName => "Generate NonToon light limit animations";

        private sealed class TargetRenderer
        {
            public Renderer Renderer;
            public string Path;
            public Type Type;
            public Material OriginalMat; // 克隆前的原始材质（cloneMap 的键）
            public Material Mat;         // 克隆后的材质
        }

        protected override void Execute(BuildContext context)
        {
            var root = context.AvatarRootObject;
            var settings = root.GetComponentInChildren<NonToonLightLimitChanger>();
            if (settings == null) return;

            try
            {
                Build(context, root, settings);
            }
            catch (Exception e)
            {
                // NDMF 只把 pass 异常写进它的 ErrorReport 面板，这里补一条 Console 日志方便排查
                Debug.LogError("[NTLLC] 生成 pass 异常：" + e);
                throw;
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        private void Build(BuildContext context, GameObject root, NonToonLightLimitChanger settings)
        {
            var p = settings.Parameters;

            // ---- 1. 收集目标 renderer / 材质 ----
            var targets = CollectTargets(context, root, settings);
            if (targets.Count == 0)
            {
                ErrorReport.ReportError(NllcError.NoTargets());
                return;
            }

            // 模块没编译进 shader 时材质上不会有属性，动画会静默失效，直接报错拦截
            if (!targets[0].Mat.HasProperty(PMin))
            {
                ErrorReport.ReportError(NllcError.ModuleMissing());
                return;
            }

            // ---- 2. 克隆材质并替换引用 ----
            var cloneMap = new Dictionary<Material, Material>();
            foreach (var group in targets.GroupBy(t => t.Renderer))
            {
                var mats = group.Key.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !IsNonToon(mats[i]) || IsExcluded(settings, mats[i])) continue;
                    if (!cloneMap.TryGetValue(mats[i], out var clone))
                    {
                        clone = new Material(mats[i]) { name = mats[i].name + " (NTLLC)", hideFlags = HideFlags.HideInHierarchy };
                        cloneMap[mats[i]] = clone;
                        context.AssetSaver.SaveAsset(clone);
                        ObjectRegistry.RegisterReplacedObject(mats[i], clone);
                    }
                    mats[i] = clone;
                    changed = true;
                }
                if (changed) group.Key.sharedMaterials = mats;
            }
            foreach (var t in targets) t.Mat = cloneMap[t.OriginalMat];

            // ---- 3. 各 renderer 的默认值（菜单关闭状态）----
            float MinDefault(TargetRenderer t) =>
                p.OverwriteDefaultLightMinMax ? p.DefaultMinLightValue : SafeGet(t.Mat, PMin, 0f);
            float MaxDefault(TargetRenderer t) =>
                p.OverwriteDefaultLightMinMax ? p.DefaultMaxLightValue : SafeGet(t.Mat, PMax, 1f);

            // ---- 4. 动画 clip ----
            var subObjects = new List<Object>();
            var resetObjects = new List<Object>();
            var saved = context.AssetSaver;

            AnimationClip NewClip(string name, List<Object> into = null)
            {
                var clip = new AnimationClip { name = name, hideFlags = HideFlags.HideInHierarchy };
                (into ?? subObjects).Add(clip);
                return clip;
            }

            void SetCurve(AnimationClip clip, string prop, Func<TargetRenderer, float> value)
            {
                // 材质属性动画绑定的固定格式：material._属性名
                foreach (var t in targets)
                    clip.SetCurve(t.Path, t.Type, "material." + prop, AnimationCurve.Constant(0f, 1f, value(t)));
            }

            // 默认（关闭）clip：所有启用的控制项都写回默认值
            var defaultClip = NewClip("NTLLC Default");
            SetCurve(defaultClip, PMin, MinDefault);
            SetCurve(defaultClip, PMax, MaxDefault);
            if (p.AllowValueControl)
                SetCurve(defaultClip, PValue, t => SafeGet(t.Mat, PValue, 1f));
            if (p.AllowHueControl)
                SetCurve(defaultClip, PHue, t => SafeGet(t.Mat, PHue, 0f));
            if (p.AllowGammaControl)
                SetCurve(defaultClip, PGamma, t => SafeGet(t.Mat, PGamma, 1f));
            if (p.AllowSaturationControl)
                SetCurve(defaultClip, PSaturation, t => SafeGet(t.Mat, PSaturation, 1f));
            if (p.AllowMonochromeControl)
                SetCurve(defaultClip, PMono, t => SafeGet(t.Mat, PMono, 0f));
            if (p.AllowUnlitControl)
                SetCurve(defaultClip, PUnlit, t => SafeGet(t.Mat, PUnlit, 0f));
            if (p.AllowColorTempControl)
                SetCurve(defaultClip, PTemp, t => SafeGet(t.Mat, PTemp, 0.5f));

            // 滑条两端 clip
            AnimationClip lightLow, lightHigh, minLow, minHigh, maxLow, maxHigh;
            if (p.IsSeparateLightControl)
            {
                minLow = NewClip("NTLLC LightMin Low"); SetCurve(minLow, PMin, _ => p.MinLightValue);
                minHigh = NewClip("NTLLC LightMin High"); SetCurve(minHigh, PMin, _ => p.MaxLightValue);
                maxLow = NewClip("NTLLC LightMax Low"); SetCurve(maxLow, PMax, _ => p.MinLightValue);
                maxHigh = NewClip("NTLLC LightMax High"); SetCurve(maxHigh, PMax, _ => p.MaxLightValue);
                lightLow = lightHigh = null;
            }
            else
            {
                // 与 LLC v1 相同：单滑条同时驱动 min = max = X(t)，从「允许全黑」扫到「全亮」
                lightLow = NewClip("NTLLC Light Low"); SetCurve(lightLow, PMin, _ => p.MinLightValue); SetCurve(lightLow, PMax, _ => p.MinLightValue);
                lightHigh = NewClip("NTLLC Light High"); SetCurve(lightHigh, PMin, _ => p.MaxLightValue); SetCurve(lightHigh, PMax, _ => p.MaxLightValue);
                minLow = minHigh = maxLow = maxHigh = null;
            }

            AnimationClip valueLow = null, valueHigh = null;
            if (p.AllowValueControl)
            {
                valueLow = NewClip("NTLLC Value Low"); SetCurve(valueLow, PValue, _ => 0f);
                valueHigh = NewClip("NTLLC Value High"); SetCurve(valueHigh, PValue, _ => 2f);
            }

            AnimationClip hueLow = null, hueHigh = null;
            if (p.AllowHueControl)
            {
                hueLow = NewClip("NTLLC Hue Low"); SetCurve(hueLow, PHue, _ => -1f);
                hueHigh = NewClip("NTLLC Hue High"); SetCurve(hueHigh, PHue, _ => 1f);
            }

            AnimationClip gammaLow = null, gammaHigh = null;
            if (p.AllowGammaControl)
            {
                gammaLow = NewClip("NTLLC Gamma Low"); SetCurve(gammaLow, PGamma, _ => 0f);
                gammaHigh = NewClip("NTLLC Gamma High"); SetCurve(gammaHigh, PGamma, _ => 2f);
            }

            AnimationClip satLow = null, satHigh = null, monoLow = null, monoHigh = null, unlitLow = null, unlitHigh = null, tempLow = null, tempHigh = null;
            if (p.AllowSaturationControl)
            {
                satLow = NewClip("NTLLC Saturation Low"); SetCurve(satLow, PSaturation, _ => 0f);
                satHigh = NewClip("NTLLC Saturation High"); SetCurve(satHigh, PSaturation, _ => 2f);
            }
            if (p.AllowMonochromeControl)
            {
                monoLow = NewClip("NTLLC Mono Low"); SetCurve(monoLow, PMono, _ => 0f);
                monoHigh = NewClip("NTLLC Mono High"); SetCurve(monoHigh, PMono, _ => 1f);
            }
            if (p.AllowUnlitControl)
            {
                unlitLow = NewClip("NTLLC Unlit Low"); SetCurve(unlitLow, PUnlit, _ => 0f);
                unlitHigh = NewClip("NTLLC Unlit High"); SetCurve(unlitHigh, PUnlit, _ => 1f);
            }
            if (p.AllowColorTempControl)
            {
                tempLow = NewClip("NTLLC ColorTemp Low"); SetCurve(tempLow, PTemp, _ => 0f);
                tempHigh = NewClip("NTLLC ColorTemp High"); SetCurve(tempHigh, PTemp, _ => 1f);
            }

            // ---- 5. 混合树 ----
            BlendTree NewTree(string name, string blendParam, BlendTreeType type)
            {
                var tree = new BlendTree { name = name, hideFlags = HideFlags.HideInHierarchy, blendParameter = blendParam };
                if (type == BlendTreeType.Direct)
                {
                    tree.blendType = BlendTreeType.Direct;
                    using (var so = new SerializedObject(tree))
                    {
                        so.FindProperty("m_NormalizedBlendValues").boolValue = false;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                subObjects.Add(tree);
                return tree;
            }

            BlendTree Radial(string param, AnimationClip low, AnimationClip high)
            {
                var tree = NewTree("NTLLC " + param, param, BlendTreeType.Simple1D);
                tree.AddChild(low, 0f);
                tree.AddChild(high, 1f);
                return tree;
            }

            // Enable 门内的控制项混合树（Direct 恒驱，属性互不重叠所以可以并列）
            var controls = NewTree("NTLLC Controls", DirectDummyParam, BlendTreeType.Direct);
            if (p.IsSeparateLightControl)
            {
                controls.AddChild(Radial(ParamLightMin, minLow, minHigh));
                controls.AddChild(Radial(ParamLightMax, maxLow, maxHigh));
            }
            else
            {
                controls.AddChild(Radial(ParamLight, lightLow, lightHigh));
            }
            if (p.AllowValueControl) controls.AddChild(Radial(ParamValue, valueLow, valueHigh));
            if (p.AllowHueControl) controls.AddChild(Radial(ParamHue, hueLow, hueHigh));
            if (p.AllowGammaControl) controls.AddChild(Radial(ParamGamma, gammaLow, gammaHigh));
            if (p.AllowSaturationControl) controls.AddChild(Radial(ParamSaturation, satLow, satHigh));
            if (p.AllowMonochromeControl) controls.AddChild(Radial(ParamMono, monoLow, monoHigh));
            if (p.AllowUnlitControl) controls.AddChild(Radial(ParamUnlit, unlitLow, unlitHigh));
            if (p.AllowColorTempControl) controls.AddChild(Radial(ParamTemp, tempLow, tempHigh));

            var directChildren = controls.children;
            for (var i = 0; i < directChildren.Length; i++) directChildren[i].directBlendParameter = DirectDummyParam;
            controls.children = directChildren;

            // Enable 门：关 = 默认 clip，开 = 控制项
            var gate = NewTree("NTLLC Enable Gate", ParamEnable, BlendTreeType.Simple1D);
            gate.AddChild(defaultClip, 0f);
            gate.AddChild(controls, 1f);

            var rootTree = NewTree("NTLLC Root", DirectDummyParam, BlendTreeType.Direct);
            rootTree.AddChild(gate, 1f);
            var rootChildren = rootTree.children;
            for (var i = 0; i < rootChildren.Length; i++) rootChildren[i].directBlendParameter = DirectDummyParam;
            rootTree.children = rootChildren;

            // ---- 6. FX 控制器 ----
            var controller = new AnimatorController { name = "NTLLC FX" };
            var layer = new AnimatorControllerLayer
            {
                name = "NonToon Light Limit",
                defaultWeight = 1f,
                stateMachine = new AnimatorStateMachine { name = "NTLLC SM", hideFlags = HideFlags.HideInHierarchy },
            };
            subObjects.Add(layer.stateMachine);
            var state = layer.stateMachine.AddState("NTLLC (WD ON)");
            state.writeDefaultValues = true;
            state.motion = rootTree;
            controller.AddLayer(layer);

            var wdOn = settings.WriteDefaults == WriteDefaultsSetting.ON;

            // ---- 6a. 预设层：Off + 每预设一个状态（Driver 设参数后回 Off）----
            var presets = settings.Presets ?? new List<NTLLCPreset>();
            presets = presets.Where(pr => pr != null && !string.IsNullOrEmpty(pr.Name)).ToList();

            // MA 的 MergeAnimator 只收集控制器里「已声明」的参数，
            // 只写混合树/转移的参数名引用不会进 FX 控制器（LLC v1 同样显式 AddParameter）。
            void Declare(AnimatorController ac, string name, AnimatorControllerParameterType type, float numeric)
            {
                var param = new AnimatorControllerParameter { name = name, type = type };
                if (type == AnimatorControllerParameterType.Bool) param.defaultBool = numeric > 0.5f;
                else if (type == AnimatorControllerParameterType.Int) param.defaultInt = Mathf.RoundToInt(numeric);
                else param.defaultFloat = numeric;
                ac.AddParameter(param);
            }

            Declare(controller, DirectDummyParam, AnimatorControllerParameterType.Float, 1f);
            // Enable 必须是 Float：1D 混合树不支持 Bool 驱动，且 Bool 树会被 MA 反应式系统/VRCFury
            // 在 play 处理时改写成无关的物体代理参数（实测 shinano 女仆服上滑条因此全部失效）。
            // VRChat 的 Toggle 对 Float 参数同样有效（选中=value，取消=0）。
            Declare(controller, ParamEnable, AnimatorControllerParameterType.Float, p.IsDefaultUse ? 1f : 0f);
            if (p.IsSeparateLightControl)
            {
                Declare(controller, ParamLightMin, AnimatorControllerParameterType.Float, p.DefaultMinLightValue);
                Declare(controller, ParamLightMax, AnimatorControllerParameterType.Float, p.DefaultMaxLightValue);
            }
            else
            {
                Declare(controller, ParamLight, AnimatorControllerParameterType.Float, p.DefaultLightValue);
            }
            if (p.AllowValueControl) Declare(controller, ParamValue, AnimatorControllerParameterType.Float, p.InitialValueControlValue);
            if (p.AllowHueControl) Declare(controller, ParamHue, AnimatorControllerParameterType.Float, p.InitialHueControlValue);
            if (p.AllowGammaControl) Declare(controller, ParamGamma, AnimatorControllerParameterType.Float, p.InitialGammaControlValue);
            if (p.AllowSaturationControl) Declare(controller, ParamSaturation, AnimatorControllerParameterType.Float, p.InitialSaturationControlValue);
            if (p.AllowMonochromeControl) Declare(controller, ParamMono, AnimatorControllerParameterType.Float, p.InitialMonochromeControlValue);
            if (p.AllowUnlitControl) Declare(controller, ParamUnlit, AnimatorControllerParameterType.Float, p.InitialUnlitControlValue);
            if (p.AllowColorTempControl) Declare(controller, ParamTemp, AnimatorControllerParameterType.Float, p.InitialTempControlValue);
            if (presets.Count > 0) Declare(controller, ParamPreset, AnimatorControllerParameterType.Int, 0f);

            if (presets.Count > 0)
            {
                var presetBlank = NewClip("NTLLC Preset Blank");
                var presetLayer = new AnimatorControllerLayer
                {
                    name = "NonToon Light Limit Presets",
                    defaultWeight = 1f,
                    stateMachine = new AnimatorStateMachine { name = "NTLLC Preset SM", hideFlags = HideFlags.HideInHierarchy },
                };
                subObjects.Add(presetLayer.stateMachine);

                var presetOff = presetLayer.stateMachine.AddState("Off");
                presetOff.writeDefaultValues = wdOn;
                presetOff.motion = presetBlank;

                for (var i = 0; i < presets.Count; i++)
                {
                    var preset = presets[i];
                    var st = presetLayer.stateMachine.AddState("预设 " + (i + 1));
                    st.writeDefaultValues = wdOn;
                    st.motion = presetBlank;

                    var driver = st.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                    void Apply(string name, float value) =>
                        driver.parameters.Add(new VRC.SDKBase.VRC_AvatarParameterDriver.Parameter { name = name, value = value });
                    if (p.IsSeparateLightControl)
                    {
                        Apply(ParamLightMin, preset.MinLight);
                        Apply(ParamLightMax, preset.MaxLight);
                    }
                    else
                    {
                        Apply(ParamLight, preset.MinLight * 0.5f + preset.MaxLight * 0.5f);
                    }
                    if (p.AllowValueControl) Apply(ParamValue, preset.Value);
                    if (p.AllowHueControl) Apply(ParamHue, preset.Hue);
                    if (p.AllowGammaControl) Apply(ParamGamma, preset.Gamma);
                    if (p.AllowSaturationControl) Apply(ParamSaturation, preset.Saturation);
                    if (p.AllowMonochromeControl) Apply(ParamMono, preset.Monochrome);
                    if (p.AllowUnlitControl) Apply(ParamUnlit, preset.Unlit);
                    if (p.AllowColorTempControl) Apply(ParamTemp, preset.ColorTemp);
                    Apply(ParamPreset, 0f);

                    var toPreset = presetOff.AddTransition(st);
                    toPreset.hasExitTime = false;
                    toPreset.duration = 0f;
                    toPreset.AddCondition(AnimatorConditionMode.Equals, i + 1, ParamPreset);

                    var back = st.AddTransition(presetOff);
                    back.hasExitTime = false;
                    back.duration = 0f;
                    // 无条件转移会被 Unity 忽略；Driver 进入本状态时已把 Preset 清 0，条件立即成立
                    back.AddCondition(AnimatorConditionMode.Equals, 0f, ParamPreset);
                }
                controller.AddLayer(presetLayer);
            }

            if (p.AddResetButton)
            {
                var blank = NewClip("NTLLC Blank", resetObjects);
                var resetLayer = new AnimatorControllerLayer
                {
                    name = "NonToon Light Limit Reset",
                    defaultWeight = 1f,
                    stateMachine = new AnimatorStateMachine { name = "NTLLC Reset SM", hideFlags = HideFlags.HideInHierarchy },
                };
                subObjects.Add(resetLayer.stateMachine);

                var stateOff = resetLayer.stateMachine.AddState("Off");
                stateOff.writeDefaultValues = wdOn;
                stateOff.motion = blank;
                var stateOn = resetLayer.stateMachine.AddState("On");
                stateOn.writeDefaultValues = wdOn;
                stateOn.motion = blank;

                var toOn = stateOff.AddTransition(stateOn);
                toOn.hasExitTime = false;
                toOn.duration = 0f;
                toOn.AddCondition(AnimatorConditionMode.If, 0f, ParamReset);
                var toOff = stateOn.AddTransition(stateOff);
                toOff.hasExitTime = false;
                toOff.duration = 0f;
                toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, ParamReset);

                var driver = stateOn.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                void ResetParam(string name, float value) =>
                    driver.parameters.Add(new VRC.SDKBase.VRC_AvatarParameterDriver.Parameter { name = name, value = value });
                ResetParam(ParamEnable, p.IsDefaultUse ? 1f : 0f);
                if (p.IsSeparateLightControl)
                {
                    ResetParam(ParamLightMin, p.DefaultMinLightValue);
                    ResetParam(ParamLightMax, p.DefaultMaxLightValue);
                }
                else
                {
                    ResetParam(ParamLight, p.DefaultLightValue);
                }
                if (p.AllowValueControl) ResetParam(ParamValue, p.InitialValueControlValue);
                if (p.AllowHueControl) ResetParam(ParamHue, p.InitialHueControlValue);
                if (p.AllowGammaControl) ResetParam(ParamGamma, p.InitialGammaControlValue);
                if (p.AllowSaturationControl) ResetParam(ParamSaturation, p.InitialSaturationControlValue);
                if (p.AllowMonochromeControl) ResetParam(ParamMono, p.InitialMonochromeControlValue);
                if (p.AllowUnlitControl) ResetParam(ParamUnlit, p.InitialUnlitControlValue);
                if (p.AllowColorTempControl) ResetParam(ParamTemp, p.InitialTempControlValue);

                var resetController = new AnimatorController { name = "NTLLC Reset FX" };
                resetController.AddLayer(resetLayer);
                Declare(resetController, ParamReset, AnimatorControllerParameterType.Bool, 0f);
                saved.SaveAsset(resetController);
                foreach (var sub in resetObjects)
                    AssetDatabase.AddObjectToAsset(sub, resetController);

                AddMergeAnimator(settings, resetController, settings.WriteDefaults);
            }

            saved.SaveAsset(controller);
            foreach (var sub in subObjects)
                AssetDatabase.AddObjectToAsset(sub, controller);

            AddMergeAnimator(settings, controller, settings.WriteDefaults);

            // ---- 7. MA 参数 ----
            var map = settings.gameObject.AddComponent<ModularAvatarParameters>();
            void AddParam(string name, ParameterSyncType syncType, float defaultValue, bool localOnly = false, bool noSave = false) =>
                map.parameters.Add(new ParameterConfig
                {
                    nameOrPrefix = name,
                    syncType = syncType,
                    defaultValue = defaultValue,
                    saved = p.IsValueSave && !noSave,
                    localOnly = localOnly,
                    internalParameter = true,
                });

            AddParam(ParamEnable, ParameterSyncType.Float, p.IsDefaultUse ? 1f : 0f);
            if (p.IsSeparateLightControl)
            {
                AddParam(ParamLightMin, ParameterSyncType.Float, p.DefaultMinLightValue);
                AddParam(ParamLightMax, ParameterSyncType.Float, p.DefaultMaxLightValue);
            }
            else
            {
                AddParam(ParamLight, ParameterSyncType.Float, p.DefaultLightValue);
            }
            if (p.AllowValueControl) AddParam(ParamValue, ParameterSyncType.Float, p.InitialValueControlValue);
            if (p.AllowHueControl) AddParam(ParamHue, ParameterSyncType.Float, p.InitialHueControlValue);
            if (p.AllowGammaControl) AddParam(ParamGamma, ParameterSyncType.Float, p.InitialGammaControlValue);
            if (p.AllowSaturationControl) AddParam(ParamSaturation, ParameterSyncType.Float, p.InitialSaturationControlValue);
            if (p.AllowMonochromeControl) AddParam(ParamMono, ParameterSyncType.Float, p.InitialMonochromeControlValue);
            if (p.AllowUnlitControl) AddParam(ParamUnlit, ParameterSyncType.Float, p.InitialUnlitControlValue);
            if (p.AllowColorTempControl) AddParam(ParamTemp, ParameterSyncType.Float, p.InitialTempControlValue);
            if (p.AddResetButton) AddParam(ParamReset, ParameterSyncType.Bool, 0f, localOnly: true, noSave: true);
            if (presets.Count > 0) AddParam(ParamPreset, ParameterSyncType.Int, 0f, localOnly: true, noSave: true);
            // Direct Blend Tree 恒定权重用的哑参数
            AddParam(DirectDummyParam, ParameterSyncType.NotSynced, 1f, localOnly: true, noSave: true);

            // ---- 8. MA 菜单 ----
            BuildMenu(context, settings, p, presets);

            // ---- 9. 网格设置对齐（LLC v2：生成 MA Mesh Settings 统一 Anchor 与 Bounds）----
            if (settings.AlignMeshSettings && root.GetComponent<ModularAvatarMeshSettings>() == null)
            {
                var ms = root.AddComponent<ModularAvatarMeshSettings>();
                ms.InheritProbeAnchor = ModularAvatarMeshSettings.InheritMode.Set;
                ms.ProbeAnchor = new AvatarObjectReference();
                ms.ProbeAnchor.Set(root);
                ms.InheritBounds = ModularAvatarMeshSettings.InheritMode.Set;
                ms.RootBone = new AvatarObjectReference();
                ms.RootBone.Set(root);
                ms.Bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(3.5f, 3.5f, 3.5f));
            }
        }

        private void AddMergeAnimator(NonToonLightLimitChanger settings, RuntimeAnimatorController controller, WriteDefaultsSetting wd)
        {
            var merge = settings.gameObject.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            merge.matchAvatarWriteDefaults = wd == WriteDefaultsSetting.MatchAvatar;
            merge.deleteAttachedAnimator = false;
        }

        private List<TargetRenderer> CollectTargets(BuildContext context, GameObject root, NonToonLightLimitChanger settings)
        {
            var result = new List<TargetRenderer>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
                if (renderer.gameObject.CompareTag("EditorOnly")) continue;
                if (RendererExcluded(root, settings, renderer)) continue;

                Material first = null;
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null || !IsNonToon(mat) || IsExcluded(settings, mat)) continue;
                    first = mat;
                    break;
                }
                if (first == null) continue;

                result.Add(new TargetRenderer
                {
                    Renderer = renderer,
                    Path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform),
                    Type = renderer.GetType(),
                    OriginalMat = first,
                    Mat = first,
                });
            }
            return result;
        }

        private static bool IsNonToon(Material mat) =>
            mat != null && mat.shader != null &&
            mat.shader.name.IndexOf("nontoon", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool RendererExcluded(GameObject root, NonToonLightLimitChanger settings, Renderer renderer)
        {
            if (settings.Excludes.Contains(renderer)) return true;
            for (var t = renderer.transform; t != null && t.gameObject != root; t = t.parent)
            {
                if (settings.Excludes.Contains(t.gameObject)) return true;
            }
            return false;
        }

        private static bool IsExcluded(NonToonLightLimitChanger settings, Object obj) =>
            obj != null && settings.Excludes.Contains(obj);

        private static float SafeGet(Material mat, string prop, float fallback) =>
            mat != null && mat.HasProperty(prop) ? mat.GetFloat(prop) : fallback;

        // ---- 菜单 ----
        // 与 LLC v1 相同：生成 VRCExpressionsMenu 资产 + ModularAvatarMenuInstaller.menuToAppend。
        // 不用 ModularAvatarMenuItem（1.18 起走 PortableControl 序列化，运行时赋 Control 字段不会同步）。
        // 注意：VRC SDK 3.10 把嵌套类型 ControlParameter/SubParameter 改成了 Parameter，
        // 为了同时兼容新旧 SDK（3.9 / 3.10+），这里用反射构造，不写死嵌套类型名。

        private static object NewControlParameter(string name, float value)
        {
            var type = typeof(VRCExpressionsMenu.Control).GetNestedType("Parameter")
                       ?? typeof(VRCExpressionsMenu.Control).GetNestedType("ControlParameter");
            var obj = System.Activator.CreateInstance(type);
            type.GetField("name")?.SetValue(obj, name);
            type.GetField("value")?.SetValue(obj, value);
            return obj;
        }

        private static void SetControlParameter(VRCExpressionsMenu.Control control, string name, float value)
        {
            var obj = NewControlParameter(name, value);
            typeof(VRCExpressionsMenu.Control).GetField("parameter")?.SetValue(control, obj);
            // 激活值：SDK 3.10 在 Control.value（Parameter 只有 name）；旧 SDK 的 Parameter.value 上面已尝试写入。
            // 两个都写，各自 null-safe。
            typeof(VRCExpressionsMenu.Control).GetField("value")?.SetValue(control, value);
        }

        private static void SetControlSubParameter(VRCExpressionsMenu.Control control, string name)
        {
            // SDK 3.10+ 的 subParameters 是 Parameter[]（默认 null），旧 SDK 是 List<SubParameter>
            var field = typeof(VRCExpressionsMenu.Control).GetField("subParameters");
            var elemType = typeof(VRCExpressionsMenu.Control).GetNestedType("Parameter")
                           ?? typeof(VRCExpressionsMenu.Control).GetNestedType("SubParameter");
            var item = NewControlParameter(name, 0f);

            var fieldType = field.FieldType;
            var existing = field.GetValue(control);
            if (typeof(Array).IsAssignableFrom(fieldType))
            {
                var arr = existing as Array;
                var len = arr == null ? 0 : arr.Length;
                var newArr = Array.CreateInstance(elemType, len + 1);
                if (arr != null) Array.Copy(arr, newArr, len);
                newArr.SetValue(item, len);
                field.SetValue(control, newArr);
            }
            else
            {
                if (existing == null) existing = System.Activator.CreateInstance(fieldType);
                existing.GetType().GetMethod("Add")?.Invoke(existing, new[] { item });
                field.SetValue(control, existing);
            }
        }

        private static VRCExpressionsMenu NewMenu()
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls = new List<VRCExpressionsMenu.Control>();
            return menu;
        }

        private static VRCExpressionsMenu.Control AddControl(VRCExpressionsMenu menu, VRCExpressionsMenu.Control.ControlType type, string label)
        {
            var control = new VRCExpressionsMenu.Control { type = type, name = label };
            menu.controls.Add(control);
            return control;
        }

        private static void BuildMenu(BuildContext context, NonToonLightLimitChanger settings, NTLLCParameters p, List<NTLLCPreset> presets)
        {
            var rootMenu = NewMenu();

            var enable = AddControl(rootMenu, VRCExpressionsMenu.Control.ControlType.Toggle, "启用");
            SetControlParameter(enable, ParamEnable, 1f);

            if (p.IsSeparateLightControl)
            {
                SetControlSubParameter(AddControl(rootMenu, VRCExpressionsMenu.Control.ControlType.RadialPuppet, "最暗"), ParamLightMin);
                SetControlSubParameter(AddControl(rootMenu, VRCExpressionsMenu.Control.ControlType.RadialPuppet, "最亮"), ParamLightMax);
            }
            else
            {
                SetControlSubParameter(AddControl(rootMenu, VRCExpressionsMenu.Control.ControlType.RadialPuppet, "亮度"), ParamLight);
            }

            var anyAdditional = p.AllowValueControl || p.AllowHueControl || p.AllowGammaControl ||
                                p.AllowSaturationControl || p.AllowMonochromeControl ||
                                p.AllowUnlitControl || p.AllowColorTempControl;

            void AdditionalControls(VRCExpressionsMenu menu)
            {
                if (p.AllowValueControl) SetControlSubParameter(AddControl(menu, VRCExpressionsMenu.Control.ControlType.RadialPuppet, "明度"), ParamValue);
                if (p.AllowHueControl) SetControlSubParameter(AddControl(menu, VRCExpressionsMenu.Control.ControlType.RadialPuppet, "色相"), ParamHue);
                if (p.AllowGammaControl) SetControlSubParameter(AddControl(menu, VRCExpressionsMenu.Control.ControlType.RadialPuppet, "伽马"), ParamGamma);
                if (p.AllowSaturationControl) SetControlSubParameter(AddControl(menu, VRCExpressionsMenu.Control.ControlType.RadialPuppet, "饱和度"), ParamSaturation);
                if (p.AllowMonochromeControl) SetControlSubParameter(AddControl(menu, VRCExpressionsMenu.Control.ControlType.RadialPuppet, "灯光单色化"), ParamMono);
                if (p.AllowUnlitControl) SetControlSubParameter(AddControl(menu, VRCExpressionsMenu.Control.ControlType.RadialPuppet, "无光照"), ParamUnlit);
                if (p.AllowColorTempControl) SetControlSubParameter(AddControl(menu, VRCExpressionsMenu.Control.ControlType.RadialPuppet, "色温"), ParamTemp);
            }

            if (p.IsGroupingAdditionalControls && anyAdditional)
            {
                var subMenu = NewMenu();
                AdditionalControls(subMenu);
                context.AssetSaver.SaveAsset(subMenu);
                var subControl = AddControl(rootMenu, VRCExpressionsMenu.Control.ControlType.SubMenu, "更多控制");
                subControl.subMenu = subMenu;
            }
            else if (anyAdditional)
            {
                AdditionalControls(rootMenu);
            }

            if (presets.Count > 0)
            {
                var presetMenu = NewMenu();
                for (var i = 0; i < presets.Count; i++)
                {
                    var button = AddControl(presetMenu, VRCExpressionsMenu.Control.ControlType.Button, presets[i].Name);
                    SetControlParameter(button, ParamPreset, i + 1);
                }
                context.AssetSaver.SaveAsset(presetMenu);
                var presetControl = AddControl(rootMenu, VRCExpressionsMenu.Control.ControlType.SubMenu, "预设");
                presetControl.subMenu = presetMenu;
            }

            if (p.AddResetButton)
            {
                var reset = AddControl(rootMenu, VRCExpressionsMenu.Control.ControlType.Button, "重置");
                SetControlParameter(reset, ParamReset, 1f);
            }

            context.AssetSaver.SaveAsset(rootMenu);
            var installer = settings.gameObject.AddComponent<ModularAvatarMenuInstaller>();
            installer.menuToAppend = rootMenu;

            // ---- 安装位置 ----
            // MA 规则：installer 无 MenuInstallTarget 接收时默认装到表情菜单根；
            // 被 MenuInstallTarget（internal 组件，字段 installer）接收后内容内联到该物体所在的菜单树位置。
            switch (p.MenuInstallMode)
            {
                case NTLLCMenuMode.Root:
                    // 强制根：不生成任何锚点
                    break;

                case NTLLCMenuMode.CustomPath:
                {
                    // 在组件物体下自动生成页面链：组件/段0/段1/.../锚点
                    // 链根的 MenuInstaller(menuToAppend=null + 自身 MenuItem=Children) 把整条链装进表情菜单根，
                    // 末级下的锚点接收本 installer，控件即出现在最后一级页面里。
                    var segments = (p.MenuCustomPath ?? "")
                        .Split('/')
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0)
                        .Take(4)
                        .ToList();
                    if (segments.Count == 0) segments.Add("NonToon LLC");

                    Transform parent = settings.transform;
                    foreach (var seg in segments)
                    {
                        var folderObj = new GameObject(seg);
                        folderObj.transform.SetParent(parent, false);
                        var mi = folderObj.AddComponent<ModularAvatarMenuItem>();
                        mi.MenuSource = SubmenuSource.Children;
                        mi.Control = new VRCExpressionsMenu.Control
                        {
                            type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                            name = seg,
                        };
                        if (parent == settings.transform)
                        {
                            // 链根负责把整条链装进表情菜单根
                            var chainInstaller = folderObj.AddComponent<ModularAvatarMenuInstaller>();
                            chainInstaller.menuToAppend = null;
                        }
                        parent = folderObj.transform;
                    }

                    var customTargetType = FindType("nadena.dev.modular_avatar.core.ModularAvatarMenuInstallTarget");
                    if (customTargetType != null)
                    {
                        var anchor = new GameObject("NTLLC Install Target");
                        anchor.transform.SetParent(parent, false);
                        var target = anchor.AddComponent(customTargetType);
                        customTargetType.GetField("installer")?.SetValue(target, installer);
                    }
                    else
                    {
                        Debug.LogWarning("[NTLLC] 找不到 ModularAvatarMenuInstallTarget，菜单将安装到表情菜单根。");
                    }
                    break;
                }

                default: // Auto
                {
                    var folder = FindMenuFolderAncestor(settings.transform, context.AvatarRootTransform.gameObject);
                    if (folder != null)
                    {
                        var targetType = FindType("nadena.dev.modular_avatar.core.ModularAvatarMenuInstallTarget");
                        if (targetType != null)
                        {
                            var anchor = new GameObject("NTLLC Install Target");
                            anchor.transform.SetParent(folder, false);
                            var target = anchor.AddComponent(targetType);
                            targetType.GetField("installer")?.SetValue(target, installer);
                        }
                        else
                        {
                            Debug.LogWarning("[NTLLC] 找不到 ModularAvatarMenuInstallTarget，菜单将安装到表情菜单根。");
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>从组件物体向上找最近的 ModularAvatarMenuItem 文件夹祖先（不含自身）。</summary>
        private static Transform FindMenuFolderAncestor(Transform start, GameObject root)
        {
            for (var t = start.parent; t != null && t.gameObject != root; t = t.parent)
            {
                if (t.GetComponent<ModularAvatarMenuItem>() != null) return t;
            }
            return null;
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
