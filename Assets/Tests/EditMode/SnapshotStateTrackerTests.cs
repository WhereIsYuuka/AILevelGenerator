using AILevelGenerator.Runtime.Utilities;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 场景快照状态追踪器单元测试（第四周-Day1）：
    /// 生命周期流转（创建/回滚/丢弃）、未创建时拒绝、重复创建覆盖、元数据记录。
    /// 追踪逻辑独立于文件操作（保存/重载在 Editor 侧 SceneSnapshotManager 执行），因此可纯单测。
    /// </summary>
    public class SnapshotStateTrackerTests
    {
        private const string SnapshotPath = "Assets/Temp/GenerateSnapshot.unity";
        private const string ScenePath = "Assets/Scenes/ToolScene.unity";

        [Test]
        public void 初始状态_无快照()
        {
            var tracker = new SnapshotStateTracker();
            Assert.IsFalse(tracker.HasSnapshot);
            Assert.IsNull(tracker.SnapshotPath);
            Assert.IsNull(tracker.OriginalScenePath);
        }

        [Test]
        public void 创建快照_登记元数据()
        {
            var tracker = new SnapshotStateTracker();
            Assert.IsTrue(tracker.Create(ScenePath, SnapshotPath, wasSceneDirty: true));

            Assert.IsTrue(tracker.HasSnapshot);
            Assert.AreEqual(SnapshotPath, tracker.SnapshotPath);
            Assert.AreEqual(ScenePath, tracker.OriginalScenePath);
            Assert.IsTrue(tracker.WasSceneDirty);
        }

        [Test]
        public void 快照路径为空_创建失败且状态不变()
        {
            var tracker = new SnapshotStateTracker();
            Assert.IsFalse(tracker.Create(ScenePath, null, wasSceneDirty: false));
            Assert.IsFalse(tracker.Create(ScenePath, "", wasSceneDirty: false));
            Assert.IsFalse(tracker.HasSnapshot);
        }

        [Test]
        public void 重复创建_视为覆盖重新登记()
        {
            var tracker = new SnapshotStateTracker();
            tracker.Create(ScenePath, SnapshotPath, wasSceneDirty: false);
            tracker.Create("Assets/Scenes/Other.unity", "Temp/Other.unity", wasSceneDirty: true);

            Assert.IsTrue(tracker.HasSnapshot);
            Assert.AreEqual("Temp/Other.unity", tracker.SnapshotPath);
            Assert.AreEqual("Assets/Scenes/Other.unity", tracker.OriginalScenePath);
            Assert.IsTrue(tracker.WasSceneDirty);
        }

        [Test]
        public void 创建快照_记录NavMesh存在标记()
        {
            var tracker = new SnapshotStateTracker();
            Assert.IsFalse(tracker.HasNavMeshData, "默认无烘焙数据");

            tracker.Create(ScenePath, SnapshotPath, wasSceneDirty: false, hasNavMeshData: true);
            Assert.IsTrue(tracker.HasNavMeshData);

            tracker.TryRollback();
            Assert.IsFalse(tracker.HasNavMeshData, "回滚消费后标记归零");
        }

        [Test]
        public void 回滚_消费快照并归零状态()
        {
            var tracker = new SnapshotStateTracker();
            tracker.Create(ScenePath, SnapshotPath, wasSceneDirty: true);

            Assert.IsTrue(tracker.TryRollback());
            Assert.IsFalse(tracker.HasSnapshot);
            Assert.IsNull(tracker.SnapshotPath);
            Assert.IsNull(tracker.OriginalScenePath);
        }

        [Test]
        public void 丢弃_消费快照并归零状态()
        {
            var tracker = new SnapshotStateTracker();
            tracker.Create(ScenePath, SnapshotPath, wasSceneDirty: false);

            Assert.IsTrue(tracker.TryDiscard());
            Assert.IsFalse(tracker.HasSnapshot);
        }

        [Test]
        public void 未创建时回滚与丢弃_被拒绝()
        {
            var tracker = new SnapshotStateTracker();
            Assert.IsFalse(tracker.TryRollback());
            Assert.IsFalse(tracker.TryDiscard());
        }

        [Test]
        public void 已回滚后_不可重复回滚()
        {
            var tracker = new SnapshotStateTracker();
            tracker.Create(ScenePath, SnapshotPath, wasSceneDirty: false);
            Assert.IsTrue(tracker.TryRollback());
            Assert.IsFalse(tracker.TryRollback(), "快照已被消费，二次回滚应失败");
            Assert.IsFalse(tracker.TryDiscard(), "快照已被消费，二次丢弃应失败");
        }
    }
}
