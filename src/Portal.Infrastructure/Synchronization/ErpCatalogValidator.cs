using System;
using System.Collections.Generic;
using Portal.Application.DTOs.Erp;
using Portal.Application.Exceptions;

namespace Portal.Infrastructure.Synchronization;

internal static class ErpCatalogValidator
{
    public static void Validate(ErpCatalogSnapshotDto snapshot)
    {
        if (snapshot.CatalogVersion <= 0)
        {
            Fail("La versión del catálogo debe ser mayor que cero.");
        }

        if (snapshot.Currency != "PEN")
        {
            Fail("El portal está configurado para trabajar con PEN.");
        }

        if (snapshot.WarehouseCode != "01")
        {
            Fail("El portal está configurado para el almacén 01.");
        }

        DateTime now = DateTime.UtcNow;

        if (snapshot.ObservedAtUtc.Kind != DateTimeKind.Utc ||
            snapshot.ObservedAtUtc < now.AddMinutes(-5) ||
            snapshot.ObservedAtUtc > now.AddMinutes(1))
        {
            Fail(
                "La fecha del catálogo debe estar en UTC y ser reciente. " +
                "Revisa también la hora de ambos equipos.");
        }

        if (snapshot.Customers is null ||
            snapshot.Items is null ||
            snapshot.Stocks is null)
        {
            Fail("Faltan colecciones en la respuesta del catálogo.");
        }

        // Una instantánea vacía desactivaría todos los registros del catálogo.
        if (snapshot.Customers!.Count == 0 ||
            snapshot.Items!.Count == 0 ||
            snapshot.Stocks!.Count == 0)
        {
            Fail(
                "El catálogo debe incluir clientes, productos y existencias.");
        }

        if (snapshot.Customers.Count > 10000 ||
            snapshot.Items.Count > 10000 ||
            snapshot.Stocks.Count > 10000)
        {
            Fail("La sincronización admite hasta 10 000 registros por colección de clientes, productos o existencias.");
        }

        var customerCodes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var customer in snapshot.Customers)
        {
            if (customer is null)
            {
                Fail("El catálogo contiene un cliente nulo.");
            }

            ValidateText(customer!.Code, 50, "Código de cliente");
            ValidateText(customer.Name, 200, "Nombre de cliente");

            if (!customerCodes.Add(customer.Code))
            {
                Fail("El catálogo contiene códigos de cliente duplicados.");
            }
        }

        var itemCodes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in snapshot.Items)
        {
            if (item is null)
            {
                Fail("El catálogo contiene un producto nulo.");
            }

            ValidateText(item!.Code, 50, "Código de producto");
            ValidateText(item.Name, 200, "Nombre de producto");
            ValidateText(item.UnitCode, 20, "Unidad de producto");

            if (!itemCodes.Add(item.Code))
            {
                Fail("El catálogo contiene códigos de producto duplicados.");
            }

            if (item.UnitPrice < 0m ||
                item.UnitPrice > 999999.9999m ||
                decimal.Round(item.UnitPrice, 4) != item.UnitPrice)
            {
                Fail(
                    "Los precios deben estar entre 0 y 999999.9999 " +
                    "y tener como máximo cuatro decimales.");
            }
        }

        var stockCodes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var stock in snapshot.Stocks)
        {
            if (stock is null)
            {
                Fail("El catálogo contiene un registro de stock nulo.");
            }

            ValidateText(stock!.ItemCode, 50, "Código de producto del stock");

            if (stock.WarehouseCode != "01")
            {
                Fail("Se recibió stock de un almacén diferente de 01.");
            }

            if (!itemCodes.Contains(stock.ItemCode))
            {
                Fail("Se recibió stock de un producto inexistente.");
            }

            if (!stockCodes.Add(stock.ItemCode))
            {
                Fail("Hay registros de stock duplicados para el almacén 01.");
            }

            if (stock.Available < 0m ||
                stock.Available > 999999999999999.9999m ||
                decimal.Round(stock.Available, 4) != stock.Available)
            {
                Fail("El stock disponible contiene un valor inválido.");
            }
        }

        if (!itemCodes.SetEquals(stockCodes))
        {
            Fail(
                "Cada producto debe tener un registro de stock " +
                "para el almacén 01.");
        }
    }

    private static void ValidateText(
        string? value,
        int maxLength,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maxLength ||
            value != value.Trim())
        {
            Fail(
                $"{field}: valor vacío, demasiado largo " +
                "o con espacios al inicio o al final.");
        }
    }

    private static void Fail(string message)
    {
        throw new ErpClientException(
            "CATALOG_INVALID",
            message);
    }
}
