//using Microsoft.Azure.Documents.Client;
//using FergusonSourcingCore.Models;
//using Newtonsoft.Json;
//using System;
//using Xunit;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using Microsoft.Azure.Documents;
//using FergusonSourcingDashboard;
//using FergusonSourcingCore.Helpers;

//using Microsoft.Extensions.Logging;

//using Microsoft.AspNetCore.Mvc;

//namespace FergusonSourcingDashboard.Tests
//{
//    public class SourcingDashboardUnitTests
//    {
//        private readonly ILogger logger = TestHelpers.CreateLogger();
//        private static string testOrderId = "allyourbasearebelongtous";
//        private static string manualOrdersKey = Environment.GetEnvironmentVariable("AZ_TEST_MANUAL_ORDERS_KEY");
//        private static Uri collectionUri = UriFactory.CreateDocumentCollectionUri("sourcing-engine", "test-manual-orders");
//        private static DocumentClient client = new DocumentClient(new Uri("https://ferguson-sourcing-engine.documents.azure.com:443/"), manualOrdersKey);

//        [Fact]
//        public async void Test_GetManualOrders()
//        {
//            await CreateManualOrder(testOrderId);
//            var req = TestHelpers.CreateHttpRequest();

//            var res = SourcingDashboard.GetManualOrders(req, client, logger);

//            var actionResult = res as OkObjectResult;
//            var manualOrders = actionResult.Value as IEnumerable<ManualOrder>;
//            var testOrder = manualOrders.FirstOrDefault(mo => mo.atgOrderId == testOrderId);

//            Assert.Equal(testOrderId, testOrder.id);

//            await DeleteManualOrder(testOrderId);
//        }


//        [Fact]
//        public async void Test_ClaimOrder()
//        {
//            await CreateManualOrder(testOrderId);
//            var req = TestHelpers.CreateHttpRequest("id", testOrderId);

//            await SourcingDashboard.ClaimOrder(req, client, logger, testOrderId);
//            var testOrder = GetManualOrderById(testOrderId);

//            Assert.Equal(testOrder.id, testOrderId);

//            await DeleteManualOrder(testOrderId);
//        }


//        [Fact]
//        public async void Test_IsOrdedClaimed_True()
//        {
//            await CreateManualOrder(testOrderId, true);
//            var req = TestHelpers.CreateHttpRequest("id", testOrderId);

//            var res = await SourcingDashboard.IsOrderClaimed(req, client, logger, testOrderId);

//            var actionResult = res as OkObjectResult;
//            var isOrderClaimed = (bool)actionResult.Value;

//            Assert.True(isOrderClaimed);

//            await DeleteManualOrder(testOrderId);
//        }


//        [Fact]
//        public async void Test_IsOrdedClaimed_False()
//        {
//            await CreateManualOrder(testOrderId, false);
//            var req = TestHelpers.CreateHttpRequest("id", testOrderId);

//            var res = await SourcingDashboard.IsOrderClaimed(req, client, logger, testOrderId);

//            var actionResult = res as OkObjectResult;
//            var isOrderClaimed = (bool)actionResult.Value;

//            Assert.False(isOrderClaimed);

//            await DeleteManualOrder(testOrderId);
//        }


//        [Fact]
//        public async void Test_ReleaseOrder()
//        {
//            await CreateManualOrder(testOrderId, true);
//            var req = TestHelpers.CreateHttpRequest("id", testOrderId);

//            await SourcingDashboard.ReleaseOrder(req, client, logger, testOrderId);

//            var testOrder = GetManualOrderById(testOrderId);

//            Assert.False(testOrder.claimed);

//            await DeleteManualOrder(testOrderId);
//        }


//        [Fact]
//        public async void Test_CompleteOrder()
//        {
//            await CreateManualOrder(testOrderId, false);
//            var req = TestHelpers.CreateHttpRequest("id", testOrderId);

//            await SourcingDashboard.CompleteOrder(req, client, logger, testOrderId);

//            var testOrder = GetManualOrderById(testOrderId);

//            Assert.True(testOrder.orderComplete);

//            await DeleteManualOrder(testOrderId);
//        }


//        private async Task CreateManualOrder(string orderId)
//        {
//            var manualOrder = new ManualOrder()
//            {
//                atgOrderId = orderId,
//                id = orderId
//            };

//            await client.CreateDocumentAsync(collectionUri, manualOrder);
//        }

//        private async Task CreateManualOrder(string orderId, bool isClaimed)
//        {
//            var manualOrder = new ManualOrder()
//            {
//                atgOrderId = orderId,
//                id = orderId,
//                claimed = isClaimed,
//                orderComplete= isClaimed
//            };

//            await client.CreateDocumentAsync(collectionUri, manualOrder);
//        }


//        private async Task DeleteManualOrder(string orderId)
//        {

//            var documentUri = UriFactory.CreateDocumentUri("sourcing-engine", "test-manual-orders", orderId);
//            var options = new RequestOptions()
//            {
//                PartitionKey = new PartitionKey(orderId)
//            };

//            await client.DeleteDocumentAsync(documentUri, options);
//        }


//        private static ManualOrder GetManualOrderById(string orderId)
//        {
//            var options = new FeedOptions() { EnableCrossPartitionQuery = true };

//            var matchingOrder = client.CreateDocumentQuery<ManualOrder>(collectionUri, options)
//                .AsEnumerable().FirstOrDefault(mo => mo.id == orderId);

//            return matchingOrder;
//        }


        
//    }
//}
