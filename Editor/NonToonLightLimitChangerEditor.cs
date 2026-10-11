using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Kuro.NTLLC.Editor
{
    /// <summary>Inspector：与 Light Limit Changer v2 相近的分区布局（精简版）。</summary>
    [CustomEditor(typeof(NonToonLightLimitChanger))]
    [CanEditMultipleObjects]
    internal sealed class NonToonLightLimitChangerEditor : UnityEditor.Editor
    {
        private bool _foldoutOptions;
        private bool _foldoutPresets;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "挂到 Avatar 下任意物体。上传时自动生成 NonToon 亮度控制的表情菜单滑条。\n" +
                "材质面板里若没有出现「NonToon LLC」参数组，先执行 Tools → NonToon Light Limit Changer → 重新注册亮度模块。",
                MessageType.Info);

            var parameters = serializedObject.FindProperty(nameof(NonToonLightLimitChanger.Parameters));

            // ---- 常规 ----
            EditorGUILayout.LabelField("常规", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.IsDefaultUse)),
                new GUIContent("默认启用", "进房时菜单开关处于打开状态"));
            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.IsValueSave)),
                new GUIContent("跨世界保存", "参数加入 saved，换世界保留当前值"));
            EditorGUILayout.Space(6);

            // ---- 亮度 ----
            EditorGUILayout.LabelField("亮度", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.IsSeparateLightControl)),
                new GUIContent("合并为单滑条", "关闭（默认）= v2 风格「最暗」「最亮」两个独立滑条；开启 = v1 风格单滑条（min=max 同扫）"));
            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.MinLightValue)),
                new GUIContent("滑条范围·下端", "滑条拉到最左时材质的亮度值（0 = 允许全黑）"));
            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.MaxLightValue)),
                new GUIContent("滑条范围·上端", "滑条拉到最右时材质的亮度值（1 = 全亮；Built-in 下超过 1 无额外效果）"));

            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.DefaultMinLightValue)),
                new GUIContent("最暗初始位置"));
            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.DefaultMaxLightValue)),
                new GUIContent("最亮初始位置"));
            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.DefaultLightValue)),
                new GUIContent("单滑条初始位置（单滑条模式用）"));

            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.OverwriteDefaultLightMinMax)),
                new GUIContent("覆盖材质默认亮度", "关闭时读取各材质当前的 Min/Max 作为菜单关闭状态的默认值"));
            EditorGUILayout.Space(6);

            // ---- 颜色 / 其他控制 ----
            EditorGUILayout.LabelField("附加控制", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.AllowValueControl)),
                new GUIContent("明度", "整体亮度倍数 0~2，1 = 不改变"));
            if (PropertyEnabled(parameters, nameof(NTLLCParameters.AllowValueControl)))
                EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.InitialValueControlValue)),
                    new GUIContent("明度初始位置"));

            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.AllowHueControl)),
                new GUIContent("色相", "±180° 绕灰轴旋转，0 = 不改变"));
            if (PropertyEnabled(parameters, nameof(NTLLCParameters.AllowHueControl)))
                EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.InitialHueControlValue)),
                    new GUIContent("色相初始位置"));

            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.AllowGammaControl)),
                new GUIContent("伽马", "0~2，<1 提亮中间调，>1 压暗中间调"));
            if (PropertyEnabled(parameters, nameof(NTLLCParameters.AllowGammaControl)))
                EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.InitialGammaControlValue)),
                    new GUIContent("伽马初始位置"));

            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.AllowSaturationControl)),
                new GUIContent("饱和度", "0=无彩色 1=原样 2=加深"));
            if (PropertyEnabled(parameters, nameof(NTLLCParameters.AllowSaturationControl)))
                EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.InitialSaturationControlValue)),
                    new GUIContent("饱和度初始位置"));

            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.AllowMonochromeControl)),
                new GUIContent("灯光单色化", "削弱彩色灯光的影响（对灯光色去饱和，与 LLC v2 的ライトのモノクロ化同义）"));
            if (PropertyEnabled(parameters, nameof(NTLLCParameters.AllowMonochromeControl)))
                EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.InitialMonochromeControlValue)),
                    new GUIContent("单色化初始位置"));

            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.AllowUnlitControl)),
                new GUIContent("无光照", "拉满后完全不响应光照（按全亮渲染）"));
            if (PropertyEnabled(parameters, nameof(NTLLCParameters.AllowUnlitControl)))
                EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.InitialUnlitControlValue)),
                    new GUIContent("无光照初始位置"));

            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.AllowColorTempControl)),
                new GUIContent("色温", "0=偏暖 0.5=原样 1=偏冷"));
            if (PropertyEnabled(parameters, nameof(NTLLCParameters.AllowColorTempControl)))
                EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.InitialTempControlValue)),
                    new GUIContent("色温初始位置"));

            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.IsGroupingAdditionalControls)),
                new GUIContent("附加控制收进子菜单"));
            EditorGUILayout.PropertyField(parameters.FindPropertyRelative(nameof(NTLLCParameters.AddResetButton)),
                new GUIContent("添加重置按钮"));
            EditorGUILayout.Space(6);

            // ---- 预设 ----
            EditorGUILayout.LabelField("预设", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("每个预设 = 菜单里的一个按钮，按下后把所有已启用的滑条一键切到预设值（值为滑条位置 0-1）。", MessageType.None);
            _foldoutPresets = EditorGUILayout.Foldout(_foldoutPresets, "预设列表（" + serializedObject.FindProperty(nameof(NonToonLightLimitChanger.Presets)).arraySize + "）");
            if (_foldoutPresets)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty(nameof(NonToonLightLimitChanger.Presets)),
                        new GUIContent("预设"), true);
                }
            }
            EditorGUILayout.Space(6);

            // ---- 选项 ----
            _foldoutOptions = EditorGUILayout.Foldout(_foldoutOptions, "选项");
            if (_foldoutOptions)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(
                        parameters.FindPropertyRelative(nameof(NTLLCParameters.MenuInstallMode)),
                        new GUIContent("菜单插入位置",
                            "自动：组件物体放在 MA 菜单文件夹（带 Modular Avatar Menu Item 的物体）下时装进该页面，否则装到表情菜单根\n" +
                            "表情菜单根：始终装到根\n" +
                            "自定义路径：按下方路径自动生成页面链（挂在组件物体下）"));
                    if (parameters.FindPropertyRelative(nameof(NTLLCParameters.MenuInstallMode)).enumValueIndex == (int)NTLLCMenuMode.CustomPath)
                        EditorGUILayout.PropertyField(
                            parameters.FindPropertyRelative(nameof(NTLLCParameters.MenuCustomPath)),
                            new GUIContent("页面路径", "用 / 分层，最多 4 级，如 插件/NTLLC"));
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty(nameof(NonToonLightLimitChanger.WriteDefaults)),
                        new GUIContent("Write Defaults", "Match Avatar：交给 Modular Avatar 判断"));
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty(nameof(NonToonLightLimitChanger.AlignMeshSettings)),
                        new GUIContent("统一网格设置", "Avatar 根上没有 MA Mesh Settings 时生成一个（Probe Anchor 与 Bounds 统一指向根，LLC v2 的网格设置对齐）；已有则不动"));
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty(nameof(NonToonLightLimitChanger.Excludes)),
                        new GUIContent("排除列表", "列入的物体 / 渲染器 / 材质不受滑条控制"));
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("NonToon 没有自发光与逆光强度属性，相应控制项不提供。", EditorStyles.miniLabel);

            serializedObject.ApplyModifiedProperties();
        }

        private static bool PropertyEnabled(SerializedProperty parameters, string allowName)
        {
            var allow = parameters.FindPropertyRelative(allowName);
            return allow == null || allow.boolValue;
        }
    }

    internal static class NllcSetupMenu
    {
        /// <summary>给选中的 Avatar 添加 NonToon Light Limit Changer 组件。</summary>
        [MenuItem("GameObject/NonToon Light Limit Changer", false, 49)]
        private static void Setup()
        {
            var avatar = FindAvatar(Selection.activeGameObject);
            if (avatar == null) return;

            var go = new GameObject("NonToon Light Limit Changer", typeof(NonToonLightLimitChanger));
            Undo.RegisterCreatedObjectUndo(go, "Add NonToon Light Limit Changer");
            go.transform.SetParent(avatar.transform, false);
            Selection.activeGameObject = go;
        }

        [MenuItem("GameObject/NonToon Light Limit Changer", true, 49)]
        private static bool ValidateSetup() => FindAvatar(Selection.activeGameObject) != null;

        private static VRCAvatarDescriptor FindAvatar(GameObject go)
        {
            while (go != null)
            {
                var descriptor = go.GetComponent<VRCAvatarDescriptor>();
                if (descriptor != null) return descriptor;
                go = go.transform.parent?.gameObject;
            }
            return null;
        }
    }
}
