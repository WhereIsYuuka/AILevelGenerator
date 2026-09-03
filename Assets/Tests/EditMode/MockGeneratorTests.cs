using System;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Scheduling;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 模拟生成器边界测试（第三周-Day6 补全）：null 请求防御、可配置实体数量（性能基准）、
    /// 演示失败/异常路径、生成开关。全部用 0ms 延迟，零等待不 flaky。
    /// </summary>
    public class MockGeneratorTests
    {
        private static GenerationRequest CreateRequest() => new GenerationRequest
        {
            Prompt = "森林营地，3个巡逻弓箭手，1个宝箱",
            TemplateId = "战斗关卡",
            RandomSeed = 42
        };

        [Test]
        public async Task 请求为null_抛出参数异常()
        {
            var generator = new MockGenerator(0);
            try
            {
                await generator.GenerateAsync(null);
                Assert.Fail("直接调用方传 null 应被防御性拒绝");
            }
            catch (ArgumentNullException)
            {
                // 预期：防御性拒绝
            }
        }

        [Test]
        public async Task 实体数量为0_返回成功且无道具()
        {
            var generator = new MockGenerator(0, propCount: 0);
            var result = await generator.GenerateAsync(CreateRequest());

            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.LevelData);
            Assert.AreEqual(0, result.LevelData.Props.Count, "propCount=0 时应无任何实体");
        }

        [Test]
        public async Task 实体数量为5_按逻辑名循环生成且坐标有限()
        {
            var generator = new MockGenerator(0, propCount: 5);
            var result = await generator.GenerateAsync(CreateRequest());

            Assert.IsTrue(result.Success);
            Assert.AreEqual(5, result.LevelData.Props.Count);
            // 逻辑名按 敌人-弓箭手/宝箱/NPC 循环
            Assert.AreEqual("敌人-弓箭手", result.LevelData.Props[0].PrefabLogicalName);
            Assert.AreEqual("宝箱", result.LevelData.Props[1].PrefabLogicalName);
            Assert.AreEqual("NPC", result.LevelData.Props[2].PrefabLogicalName);
            Assert.AreEqual("敌人-弓箭手", result.LevelData.Props[3].PrefabLogicalName);
            // 扩展实体坐标须为有限值（NaN/Infinity 会污染构建链路）
            foreach (var prop in result.LevelData.Props)
            {
                Assert.IsTrue(IsFinite(prop.Position), $"实体 {prop.PrefabLogicalName} 坐标含非有限值");
            }
        }

        [Test]
        public async Task 默认实体数量3_保持固定演示坐标()
        {
            var generator = new MockGenerator(0);
            var result = await generator.GenerateAsync(CreateRequest());

            Assert.AreEqual(3, result.LevelData.Props.Count);
            Assert.AreEqual(new Vector3(5, 0, 5), result.LevelData.Props[0].Position, "前 3 个实体应保持固定演示坐标（早期行为兼容）");
        }

        [Test]
        public async Task 提示词含失败_返回业务失败结果()
        {
            var generator = new MockGenerator(0);
            var request = CreateRequest();
            request.Prompt = "演示失败场景";

            var result = await generator.GenerateAsync(request);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Errors.Exists(e => e.Code == "DEMO_FAIL"), "应带演示失败校验错误");
        }

        [Test]
        public async Task 提示词含异常_抛出异常()
        {
            var generator = new MockGenerator(0);
            var request = CreateRequest();
            request.Prompt = "触发异常场景";

            Exception caught = null;
            try { await generator.GenerateAsync(request); }
            catch (Exception ex) { caught = ex; }

            Assert.IsNotNull(caught, "提示词含「异常」应抛出（演示异常路径）");
        }

        [Test]
        public async Task 关闭道具开关_无实体()
        {
            var generator = new MockGenerator(0, propCount: 10);
            var request = CreateRequest();
            request.GenerateProps = false;

            var result = await generator.GenerateAsync(request);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, result.LevelData.Props.Count, "GenerateProps=false 时应跳过实体生成");
        }

        [Test]
        public async Task 关闭地形开关_地形数据为空()
        {
            var generator = new MockGenerator(0);
            var request = CreateRequest();
            request.GenerateTerrain = false;

            var result = await generator.GenerateAsync(request);

            Assert.IsNull(result.LevelData.Terrain, "GenerateTerrain=false 时 Terrain 应为空");
        }

        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
