using System.Collections.Generic;
using AILevelGenerator.Runtime.Components;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Validation;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 后置校验器单元测试（第四周-Day3）：
    /// 实体空引用（清单缺失/数量不一致/含 null）、组件完整性（按实体名查绑定配置核对挂载）、
    /// 逻辑可达性（构造注入 override 确定性判定，零物理环境波动）、降级语义（无配置/类型不可解析/无地面）。
    /// </summary>
    public class PostBuildValidatorTests
    {
        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private GameObject CreateEntity(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        private static PostBuildData CreateData(List<GameObject> entities, int expected)
            => new() { Entities = entities, ExpectedCount = expected };

        /// <summary> 真校验器（可达性用注入 override，无物理依赖；组件完整性按需传配置） </summary>
        private static PostBuildValidator CreateValidator(
            ComponentBindingConfig bindingConfig = null, bool checkReachability = false,
            System.Func<GameObject, bool> groundedOverride = null)
            => new(bindingConfig, checkReachability, groundedOverride);

        [Test]
        public void 数据为空_报实体清单缺失错误()
        {
            var result = CreateValidator().Validate(null, null);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("POST_ENTITIES_MISSING", result.Errors[0].Code);
        }

        [Test]
        public void 实体清单为null_报实体清单缺失错误()
        {
            var result = CreateValidator().Validate(CreateData(null, 0), null);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("POST_ENTITIES_MISSING", result.Errors[0].Code);
        }

        [Test]
        public void 数量与预期不一致_报数量不一致错误()
        {
            var result = CreateValidator().Validate(CreateData(new List<GameObject> { CreateEntity("宝箱") }, 3), null);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("POST_COUNT_MISMATCH", result.Errors[0].Code);
            Assert.AreEqual("entities", result.Errors[0].DataPath);
            StringAssert.Contains("3", result.Errors[0].Message);
        }

        [Test]
        public void 预期零实体且列表为空_合法通过()
        {
            var result = CreateValidator().Validate(CreateData(new List<GameObject>(), 0), null);

            Assert.IsTrue(result.IsValid, "零实体关卡（无道具生成）应合法通过");
        }

        [Test]
        public void 列表含空引用_报实体空引用错误且定位索引()
        {
            var go = CreateEntity("宝箱");
            var result = CreateValidator().Validate(CreateData(new List<GameObject> { go, null }, 2), null);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("POST_ENTITY_NULL", result.Errors[0].Code);
            Assert.AreEqual("entities[1]", result.Errors[0].DataPath);
        }

        [Test]
        public void 实体缺少绑定组件_报组件缺失错误且定位到组件类型()
        {
            var bindingConfig = ScriptableObject.CreateInstance<ComponentBindingConfig>();
            bindingConfig.Bindings.Add(new LogicalBinding
            {
                LogicalName = "宝箱",
                Components = new List<ComponentBindingEntry>
                {
                    new() { ComponentTypeName = "UnityEngine.Rigidbody" }
                }
            });
            var entity = CreateEntity("宝箱"); // 未挂 Rigidbody

            var result = CreateValidator(bindingConfig).Validate(CreateData(new List<GameObject> { entity }, 1), null);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("POST_COMPONENT_MISSING", result.Errors[0].Code);
            StringAssert.Contains("Rigidbody", result.Errors[0].DataPath);
        }

        [Test]
        public void 实体已挂载绑定组件_组件完整性通过()
        {
            var bindingConfig = ScriptableObject.CreateInstance<ComponentBindingConfig>();
            bindingConfig.Bindings.Add(new LogicalBinding
            {
                LogicalName = "宝箱",
                Components = new List<ComponentBindingEntry>
                {
                    new() { ComponentTypeName = "UnityEngine.Rigidbody" }
                }
            });
            var entity = CreateEntity("宝箱");
            entity.AddComponent<Rigidbody>();

            var result = CreateValidator(bindingConfig).Validate(CreateData(new List<GameObject> { entity }, 1), null);

            Assert.IsTrue(result.IsValid, "绑定组件已挂载时组件完整性应通过");
        }

        [Test]
        public void 无绑定配置_组件完整性整体降级跳过()
        {
            var entity = CreateEntity("任意实体");

            var result = CreateValidator(null).Validate(CreateData(new List<GameObject> { entity }, 1), null);

            Assert.IsTrue(result.IsValid, "无绑定配置时不得报组件错误（与 ComponentBinder 语义一致）");
        }

        [Test]
        public void 组件类型名不可解析_跳过不报错()
        {
            var bindingConfig = ScriptableObject.CreateInstance<ComponentBindingConfig>();
            bindingConfig.Bindings.Add(new LogicalBinding
            {
                LogicalName = "宝箱",
                Components = new List<ComponentBindingEntry>
                {
                    new() { ComponentTypeName = "不存在的.类型.XYZ" }
                }
            });
            var entity = CreateEntity("宝箱");

            var result = CreateValidator(bindingConfig).Validate(CreateData(new List<GameObject> { entity }, 1), null);

            Assert.IsTrue(result.IsValid, "类型不可解析属配置问题（绑定期已告警），不得误报实体缺陷");
        }

        [Test]
        public void 悬空实体_报逻辑不可达错误()
        {
            var entity = CreateEntity("宝箱");

            var result = CreateValidator(checkReachability: true, groundedOverride: _ => false)
                .Validate(CreateData(new List<GameObject> { entity }, 1), null);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("POST_FLOAT_UNSUPPORTED", result.Errors[0].Code);
            Assert.AreEqual("entities[0]", result.Errors[0].DataPath);
        }

        [Test]
        public void 实体有地面支撑_可达性通过()
        {
            var entity = CreateEntity("宝箱");

            var result = CreateValidator(checkReachability: true, groundedOverride: _ => true)
                .Validate(CreateData(new List<GameObject> { entity }, 1), null);

            Assert.IsTrue(result.IsValid, "有地面支撑时可达性应通过");
        }

        [Test]
        public void 可达性开关关闭_跳过可达性检查()
        {
            var entity = CreateEntity("宝箱");

            // 开关关闭 + override 注入 false（即使注入判定失败也不应报错——总开关优先）
            var result = CreateValidator(checkReachability: false, groundedOverride: _ => false)
                .Validate(CreateData(new List<GameObject> { entity }, 1), null);

            Assert.IsTrue(result.IsValid, "总开关关闭时悬空也不应报错");
        }

        [Test]
        public void 注入override_跳过地面预探测且不报无地面警告()
        {
            var entity = CreateEntity("宝箱");

            // 注入 override 时测试全控：即使真实场景无地面也不应出现 POST_GROUND_MISSING 警告
            var result = CreateValidator(checkReachability: true, groundedOverride: _ => true)
                .Validate(CreateData(new List<GameObject> { entity }, 1), null);

            Assert.IsTrue(result.IsValid);
            Assert.IsEmpty(result.Warnings, "注入 override 应跳过地面预探测，不产生警告");
        }

        [Test]
        public void 全部正常_校验通过()
        {
            var bindingConfig = ScriptableObject.CreateInstance<ComponentBindingConfig>();
            bindingConfig.Bindings.Add(new LogicalBinding
            {
                LogicalName = "宝箱",
                Components = new List<ComponentBindingEntry>
                {
                    new() { ComponentTypeName = "UnityEngine.Rigidbody" }
                }
            });
            var entity = CreateEntity("宝箱");
            entity.AddComponent<Rigidbody>();

            var result = CreateValidator(bindingConfig, checkReachability: true, groundedOverride: _ => true)
                .Validate(CreateData(new List<GameObject> { entity }, 1), null);

            Assert.IsTrue(result.IsValid, "组件完整 + 有支撑 + 数量一致应整体通过");
            Assert.IsEmpty(result.Errors);
        }
    }
}
