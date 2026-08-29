using System.Collections.Generic;
using AILevelGenerator.Runtime.Scheduling;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 生成任务状态机单元测试：覆盖流转表全部合法/非法路径与事件行为
    /// </summary>
    public class GenerationTaskStateMachineTests
    {
        [Test]
        public void 初始状态_为准备()
        {
            var machine = new GenerationTaskStateMachine();
            Assert.AreEqual(GenerationTaskState.Ready, machine.CurrentState);
        }

        [Test]
        public void 合法流转_准备到生成中()
        {
            var machine = new GenerationTaskStateMachine();
            Assert.IsTrue(machine.TryTransit(GenerationTaskState.Generating));
            Assert.AreEqual(GenerationTaskState.Generating, machine.CurrentState);
        }

        [Test]
        public void 合法流转_生成中到成功()
        {
            var machine = new GenerationTaskStateMachine();
            machine.TryTransit(GenerationTaskState.Generating);
            Assert.IsTrue(machine.TryTransit(GenerationTaskState.Success));
            Assert.AreEqual(GenerationTaskState.Success, machine.CurrentState);
        }

        [Test]
        public void 合法流转_生成中到失败()
        {
            var machine = new GenerationTaskStateMachine();
            machine.TryTransit(GenerationTaskState.Generating);
            Assert.IsTrue(machine.TryTransit(GenerationTaskState.Failed));
            Assert.AreEqual(GenerationTaskState.Failed, machine.CurrentState);
        }

        [Test]
        public void 合法流转_成功或失败_可重置到准备()
        {
            var machine = new GenerationTaskStateMachine();
            machine.TryTransit(GenerationTaskState.Generating);
            machine.TryTransit(GenerationTaskState.Success);
            Assert.IsTrue(machine.TryTransit(GenerationTaskState.Ready));
            Assert.AreEqual(GenerationTaskState.Ready, machine.CurrentState);

            var machine2 = new GenerationTaskStateMachine();
            machine2.TryTransit(GenerationTaskState.Generating);
            machine2.TryTransit(GenerationTaskState.Failed);
            Assert.IsTrue(machine2.TryTransit(GenerationTaskState.Ready));
            Assert.AreEqual(GenerationTaskState.Ready, machine2.CurrentState);
        }

        [Test]
        public void 非法流转_被拒绝且状态不变()
        {
            var machine = new GenerationTaskStateMachine();
            // 准备态：只有"生成中"合法
            Assert.IsFalse(machine.TryTransit(GenerationTaskState.Success));
            Assert.IsFalse(machine.TryTransit(GenerationTaskState.Failed));
            Assert.IsFalse(machine.TryTransit(GenerationTaskState.Ready)); // 自环
            Assert.AreEqual(GenerationTaskState.Ready, machine.CurrentState);

            // 生成中态：不得回退到准备，也不得自环
            machine.TryTransit(GenerationTaskState.Generating);
            Assert.IsFalse(machine.TryTransit(GenerationTaskState.Ready));
            Assert.IsFalse(machine.TryTransit(GenerationTaskState.Generating));
            Assert.AreEqual(GenerationTaskState.Generating, machine.CurrentState);

            // 成功态：不得直接跳到生成中/失败
            machine.TryTransit(GenerationTaskState.Success);
            Assert.IsFalse(machine.TryTransit(GenerationTaskState.Generating));
            Assert.IsFalse(machine.TryTransit(GenerationTaskState.Failed));
            Assert.AreEqual(GenerationTaskState.Success, machine.CurrentState);
        }

        [Test]
        public void 非法流转_不触发状态变更事件()
        {
            var machine = new GenerationTaskStateMachine();
            var events = new List<GenerationTaskState>();
            machine.StateChanged += s => events.Add(s);

            machine.TryTransit(GenerationTaskState.Success); // 从准备直接到成功：非法
            Assert.IsEmpty(events);
        }

        [Test]
        public void 状态变更事件_每次合法流转触发一次且参数为新状态()
        {
            var machine = new GenerationTaskStateMachine();
            var events = new List<GenerationTaskState>();
            machine.StateChanged += s => events.Add(s);

            machine.TryTransit(GenerationTaskState.Generating);
            machine.TryTransit(GenerationTaskState.Success);
            machine.TryTransit(GenerationTaskState.Ready);

            Assert.AreEqual(
                new[] { GenerationTaskState.Generating, GenerationTaskState.Success, GenerationTaskState.Ready },
                events);
        }
    }
}
