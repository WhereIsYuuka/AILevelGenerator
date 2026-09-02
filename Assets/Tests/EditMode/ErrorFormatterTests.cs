using AILevelGenerator.Runtime.Diagnostics;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 错误信息统一格式化测试（第四周-Day5「统一错误码与错误信息规范」）：
    /// Format 输出与既有日志格式逐字兼容（调度器汇总行与旧测试断言依赖）；
    /// 未知码/空入参安全降级（日志链路永不中断）。
    /// </summary>
    public class ErrorFormatterTests
    {
        [Test]
        public void Format_完整入参_输出统一格式()
        {
            Assert.That(ErrorFormatter.Format("REQUEST_PROMPT_EMPTY", "生成描述为空", "prompt"),
                Is.EqualTo("REQUEST_PROMPT_EMPTY：生成描述为空（prompt）"));
        }

        [Test]
        public void Format_无定位路径_省略括号段()
        {
            Assert.That(ErrorFormatter.Format("LLM_ERROR", "生成失败"),
                Is.EqualTo("LLM_ERROR：生成失败"));
        }

        [Test]
        public void Format_空错误码_降级为UNKNOWN()
        {
            Assert.That(ErrorFormatter.Format(null, "消息"), Is.EqualTo("UNKNOWN：消息"));
            Assert.That(ErrorFormatter.Format("", "消息"), Is.EqualTo("UNKNOWN：消息"));
        }

        [Test]
        public void Format_空消息_降级为占位文案()
        {
            Assert.That(ErrorFormatter.Format("DATA_NULL", null), Is.EqualTo("DATA_NULL：无错误信息"));
        }

        [Test]
        public void Format_与旧格式逐字兼容_调度器汇总行依赖()
        {
            // 调度器失败汇总：code：message（dataPath）——旧测试断言 Contains("REQUEST_PROMPT_EMPTY：...") 即依赖此格式
            var oldStyle = $"REQUEST_PROMPT_EMPTY：生成请求缺少描述，已取消（prompt）";
            Assert.That(ErrorFormatter.Format("REQUEST_PROMPT_EMPTY", "生成请求缺少描述，已取消", "prompt"),
                Is.EqualTo(oldStyle));
        }

        [Test]
        public void FormatDetailed_已知码_追加解决建议()
        {
            var formatted = ErrorFormatter.FormatDetailed("RESOURCE_NOT_FOUND", "资源不存在：宝箱", "props[0].prefabLogicalName");
            Assert.That(formatted, Does.StartWith("RESOURCE_NOT_FOUND：资源不存在：宝箱（props[0].prefabLogicalName）"));
            Assert.That(formatted, Does.Contain("建议："));
        }

        [Test]
        public void FormatDetailed_未知码_不追加建议()
        {
            Assert.That(ErrorFormatter.FormatDetailed("UNKNOWN_CODE", "消息"), Is.EqualTo("UNKNOWN_CODE：消息"));
        }

        [Test]
        public void GetHint_已知码_返回目录建议()
        {
            var hint = ErrorFormatter.GetHint(ErrorCodes.REQUEST_PROMPT_EMPTY);
            Assert.That(hint, Is.Not.Null.And.Not.Empty);
            Assert.That(hint, Does.Contain("描述")); // 中文建议应提及修复方向
        }

        [Test]
        public void GetHint_未知码或空_返回null()
        {
            Assert.That(ErrorFormatter.GetHint("UNKNOWN_CODE"), Is.Null);
            Assert.That(ErrorFormatter.GetHint(null), Is.Null);
        }
    }
}
