// 把 NonToon LLC 亮度模块登记进 Shader Core 的模块白名单。
//
// Shader Core 按「shader 白名单」编译模块：SCShaderImporter 只编译
// ProjectSettings/jp.lilxyzw.shadercore.asset 里登记过的模块。外挂包里的模块
// 不在 NonToon 的 shader 目录下，不会被自动发现，所以必须自己登记。
//
// 反射访问 Shader Core 内部设置的方式改编自 ATRI_NAIXU 的 NonToonLightLimit（MIT）：
//   1. 调 Shader Core 自己的 ProjectSettings.GetShaderModules()，按它的规则建立条目；
//   2. 追加本模块 uniqueID 并落盘，然后重导入 NonToon 的 scshader。
// 只追加、不删除，幂等；NonToon 自带模块一个不动。

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Kuro.NTLLC.Editor
{
    [InitializeOnLoad]
    internal static class NllcModuleInstaller
    {
        /// <summary>本模块在 .scmodule 里声明的 uniqueID。</summary>
        internal const string ModuleId = "jp.kuro.nllc";

        private const string SettingsRelativePath = "ProjectSettings/jp.lilxyzw.shadercore.asset";
        private const string OptOutKey = "Kuro.NTLLC.AutoRegisterDisabled";
        private const string SessionKey = "Kuro.NTLLC.RegisterChecked";

        static NllcModuleInstaller()
        {
            EditorApplication.update += WaitForIdle;
        }

        private static void WaitForIdle()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= WaitForIdle;
            AutoRegister();
        }

        private static void AutoRegister()
        {
            if (EditorPrefs.GetBool(OptOutKey, false)) return;
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);

            var pending = FindUnregisteredShaders();
            if (pending.Count == 0) return;

            if (Register())
            {
                Debug.Log("[NonToon LLC] 已把亮度模块登记到 " + string.Join(" / ", pending) + "，NonToon 正在重新导入。\n" +
                          "手动管理：NonToon.scshader 的模块列表里勾选 " + ModuleId +
                          "；关闭自动登记：EditorPrefs 的 " + OptOutKey + " 设为 true。");
            }
            else
            {
                Debug.LogWarning("[NonToon LLC] 自动登记没成功，请执行菜单 Tools/NonToon Light Limit Changer/重新注册亮度模块，" +
                                 "或在 NonToon.scshader 的模块列表里手动勾选 " + ModuleId + "。");
            }
        }

        [MenuItem("Tools/NonToon Light Limit Changer/重新注册亮度模块", false, 10)]
        private static void RegisterFromMenu()
        {
            if (Register()) Debug.Log("[NonToon LLC] 模块列表已更新，NonToon 正在重新导入。");
            else if (FindUnregisteredShaders().Count == 0) Debug.Log("[NonToon LLC] NonToon 已经加载了该模块，无需重复登记。");
            else Debug.LogWarning("[NonToon LLC] 登记失败，请在 NonToon.scshader 的模块列表里手动勾选 " + ModuleId + "。");
        }

        /// <summary>把模块补进 NonToon 的模块列表。真的有改动才返回 true（幂等）。</summary>
        internal static bool Register()
        {
            try
            {
                if (!TryGetSettings(out var instance, out var shaderSettings))
                {
                    Debug.LogWarning("[NonToon LLC] 找不到 Shader Core 的设置对象，请确认已安装 jp.lilxyzw.shadercore。");
                    return false;
                }

                var getModules = GetShaderModulesMethod(instance.GetType());
                var paths = FindNonToonShaderPaths();
                if (paths.Count == 0)
                {
                    Debug.LogWarning("[NonToon LLC] 没有找到 NonToon 的 .scshader，请确认已安装 jp.lilxyzw.nontoon。");
                    return false;
                }

                var changed = false;
                foreach (var path in paths)
                {
                    // 优先走 Shader Core 自己的入口：缺失条目时它会按官方规则建立并落盘
                    var modules = GetModules(getModules, path) ?? FindModules(shaderSettings, ShaderNameOf(path));
                    if (modules == null)
                    {
                        modules = CreateShaderSettings(shaderSettings, path);
                        if (modules == null)
                        {
                            Debug.LogWarning("[NonToon LLC] 无法为 " + path + " 建立 Shader Core 模块设置条目。");
                            continue;
                        }
                        changed = true;
                    }

                    if (!modules.Contains(ModuleId))
                    {
                        modules.Add(ModuleId);
                        changed = true;
                    }
                }

                if (!changed) return false;

                if (!SaveSettings(instance))
                {
                    Debug.LogWarning("[NonToon LLC] Shader Core 的设置没能落盘，本次会话仍然生效；" +
                                     "重启 Unity 后若失效，请在 NonToon.scshader 的模块列表里手动勾选 " + ModuleId + "。");
                }
                ReimportTargetShaders();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[NonToon LLC] 登记失败：" + exception);
                return false;
            }
        }

        /// <summary>哪些目标 shader 还没有登记本模块。</summary>
        internal static List<string> FindUnregisteredShaders()
        {
            var result = new List<string>();
            try
            {
                if (!TryGetSettings(out var instance, out var shaderSettings)) return result;
                var getModules = GetShaderModulesMethod(instance.GetType());

                foreach (var path in FindNonToonShaderPaths())
                {
                    var modules = GetModules(getModules, path) ?? FindModules(shaderSettings, ShaderNameOf(path));
                    if (modules == null || !modules.Contains(ModuleId)) result.Add(path);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[NonToon LLC] 读取 Shader Core 设置失败：" + exception.Message);
            }
            return result;
        }

        // ------------------------------------------------------------------ 基础工具

        /// <summary>目标 shader 的 asset 路径（Packages / Assets 下所有含 nontoon 的 .scshader）。</summary>
        private static List<string> FindNonToonShaderPaths()
        {
            var result = new List<string>();
            foreach (var rootDir in new[] { "Packages", "Assets" })
            {
                if (!Directory.Exists(rootDir)) continue;
                foreach (var path in Directory.GetFiles(rootDir, "*.scshader", SearchOption.AllDirectories))
                {
                    var normalized = path.Replace('\\', '/');
                    if (normalized.IndexOf("nontoon", StringComparison.OrdinalIgnoreCase) >= 0) result.Add(normalized);
                }
            }
            return result;
        }

        /// <summary>复刻 Shader Core 取 shader 名的方式，用于在设置文件里定位条目。</summary>
        private static string ShaderNameOf(string shaderPath)
        {
            try
            {
                foreach (var line in File.ReadLines(shaderPath))
                {
                    var trimmed = line.TrimStart();
                    if (!trimmed.StartsWith("Shader", StringComparison.Ordinal)) continue;
                    var first = trimmed.IndexOf('"');
                    var last = trimmed.LastIndexOf('"');
                    if (first >= 0 && last > first) return trimmed.Substring(first + 1, last - first - 1);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[NonToon LLC] 解析 " + shaderPath + " 的 shader 名失败：" + exception.Message);
            }
            return null;
        }

        /// <summary>兜底：Shader Core 的 GetShaderModules 不存在时（换版本）自己补条目。</summary>
        private static List<string> CreateShaderSettings(IList list, string shaderPath)
        {
            try
            {
                var shaderName = ShaderNameOf(shaderPath);
                if (string.IsNullOrEmpty(shaderName)) return null;

                var listType = list.GetType();
                var entryType = listType.IsGenericType ? listType.GetGenericArguments()[0] : null;
                if (entryType == null) return null;

                var entry = Activator.CreateInstance(entryType);
                var nameField = entryType.GetField("shadername");
                var modulesField = entryType.GetField("modules");
                if (nameField == null || modulesField == null) return null;

                nameField.SetValue(entry, shaderName);
                modulesField.SetValue(entry, new List<string>());

                var multiField = entryType.GetField("multiModules");
                if (multiField != null && multiField.GetValue(entry) == null)
                    multiField.SetValue(entry, Activator.CreateInstance(multiField.FieldType));

                list.Add(entry);
                return modulesField.GetValue(entry) as List<string>;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[NonToon LLC] 建立 Shader Core 设置条目失败：" + exception.Message);
                return null;
            }
        }

        private static void ReimportTargetShaders()
        {
            AssetDatabase.Refresh();
            foreach (var path in FindNonToonShaderPaths())
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        // ------------------------------------------------------------------ 反射访问 Shader Core 的内部设置

        private static bool TryGetSettings(out object instance, out IList shaderSettings)
        {
            instance = null;
            shaderSettings = null;

            var type = FindType("jp.lilxyzw.shadercore.ProjectSettings");
            if (type == null) return false;

            var property = type.GetProperty("instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (property != null) instance = property.GetValue(null, null);
            if (instance == null)
            {
                foreach (var obj in Resources.FindObjectsOfTypeAll(type))
                {
                    if (obj == null) continue;
                    instance = obj;
                    break;
                }
            }
            if (instance == null) return false;

            var field = type.GetField("shaderSettings", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return false;
            shaderSettings = field.GetValue(instance) as IList;
            return shaderSettings != null;
        }

        /// <summary>Shader Core 的 ProjectSettings.GetShaderModules(path, out modules, out multiModules)。</summary>
        private static MethodInfo GetShaderModulesMethod(Type settingsType)
        {
            foreach (var method in settingsType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (method.Name != "GetShaderModules") continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 3 && parameters[0].ParameterType == typeof(string)) return method;
            }
            return null;
        }

        private static List<string> GetModules(MethodInfo getModules, string shaderPath)
        {
            if (getModules == null) return null;
            try
            {
                var arguments = new object[] { shaderPath, null, null };
                getModules.Invoke(null, arguments);
                return arguments[1] as List<string>;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<string> FindModules(IList shaderSettings, string shaderName)
        {
            if (shaderSettings == null || string.IsNullOrEmpty(shaderName)) return null;
            foreach (var entry in shaderSettings)
            {
                if (entry == null) continue;
                var type = entry.GetType();
                var nameField = type.GetField("shadername");
                if (nameField == null || (string)nameField.GetValue(entry) != shaderName) continue;
                return type.GetField("modules")?.GetValue(entry) as List<string>;
            }
            return null;
        }

        /// <summary>调用 Shader Core 自己的 Save()。</summary>
        private static bool SaveSettings(object instance)
        {
            var type = instance.GetType();
            var save = type.GetMethod("Save", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (save != null)
            {
                try { save.Invoke(instance, null); }
                catch (Exception exception)
                {
                    Debug.LogWarning("[NonToon LLC] 调用 Shader Core 的 Save() 失败：" + exception.Message);
                }
            }

            if (File.Exists(SettingsFilePath())) return true;

            if (instance is UnityEngine.Object unityObject)
            {
                EditorUtility.SetDirty(unityObject);
                AssetDatabase.SaveAssets();
                return File.Exists(SettingsFilePath());
            }
            return false;
        }

        private static string SettingsFilePath()
        {
            var root = Directory.GetParent(Application.dataPath);
            return Path.Combine(root?.FullName ?? ".", SettingsRelativePath.Replace('/', Path.DirectorySeparatorChar));
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
