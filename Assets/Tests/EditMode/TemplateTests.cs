using System;
using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Templates;
using NUnit.Framework;
using UnityEngine;
// using System 引入 object 与 UnityEngine.Object 歧义；Runtime.Data.TerrainData 与 UnityEngine.TerrainData 歧义 → 本地别名消歧
using TerrainData = AILevelGenerator.Runtime.Data.TerrainData;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 数据驱动模板单元测试：关卡模板 ApplyDefaults 默认值/覆盖语义与 ValidateSelf 自校验，
    /// 任务模板兜底语义（空字段写入、已设置不覆盖）。
    /// ScriptableObject 统一用 CreateInstance 创建（Unity 规范），TearDown 销毁防止内存泄漏。
    /// </summary>
    public class TemplateTests
    {
        private readonly List<ScriptableObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var so in _created)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _created.Clear();
        }

        /// <summary> 创建关卡模板实例（CreateInstance + 配置回调） </summary>
        private ConfigurableLevelTemplate NewLevel(Action<ConfigurableLevelTemplate> setup)
        {
            var t = ScriptableObject.CreateInstance<ConfigurableLevelTemplate>();
            _created.Add(t);
            setup(t);
            return t;
        }

        /// <summary> 创建任务模板实例（CreateInstance + 配置回调） </summary>
        private ConfigurableTaskTemplate NewTask(Action<ConfigurableTaskTemplate> setup)
        {
            var t = ScriptableObject.CreateInstance<ConfigurableTaskTemplate>();
            _created.Add(t);
            setup(t);
            return t;
        }

        // —— 关卡模板 ——

        [Test]
        public void 关卡ApplyDefaults_地形为空_创建Terrain并填模板默认值()
        {
            var template = NewLevel(t =>
            {
                t.TemplateId = "linear";
                t.TerrainWidth = 80;
                t.TerrainLength = 200;
                t.TerrainHeightScale = 8;
            });
            var level = new LevelData(); // Terrain 为空

            template.ApplyDefaults(level);

            Assert.IsNotNull(level.Terrain, "地形为空时应按模板默认值创建");
            Assert.AreEqual(80, level.Terrain.Width);
            Assert.AreEqual(200, level.Terrain.Length);
            Assert.AreEqual(8f, level.Terrain.HeightScale);
        }

        [Test]
        public void 关卡ApplyDefaults_OverrideTerrain为false_不覆盖已有地形()
        {
            var template = NewLevel(t =>
            {
                t.OverrideTerrain = false;
                t.TerrainWidth = 80;
                t.TerrainLength = 200;
                t.TerrainHeightScale = 8;
            });
            var level = new LevelData { Terrain = new TerrainData { Width = 50, Length = 60, HeightScale = 3 } };

            template.ApplyDefaults(level);

            Assert.AreEqual(50, level.Terrain.Width, "OverrideTerrain=false 时不应覆盖生成结果的地形");
            Assert.AreEqual(60, level.Terrain.Length);
            Assert.AreEqual(3f, level.Terrain.HeightScale);
        }

        [Test]
        public void 关卡ApplyDefaults_OverrideTerrain为true_覆盖已有地形()
        {
            var template = NewLevel(t =>
            {
                t.OverrideTerrain = true;
                t.TerrainWidth = 80;
                t.TerrainLength = 200;
                t.TerrainHeightScale = 8;
            });
            var level = new LevelData { Terrain = new TerrainData { Width = 50, Length = 60, HeightScale = 3 } };

            template.ApplyDefaults(level);

            Assert.AreEqual(80, level.Terrain.Width, "OverrideTerrain=true 时应用模板默认值覆盖");
            Assert.AreEqual(200, level.Terrain.Length);
            Assert.AreEqual(8f, level.Terrain.HeightScale);
        }

        [Test]
        public void 关卡ApplyDefaults_空数据_不抛异常()
        {
            var template = NewLevel(_ => { });
            Assert.DoesNotThrow(() => template.ApplyDefaults(null));
        }

        [Test]
        public void 关卡ValidateSelf_数量范围倒挂_校验失败()
        {
            var template = NewLevel(t =>
            {
                t.TemplateId = "linear";
                t.MinPropCount = 10;
                t.MaxPropCount = 5;
            });

            var ok = template.ValidateSelf(out var error);

            Assert.IsFalse(ok);
            Assert.IsTrue(error.Contains("MinPropCount"), "错误信息应指明倒挂的字段");
        }

        [Test]
        public void 关卡ValidateSelf_TemplateId缺失_校验失败()
        {
            var template = NewLevel(_ => { }); // TemplateId 为空

            var ok = template.ValidateSelf(out var error);

            Assert.IsFalse(ok);
            Assert.IsTrue(error.Contains("TemplateId"));
        }

        [Test]
        public void 关卡ValidateSelf_配置合法_校验通过()
        {
            var template = NewLevel(t =>
            {
                t.TemplateId = "linear";
                t.MinPropCount = 5;
                t.MaxPropCount = 15;
            });
            Assert.IsTrue(template.ValidateSelf(out _));
        }

        [Test]
        public void GetGuideline_返回Guideline字段_缺省为空字符串()
        {
            Assert.AreEqual("单向推进", NewLevel(t => t.Guideline = "单向推进").GetGuideline());
            Assert.AreEqual(string.Empty, NewLevel(_ => { }).GetGuideline());
            Assert.AreEqual(string.Empty, NewLevel(t => t.Guideline = null).GetGuideline(), "null 指南应返回空字符串而非 null");
        }

        // —— 任务模板 ——

        [Test]
        public void 任务ApplyDefaults_字段未设置_写入模板默认值()
        {
            var template = NewTask(t =>
            {
                t.TemplateId = "kill";
                t.DisplayName = "击杀任务";
                t.Description = "击败指定数量的敌人";
                t.DefaultTimeLimit = -1;
                t.DefaultTriggerCondition = "击败敌人";
            });
            var task = new TaskData(); // 全部为空/默认值

            template.ApplyDefaults(task);

            Assert.AreEqual("击杀任务", task.TaskName);
            Assert.AreEqual("击败指定数量的敌人", task.Description);
            Assert.AreEqual(-1f, task.TimeLimit);
            Assert.AreEqual("击败敌人", task.TriggerCondition);
        }

        [Test]
        public void 任务ApplyDefaults_字段已设置_不覆盖生成结果()
        {
            var template = NewTask(t =>
            {
                t.DisplayName = "击杀任务";
                t.DefaultTimeLimit = 120;
                t.DefaultTriggerCondition = "击败敌人";
            });
            var task = new TaskData
            {
                TaskName = "自定义任务名",
                Description = "LLM 生成描述",
                TimeLimit = 60,
                TriggerCondition = "进入区域"
            };

            template.ApplyDefaults(task);

            Assert.AreEqual("自定义任务名", task.TaskName, "已填写的任务名不应被覆盖");
            Assert.AreEqual("LLM 生成描述", task.Description);
            Assert.AreEqual(60, task.TimeLimit, "已设置的时限不应被覆盖");
            Assert.AreEqual("进入区域", task.TriggerCondition);
        }

        [Test]
        public void 任务ApplyDefaults_生成结果无奖励_写入模板奖励()
        {
            var template = NewTask(t => t.DefaultReward = new RewardData { Experience = 100, Gold = 50 });
            var task = new TaskData(); // Reward 为 null

            template.ApplyDefaults(task);

            Assert.IsNotNull(task.Reward);
            Assert.AreEqual(100, task.Reward.Experience);
            Assert.AreEqual(50, task.Reward.Gold);
        }

        [Test]
        public void 任务ApplyDefaults_默认奖励为空_保留原奖励()
        {
            var template = NewTask(_ => { }); // DefaultReward 为 null
            var task = new TaskData { Reward = new RewardData { Experience = 500, Gold = 200 } };

            template.ApplyDefaults(task);

            Assert.IsNotNull(task.Reward);
            Assert.AreEqual(500, task.Reward.Experience, "模板未配置奖励时不应清空生成结果的奖励");
            Assert.AreEqual(200, task.Reward.Gold);
        }

        [Test]
        public void 任务ApplyDefaults_空数据_不抛异常()
        {
            var template = NewTask(_ => { });
            Assert.DoesNotThrow(() => template.ApplyDefaults(null));
        }
    }
}
