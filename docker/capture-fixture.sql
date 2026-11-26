USE ErpSimulatorDockerDemo;
BEGIN TRANSACTION;
-- 500 clientes, productos y existencias exclusivos de la prueba de captura.
;WITH n AS (SELECT 1 AS num UNION ALL SELECT num + 1 FROM n WHERE num < 500)
SELECT RIGHT('0000' + CAST(num AS varchar(4)), 4) AS suffix INTO #capture FROM n
OPTION (MAXRECURSION 500);
INSERT ErpCustomers (Code, Name, Active)
SELECT 'TC' + suffix, CONCAT('Cliente captura ', suffix, IIF(suffix='0001', ' TC0500', '')), 1 FROM #capture;
INSERT ErpItems (Code, Name, UnitCode, UnitPrice, Active)
SELECT 'TP' + suffix, CONCAT('Producto captura ', suffix, IIF(suffix='0001', ' TP0500', '')), 'UND', 12.3456, 1 FROM #capture;
INSERT ErpStocks (ItemCode, WarehouseCode, OnHand, Reserved)
SELECT 'TP' + suffix, '01', 1000, 0 FROM #capture;
UPDATE ErpCatalogStates SET CurrentVersion = CurrentVersion + 1, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = 1;
COMMIT;
