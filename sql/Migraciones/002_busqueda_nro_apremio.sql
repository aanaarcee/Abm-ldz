/*
  Migración 002 · búsqueda por N° de apremio
  ------------------------------------------
  La búsqueda general (sp_BuscarGeneral, en sql/OficinaRegistros.sql) ahora también encuentra actas por su
  N° de apremio y lo muestra en el detalle. La función no cambia de tablas: se vuelve a crear sola desde el
  script principal cada vez que se aplica una migración nueva, y esta migración existe para que eso pase
  en las bases que ya estaban creadas.
  El índice sobre NroApremio ya existe (UQ_Acta_NroApremio, migración 001): no hace falta otro.
*/
-- En una base nueva las funciones se crean después de las migraciones: si todavía no existe, no se comenta
DO $$
BEGIN
    IF to_regprocedure('sp_buscargeneral(text, text, integer)') IS NOT NULL THEN
        COMMENT ON FUNCTION sp_BuscarGeneral(text, text, int) IS
            'Búsqueda general: actas (incluye N° de apremio), movimientos, reclamos y lotes de los sectores que se le pasan.';
    END IF;
END $$;
GO
