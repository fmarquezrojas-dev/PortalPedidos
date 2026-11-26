-- Ejecutado al final de la suite en la copia privada con los workers detenidos.
IF DB_NAME() <> 'PortalPedidosDockerDemo' THROW 51001, 'Unexpected database', 1;
DECLARE @now datetime2(3) = SYSUTCDATETIME(), @n int = 1;
DECLARE @orders TABLE (Id uniqueidentifier);
BEGIN TRAN;
WHILE @n <= 29
BEGIN
    DECLARE @order uniqueidentifier = NEWID(), @queued datetime2(3) = DATEADD(minute, -10, @now);
    DECLARE @state varchar(20) = CASE WHEN @n <= 22 THEN 'DeadLetter'
        WHEN @n = 23 THEN 'Unknown' WHEN @n IN (24,25) THEN 'Processing'
        WHEN @n IN (26,27) THEN 'RetryWaiting' WHEN @n = 29 THEN 'Rejected' ELSE 'Pending' END;
    DECLARE @payload varchar(200) = '{"externalReference":"' + CONVERT(varchar(36), @order) + '"}';
    INSERT @orders VALUES (@order);
    IF @n IN (25,26,28) SET @queued = @now;
    INSERT SalesOrders (Id, OwnerUserId, CustomerId, CustomerCode, CustomerName, RequestedDate,
        Currency, Status, Subtotal, CatalogVersion, CreatedAtUtc, UpdatedAtUtc, Provider)
        SELECT TOP (1) @order, OwnerUserId, CustomerId, CustomerCode, CONCAT('Caso operativo ', @n),
            RequestedDate, Currency, IIF(@state='Rejected', 'Rejected', 'Queued'), Subtotal,
            CatalogVersion, @queued, @queued, Provider FROM SalesOrders ORDER BY PublicNumber;
    INSERT OutboxMessages (Id, OrderId, IdempotencyKey, Provider, SchemaVersion, PayloadJson,
        PayloadHash, Status, AttemptCount, RecoveryCycle, NextAttemptAtUtc, CreatedAtUtc,
        LeaseOwner, LeaseUntilUtc, CompletedAtUtc, LastErrorCode)
        VALUES (NEWID(), @order, @order, 'Simulator', 1, @payload, CONVERT(char(64), HASHBYTES('SHA2_256',@payload),2),
            @state, 0, 0, DATEADD(hour, IIF(@n=26,1,-1), @now), @queued,
            IIF(@state='Processing', NEWID(), NULL),
            CASE WHEN @n=24 THEN DATEADD(minute,-1,@now) WHEN @n=25 THEN DATEADD(hour,1,@now) END,
            CASE WHEN @state IN ('DeadLetter','Rejected') THEN DATEADD(minute,-2,@now) END,
            CASE WHEN @n=1 THEN 'ERP_REMOTE_PAYLOAD_MISMATCH' WHEN @state='DeadLetter' THEN 'ERP_FORBIDDEN' END);
    SET @n += 1;
END;
COMMIT;
SELECT Id FROM @orders FOR JSON PATH;
