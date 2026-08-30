using AILevelGenerator.Runtime.Utilities;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// NavMesh 烘焙源过滤器单元测试（Week3-Day5）：实体自身不应作为 NavMesh 障碍物。
    /// 判定纯逻辑（IsUnderRoot）；剔除执行在编辑器侧 NavMeshBaker（端到端验证）。
    /// </summary>
    public class NavMeshSourceFilterTests
    {
        [Test]
        public void 源属于排除根下_判定应剔除()
        {
            var root = new GameObject("Root");
            var child = new GameObject("Child");
            child.transform.SetParent(root.transform);
            var collider = child.AddComponent<BoxCollider>();
            var source = new NavMeshBuildSource { component = collider };

            Assert.IsTrue(NavMeshSourceFilter.IsUnderRoot(source, root.transform),
                "根层级下的 Collider 几何源应判定为需剔除");

            Object.DestroyImmediate(root);
        }

        [Test]
        public void 源不属于排除根_判定不剔除()
        {
            var root = new GameObject("Root");
            var other = new GameObject("Other");
            var collider = other.AddComponent<BoxCollider>();
            var source = new NavMeshBuildSource { component = collider };

            Assert.IsFalse(NavMeshSourceFilter.IsUnderRoot(source, root.transform),
                "根层级之外的 Collider 几何源不应剔除");

            Object.DestroyImmediate(root);
            Object.DestroyImmediate(other);
        }

        [Test]
        public void 排除根为空_判定不剔除()
        {
            var go = new GameObject("Any");
            var collider = go.AddComponent<BoxCollider>();
            var source = new NavMeshBuildSource { component = collider };

            Assert.IsFalse(NavMeshSourceFilter.IsUnderRoot(source, null),
                "未指定排除根时不剔除任何源");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void 源组件非碰撞体_判定不剔除()
        {
            var root = new GameObject("Root");
            var child = new GameObject("Child");
            child.transform.SetParent(root.transform);
            // component 指向非 Collider 组件（Transform）：不应剔除（Collider 才是可烘焙几何）
            var source = new NavMeshBuildSource { component = child.transform };

            Assert.IsFalse(NavMeshSourceFilter.IsUnderRoot(source, root.transform));

            Object.DestroyImmediate(root);
        }

        [Test]
        public void 多层嵌套下的源_判定剔除()
        {
            var root = new GameObject("Root");
            var mid = new GameObject("Mid");
            mid.transform.SetParent(root.transform);
            var leaf = new GameObject("Leaf");
            leaf.transform.SetParent(mid.transform);
            var collider = leaf.AddComponent<BoxCollider>();
            var source = new NavMeshBuildSource { component = collider };

            Assert.IsTrue(NavMeshSourceFilter.IsUnderRoot(source, root.transform),
                "深层嵌套（IsChildOf 语义）也应判定为需剔除");

            Object.DestroyImmediate(root);
        }
    }
}
