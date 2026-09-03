#if UNITY_EDITOR
using System.Globalization;
using AILevelGenerator.Runtime.Interfaces.Templates;
using UnityEditor;

namespace AILevelGenerator.Runtime.LLM
{
    /// <summary>
    /// Editor 资产模板依赖哈希实现（第五周-Day5）：AssetDatabase.GetAssetDependencyHash 直接取模板资产
    /// （含其全部依赖：脚本、引用资产）的哈希 —— 策划修改模板资产任意字段/脚本 → 哈希自动变化 →
    /// 缓存键变化 → 旧条目自动失效（比手动算字符串哈希更准更省心，需求指定实现）。
    /// 代码新建模板无资产路径 → 返回 0（键退化为不含哈希组件）。
    /// 整文件 #if UNITY_EDITOR（AssetDatabase 为 Editor API，先例见 TemplateAssetSource）。
    /// </summary>
    public class AssetTemplateDependencyHashProvider : ITemplateDependencyHashProvider
    {
        public ulong GetDependencyHash(LevelTemplate template)
        {
            if (template == null) return 0;
            var path = AssetDatabase.GetAssetPath(template);
            if (string.IsNullOrEmpty(path)) return 0; // 非资产模板（代码 CreateInstance）

            var hash = AssetDatabase.GetAssetDependencyHash(path);
            // Hash128 无公开数值转换：取其 32 位十六进制串前 16 位解析为 64 位值（确定性、跨进程稳定）
            if (!hash.isValid) return 0;
            var hex = hash.ToString();
            return hex.Length >= 16 && ulong.TryParse(hex.Substring(0, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }
    }
}
#endif
