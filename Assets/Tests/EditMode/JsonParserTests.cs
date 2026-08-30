using AILevelGenerator.Runtime.Parsing;
using NUnit.Framework;

namespace AILevelGenerator.Tests.EditMode
{
    /// <summary>
    /// 零依赖 JSON 解析器单元测试：裸 JSON / 提取（代码块围栏、前后杂文）/ 修复（尾逗号、控制字符）/ 失败边界 / 容错类型转换。
    /// </summary>
    public class JsonParserTests
    {
        // —— 基础解析 ——

        [Test]
        public void 解析_裸JSON对象_字段类型齐全()
        {
            var root = JsonParser.Parse("{\"name\":\"森林营地\",\"count\":3,\"ratio\":1.5,\"ok\":true,\"none\":null}");

            Assert.IsTrue(root.IsObject);
            Assert.AreEqual("森林营地", root.GetString("name", null));
            Assert.AreEqual(3, root.GetInt("count", -1));
            Assert.AreEqual(1.5f, root.GetFloat("ratio", -1f));
            Assert.IsTrue(root.GetBool("ok", false));
            var none = root.Get("none");
            Assert.IsNotNull(none, "null 值也应解析为节点");
            Assert.AreEqual(JsonValue.Kind.Null, none.ValueKind);
        }

        [Test]
        public void 解析_嵌套数组与对象()
        {
            var root = JsonParser.Parse(
                "{\"list\":[{\"a\":1},{\"a\":2}],\"grid\":[[1,2],[3,4]],\"empty\":{}}");

            Assert.IsTrue(root.Get("list").IsArray);
            Assert.AreEqual(2, root.Get("list").ArrayValue.Count);
            Assert.AreEqual(1, root.Get("list").GetAt(0).GetInt("a", -1));
            Assert.AreEqual(2, root.Get("list").GetAt(1).GetInt("a", -1));
            Assert.AreEqual(4, root.Get("grid").GetAt(1).GetAt(1).AsInt(-1));
            Assert.IsTrue(root.Get("empty").IsObject);
        }

        [Test]
        public void 解析_字符串转义_还原转义字符()
        {
            var root = JsonParser.Parse("{\"s\":\"a\\\"b\\\\c\\n\\t\\u4f60\"}");

            Assert.AreEqual("a\"b\\c\n\t你", root.GetString("s", null));
        }

        [Test]
        public void 解析_数字格式_负数小数指数()
        {
            var root = JsonParser.Parse("{\"a\":-42,\"b\":0.5,\"c\":1e3,\"d\":-2.5e-2}");

            Assert.AreEqual(-42, root.GetInt("a", 0));
            Assert.AreEqual(0.5f, root.GetFloat("b", 0f));
            Assert.AreEqual(1000f, root.GetFloat("c", 0f));
            Assert.AreEqual(-0.025f, root.GetFloat("d", 0f));
        }

        [Test]
        public void 解析_顶层数组()
        {
            var root = JsonParser.Parse("[1,\"two\",true]");

            Assert.IsTrue(root.IsArray);
            Assert.AreEqual(1, root.GetAt(0).AsInt(0));
            Assert.AreEqual("two", root.GetAt(1).AsString(null));
            Assert.IsTrue(root.GetAt(2).AsBool(false));
        }

        // —— 提取（容错） ——

        [Test]
        public void 提取_代码块围栏剥离()
        {
            var root = JsonParser.Parse("```json\n{\"name\":\"森林营地\"}\n```");

            Assert.AreEqual("森林营地", root.GetString("name", null));
        }

        [Test]
        public void 提取_前后杂文截取()
        {
            var root = JsonParser.Parse("好的，以下是设计结果：\n{\"name\":\"营地\"}\n请查收！");

            Assert.AreEqual("营地", root.GetString("name", null));
        }

        [Test]
        public void 提取_字符串内括号不影响配对()
        {
            // 配对扫描必须跳过字符串内的 { } [ ]
            var root = JsonParser.Parse("{\"desc\":\"包含{花括号}和[中括号]的文本\",\"x\":1}");

            Assert.AreEqual("包含{花括号}和[中括号]的文本", root.GetString("desc", null));
            Assert.AreEqual(1, root.GetInt("x", -1));
        }

        // —— 修复（容错） ——

        [Test]
        public void 修复_对象与数组尾逗号()
        {
            var root = JsonParser.Parse("{\"a\":1,}");

            Assert.AreEqual(1, root.GetInt("a", -1));

            var arr = JsonParser.Parse("[1,2,]");
            Assert.AreEqual(2, arr.ArrayValue.Count);
        }

        [Test]
        public void 修复_重复键_后者覆盖()
        {
            var root = JsonParser.Parse("{\"a\":1,\"a\":2}");

            Assert.AreEqual(2, root.GetInt("a", -1));
        }

        // —— 失败边界 ——

        [Test]
        public void 失败_空文本_抛异常()
        {
            Assert.Throws<JsonParseException>(() => JsonParser.Parse(null));
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("   "));
        }

        [Test]
        public void 失败_无JSON内容_抛异常()
        {
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("这是一段纯文字说明"));
        }

        [Test]
        public void 失败_括号不配对_抛异常()
        {
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\"a\":1"));
        }

        [Test]
        public void 失败_语法错误_抛异常()
        {
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\"a\" 1}"));
        }

        // —— 容错类型转换 ——

        [Test]
        public void 类型转换_字符串数字可转数字与布尔()
        {
            var root = JsonParser.Parse("{\"s\":\"42\",\"f\":\"1.5\",\"b\":\"true\",\"B\":\"False\"}");

            Assert.AreEqual(42, root.GetInt("s", -1));
            Assert.AreEqual(1.5f, root.GetFloat("f", -1f));
            Assert.IsTrue(root.GetBool("b", false));
            Assert.IsFalse(root.GetBool("B", true));
        }

        [Test]
        public void 类型转换_类型不符_返回兜底值()
        {
            var root = JsonParser.Parse("{\"str\":\"你好\",\"obj\":{\"x\":1},\"arr\":[1],\"num\":3.7}");

            Assert.AreEqual(0, root.GetInt("str", 0), "非数字字符串转 int 应返回兜底值");
            Assert.AreEqual(0f, root.GetFloat("obj", 0f), "对象转 float 应返回兜底值");
            Assert.AreEqual(-1, root.Get("arr").GetInt("x", -1), "数组取字段应返回兜底值");
            Assert.AreEqual(3, root.GetInt("num", 0), "小数转 int 应截断");
        }

        // —— 序列化转义 ——

        [Test]
        public void EscapeString_特殊字符正确转义()
        {
            Assert.AreEqual("a\\\"b\\\\c\\nd", JsonParser.EscapeString("a\"b\\c\nd"));
            Assert.AreEqual("", JsonParser.EscapeString(null));
            Assert.AreEqual("中文ok", JsonParser.EscapeString("中文ok"));
        }
    }
}
