using AILevelGenerator.Runtime.Utilities;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 服务定位器单元测试：注册/获取/覆盖/注销基本行为。
    /// 使用私有测试类型，避免污染 ServiceLocator 中 Editor 端注册的服务
    /// （测试间通过 TearDown 注销本测试类型，不动全局其他注册）。
    /// </summary>
    public class ServiceLocatorTests
    {
        private class TestService { }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Unregister<TestService>();
        }

        [Test]
        public void 注册后_可获取同一实例()
        {
            var service = new TestService();
            ServiceLocator.Register(service);
            Assert.IsTrue(ServiceLocator.IsRegistered<TestService>());
            Assert.AreSame(service, ServiceLocator.Get<TestService>());
        }

        [Test]
        public void 未注册_返回null且IsRegistered为false()
        {
            Assert.IsFalse(ServiceLocator.IsRegistered<TestService>());
            Assert.IsNull(ServiceLocator.Get<TestService>());
        }

        [Test]
        public void 重复注册_覆盖旧实例()
        {
            var first = new TestService();
            var second = new TestService();
            ServiceLocator.Register(first);
            ServiceLocator.Register(second);
            Assert.AreSame(second, ServiceLocator.Get<TestService>());
        }

        [Test]
        public void 注销后_返回null()
        {
            ServiceLocator.Register(new TestService());
            ServiceLocator.Unregister<TestService>();
            Assert.IsFalse(ServiceLocator.IsRegistered<TestService>());
            Assert.IsNull(ServiceLocator.Get<TestService>());
        }
    }
}
