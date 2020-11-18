using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Documents.Client;
using System.Linq;
using Microsoft.Azure.Documents;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using FergusonSourcingCore.Models;
using AzureFunctions.Extensions.Swashbuckle.Attribute;
using FergusonSourcingDashboard.Controllers;
using System.IO;
using Newtonsoft.Json;
using System.Net;

namespace FergusonSourcingDashboard
{
    public class SourcingDashboard
    {
        public static IConfiguration _config { get; set; }
        public static string errorLogsUrl = Environment.GetEnvironmentVariable("ERROR_LOGS_URL");

        public SourcingDashboard(IConfiguration config)
        {
            _config = config;
        }

        [FunctionName("GetManualOrders")]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(List<ManualOrder>))]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError, Type = typeof(StatusCodeResult))]
        public static async Task<IActionResult> GetManualOrders(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "manual-orders")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "AzureCosmosDBConnectionString"), SwaggerIgnore] DocumentClient document,
            ILogger log)
        {
            try
            {
                var manualOrdersContainerName = Environment.GetEnvironmentVariable("MANUAL_ORDERS_CONTAINER_NAME");
                var collectionUri = UriFactory.CreateDocumentCollectionUri("sourcing-engine", manualOrdersContainerName);
                var feedOption = new FeedOptions { EnableCrossPartitionQuery = true };

                var query = "SELECT VALUE o FROM o WHERE o.orderComplete = false AND o.claimed = false ORDER BY o.orderSubmitDate ASC";

                var distinctDocuments = document.CreateDocumentQuery<Document>(collectionUri, query, feedOption)
                    .ToList().Distinct();
#if DEBUG
                // Limit to last 50 orders
                distinctDocuments = distinctDocuments.Skip(Math.Max(0, distinctDocuments.Count() - 50)).Take(50);
#endif
                var manualOrders = await OrderController.SetItemFields(document, distinctDocuments);

                return new OkObjectResult(manualOrders);
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogError(ex.StackTrace);

                var title = "Exception in GetManualOrders";
                var text = $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}";
                var teamsMessage = new TeamsMessage(title, text, "red", errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
                return new StatusCodeResult(500);
            }

        }


        [FunctionName("GetManualOrder")]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(ManualOrder))]
        [ProducesResponseType((int)HttpStatusCode.NotFound, Type = typeof(NotFoundObjectResult))]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError, Type = typeof(StatusCodeResult))]
        public static IActionResult GetManualOrder(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "manual-orders/{id}")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "AzureCosmosDBConnectionString"), SwaggerIgnore] DocumentClient manualOrders,
            ILogger log,
            string id)
        {
            try
            {
                var manualOrdersContainerName = Environment.GetEnvironmentVariable("MANUAL_ORDERS_CONTAINER_NAME");
                var collectionUri = UriFactory.CreateDocumentCollectionUri("sourcing-engine", manualOrdersContainerName);
                var feedOption = new FeedOptions { EnableCrossPartitionQuery = true };

                var query = new SqlQuerySpec
                {
                    QueryText = "SELECT * FROM c WHERE c.id = @id",
                    Parameters = new SqlParameterCollection() { new SqlParameter("@id", id) }
                };

                var manualOrder = manualOrders.CreateDocumentQuery<ManualOrder>(collectionUri, query, feedOption)
                    .AsEnumerable().FirstOrDefault();

                if (manualOrder == null)
                {
                    log.LogError("Order ID not found");
                    return new NotFoundObjectResult("Order ID not found")
                    {
                        Value = $"Order ID {id} not found",
                        StatusCode = 400
                    };
                }

                return new OkObjectResult(manualOrder);
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogError(ex.StackTrace);

                var title = "Exception in GetManualOrder";
                var text = $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}";
                var teamsMessage = new TeamsMessage(title, text, "red", errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
                return new StatusCodeResult(500);
            }
        }


        [FunctionName("UpdateManualOrderNote")]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(ManualOrder))]
        [ProducesResponseType((int)HttpStatusCode.NotFound, Type = typeof(NotFoundObjectResult))]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError, Type = typeof(StatusCodeResult))]
        public static async Task<IActionResult> UpdateManualOrderNote(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "order/{id}/note"), RequestBodyType(typeof(OrderNote), "order note")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "AzureCosmosDBConnectionString"), SwaggerIgnore] DocumentClient cosmosClient,
            ILogger log,
            string id)
        {
            try
            {
                var manualOrdersContainerName = Environment.GetEnvironmentVariable("MANUAL_ORDERS_CONTAINER_NAME");
                var collectionUri = UriFactory.CreateDocumentCollectionUri("sourcing-engine", manualOrdersContainerName);
                var feedOption = new FeedOptions { EnableCrossPartitionQuery = true };

                var jsonReq = new StreamReader(req.Body).ReadToEnd();
                var reqBody = JsonConvert.DeserializeObject<OrderNote>(jsonReq);
                var note = reqBody?.note;

                if (reqBody == null || note == null)
                {
                    log.LogError("Request did not contain a JSON body with note property");
                    return new NotFoundResult();
                }

                log.LogInformation($"Order ID: {id}");
                log.LogInformation(@"Request: {jsonReq}", jsonReq);

                var query = new SqlQuerySpec
                {
                    QueryText = "SELECT * FROM c WHERE c.id = @id",
                    Parameters = new SqlParameterCollection() { new SqlParameter("@id", id) }
                };

                var manualOrderDoc = cosmosClient.CreateDocumentQuery<Document>(collectionUri, query, feedOption)
                    .AsEnumerable().FirstOrDefault();

                if (manualOrderDoc == null)
                {
                    log.LogError("Order ID not found");
                    return new NotFoundObjectResult("Order ID not found")
                    {
                        Value = $"Order ID {id} not found",
                        StatusCode = 400
                    };
                }

                ManualOrder manualOrder = (dynamic)manualOrderDoc;
                manualOrder.notes = note;

                log.LogInformation(@"Manual order: {ManualOrder}", manualOrder);
                await cosmosClient.ReplaceDocumentAsync(manualOrderDoc.SelfLink, manualOrder);

                return new OkObjectResult(manualOrder);
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogError(ex.StackTrace);

                var title = "Exception in UpdateManualOrderNote";
                var text = $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}";
                var teamsMessage = new TeamsMessage(title, text, "red", errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
                return new StatusCodeResult(500);
            }
        }


        [FunctionName("ClaimOrder")]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(ManualOrder))]
        [ProducesResponseType((int)HttpStatusCode.NotFound, Type = typeof(NotFoundObjectResult))]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError, Type = typeof(StatusCodeResult))]
        public static async Task<IActionResult> ClaimOrder(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "order/claim/{id}")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "AzureCosmosDBConnectionString"), SwaggerIgnore] DocumentClient cosmosClient,
            ILogger log,
            string id)
        {
            try
            {
                var updatedOrderData = OrderController.GetManualOrderById(cosmosClient, id);

                if (updatedOrderData == null)
                {
                    log.LogError("Order ID not found");
                    return new NotFoundObjectResult("Order ID not found")
                    {
                        Value = $"Order ID {id} not found",
                        StatusCode = 400
                    };
                }

                ManualOrder updatedOrder = (dynamic)updatedOrderData;

                updatedOrder.claimed = true;
                updatedOrder.timeClaimed = OrderController.GetCurrentEasternTime();

                await cosmosClient.ReplaceDocumentAsync(updatedOrderData.SelfLink, updatedOrder);

                return new OkObjectResult(updatedOrder);
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogError(ex.StackTrace);

                var title = "Exception in ClaimOrder";
                var text = $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}";
                var teamsMessage = new TeamsMessage(title, text, "red", errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
                return new StatusCodeResult(500);
            }
        }


        [FunctionName("IsOrderClaimed")]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(bool))]
        [ProducesResponseType((int)HttpStatusCode.NotFound, Type = typeof(NotFoundObjectResult))]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError, Type = typeof(StatusCodeResult))]
        public static async Task<IActionResult> IsOrderClaimed(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "order/is-claimed/{id}")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "AzureCosmosDBConnectionString"), SwaggerIgnore] DocumentClient cosmosClient,
            ILogger log,
            string id)
        {
            try
            {
                var updatedOrderData = OrderController.GetManualOrderById(cosmosClient, id);

                if (updatedOrderData == null)
                {
                    log.LogError("Order ID not found");
                    return new NotFoundObjectResult("Order ID not found")
                    {
                        Value = $"Order ID {id} not found",
                        StatusCode = 400
                    };
                }

                ManualOrder order = (dynamic)updatedOrderData;

                return new OkObjectResult(order.claimed || order.orderComplete);
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogError(ex.StackTrace);

                var title = "Exception in IsOrderClaimed";
                var text = $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}";
                var teamsMessage = new TeamsMessage(title, text, "red", errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
                return new StatusCodeResult(500);
            }
        }


        [FunctionName("ReleaseOrder")]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(ManualOrder))]
        [ProducesResponseType((int)HttpStatusCode.NotFound, Type = typeof(NotFoundObjectResult))]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError, Type = typeof(StatusCodeResult))]
        public static async Task<IActionResult> ReleaseOrder(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "order/release/{id}")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "AzureCosmosDBConnectionString"), SwaggerIgnore] DocumentClient cosmosClient,
            ILogger log,
            string id)
        {
            try
            {
                var updatedOrderData = OrderController.GetManualOrderById(cosmosClient, id);

                if (updatedOrderData == null)
                {
                    log.LogError("Order ID not found");
                    return new NotFoundObjectResult("Order ID not found")
                    {
                        Value = $"Order ID {id} not found",
                        StatusCode = 400
                    };
                }

                ManualOrder updatedOrder = (dynamic)updatedOrderData;

                updatedOrder.claimed = false;
                updatedOrder.timeClaimed = null;

                await cosmosClient.ReplaceDocumentAsync(updatedOrderData.SelfLink, updatedOrder);

                return new OkObjectResult(updatedOrder);
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogError(ex.StackTrace);

                var title = "Exception in ReleaseOrder";
                var text = $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}";
                var teamsMessage = new TeamsMessage(title, text, "red", errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
                return new StatusCodeResult(500);
            }
        }


        [FunctionName("CompleteOrder")]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(ManualOrder))]
        [ProducesResponseType((int)HttpStatusCode.NotFound, Type = typeof(NotFoundObjectResult))]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError, Type = typeof(StatusCodeResult))]
        public static async Task<IActionResult> CompleteOrder(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "order/complete/{id}")] HttpRequest req,
            [CosmosDB(ConnectionStringSetting = "AzureCosmosDBConnectionString"), SwaggerIgnore] DocumentClient cosmosClient,
            ILogger log,
            string id)
        {
            try
            {
                var updatedOrderData = OrderController.GetManualOrderById(cosmosClient, id);
                log.LogInformation($"ID {id}");

                if (updatedOrderData == null)
                {
                    log.LogError("Order ID not found");
                    return new NotFoundObjectResult("Order ID not found")
                    {
                        Value = $"Order ID {id} not found",
                        StatusCode = 400
                    };
                }

                ManualOrder updatedOrder = (dynamic)updatedOrderData;

                updatedOrder.orderComplete = true;
                updatedOrder.timeCompleted = OrderController.GetCurrentEasternTime();
                log.LogInformation($"Updated order is complete {updatedOrder.orderComplete}");

                await cosmosClient.ReplaceDocumentAsync(updatedOrderData.SelfLink, updatedOrder);

                return new OkObjectResult(updatedOrder);
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogError(ex.StackTrace);

                var title = "Exception in CompleteOrder";
                var text = $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}";
                var teamsMessage = new TeamsMessage(title, text, "red", errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
                return new StatusCodeResult(500);
            }
        }



        /// <summary>
        ///     Unclaims manual orders that are claimed not marked completed in the web app. Runs nightly Mon-Fri.
        /// </summary>
        [SwaggerIgnore]
        [FunctionName("ResetClaimedIncompleteOrders")]
        public async Task ResetClaimedIncompleteOrders(
            [TimerTrigger("0 0 23 * * 1-5")] TimerInfo timer, // every workday at 11pm
            [CosmosDB(ConnectionStringSetting = "AzureCosmosDBConnectionString"), SwaggerIgnore] DocumentClient document,
            ILogger log)
        {
            try
            {
                var manualOrdersContainerName = Environment.GetEnvironmentVariable("MANUAL_ORDERS_CONTAINER_NAME");
                var collectionUri = UriFactory.CreateDocumentCollectionUri("sourcing-engine", manualOrdersContainerName);
                var feedOption = new FeedOptions { EnableCrossPartitionQuery = true };

                var query = "SELECT VALUE o FROM o WHERE o.orderComplete = false AND o.claimed = true ORDER BY o.id";

                var claimedIncompleteOrderDocs = document.CreateDocumentQuery<Document>(collectionUri, query, feedOption)
                    .AsEnumerable().Distinct();
                log.LogInformation(@"Claimed, Incomplete Orders: {ClaimedIncompleteOrderDocs}", claimedIncompleteOrderDocs);

                if (claimedIncompleteOrderDocs.Count() == 0)
                {
                    log.LogInformation("No claimed uncompleted orders found.");
                    return;
                }

                await OrderController.UnclaimOrders(claimedIncompleteOrderDocs.ToList(), document);
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogError(ex.StackTrace);

                var title = "Exception in ResetClaimedIncompleteOrders";
                var text = $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}";
                var teamsMessage = new TeamsMessage(title, text, "yellow", errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
            }
        }
    }
}
