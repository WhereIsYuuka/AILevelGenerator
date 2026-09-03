using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.Utilities;
using UnityEngine;
// 数据层 TerrainData 与 UnityEngine.TerrainData 同名，别名消除歧义（见 CLAUDE.md 已知坑）
using TerrainData = AILevelGenerator.Runtime.Data.TerrainData;

namespace AILevelGenerator.Runtime.Templates
{
    /// <summary>
    /// 数据驱动关卡模板：策划复制资产改配置即可新建模板，无需写代码。
    /// 模板只描述"规则"（指南文本 + 地形默认值 + 规模约束），不写死关卡内容（内容由 LLM 产出）。
    /// 四类内置模板（线性闯关/开放世界/塔防防守/谜题收集）均为此类资产实例。
    /// </summary>
    [CreateAssetMenu(fileName = "LevelTemplate", menuName = "AI Level Generator/关卡模板（数据驱动）")]
    public class ConfigurableLevelTemplate : LevelTemplate
    {
        [Tooltip("模板指南：本模板的布局规则，随 Prompt 注入 {templateGuideline} 告知 LLM。文案避免使用半角花括号，用「」代替")]
        [TextArea(3, 10)]
        public string Guideline;

        [Header("地形默认值")]
        [Tooltip("为 true 时用下方地形默认值覆盖生成结果；false 时仅在生成结果无地形时兜底")]
        public bool OverrideTerrain = true;
        [Min(1)] public int TerrainWidth = 100;
        [Min(1)] public int TerrainLength = 100;
        [Min(0)] public float TerrainHeightScale = 10f;

        [Header("规模约束（0 = 不限制）")]
        [Min(0)] public int MinPropCount;
        [Min(0)] public int MaxPropCount;
        [Min(0)] public int MinTaskCount;
        [Min(0)] public int MaxTaskCount;
        [Tooltip("是否必须存在主线任务（IsMainTask=true）")]
        public bool ForceMainTask = true;

        [Header("战斗扩展（Day2 敌人兜底）")]
        [Tooltip("敌人清单与兜底选型权重（逻辑名需命中资源映射表）。列表为空 = 关闭战斗兜底，LLM 结果原样保留（既有模板资产零影响）")]
        public List<EnemyTypeOption> EnemyOptions = new();
        [Tooltip("敌人数量下限：LLM 产出的敌人（逻辑名命中 EnemyOptions）少于该值 → 按权重确定性补齐（0 = 不补齐）")]
        [Min(0)] public int MinEnemyCount;
        [Tooltip("巡逻点兜底：为巡逻点为空的敌人物体按固定扇区+随机半径补齐巡逻点（0 = 不补齐；LLM 已输出的巡逻点不覆盖）")]
        [Min(0)] public int PatrolPointsPerEnemy;
        [Tooltip("默认巡逻半径下限（仅 PatrolPointsPerEnemy>0 时生效）")]
        [Min(0.1f)] public float PatrolRadiusMin = 3f;
        [Tooltip("默认巡逻半径上限")]
        [Min(0.1f)] public float PatrolRadiusMax = 8f;
        [Tooltip("兜底敌人落点环形半径下限（相对玩家出生点，环形内随机散布）")]
        [Min(0.1f)] public float EnemySpawnRingMin = 8f;
        [Tooltip("兜底敌人落点环形半径上限")]
        [Min(0.1f)] public float EnemySpawnRingMax = 25f;
        [Tooltip("兜底敌人最小间距（尝试若干候选不满足则放宽，保证数量必然达成）")]
        [Min(0.1f)] public float EnemyMinSpacing = 6f;
        [Tooltip("兜底落点/巡逻点相对地形边界的保留边距（贴合边界防穿出地形）")]
        [Min(0f)] public float BoundsMargin = 3f;

        /// <summary> 覆写基类：返回模板指南（PromptBuilder 只依赖基类方法，保持多态） </summary>
        public override string GetGuideline() => Guideline ?? string.Empty;

        /// <summary>
        /// 应用默认值到 LevelData：地形为空时创建并填默认值；OverrideTerrain 时覆盖已有地形。
        /// 只补默认值不裁剪规模（数量越界由校验器负责，ApplyDefaults 保持单一职责）。
        /// </summary>
        public override void ApplyDefaults(LevelData data)
        {
            if (data == null) return;
            if (data.Terrain == null)
            {
                data.Terrain = new TerrainData
                {
                    Width = TerrainWidth,
                    Length = TerrainLength,
                    HeightScale = TerrainHeightScale
                };
            }
            else if (OverrideTerrain)
            {
                data.Terrain.Width = TerrainWidth;
                data.Terrain.Length = TerrainLength;
                data.Terrain.HeightScale = TerrainHeightScale;
            }
        }

        // —— Day2 战斗扩展：PostGenerate 确定性兜底 ——
        // 职责：敌人数量/巡逻点未命中 Schema（LLM 产出不足或缺失）时的确定性补齐。
        // 敌人本体仍由 LLM 生成（props + 逻辑名），模板只兜"欠数"与"缺巡逻点"，不覆盖 LLM 已产出的内容。

        /// <summary> 子流盐名：确定性子流标签，禁止改动/删除/调整抽取顺序（向后确定性契约，见 LevelTemplate 注释） </summary>
        private const string SaltEnemySpawn = "Battle.EnemySpawn";
        private const string SaltPatrolFill = "Battle.PatrolFill";
        private const int PlacementAttempts = 24; // 间距拒绝采样尝试上限（保证数量必然达成）

        /// <summary>
        /// 覆写统一确定性钩子：执行战斗兜底。敌人清单为空 = 功能关闭（既有模板资产零影响）。
        /// 子流抽取顺序即契约：每个随机用途从传入 rng 派生独立子流（只消耗一次父流抽取），
        /// 新增随机逻辑只能在末尾追加新子流，禁止在中间插入/删除，否则破坏同种子序列的向后确定性。
        /// </summary>
        protected override void PostGenerate(LevelData data, DeterministicRandom rng)
        {
            if (data == null || data.Props == null) return;
            if (EnemyOptions == null || EnemyOptions.Count == 0) return;

            // 固定顺序抽取：先敌人落点流、后巡逻点流（即使某功能未启用也照常派生，保证流序稳定）
            FallbackEnemyCount(data, BattleSubStream(rng, SaltEnemySpawn));
            FallbackPatrolPoints(data, BattleSubStream(rng, SaltPatrolFill));
        }

        /// <summary> 由父流抽取一次盐值派生独立子流（派生结果与抽取次数均确定性） </summary>
        private static DeterministicRandom BattleSubStream(DeterministicRandom parent, string salt)
            => new DeterministicRandom(RandomSeedUtility.Derive(parent.Range(1, int.MaxValue), RandomSeedUtility.StableHash(salt)));

        /// <summary>
        /// 敌人数量兜底：LLM 敌人 < MinEnemyCount → 按 EnemyOptions 权重确定性补齐。
        /// 落点：围绕玩家出生点的环形（[EnemySpawnRingMin, Max] 半径）随机散布 + 与既有敌人保持最小间距
        /// （拒绝采样尝试 PlacementAttempts 次，失败接受末个候选 —— 数量优先于间距）。
        /// 落点与选型均走确定性子流：相同配置 + 相同种子 → 结果完全一致。
        /// </summary>
        private void FallbackEnemyCount(LevelData data, DeterministicRandom rng)
        {
            if (MinEnemyCount <= 0) return;

            var enemies = CollectEnemyProps(data);
            var missing = MinEnemyCount - enemies.Count;
            if (missing <= 0) return; // LLM 已达标：只提示不裁剪（数量越界由校验器负责）

            var center = data.PlayerStartPosition;
            center.y = 0f;
            var minRadius = Mathf.Min(EnemySpawnRingMin, EnemySpawnRingMax); // 配置倒挂时自动纠正，保持确定性
            var maxRadius = Mathf.Max(EnemySpawnRingMin, EnemySpawnRingMax);

            for (var i = 0; i < missing; i++)
            {
                var name = PickEnemyName(rng);
                var candidate = center;
                for (var attempt = 0; attempt < PlacementAttempts; attempt++)
                {
                    var radius = rng.Range(minRadius, maxRadius);
                    var rad = rng.Range(0f, 360f) * Mathf.Deg2Rad;
                    candidate = ClampToTerrain(data, center + new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius));
                    if (IsSpacingOk(candidate, enemies)) break; // 满足最小间距即接受
                }

                var prop = new PropPlacement { PrefabLogicalName = name, Position = candidate };
                data.Props.Add(prop);
                enemies.Add(prop); // 后续候选须与刚补的敌人保持间距
            }
        }

        /// <summary>
        /// 巡逻点兜底：为"巡逻点为空的敌人物体"补齐 PatrolPointsPerEnemy 个巡逻点。
        /// 生成规则：固定扇区角（2π·i/N）+ 确定性随机半径 ∈ [PatrolRadiusMin, Max]，
        /// 落在自身周边圆周上，整体构成有序环形路径（点列顺序即移动顺序），并夹取在地形边界内。
        /// LLM 已输出巡逻点（Schema 命中）的物体不覆盖。
        /// </summary>
        private void FallbackPatrolPoints(LevelData data, DeterministicRandom rng)
        {
            if (PatrolPointsPerEnemy <= 0) return;

            var minRadius = Mathf.Min(PatrolRadiusMin, PatrolRadiusMax);
            var maxRadius = Mathf.Max(PatrolRadiusMin, PatrolRadiusMax);
            var stepAngle = 360f / PatrolPointsPerEnemy;

            foreach (var prop in data.Props)
            {
                if (prop == null || !IsEnemyName(prop.PrefabLogicalName)) continue;
                if (prop.PatrolPoints != null && prop.PatrolPoints.Count > 0) continue; // 已命中 Schema：不覆盖

                var center = prop.Position;
                center.y = 0f;
                var points = new List<Vector3>(PatrolPointsPerEnemy);
                for (var i = 0; i < PatrolPointsPerEnemy; i++)
                {
                    var rad = i * stepAngle * Mathf.Deg2Rad;
                    var radius = rng.Range(minRadius, maxRadius);
                    var point = center + new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
                    points.Add(ClampToTerrain(data, point));
                }
                prop.PatrolPoints = points;
            }
        }

        /// <summary> 收集逻辑名命中 EnemyOptions 的既有敌人（含 LLM 产出） </summary>
        private List<PropPlacement> CollectEnemyProps(LevelData data)
        {
            var enemies = new List<PropPlacement>();
            foreach (var prop in data.Props)
                if (prop != null && IsEnemyName(prop.PrefabLogicalName))
                    enemies.Add(prop);
            return enemies;
        }

        /// <summary> 逻辑名是否命中敌人清单 </summary>
        private bool IsEnemyName(string logicalName)
        {
            foreach (var option in EnemyOptions)
                if (option != null && option.LogicalName == logicalName)
                    return true;
            return false;
        }

        /// <summary> 按权重从敌人清单选型（配置含非法权重时回退首个有效项，容错不抛） </summary>
        private string PickEnemyName(DeterministicRandom rng)
        {
            var names = new List<string>();
            var weights = new List<float>();
            foreach (var option in EnemyOptions)
            {
                if (option == null || string.IsNullOrEmpty(option.LogicalName)) continue;
                if (option.Weight <= 0f) continue; // 非法权重跳过（ValidateSelf 已拦截，此处双保险）
                names.Add(option.LogicalName);
                weights.Add(option.Weight);
            }
            return names.Count > 0 ? rng.WeightedChoice(names, weights) : EnemyOptions[0].LogicalName;
        }

        /// <summary> 候选点与既有敌人水平距离是否满足最小间距 </summary>
        private bool IsSpacingOk(Vector3 candidate, List<PropPlacement> enemies)
        {
            if (EnemyMinSpacing <= 0f) return true;
            var minSq = EnemyMinSpacing * EnemyMinSpacing;
            for (var i = 0; i < enemies.Count; i++)
            {
                var pos = enemies[i].Position;
                var dx = candidate.x - pos.x;
                var dz = candidate.z - pos.z;
                if (dx * dx + dz * dz < minSq) return false;
            }
            return true;
        }

        /// <summary> 夹取到地形矩形边界内（保留 BoundsMargin 边距；地形为空/过小时跳过夹取） </summary>
        private Vector3 ClampToTerrain(LevelData data, Vector3 position)
        {
            var terrain = data?.Terrain;
            if (terrain == null || terrain.Width <= 0 || terrain.Length <= 0) return position;
            var margin = Mathf.Max(0f, BoundsMargin);
            var halfX = Mathf.Max(0f, terrain.Width / 2f - margin);
            var halfZ = Mathf.Max(0f, terrain.Length / 2f - margin);
            position.x = Mathf.Clamp(position.x, -halfX, halfX);
            position.z = Mathf.Clamp(position.z, -halfZ, halfZ);
            return position;
        }

        /// <summary> 自校验：继承基类 TemplateId 检查 + 数量范围合法性（0 表示不限，Max 非 0 时不得小于 Min） </summary>
        public override bool ValidateSelf(out string error)
        {
            if (!base.ValidateSelf(out error)) return false;
            if (MaxPropCount > 0 && MinPropCount > MaxPropCount)
            {
                error = "道具数量范围倒挂：MinPropCount 大于 MaxPropCount";
                return false;
            }
            if (MaxTaskCount > 0 && MinTaskCount > MaxTaskCount)
            {
                error = "任务数量范围倒挂：MinTaskCount 大于 MaxTaskCount";
                return false;
            }

            // 战斗扩展配置合法性（敌人清单是战斗兜底的总开关）
            var enemyOptionsReady = EnemyOptions != null && EnemyOptions.Count > 0;
            if (enemyOptionsReady)
            {
                for (var i = 0; i < EnemyOptions.Count; i++)
                {
                    var option = EnemyOptions[i];
                    if (option == null || string.IsNullOrWhiteSpace(option.LogicalName))
                    {
                        error = $"EnemyOptions[{i}] 逻辑名为空（战斗兜底选型需要有效敌人逻辑名）";
                        return false;
                    }
                    if (option.Weight <= 0f)
                    {
                        error = $"EnemyOptions[{i}]「{option.LogicalName}」权重必须大于 0（当前 {option.Weight}）";
                        return false;
                    }
                }
                if (PatrolRadiusMax < PatrolRadiusMin)
                {
                    error = $"巡逻半径倒挂：PatrolRadiusMax({PatrolRadiusMax}) 小于 PatrolRadiusMin({PatrolRadiusMin})";
                    return false;
                }
                if (EnemySpawnRingMax < EnemySpawnRingMin)
                {
                    error = $"落点环形半径倒挂：EnemySpawnRingMax({EnemySpawnRingMax}) 小于 EnemySpawnRingMin({EnemySpawnRingMin})";
                    return false;
                }
            }
            else if (MinEnemyCount > 0 || PatrolPointsPerEnemy > 0)
            {
                error = "已配置 MinEnemyCount/PatrolPointsPerEnemy，但 EnemyOptions 为空（战斗兜底需先配置敌人清单）";
                return false;
            }
            return true;
        }
    }

    /// <summary> 敌人选型条目（Day2 战斗扩展）：逻辑名（资源映射表 Key）+ 兜底补齐时的选型权重 </summary>
    [System.Serializable]
    public class EnemyTypeOption
    {
        [Tooltip("敌人逻辑名（映射表 Key），如 敌人-近战/敌人-弓箭手/敌人-精英")]
        public string LogicalName;
        [Tooltip("兜底选型权重（>0），数值越大越常被选中")]
        public float Weight = 1f;
    }
}
