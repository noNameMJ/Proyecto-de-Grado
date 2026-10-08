--
-- PostgreSQL database dump
--

-- Dumped from database version 16.4 (Debian 16.4-1.pgdg110+2)
-- Dumped by pg_dump version 16.4 (Debian 16.4-1.pgdg110+2)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: geovisor; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA geovisor;


--
-- Name: municipio_sync_codes(); Type: FUNCTION; Schema: geovisor; Owner: -
--

CREATE FUNCTION geovisor.municipio_sync_codes() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP IN ('INSERT','UPDATE') THEN
        -- Caso 1: viene mpio_cdpmp => deriva los otros
        IF NEW.mpio_cdpmp IS NOT NULL THEN
            NEW.dpto_ccdgo := substr(NEW.mpio_cdpmp,1,2);
            NEW.mpio_ccdgo := substr(NEW.mpio_cdpmp,3,3);
        ELSE
            -- Caso 2: vienen dpto+mpio => compone mpio_cdpmp
            IF NEW.dpto_ccdgo IS NOT NULL AND NEW.mpio_ccdgo IS NOT NULL THEN
                NEW.mpio_cdpmp := NEW.dpto_ccdgo || NEW.mpio_ccdgo;
            END IF;
        END IF;
    END IF;
    RETURN NEW;
END $$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: auditoria_proyecto; Type: TABLE; Schema: geovisor; Owner: -
--

CREATE TABLE geovisor.auditoria_proyecto (
    id_auditoria integer NOT NULL,
    id_proyecto integer,
    titulo_proyecto character varying(255) NOT NULL,
    accion character varying(50) NOT NULL,
    usuario character varying(150) NOT NULL,
    equipo character varying(100),
    fecha_hora timestamp with time zone DEFAULT now(),
    detalles text
);


--
-- Name: auditoria_proyecto_id_auditoria_seq; Type: SEQUENCE; Schema: geovisor; Owner: -
--

CREATE SEQUENCE geovisor.auditoria_proyecto_id_auditoria_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: auditoria_proyecto_id_auditoria_seq; Type: SEQUENCE OWNED BY; Schema: geovisor; Owner: -
--

ALTER SEQUENCE geovisor.auditoria_proyecto_id_auditoria_seq OWNED BY geovisor.auditoria_proyecto.id_auditoria;


--
-- Name: departamento; Type: TABLE; Schema: geovisor; Owner: -
--

CREATE TABLE geovisor.departamento (
    dpto_ccdgo character(2) NOT NULL,
    dpto_cnmbr character varying(250),
    geom public.geometry(MultiPolygon,4686),
    CONSTRAINT chk_departamento_srid CHECK ((public.st_srid(geom) = 4686))
);


--
-- Name: municipio; Type: TABLE; Schema: geovisor; Owner: -
--

CREATE TABLE geovisor.municipio (
    dpto_ccdgo character(2) NOT NULL,
    mpio_cdpmp character(5) NOT NULL,
    mpio_cnmbr character varying(250),
    geom public.geometry(MultiPolygon,4686),
    CONSTRAINT chk_municipio_srid CHECK ((public.st_srid(geom) = 4686))
);


--
-- Name: COLUMN municipio.mpio_cdpmp; Type: COMMENT; Schema: geovisor; Owner: -
--

COMMENT ON COLUMN geovisor.municipio.mpio_cdpmp IS 'Código DANE 5 dígitos (dept+mpio)';


--
-- Name: proyecto; Type: TABLE; Schema: geovisor; Owner: -
--

CREATE TABLE geovisor.proyecto (
    id_proyecto integer NOT NULL,
    titulo character varying(200) NOT NULL,
    descripcion text,
    fecha_inicio date,
    palabra_clave text,
    geom public.geometry(Point,4686) NOT NULL,
    ruta_archivos text,
    entidades character varying(255),
    representante character varying(255),
    fecha_fin date,
    sistema_referencia character varying(100),
    formato_datos character varying(100),
    linaje text,
    fecha_actualizacion timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_proyecto_srid CHECK ((public.st_srid(geom) = 4686))
);


--
-- Name: TABLE proyecto; Type: COMMENT; Schema: geovisor; Owner: -
--

COMMENT ON TABLE geovisor.proyecto IS 'Proyectos georreferenciados, SRID 4686';


--
-- Name: COLUMN proyecto.sistema_referencia; Type: COMMENT; Schema: geovisor; Owner: -
--

COMMENT ON COLUMN geovisor.proyecto.sistema_referencia IS 'ISO 19115: Reference System (ej. EPSG:4326)';


--
-- Name: COLUMN proyecto.formato_datos; Type: COMMENT; Schema: geovisor; Owner: -
--

COMMENT ON COLUMN geovisor.proyecto.formato_datos IS 'ISO 19115: Distribution Format / Spatial Representation Type (ej. Vector Shapefile)';


--
-- Name: COLUMN proyecto.linaje; Type: COMMENT; Schema: geovisor; Owner: -
--

COMMENT ON COLUMN geovisor.proyecto.linaje IS 'ISO 19115: Lineage - Origen y calidad de los datos';


--
-- Name: COLUMN proyecto.fecha_actualizacion; Type: COMMENT; Schema: geovisor; Owner: -
--

COMMENT ON COLUMN geovisor.proyecto.fecha_actualizacion IS 'ISO 19115: Metadata Date Stamp';


--
-- Name: proyecto_id_proyecto_seq; Type: SEQUENCE; Schema: geovisor; Owner: -
--

CREATE SEQUENCE geovisor.proyecto_id_proyecto_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: proyecto_id_proyecto_seq; Type: SEQUENCE OWNED BY; Schema: geovisor; Owner: -
--

ALTER SEQUENCE geovisor.proyecto_id_proyecto_seq OWNED BY geovisor.proyecto.id_proyecto;


--
-- Name: proyecto_municipio; Type: TABLE; Schema: geovisor; Owner: -
--

CREATE TABLE geovisor.proyecto_municipio (
    id_proyecto integer NOT NULL,
    mpio_cdpmp character(5) NOT NULL
);


--
-- Name: auditoria_proyecto id_auditoria; Type: DEFAULT; Schema: geovisor; Owner: -
--

ALTER TABLE ONLY geovisor.auditoria_proyecto ALTER COLUMN id_auditoria SET DEFAULT nextval('geovisor.auditoria_proyecto_id_auditoria_seq'::regclass);


--
-- Name: proyecto id_proyecto; Type: DEFAULT; Schema: geovisor; Owner: -
--

ALTER TABLE ONLY geovisor.proyecto ALTER COLUMN id_proyecto SET DEFAULT nextval('geovisor.proyecto_id_proyecto_seq'::regclass);


--
-- Name: auditoria_proyecto auditoria_proyecto_pkey; Type: CONSTRAINT; Schema: geovisor; Owner: -
--

ALTER TABLE ONLY geovisor.auditoria_proyecto
    ADD CONSTRAINT auditoria_proyecto_pkey PRIMARY KEY (id_auditoria);


--
-- Name: departamento departamento_pkey; Type: CONSTRAINT; Schema: geovisor; Owner: -
--

ALTER TABLE ONLY geovisor.departamento
    ADD CONSTRAINT departamento_pkey PRIMARY KEY (dpto_ccdgo);


--
-- Name: municipio municipio_pkey; Type: CONSTRAINT; Schema: geovisor; Owner: -
--

ALTER TABLE ONLY geovisor.municipio
    ADD CONSTRAINT municipio_pkey PRIMARY KEY (mpio_cdpmp);


--
-- Name: proyecto_municipio proyecto_municipio_pkey; Type: CONSTRAINT; Schema: geovisor; Owner: -
--

ALTER TABLE ONLY geovisor.proyecto_municipio
    ADD CONSTRAINT proyecto_municipio_pkey PRIMARY KEY (id_proyecto, mpio_cdpmp);


--
-- Name: proyecto proyecto_pkey; Type: CONSTRAINT; Schema: geovisor; Owner: -
--

ALTER TABLE ONLY geovisor.proyecto
    ADD CONSTRAINT proyecto_pkey PRIMARY KEY (id_proyecto);


--
-- Name: idx_auditoria_fecha; Type: INDEX; Schema: geovisor; Owner: -
--

CREATE INDEX idx_auditoria_fecha ON geovisor.auditoria_proyecto USING btree (fecha_hora DESC);


--
-- Name: idx_auditoria_id_proyecto; Type: INDEX; Schema: geovisor; Owner: -
--

CREATE INDEX idx_auditoria_id_proyecto ON geovisor.auditoria_proyecto USING btree (id_proyecto);


--
-- Name: idx_departamento_geom; Type: INDEX; Schema: geovisor; Owner: -
--

CREATE INDEX idx_departamento_geom ON geovisor.departamento USING gist (geom);


--
-- Name: idx_departamento_nombre; Type: INDEX; Schema: geovisor; Owner: -
--

CREATE INDEX idx_departamento_nombre ON geovisor.departamento USING btree (dpto_cnmbr);


--
-- Name: idx_municipio_dpto; Type: INDEX; Schema: geovisor; Owner: -
--

CREATE INDEX idx_municipio_dpto ON geovisor.municipio USING btree (dpto_ccdgo);


--
-- Name: idx_municipio_geom; Type: INDEX; Schema: geovisor; Owner: -
--

CREATE INDEX idx_municipio_geom ON geovisor.municipio USING gist (geom);


--
-- Name: idx_municipio_nombre; Type: INDEX; Schema: geovisor; Owner: -
--

CREATE INDEX idx_municipio_nombre ON geovisor.municipio USING btree (mpio_cnmbr);


--
-- Name: idx_proj_mpio_mpio; Type: INDEX; Schema: geovisor; Owner: -
--

CREATE INDEX idx_proj_mpio_mpio ON geovisor.proyecto_municipio USING btree (mpio_cdpmp);


--
-- Name: idx_proyecto_fecha; Type: INDEX; Schema: geovisor; Owner: -
--

CREATE INDEX idx_proyecto_fecha ON geovisor.proyecto USING btree (fecha_inicio);


--
-- Name: idx_proyecto_geom; Type: INDEX; Schema: geovisor; Owner: -
--

CREATE INDEX idx_proyecto_geom ON geovisor.proyecto USING gist (geom);


--
-- Name: idx_proyecto_palabra_clave_trgm; Type: INDEX; Schema: geovisor; Owner: -
--

CREATE INDEX idx_proyecto_palabra_clave_trgm ON geovisor.proyecto USING gin (palabra_clave public.gin_trgm_ops);


--
-- Name: municipio trg_municipio_sync_codes; Type: TRIGGER; Schema: geovisor; Owner: -
--

CREATE TRIGGER trg_municipio_sync_codes BEFORE INSERT OR UPDATE ON geovisor.municipio FOR EACH ROW EXECUTE FUNCTION geovisor.municipio_sync_codes();


--
-- Name: municipio municipio_dpto_fk; Type: FK CONSTRAINT; Schema: geovisor; Owner: -
--

ALTER TABLE ONLY geovisor.municipio
    ADD CONSTRAINT municipio_dpto_fk FOREIGN KEY (dpto_ccdgo) REFERENCES geovisor.departamento(dpto_ccdgo);


--
-- Name: proyecto_municipio proyecto_municipio_id_proyecto_fkey; Type: FK CONSTRAINT; Schema: geovisor; Owner: -
--

ALTER TABLE ONLY geovisor.proyecto_municipio
    ADD CONSTRAINT proyecto_municipio_id_proyecto_fkey FOREIGN KEY (id_proyecto) REFERENCES geovisor.proyecto(id_proyecto) ON DELETE CASCADE;


--
-- Name: proyecto_municipio proyecto_municipio_mpio_cdpmp_fkey; Type: FK CONSTRAINT; Schema: geovisor; Owner: -
--

ALTER TABLE ONLY geovisor.proyecto_municipio
    ADD CONSTRAINT proyecto_municipio_mpio_cdpmp_fkey FOREIGN KEY (mpio_cdpmp) REFERENCES geovisor.municipio(mpio_cdpmp) ON DELETE RESTRICT;


--
-- PostgreSQL database dump complete
--

