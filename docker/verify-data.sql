SET NOCOUNT ON;
IF CONVERT(nvarchar(128), SERVERPROPERTY('Edition')) NOT LIKE '%Developer%'
    THROW 51000, 'Unexpected SQL Server edition.', 1;
IF DB_ID('PortalPedidosDockerDemo') IS NULL OR DB_ID('ErpSimulatorDockerDemo') IS NULL
    THROW 51001, 'Demo databases are missing.', 1;

SELECT name, state_desc FROM sys.databases
WHERE name IN ('PortalPedidosDockerDemo', 'ErpSimulatorDockerDemo') ORDER BY name;
SELECT 'PortalMigrations' AS Metric, COUNT(*) AS Total FROM PortalPedidosDockerDemo.dbo.__EFMigrationsHistory
UNION ALL SELECT 'ErpMigrations', COUNT(*) FROM ErpSimulatorDockerDemo.dbo.__EFMigrationsHistory
UNION ALL SELECT 'Users', COUNT(*) FROM PortalPedidosDockerDemo.dbo.AspNetUsers
UNION ALL SELECT 'Customers', COUNT(*) FROM PortalPedidosDockerDemo.dbo.Customers
UNION ALL SELECT 'Products', COUNT(*) FROM PortalPedidosDockerDemo.dbo.Products
UNION ALL SELECT 'Orders', COUNT(*) FROM PortalPedidosDockerDemo.dbo.SalesOrders
UNION ALL SELECT 'ConfirmedOrders', COUNT(*) FROM PortalPedidosDockerDemo.dbo.SalesOrders WHERE Status = 'Confirmed';
SELECT Id, PublicNumber, Status, ExternalOrderId FROM PortalPedidosDockerDemo.dbo.SalesOrders ORDER BY PublicNumber;
SELECT ExternalReference, Status, ExternalOrderId FROM ErpSimulatorDockerDemo.dbo.ErpOrderReceipts ORDER BY ExternalReference;
SELECT ItemCode, OnHand, Reserved FROM ErpSimulatorDockerDemo.dbo.ErpStocks WHERE ItemCode = 'P001';
