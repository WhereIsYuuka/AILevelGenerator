using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AILevelGenerator.Runtime.Data;
using AILevelGenerator.Runtime.Interfaces;
using AILevelGenerator.Runtime.Scheduling;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 生成调度器单元测试：覆盖成功/失败/异常/忙碌拒绝/参数校验/日志输出。
    /// 全部使用已完成 Task 的假生成器（Task.FromResult/FromException），
    /// await 同步内联完成，零等待、完全确定，规避异步测试 flaky。
    /// </summary>
    public class GeneratorSchedulerTests
    {
        /// <summary> 假生成器：按配置返回已完成 Task（或挂起 TCS 用于模拟"生成中"） </summary>
        private class FakeGenerator : IGenerator
        {
            public Func<GenerationRequest, Task<GenerationResult>> Handler;

            public Task<GenerationResult> GenerateAsync(GenerationRequest request) => Handler(request);
        }

        private static GenerationRequest CreateRequest() => new GenerationRequest
        {
            Prompt = "森林营地，3个巡逻弓箭手，1个宝箱",
            TemplateId = "战斗关卡",
            RandomSeed = 42
        };

        private static GenerationResult CreateSuccessResult() => new GenerationResult
        {
            Success = true,
            LevelData = new LevelData { LevelName = "测试关卡" },
            GenerationTime = 1.5f
        };

        [Test]
        public async Task 成功路径_状态流转到成功且输出状态与成功日志()
        {
            var scheduler = new GeneratorScheduler(new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            var logger = new TestLogger();
            scheduler.SetLogger(logger);
            var states = new List<GenerationTaskState>();
            scheduler.StateChanged += s => states.Add(s);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState);
            Assert.AreEqual(
                new[] { GenerationTaskState.Generating, GenerationTaskState.Success },
                states, "状态序列应为 生成中 → 成功");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("状态流转")), "应输出状态流转日志");
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[SUCCESS]") && m.Contains("生成成功")), "应输出成功日志");
        }

        [Test]
        public async Task 业务失败路径_状态流转到失败且输出失败日志()
        {
            var failedResult = new GenerationResult
            {
                Success = false,
                Errors = new List<ValidationError>
                {
                    new() { Code = "DEMO_FAIL", Message = "演示失败原因" }
                }
            };
            var scheduler = new GeneratorScheduler(new FakeGenerator { Handler = _ => Task.FromResult(failedResult) });
            var logger = new TestLogger();
            scheduler.SetLogger(logger);
            var states = new List<GenerationTaskState>();
            scheduler.StateChanged += s => states.Add(s);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);
            Assert.AreEqual(
                new[] { GenerationTaskState.Generating, GenerationTaskState.Failed },
                states);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("生成失败") && m.Contains("演示失败原因")),
                "应输出包含错误明细的失败日志");
        }

        [Test]
        public async Task 生成器抛异常_状态流转到失败且输出异常日志()
        {
            var scheduler = new GeneratorScheduler(
                new FakeGenerator { Handler = _ => Task.FromException<GenerationResult>(new Exception("boom")) });
            var logger = new TestLogger();
            scheduler.SetLogger(logger);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("生成异常") && m.Contains("boom")),
                "应输出异常日志");
        }

        [Test]
        public async Task 生成器返回null结果_判定为失败()
        {
            var scheduler = new GeneratorScheduler(new FakeGenerator { Handler = _ => Task.FromResult<GenerationResult>(null) });
            var logger = new TestLogger();
            scheduler.SetLogger(logger);

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("未返回成功结果")), "应输出 null 结果的失败原因");
        }

        [Test]
        public async Task 空请求null_停留在准备且输出错误日志()
        {
            var scheduler = new GeneratorScheduler(new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            var logger = new TestLogger();
            scheduler.SetLogger(logger);

            await scheduler.StartGenerationAsync(null);

            Assert.AreEqual(GenerationTaskState.Ready, scheduler.CurrentState, "调用方 bug 不应触发状态流转");
            Assert.IsFalse(scheduler.IsBusy);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("生成请求为空")), "应输出错误日志");
        }

        [Test]
        public async Task 空白提示词_停留在准备()
        {
            var scheduler = new GeneratorScheduler(new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            var logger = new TestLogger();
            scheduler.SetLogger(logger);

            var request = CreateRequest();
            request.Prompt = "   ";
            await scheduler.StartGenerationAsync(request);

            Assert.AreEqual(GenerationTaskState.Ready, scheduler.CurrentState);
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[ERROR]") && m.Contains("缺少描述")), "应输出缺描述错误");
        }

        [Test]
        public async Task 生成中_重复请求被忽略且状态不变()
        {
            // 挂起的 TCS 让生成停在 Generating，模拟 LLM 调用中
            var tcs = new TaskCompletionSource<GenerationResult>();
            var scheduler = new GeneratorScheduler(new FakeGenerator { Handler = _ => tcs.Task });
            var logger = new TestLogger();
            scheduler.SetLogger(logger);

            var firstTask = scheduler.StartGenerationAsync(CreateRequest());
            // 同步前缀保证 Generating 已置位（无需等待）
            Assert.IsTrue(scheduler.IsBusy, "发起后应立即处于生成中");
            Assert.AreEqual(GenerationTaskState.Generating, scheduler.CurrentState);

            await scheduler.StartGenerationAsync(CreateRequest()); // 忙碌中被拒，同步返回
            Assert.IsTrue(logger.Messages.Exists(m => m.Contains("[WARN]") && m.Contains("已有生成任务进行中")), "应输出忙碌警告");
            Assert.AreEqual(GenerationTaskState.Generating, scheduler.CurrentState, "忙碌时重复请求不应改变状态");

            tcs.SetResult(CreateSuccessResult());
            await firstTask; // 收尾：驱动首次请求完成
            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState);
        }

        [Test]
        public async Task 成功后可发起新一轮_先重置到准备再流转()
        {
            var scheduler = new GeneratorScheduler(new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });
            var states = new List<GenerationTaskState>();
            scheduler.StateChanged += s => states.Add(s);

            await scheduler.StartGenerationAsync(CreateRequest());
            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState);

            await scheduler.StartGenerationAsync(CreateRequest()); // 第二轮

            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState);
            Assert.AreEqual(
                new[] { GenerationTaskState.Generating, GenerationTaskState.Success,
                        GenerationTaskState.Ready, GenerationTaskState.Generating, GenerationTaskState.Success },
                states, "第二轮应先重置到准备再走 生成中→成功");
        }

        [Test]
        public async Task 失败后可发起新一轮_先重置到准备()
        {
            var scheduler = new GeneratorScheduler(
                new FakeGenerator { Handler = _ => Task.FromException<GenerationResult>(new Exception("boom")) });
            var states = new List<GenerationTaskState>();
            scheduler.StateChanged += s => states.Add(s);

            await scheduler.StartGenerationAsync(CreateRequest());
            Assert.AreEqual(GenerationTaskState.Failed, scheduler.CurrentState);

            await scheduler.StartGenerationAsync(CreateRequest()); // 第二轮（仍失败）

            Assert.AreEqual(
                new[] { GenerationTaskState.Generating, GenerationTaskState.Failed,
                        GenerationTaskState.Ready, GenerationTaskState.Generating, GenerationTaskState.Failed },
                states, "失败后新一轮应先重置到准备");
        }

        [Test]
        public async Task 未注入日志器_不抛异常且状态正常流转()
        {
            var scheduler = new GeneratorScheduler(new FakeGenerator { Handler = _ => Task.FromResult(CreateSuccessResult()) });

            await scheduler.StartGenerationAsync(CreateRequest());

            Assert.AreEqual(GenerationTaskState.Success, scheduler.CurrentState);
        }

        [Test]
        public void 构造参数为空_抛出参数异常()
        {
            Assert.Throws<ArgumentNullException>(() => new GeneratorScheduler(null));
        }
    }
}
