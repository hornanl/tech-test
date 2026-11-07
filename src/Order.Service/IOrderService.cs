using Order.Model.Filters;
using Order.Model.Requests;
using Order.Model.Responses;
using Order.Model.Results;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Order.Service
{
    public interface IOrderService
    {
        Task<IEnumerable<OrderSummary>> GetOrdersAsync(OrdersFilter filter, CancellationToken cancellationToken = default);
        
        Task<OrderDetail> GetOrderByIdAsync(Guid orderId, CancellationToken cancellationToken = default);

        Task<OrderOperationResult> UpdateOrderStatusAsync(Guid orderId, string status, CancellationToken cancellationToken = default);

        Task<(OrderOperationResult Result, Guid OrderId)> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);

        Task<IEnumerable<MonthlyProfit>> GetMonthlyProfitAsync(CancellationToken cancellationToken = default);
    }
}
