using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq.Expressions;

namespace CloudFabric.Libraries.Search.Test
{
    public class TestRequest
    {
        public int? ValueProperty { get; set; }
        public int? ValueField;

        public int? ValueMethod ()
        {
            return ValueField;
        }

        public string ValueMethodWithArgs(string value1, int value2)
        {
            return $"{value1}{value2}";
        }

        public static string StaticMethod(string arg)
        {
            return $"test{arg}";
        }
    }

    public class TestModel
    {
        public string Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }

        public int? TestNullableInt { get; set; }

        public TestEnum TestEnumProperty { get; set; }
    }

    public enum TestEnum
    {
        One = 1,
        Two = 2
    }

    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            var filter = Filter.Where<TestModel>(o => o.IsActive == true);

            Assert.IsTrue(filter.PropertyName == "IsActive");

            var filter2 = Filter.Where<TestModel>(o => o.IsActive == true && o.Id == "123");

            Assert.IsTrue(filter2.PropertyName == "IsActive");
            Assert.IsTrue((bool)filter2.Value == true);
            Assert.IsTrue(filter2.Filters[0].Logic == FilterLogic.And);
            Assert.IsTrue(filter2.Filters[0].Filter.PropertyName == "Id");
            Assert.IsTrue(filter2.Filters[0].Filter.Value.ToString() == "123");

            var filter3 = Filter.Where<TestModel>(o => o.IsActive == true && (o.Id == "123" || o.Name == "456"));

            Assert.IsTrue(filter3.PropertyName == "IsActive");
            Assert.IsTrue((bool)filter3.Value == true);
        }

        [TestMethod]
        public void TestNullableInt()
        {
            var filter = Filter.Where<TestModel>(o => o.TestNullableInt == 3);

            Assert.IsTrue(filter.PropertyName == "TestNullableInt");
            Assert.IsTrue((int)filter.Value == 3);

            var value = 4 + 1;
            var filter2 = Filter.Where<TestModel>(o => o.TestNullableInt == value);

            Assert.IsTrue(filter2.PropertyName == "TestNullableInt");
            Assert.IsTrue((int)filter2.Value == 5);
        }

        [TestMethod]
        public void TestConversionNullableIntAsProperty()
        {
            var testRequest = new TestRequest()
            {
                ValueProperty = 5
            };

            var filter = Filter.Where<TestModel>(o => o.TestNullableInt == (int)testRequest.ValueProperty);

            Assert.IsTrue(filter.PropertyName == "TestNullableInt");
            Assert.IsTrue((int)filter.Value == 5);
        }

        [TestMethod]
        public void TestConversionNullableIntAsField()
        {
            var testRequest = new TestRequest()
            {
                ValueField = 5
            };

            var filter = Filter.Where<TestModel>(o => o.TestNullableInt == (int)testRequest.ValueField);

            Assert.IsTrue(filter.PropertyName == "TestNullableInt");
            Assert.IsTrue((int)filter.Value == 5);
        }

        [TestMethod]
        public void TestConversionNullableIntAsMethod()
        {
            var testRequest = new TestRequest()
            {
                ValueField = 5
            };

            var filter = Filter.Where<TestModel>(o => o.TestNullableInt == (int)testRequest.ValueMethod());

            Assert.IsTrue(filter.PropertyName == "TestNullableInt");
            Assert.IsTrue((int)filter.Value == 5);
        }

        [TestMethod]
        public void TestMethodWithArgs()
        {
            var testRequest = new TestRequest();

            var filter = Filter.Where<TestModel>(
                o => o.Name == testRequest.ValueMethodWithArgs("test", 4));

            Assert.IsTrue(filter.PropertyName == "Name");
            Assert.IsTrue(filter.Value.ToString() == "test4");
        }

        [TestMethod]
        public void TestStaticMethod()
        {
            var testRequest = new TestRequest();

            var filter = Filter.Where<TestModel>(
                o => o.Name == TestRequest.StaticMethod("5"));

            Assert.IsTrue(filter.PropertyName == "Name");
            Assert.IsTrue(filter.Value.ToString() == "test5");
        }

        [TestMethod]
        public void TestNotEqual()
        {
            var testRequest = new TestRequest();

            var filter = Filter.Where<TestModel>(
                o => o.Name != TestRequest.StaticMethod("5"));

            Assert.IsTrue(filter.PropertyName == "Name");
            Assert.IsTrue(filter.Operator == "ne");
            Assert.IsTrue(filter.Value.ToString() == "test5");
        }

        [TestMethod]
        public void TestEnumFilter()
        {
            var testRequest = new TestRequest();

            var filter = Filter.Where<TestModel>(
                o => o.TestEnumProperty == TestEnum.One);

            Assert.IsTrue(filter.PropertyName == "TestEnumProperty");
            Assert.IsTrue(filter.Operator == "eq");
            Assert.IsTrue(filter.Value.ToString() == "1");
        }
    }
}
