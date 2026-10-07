using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace SampleApp;

public class OrderProcessor
{
    // Hardcoded connection string with password
    private const string ConnectionString =
        "Server=prod-db.internal;Database=Orders;User Id=admin;Password=SuperSecret123!;";

    public string FindUserByName(string name)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();

        // SQL injection: user input concatenated directly into the query
        var query = "SELECT * FROM Users WHERE Name = '" + name + "'";
        using var command = new SqlCommand(query, connection);
        using var reader = command.ExecuteReader();

        return reader.Read() ? reader["Id"].ToString() : null;
    }

    // Method well over 50 lines, poor variable naming (x, y, temp), and no null checks
    // on any of the incoming parameters.
    public decimal ProcessOrder(Customer customer, Order order, Discount discount, Inventory inventory)
    {
        decimal x = 0;
        decimal y = 0;
        decimal temp = 0;

        Console.WriteLine("Processing order for " + customer.Name);

        foreach (var item in order.Items)
        {
            temp = item.UnitPrice * item.Quantity;
            x += temp;

            if (inventory.GetStock(item.ProductId) < item.Quantity)
            {
                Console.WriteLine("Insufficient stock for " + item.ProductId);
            }
            else
            {
                inventory.Reserve(item.ProductId, item.Quantity);
            }
        }

        if (discount.Type == "PERCENT")
        {
            y = x * (discount.Value / 100);
        }
        else if (discount.Type == "FIXED")
        {
            y = discount.Value;
        }
        else
        {
            y = 0;
        }

        x = x - y;

        if (x < 0)
        {
            x = 0;
        }

        decimal tax = x * 0.08m;
        x = x + tax;

        decimal shipping = 0;
        if (order.Items.Count > 0)
        {
            shipping = 5.99m;
        }
        if (x > 100)
        {
            shipping = 0;
        }
        x = x + shipping;

        order.Total = x;
        order.Status = "PROCESSED";
        order.ProcessedDate = DateTime.Now;

        Console.WriteLine("Sending confirmation email to " + customer.Email);
        var emailBody = "Dear " + customer.Name + ", your order total is " + x.ToString() + ".";
        SendEmail(customer.Email, "Order Confirmation", emailBody);

        Console.WriteLine("Order " + order.Id + " processed. Total: " + x);

        LogOrder(order);

        return x;
    }

    private void SendEmail(string to, string subject, string body)
    {
        Console.WriteLine($"Email sent to {to}: {subject}");
    }

    private void LogOrder(Order order)
    {
        Console.WriteLine($"Order logged: {order.Id}");
    }
}

public class Customer
{
    public string Name { get; set; }
    public string Email { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public decimal Total { get; set; }
    public string Status { get; set; }
    public DateTime ProcessedDate { get; set; }
}

public class OrderItem
{
    public string ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class Discount
{
    public string Type { get; set; }
    public decimal Value { get; set; }
}

public class Inventory
{
    public int GetStock(string productId) => 100;
    public void Reserve(string productId, int quantity) { }
}
