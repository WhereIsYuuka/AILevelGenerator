using AILevelGenerator.Runtime.Utilities;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// NavMesh 烘焙状态追踪器单元测试（Day5）：状态流转与提示文案。
    /// 追踪器为纯逻辑（状态记录），烘焙执行体在编辑器侧（NavMeshBaker），端到端覆盖。
    /// </summary>
    public class NavMeshBakeTrackerTests
    {
        [Test]
        public void 初始状态_为Ready且文案为未烘焙()
        {
            var tracker = new NavMeshBakeTracker();
            Assert.AreEqual(NavMeshBakeState.Ready, tracker.State);
            Assert.AreEqual("NavMesh 未烘焙", tracker.Message);
        }

        [Test]
        public void BeginBaking_进入烘焙中状态()
        {
            var tracker = new NavMeshBakeTracker();
            tracker.BeginBaking();
            Assert.AreEqual(NavMeshBakeState.Baking, tracker.State);
            Assert.IsTrue(tracker.Message.Contains("烘焙"), "烘焙中提示文案应含「烘焙」关键词");
        }

        [Test]
        public void Complete_进入完成状态且记录几何源数()
        {
            var tracker = new NavMeshBakeTracker();
            tracker.BeginBaking();
            tracker.Complete(42);
            Assert.AreEqual(NavMeshBakeState.Completed, tracker.State);
            Assert.AreEqual(42, tracker.SourceCount);
            Assert.IsTrue(tracker.Message.Contains("42"), "完成文案应包含几何源数量");
        }

        [Test]
        public void Fail_进入失败状态且记录原因()
        {
            var tracker = new NavMeshBakeTracker();
            tracker.BeginBaking();
            tracker.Fail("未收集到任何几何");
            Assert.AreEqual(NavMeshBakeState.Failed, tracker.State);
            Assert.AreEqual(0, tracker.SourceCount);
            Assert.IsTrue(tracker.Message.Contains("未收集到任何几何"), "失败文案应包含失败原因");
        }

        [Test]
        public void 失败后再次烘焙_状态可流转回烘焙中()
        {
            var tracker = new NavMeshBakeTracker();
            tracker.Fail("某原因");
            tracker.BeginBaking();
            Assert.AreEqual(NavMeshBakeState.Baking, tracker.State, "失败后应可重新发起烘焙");
        }
    }
}
