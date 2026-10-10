IF DB_ID(N'PMP_SupermarketDB') IS NULL
BEGIN
    CREATE DATABASE [PMP_SupermarketDB];
END;
GO

USE [PMP_SupermarketDB];
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        UserId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        Username NVARCHAR(50) NOT NULL,
        PasswordHash NVARCHAR(256) NOT NULL,
        Role NVARCHAR(20) NOT NULL CONSTRAINT DF_Users_Role DEFAULT (N'User'),
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_Users_CreatedDate DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_Users_Role CHECK (Role IN (N'Admin', N'User'))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_Username' AND object_id = OBJECT_ID(N'dbo.Users'))
BEGIN
    CREATE UNIQUE INDEX UX_Users_Username ON dbo.Users (Username);
END;
GO

IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Products
    (
        ProductId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
        ProductName NVARCHAR(150) NOT NULL,
        Category NVARCHAR(50) NOT NULL,
        Price DECIMAL(18,2) NOT NULL CONSTRAINT CK_Products_Price_Positive CHECK (Price > 0),
        Quantity DECIMAL(18,3) NOT NULL CONSTRAINT CK_Products_Quantity_NonNegative CHECK (Quantity >= 0),
        QuantityDescription NVARCHAR(50) NULL,
        Unit NVARCHAR(20) NOT NULL,
        ProductImage NVARCHAR(500) NULL,
        CreatedDate DATETIME2(0) NOT NULL CONSTRAINT DF_Products_CreatedDate DEFAULT (SYSUTCDATETIME())
    );
END;
GO

IF COL_LENGTH(N'dbo.Products', N'QuantityDescription') IS NULL
BEGIN
    ALTER TABLE dbo.Products ADD QuantityDescription NVARCHAR(50) NULL;
END;
GO

IF COL_LENGTH(N'dbo.Products', N'ProductImage') IS NULL
BEGIN
    ALTER TABLE dbo.Products ADD ProductImage NVARCHAR(500) NULL;
END;
GO

IF COL_LENGTH(N'dbo.Products', N'ProductPhoto') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Products ALTER COLUMN ProductPhoto VARBINARY(MAX) NULL;
END;
GO

IF COL_LENGTH(N'dbo.Products', N'PhotoContentType') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Products ALTER COLUMN PhotoContentType VARCHAR(20) NULL;
END;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name IN (N'CK_Products_Quantity', N'CK_Products_Quantity_Positive') AND parent_object_id = OBJECT_ID(N'dbo.Products'))
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Products_Quantity' AND parent_object_id = OBJECT_ID(N'dbo.Products'))
    BEGIN
        ALTER TABLE dbo.Products DROP CONSTRAINT CK_Products_Quantity;
    END;
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Products_Quantity_Positive' AND parent_object_id = OBJECT_ID(N'dbo.Products'))
    BEGIN
        ALTER TABLE dbo.Products DROP CONSTRAINT CK_Products_Quantity_Positive;
    END;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Products_Quantity_NonNegative' AND parent_object_id = OBJECT_ID(N'dbo.Products'))
BEGIN
    ALTER TABLE dbo.Products ADD CONSTRAINT CK_Products_Quantity_NonNegative CHECK (Quantity >= 0);
END;
GO

IF OBJECT_ID(N'dbo.CartItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CartItems
    (
        CartItemId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CartItems PRIMARY KEY,
        UserId INT NOT NULL,
        ProductId INT NOT NULL,
        NeededQuantity DECIMAL(10,2) NOT NULL CONSTRAINT CK_CartItems_NeededQuantity_Positive CHECK (NeededQuantity > 0),
        Unit NVARCHAR(20) NOT NULL,
        CreatedDate DATETIME NOT NULL CONSTRAINT DF_CartItems_CreatedDate DEFAULT (GETDATE()),
        CONSTRAINT FK_CartItems_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_CartItems_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products(ProductId)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_CartItems_User_Product' AND object_id = OBJECT_ID(N'dbo.CartItems'))
BEGIN
    CREATE UNIQUE INDEX UX_CartItems_User_Product ON dbo.CartItems (UserId, ProductId);
END;
GO

IF OBJECT_ID(N'dbo.SalesLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SalesLog
    (
        SalesLogId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesLog PRIMARY KEY,
        ProductId INT NOT NULL,
        SaleDate DATE NOT NULL,
        Amount DECIMAL(18,2) NOT NULL CONSTRAINT CK_SalesLog_Amount_NonNegative CHECK (Amount >= 0),
        Quantity DECIMAL(18,3) NOT NULL CONSTRAINT CK_SalesLog_Quantity_Positive CHECK (Quantity > 0),
        CreatedTime DATETIME2(0) NOT NULL CONSTRAINT DF_SalesLog_CreatedTime DEFAULT (SYSDATETIME())
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SalesLog_SaleDate_ProductId' AND object_id = OBJECT_ID(N'dbo.SalesLog'))
BEGIN
    CREATE INDEX IX_SalesLog_SaleDate_ProductId ON dbo.SalesLog (SaleDate, ProductId) INCLUDE (Amount, Quantity);
END;
GO
