using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Templates;
using AILevelGenerator.Runtime.Utilities;
using NUnit.Framework;
using UnityEngine;
// Runtime.Data.TerrainData 与 UnityEngine.TerrainData 同名 → 本地别名消歧
using TerrainData = AILevelGenerator.Runtime.Data.TerrainData;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 战斗模板兜底单元测试（第五周-Day2）：
    /// ConfigurableLevelTemplate.PostGenerate 的敌人数量/巡逻点确定性兜底 ——
    /// 相同种子+相同输入 → 完全一致；不同种子 → 明显不同；LLM 已命中 Schema 的内容不被覆盖。
    /// 敌人本体仍由 LLM 生成（props + 逻辑名），模板只兜"欠数"与"缺巡逻点"。
    /// </summary>
    public class BattleTemplateTests
    {
        private const string 近战 = "敌人-近战";
        private const string 弓箭手 = "敌人-弓箭手";
        private const string 精英 = "敌人-精英";

        private readonly List<ScriptableObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var so in _created)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _created.Clear();
        }

        // —— 构造辅助 ——

        /// <summary> 基础战斗模板：三敌人等权重 + 数量下限 4 + 每敌人 2 巡逻点（环形 8~25，巡逻半径 3~8，间距 6，边距 3） </summary>
        private ConfigurableLevelTemplate NewBattleTemplate()
        {
            var t = ScriptableObject.CreateInstance<ConfigurableLevelTemplate>();
            _created.Add(t);
            t.TemplateId = "battle_tpl";
            t.OverrideTerrain = false; // 测试用例自行控制地形尺寸
            t.EnemyOptions = new List<EnemyTypeOption>
            {
                new() { LogicalName = 近战, Weight = 1f },
                new() { LogicalName = 弓箭手, Weight = 1f },
                new() { LogicalName = 精英, Weight = 1f }
            };
            t.MinEnemyCount = 4;
            t.PatrolPointsPerEnemy = 2;
            t.PatrolRadiusMin = 3f;
            t.PatrolRadiusMax = 8f;
            t.EnemySpawnRingMin = 8f;
            t.EnemySpawnRingMax = 25f;
            t.EnemyMinSpacing = 6f;
            t.BoundsMargin = 3f;
            return t;
        }

        /// <summary> 120×120 地形 + 出生点在原点 + 可选 LLM 初始敌人/宝箱 </summary>
        private static LevelData NewLevel(params PropPlacement[] props)
        {
            var level = new LevelData
            {
                PlayerStartPosition = Vector3.zero,
                Terrain = new TerrainData { Width = 120, Length = 120, HeightScale = 8f },
                Props = new List<PropPlacement>(props)
            };
            return level;
        }

        private static PropPlacement Enemy(string name, float x, float z) =>
            new() { PrefabLogicalName = name, Position = new Vector3(x, 0f, z) };

        private static PropPlacement 宝箱(float x, float z) => Enemy("宝箱", x, z);

        private static int CountEnemies(LevelData level)
        {
            var count = 0;
            foreach (var prop in level.Props)
                if (prop.PrefabLogicalName == 近战 || prop.PrefabLogicalName == 弓箭手 || prop.PrefabLogicalName == 精英)
                    count++;
            return count;
        }

        /// <summary> 逐字节 JSON 对比（含 Props/巡逻点等全部字段） </summary>
        private static string ToJson(LevelData level) => JsonUtility.ToJson(level);

        // —— 开关语义 ——

        [Test]
        public void 未配置敌人清单_战斗兜底不生效()
        {
            var template = NewBattleTemplate();
            template.EnemyOptions.Clear(); // 总开关关闭
            var level = NewLevel(Enemy(近战, 8f, 4f), 宝箱(-3f, -3f));

            template.FinalizeData(level, 7);

            Assert.AreEqual(2, level.Props.Count, "关闭敌人清单时不得增删任何物体");
            Assert.IsEmpty(level.Props[0].PatrolPoints);
            Assert.IsEmpty(level.Props[1].PatrolPoints);
        }

        [Test]
        public void 数量下限为零_只补巡逻点不补敌人()
        {
            var template = NewBattleTemplate();
            template.MinEnemyCount = 0;
            var level = NewLevel(Enemy(近战, 8f, 4f), 宝箱(0f, 10f));

            template.FinalizeData(level, 7);

            Assert.AreEqual(2, level.Props.Count, "数量下限为 0 时不得补敌人");
            Assert.AreEqual(2, level.Props[0].PatrolPoints.Count, "敌人缺巡逻点应按配置补齐");
            Assert.IsEmpty(level.Props[1].PatrolPoints, "非敌人物体不受巡逻点兜底影响");
        }

        [Test]
        public void 巡逻点数配置为零_不生成任何巡逻点()
        {
            var template = NewBattleTemplate();
            template.PatrolPointsPerEnemy = 0;
            var level = NewLevel(Enemy(近战, 8f, 4f));

            template.FinalizeData(level, 7);

            Assert.AreEqual(4, CountEnemies(level), "巡逻关闭不影响敌人数量兜底");
            foreach (var prop in level.Props)
                Assert.IsEmpty(prop.PatrolPoints);
        }

        // —— 敌人数量兜底 ——

        [Test]
        public void 敌人不足MinEnemyCount_确定性补齐到数量下限()
        {
            var template = NewBattleTemplate();
            var level = NewLevel(Enemy(近战, 8f, 4f), 宝箱(-3f, -3f));

            template.FinalizeData(level, 42);

            Assert.AreEqual(4, CountEnemies(level), "敌人数量应补齐到 MinEnemyCount");
            Assert.AreEqual(1, 宝箱Count(level), "LLM 的非敌人物体不受影响");
            foreach (var prop in level.Props)
            {
                if (IsEnemyName(prop.PrefabLogicalName))
                {
                    var dist = new Vector2(prop.Position.x, prop.Position.z).magnitude;
                    Assert.GreaterOrEqual(dist, 8f - 0.05f, "兜底敌人落点不得低于环形内径");
                    Assert.LessOrEqual(dist, 25f + 0.05f, "兜底敌人落点不得超出环形外径");
                    Assert.GreaterOrEqual(Mathf.Abs(prop.Position.x), 0f);
                    Assert.LessOrEqual(Mathf.Abs(prop.Position.x), 57.01f, "落点必须保留地形边距（120/2-3）");
                    Assert.LessOrEqual(Mathf.Abs(prop.Position.z), 57.01f);
                    Assert.AreEqual(2, prop.PatrolPoints.Count, "补齐的敌人也应获得默认巡逻点");
                }
                else
                {
                    Assert.IsEmpty(prop.PatrolPoints, "宝箱不应被补巡逻点");
                }
            }
        }

        [Test]
        public void LLM敌人已达数量下限_不额外补敌人()
        {
            var template = NewBattleTemplate(); // MinEnemyCount = 4
            var level = NewLevel(
                Enemy(近战, 0f, 10f), Enemy(弓箭手, 0f, -10f), Enemy(近战, 10f, 0f),
                Enemy(精英, -10f, 0f), Enemy(弓箭手, 20f, 20f));

            template.FinalizeData(level, 42);

            Assert.AreEqual(5, CountEnemies(level), "LLM 已达标时只提示不裁剪、不补齐");
        }

        [Test]
        public void 清单外逻辑名_不计数不补巡逻点()
        {
            var template = NewBattleTemplate();
            var level = NewLevel(new PropPlacement { PrefabLogicalName = "敌人-守卫", Position = new Vector3(8f, 0f, 4f) });

            template.FinalizeData(level, 42);

            Assert.AreEqual(4, CountEnemies(level), "清单外的守卫不算敌人，模板只按清单补齐（守卫作为普通物体保留）");
            foreach (var prop in level.Props)
                if (prop.PrefabLogicalName == "敌人-守卫")
                    Assert.IsEmpty(prop.PatrolPoints, "清单外的物体不补巡逻点");
        }

        [Test]
        public void 全零权重配置_运行时容错回退首个有效项()
        {
            var template = NewBattleTemplate();
            template.EnemyOptions.Clear();
            template.EnemyOptions.Add(new EnemyTypeOption { LogicalName = 近战, Weight = 1f });
            template.EnemyOptions.Add(new EnemyTypeOption { LogicalName = 弓箭手, Weight = 0f }); // 非法权重运行时跳过
            template.EnemyOptions.Add(new EnemyTypeOption { LogicalName = 精英, Weight = 0f });
            var level = NewLevel();

            template.FinalizeData(level, 42);

            Assert.AreEqual(4, CountEnemies(level));
            foreach (var prop in level.Props)
                Assert.AreEqual(近战, prop.PrefabLogicalName, "仅剩唯一有效权重项时全部选它（确定性兜底）");
        }

        // —— 巡逻点兜底 ——

        [Test]
        public void LLM已输出巡逻点_不覆盖_空巡逻点者补齐()
        {
            var template = NewBattleTemplate();
            var llmPatrol = new List<Vector3> { new(9f, 0f, 9f), new(1f, 0f, 1f), new(5f, 0f, 3f) };
            var withPatrol = Enemy(近战, 8f, 4f);
            withPatrol.PatrolPoints = new List<Vector3>(llmPatrol);
            var level = NewLevel(withPatrol, Enemy(弓箭手, -8f, 6f));

            template.FinalizeData(level, 42);

            // LLM 两条敌人保留原索引（补位敌人一律追加到尾部）
            CollectionAssert.AreEqual(llmPatrol, level.Props[0].PatrolPoints, "LLM 已命中 Schema 的巡逻点必须原样保留");
            Assert.AreEqual(2, level.Props[1].PatrolPoints.Count, "空巡逻点的敌人应按配置补齐默认巡逻点");
            for (var i = 2; i < level.Props.Count; i++)
                Assert.AreEqual(2, level.Props[i].PatrolPoints.Count, "模板补位的敌人同样获得默认巡逻点");
        }

        [Test]
        public void 巡逻点补齐_半径区间与环形有序()
        {
            var template = NewBattleTemplate(); // 巡逻半径 3~8，每敌 2 点 → 两点夹角 180°
            var level = NewLevel(Enemy(弓箭手, 0f, 0f));

            template.FinalizeData(level, 42);

            var points = level.Props[0].PatrolPoints;
            Assert.AreEqual(2, points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                var radius = Mathf.Sqrt(points[i].x * points[i].x + points[i].z * points[i].z);
                Assert.GreaterOrEqual(radius, 3f - 0.05f, "巡逻半径不得低于下限");
                Assert.LessOrEqual(radius, 8f + 0.05f, "巡逻半径不得超出上限");
                Assert.AreEqual(0f, points[i].y, "巡逻点为水平落点（y=0，地面贴合由构建层处理）");
            }
            // 两点夹角 ≈ 180°（固定扇区角，环状路径语义）
            var dot = Vector2.Dot(new Vector2(points[0].x, points[0].z), new Vector2(points[1].x, points[1].z));
            Assert.LessOrEqual(dot, 0.05f * 8f * 8f, "两点应在敌人两侧（扇区角 180°）");
        }

        // —— 确定性 ——

        [Test]
        public void 同种子两遍执行_兜底结果逐字节一致()
        {
            var jsonA = ToJson(RunOnce(NewBattleTemplate(), NewLevel(Enemy(近战, 8f, 4f)), 42));
            var jsonB = ToJson(RunOnce(NewBattleTemplate(), NewLevel(Enemy(近战, 8f, 4f)), 42));
            Assert.AreEqual(jsonA, jsonB, "相同种子+相同输入必须产出完全一致的 LevelData");
        }

        [Test]
        public void 不同种子_兜底补位内容不同()
        {
            var a = RunOnce(NewBattleTemplate(), NewLevel(Enemy(近战, 8f, 4f)), 1);
            var b = RunOnce(NewBattleTemplate(), NewLevel(Enemy(近战, 8f, 4f)), 2);

            var differ = false;
            for (var i = 1; i < a.Props.Count && i < b.Props.Count; i++)
                if (a.Props[i].Position != b.Props[i].Position || a.Props[i].PrefabLogicalName != b.Props[i].PrefabLogicalName)
                    differ = true;
            Assert.IsTrue(differ, "不同种子的兜底落点/选型必须不同");
        }

        [Test]
        public void 半径配置倒挂_自动纠正且不抛()
        {
            var template = NewBattleTemplate();
            template.EnemySpawnRingMin = 25f; // 与 Max=8 倒挂 → 运行时纠正为 [8,25]
            template.EnemySpawnRingMax = 8f;
            template.PatrolRadiusMin = 8f;   // 与 Max=3 倒挂 → 纠正为 [3,8]
            template.PatrolRadiusMax = 3f;
            var level = NewLevel(Enemy(近战, 8f, 4f));

            Assert.DoesNotThrow(() => template.FinalizeData(level, 42));
            Assert.AreEqual(4, CountEnemies(level));
            var enemies = level.Props;
            foreach (var prop in enemies)
            {
                var dist = new Vector2(prop.Position.x, prop.Position.z).magnitude;
                Assert.GreaterOrEqual(dist, 8f - 0.05f);
                Assert.LessOrEqual(dist, 25f + 0.05f);
            }
        }

        [Test]
        public void 地形缺失_兜底不崩()
        {
            var template = NewBattleTemplate();
            var level = NewLevel(Enemy(近战, 8f, 4f));
            level.Terrain = null;

            Assert.DoesNotThrow(() => template.FinalizeData(level, 42));
            Assert.AreEqual(4, CountEnemies(level), "地形缺失时仍应完成数量兜底（不做边界夹取）");
        }

        // —— ValidateSelf ——

        [Test]
        public void 战斗配置合法_自校验通过()
        {
            var template = NewBattleTemplate();
            template.TemplateId = "battle_tpl";
            Assert.IsTrue(template.ValidateSelf(out _));
        }

        [Test]
        public void 敌人清单含空逻辑名_自校验失败()
        {
            var template = NewBattleTemplate();
            template.EnemyOptions.Add(new EnemyTypeOption { LogicalName = "  ", Weight = 1f });
            Assert.IsFalse(template.ValidateSelf(out var error));
            StringAssert.Contains("逻辑名为空", error);
        }

        [Test]
        public void 敌人权重为零_自校验失败()
        {
            var template = NewBattleTemplate();
            template.EnemyOptions[1].Weight = 0f;
            Assert.IsFalse(template.ValidateSelf(out var error));
            StringAssert.Contains("权重必须大于 0", error);
        }

        [Test]
        public void 启用数量下限但清单为空_自校验失败()
        {
            var template = NewBattleTemplate();
            template.EnemyOptions.Clear();
            Assert.IsFalse(template.ValidateSelf(out var error));
            StringAssert.Contains("EnemyOptions 为空", error);
        }

        [Test]
        public void 巡逻半径倒挂_自校验失败()
        {
            var template = NewBattleTemplate();
            template.PatrolRadiusMin = 10f;
            template.PatrolRadiusMax = 2f;
            Assert.IsFalse(template.ValidateSelf(out var error));
            StringAssert.Contains("巡逻半径倒挂", error);
        }

        [Test]
        public void 落点环形倒挂_自校验失败()
        {
            var template = NewBattleTemplate();
            template.EnemySpawnRingMin = 30f;
            template.EnemySpawnRingMax = 10f;
            Assert.IsFalse(template.ValidateSelf(out var error));
            StringAssert.Contains("落点环形半径倒挂", error);
        }

        // —— 内部辅助 ——

        private static LevelData RunOnce(ConfigurableLevelTemplate template, LevelData level, int seed)
        {
            template.FinalizeData(level, seed);
            return level;
        }

        private static bool IsEnemyName(string name) =>
            name == 近战 || name == 弓箭手 || name == 精英;

        private static int 宝箱Count(LevelData level)
        {
            var count = 0;
            foreach (var prop in level.Props)
                if (prop.PrefabLogicalName == "宝箱") count++;
            return count;
        }
    }
}
