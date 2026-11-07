using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Order.Service;
using Order.Model.Filters;
using Order.Model.Requests;
using Order.Model.Results;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OrderService.WebAPI.Controllers
{
    // ASP.NET Core supplies a cancellation token that is cancelled when the request is aborted.
    [ApiController]
    [Route("orders")]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public OrderController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Get([FromQuery] OrdersFilter filter, CancellationToken cancellationToken)
        {
            var orders = await _orderService.GetOrdersAsync(filter, cancellationToken);
            return Ok(orders);
        }

        [HttpGet("{orderId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetOrderById(Guid orderId, CancellationToken cancellationToken)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId, cancellationToken);
            if (order != null)
            {
                return Ok(order);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
        {
            var result = await _orderService.CreateOrderAsync(request, cancellationToken);
            // A missing product or wrong service is a bad request.
            // Any other unexpected result means something is wrong in the code.
            if (result.Result == OrderOperationResult.InvalidProductOrService)
                return BadRequest("Each product must exist and belong to the specified service.");
            if (result.Result != OrderOperationResult.Success)
                throw new InvalidOperationException($"Unexpected creation result: {result.Result}.");

            // Return the saved order.
            var order = await _orderService.GetOrderByIdAsync(result.OrderId, cancellationToken);
            return CreatedAtAction(nameof(GetOrderById), new { orderId = result.OrderId }, order);
        }

        [HttpPatch("{orderId:guid}/status")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateOrderStatus(Guid orderId, [FromBody] UpdateOrderStatusRequest request, CancellationToken cancellationToken)
        {
            var result = await _orderService.UpdateOrderStatusAsync(orderId, request.Status, cancellationToken);
            // Return 404 for a missing order, 400 for an unknown status, or 204 for success.
            if (result == OrderOperationResult.OrderNotFound)
                return NotFound();
            if (result == OrderOperationResult.InvalidStatus)
                return BadRequest("The specified order status does not exist.");
            if (result == OrderOperationResult.Success)
                return NoContent();
            throw new InvalidOperationException($"Unexpected update result: {result}.");
        }

        [HttpGet("profit-by-month")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMonthlyProfit(CancellationToken cancellationToken)
        {
            var profit = await _orderService.GetMonthlyProfitAsync(cancellationToken);
            return Ok(profit);
        }
    }
}
