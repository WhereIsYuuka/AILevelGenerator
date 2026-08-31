using System.Collections.Generic;
using System.Linq;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Validation;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 资源存在性前置校验单元测试（100% 拦截不存在资源）：
    /// 映射服务缺失/映射表空/逻辑名为空/未命中拦截，错误定位到具体 props[i].prefabLogicalName。
    /// </summary>
    public class ResourceValidatorTests
    {
        /// <summary> 假资源映射：按逻辑名集合命中（干净查询，无 GetPrefab 的 LogWarning 噪音） </summary>
        private class FakeResourceMapper : IResourceMapper
        {
            public HashSet<string> Available = new();
            public bool GetPrefabCalled; // 应始终走 TryGetPrefab（校验用干净查询）

            public GameObject GetPrefab(string logicalName)
            {
                GetPrefabCalled = true;
                return Available.Contains(logicalName) ? new GameObject() : null;
            }

            public bool TryGetPrefab(string logicalName, out GameObject prefab)
            {
                prefab = Available.Contains(logicalName) ? new GameObject() : null;
                return prefab != null;
            }

            public GameObject GetPrefabByFuzzy(string keyword) => null;

            public IReadOnlyList<string> GetAllLogicalNames() => Available.ToList();
        }

        private static ValidationResult Validate(LevelData data, IResourceMapper mapper)
        {
            var validator = new ResourceValidator();
            return validator.Validate(data, new ValidationContext { ResourceMapper = mapper });
        }

        private static LevelData CreateLevelData() => new LevelData
        {
            LevelName = "测试关卡",
            Props = new List<PropPlacement>
            {
                new() { PrefabLogicalName = "敌人-弓箭手", Position = Vector3.zero, Scale = Vector3.one }
            }
        };

        [Test]
        public void 映射服务未注入_报缺失错误()
        {
            var result = Validate(CreateLevelData(), null);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("RESOURCE_MAPPER_MISSING", result.Errors[0].Code);
        }

        [Test]
        public void 映射表为空_报映射表空错误()
        {
            var mapper = new FakeResourceMapper(); // 无可用逻辑名

            var result = Validate(CreateLevelData(), mapper);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("RESOURCE_MAPPING_EMPTY", result.Errors[0].Code);
        }

        [Test]
        public void 逻辑名为空_报名称为空错误且定位到具体道具()
        {
            var mapper = new FakeResourceMapper { Available = { "敌人-弓箭手" } };
            var data = CreateLevelData();
            data.Props[0].PrefabLogicalName = "  ";

            var result = Validate(data, mapper);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("RESOURCE_NAME_EMPTY", result.Errors[0].Code);
            Assert.AreEqual("props[0].prefabLogicalName", result.Errors[0].DataPath, "错误应定位到具体字段");
        }

        [Test]
        public void 资源不存在_100拦截且定位到具体道具()
        {
            var mapper = new FakeResourceMapper { Available = { "宝箱" } }; // 仅宝箱可命中
            var data = CreateLevelData(); // 引用"敌人-弓箭手"

            var result = Validate(data, mapper);

            Assert.IsFalse(result.IsValid, "不存在的资源必须 100% 拦截");
            Assert.AreEqual("RESOURCE_NOT_FOUND", result.Errors[0].Code);
            Assert.AreEqual("props[0].prefabLogicalName", result.Errors[0].DataPath);
            Assert.IsFalse(((FakeResourceMapper)mapper).GetPrefabCalled, "校验应走 TryGetPrefab 干净查询，避免 LogWarning 噪音");
        }

        [Test]
        public void 多个道具_逐条校验并聚合全部错误()
        {
            var mapper = new FakeResourceMapper { Available = { "宝箱" } };
            var data = CreateLevelData();
            data.Props.Add(new PropPlacement { PrefabLogicalName = "NPC_不存在", Position = Vector3.zero, Scale = Vector3.one });

            var result = Validate(data, mapper);

            Assert.AreEqual(2, result.Errors.Count, "应聚合全部未命中错误");
            Assert.AreEqual("props[0].prefabLogicalName", result.Errors[0].DataPath);
            Assert.AreEqual("props[1].prefabLogicalName", result.Errors[1].DataPath);
        }

        [Test]
        public void 全部命中_校验通过()
        {
            var mapper = new FakeResourceMapper { Available = { "敌人-弓箭手", "宝箱" } };

            var result = Validate(CreateLevelData(), mapper);

            Assert.IsTrue(result.IsValid, "全部逻辑名可命中时应通过");
        }

        [Test]
        public void 无道具列表_视为合法()
        {
            var mapper = new FakeResourceMapper(); // 映射表为空但无道具可校验

            var result = Validate(new LevelData { LevelName = "无道具关卡" }, mapper);

            Assert.IsTrue(result.IsValid, "无道具列表时不应校验映射表（生成器不产出道具属正常状态）");
        }

        [Test]
        public void 空数据_报数据为空错误()
        {
            var result = Validate(null, new FakeResourceMapper());

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("DATA_NULL", result.Errors[0].Code);
        }
    }
}
