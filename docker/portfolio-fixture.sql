IF DB_NAME() <> 'PortalPedidosDockerDemo' THROW 51000, 'Unexpected fixture database.', 1;
BEGIN TRAN;
DECLARE @accounts TABLE (Alias varchar(40), RoleName nvarchar(60));
INSERT @accounts VALUES ('suplente','Vendedor'),('ajeno','Vendedor'),('supervisor1','SupervisorComercial'),
    ('supervisor2','SupervisorComercial'),('admin2','Administrador');
INSERT AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
    PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumberConfirmed, TwoFactorEnabled,
    LockoutEnabled, AccessFailedCount, IsActive, CreatedAtUtc)
SELECT NEWID(), e.email, UPPER(e.email), e.email, UPPER(e.email), 1, u.PasswordHash, NEWID(), NEWID(),
    0,0,1,0,1,SYSUTCDATETIME()
FROM @accounts a CROSS APPLY (SELECT CONCAT(a.Alias,'@portal.test') email) e
JOIN AspNetUsers u ON u.Email='vendedor@portal.test';
INSERT AspNetUserRoles SELECT u.Id,r.Id FROM @accounts a
JOIN AspNetUsers u ON u.Email=CONCAT(a.Alias,'@portal.test') JOIN AspNetRoles r ON r.Name=a.RoleName;
INSERT SalesTeams(Id, Code, Name, SupervisorUserId, IsActive, CreatedAtUtc, UpdatedAtUtc)
SELECT NEWID(), a.Alias, CONCAT('Equipo ',a.Alias), u.Id, 1, SYSUTCDATETIME(), SYSUTCDATETIME()
FROM @accounts a JOIN AspNetUsers u ON u.Email=CONCAT(a.Alias,'@portal.test') WHERE a.RoleName='SupervisorComercial';
INSERT SalesTeamMembers(Id, SalesTeamId, UserId, JoinedAtUtc)
SELECT NEWID(), t.Id, u.Id, SYSUTCDATETIME() FROM AspNetUsers u JOIN SalesTeams t
ON t.Code=IIF(u.Email='ajeno@portal.test','supervisor2','supervisor1')
WHERE u.Email IN ('vendedor@portal.test','suplente@portal.test','ajeno@portal.test');
-- 23 filas estables para paginación. Los casos de autorización usan el formulario administrativo.
INSERT CustomerAssignments(Id, CustomerId, SalesTeamMemberId, AssignedByUserId, AssignedAtUtc, AssignmentReason)
SELECT NEWID(), c.Id, m.Id, admin.Id, SYSUTCDATETIME(), 'Fixture de paginación' FROM Customers c
JOIN AspNetUsers u ON u.Email='vendedor@portal.test' JOIN SalesTeamMembers m ON m.UserId=u.Id
JOIN AspNetUsers admin ON admin.Email='admin@portal.test'
WHERE c.ExternalCode BETWEEN 'TC0200' AND 'TC0222';
COMMIT;
