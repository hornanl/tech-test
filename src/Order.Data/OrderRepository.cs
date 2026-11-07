using Microsoft.EntityFrameworkCore;
using Order.Model.Exceptions;
using Order.Model.Filters;
using Order.Model.Requests;
using Order.Model.Responses;
using Order.Model.Results;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Order.Data
{
    public class OrderRepository : IOrderRepository
    {
        private readonly OrderContext _orderContext;

        public OrderRepository(OrderContext orderContext)
        {
            _orderContext = orderContext;
        }

        public async Task<IEnumerable<OrderSummary>> GetOrdersAsync(OrdersFilter filter, CancellationToken cancellationToken = default)
        {
            var query = _orderContext.Order.AsQueryable();

            // No status means return all orders. An unknown status returns an empty list.
            if (!string.IsNullOrEmpty(filter.Status))
            {
                query = query.Where(x => x.Status.Name == filter.Status);
            }

            return await GetOrderSummaries(query, cancellationToken);
        }

        private static async Task<IEnumerable<OrderSummary>> GetOrderSummaries(IQueryable<Entities.Order> query, CancellationToken cancellationToken)
        {
            // Calculate totals in the database using quantity * the current product price or cost.
            // SQL SUM skips items whose quantity is null.
            var orders = await query
                .Include(x => x.Items)
                .Include(x => x.Status)
                .Select(x => new OrderSummary
                {
                    Id = new Guid(x.Id),
                    ResellerId = new Guid(x.ResellerId),
                    CustomerId = new Guid(x.CustomerId),
                    StatusId = new Guid(x.StatusId),
                    StatusName = x.Status.Name,
                    ItemCount = x.Items.Count,
                    TotalCost = x.Items.Sum(i => i.Quantity * i.Product.UnitCost).Value,
                    TotalPrice = x.Items.Sum(i => i.Quantity * i.Product.UnitPrice).Value,
                    CreatedDate = x.CreatedDate
                })
                .OrderByDescending(x => x.CreatedDate)
                .ToListAsync(cancellationToken);

            return orders;
        }

        public async Task<OrderDetail> GetOrderByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            var orderIdBytes = orderId.ToByteArray();

            var order = await _orderContext.Order
                .Where(x => _orderContext.Database.IsInMemory() ? x.Id.SequenceEqual(orderIdBytes) : x.Id == orderIdBytes)
                .Select(x => new OrderDetail
                {
                    Id = new Guid(x.Id),
                    ResellerId = new Guid(x.ResellerId),
                    CustomerId = new Guid(x.CustomerId),
                    StatusId = new Guid(x.StatusId),
                    StatusName = x.Status.Name,
                    CreatedDate = x.CreatedDate,
                    TotalCost = x.Items.Sum(i => i.Quantity * i.Product.UnitCost).Value,
                    TotalPrice = x.Items.Sum(i => i.Quantity * i.Product.UnitPrice).Value,
                    Items = x.Items.Select(i => new OrderItem
                    {
                        Id = new Guid(i.Id),
                        OrderId = new Guid(i.OrderId),
                        ServiceId = new Guid(i.ServiceId),
                        ServiceName = i.Service.Name,
                        ProductId = new Guid(i.ProductId),
                        ProductName = i.Product.Name,
                        UnitCost = i.Product.UnitCost,
                        UnitPrice = i.Product.UnitPrice,
                        TotalCost = i.Product.UnitCost * i.Quantity.Value,
                        TotalPrice = i.Product.UnitPrice * i.Quantity.Value,
                        Quantity = i.Quantity.Value
                    })
                }).SingleOrDefaultAsync(cancellationToken);
            
            return order;
        }

        public async Task<OrderOperationResult> UpdateOrderStatusAsync(Guid orderId, string status, CancellationToken cancellationToken = default)
        {
            var orderIdBytes = orderId.ToByteArray();
            // Enable tracking so EF can detect and save the status change.
            var order = await _orderContext.Order
                .AsTracking()
                .SingleOrDefaultAsync(x => x.Id == orderIdBytes, cancellationToken);
            if (order == null)
                return OrderOperationResult.OrderNotFound;

            // We assume each status name is unique.
            // Orders can move to any existing status or stay in their current status.
            var orderStatus = await _orderContext.OrderStatus
                .SingleOrDefaultAsync(x => x.Name == status, cancellationToken);
            if (orderStatus == null)
                return OrderOperationResult.InvalidStatus;

            order.StatusId = orderStatus.Id;
            await _orderContext.SaveChangesAsync(cancellationToken);
            return OrderOperationResult.Success;
        }

        public async Task<(OrderOperationResult Result, Guid OrderId)> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
        {
            var status = await _orderContext.OrderStatus
                .SingleOrDefaultAsync(x => x.Name == "Created", cancellationToken);
            if (status == null)
                // If Created is missing, the database configuration needs fixing, not the request.
                throw new OrderDataException("The Created order status is not configured.");

            // Check that each product belongs to the requested service.
            // The database's foreign keys do not check this relationship
            foreach (var item in request.Items)
            {
                var productId = item.ProductId.Value.ToByteArray();
                var serviceId = item.ServiceId.Value.ToByteArray();
                // One query with Contains would be more efficient, but the current MySQL
                //   provider generates invalid SQL for byte[] IDs. Check each item separately for now.
                var productExists = await _orderContext.OrderProduct
                    .AnyAsync(product => product.Id == productId && product.ServiceId == serviceId, cancellationToken);

                if (!productExists)
                {
                    return (OrderOperationResult.InvalidProductOrService, Guid.Empty);
                }
            }

            // Generate the ID and creation time here. Every new order starts as Created.
            var orderId = Guid.NewGuid();
            var order = new Entities.Order
            {
                Id = orderId.ToByteArray(),
                ResellerId = request.ResellerId.Value.ToByteArray(),
                CustomerId = request.CustomerId.Value.ToByteArray(),
                StatusId = status.Id,
                CreatedDate = DateTime.UtcNow
            };

            foreach (var item in request.Items)
            {
                var productId = item.ProductId.Value.ToByteArray();
                var serviceId = item.ServiceId.Value.ToByteArray();
                order.Items.Add(new Entities.OrderItem
                {
                    Id = Guid.NewGuid().ToByteArray(),
                    OrderId = order.Id,
                    ProductId = productId,
                    ServiceId = serviceId,
                    Quantity = item.Quantity.Value
                });
            }

            _orderContext.Order.Add(order);
            await _orderContext.SaveChangesAsync(cancellationToken);
            return (OrderOperationResult.Success, orderId);
        }

        public async Task<IEnumerable<MonthlyProfit>> GetMonthlyProfitAsync(CancellationToken cancellationToken = default)
        {
            var totals = new Dictionary<(int Year, int Month), decimal>();
            // Read completed items one at a time instead of loading them all into a list.
            // Keep only the running total for each month in memory.
            var items = _orderContext.OrderItem
                // Skip missing quantities
                .Where(x => x.Order.Status.Name == "Completed" && x.Quantity.HasValue)
                .Select(x => new
                {
                    x.Order.CreatedDate,
                    Quantity = x.Quantity.Value,
                    x.Product.UnitCost,
                    x.Product.UnitPrice
                }).AsAsyncEnumerable();

            // WithCancellation passes the token to EF while it reads rows.
            await foreach (var item in items.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // There is no completion date, so use the creation date.
                // Include the year so January in different years stays separate.
                var month = (item.CreatedDate.Year, item.CreatedDate.Month);
                totals.TryGetValue(month, out var total);
                // Profit = quantity * (price - cost). Zero profit and losses are valid.
                totals[month] = total + item.Quantity * (item.UnitPrice - item.UnitCost);
            }

            // Return the newest months first. Months without matching items are omitted.
            return totals
                .Select(month => new MonthlyProfit
                {
                    Year = month.Key.Year,
                    Month = month.Key.Month,
                    Profit = month.Value
                })
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.Month)
                .ToList();
        }

    }
}
