using System;
using System.Collections.Generic;
using System.Linq;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Utilities;
using UnityEditor;
using UnityEngine;

namespace AILevelGenerator.Editor.Tools
{
    /// <summary>
    /// 准确率测试工具（Day5 验收）：随机 20 次真实 LLM 生成，统计 Success 率（验收线 ≥90%）。
    /// 经 ServiceLocator 获取真实生成器（LLMGenerator 全链路：Prompt → API → 容错解析 → 校验），
    /// 结果输出到 Console（独立于窗口日志面板）。
    /// 说明：真实调用 1-2 分钟；async void 在 UnitySynchronizationContext 上 await，无死锁。
    /// </summary>
    public static class AccuracyTestRunner
    {
        private const int TestCount = 20;
        private const int SeedBase = 20260829; // 固定种子保证可复现

        /// <summary> 预设描述池：覆盖不同规模、风格与难度（与资源映射表内逻辑名对齐） </summary>
        private static readonly string[] PromptPool =
        {
            "小型森林营地：1 个巡逻弓箭手，1 个宝箱，任务为抵达篝火",
            "中型沙漠要塞：4 个弓箭手守卫，2 个宝箱，主线为击败首领，支线为收集 3 份补给",
            "雪山村庄：2 个村民 NPC，1 个宝箱藏在房屋后，任务为护送商人过桥",
            "地下迷宫入口：2 个敌人巡逻，1 个宝箱，任务为击杀 5 只蝙蝠",
            "湖畔营地：1 个弓箭手，任务为守卫营地 60 秒，奖励 500 经验",
            "废弃矿洞：3 个宝箱，2 个巡逻弓箭手，任务为收集矿晶 3 个"
        };

        // 菜单路径注意：不得挂在 "Tools/AI Level Generator"（窗口叶子项）之下——Unity 菜单系统
        // 同路径"叶子项 + 子菜单"并存时，子菜单优先，叶子项会被吞掉（点击与 ExecuteMenuItem 均失效，
        // 表现为窗口入口消失，Day5 线上发现的坑）。因此测试项独立为兄弟菜单，与窗口项互不冲突。
        [MenuItem("Tools/AI Level Generator Tests/运行准确率测试（20 次）")]
        public static async void RunAccuracyTest()
        {
            var generator = ServiceLocator.Get<IGenerator>();
            if (generator == null)
            {
                Debug.LogError("[准确率测试] 生成器未注册（ServiceLocator），请检查 GeneratorServiceInitializer");
                return;
            }

            var templates = ServiceLocator.Get<ITemplateProvider>()?.GetLevelTemplates();
            var templateList = templates?.Where(t => t != null).ToList();
            if (templateList == null || templateList.Count == 0)
            {
                Debug.LogError("[准确率测试] 未加载到任何关卡模板，无法测试");
                return;
            }

            Debug.Log($"[准确率测试] 开始：{TestCount} 次真实 LLM 生成（模板池 {templateList.Count} 个 × 描述池 {PromptPool.Length} 条）");
            var rng = new System.Random(SeedBase);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var success = 0;
            var failures = new List<string>();
            var warningsTotal = 0;

            for (var i = 0; i < TestCount; i++)
            {
                var tpl = templateList[rng.Next(templateList.Count)];
                var prompt = PromptPool[rng.Next(PromptPool.Length)];
                var seed = rng.Next(0, 99999);

                Debug.Log($"[准确率测试] [{i + 1}/{TestCount}] 模板={tpl.DisplayName ?? tpl.TemplateId} | 描述={prompt} | 种子={seed}");

                var result = await generator.GenerateAsync(new GenerationRequest
                {
                    Prompt = prompt,
                    TemplateId = tpl.TemplateId,
                    RandomSeed = seed
                });

                if (result.Success)
                {
                    success++;
                    warningsTotal += result.Warnings?.Count ?? 0;
                    Debug.Log($"[准确率测试]   ✓ 成功：{result.LevelData?.LevelName}（道具 {result.LevelData?.Props.Count ?? 0}，任务 {result.Tasks?.Count ?? 0}，耗时 {result.GenerationTime:F1}s）");
                }
                else
                {
                    var errors = result.Errors != null && result.Errors.Count > 0
                        ? string.Join("；", result.Errors.Select(e => e.Message))
                        : "无错误信息";
                    failures.Add($"[{i + 1}] {prompt}（模板 {tpl.DisplayName ?? tpl.TemplateId}，种子 {seed}）→ {errors}");
                    Debug.LogError($"[准确率测试]   ✗ 失败：{errors}");
                }
            }

            sw.Stop();
            var rate = TestCount > 0 ? (double)success / TestCount * 100.0 : 0.0;
            var avgTime = TestCount > 0 ? sw.Elapsed.TotalSeconds / TestCount : 0.0;

            Debug.Log("========================================");
            Debug.Log($"[准确率测试] 完成：{success}/{TestCount} 成功（{rate:F0}%），平均耗时 {avgTime:F1}s/次，警告总数 {warningsTotal}");
            Debug.Log(rate >= 90.0
                ? "[准确率测试] 验收结果：达标（≥90%）✓"
                : "[准确率测试] 验收结果：未达标（<90%），建议调整 Prompt 模板后重试");
            if (failures.Count > 0)
            {
                Debug.LogError($"[准确率测试] 失败样本 {failures.Count} 条：\n" + string.Join("\n", failures));
            }
        }
    }
}
