using System.Collections.Generic;
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
    /// 数值边界前置校验单元测试：NaN/Infinity、零/负缩放、坐标超限、地形越界、任务 ID 空/重复。
    /// 错误路径精确到具体字段（props[i].position / terrain.width / tasks[i].taskID）。
    /// </summary>
    public class DataBoundsValidatorTests
    {
        private static ValidationResult Validate(LevelData data)
        {
            var validator = new DataBoundsValidator();
            return validator.Validate(data, new ValidationContext());
        }

        private static LevelData CreateLevelData() => new LevelData
        {
            LevelName = "测试关卡",
            PlayerStartPosition = Vector3.zero,
            Props = new List<PropPlacement>
            {
                new() { PrefabLogicalName = "宝箱", Position = Vector3.one, Rotation = Vector3.zero, Scale = Vector3.one }
            },
            Terrain = new TerrainData { Width = 100, Length = 100, HeightScale = 10f },
            Tasks = new List<TaskData>
            {
                new() { TaskID = "main_1", IsMainTask = true }
            }
        };

        [Test]
        public void 位置含NaN_报数值非法且定位到具体字段()
        {
            var data = CreateLevelData();
            data.Props[0].Position = new Vector3(float.NaN, 0, 0);

            var result = Validate(data);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("DATA_NAN_OR_INFINITE", result.Errors[0].Code);
            Assert.AreEqual("props[0].position", result.Errors[0].DataPath);
        }

        [Test]
        public void 旋转含无穷大_报数值非法()
        {
            var data = CreateLevelData();
            data.Props[0].Rotation = new Vector3(0, float.PositiveInfinity, 0);

            var result = Validate(data);

            Assert.AreEqual("DATA_NAN_OR_INFINITE", result.Errors[0].Code);
            Assert.AreEqual("props[0].rotation", result.Errors[0].DataPath);
        }

        [Test]
        public void 缩放为零_报缩放非法()
        {
            var data = CreateLevelData();
            data.Props[0].Scale = Vector3.zero;

            var result = Validate(data);

            Assert.AreEqual("DATA_SCALE_INVALID", result.Errors[0].Code);
            Assert.AreEqual("props[0].scale", result.Errors[0].DataPath);
        }

        [Test]
        public void 缩放为负_报缩放非法()
        {
            var data = CreateLevelData();
            data.Props[0].Scale = new Vector3(1, -1, 1);

            var result = Validate(data);

            Assert.AreEqual("DATA_SCALE_INVALID", result.Errors[0].Code);
        }

        [Test]
        public void 坐标超限_报超出范围()
        {
            var data = CreateLevelData();
            data.Props[0].Position = new Vector3(10001, 0, 0); // 上限 10000

            var result = Validate(data);

            Assert.AreEqual("DATA_POSITION_OUT_OF_RANGE", result.Errors[0].Code);
            Assert.AreEqual("props[0].position", result.Errors[0].DataPath);
        }

        [Test]
        public void 出生点超限_报超出范围()
        {
            var data = CreateLevelData();
            data.PlayerStartPosition = new Vector3(0, 0, -50000);

            var result = Validate(data);

            Assert.AreEqual("DATA_POSITION_OUT_OF_RANGE", result.Errors[0].Code);
            Assert.AreEqual("playerStartPosition", result.Errors[0].DataPath);
        }

        [Test]
        public void 地形宽度越界_报地形非法()
        {
            var data = CreateLevelData();
            data.Terrain.Width = 0; // 允许范围 1~10000

            var result = Validate(data);

            Assert.AreEqual("DATA_TERRAIN_INVALID", result.Errors[0].Code);
            Assert.AreEqual("terrain.width", result.Errors[0].DataPath);
        }

        [Test]
        public void 地形高度缩放越界_报地形非法()
        {
            var data = CreateLevelData();
            data.Terrain.HeightScale = -5f;

            var result = Validate(data);

            Assert.AreEqual("DATA_TERRAIN_INVALID", result.Errors[0].Code);
            Assert.AreEqual("terrain.heightScale", result.Errors[0].DataPath);
        }

        [Test]
        public void 地形为null_不崩且通过()
        {
            var data = CreateLevelData();
            data.Terrain = null; // 无模板时 ApplyDefaults 不执行属正常状态

            var result = Validate(data);

            Assert.IsTrue(result.IsValid, "地形缺失不应报错（正常降级状态）");
        }

        [Test]
        public void 任务ID为空_报ID为空()
        {
            var data = CreateLevelData();
            data.Tasks[0].TaskID = "  ";

            var result = Validate(data);

            Assert.AreEqual("DATA_TASK_ID_EMPTY", result.Errors[0].Code);
            Assert.AreEqual("tasks[0].taskID", result.Errors[0].DataPath);
        }

        [Test]
        public void 任务ID重复_报ID重复()
        {
            var data = CreateLevelData();
            data.Tasks.Add(new TaskData { TaskID = "main_1", IsMainTask = false });

            var result = Validate(data);

            Assert.AreEqual("DATA_TASK_ID_DUPLICATE", result.Errors[0].Code);
            Assert.AreEqual("tasks[1].taskID", result.Errors[0].DataPath);
        }

        [Test]
        public void 正常数据_校验通过()
        {
            var result = Validate(CreateLevelData());

            Assert.IsTrue(result.IsValid, "合法数值应通过全部边界检查");
        }
    }
}
