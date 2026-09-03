using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.Templates;
using AILevelGenerator.Runtime.Utilities;
using AILevelGenerator.Runtime.Validation;
using UnityEditor;
using UnityEngine;

namespace AILevelGenerator.Editor.Tools
{
    /// <summary>
    /// 双模板混合联调测试（第五周-Day6 验收工具）：10 个固定场景 = 战斗关卡模板（linear/defense/open_world）
    /// × 收集任务模板（collect）同关混合，走**生产同源链路**（ServiceLocator 的 IGenerator = 注册的真实
    /// LLMGenerator，含两级缓存 + 模板依赖哈希），每场景先真实 API 生成、再同参重放验证缓存命中与确定性。
    /// 每场景五道生产门（与调度器同口径）：
    ///   ① 请求级 Pre 校验（registry.Run 与调度器同一实例）② 生成 Success（Errors=0）
    ///   ③ 数据级 Pre 校验（资源存在性/数值边界/模板范围 —— 通过才算成功，否则生产链路会拦截）
    ///   ④ 混合证据恒等式：敌人 ≥ 关卡模板 MinEnemyCount（模板兜底保证）；含 Collect 任务 → 收集物 ≥
    ///      Collect_TaskTemplate.MinCollectibleCount（关卡数量池下限保证）
    ///   ⑤ 同参重放 = 缓存命中（零额外 API）+ 数据指纹与新鲜生成完全一致（确定性重放）
    /// 先跑静态预检（零成本）：组合兼容分析 = 各关卡模板「双兜底下限占用」vs MaxPropCount，
    /// 超限组合即使 LLM 零产出也会被范围校验拦截（提示调高 MaxPropCount 或调低任务模板下限）。
    /// 验收线：数据门通过 ≥ 9/10、收集任务命中 ≥ 9/10、重放一致 10/10、真实 API 调用 = 10。
    /// 离线等价回归见 MixedTemplatePipelineTests（fake 客户端，无需网络，可反复跑）。
    /// </summary>
    public static class MixedTemplateIntegrationRunner
    {
        private const string Title = "双模板混合联调";
        private static volatile bool _running; // 防重复触发（10+10 次调用耗时长，双保险）

        /// <summary> 固定场景：战斗关卡模板 TemplateId × 收集任务描述的混合关卡（种子 5001+ 避开历史缓存键） </summary>
        private static readonly Scenario[] Scenarios =
        {
            new("linear", "线性沙漠古堡关卡：从出生点沿唯一主路推进到终点 Boss。主线战斗任务（约 4~6 名敌人驻守检查点与关键节点，含 1 名近战队长），" +
                "1 个收集支线任务（金币散落在城墙根地面），另 1 个抵达支线任务（到达终点 Boss 前）。共 3~4 个任务，其中恰好 1 个主线。", 5001),
            new("linear", "线性雪原村道关卡：出生点沿雪径单向推进。主线战斗任务（约 5 名敌人伏击，近战为主，1 名精英殿后），" +
                "收集支线任务（生命药水散落在路边雪地），第 3 个任务为抵达村庄终点的支线。共 3 个任务、恰好 1 个主线，道具沿主路两侧分布。", 5002),
            new("linear", "线性矿洞关卡：主线战斗任务（矿洞守卫近战与弓箭手约 5 名），收集支线任务 2 个（金币散落在矿车轨道旁、" +
                "生命药水散落在通风口附近），再加 1 个抵达支线。共 4 个任务、恰好 1 个主线。", 5003),
            new("defense", "塔防防守关卡：核心基地位于敌人行进路线终点，敌人沿固定路线波次进攻。主线防守任务（守住基地 3 波，带生存时长约束），" +
                "1 个收集支线任务（金币散落在基地外围地面），另有 1 个击败精英敌人的支线。共 3~4 个任务、恰好 1 个主线。" +
                "敌人 4~6 名沿路径含弓箭手；防守位沿路径两侧交错排布。", 5004),
            new("defense", "冰原塔防关卡：基地为据点，冰面通道为唯一进攻路线。主线防守任务（击退 4 波进攻，含 1 名精英压阵），" +
                "1 个收集支线任务（生命药水散落在通道两侧雪地），另有 1 个收集金币的小支线。共 3~4 个任务、恰好 1 个主线。", 5005),
            new("defense", "峡谷塔防关卡：基地背靠崖壁，敌人沿峡谷主路进攻。主线防守任务（守住 2 波攻势），" +
                "收集支线任务 2 个（金币散落在峡谷入口、生命药水散落在基地外墙附近），另带 1 个击退首领的支线。共 4 个任务、恰好 1 个主线。", 5006),
            new("open_world", "开放世界草原关卡：出生点位于地图中心，自由探索无强制顺序。主线战斗任务（讨伐 2 处敌人营地，每处 2~3 名敌人），" +
                "1 个收集支线任务（金币散落在营地与资源点周围地面），1 个探索支线（抵达湖心岛），可再补充 1 个护送或采集支线。" +
                "共 4~6 个任务、恰好 1 个主线；营地间距不小于 15 米，资源点与营地数量大致相当。", 5007),
            new("open_world", "开放世界荒漠绿洲关卡：出生点居中，绿洲与废墟散布四周。主线战斗任务（清剿废墟里的远程弓箭手营地 2~3 名与近战守卫 2 名），" +
                "1 个收集支线任务（生命药水散落在绿洲水边），探索支线 2 个（抵达废弃水井、抵达北侧峡谷）。共 5~6 个任务、恰好 1 个主线。", 5008),
            new("open_world", "开放世界丛林关卡：主线战斗任务（讨伐 3 处野兽营地，含 1 名精英首领），" +
                "收集支线任务 2 个（金币散落在树屋周围、生命药水散落在溪流边），另带 1 个抵达树屋的探索支线。共 5~6 个任务、恰好 1 个主线。", 5009),
            new("open_world", "开放世界雪山关卡：出生点居中，自由探索。主线战斗任务（攻破 2 处雪地哨站，各 3 名敌人，含弓箭手与精英），" +
                "收集支线任务 2 个（金币散落在冰湖边、生命药水散落在山道补给点），探索支线 2 个（抵达山顶祭坛、抵达冻湖洞穴）。共 6~7 个任务、恰好 1 个主线。", 5010)
        };

        [MenuItem("Tools/AI Level Generator Tests/运行双模板混合联调测试（10 次真实 API）")]
        public static async void RunMixedIntegration()
        {
            if (_running)
            {
                Debug.LogWarning($"[{Title}] 已有测试运行中，忽略本次触发");
                return;
            }
            _running = true;
            try
            {
                await RunAsync();
            }
            finally
            {
                _running = false;
            }
        }

        private static async Task RunAsync()
        {
            var generator = ServiceLocator.Get<IGenerator>();
            var manager = ServiceLocator.Get<ITemplateManager>();
            var registry = ServiceLocator.Get<ValidatorRegistry>();
            if (generator == null || manager == null || registry == null)
            {
                Debug.LogError($"[{Title}] 核心服务未就绪（IGenerator/ITemplateManager/ValidatorRegistry），请检查 GeneratorServiceInitializer");
                return;
            }

            // —— 静态预检（零成本，不调 API） ——
            var collectTemplate = manager.GetTaskTemplates()?.FirstOrDefault(t => t != null && t.TaskType == TaskType.Collect) as ConfigurableTaskTemplate;
            Debug.Log($"[{Title}] 开始：{Scenarios.Length} 场景 × 2 次（新鲜生成 + 缓存重放）" +
                $" | 收集任务模板：{collectTemplate?.DisplayName ?? "未配置（收集兜底关闭，恒等式降级为提示）"}");
            PreflightCombos(manager, collectTemplate);

            var passCount = 0;        // 数据门 + 混合证据全过
            var collectHit = 0;       // LLM 正确产出 Collect 任务
            var replayOk = 0;         // 重放一致性
            var apiCalls = 0;         // 新鲜调用计数（重放应命中缓存零调用，接近 0 由指纹一致性佐证）
            var failReasons = new List<string>();

            for (var i = 0; i < Scenarios.Length; i++)
            {
                var scenario = Scenarios[i];
                var label = $"场景{i + 1}/{Scenarios.Length}";
                Debug.Log("----------------------------------------");
                Debug.Log($"[{Title}] {label}：模板 {scenario.TemplateId} | 种子 {scenario.Seed} | {Truncate(scenario.Prompt, 36)}");

                // ① 请求级前置校验（与调度器同一注册表同一阶段）
                var request = new GenerationRequest
                {
                    Prompt = scenario.Prompt,
                    TemplateId = scenario.TemplateId,
                    RandomSeed = scenario.Seed,
                    GenerateTerrain = true,
                    GenerateProps = true,
                    GenerateTasks = true
                };
                var requestPre = registry.Run(ValidationStage.Pre, request, scenario.TemplateId);
                if (!requestPre.IsValid)
                {
                    failReasons.Add($"{label} 请求级校验失败：{JoinErrors(requestPre)}");
                    continue;
                }

                // ② 新鲜生成（真实 API；唯一随机种子 + 独有文案 → 必 miss，计一次 API）
                GenerationResult fresh;
                GenerationResult replay = null;
                try
                {
                    fresh = await generator.GenerateAsync(request);
                    apiCalls++;
                    if (!fresh.Success || fresh.LevelData == null)
                    {
                        failReasons.Add($"{label} 生成失败：{JoinErrors(fresh)}");
                        continue;
                    }

                    // ③ 数据级前置校验（生产链路同口径：不过 = 调度器会拦截，场景判失败）
                    var dataPre = registry.Run(ValidationStage.Pre, fresh.LevelData, scenario.TemplateId);
                    if (!dataPre.IsValid)
                    {
                        failReasons.Add($"{label} 数据级校验失败：{JoinErrors(dataPre)}");
                        continue;
                    }

                    // ④ 混合证据恒等式（模板兜底是确定性保证，不达标即框架缺陷）
                    var evidence = EvaluateEvidence(fresh.LevelData, scenario.TemplateId, manager, collectTemplate);
                    if (!string.IsNullOrEmpty(evidence.Fail))
                    {
                        failReasons.Add($"{label} 混合证据未达标：{evidence.Fail}");
                        continue;
                    }
                    if (evidence.CollectTaskHit) collectHit++;

                    // ⑤ 同参重放：命中两级缓存（零 API）+ 确定性收尾 → 指纹与新鲜结果完全一致
                    replay = await generator.GenerateAsync(request);
                    var replayTime = replay?.GenerationTime ?? 0f;
                    var freshTime = fresh.GenerationTime;
                    if (replay == null || !replay.Success)
                    {
                        failReasons.Add($"{label} 重放失败（不应发生）：{(replay == null ? "结果为空" : JoinErrors(replay))}");
                        continue;
                    }
                    var same = Fingerprint(fresh.LevelData) == Fingerprint(replay.LevelData);
                    if (!same)
                    {
                        failReasons.Add($"{label} 重放结果与新鲜生成不一致（确定性收尾或缓存键异常）");
                        continue;
                    }
                    replayOk++;
                    if (replayTime >= freshTime * 0.5f)
                        Debug.LogWarning($"[{Title}] {label}：重放耗时 {replayTime:F2}s 接近新鲜 {freshTime:F2}s，疑似未命中缓存（仍以指纹一致为准）");

                    passCount++;
                    Debug.Log($"[{Title}] {label} ✓：LLM {freshTime:F2}s / 重放 {replayTime:F3}s | 道具 {CountProps(fresh.LevelData)}" +
                        $"（敌人 {evidence.EnemyCount} / 收集物 {evidence.CollectibleCount}）| 任务 {CountTasks(fresh.LevelData)} | " +
                        $"警告 {fresh.Warnings?.Count ?? 0} 项 | 收集任务命中：{(evidence.CollectTaskHit ? "是" : "否")}");
                }
                catch (Exception ex)
                {
                    failReasons.Add($"{label} 异常：{ex.Message}");
                    Debug.LogError($"[{Title}] {label} 异常：{ex}");
                }
            }

            // —— 汇总与判定 ——
            Debug.Log("========================================");
            Debug.Log($"[{Title}] 完成：数据门通过 {passCount}/{Scenarios.Length} | 收集任务命中 {collectHit}/{Scenarios.Length}" +
                $" | 重放一致 {replayOk}/{Scenarios.Length} | 真实 API 调用 {apiCalls}");
            foreach (var reason in failReasons)
                Debug.LogWarning($"[{Title}] 未达标明细：{reason}");
            var verdict = passCount >= 9 && replayOk == Scenarios.Length && collectHit >= 9
                ? $"达标 ✓（数据门 ≥9/10 + 收集命中 ≥9/10 + 重放一致 {Scenarios.Length}/{Scenarios.Length}）"
                : $"未达标 ✗（目标：数据门 ≥9/10、收集命中 ≥9/10、重放一致 {Scenarios.Length}/{Scenarios.Length}），请按明细定位修复后重跑";
            Debug.Log($"[{Title}] 验收结果：{verdict}");
        }

        /// <summary> 静态组合预检：每个关卡模板的「兜底下限占用」vs MaxPropCount，输出兼容行与超限预警 </summary>
        private static void PreflightCombos(ITemplateManager manager, ConfigurableTaskTemplate collectTemplate)
        {
            var levelTemplates = manager.GetLevelTemplates();
            if (levelTemplates == null || levelTemplates.Count == 0) return;
            var collectMin = collectTemplate?.MinCollectibleCount ?? 0;
            Debug.Log($"[{Title}] 组合兼容预检（战斗兜底 + 收集兜底 {collectMin} 的确定性占用 vs 模板上限，收集兜底需场景含 Collect 任务才生效）：");
            foreach (var t in levelTemplates)
            {
                if (t == null) continue;
                var configurable = t as ConfigurableLevelTemplate;
                var enemyMin = configurable != null && configurable.EnemyOptions != null && configurable.EnemyOptions.Count > 0
                    ? configurable.MinEnemyCount
                    : 0;
                var floor = enemyMin + collectMin;
                var max = configurable?.MaxPropCount ?? 0;
                if (max > 0 && floor > max)
                    Debug.LogWarning($"[{Title}]   {t.TemplateId}（{t.DisplayName}）：兜底下限占用 {floor} > MaxPropCount {max}" +
                        $" —— 该组合无论 LLM 产出多少都会被范围校验拦截，请调高 MaxPropCount 或调低任务模板下限");
                else
                    Debug.Log($"[{Title}]   {t.TemplateId}（{t.DisplayName}）：敌人兜底 {enemyMin} + 收集兜底 {collectMin} = {floor}" +
                        $"（MaxPropCount {max}）→ 可混用余量 {(max > 0 ? max - floor : "不限")}");
            }
        }

        /// <summary> 混合证据评估：返回兜底恒等式校验结果与命中统计（只读模板配置，不修改数据） </summary>
        private static EvidenceResult EvaluateEvidence(LevelData level, string templateId, ITemplateManager manager,
            ConfigurableTaskTemplate collectTemplate)
        {
            var evidence = new EvidenceResult();
            if (level?.Props == null) { evidence.Fail = "LevelData 无 Props"; return evidence; }

            var levelTemplate = manager.GetTemplateById(templateId) as ConfigurableLevelTemplate;
            var enemyNames = levelTemplate?.EnemyOptions?.Where(o => o != null && !string.IsNullOrEmpty(o.LogicalName))
                .Select(o => o.LogicalName).ToList() ?? new List<string>();
            var collectibleNames = collectTemplate?.CollectibleOptions?.Where(o => o != null && !string.IsNullOrEmpty(o.LogicalName))
                .Select(o => o.LogicalName).ToList() ?? new List<string>();

            evidence.EnemyCount = CountByName(level.Props, enemyNames);
            evidence.CollectibleCount = CountByName(level.Props, collectibleNames);
            evidence.CollectTaskHit = level.Tasks?.Any(t => t != null && t.Type == TaskType.Collect) ?? false;

            // 战斗兜底恒等式：EnemyOptions 开启且 MinEnemyCount>0 → 收尾后敌人必然 ≥ 下限（LLM 不足即确定性补齐）
            if (enemyNames.Count > 0 && levelTemplate != null && levelTemplate.MinEnemyCount > 0
                && evidence.EnemyCount < levelTemplate.MinEnemyCount)
                evidence.Fail = $"敌人 {evidence.EnemyCount} < 关卡模板下限 {levelTemplate.MinEnemyCount}（战斗兜底未生效？）";

            // 收集兜底恒等式：场景含 Collect 任务且收集模板开启 → 收尾后收集物必然 ≥ 下限（关卡数量池）
            if (evidence.Fail == null && evidence.CollectTaskHit && collectTemplate != null && collectibleNames.Count > 0
                && collectTemplate.MinCollectibleCount > 0 && evidence.CollectibleCount < collectTemplate.MinCollectibleCount)
                evidence.Fail = $"收集物 {evidence.CollectibleCount} < 收集模板下限 {collectTemplate.MinCollectibleCount}（收集兜底未生效？）";
            return evidence;
        }

        private static int CountByName(List<PropPlacement> props, List<string> names)
        {
            var count = 0;
            if (props == null || names == null || names.Count == 0) return count;
            foreach (var p in props)
                if (p != null && names.Contains(p.PrefabLogicalName))
                    count++;
            return count;
        }

        /// <summary> 数据指纹：LevelData 全字段确定序列化（缓存重放应逐字符一致） </summary>
        private static string Fingerprint(LevelData level)
        {
            var sb = new StringBuilder();
            sb.Append(level.LevelName).Append('|').Append(level.Description).Append('|');
            sb.Append(F(level.PlayerStartPosition)).Append('|');
            if (level.Terrain != null)
                sb.Append(level.Terrain.Width).Append('x').Append(level.Terrain.Length).Append('x').Append(level.Terrain.HeightScale);
            sb.Append('|');
            foreach (var p in level.Props)
            {
                if (p == null) { sb.Append("<null-prop>|"); continue; }
                sb.Append(p.PrefabLogicalName).Append('@').Append(F(p.Position)).Append('!').Append(F(p.Rotation))
                    .Append('!').Append(F(p.Scale)).Append('#');
                if (p.PatrolPoints != null)
                    foreach (var point in p.PatrolPoints)
                        sb.Append(F(point)).Append('&');
                sb.Append('|');
            }
            sb.Append('T');
            foreach (var task in level.Tasks)
            {
                if (task == null) { sb.Append("<null-task>|"); continue; }
                sb.Append(task.TaskID).Append('|').Append(task.TaskName).Append('|').Append(task.Description).Append('|')
                    .Append(task.Type).Append('|').Append(task.Objective).Append('|').Append(task.IsMainTask).Append('|')
                    .Append(task.TimeLimit).Append('|').Append(task.TriggerCondition).Append('|');
                if (task.Reward != null)
                    sb.Append(task.Reward.Experience).Append('+').Append(task.Reward.Gold).Append('+')
                        .Append(task.Reward.ItemRewards != null ? string.Join(",", task.Reward.ItemRewards) : "");
                sb.Append(';');
            }
            return sb.ToString();
        }

        private static string F(Vector3 v)
            => v.x.ToString("R", CultureInfo.InvariantCulture) + "," + v.y.ToString("R", CultureInfo.InvariantCulture) + "," + v.z.ToString("R", CultureInfo.InvariantCulture);

        private static string JoinErrors(GenerationResult result)
            => result?.Errors != null && result.Errors.Count > 0
                ? string.Join("；", result.Errors.Select(e => $"{e.Code}：{e.Message}"))
                : "无错误条目";

        private static string JoinErrors(ValidationResult result)
            => result?.Errors != null && result.Errors.Count > 0
                ? string.Join("；", result.Errors.Select(e => $"{e.Code}：{e.Message}（{e.DataPath}）"))
                : "无错误条目";

        private static int CountProps(LevelData level) => level?.Props?.Count ?? 0;
        private static int CountTasks(LevelData level) => level?.Tasks?.Count ?? 0;

        /// <summary> 长文案日志截断（Console 可读性） </summary>
        private static string Truncate(string text, int max)
            => string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text.Substring(0, max) + "…";

        private sealed class Scenario
        {
            public readonly string TemplateId;
            public readonly string Prompt;
            public readonly int Seed;

            public Scenario(string templateId, string prompt, int seed)
            {
                TemplateId = templateId;
                Prompt = prompt;
                Seed = seed;
            }
        }

        private sealed class EvidenceResult
        {
            public int EnemyCount;
            public int CollectibleCount;
            public bool CollectTaskHit;
            public string Fail;
        }
    }
}
