using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Diagnostics;
using AILevelGenerator.Runtime.Interfaces;
using UnityEngine;
// 数据层 TerrainData 与 UnityEngine.TerrainData 同名，别名消除歧义（见 CLAUDE.md 已知坑）
using TerrainData = AILevelGenerator.Runtime.Data.TerrainData;

namespace AILevelGenerator.Runtime.Scheduling
{
    /// <summary>
    /// 模拟生成器（第四周-Day5 占位实现，真实 LLM 接入后保留为演示/压力测试通道；文中裸 DayN 均指第四周）。
    /// 演示通道（便于手动验收状态机各路径）：
    ///   - 提示词含"失败" → 返回 Success=false + DEMO_FAIL 校验错误（演示业务失败路径）
    ///   - 提示词含"异常" → 抛出异常（演示 catch → Failed 路径）
    ///   - 其余 → 按请求开关返回演示关卡数据（道具名与 Day4 资源映射表一致）
    /// </summary>
    public class MockGenerator : IGenerator
    {
        private readonly int _delayMilliseconds;
        private readonly int _propCount; // Day6：可配置实体数量（性能基准/压力测试用），默认 3 保持演示行为

        public MockGenerator(int delayMilliseconds = 1500, int propCount = 3)
        {
            _delayMilliseconds = Math.Max(0, delayMilliseconds);
            _propCount = Math.Max(0, propCount);
        }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request)
        {
            // Day6 防御：直接调用方传 null（调度器已前置校验，这里防绕过调度器的测试/脚本误用）
            if (request == null) throw new ArgumentNullException(nameof(request));

            var stopwatch = Stopwatch.StartNew();
            if (_delayMilliseconds > 0)
                await Task.Delay(_delayMilliseconds); // 模拟 LLM 网络耗时

            if (request.Prompt.Contains("异常"))
                throw new Exception("模拟生成器抛出异常（演示用）");

            if (request.Prompt.Contains("失败"))
            {
                return new GenerationResult
                {
                    Success = false,
                    Errors = new List<ValidationError>
                    {
                        new() { Code = ErrorCodes.DEMO_FAIL, Message = "模拟生成器演示失败路径（提示词含“失败”）" }
                    },
                    GenerationTime = (float)stopwatch.Elapsed.TotalSeconds
                };
            }

            var result = new GenerationResult
            {
                Success = true,
                LevelData = BuildLevel(request),
                Tasks = BuildTasks(request),
                GenerationTime = (float)stopwatch.Elapsed.TotalSeconds
            };
            return result;
        }

        /// <summary> 按请求开关构建演示关卡数据（Day6：实体数量由 propCount 构造参数控制） </summary>
        private LevelData BuildLevel(GenerationRequest request)
        {
            var seed = Math.Abs(request.RandomSeed);
            var level = new LevelData
            {
                LevelName = $"演示关卡-{request.TemplateId ?? "通用"}",
                Description = request.Prompt,
                PlayerStartPosition = Vector3.zero,
                Terrain = request.GenerateTerrain
                    ? new TerrainData
                    {
                        Width = 80 + seed % 40,
                        Length = 80 + seed % 40,
                        HeightScale = 5f + seed % 8
                    }
                    : null
            };

            if (request.GenerateProps)
            {
                // 逻辑名与 PrefabMapping_Default.asset 映射表一致（敌人-弓箭手/宝箱/NPC）；
                // 前 3 个保持固定演示坐标（与早期版本一致），更多实体按黄金角环状散开（确定性分布，可复现）
                var logicalNames = new[] { "敌人-弓箭手", "宝箱", "NPC" };
                var fixedPositions = new[]
                {
                    new Vector3(5, 0, 5), new Vector3(-5, 0, 8), new Vector3(0, 0, 12)
                };
                for (var i = 0; i < _propCount; i++)
                {
                    level.Props.Add(new PropPlacement
                    {
                        PrefabLogicalName = logicalNames[i % logicalNames.Length],
                        Position = i < fixedPositions.Length ? fixedPositions[i] : ScatterPosition(i)
                    });
                }
            }
            return level;
        }

        /// <summary> 第 i 个扩展实体的散点坐标：黄金角（≈137.5°）保证角度均匀散开，半径按 8 个/圈递增 </summary>
        private static Vector3 ScatterPosition(int i)
        {
            const float goldenAngle = 2.399963f;
            var angle = i * goldenAngle;
            var radius = 12f + (i / 8) * 15f;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        /// <summary> 构建演示任务（1 主任务 + 1 支线） </summary>
        private static List<TaskData> BuildTasks(GenerationRequest request)
        {
            var tasks = new List<TaskData>();
            if (!request.GenerateTasks) return tasks;

            tasks.Add(new TaskData
            {
                TaskID = "T1",
                TaskName = "击败巡逻弓箭手",
                Description = "击败 3 个巡逻弓箭手",
                Type = TaskType.Kill,
                Objective = TaskObjective.Count,
                Reward = new RewardData { Experience = 100, Gold = 50 },
                IsMainTask = true,
                TriggerCondition = "击败敌人"
            });
            tasks.Add(new TaskData
            {
                TaskID = "T2",
                TaskName = "收集宝箱",
                Description = "找到并打开场景中的宝箱",
                Type = TaskType.Collect,
                Objective = TaskObjective.CollectItems,
                Reward = new RewardData { Gold = 30 },
                IsMainTask = false
            });
            return tasks;
        }
    }
}
