/*
  Migración 001 · datos de las entrevistas (audios del 2/11 y 5/11)
  ----------------------------------------------------------------
  Lleva una base creada con la versión anterior del script a la estructura actual.
  La aplicación la ejecuta sola al conectarse a una base que ya existe; cada paso
  revisa si ya se hizo, así que se puede correr las veces que haga falta.
  A mano: sqlcmd -S . -E -C -d OficinaRegistros -i sql/Migraciones/001_datos_entrevistas.sql
  (después hay que volver a correr el procedimiento sp_BuscarGeneral de OficinaRegistros.sql).
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Los montos ya tienen nombre
IF COL_LENGTH('Acta', 'SumaReclamada') IS NULL
BEGIN
    EXEC sp_rename 'Acta.Monto1', 'SumaReclamada', 'COLUMN';
    EXEC sp_rename 'Acta.Monto2', 'GastosAdministrativos', 'COLUMN';
END
IF COL_LENGTH('ActaVersion', 'SumaReclamada') IS NULL
BEGIN
    EXEC sp_rename 'ActaVersion.Monto1', 'SumaReclamada', 'COLUMN';
    EXEC sp_rename 'ActaVersion.Monto2', 'GastosAdministrativos', 'COLUMN';
END
GO

-- Dominio del acta: es uno de los datos que cambia el acta modificada
IF COL_LENGTH('Acta', 'Dominio') IS NULL ALTER TABLE Acta ADD Dominio VARCHAR(10) NULL;
IF COL_LENGTH('ActaVersion', 'Dominio') IS NULL ALTER TABLE ActaVersion ADD Dominio VARCHAR(10) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Acta_Dominio')
    CREATE INDEX IX_Acta_Dominio ON Acta(Dominio) WHERE Dominio IS NOT NULL;
GO

-- Número de repartición. Las que ya estaban no tienen número real: quedan con 900 + id para que
-- el Administrador las corrija desde Configuración → Listas.
IF COL_LENGTH('Reparticion', 'Codigo') IS NULL ALTER TABLE Reparticion ADD Codigo SMALLINT NULL;
GO
IF COLUMNPROPERTY(OBJECT_ID('Reparticion'), 'Codigo', 'AllowsNull') = 1
BEGIN
    UPDATE Reparticion SET Codigo = 900 + IdReparticion WHERE Codigo IS NULL;
    ALTER TABLE Reparticion ALTER COLUMN Codigo SMALLINT NOT NULL;
    ALTER TABLE Reparticion ADD CONSTRAINT UQ_Reparticion_Codigo UNIQUE (Codigo);
END
GO

-- Reclamos: los datos que lleva hoy la planilla
IF COL_LENGTH('Reclamo', 'Origen') IS NULL
    ALTER TABLE Reclamo ADD Origen TINYINT NULL CONSTRAINT CK_Reclamo_Origen CHECK (Origen IN (1, 2));
IF COL_LENGTH('Reclamo', 'Estado') IS NULL
BEGIN
    ALTER TABLE Reclamo ADD Estado TINYINT NOT NULL CONSTRAINT DF_Reclamo_Estado DEFAULT 1
        CONSTRAINT CK_Reclamo_Estado CHECK (Estado BETWEEN 1 AND 3);
    -- los ya verificados pasan a "Verificando"
    EXEC ('UPDATE Reclamo SET Estado = 2 WHERE Verificado = 1');
END
IF COL_LENGTH('Reclamo', 'Entrecalles') IS NULL ALTER TABLE Reclamo ADD Entrecalles NVARCHAR(150) NULL;
IF COL_LENGTH('Reclamo', 'Delegacion') IS NULL ALTER TABLE Reclamo ADD Delegacion NVARCHAR(80) NULL;
IF COL_LENGTH('Reclamo', 'Denunciante') IS NULL ALTER TABLE Reclamo ADD Denunciante NVARCHAR(120) NULL;
IF COL_LENGTH('Reclamo', 'NroActaInfraccion') IS NULL ALTER TABLE Reclamo ADD NroActaInfraccion VARCHAR(20) NULL;
IF COL_LENGTH('Reclamo', 'FechaNotificacion') IS NULL ALTER TABLE Reclamo ADD FechaNotificacion DATE NULL;
GO
