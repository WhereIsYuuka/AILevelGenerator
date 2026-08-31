using System.Collections.Generic;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Templates;
using AILevelGenerator.Runtime.Validation;
using NUnit.Framework;
using UnityEngine;
// 数据层 TerrainData 与 UnityEngine.TerrainData 同名，别名消除歧义（见 CLAUDE.md 已知坑）
using TerrainData = AILevelGenerator.Runtime.Data.TerrainData;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 模板范围校验单元测试（模板专属校验器，构造注入模板实例）：
    /// Props/Tasks 数量与模板 Min/Max 一致、主线任务强制、地形与模板一致（OverrideTerrain=true 时）。
    /// 错误码复用 LLM 侧（同码双级：LLM 产 Warning 提示，此处产 Error 拦截）。
    /// </summary>
    public class TemplateScopeValidatorTests
    {
        private static ConfigurableLevelTemplate CreateTemplate() => ScriptableObject.CreateInstance<ConfigurableLevelTemplate>();

        private static ValidationResult Validate(ConfigurableLevelTemplate template, LevelData data)
        {
            var validator = new TemplateScopeValidator(template);
            return validator.Validate(data, new ValidationContext());
        }

        private static LevelData CreateLevelData(int propCount, int taskCount, bool hasMainTask = true) => new LevelData
        {
            LevelName = "测试关卡",
            Props = CreateProps(propCount),
            Tasks = CreateTasks(taskCount, hasMainTask),
            Terrain = new TerrainData { Width = 100, Length = 100, HeightScale = 10f }
        };

        private static List<PropPlacement> CreateProps(int count)
        {
            var list = new List<PropPlacement>();
            for (var i = 0; i < count; i++)
                list.Add(new PropPlacement { PrefabLogicalName = $"道具{i}", Position = new Vector3(i, 0, 0), Scale = Vector3.one });
            return list;
        }

        private static List<TaskData> CreateTasks(int count, bool hasMainTask)
        {
            var list = new List<TaskData>();
            for (var i = 0; i < count; i++)
                list.Add(new TaskData { TaskID = $"task_{i}", IsMainTask = hasMainTask && i == 0 });
            return list;
        }

        [Test]
        public void 构造空模板_抛参数异常()
        {
            Assert.Throws<System.ArgumentNullException>(() => new TemplateScopeValidator(null));
        }

        [Test]
        public void 道具超上限_报数量超限错误()
        {
            var template = CreateTemplate();
            template.MaxPropCount = 5;
            template.MinTaskCount = 1;
            template.MaxTaskCount = 10;

            var result = Validate(template, CreateLevelData(propCount: 6, taskCount: 1));

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("PROPS_TOO_MANY", result.Errors[0].Code);
            StringAssert.Contains("超过模板上限", result.Errors[0].Message);
        }

        [Test]
        public void 道具低于下限_报数量不足错误()
        {
            var template = CreateTemplate();
            template.MinPropCount = 3;
            template.MinTaskCount = 1;
            template.MaxTaskCount = 10;

            var result = Validate(template, CreateLevelData(propCount: 2, taskCount: 1));

            Assert.AreEqual("PROPS_TOO_FEW", result.Errors[0].Code);
        }

        [Test]
        public void 任务超上限_报任务数量超限错误()
        {
            var template = CreateTemplate();
            template.MinPropCount = 1;
            template.MaxTaskCount = 3;

            var result = Validate(template, CreateLevelData(propCount: 1, taskCount: 4));

            Assert.AreEqual("TASKS_TOO_MANY", result.Errors[0].Code);
        }

        [Test]
        public void 任务低于下限_报任务数量不足错误()
        {
            var template = CreateTemplate();
            template.MinPropCount = 1;
            template.MinTaskCount = 2;
            template.MaxTaskCount = 5;

            var result = Validate(template, CreateLevelData(propCount: 1, taskCount: 1));

            Assert.AreEqual("TASKS_TOO_FEW", result.Errors[0].Code);
        }

        [Test]
        public void 缺少主线任务_报无主线错误()
        {
            var template = CreateTemplate();
            template.ForceMainTask = true;
            template.MinPropCount = 1;
            template.MaxTaskCount = 10;

            var result = Validate(template, CreateLevelData(propCount: 1, taskCount: 2, hasMainTask: false));

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("NO_MAIN_TASK", result.Errors[0].Code);
        }

        [Test]
        public void 地形与模板不一致_报地形不一致错误()
        {
            var template = CreateTemplate();
            template.OverrideTerrain = true;
            template.TerrainWidth = 200;
            template.MinPropCount = 1;
            template.MaxTaskCount = 10;
            var data = CreateLevelData(propCount: 1, taskCount: 1); // 地形 100×100

            var result = Validate(template, data);

            Assert.AreEqual("TERRAIN_MISMATCH", result.Errors[0].Code);
            Assert.AreEqual("terrain.width", result.Errors[0].DataPath);
        }

        [Test]
        public void 覆盖地形关闭_跳过地形一致性比较()
        {
            var template = CreateTemplate();
            template.OverrideTerrain = false; // 仅兜底，不强制一致
            template.MinPropCount = 1;
            template.MaxTaskCount = 10;
            var data = CreateLevelData(propCount: 1, taskCount: 1);
            data.Terrain.Width = 999; // 与模板默认 100 不同，但不应报错

            var result = Validate(template, data);

            Assert.IsTrue(result.IsValid, "OverrideTerrain=false 时地形由生成结果主导，不做一致性拦截");
        }

        [Test]
        public void 上限为零_视为不限制()
        {
            var template = CreateTemplate(); // MaxPropCount=0 MaxTaskCount=0（默认不限制）
            var data = CreateLevelData(propCount: 50, taskCount: 50);

            var result = Validate(template, data);

            Assert.IsTrue(result.IsValid, "0 = 不限制，大量内容也不应被拦截");
        }

        [Test]
        public void 全符合模板_校验通过()
        {
            var template = CreateTemplate();
            template.MinPropCount = 1;
            template.MaxPropCount = 10;
            template.MinTaskCount = 1;
            template.MaxTaskCount = 5;
            template.ForceMainTask = true;
            template.OverrideTerrain = true;
            template.TerrainWidth = 100;
            template.TerrainLength = 100;
            template.TerrainHeightScale = 10f;

            var result = Validate(template, CreateLevelData(propCount: 3, taskCount: 2));

            Assert.IsTrue(result.IsValid, "数量/主线/地形全部符合模板时应通过");
        }

        [Test]
        public void 空数据_报数据为空错误()
        {
            var template = CreateTemplate();

            var result = Validate(template, null);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("DATA_NULL", result.Errors[0].Code);
        }
    }
}
