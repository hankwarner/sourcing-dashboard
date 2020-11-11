using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FergusonSourcingCore.Models;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Configuration;

namespace FergusonSourcingDashboard.Controllers
{
    public class OrderController
    {
        
        public static Document GetManualOrderById(DocumentClient cosmosClient, string id)
        {
            var manualOrdersContainerName = Environment.GetEnvironmentVariable("MANUAL_ORDERS_CONTAINER_NAME");
            var collectionUri = UriFactory.CreateDocumentCollectionUri("sourcing-engine", manualOrdersContainerName);
            var feedOption = new FeedOptions { EnableCrossPartitionQuery = true };

            var query = new SqlQuerySpec
            {
                QueryText = "SELECT VALUE o FROM o WHERE o.id = @id",
                Parameters = new SqlParameterCollection()
                {
                    new SqlParameter("@id", id)
                }
            };

            return cosmosClient.CreateDocumentQuery<Document>(collectionUri, query, feedOption)
                .AsEnumerable().FirstOrDefault();
        }


        public async static Task UnclaimOrders(List<Document> manualOrderDocs, DocumentClient document)
        {
            manualOrderDocs.ForEach(async orderDoc =>
            {
                ManualOrder manualOrder = (dynamic)orderDoc;
                manualOrder.claimed = false;

                await document.ReplaceDocumentAsync(orderDoc.SelfLink, manualOrder);
            });
        }



        /// <summary>
        ///     This function provides a safety net for missing unit prices and extended prices on manual orders. If any item on the manual
        ///     order does not have a unit price or extended price, gets the prices from the matching ATG Order and writes them on the manual order.
        /// </summary>
        /// <param name="document">CosmosDB document client for CRUD operations.</param>
        /// <param name="manualOrderDocs">Manual order documents that are incomplete and unclaimed.</param>
        /// <returns>List of manual orders that need to be worked by a rep.</returns>
        public static async Task<List<ManualOrder>> SetItemFields(DocumentClient document, IEnumerable<Document> manualOrderDocs)
        {
            var manualOrders = new List<ManualOrder>();

            foreach (var orderDoc in manualOrderDocs)
            {
                ManualOrder manualOrder = (dynamic)orderDoc;

                // if unit price is missing on the first item, it is missing from all items
                var unitPrice = manualOrder.sourcing[0].items[0].unitPrice;
                var prefShipVia = manualOrder.sourcing[0].items[0].preferredShipVia;
                var alt1Code = manualOrder.sourcing[0].items[0].alt1Code;

                var needsUpdate = string.IsNullOrEmpty(unitPrice) || string.IsNullOrEmpty(prefShipVia) || string.IsNullOrEmpty(alt1Code);

                if (needsUpdate)
                {
                    var ordersContainerName = Environment.GetEnvironmentVariable("ORDERS_CONTAINER_NAME");
                    var collectionUri = UriFactory.CreateDocumentCollectionUri("sourcing-engine", ordersContainerName);
                    var feedOption = new FeedOptions { EnableCrossPartitionQuery = true };

                    var query = new SqlQuerySpec
                    {
                        QueryText = "SELECT * FROM c WHERE c.id = @id",
                        Parameters = new SqlParameterCollection() { new SqlParameter("@id", manualOrder.id) }
                    };

                    var order = document.CreateDocumentQuery<AtgOrderRes>(collectionUri, query, feedOption)
                        .AsEnumerable().FirstOrDefault();

                    if(order != null)
                    {
                        // Set item details on the manual order
                        manualOrder.sourcing.ForEach(source =>
                        {
                            source.items.ForEach(item =>
                            {
                                var atgLine = order.items.Where(l => l.lineId == item.lineItemId).Select(l => l).FirstOrDefault();

                                // Write unit price and extended price to ManualOrder
                                item.unitPrice = atgLine.unitPrice;
                                item.extendedPrice = atgLine.extendedPrice;
                                item.preferredShipVia = atgLine.preferredShipVia;
                            });
                        });

                        await document.ReplaceDocumentAsync(orderDoc.SelfLink, manualOrder);
                    }
                }

                manualOrders.Add(manualOrder);
            }

            return manualOrders;
        }
    }
}
