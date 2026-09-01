using System.Collections.Generic;
using System.Linq;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Validation;
using NUnit.Framework;
using UnityEngine;
// 数据层 TerrainData 与 UnityEngine.TerrainData 同名，别名消除歧义（见 CLAUDE.md 已知坑）
using TerrainData = AILevelGenerator.Runtime.Data.TerrainData;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 生成中校验累积器单元测试（第四周-Day3）：
    /// 增长前缀累积语义（错误路径全局索引、与源数据字段引用复用）、阶段隔离（Pre 注册不触发 Mid）、
    /// 未注入注册表恒通过（Mid 禁用降级）。
    /// </summary>
    public class MidValidationAccumulatorTests
    {
        private class FakeResourceMapper : IResourceMapper
        {
            public HashSet<string> Available = new();
            public GameObject GetPrefab(string logicalName) => Available.Contains(logicalName) ? new GameObject() : null;
            public bool TryGetPrefab(string logicalName, out GameObject prefab)
            {
                prefab = Available.Contains(logicalName) ? new GameObject() : null;
                return prefab != null;
            }
            public GameObject GetPrefabByFuzzy(string keyword) => null;
            public IReadOnlyList<string> GetAllLogicalNames() => Available.ToList();
        }

        private static PropPlacement CreateProp(string logicalName, Vector3 position, Vector3? scale = null) => new()
        {
            PrefabLogicalName = logicalName,
            Position = position,
            Scale = scale ?? Vector3.one
        };

        private static LevelData CreateSource() => new()
        {
            LevelName = "测试关卡",
            Props = new List<PropPlacement>(),
            Tasks = new List<TaskData> { new() { TaskID = "main_1", IsMainTask = true } },
            Terrain = new TerrainData { Width = 100, Length = 100, HeightScale = 10f }
        };

        /// <summary> 装配 Mid 阶段校验器（数值边界 + 资源存在性，与生产装配一致） </summary>
        private static ValidatorRegistry CreateRegistry(ISet<string> available)
        {
            var registry = new ValidatorRegistry();
            registry.SetServices(new FakeResourceMapper { Available = new HashSet<string>(available) }, null);
            registry.Register(ValidationStage.Mid, new DataBoundsValidator());
            registry.Register(ValidationStage.Mid, new ResourceValidator());
            return registry;
        }

        [Test]
        public void 分批累积第二批含NaN_失败且错误路径为全局索引()
        {
            var registry = CreateRegistry(new HashSet<string> { "宝箱" });
            var accumulator = new MidValidationAccumulator(registry, CreateSource());

            // 第一批 2 个合法（全局 props[0]、props[1]）
            var first = new List<PropPlacement>
            {
                CreateProp("宝箱", new Vector3(1f, 0f, 1f)),
                CreateProp("宝箱", new Vector3(2f, 0f, 2f))
            };
            Assert.IsTrue(accumulator.ValidateBatch(first).IsValid, "第一批合法应通过");

            // 第二批第 1 个含 NaN（全局 props[2]）——错误路径必须指向全局下标而非切片下标
            var second = new List<PropPlacement>
            {
                CreateProp("宝箱", new Vector3(float.NaN, 0f, 3f))
            };
            var result = accumulator.ValidateBatch(second);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("DATA_NAN_OR_INFINITE", result.Errors[0].Code);
            StringAssert.Contains("props[2]", result.Errors[0].DataPath, "错误路径应保持全量数据的全局索引");
        }

        [Test]
        public void 累积过程中出现映射缺失资源_报资源不存在且全局索引()
        {
            var registry = CreateRegistry(new HashSet<string> { "宝箱" });
            var accumulator = new MidValidationAccumulator(registry, CreateSource());

            var first = new List<PropPlacement> { CreateProp("宝箱", Vector3.zero) };
            Assert.IsTrue(accumulator.ValidateBatch(first).IsValid);

            // 第二批引用映射表中不存在的资源（全局 props[1]）
            var second = new List<PropPlacement> { CreateProp("不存在的敌人", Vector3.zero) };
            var result = accumulator.ValidateBatch(second);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("RESOURCE_NOT_FOUND", result.Errors[0].Code);
            StringAssert.Contains("props[1]", result.Errors[0].DataPath);
        }

        [Test]
        public void 阶段隔离_仅Mid注册触发_Pre注册不触发()
        {
            var registry = new ValidatorRegistry();
            registry.SetServices(new FakeResourceMapper { Available = { "宝箱" } }, null);
            // 只注册 Pre 阶段（校验器类型相同但阶段不同）——Mid 运行时应为空结果
            registry.Register(ValidationStage.Pre, new DataBoundsValidator());
            var accumulator = new MidValidationAccumulator(registry, CreateSource());

            var result = accumulator.ValidateBatch(new List<PropPlacement> { CreateProp("宝箱", Vector3.zero) });

            Assert.IsTrue(result.IsValid, "Pre 注册的校验器不得在 Mid 阶段触发（阶段过滤）");
            Assert.IsEmpty(result.Errors);
        }

        [Test]
        public void 未注入注册表_恒返回合法结果()
        {
            var accumulator = new MidValidationAccumulator(null, CreateSource());

            var result = accumulator.ValidateBatch(new List<PropPlacement> { CreateProp("宝箱", Vector3.zero) });

            Assert.IsTrue(result.IsValid, "Mid 未启用（registry 为 null）时恒通过，不影响构建");
        }

        [Test]
        public void 字段引用复用_与源数据同一实例()
        {
            var source = CreateSource();
            var accumulator = new MidValidationAccumulator(CreateRegistry(new HashSet<string> { "宝箱" }), source);

            accumulator.ValidateBatch(new List<PropPlacement> { CreateProp("宝箱", Vector3.zero) });

            Assert.AreSame(source.Tasks, accumulator.Data.Tasks, "Tasks 必须复用源引用（无逐帧副本）");
            Assert.AreSame(source.Terrain, accumulator.Data.Terrain, "Terrain 必须复用源引用");
            Assert.AreEqual(source.LevelName, accumulator.Data.LevelName);
            Assert.AreNotSame(source.Props, accumulator.Data.Props, "Props 指向增长列表（累积语义），不复用源列表");
        }
    }
}
