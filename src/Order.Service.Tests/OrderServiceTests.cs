using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NUnit.Framework;
using Order.Data;
using Order.Data.Entities;
using Order.Model.Exceptions;
using Order.Model.Filters;
using Order.Model.Requests;
using Order.Model.Results;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Order.Service.Tests
{
    public class OrderServiceTests
    {
        private IOrderService _orderService;
        private IOrderRepository _orderRepository;
        private OrderContext _orderContext;
        private DbConnection _connection;

        private readonly byte[] _orderStatusCreatedId = Guid.NewGuid().ToByteArray();
        private readonly byte[] _orderServiceEmailId = Guid.NewGuid().ToByteArray();
        private readonly byte[] _orderProductEmailId = Guid.NewGuid().ToByteArray();


        [SetUp]
        public async Task Setup()
        {
            var options = new DbContextOptionsBuilder<OrderContext>()
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .UseSqlite(CreateInMemoryDatabase())
                .EnableDetailedErrors(true)
                .EnableSensitiveDataLogging(true)
                .Options;

            _connection = RelationalOptionsExtension.Extract(options).Connection;

            _orderContext = new OrderContext(options);
            _orderContext.Database.EnsureDeleted();
            _orderContext.Database.EnsureCreated();

            _orderRepository = new OrderRepository(_orderContext);
            _orderService = new OrderService(_orderRepository);

            await AddReferenceDataAsync(_orderContext);
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            _orderContext.Dispose();
        }


        private static DbConnection CreateInMemoryDatabase()
        {
            var connection = new SqliteConnection("Filename=:memory:");
            connection.Open();

            return connection;
        }

        [Test]
        public async Task GetOrdersAsync_ReturnsOrdersWithCorrectTotals()
        {
            // Arrange
            var orderId1 = Guid.NewGuid();
            await AddOrder(orderId1, 1);

            var orderId2 = Guid.NewGuid();
            await AddOrder(orderId2, 2);

            var orderId3 = Guid.NewGuid();
            await AddOrder(orderId3, 3);

            // Act
            var orders = await _orderService.GetOrdersAsync(new OrdersFilter());

            // Assert
            Assert.AreEqual(3, orders.Count());
            var order1 = orders.SingleOrDefault(x => x.Id == orderId1);
            var order2 = orders.SingleOrDefault(x => x.Id == orderId2);
            var order3 = orders.SingleOrDefault(x => x.Id == orderId3);

            Assert.AreEqual(0.8m, order1.TotalCost);
            Assert.AreEqual(0.9m, order1.TotalPrice);

            Assert.AreEqual(1.6m, order2.TotalCost);
            Assert.AreEqual(1.8m, order2.TotalPrice);

            Assert.AreEqual(2.4m, order3.TotalCost);
            Assert.AreEqual(2.7m, order3.TotalPrice);
        }

        [Test]
        public async Task GetOrderByIdAsync_ReturnsOrderWithItemsAndTotals()
        {
            // Arrange
            var orderId1 = Guid.NewGuid();
            await AddOrder(orderId1, 2);

            // Act
            var order = await _orderService.GetOrderByIdAsync(orderId1);

            // Assert
            Assert.AreEqual(orderId1, order.Id);
            Assert.AreEqual(1, order.Items.Count());
            Assert.AreEqual(2, order.Items.Single().Quantity);
            Assert.AreEqual(new Guid(_orderProductEmailId), order.Items.Single().ProductId);
            Assert.AreEqual(1.6m, order.TotalCost);
            Assert.AreEqual(1.8m, order.TotalPrice);
        }

        [TestCase("Created")]
        [TestCase("Failed")]
        [TestCase("In Progress")]
        [TestCase("Completed")]
        public async Task UpdateOrderStatusAsync_PersistsStatusAndPreservesItems(string status)
        {
            // Arrange
            var orderId = Guid.NewGuid();
            await AddOrder(orderId, 1);

            // Act
            var result = await _orderService.UpdateOrderStatusAsync(orderId, status);

            _orderContext.ChangeTracker.Clear();

            var order = await _orderService.GetOrderByIdAsync(orderId);

            // Assert
            Assert.AreEqual(OrderOperationResult.Success, result);
            Assert.AreEqual(status, order.StatusName);
            Assert.AreEqual(1, order.Items.Single().Quantity);
        }

        [Test]
        public async Task CreateOrderAsync_PersistsOrderWithCreatedStatusAndUtcDate()
        {
            // Arrange
            var request = ValidRequest();
            var beforeCreation = DateTime.UtcNow;

            // Act
            var created = await _orderService.CreateOrderAsync(request);
            var afterCreation = DateTime.UtcNow;

            _orderContext.ChangeTracker.Clear();

            var order = await _orderService.GetOrderByIdAsync(created.OrderId);

            // Assert
            Assert.AreEqual(OrderOperationResult.Success, created.Result);
            Assert.AreEqual("Created", order.StatusName);
            Assert.AreEqual(request.CustomerId, order.CustomerId);
            Assert.AreEqual(request.ResellerId, order.ResellerId);
            Assert.AreEqual(2, order.Items.Single().Quantity);
            Assert.AreEqual(1.6m, order.TotalCost);
            Assert.AreEqual(1.8m, order.TotalPrice);
            Assert.AreNotEqual(Guid.Empty, created.OrderId);
            Assert.That(order.CreatedDate, Is.InRange(beforeCreation, afterCreation));
            Assert.AreEqual(created.OrderId, order.Items.Single().OrderId);
        }

        [Test]
        public async Task GetMonthlyProfitAsync_GroupsCompletedOrdersByYearAndMonth()
        {
            // Arrange
            await AddOrder(Guid.NewGuid(), 4, "Completed", new DateTime(2025, 1, 15));
            await AddOrder(Guid.NewGuid(), 1, "Completed", new DateTime(2024, 1, 1));
            await AddOrder(Guid.NewGuid(), 2, "Completed", new DateTime(2024, 1, 31));
            await AddOrder(Guid.NewGuid(), 3, "Completed", new DateTime(2024, 2, 1));
            await AddOrder(Guid.NewGuid(), 10, "Created", new DateTime(2024, 1, 15));

            // Act
            var profit = (await _orderService.GetMonthlyProfitAsync()).ToList();

            // Assert
            Assert.AreEqual(3, profit.Count);
            Assert.AreEqual((2025, 1, 0.4m), (profit[0].Year, profit[0].Month, profit[0].Profit));
            Assert.AreEqual((2024, 2, 0.3m), (profit[1].Year, profit[1].Month, profit[1].Profit));
            Assert.AreEqual((2024, 1, 0.3m), (profit[2].Year, profit[2].Month, profit[2].Profit));
        }

        [Test]
        public void CreateOrderAsync_ThrowsWhenCreatedStatusIsMissing()
        {
            // Arrange
            _orderContext.OrderStatus.Remove(_orderContext.OrderStatus.AsTracking().Single(status => status.Name == "Created"));
            _orderContext.SaveChanges();

            var request = ValidRequest();
            // Act
            AsyncTestDelegate createOrder = async () => await _orderService.CreateOrderAsync(request);

            // Assert
            Assert.ThrowsAsync<OrderDataException>(createOrder);
            Assert.AreEqual(0, _orderContext.Order.Count());
        }

        [Test]
        public async Task GetMonthlyProfitAsync_IgnoresNullQuantityForNonCompletedOrder()
        {
            // Arrange
            await AddOrder(Guid.NewGuid(), null);

            // Act
            var nonCompletedProfit = await _orderService.GetMonthlyProfitAsync();

            // Assert
            Assert.IsEmpty(nonCompletedProfit);
        }

        [Test]
        public async Task GetMonthlyProfitAsync_OmitsMonthWithOnlyNullQuantities()
        {
            // Arrange
            await AddOrder(Guid.NewGuid(), null, "Completed");

            // Act
            var profit = await _orderService.GetMonthlyProfitAsync();

            // Assert
            Assert.IsEmpty(profit);
        }

        [Test]
        public async Task GetMonthlyProfitAsync_IgnoresNullItemsAndKeepsValidProfit()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            await AddOrder(orderId, 3, "Completed");

            _orderContext.OrderItem.Add(new OrderItem
            {
                Id = Guid.NewGuid().ToByteArray(),
                OrderId = orderId.ToByteArray(),
                ProductId = _orderProductEmailId,
                ServiceId = _orderServiceEmailId,
                Quantity = null
            });

            await _orderContext.SaveChangesAsync();

            await AddOrder(Guid.NewGuid(), null, "Completed", new DateTime(2025, 2, 1));

            // Act
            var profit = (await _orderService.GetMonthlyProfitAsync()).ToList();

            // Assert
            Assert.AreEqual(1, profit.Count);
            Assert.AreEqual((2024, 1, 0.3m), (profit[0].Year, profit[0].Month, profit[0].Profit));
        }

        [Test]
        public async Task CreateOrderAsync_ValidatesMultipleProductsAndRepeatedItems()
        {
            // Arrange
            var request = ValidRequest();
            request.Items.Clear();

            for (var i = 0; i < 2; i++)
            {
                var productId = Guid.NewGuid();
                _orderContext.OrderProduct.Add(new OrderProduct
                {
                    Id = productId.ToByteArray(), ServiceId = _orderServiceEmailId,
                    Name = $"Product {i}", UnitCost = 1m, UnitPrice = 2m
                });
                request.Items.Add(new CreateOrderItemRequest
                {
                    ProductId = productId, ServiceId = new Guid(_orderServiceEmailId), Quantity = 1
                });
            }
            request.Items.Add(request.Items[0]);

            await _orderContext.SaveChangesAsync();

            // Act
            var result = await _orderService.CreateOrderAsync(request);
            _orderContext.ChangeTracker.Clear();
            var order = await _orderService.GetOrderByIdAsync(result.OrderId);

            // Assert
            Assert.AreEqual(OrderOperationResult.Success, result.Result);
            Assert.AreEqual(3, order.Items.Count());
            Assert.AreEqual(3, order.Items.Select(item => item.Id).Distinct().Count());
            Assert.AreEqual(2, order.Items.Count(item => item.ProductId == request.Items[0].ProductId));
            Assert.AreEqual(3m, order.TotalCost);
            Assert.AreEqual(6m, order.TotalPrice);
        }

        [TestCase(null)]
        [TestCase("")]
        public async Task GetOrdersAsync_WithoutStatus_ReturnsAllOrders(string status)
        {
            // Arrange
            await AddOrder(Guid.NewGuid(), 1);
            await AddOrder(Guid.NewGuid(), 2);

            // Act
            var orders = await _orderService.GetOrdersAsync(new OrdersFilter { Status = status });

            // Assert
            Assert.AreEqual(2, orders.Count());
        }

        [Test]
        public async Task GetOrdersAsync_ReturnsEmptyForEmptyDatabase()
        {
            // Arrange
            var filter = new OrdersFilter();

            // Act
            var orders = await _orderService.GetOrdersAsync(filter);

            // Assert
            Assert.IsEmpty(orders);
        }

        [Test]
        public async Task GetOrderByIdAsync_ReturnsNullForMissingOrder()
        {
            // Arrange
            var orderId = Guid.NewGuid();

            // Act
            var order = await _orderService.GetOrderByIdAsync(orderId);

            // Assert
            Assert.IsNull(order);
        }

        [Test]
        public async Task GetOrdersAsync_ReturnsNewestOrdersFirst()
        {
            // Arrange
            var older = Guid.NewGuid();
            await AddOrder(older, 1, createdDate: new DateTime(2024, 1, 1));

            var newer = Guid.NewGuid();
            await AddOrder(newer, 1, createdDate: new DateTime(2025, 1, 1));

            // Act
            var orders = (await _orderService.GetOrdersAsync(new OrdersFilter())).ToList();

            // Assert
            CollectionAssert.AreEqual(new[] { newer, older }, orders.Select(order => order.Id));
        }

        [TestCase("Completed")]
        [TestCase("Created")]
        [TestCase("Failed")]
        [TestCase("In Progress")]
        [TestCase("Unknown")]
        public async Task GetOrdersAsync_FiltersEachSupportedStatus(string status)
        {
            // Arrange
            foreach (var name in new[] { "Created", "Failed", "In Progress", "Completed" })
                await AddOrder(Guid.NewGuid(), 1, name);

            // Act
            var orders = (await _orderService.GetOrdersAsync(new OrdersFilter { Status = status })).ToList();

            // Assert
            Assert.AreEqual(status == "Unknown" ? 0 : 1, orders.Count);
            Assert.IsTrue(orders.All(order => order.StatusName == status));
        }

        [Test]
        public async Task UpdateOrderStatusAsync_RejectsUnknownStatusWithoutChangingOrder()
        {
            // Arrange
            var id = Guid.NewGuid();
            await AddOrder(id, 1);

            // Act
            var result = await _orderService.UpdateOrderStatusAsync(id, "Unknown");

            _orderContext.ChangeTracker.Clear();

            var order = await _orderService.GetOrderByIdAsync(id);

            // Assert
            Assert.AreEqual(OrderOperationResult.InvalidStatus, result);
            Assert.AreEqual("Created", order.StatusName);
        }

        [Test]
        public async Task UpdateOrderStatusAsync_ReturnsNotFoundForMissingOrder()
        {
            // Arrange
            var orderId = Guid.NewGuid();

            // Act
            var result = await _orderService.UpdateOrderStatusAsync(orderId, "Created");

            // Assert
            Assert.AreEqual(OrderOperationResult.OrderNotFound, result);
            Assert.AreEqual(0, await _orderContext.Order.CountAsync());
        }

        [TestCase("missing-product")]
        [TestCase("missing-service")]
        [TestCase("wrong-existing-service")]
        public async Task CreateOrderAsync_RejectsInvalidReferencesWithoutPartialWrites(string scenario)
        {
            // Arrange
            var request = ValidRequest();
            var invalidItem = new CreateOrderItemRequest
            {
                ProductId = new Guid(_orderProductEmailId),
                ServiceId = new Guid(_orderServiceEmailId),
                Quantity = 1
            };

            if (scenario == "missing-product")
                invalidItem.ProductId = Guid.NewGuid();
            else
                invalidItem.ServiceId = Guid.NewGuid();
            if (scenario == "wrong-existing-service")
                _orderContext.OrderService.Add(new Data.Entities.OrderService
                {
                    Id = invalidItem.ServiceId.Value.ToByteArray(), Name = "Other service"
                });

            await _orderContext.SaveChangesAsync();

            request.Items.Add(invalidItem);

            // Act
            var result = await _orderService.CreateOrderAsync(request);

            // Assert
            Assert.AreEqual(OrderOperationResult.InvalidProductOrService, result.Result);
            Assert.AreEqual(Guid.Empty, result.OrderId);
            Assert.AreEqual(0, await _orderContext.Order.CountAsync());
            Assert.AreEqual(0, await _orderContext.OrderItem.CountAsync());
        }

        [TestCase("missing-reseller", "ResellerId")]
        [TestCase("empty-reseller", "ResellerId")]
        [TestCase("missing-customer", "CustomerId")]
        [TestCase("empty-customer", "CustomerId")]
        [TestCase("missing-items", "Items")]
        [TestCase("empty-items", "Items")]
        [TestCase("null-item", "Items")]
        public void CreateOrderRequest_RejectsInvalidFields(string scenario, string member)
        {
            // Arrange
            var request = ValidRequest();
            switch (scenario)
            {
                case "missing-reseller": request.ResellerId = null; break;
                case "empty-reseller": request.ResellerId = Guid.Empty; break;
                case "missing-customer": request.CustomerId = null; break;
                case "empty-customer": request.CustomerId = Guid.Empty; break;
                case "missing-items": request.Items = null; break;
                case "empty-items": request.Items.Clear(); break;
                case "null-item": request.Items.Add(null); break;
            }

            // Act
            var errors = Validate(request);

            // Assert
            AssertInvalid(errors, member);
        }

        [TestCase("missing-product", "ProductId")]
        [TestCase("empty-product", "ProductId")]
        [TestCase("missing-service", "ServiceId")]
        [TestCase("empty-service", "ServiceId")]
        [TestCase("missing-quantity", "Quantity")]
        [TestCase("zero-quantity", "Quantity")]
        [TestCase("negative-quantity", "Quantity")]
        public void CreateOrderItemRequest_RejectsInvalidFields(string scenario, string member)
        {
            // Arrange
            var item = ValidRequest().Items.Single();
            switch (scenario)
            {
                case "missing-product": item.ProductId = null; break;
                case "empty-product": item.ProductId = Guid.Empty; break;
                case "missing-service": item.ServiceId = null; break;
                case "empty-service": item.ServiceId = Guid.Empty; break;
                case "missing-quantity": item.Quantity = null; break;
                case "zero-quantity": item.Quantity = 0; break;
                case "negative-quantity": item.Quantity = -1; break;
            }

            // Act
            var errors = Validate(item);

            // Assert
            AssertInvalid(errors, member);
        }

        [TestCase(1)]
        [TestCase(int.MaxValue)]
        public void CreateOrderRequest_AcceptsValidQuantityBoundaries(int quantity)
        {
            // Arrange
            var request = ValidRequest();
            request.Items[0].Quantity = quantity;
            var item = request.Items.Single();

            // Act
            var requestErrors = Validate(request);
            var itemErrors = Validate(item);

            // Assert
            Assert.IsEmpty(requestErrors);
            Assert.IsEmpty(itemErrors);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("123456789012345678901")]
        public void UpdateOrderStatusRequest_RejectsInvalidStatus(string status)
        {
            // Arrange
            var request = new UpdateOrderStatusRequest { Status = status };

            // Act
            var errors = Validate(request);

            // Assert
            AssertInvalid(errors, "Status");
        }

        [TestCase("Completed")]
        [TestCase("Created")]
        [TestCase("Failed")]
        [TestCase("In Progress")]
        public void UpdateOrderStatusRequest_AcceptsSupportedStatusNames(string status)
        {
            // Arrange
            var request = new UpdateOrderStatusRequest { Status = status };

            // Act
            var errors = Validate(request);

            // Assert
            Assert.IsEmpty(errors);
        }

        [Test]
        public async Task GetMonthlyProfitAsync_ReturnsEmptyForEmptyDatabase()
        {
            // Arrange
            // The shared setup creates an empty orders database.

            // Act
            var profit = await _orderService.GetMonthlyProfitAsync();

            // Assert
            Assert.IsEmpty(profit);
        }

        [TestCase("Created")]
        [TestCase("Failed")]
        [TestCase("In Progress")]
        public async Task GetMonthlyProfitAsync_ExcludesNonCompletedStatuses(string status)
        {
            // Arrange
            await AddOrder(Guid.NewGuid(), 3, status);

            // Act
            var profit = await _orderService.GetMonthlyProfitAsync();

            // Assert
            Assert.IsEmpty(profit);
        }

        [TestCase(0.8, 0.8, 0)]
        [TestCase(0.9, 0.8, -0.3)]
        public async Task GetMonthlyProfitAsync_PreservesZeroProfitAndLosses(double cost, double price, double expected)
        {
            // Arrange
            var product = await _orderContext.OrderProduct.AsTracking().SingleAsync();
            product.UnitCost = (decimal)cost;
            product.UnitPrice = (decimal)price;

            await _orderContext.SaveChangesAsync();

            await AddOrder(Guid.NewGuid(), 3, "Completed");

            // Act
            var profit = await _orderService.GetMonthlyProfitAsync();

            // Assert
            Assert.AreEqual((decimal)expected, profit.Single().Profit);
        }

        [Test]
        public async Task GetMonthlyProfitAsync_SumsDifferentProductsAndQuantitiesInOneOrder()
        {
            // Arrange
            var secondProductId = Guid.NewGuid();
            _orderContext.OrderProduct.Add(new OrderProduct
            {
                Id = secondProductId.ToByteArray(),
                ServiceId = _orderServiceEmailId,
                Name = "Second mailbox",
                UnitCost = 1.25m,
                UnitPrice = 1.75m
            });

            await _orderContext.SaveChangesAsync();

            var request = ValidRequest();
            request.Items.Add(new CreateOrderItemRequest
            {
                ProductId = secondProductId,
                ServiceId = new Guid(_orderServiceEmailId),
                Quantity = 3
            });

            var result = await _orderService.CreateOrderAsync(request);
            await _orderService.UpdateOrderStatusAsync(result.OrderId, "Completed");

            _orderContext.ChangeTracker.Clear();

            // Act
            var profit = await _orderService.GetMonthlyProfitAsync();

            // Assert
            Assert.AreEqual(1.7m, profit.Single().Profit);
        }

        [TestCase("list")]
        [TestCase("detail")]
        [TestCase("update")]
        [TestCase("create")]
        [TestCase("profit")]
        public async Task OrderOperations_PropagateCancellationWithoutChangingStoredOrders(string operation)
        {
            // Arrange
            var orderId = Guid.NewGuid();
            await AddOrder(orderId, 1, "Completed");
            var request = ValidRequest();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var token = cancellation.Token;

            // Act
            AsyncTestDelegate execute = async () =>
            {
                switch (operation)
                {
                    case "list": await _orderService.GetOrdersAsync(new OrdersFilter(), token); break;
                    case "detail": await _orderService.GetOrderByIdAsync(orderId, token); break;
                    case "update": await _orderService.UpdateOrderStatusAsync(orderId, "Failed", token); break;
                    case "create": await _orderService.CreateOrderAsync(request, token); break;
                    case "profit": await _orderService.GetMonthlyProfitAsync(token); break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation));
                }
            };

            // Assert
            Assert.CatchAsync<OperationCanceledException>(execute);
            _orderContext.ChangeTracker.Clear();
            Assert.AreEqual(1, await _orderContext.Order.CountAsync());
            Assert.AreEqual(1, await _orderContext.OrderItem.CountAsync());
            Assert.AreEqual("Completed", (await _orderService.GetOrderByIdAsync(orderId)).StatusName);
        }

        private CreateOrderRequest ValidRequest()
        {
            return new CreateOrderRequest
            {
                ResellerId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Items = new List<CreateOrderItemRequest>
                {
                    new CreateOrderItemRequest
                    {
                        ProductId = new Guid(_orderProductEmailId),
                        ServiceId = new Guid(_orderServiceEmailId),
                        Quantity = 2
                    }
                }
            };
        }

        private static List<ValidationResult> Validate(object request)
        {
            var errors = new List<ValidationResult>();
            Validator.TryValidateObject(request, new ValidationContext(request), errors, true);
            return errors;
        }

        private static void AssertInvalid(List<ValidationResult> errors, string member)
        {
            Assert.IsNotEmpty(errors);
            Assert.IsTrue(errors.Any(error => error.MemberNames.Contains(member)));
        }

        private async Task AddOrder(Guid orderId, int? quantity, string status = "Created", DateTime? createdDate = null)
        {
            var orderIdBytes = orderId.ToByteArray();
            var statusId = (await _orderContext.OrderStatus.SingleAsync(value => value.Name == status)).Id;
            _orderContext.Order.Add(new Data.Entities.Order
            {
                Id = orderIdBytes,
                ResellerId = Guid.NewGuid().ToByteArray(),
                CustomerId = Guid.NewGuid().ToByteArray(),
                CreatedDate = createdDate ?? new DateTime(2024, 1, 15),
                StatusId = statusId,
            });

            _orderContext.OrderItem.Add(new OrderItem
            {
                Id = Guid.NewGuid().ToByteArray(),
                OrderId = orderIdBytes,
                ServiceId = _orderServiceEmailId,
                ProductId = _orderProductEmailId,
                Quantity = quantity
            });

            await _orderContext.SaveChangesAsync();
        }

        private async Task AddReferenceDataAsync(OrderContext orderContext)
        {
            orderContext.OrderStatus.Add(new OrderStatus
            {
                Id = _orderStatusCreatedId,
                Name = "Created",
            });
            foreach (var name in new[] { "Failed", "In Progress", "Completed" })
                orderContext.OrderStatus.Add(new OrderStatus { Id = Guid.NewGuid().ToByteArray(), Name = name });

            orderContext.OrderService.Add(new Data.Entities.OrderService
            {
                Id = _orderServiceEmailId,
                Name = "Email"
            });

            orderContext.OrderProduct.Add(new OrderProduct
            {
                Id = _orderProductEmailId,
                Name = "100GB Mailbox",
                UnitCost = 0.8m,
                UnitPrice = 0.9m,
                ServiceId = _orderServiceEmailId
            });

            await orderContext.SaveChangesAsync();
        }
    }
}
