using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq.Expressions;

namespace CloudFabric.Libraries.Search.Test
{
    public class TestModel
    {
        public string Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; }

        public int? TestNullableInt { get; set; }
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
    }
}
