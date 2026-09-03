using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces.Templates;
using AILevelGenerator.Runtime.Utilities;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 测试用确定性模板：PostGenerate 里用 rng 散布 12 个实体（位置/旋转均随机）。
    /// 只许用传入 rng —— 验证种子契约与确定性收尾链路。
    /// </summary>
    public class 散点测试关卡模板 : LevelTemplate
    {
        public override void ApplyDefaults(LevelData data) { }

        protected override void PostGenerate(LevelData data, DeterministicRandom rng)
        {
            for (var i = 0; i < 12; i++)
                data.Props.Add(new PropPlacement
                {
                    PrefabLogicalName = i % 2 == 0 ? "敌人-弓箭手" : "宝箱",
                    Position = new Vector3(rng.Range(-30f, 30f), 0f, rng.Range(-30f, 30f)),
                    Rotation = new Vector3(0f, rng.RotationY(), 0f)
                });
        }
    }

    /// <summary> 测试用确定性任务模板：PostGenerate 写回一个随机描述（证明任务侧生命周期同构）。
    /// Day3 起钩子签名带关卡数据（可为 null，仅默认值路径仍可用） </summary>
    public class 散点测试任务模板 : TaskTemplate
    {
        public override void ApplyDefaults(TaskData taskData) { }

        protected override void PostGenerate(TaskData taskData, LevelData levelData, DeterministicRandom rng)
        {
            taskData.Description = $"数量 {rng.Range(1, 20)}，间隔 {rng.Range(0f, 10f):F3}";
        }
    }

    /// <summary>
    /// 模板统一生命周期单元测试（第五周-Day1）：
    /// 验收标准 1「相同种子+相同输入 → 完全一致」：两遍 FinalizeData 产出逐字段一致的 LevelData（JSON 级比对）
    /// 验收标准 2「不同种子 → 明显不同」：散点内容随种子显著变化
    /// </summary>
    public class TemplateLifecycleTests
    {
        // —— 关卡模板 ——

        [Test]
        public void FinalizeData_同种子两遍_场景数据完全一致()
        {
            var jsonA = JsonUtility.ToJson(RunOnce(42));
            var jsonB = JsonUtility.ToJson(RunOnce(42));
            Assert.AreEqual(jsonA, jsonB, "同种子+同输入必须产出逐字节一致的 LevelData");
        }

        [Test]
        public void FinalizeData_不同种子_场景明显不同()
        {
            var a = RunOnce(1);
            var b = RunOnce(2);
            CollectionAssert.AreNotEqual(a.Props, b.Props, "不同种子散点内容必须不同");
            // 抽样验证位置确实发生变化（排除仅顺序不同）
            var samePositions = 0;
            for (var i = 0; i < a.Props.Count; i++)
                if (a.Props[i].Position == b.Props[i].Position) samePositions++;
            Assert.Less(samePositions, a.Props.Count, "不同种子下绝大多数实体位置都应不同");
        }

        [Test]
        public void FinalizeData_随机钩子_确实执行()
        {
            var data = new LevelData();
            var template = new 散点测试关卡模板 { TemplateId = "散布测试" };
            template.FinalizeData(data, 42);
            Assert.AreEqual(12, data.Props.Count, "PostGenerate 必须被统一入口执行");
        }

        [Test]
        public void FinalizeData_数据为空_静默返回不抛()
        {
            var template = new 散点测试关卡模板 { TemplateId = "散布测试" };
            Assert.DoesNotThrow(() => template.FinalizeData(null, 42));
        }

        [Test]
        public void FinalizeData_空TemplateId_仍确定性()
        {
            var jsonA = JsonUtility.ToJson(RunOnce(42, ""));
            var jsonB = JsonUtility.ToJson(RunOnce(42, ""));
            Assert.AreEqual(jsonA, jsonB);
        }

        // —— 任务模板 ——

        [Test]
        public void TaskFinalizeData_同种子一致_异种子不同()
        {
            var t = new 散点测试任务模板 { TemplateId = "收集测试" };
            var a = new TaskData();
            var b = new TaskData();
            t.FinalizeData(a, null, 9);
            t.FinalizeData(b, null, 9);
            Assert.AreEqual(a.Description, b.Description, "同种子任务模板输出必须一致");
            var c = new TaskData();
            t.FinalizeData(c, null, 10);
            Assert.AreNotEqual(a.Description, c.Description, "不同种子任务模板输出必须不同");
        }

        [Test]
        public void TaskFinalizeData_数据为空_静默返回()
        {
            var t = new 散点测试任务模板 { TemplateId = "收集测试" };
            Assert.DoesNotThrow(() => t.FinalizeData(null, null, 42));
        }

        [Test]
        public void TaskFinalizeData_任务不在关卡列表_退化为原派生流不抛()
        {
            // levelData 非空但任务不在列表（引用不一致/外部构造）：跳过任务槽盐，按 Day1 原派生流收尾
            var t = new 散点测试任务模板 { TemplateId = "收集测试" };
            var task = new TaskData();
            var level = new LevelData();
            Assert.DoesNotThrow(() => t.FinalizeData(task, level, 42));
            Assert.IsNotEmpty(task.Description, "内容钩子仍应执行（任务槽盐缺失不阻断收尾）");
        }

        [Test]
        public void TaskFinalizeData_任务槽独立流_不同序号内容不同()
        {
            // 同一模板 + 同一种子：两个任务分处不同槽位 → 独立子流 → 随机内容必须不同（防同型多任务产出完全相同的散点）
            var t = new 散点测试任务模板 { TemplateId = "收集测试" };
            var level = new LevelData { Tasks = { new TaskData(), new TaskData() } };
            t.FinalizeData(level.Tasks[0], level, 9);
            t.FinalizeData(level.Tasks[1], level, 9);
            Assert.AreNotEqual(level.Tasks[0].Description, level.Tasks[1].Description,
                "同种子不同槽位的任务必须拥有独立随机流");

            // 同槽位 + 同种子 → 完全一致（确定性收尾）
            var levelB = new LevelData { Tasks = { new TaskData(), new TaskData() } };
            t.FinalizeData(levelB.Tasks[0], levelB, 9);
            Assert.AreEqual(level.Tasks[0].Description, levelB.Tasks[0].Description,
                "同槽位同种子必须逐字符一致");
        }

        private static LevelData RunOnce(int seed, string templateId = "散布测试")
        {
            var data = new LevelData();
            var template = new 散点测试关卡模板 { TemplateId = templateId };
            template.FinalizeData(data, seed);
            return data;
        }
    }
}
