/*
  Migración 001 · respuestas de la entrevista (PDF)
  -------------------------------------------------
  Reconstruida el 20/11/2026 a partir de la estructura del servidor superjarvis, donde se aplicó el 10/11/2026.
  El archivo original se perdió antes de llegar a git. Este deja la base igual que la del servidor.
  Los números entre paréntesis (P12, P20…) son las preguntas del PDF de donde sale cada regla.

  El nombre del archivo no se cambia: el servidor ya la tiene anotada en EsquemaMigracion con este nombre.
  Cada paso revisa si ya está hecho, así que también se puede correr sobre una base que ya la tenga.
*/

-- Para la restricción "un remito, una sola acta" (EXCLUDE con = y <> sobre columnas comunes)
CREATE EXTENSION IF NOT EXISTS btree_gist;
GO

/* ---------- Acta: archivo en lote y número de Apremio ---------- */

ALTER TABLE Acta ADD COLUMN IF NOT EXISTS IdLote INT NULL REFERENCES Lote(IdLote);
ALTER TABLE Acta ADD COLUMN IF NOT EXISTS MotivoArchivo SMALLINT NULL;
ALTER TABLE Acta ADD COLUMN IF NOT EXISTS FechaArchivo DATE NULL;
ALTER TABLE Acta ADD COLUMN IF NOT EXISTS NroApremio VARCHAR(20) NULL;
GO

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_acta_motivoarchivo') THEN
        ALTER TABLE Acta ADD CONSTRAINT CK_Acta_MotivoArchivo CHECK (MotivoArchivo IN (1, 2));
    END IF;
    -- archivada = tiene lote, motivo y fecha; si no, no tiene ninguno de los tres
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_acta_archivo') THEN
        ALTER TABLE Acta ADD CONSTRAINT CK_Acta_Archivo CHECK (
            (IdLote IS NULL AND MotivoArchivo IS NULL AND FechaArchivo IS NULL)
            OR (IdLote IS NOT NULL AND MotivoArchivo IS NOT NULL AND FechaArchivo IS NOT NULL));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_acta_montos') THEN
        ALTER TABLE Acta ADD CONSTRAINT CK_Acta_Montos CHECK (COALESCE(SumaReclamada, 0) >= 0 AND COALESCE(GastosAdministrativos, 0) >= 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_actaversion_montos') THEN
        ALTER TABLE ActaVersion ADD CONSTRAINT CK_ActaVersion_Montos CHECK (COALESCE(SumaReclamada, 0) >= 0 AND COALESCE(GastosAdministrativos, 0) >= 0);
    END IF;
END $$;
GO

CREATE INDEX IF NOT EXISTS IX_Acta_Lote ON Acta(IdLote) WHERE IdLote IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS UQ_Acta_NroApremio ON Acta(NroApremio) WHERE NroApremio IS NOT NULL;
GO

/* ---------- Movimiento: un remito pertenece a una sola acta ---------- */

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_movimiento_remitounaacta') THEN
        ALTER TABLE Movimiento ADD CONSTRAINT EX_Movimiento_RemitoUnaActa
            EXCLUDE USING gist (Remito WITH =, IdActa WITH <>) WHERE (Remito IS NOT NULL AND NOT Eliminado);
    END IF;
END $$;
GO

CREATE INDEX IF NOT EXISTS IX_Movimiento_Sector_Fecha ON Movimiento(IdSector, Fecha) WHERE NOT Eliminado;
GO

/* ---------- Persona: documento solo con números ---------- */

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_persona_documento') THEN
        ALTER TABLE Persona ADD CONSTRAINT CK_Persona_Documento CHECK (
            NroDocumento IS NULL
            OR (TipoDocumento = 'DNI'  AND NroDocumento ~ '^[0-9]{7,8}$')
            OR (TipoDocumento = 'CUIT' AND NroDocumento ~ '^[0-9]{11}$'));
    END IF;
END $$;
GO

/* ---------- Reclamo: categoría y verificación completa ---------- */

ALTER TABLE Reclamo ADD COLUMN IF NOT EXISTS Categoria SMALLINT NULL;
GO

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_reclamo_categoria') THEN
        ALTER TABLE Reclamo ADD CONSTRAINT CK_Reclamo_Categoria CHECK (Categoria BETWEEN 1 AND 3);
    END IF;
    -- verificado = alguien fue: tiene que tener la fecha y el inspector
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_reclamo_verificacion') THEN
        ALTER TABLE Reclamo ADD CONSTRAINT CK_Reclamo_Verificacion CHECK (
            NOT Verificado OR (FechaVerificacion IS NOT NULL AND VerificadoPor IS NOT NULL));
    END IF;
END $$;
GO

/* ---------- Qué significa cada dato (respuestas del PDF) ---------- */

COMMENT ON COLUMN Persona.Apellido IS 'Apellido, o razón social si es una empresa (P34).';
COMMENT ON COLUMN Persona.NroDocumento IS 'Solo números, sin puntos ni guiones: DNI de 7 u 8, CUIT de 11 (P36).';
COMMENT ON COLUMN Acta.SumaReclamada IS 'Monto 1: suma reclamada, lo que se estima pagar (P22).';
COMMENT ON COLUMN Acta.GastosAdministrativos IS 'Monto 2: gastos administrativos (P22).';
COMMENT ON COLUMN Acta.IdLote IS 'Lote en el que se archivó el acta. Con lote, el acta está terminada (P12, P13).';
COMMENT ON COLUMN Acta.MotivoArchivo IS '1 Sentencia definitiva, 2 Pagada. No hay otra forma de terminar (P12).';
COMMENT ON COLUMN Acta.NroApremio IS 'Número propio que le da Apremio al acta (P29).';
COMMENT ON COLUMN Movimiento.Detalle IS 'Actividad registrada sobre el acta. Texto libre: no existe una lista fija de movimientos (P9).';
COMMENT ON COLUMN Reclamo.Vehiculo IS 'Datos del auto que se encontró al verificar: marca, color, patente (P21).';
COMMENT ON COLUMN Reclamo.Categoria IS '1 Tránsito, 2 Demanda, 3 Municipales (P20).';
GO
