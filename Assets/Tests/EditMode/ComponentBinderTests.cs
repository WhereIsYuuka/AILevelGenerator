using System.Collections.Generic;
using AILevelGenerator.Runtime.Components;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 组件绑定器单元测试（Day4）：类型映射（轻量反射）、显式 AddComponent、参数装配、
    /// 失败不阻塞、幂等。绑定器为纯逻辑 + Unity 原生 API，EditMode 可直接测。
    /// </summary>
    public class ComponentBinderTests
    {
        private static ComponentBindingConfig CreateConfig()
        {
            var config = ScriptableObject.CreateInstance<ComponentBindingConfig>();
            var binding = new LogicalBinding { LogicalName = "敌人-弓箭手" };
            binding.Components.Add(new ComponentBindingEntry
            {
                ComponentTypeName = "AILevelGenerator.Runtime.Components.MonsterHealth",
                Parameters = { new ParameterOverride { Key = "maxHealth", Value = "150" } }
            });
            binding.Components.Add(new ComponentBindingEntry
            {
                ComponentTypeName = "AILevelGenerator.Runtime.Components.BasicAI",
                Parameters =
                {
                    new ParameterOverride { Key = "patrolRadius", Value = "6" },
                    new ParameterOverride { Key = "moveSpeed", Value = "2.5" }
                }
            });
            config.Bindings.Add(binding);
            return config;
        }

        [Test]
        public void 类型映射_全限定名可解析()
        {
            var binder = new ComponentBinder(null);

            Assert.IsNotNull(binder.ResolveType("AILevelGenerator.Runtime.Components.MonsterHealth"), "全限定名应解析成功");
            Assert.IsNotNull(binder.ResolveType("AILevelGenerator.Runtime.Components.MonsterHealth, AILevelGenerator.Runtime"),
                "程序集限定名应解析成功");
        }

        [Test]
        public void 类型映射_短名唯一匹配_可解析()
        {
            var binder = new ComponentBinder(null);
            Assert.IsNotNull(binder.ResolveType("MonsterHealth"), "短名唯一匹配应解析成功");
        }

        [Test]
        public void 类型映射_不存在类型_返回null()
        {
            var binder = new ComponentBinder(null);
            Assert.IsNull(binder.ResolveType("No.Such.Type.Exists"));
        }

        [Test]
        public void 绑定_挂载组件且参数装配正确()
        {
            var logger = new TestLogger();
            var binder = new ComponentBinder(CreateConfig(), logger);
            var go = new GameObject("实体");

            var result = binder.BindTo("敌人-弓箭手", go);

            Assert.AreEqual(2, result.BoundCount, "两个组件都应绑定成功");
            Assert.AreEqual(0, result.FailedCount);
            var health = go.GetComponent<MonsterHealth>();
            var ai = go.GetComponent<BasicAI>();
            Assert.IsNotNull(health, "应挂载 MonsterHealth");
            Assert.IsNotNull(ai, "应挂载 BasicAI");
            Assert.AreEqual(150, health.MaxHealth, "maxHealth=150 应生效");
            Assert.AreEqual(0, logger.Messages.Count, "全量成功不应产生日志");
        }

        [Test]
        public void 绑定_类型找不到_失败不阻塞后续组件()
        {
            var logger = new TestLogger();
            var config = ScriptableObject.CreateInstance<ComponentBindingConfig>();
            var binding = new LogicalBinding { LogicalName = "测试" };
            binding.Components.Add(new ComponentBindingEntry { ComponentTypeName = "Bad.Type.NotExists" }); // 故意写错
            binding.Components.Add(new ComponentBindingEntry { ComponentTypeName = "AILevelGenerator.Runtime.Components.MonsterHealth" });
            config.Bindings.Add(binding);
            var binder = new ComponentBinder(config, logger);
            var go = new GameObject("实体");

            var result = binder.BindTo("测试", go);

            Assert.AreEqual(1, result.FailedCount, "坏类型应记失败");
            Assert.AreEqual(1, result.BoundCount, "好类型仍应绑定成功（不阻塞）");
            Assert.IsNotNull(go.GetComponent<MonsterHealth>(), "后续组件应正常挂载");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("Bad.Type.NotExists")),
                "日志应明确提示找不到的类型名");
        }

        [Test]
        public void 绑定_重复绑定_幂等不重复挂载()
        {
            var binder = new ComponentBinder(CreateConfig(), new TestLogger());
            var go = new GameObject("实体");

            binder.BindTo("敌人-弓箭手", go);
            var result = binder.BindTo("敌人-弓箭手", go);

            Assert.AreEqual(2, result.SkippedCount, "重复绑定应全部跳过（已存在）");
            Assert.AreEqual(1, go.GetComponents<MonsterHealth>().Length, "同类型组件不得重复添加");
            Assert.AreEqual(1, go.GetComponents<BasicAI>().Length);
        }

        [Test]
        public void 绑定_未实现装配接口的组件_挂载成功但警告()
        {
            var logger = new TestLogger();
            var config = ScriptableObject.CreateInstance<ComponentBindingConfig>();
            var binding = new LogicalBinding { LogicalName = "刚体" };
            binding.Components.Add(new ComponentBindingEntry { ComponentTypeName = "UnityEngine.Rigidbody" });
            config.Bindings.Add(binding);
            var binder = new ComponentBinder(config, logger);
            var go = new GameObject("实体");

            var result = binder.BindTo("刚体", go);

            Assert.AreEqual(1, result.BoundCount, "组件本身挂载成功");
            Assert.IsNotNull(go.GetComponent<Rigidbody>());
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("IBindableComponent")),
                "应警告未实现装配接口");
        }

        [Test]
        public void 绑定_非法参数_组件保持默认值()
        {
            var config = ScriptableObject.CreateInstance<ComponentBindingConfig>();
            var binding = new LogicalBinding { LogicalName = "敌人" };
            binding.Components.Add(new ComponentBindingEntry
            {
                ComponentTypeName = "AILevelGenerator.Runtime.Components.MonsterHealth",
                Parameters = { new ParameterOverride { Key = "maxHealth", Value = "abc" } } // 非法值
            });
            config.Bindings.Add(binding);
            var binder = new ComponentBinder(config, new TestLogger());
            var go = new GameObject("实体");

            binder.BindTo("敌人", go);

            Assert.AreEqual(100, go.GetComponent<MonsterHealth>().MaxHealth, "非法参数应保持默认值（100）");
        }

        [Test]
        public void 绑定_未配置逻辑名_空结果不报错()
        {
            var logger = new TestLogger();
            var binder = new ComponentBinder(CreateConfig(), logger);
            var go = new GameObject("实体");

            var result = binder.BindTo("不存在的逻辑名", go);

            Assert.AreEqual(0, result.BoundCount);
            Assert.AreEqual(0, result.FailedCount);
            Assert.AreEqual(0, logger.Messages.Count, "未配置绑定不应产生日志");
        }

        [Test]
        public void 绑定_配置为null_空结果不报错()
        {
            var binder = new ComponentBinder(null, new TestLogger());
            var go = new GameObject("实体");

            var result = binder.BindTo("敌人-弓箭手", go);

            Assert.AreEqual(0, result.BoundCount);
            Assert.AreEqual(0, result.FailedCount);
        }

        [Test]
        public void 绑定_目标为null_空结果不报错()
        {
            var binder = new ComponentBinder(CreateConfig(), new TestLogger());
            var result = binder.BindTo("敌人-弓箭手", null);
            Assert.AreEqual(0, result.BoundCount);
            Assert.AreEqual(0, result.FailedCount);
        }
    }
}
