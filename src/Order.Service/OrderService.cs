using Order.Data;
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
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _orderRepository;

        public OrderService(IOrderRepository orderRepository)
        {
            _orderRepository = orderRepository;
        }

        public async Task<IEnumerable<OrderSummary>> GetOrdersAsync(OrdersFilter filter, CancellationToken cancellationToken = default)
        {
            var orders = await _orderRepository.GetOrdersAsync(filter, cancellationToken);
            return orders;
        }

        public async Task<OrderDetail> GetOrderByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            var order = await _orderRepository.GetOrderByIdAsync(orderId, cancellationToken);
            return order;
        }

        public async Task<OrderOperationResult> UpdateOrderStatusAsync(Guid orderId, string status, CancellationToken cancellationToken = default)
        {
            return await _orderRepository.UpdateOrderStatusAsync(orderId, status, cancellationToken);
        }

        public async Task<(OrderOperationResult Result, Guid OrderId)> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
        {
            return await _orderRepository.CreateOrderAsync(request, cancellationToken);
        }

        public async Task<IEnumerable<MonthlyProfit>> GetMonthlyProfitAsync(CancellationToken cancellationToken = default)
        {
            return await _orderRepository.GetMonthlyProfitAsync(cancellationToken);
        }
    }
}
