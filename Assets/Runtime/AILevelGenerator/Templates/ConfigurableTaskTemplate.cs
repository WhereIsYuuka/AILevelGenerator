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
    /// 数据驱动任务模板：兜底语义 —— 仅当 TaskData 字段为空/默认值时写入模板默认值，不覆盖 LLM 已产出的内容。
    /// 任务类型（TaskType）与模板基础信息来自基类字段；新任务模板 = 复制资产改配置，无需写代码。
    /// 第五周-Day3 起增加「收集扩展」：CollectibleOptions 开启后，本模板覆盖的收集任务（TaskType=Collect）
    /// 会把散落收集物（金币/道具，逻辑名命中映射表）确定性补齐进关卡 Props（数量下限/环形散布范围/
    /// 最小间距/地形边距均可配置）。列表为空 = 功能关闭（既有 Kill 等资产零影响）。
    /// </summary>
    [CreateAssetMenu(fileName = "TaskTemplate", menuName = "AI Level Generator/任务模板（数据驱动）")]
    public class ConfigurableTaskTemplate : TaskTemplate
    {
        [Header("默认值（仅兜底，不覆盖生成结果）")]
        [Tooltip("默认时限（秒），-1 = 无时限；仅当生成结果未设置时限（TimeLimit<=0）时生效")]
        [Min(-1)] public float DefaultTimeLimit = -1f;
        [Tooltip("默认奖励（Experience/Gold 全 0 且无物品奖励时视为未配置，不覆盖生成结果）")]
        public RewardData DefaultReward;
        [Tooltip("默认触发条件文本（如 进入区域/击败敌人），仅当生成结果未填写时生效")]
        [TextArea] public string DefaultTriggerCondition;

        // —— Day3 收集扩展：收集物散布兜底 ——
        // 职责：收集任务（LLM 产出 Type=Collect 的任务）的场景内容兜底 —— 把"散落在地形上的收集物"
        // 确定性补齐到数量下限。收集物本体为关卡实体（PropPlacement），与敌人兜底一致追加进 LevelData.Props，
        // 场景构建层负责地面贴合（模板只产出 y=0 水平散点 + 边界夹取，见 LevelTemplate Day2 边界说明）。

        [Header("收集扩展（Day3 收集物兜底）")]
        [Tooltip("收集物清单与兜底选型权重（逻辑名需命中资源映射表）。列表为空 = 关闭收集兜底，LLM 结果原样保留")]
        public List<CollectibleTypeOption> CollectibleOptions = new();
        [Tooltip("收集物数量下限：本关卡中命中 CollectibleOptions 的收集物少于该值时按权重确定性补齐（0 = 不补齐）")]
        [Min(0)] public int MinCollectibleCount;
        [Tooltip("兜底收集物落点环形半径下限（相对玩家出生点水平散布）")]
        [Min(0.1f)] public float CollectSpawnRingMin = 6f;
        [Tooltip("兜底收集物落点环形半径上限")]
        [Min(0.1f)] public float CollectSpawnRingMax = 30f;
        [Tooltip("兜底收集物最小间距（与全量既有实体水平间距；尝试若干候选不满足则放宽，保证数量必然达成）")]
        [Min(0.1f)] public float CollectMinSpacing = 2.5f;
        [Tooltip("兜底落点相对地形边界的保留边距（贴合边界防穿出地形）")]
        [Min(0f)] public float CollectBoundsMargin = 3f;

        /// <summary> 子流盐名：确定性子流标签，禁止改动/删除/调整抽取顺序（向后确定性契约，见 TaskTemplate 注释） </summary>
        private const string SaltCollectType = "Collect.Type";
        private const string SaltCollectPosition = "Collect.Position";
        private const string SaltCollectRotation = "Collect.Rotation";
        private const int PlacementAttempts = 24; // 间距拒绝采样尝试上限（保证数量必然达成）

        /// <summary>
        /// 应用默认值到 TaskData：逐字段兜底 —— 空/默认值才写入，已设置的内容保留。
        /// 判空规则：TimeLimit&lt;=0 视为未设置；Reward 为 null 或全 0 且物品列表为空视为未设置。
        /// </summary>
        public override void ApplyDefaults(TaskData taskData)
        {
            if (taskData == null) return;

            if (string.IsNullOrEmpty(taskData.TaskName))
                taskData.TaskName = DisplayName;
            if (string.IsNullOrEmpty(taskData.Description))
                taskData.Description = Description;
            if (taskData.TimeLimit <= 0f)
                taskData.TimeLimit = DefaultTimeLimit;
            if (taskData.Reward == null || IsRewardEmpty(taskData.Reward))
                taskData.Reward = DefaultReward;
            if (string.IsNullOrEmpty(taskData.TriggerCondition))
                taskData.TriggerCondition = DefaultTriggerCondition;
        }

        /// <summary>
        /// 覆写统一确定性钩子：收集物数量兜底。CollectibleOptions 为空 = 功能关闭（既有模板资产零影响）。
        /// levelData 为 null（仅默认值路径）或关卡 Props 缺失时静默跳过。
        /// 子流抽取顺序即契约：类型/位置/旋转三个子流由任务随机流派生一次后按索引锁步消费，
        /// 新增随机逻辑只能在末尾追加新子流，禁止在中间插入/删除，否则破坏同种子序列的向后确定性。
        /// </summary>
        protected override void PostGenerate(TaskData taskData, LevelData levelData, DeterministicRandom rng)
        {
            if (levelData == null || levelData.Props == null) return;
            if (CollectibleOptions == null || CollectibleOptions.Count == 0) return;

            // 固定顺序抽取三个子流：类型选型/落点（半径+角度）/朝向旋转（即使某用途当前未消耗也照常派生）
            var typeRng = CollectSubStream(rng, SaltCollectType);
            var positionRng = CollectSubStream(rng, SaltCollectPosition);
            var rotationRng = CollectSubStream(rng, SaltCollectRotation);

            var missing = MinCollectibleCount - CountCollectibleProps(levelData.Props);
            if (missing <= 0) return; // LLM 已达标：只提示不裁剪（数量越界由校验器负责）

            var center = levelData.PlayerStartPosition;
            center.y = 0f;
            var minRadius = Mathf.Min(CollectSpawnRingMin, CollectSpawnRingMax); // 配置倒挂时自动纠正，保持确定性
            var maxRadius = Mathf.Max(CollectSpawnRingMin, CollectSpawnRingMax);

            for (var i = 0; i < missing; i++)
            {
                var name = PickCollectibleType(typeRng);
                var candidate = center;
                for (var attempt = 0; attempt < PlacementAttempts; attempt++)
                {
                    var radius = positionRng.Range(minRadius, maxRadius);
                    var rad = positionRng.Range(0f, 360f) * Mathf.Deg2Rad;
                    candidate = ClampToTerrain(levelData,
                        center + new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius));
                    if (IsSpacingOk(candidate, levelData.Props)) break; // 满足最小间距即接受
                }

                levelData.Props.Add(new PropPlacement
                {
                    PrefabLogicalName = name,
                    Position = candidate,
                    Rotation = new Vector3(0f, rotationRng.RotationY(), 0f)
                });
            }
        }

        /// <summary> 由父流抽取一次盐值派生独立子流（派生结果与抽取次数均确定性） </summary>
        private static DeterministicRandom CollectSubStream(DeterministicRandom parent, string salt)
            => new DeterministicRandom(RandomSeedUtility.Derive(parent.Range(1, int.MaxValue), RandomSeedUtility.StableHash(salt)));

        /// <summary> 收集逻辑名命中 CollectibleOptions 的既有收集物数量（含 LLM 产出与已补位） </summary>
        private int CountCollectibleProps(List<PropPlacement> props)
        {
            var count = 0;
            if (props == null) return count;
            foreach (var prop in props)
                if (prop != null && IsCollectibleName(prop.PrefabLogicalName))
                    count++;
            return count;
        }

        /// <summary> 收集逻辑名是否命中收集物清单 </summary>
        private bool IsCollectibleName(string logicalName)
        {
            if (CollectibleOptions == null) return false;
            foreach (var option in CollectibleOptions)
                if (option != null && option.LogicalName == logicalName)
                    return true;
            return false;
        }

        /// <summary> 按权重从收集物清单选型（配置含非法权重时回退首个有效项，容错不抛） </summary>
        private string PickCollectibleType(DeterministicRandom rng)
        {
            var names = new List<string>();
            var weights = new List<float>();
            foreach (var option in CollectibleOptions)
            {
                if (option == null || string.IsNullOrEmpty(option.LogicalName)) continue;
                if (option.Weight <= 0f) continue; // 非法权重跳过（ValidateSelf 已拦截，此处双保险）
                names.Add(option.LogicalName);
                weights.Add(option.Weight);
            }
            return names.Count > 0 ? rng.WeightedChoice(names, weights) : CollectibleOptions[0].LogicalName;
        }

        /// <summary> 候选点与全量既有实体水平距离是否满足最小间距（收集物不与任何实体叠放） </summary>
        private bool IsSpacingOk(Vector3 candidate, List<PropPlacement> props)
        {
            if (CollectMinSpacing <= 0f) return true;
            var minSq = CollectMinSpacing * CollectMinSpacing;
            for (var i = 0; i < props.Count; i++)
            {
                var pos = props[i].Position;
                var dx = candidate.x - pos.x;
                var dz = candidate.z - pos.z;
                if (dx * dx + dz * dz < minSq) return false;
            }
            return true;
        }

        /// <summary> 夹取到地形矩形边界内（保留 CollectBoundsMargin 边距；地形为空/过小时跳过夹取） </summary>
        private Vector3 ClampToTerrain(LevelData data, Vector3 position)
        {
            var terrain = data?.Terrain;
            if (terrain == null || terrain.Width <= 0 || terrain.Length <= 0) return position;
            var margin = Mathf.Max(0f, CollectBoundsMargin);
            var halfX = Mathf.Max(0f, terrain.Width / 2f - margin);
            var halfZ = Mathf.Max(0f, terrain.Length / 2f - margin);
            position.x = Mathf.Clamp(position.x, -halfX, halfX);
            position.z = Mathf.Clamp(position.z, -halfZ, halfZ);
            return position;
        }

        /// <summary> 奖励是否为空（Experience/Gold 全 0 且物品列表为空） </summary>
        private static bool IsRewardEmpty(RewardData reward)
        {
            if (reward == null) return true;
            return reward.Experience == 0 && reward.Gold == 0
                   && (reward.ItemRewards == null || reward.ItemRewards.Count == 0);
        }

        /// <summary> 自校验：继承基类 TemplateId 检查 + 收集扩展配置合法性（收集物清单是收集兜底的总开关） </summary>
        public override bool ValidateSelf(out string error)
        {
            if (!base.ValidateSelf(out error)) return false;

            var collectOptionsReady = CollectibleOptions != null && CollectibleOptions.Count > 0;
            if (collectOptionsReady)
            {
                for (var i = 0; i < CollectibleOptions.Count; i++)
                {
                    var option = CollectibleOptions[i];
                    if (option == null || string.IsNullOrWhiteSpace(option.LogicalName))
                    {
                        error = $"CollectibleOptions[{i}] 逻辑名为空（收集兜底选型需要有效收集物逻辑名）";
                        return false;
                    }
                    if (option.Weight <= 0f)
                    {
                        error = $"CollectibleOptions[{i}]「{option.LogicalName}」权重必须大于 0（当前 {option.Weight}）";
                        return false;
                    }
                }
                if (CollectSpawnRingMax < CollectSpawnRingMin)
                {
                    error = $"收集落点环形半径倒挂：CollectSpawnRingMax({CollectSpawnRingMax}) 小于 CollectSpawnRingMin({CollectSpawnRingMin})";
                    return false;
                }
            }
            else if (MinCollectibleCount > 0)
            {
                error = "已配置 MinCollectibleCount，但 CollectibleOptions 为空（收集兜底需先配置收集物清单）";
                return false;
            }
            return true;
        }
    }

    /// <summary> 收集物选型条目（Day3 收集扩展）：逻辑名（资源映射表 Key）+ 兜底补齐时的选型权重 </summary>
    [System.Serializable]
    public class CollectibleTypeOption
    {
        [Tooltip("收集物逻辑名（映射表 Key），如 金币/道具-生命药水")]
        public string LogicalName;
        [Tooltip("兜底选型权重（>0），数值越大越常被选中")]
        public float Weight = 1f;
    }
}
