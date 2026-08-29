using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using UnityEngine;
// 数据层 TerrainData 与 UnityEngine.TerrainData 同名，别名消除歧义（见 CLAUDE.md 已知坑）
using TerrainData = AILevelGenerator.Runtime.Data.TerrainData;

namespace AILevelGenerator.Runtime.Scheduling
{
    /// <summary>
    /// 模拟生成器（Day5 占位实现，Day6 接入真实 LLM 时替换）。
    /// 演示通道（便于手动验收状态机各路径）：
    ///   - 提示词含"失败" → 返回 Success=false + DEMO_FAIL 校验错误（演示业务失败路径）
    ///   - 提示词含"异常" → 抛出异常（演示 catch → Failed 路径）
    ///   - 其余 → 按请求开关返回演示关卡数据（道具名与 Day4 资源映射表一致）
    /// </summary>
    public class MockGenerator : IGenerator
    {
        private readonly int _delayMilliseconds;

        public MockGenerator(int delayMilliseconds = 1500)
        {
            _delayMilliseconds = Math.Max(0, delayMilliseconds);
        }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request)
        {
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
                        new() { Code = "DEMO_FAIL", Message = "模拟生成器演示失败路径（提示词含“失败”）" }
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

        /// <summary> 按请求开关构建演示关卡数据 </summary>
        private static LevelData BuildLevel(GenerationRequest request)
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
                // 逻辑名与 PrefabMapping_Default.asset 映射表一致（敌人-弓箭手/宝箱/NPC）
                level.Props.Add(new PropPlacement { PrefabLogicalName = "敌人-弓箭手", Position = new Vector3(5, 0, 5) });
                level.Props.Add(new PropPlacement { PrefabLogicalName = "宝箱", Position = new Vector3(-5, 0, 8) });
                level.Props.Add(new PropPlacement { PrefabLogicalName = "NPC", Position = new Vector3(0, 0, 12) });
            }
            return level;
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
