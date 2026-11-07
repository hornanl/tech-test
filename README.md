# Orders API

Configure the MySQL connection using `OrderConnectionString`. From `src`, run:

```powershell
dotnet run --project Order.WebAPI
dotnet test Order.sln
```

## Endpoints

| Request | Behavior |
|---|---|
| `GET /orders?status=Failed` | Filter by status. Omitted/empty status returns all orders; unknown status returns none. |
| `GET /orders/{orderId}` | Retrieve an order, or return 404 if missing. |
| `POST /orders` | Create an order with `resellerId`, `customerId`, and `items` containing `productId`, `serviceId`, and `quantity`. Return 201 with the order and its URL. |
| `PATCH /orders/{orderId}/status` | Accept `{"status":"In Progress"}`. Return 204 on success, 404 for a missing order, or 400 for an invalid status. |
| `GET /orders/profit-by-month` | Return `{year, month, profit}` for completed orders, newest first. |

Creation requires nonempty GUIDs, at least one item, positive quantities, and
products belonging to the specified services. New orders start as `Created`.

## Assumptions

- Profit is quantity * (current product price - current product cost), grouped by
  creation month because the schema has no completion date.
- The report skips null quantities and omits months without eligible items.
- Any existing status is allowed; status names are assumed unique.
- The schema has no customer/reseller records against which to validate their IDs.

Tests cover service/repository behavior using SQLite and request-model validation
rules directly. They do not cover HTTP behavior or MySQL-specific behavior.
