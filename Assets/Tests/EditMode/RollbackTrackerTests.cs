using AILevelGenerator.Runtime.Utilities;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 回滚追踪器单元测试（Day3）：登记去重、取最近登记、空安全、清空。
    /// 追踪逻辑独立于删除执行（删除在 Editor 侧 RollbackManager 分帧完成），因此可纯单测。
    /// </summary>
    public class RollbackTrackerTests
    {
        [Test]
        public void 登记根物体_可取出最近一次()
        {
            var tracker = new RollbackTracker();
            var a = new GameObject("A");
            var b = new GameObject("B");

            tracker.Track(a);
            tracker.Track(b);

            Assert.AreEqual(2, tracker.Count);
            Assert.AreSame(b, tracker.TakeLast(), "应取出最近登记的根（后进先出）");
            Assert.AreSame(a, tracker.TakeLast());
            Assert.AreEqual(0, tracker.Count, "取出后登记清空");
        }

        [Test]
        public void 登记null与重复_被忽略()
        {
            var tracker = new RollbackTracker();
            var a = new GameObject("A");

            tracker.Track(null);
            tracker.Track(a);
            tracker.Track(a); // 重复登记忽略

            Assert.AreEqual(1, tracker.Count);
        }

        [Test]
        public void 无登记时取出_返回null不抛异常()
        {
            var tracker = new RollbackTracker();
            Assert.IsNull(tracker.TakeLast());
            Assert.AreEqual(0, tracker.Count);
        }

        [Test]
        public void 清空_全部登记移除()
        {
            var tracker = new RollbackTracker();
            tracker.Track(new GameObject("A"));
            tracker.Track(new GameObject("B"));

            tracker.Clear();

            Assert.AreEqual(0, tracker.Count);
            Assert.IsNull(tracker.TakeLast(), "清空后取出应为 null");
        }
    }
}
