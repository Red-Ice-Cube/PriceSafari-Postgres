using Heat_Lead.Data;
using Heat_Lead.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Serialization;

namespace Heat_Lead.Services
{
    public class OrderService : IOrderService
    {
        private readonly Heat_LeadContext _context;

        public OrderService(Heat_LeadContext context)
        {
            _context = context;
        }

        public async Task FetchAndProcessOrders()
        {
            var orderIds = await _context.InterceptOrders.Select(o => o.OrderId).ToListAsync();
            foreach (var id in orderIds)
            {
                await ProcessOrder(id);
            }
        }

        public async Task ProcessOrder(string orderId)
        {
            var interceptOrders = await _context.InterceptOrders
                                             .Where(co => co.OrderId == orderId && !co.IsProcessed)
                                             .ToListAsync();

            foreach (var interceptOrder in interceptOrders)
            {
                var affiliateLinkClick = await _context.AffiliateLinkClick
                    .Include(al => al.AffiliateLink)
                    .ThenInclude(al => al.Store)
                    .FirstOrDefaultAsync(al => al.HLTT == interceptOrder.HLTT);

                if (affiliateLinkClick == null)
                {
                    interceptOrder.IsProcessed = true;
                    _context.Update(interceptOrder);
                    continue;
                }

                var affiliateLink = affiliateLinkClick.AffiliateLink;
                var affiliateLinkId = affiliateLink.AffiliateLinkId;
                var userId = affiliateLink.UserId;
                var store = affiliateLink.Store;

                using (HttpClient client = new HttpClient())
                {
                    var authenticationString = $"{store.APIkey}:";
                    var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));

                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);

                    var url = $"{store.APIurl}{orderId}";
                    var response = await client.GetStringAsync(url);

                    XmlSerializer serializer = new XmlSerializer(typeof(PrestashopOrderResponseDTO));
                    using (TextReader reader = new StringReader(response))
                    {
                        PrestashopOrderResponseDTO orderResponse = (PrestashopOrderResponseDTO)serializer.Deserialize(reader);

                        if (interceptOrder.OrderKey != orderResponse.OrderId.SecureKey)
                        {
                            continue;
                        }

                        int orderIntId = orderResponse.OrderId.Id;
                        foreach (var orderRow in orderResponse.OrderId.Associations.OrderRows.OrderRowItems)
                        {
                            var settings = await _context.Settings.FirstOrDefaultAsync();
                            string responseProductId = settings.UseEanForTracking
                                ? string.IsNullOrWhiteSpace(orderRow.ProductEan13) ? "NIEZNANY" : orderRow.ProductEan13
                                : string.IsNullOrWhiteSpace(orderRow.ProductId) ? "NIEZNANY" : orderRow.ProductId;

                            int quantity = orderRow.ProductQuantity;

                            var matchingProducts = await _context.ProductIdStores
                                .Where(pis => pis.StoreProductId == responseProductId)
                                .Select(pis => pis.Product)
                                .Where(p => p.IsActive)
                                .ToListAsync();

                            if (matchingProducts.Any())
                            {
                                foreach (var activeProduct in matchingProducts)
                                {
                                    var orderDetail = new OrderDetail
                                    {
                                        OrderId = orderIntId,
                                        ResponseProductId = responseProductId,
                                        ProductQuantity = quantity,
                                        InterceptOrderId = interceptOrder.InterceptOrderId,
                                        ProductId = activeProduct.ProductId,
                                        OrderNumber = orderResponse.OrderId.Reference,
                                        AffiliateLinkId = affiliateLinkId,
                                        UserId = userId,
                                        CampaignId = affiliateLink.CampaignId
                                    };
                                    _context.OrderDetails.Add(orderDetail);

                                    var affiliateLinksToUpdate = _context.AffiliateLink
                                        .Where(al => al.ProductId == activeProduct.ProductId && al.CampaignId == orderDetail.CampaignId)
                                        .ToList();

                                    foreach (var linkToUpdate in affiliateLinksToUpdate)
                                    {
                                        linkToUpdate.ExactSoldProductsCount += quantity;
                                    }
                                }
                            }
                            else
                            {
                                var ghostOrderDetail = new GhostOrderDetail
                                {
                                    OrderId = orderIntId,
                                    ResponseProductId = responseProductId,
                                    ProductQuantity = quantity,
                                    InterceptOrderId = interceptOrder.InterceptOrderId,
                                    OrderNumber = orderResponse.OrderId.Reference,
                                    AffiliateLinkId = affiliateLinkId,
                                    UserId = userId
                                };
                                _context.GhostOrderDetail.Add(ghostOrderDetail);
                            }
                        }
                    }
                }

                interceptOrder.IsProcessed = true;
                _context.Update(interceptOrder);
            }

            await _context.SaveChangesAsync();
        }

        public async Task CalculateOrders()
        {
            var orderDetails = await _context.OrderDetails
                .Where(od => !od.IsProcessed)
                .Include(od => od.Product)
                .ThenInclude(p => p.Category)
                .ToListAsync();

            foreach (var orderDetail in orderDetails)
            {
                var order = await _context.Order
                    .FirstOrDefaultAsync(o => o.ResponseProductId == orderDetail.ResponseProductId && o.InterceptOrderId == orderDetail.InterceptOrderId && o.OrderNumber == orderDetail.OrderNumber);

                if (order != null)
                {
                    order.Amount += orderDetail.ProductQuantity;
                    order.AffiliateCommision = order.Product.AffiliateCommission * order.Amount;
                    order.ProductPrice = order.Product.ProductPrice * order.Amount;

                    if (order.Product?.Category != null)
                    {
                        order.ValidationEndDate = DateTime.Now.AddDays(order.Product.Category.Validation);
                    }

                    if (order.ValidationEndDate.HasValue && DateTime.Now > order.ValidationEndDate)
                    {
                        order.InValidation = false;
                    }

                    _context.Update(order);
                }
                else
                {
                    order = new Order
                    {
                        ResponseProductId = orderDetail.ResponseProductId,
                        InterceptOrderId = orderDetail.InterceptOrderId,
                        Amount = orderDetail.ProductQuantity,
                        ProductId = orderDetail.ProductId,
                        OrderNumber = orderDetail.OrderNumber,
                        AffiliateCommision = orderDetail.Product.AffiliateCommission * orderDetail.ProductQuantity,
                        ProductPrice = orderDetail.Product.ProductPrice * orderDetail.ProductQuantity,
                        UserId = orderDetail.UserId,
                        AffiliateLinkId = orderDetail.AffiliateLinkId,
                        InValidation = true
                    };

                    if (orderDetail.Product?.Category != null)
                    {
                        order.ValidationEndDate = DateTime.Now.AddDays(orderDetail.Product.Category.Validation);
                        order.CategoryId = (int)orderDetail.Product.CategoryId;
                    }

                    _context.Order.Add(order);
                }

                if (order.AffiliateLinkId.HasValue)
                {
                    var affiliateLink = await _context.AffiliateLink
                        .FirstOrDefaultAsync(al => al.AffiliateLinkId == order.AffiliateLinkId.Value);

                    if (affiliateLink != null)
                    {
                        affiliateLink.SoldProductsCount += order.Amount;
                        _context.Update(affiliateLink);
                    }
                }

                orderDetail.IsProcessed = true;
                _context.Update(orderDetail);
            }

            await _context.SaveChangesAsync();
        }
    }
}
