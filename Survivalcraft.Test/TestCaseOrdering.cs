using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Survivalcraft.Test;

// 定义一个可以标记在测试方法上的属性，用于指定优先级（数字越小越先执行）
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class TestOrderAttribute : Attribute {
    public int Priority { get; set; }
}

// 实现 xUnit 的测试用例排序接口
public class PriorityOrderer : ITestCaseOrderer {
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase {
        var sortedMethods = new SortedDictionary<int, List<TTestCase>>();

        foreach (TTestCase testCase in testCases) {
            int priority = int.MaxValue; // 默认优先级最低

            // 查找该方法上是否有 [TestOrder] 属性
            var attr = testCase.TestMethod.Method.GetCustomAttributes(typeof(TestOrderAttribute).AssemblyQualifiedName).FirstOrDefault();
            if (attr != null) {
                priority = attr.GetNamedArgument<int>(nameof(TestOrderAttribute.Priority));
            }

            if (!sortedMethods.TryGetValue(priority, out var list)) {
                list = new List<TTestCase>();
                sortedMethods[priority] = list;
            }
            list.Add(testCase);
        }

        // 按优先级顺序返回测试用例（如果优先级一样，按方法名首字母排）
        foreach (var list in sortedMethods.Values) {
            list.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.TestMethod.Method.Name, y.TestMethod.Method.Name));
            foreach (TTestCase testCase in list) {
                yield return testCase;
            }
        }
    }
}
