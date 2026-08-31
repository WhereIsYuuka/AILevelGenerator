using System.Collections.Generic;
using AILevelGenerator.Runtime.Utilities;
using NUnit.Framework;
using UnityEngine;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 场景指纹单元测试（第四周-Day1）：「回滚后场景 100% 恢复」的度量工具。
    /// 测试基于相对变化断言（不依赖编辑器当前场景基线）：
    /// 同结构指纹一致、增删物体/改组件/变层级指纹变化、子树计算只含子树条目。
    /// </summary>
    public class SceneFingerprintTests
    {
        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        private GameObject Spawn(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        [Test]
        public void 删除物体后重建同结构_指纹完全一致()
        {
            var before = SceneFingerprint.Compute();

            // 构造：根 A → 子 B/C，各带一个组件差异
            var a = Spawn("RootA");
            var b = new GameObject("ChildB");
            b.transform.SetParent(a.transform);
            var c = new GameObject("ChildC");
            c.transform.SetParent(a.transform);
            _created.Add(b);
            _created.Add(c);
            b.AddComponent<Rigidbody>();

            var with = SceneFingerprint.Compute();
            Assert.AreNotEqual(before, with, "构造物体后指纹必须变化");

            // 清空重建同样结构（模拟回滚后重建）
            foreach (var go in _created) UnityEngine.Object.DestroyImmediate(go);
            _created.Clear();
            var a2 = Spawn("RootA");
            var b2 = new GameObject("ChildB");
            b2.transform.SetParent(a2.transform);
            var c2 = new GameObject("ChildC");
            c2.transform.SetParent(a2.transform);
            _created.Add(b2);
            _created.Add(c2);
            b2.AddComponent<Rigidbody>();

            var after = SceneFingerprint.Compute();
            Assert.AreEqual(with, after, "相同层级与组件结构应产出完全相同的指纹");
        }

        [Test]
        public void 删除物体后清空_指纹恢复原状()
        {
            var before = SceneFingerprint.Compute();
            Spawn("TempObject");

            Assert.AreNotEqual(before, SceneFingerprint.Compute());

            foreach (var go in _created) UnityEngine.Object.DestroyImmediate(go);
            _created.Clear();

            Assert.AreEqual(before, SceneFingerprint.Compute(), "删除全部新增物体后指纹应恢复");
        }

        [Test]
        public void 组件增减_指纹变化()
        {
            var go = Spawn("FingerprintTarget");
            var baseline = SceneFingerprint.Compute(go);

            go.AddComponent<Rigidbody>();
            var withRigidbody = SceneFingerprint.Compute(go);
            Assert.AreNotEqual(baseline, withRigidbody, "增加组件后指纹必须变化");

            UnityEngine.Object.DestroyImmediate(go.GetComponent<Rigidbody>());
            Assert.AreEqual(baseline, SceneFingerprint.Compute(go), "移除组件后指纹应恢复");
        }

        [Test]
        public void 层级调整_指纹变化()
        {
            var parent = Spawn("ParentObj");
            var child = Spawn("ChildObj");
            child.transform.SetParent(parent.transform);

            var before = SceneFingerprint.Compute();

            child.transform.SetParent(null); // 解除父子关系
            var after = SceneFingerprint.Compute();
            Assert.AreNotEqual(before, after, "父子关系变化必须体现在指纹中");

            child.transform.SetParent(parent.transform);
            Assert.AreEqual(before, SceneFingerprint.Compute(), "恢复父子关系后指纹应还原");
        }

        [Test]
        public void 子树指纹_只包含根下条目()
        {
            var root = Spawn("SubRoot");
            var inner = new GameObject("Inner");
            inner.transform.SetParent(root.transform);
            _created.Add(inner);

            var fp = SceneFingerprint.Compute(root);
            Assert.IsTrue(fp.Contains("SubRoot|"), "子树指纹必须包含根条目");
            Assert.IsTrue(fp.Contains("SubRoot/Inner|"), "子树指纹必须包含子物体条目");
            Assert.IsFalse(fp.Contains("Main Camera"), "子树指纹不得包含根外的场景物体");

            // 全场景指纹必须包含子树指纹的所有条目
            var full = SceneFingerprint.Compute();
            foreach (var line in fp.Split('\n'))
            {
                Assert.IsTrue(full.Contains(line), $"全场景指纹应包含子树条目：{line}");
            }
        }

        [Test]
        public void 空根计算_返回空串不抛异常()
        {
            Assert.AreEqual(string.Empty, SceneFingerprint.Compute(null));
        }
    }
}
